using StockScout.Core.Analysis;
using StockScout.Core.Models;

namespace StockScout.Core.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A price series that compounds at <paramref name="dailyDrift"/> per day with a little deterministic wiggle.</summary>
    public static PriceHistory History(string ticker, double dailyDrift, int days = 200, long volume = 1_000_000,
        long recentVolume = 1_000_000, double start = 100)
    {
        var bars = new List<PriceBar>();
        double price = start;
        var date = new DateOnly(2026, 1, 1);
        for (int i = 0; i < days; i++)
        {
            price *= 1 + dailyDrift + (i % 2 == 0 ? 0.004 : -0.004);
            long v = i >= days - 5 ? recentVolume : volume;
            bars.Add(new PriceBar(date.AddDays(i), price, price * 1.01, price * 0.99, price, v));
        }
        return new PriceHistory(ticker, "USD", bars);
    }

    public static Candidate Candidate(string ticker, double dailyDrift, Market market = Market.US,
        long recentVolume = 1_000_000, PriceHistory? benchmark = null)
    {
        var history = History(ticker, dailyDrift, recentVolume: recentVolume);
        return new Candidate
        {
            Stock = new StockInfo(ticker, ticker + " Inc", market, "Technology"),
            History = history,
            Technicals = Indicators.Snapshot(history, benchmark),
        };
    }

    public static NewsItem News(string ticker, string title, int daysAgo) =>
        new(ticker, title, "summary", "https://example.com/" + Uri.EscapeDataString(title), Now.AddDays(-daysAgo));
}
