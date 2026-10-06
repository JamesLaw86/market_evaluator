using System.Collections.Concurrent;
using StockScout.Core.Ai;
using StockScout.Core.Analysis;
using StockScout.Core.Data;
using StockScout.Core.Models;

namespace StockScout.Core.Pipeline;

public sealed record PipelineProgress(string Stage, int Done, int Total)
{
    public double Fraction => Total == 0 ? 0 : (double)Done / Total;
}

/// <summary>
/// The whole "click the button" flow:
/// prices for every stock -> price screen -> news for the shortlist -> news screen -> Claude -> validated picks.
/// </summary>
public sealed class PickPipeline(
    Universe universe,
    IPriceDataProvider prices,
    INewsProvider news,
    IStockAnalyst analyst,
    Screener screener,
    TimeProvider? clock = null)
{
    private const int MaxConcurrentRequests = 6; // be polite to Yahoo
    private const int MinBarsRequired = 60;      // need enough history for SMA50 / 3M return
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<PickReport> RunAsync(MarketFilter filter, IProgress<PipelineProgress>? progress, CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var stocks = universe.Filter(filter);

        // 1. Benchmarks (needed for relative strength).
        progress?.Report(new("Fetching benchmarks", 0, 1));
        var benchmarks = new Dictionary<Market, PriceHistory>();
        foreach (var (market, bench) in universe.Benchmarks)
        {
            var h = await prices.GetDailyHistoryAsync(bench.Ticker, ct);
            if (h is not null) benchmarks[market] = h;
        }

        // 2. Price history for every stock, in parallel.
        var priced = new ConcurrentBag<Candidate>();
        await ForEachThrottledAsync(stocks, "Downloading price history", progress, ct, async (stock, token) =>
        {
            var history = await prices.GetDailyHistoryAsync(stock.Ticker, token);
            if (history is null || history.Bars.Count < MinBarsRequired) return;
            priced.Add(new Candidate
            {
                Stock = stock,
                History = history,
                Technicals = Indicators.Snapshot(history, benchmarks.GetValueOrDefault(stock.Market)),
            });
        });

        if (priced.IsEmpty)
            throw new InvalidOperationException("Could not download price data for any stock. Is Yahoo Finance reachable?");

        // 3. Stage 1 screen on price action.
        var screened = priced.OrderBy(c => c.Stock.Ticker).ToList();
        var newsShortlist = screener.ScoreAndShortlistByPrice(screened);

        // 4. News for the shortlist only (far fewer requests than fetching news for everything).
        await ForEachThrottledAsync(newsShortlist, "Reading the news", progress, ct, async (c, token) =>
        {
            c.News = await news.GetNewsAsync(c.Stock, token);
        });

        // 5. Stage 2: combine price + news attention.
        var forAnalyst = screener.ShortlistForAnalyst(newsShortlist, now);

        // 6. Claude makes the final call.
        progress?.Report(new($"Asking Claude to review {forAnalyst.Count} stocks", 0, 1));
        var analysis = await analyst.AnalyzeAsync(forAnalyst, now, ct);
        progress?.Report(new("Done", 1, 1));

        return new PickReport(
            GeneratedAt: now,
            Filter: filter,
            UniverseSize: stocks.Count,
            PricedCount: screened.Count,
            ShortlistSize: newsShortlist.Count,
            Screened: screened,
            SentToAnalyst: forAnalyst,
            Analysis: analysis,
            Benchmarks: benchmarks);
    }

    private static async Task ForEachThrottledAsync<T>(
        IReadOnlyList<T> items, string stage, IProgress<PipelineProgress>? progress, CancellationToken ct,
        Func<T, CancellationToken, Task> body)
    {
        int done = 0;
        progress?.Report(new(stage, 0, items.Count));
        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRequests, CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    await body(item, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One bad ticker shouldn't sink the whole run; it just drops out.
                    System.Diagnostics.Debug.WriteLine($"{stage}: {item} failed: {ex.Message}");
                }
                progress?.Report(new(stage, Interlocked.Increment(ref done), items.Count));
            });
    }
}
