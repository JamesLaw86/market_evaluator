using StockScout.Core.Models;

namespace StockScout.Core.Analysis;

public sealed record ScreenerOptions
{
    /// <summary>Top N by price score that get their news fetched.</summary>
    public int MomentumShortlist { get; init; } = 35;
    /// <summary>Extra stocks with unusual trading volume that get news fetched even if momentum is weak (possible turnarounds).</summary>
    public int UnusualVolumeShortlist { get; init; } = 10;
    /// <summary>How many candidates are sent to the LLM.</summary>
    public int AnalystShortlist { get; init; } = 25;
    /// <summary>Headlines older than this are ignored.</summary>
    public int NewsLookbackDays { get; init; } = 14;
    /// <summary>Window for measuring how much coverage a stock is getting right now.</summary>
    public int NewsAttentionDays { get; init; } = 3;
    /// <summary>Max headlines per candidate sent to the LLM (keeps the prompt small).</summary>
    public int MaxHeadlinesPerStock { get; init; } = 8;
}

/// <summary>
/// Two-stage, deterministic screen for a swing (weeks) horizon.
/// Stage 1 scores every stock on price action alone; stage 2 adds news attention for the shortlist.
/// The LLM only ever sees stocks that pass both stages.
/// </summary>
public sealed class Screener(ScreenerOptions options)
{
    public ScreenerOptions Options { get; } = options;

    /// <summary>Stage 1: score every candidate on price action and choose which ones to fetch news for.</summary>
    public IReadOnlyList<Candidate> ScoreAndShortlistByPrice(IReadOnlyList<Candidate> candidates)
    {
        if (candidates.Count == 0) return [];

        var zRs = ZScores(candidates.Select(c => c.Technicals.RelativeStrength3M));
        var z1m = ZScores(candidates.Select(c => c.Technicals.Return1M));
        var zVolRatio = ZScores(candidates.Select(c => c.Technicals.VolumeRatio));
        var zVolatility = ZScores(candidates.Select(c => c.Technicals.Volatility));

        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var t = c.Technicals;

            double momentum = 0.40 * zRs[i] + 0.30 * z1m[i];
            double trend = (t.AboveSma50(c.History.LastClose) ? 0.25 : -0.25) + (t.UpTrend ? 0.25 : -0.25);
            double volume = 0.15 * zVolRatio[i];
            double risk = -0.10 * zVolatility[i];
            // For a few-weeks horizon, very overbought stocks tend to pause; deeply oversold ones are falling knives.
            double rsiPenalty = t.Rsi14 > 75 ? -0.5 : t.Rsi14 < 30 ? -0.25 : 0;

            c.QuantScore = momentum + trend + volume + risk + rsiPenalty;
        }

        var momentumPicks = candidates.OrderByDescending(c => c.QuantScore).Take(Options.MomentumShortlist).ToList();
        var unusualVolume = candidates.Except(momentumPicks)
            .Where(c => c.Technicals.VolumeRatio > 1.3)
            .OrderByDescending(c => c.Technicals.VolumeRatio)
            .Take(Options.UnusualVolumeShortlist);

        return [.. momentumPicks, .. unusualVolume];
    }

    /// <summary>Stage 2: attach news, drop stocks with no recent coverage, rank by price + news attention.</summary>
    public IReadOnlyList<Candidate> ShortlistForAnalyst(IReadOnlyList<Candidate> shortlist, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-Options.NewsLookbackDays);
        var attentionCutoff = now.AddDays(-Options.NewsAttentionDays);

        // Measure attention on the full feed *before* trimming to the headlines we send to the LLM.
        var attention = new Dictionary<Candidate, int>();
        foreach (var c in shortlist)
        {
            attention[c] = c.News.Count(n => n.Published >= attentionCutoff);
            c.News = c.News.Where(n => n.Published >= cutoff).Take(Options.MaxHeadlinesPerStock).ToList();
        }

        var withNews = shortlist.Where(c => c.News.Count > 0).ToList();
        if (withNews.Count == 0) return [];

        // log(1+n) so one stock with 50 articles doesn't dominate.
        var zNews = ZScores(withNews.Select(c => Math.Log(1 + attention[c])));
        for (int i = 0; i < withNews.Count; i++)
        {
            withNews[i].NewsScore = zNews[i];
            withNews[i].CombinedScore = withNews[i].QuantScore + 0.5 * zNews[i];
        }

        return withNews.OrderByDescending(c => c.CombinedScore).Take(Options.AnalystShortlist).ToList();
    }

    /// <summary>Standard scores, clamped to ±3 so one outlier can't swamp the ranking.</summary>
    internal static double[] ZScores(IEnumerable<double> values)
    {
        var v = values.Select(x => double.IsFinite(x) ? x : 0).ToArray();
        if (v.Length < 2) return new double[v.Length];
        double mean = v.Average();
        double sd = Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Length - 1));
        if (sd < 1e-12) return new double[v.Length];
        return v.Select(x => Math.Clamp((x - mean) / sd, -3, 3)).ToArray();
    }
}
