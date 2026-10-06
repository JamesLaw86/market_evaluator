using StockScout.Core.Models;

namespace StockScout.Core.Analysis;

/// <summary>Standard technical indicators. Series methods return NaN where there isn't enough history yet.</summary>
public static class Indicators
{
    public const int TradingDaysPerMonth = 21;
    public const int TradingDaysPerYear = 252;

    public static double[] SmaSeries(IReadOnlyList<double> values, int period)
    {
        var result = new double[values.Count];
        double sum = 0;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
            if (i >= period) sum -= values[i - period];
            result[i] = i >= period - 1 ? sum / period : double.NaN;
        }
        return result;
    }

    /// <summary>Relative Strength Index using Wilder's smoothing (the conventional definition).</summary>
    public static double[] RsiSeries(IReadOnlyList<double> closes, int period = 14)
    {
        var rsi = Enumerable.Repeat(double.NaN, closes.Count).ToArray();
        if (closes.Count <= period) return rsi;

        double avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            double change = closes[i] - closes[i - 1];
            if (change > 0) avgGain += change; else avgLoss -= change;
        }
        avgGain /= period;
        avgLoss /= period;
        rsi[period] = ToRsi(avgGain, avgLoss);

        for (int i = period + 1; i < closes.Count; i++)
        {
            double change = closes[i] - closes[i - 1];
            double gain = Math.Max(change, 0), loss = Math.Max(-change, 0);
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            rsi[i] = ToRsi(avgGain, avgLoss);
        }
        return rsi;
    }

    private static double ToRsi(double avgGain, double avgLoss) =>
        avgLoss == 0 ? (avgGain == 0 ? 50 : 100) : 100 - 100 / (1 + avgGain / avgLoss);

    /// <summary>Simple return over the last <paramref name="days"/> bars, e.g. 0.05 = +5%.</summary>
    public static double Return(IReadOnlyList<double> closes, int days)
    {
        if (closes.Count < 2) return 0;
        int start = Math.Max(0, closes.Count - 1 - days);
        return closes[^1] / closes[start] - 1;
    }

    /// <summary>Annualised standard deviation of daily log returns over the last <paramref name="days"/> bars.</summary>
    public static double AnnualisedVolatility(IReadOnlyList<double> closes, int days = 63)
    {
        int start = Math.Max(1, closes.Count - days);
        var logReturns = new List<double>();
        for (int i = start; i < closes.Count; i++)
            logReturns.Add(Math.Log(closes[i] / closes[i - 1]));
        if (logReturns.Count < 2) return 0;

        double mean = logReturns.Average();
        double variance = logReturns.Sum(r => (r - mean) * (r - mean)) / (logReturns.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(TradingDaysPerYear);
    }

    public static TechnicalSnapshot Snapshot(PriceHistory history, PriceHistory? benchmark)
    {
        var closes = history.Bars.Select(b => b.Close).ToList();
        var volumes = history.Bars.Select(b => (double)b.Volume).ToList();

        double r3m = Return(closes, 3 * TradingDaysPerMonth);
        double benchR3m = benchmark is null ? 0 : Return(benchmark.Bars.Select(b => b.Close).ToList(), 3 * TradingDaysPerMonth);

        double avgVol50 = volumes.TakeLast(50).DefaultIfEmpty(0).Average();
        double avgVol5 = volumes.TakeLast(5).DefaultIfEmpty(0).Average();

        double high52 = history.Bars.TakeLast(TradingDaysPerYear).Max(b => b.High);

        return new TechnicalSnapshot(
            Return1M: Return(closes, TradingDaysPerMonth),
            Return3M: r3m,
            RelativeStrength3M: r3m - benchR3m,
            Sma20: LastOrNaN(SmaSeries(closes, 20)),
            Sma50: LastOrNaN(SmaSeries(closes, 50)),
            Rsi14: LastOrNaN(RsiSeries(closes, 14)),
            Volatility: AnnualisedVolatility(closes),
            VolumeRatio: avgVol50 > 0 ? avgVol5 / avgVol50 : 1,
            DistanceFrom52WHigh: high52 > 0 ? closes[^1] / high52 - 1 : 0);
    }

    private static double LastOrNaN(double[] series) => series.Length == 0 ? double.NaN : series[^1];

    /// <summary>Rebases a series so its first value is 100 (for comparing a stock with its benchmark).</summary>
    public static double[] Rebase(IReadOnlyList<double> values)
    {
        if (values.Count == 0 || values[0] == 0) return [.. values];
        double first = values[0];
        return values.Select(v => v / first * 100).ToArray();
    }
}
