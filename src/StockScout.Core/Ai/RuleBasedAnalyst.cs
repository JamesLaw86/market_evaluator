using System.Globalization;
using StockScout.Core.Models;

namespace StockScout.Core.Ai;

/// <summary>
/// Offline fallback used when no API key is configured: picks the highest combined scores (max two per sector)
/// and writes a templated explanation from the numbers. No judgement of the news itself - that is what Claude adds.
/// </summary>
public sealed class RuleBasedAnalyst : IStockAnalyst
{
    public const string ModelName = "rule-based fallback (no Claude)";
    private const int PickCount = 7;
    private const int MaxPerSector = 2;

    public Task<AnalystResult> AnalyzeAsync(IReadOnlyList<Candidate> shortlist, DateTimeOffset asOf, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var chosen = new List<Candidate>();
        foreach (var c in shortlist.OrderByDescending(c => c.CombinedScore))
        {
            if (chosen.Count == PickCount) break;
            if (chosen.Count(x => x.Stock.Sector == c.Stock.Sector) >= MaxPerSector) continue;
            chosen.Add(c);
        }

        var picks = chosen.Select((c, rank) =>
        {
            var t = c.Technicals;
            var thesis = string.Create(inv,
                $"{c.Stock.Name} is up {t.Return1M:0.0%} over the last month and {t.RelativeStrength3M * 100:+0.0;-0.0} points " +
                $"versus its benchmark over three months, with the 20-day average {(t.UpTrend ? "above" : "below")} the 50-day. " +
                $"It had {c.News.Count} recent headlines, most recently: \"{c.News.FirstOrDefault()?.Title}\".");

            var risks = new List<string>();
            if (t.Rsi14 > 70) risks.Add(string.Create(inv, $"RSI of {t.Rsi14:0} suggests the stock is overbought and may pause."));
            if (t.Volatility > 0.5) risks.Add(string.Create(inv, $"High volatility ({t.Volatility:0%} annualised) means large swings either way."));
            risks.Add("This pick was made by a formula with no understanding of what the headlines actually say.");

            return new StockPick(
                c.Stock.Ticker,
                Conviction: Math.Max(3, 7 - rank / 2),
                Thesis: thesis,
                Catalysts: c.News.Take(2).Select(n => n.Title).ToList(),
                Risks: risks,
                SupportingHeadlines: c.News.Count > 0 ? [0] : [],
                HorizonWeeks: 4);
        }).ToList();

        const string summary =
            "Offline mode: these picks come from the quantitative screen alone (momentum, trend, volume and news attention), " +
            "not from Claude reading the news. Set ANTHROPIC_API_KEY and restart to get AI-reviewed picks with real reasoning.";

        return Task.FromResult(new AnalystResult(summary, picks, [], ModelName, 0, 0, 0));
    }
}
