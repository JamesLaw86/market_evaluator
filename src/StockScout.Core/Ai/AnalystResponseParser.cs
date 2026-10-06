using System.Text.Json;
using System.Text.Json.Serialization;
using StockScout.Core.Models;

namespace StockScout.Core.Ai;

/// <summary>
/// Turns the model's JSON into validated picks. Never trust LLM output blindly:
/// tickers must come from the shortlist, numbers are clamped, and headline references must exist.
/// </summary>
public static class AnalystResponseParser
{
    public const int MaxPicks = 10;

    public static (string MarketSummary, IReadOnlyList<StockPick> Picks, IReadOnlyList<string> Warnings) Parse(
        string json, IReadOnlyList<Candidate> shortlist)
    {
        var raw = JsonSerializer.Deserialize<RawResponse>(json)
                  ?? throw new JsonException("Model returned empty JSON.");

        var byTicker = shortlist.ToDictionary(c => c.Stock.Ticker, StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var picks = new List<StockPick>();

        foreach (var p in raw.Picks ?? [])
        {
            var ticker = p.Ticker?.Trim() ?? "";
            if (!byTicker.TryGetValue(ticker, out var candidate))
            {
                warnings.Add($"Dropped '{ticker}': not in the shortlist the model was given.");
                continue;
            }
            if (picks.Any(x => x.Ticker == candidate.Stock.Ticker))
            {
                warnings.Add($"Dropped duplicate pick for {ticker}.");
                continue;
            }

            var headlines = (p.SupportingHeadlines ?? [])
                .Where(i => i >= 0 && i < candidate.News.Count).Distinct().ToList();

            picks.Add(new StockPick(
                Ticker: candidate.Stock.Ticker, // canonical casing
                Conviction: Math.Clamp(p.Conviction, 1, 10),
                Thesis: p.Thesis?.Trim() ?? "",
                Catalysts: Clean(p.Catalysts),
                Risks: Clean(p.Risks),
                SupportingHeadlines: headlines,
                HorizonWeeks: Math.Clamp(p.HorizonWeeks, 1, 52)));
        }

        if (picks.Count > MaxPicks)
        {
            warnings.Add($"Model returned {picks.Count} picks; kept the {MaxPicks} with highest conviction.");
            picks = picks.OrderByDescending(p => p.Conviction).Take(MaxPicks).ToList();
        }
        if (picks.Count < 5)
            warnings.Add($"Only {picks.Count} valid picks (asked for 5-10).");

        return (raw.MarketSummary?.Trim() ?? "", picks, warnings);
    }

    private static IReadOnlyList<string> Clean(List<string>? items) =>
        (items ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private sealed class RawResponse
    {
        [JsonPropertyName("market_summary")] public string? MarketSummary { get; set; }
        [JsonPropertyName("picks")] public List<RawPick>? Picks { get; set; }
    }

    private sealed class RawPick
    {
        [JsonPropertyName("ticker")] public string? Ticker { get; set; }
        [JsonPropertyName("conviction")] public int Conviction { get; set; }
        [JsonPropertyName("thesis")] public string? Thesis { get; set; }
        [JsonPropertyName("catalysts")] public List<string>? Catalysts { get; set; }
        [JsonPropertyName("risks")] public List<string>? Risks { get; set; }
        [JsonPropertyName("supporting_headlines")] public List<int>? SupportingHeadlines { get; set; }
        [JsonPropertyName("horizon_weeks")] public int HorizonWeeks { get; set; }
    }
}
