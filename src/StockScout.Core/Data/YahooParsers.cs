using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StockScout.Core.Models;

namespace StockScout.Core.Data;

/// <summary>Pure parsing functions, kept separate from HTTP so they can be unit tested with fixtures.</summary>
public static partial class YahooParsers
{
    /// <summary>Parses the v8/finance/chart JSON response. Bars with missing values (holidays, halts) are skipped.</summary>
    public static PriceHistory? ParseChart(string ticker, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var chart = doc.RootElement.GetProperty("chart");
        if (chart.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null)
            return null;

        var results = chart.GetProperty("result");
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            return null;

        var result = results[0];
        var meta = result.GetProperty("meta");
        var currency = meta.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "";
        var tzOffset = meta.TryGetProperty("gmtoffset", out var g) ? g.GetInt32() : 0;

        if (!result.TryGetProperty("timestamp", out var timestamps))
            return null;

        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var open = quote.GetProperty("open");
        var high = quote.GetProperty("high");
        var low = quote.GetProperty("low");
        var close = quote.GetProperty("close");
        var volume = quote.GetProperty("volume");

        var bars = new List<PriceBar>(timestamps.GetArrayLength());
        for (int i = 0; i < timestamps.GetArrayLength(); i++)
        {
            if (close[i].ValueKind != JsonValueKind.Number) continue;

            // Shift by the exchange's UTC offset so the bar lands on the local trading date.
            var date = DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64() + tzOffset).UtcDateTime);
            double cl = close[i].GetDouble();
            bars.Add(new PriceBar(
                date,
                NumberOr(open[i], cl),
                NumberOr(high[i], cl),
                NumberOr(low[i], cl),
                cl,
                volume[i].ValueKind == JsonValueKind.Number ? volume[i].GetInt64() : 0));
        }

        // Yahoo sometimes returns the live intraday bar twice; keep the last bar for each date.
        bars = bars.GroupBy(b => b.Date).Select(grp => grp.Last()).OrderBy(b => b.Date).ToList();
        return bars.Count == 0 ? null : new PriceHistory(ticker, currency, bars);
    }

    private static double NumberOr(JsonElement e, double fallback) =>
        e.ValueKind == JsonValueKind.Number ? e.GetDouble() : fallback;

    /// <summary>Parses a Yahoo Finance headline RSS feed.</summary>
    public static IReadOnlyList<NewsItem> ParseRss(string ticker, string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("item")
            .Select(item =>
            {
                var published = DateTimeOffset.TryParse(
                    (string?)item.Element("pubDate"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var p) ? p : DateTimeOffset.MinValue;
                return new NewsItem(
                    ticker,
                    CleanText((string?)item.Element("title")),
                    CleanText((string?)item.Element("description")),
                    ((string?)item.Element("link") ?? "").Trim(),
                    published);
            })
            .Where(n => n.Title.Length > 0)
            .DistinctBy(n => n.Title)
            .OrderByDescending(n => n.Published)
            .ToList();
    }

    /// <summary>Strips HTML tags/entities and collapses whitespace.</summary>
    internal static string CleanText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = TagRegex().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        return WhitespaceRegex().Replace(s, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")] private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+")] private static partial Regex WhitespaceRegex();
}
