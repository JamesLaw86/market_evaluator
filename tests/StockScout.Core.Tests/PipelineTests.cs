using Microsoft.Extensions.Time.Testing;
using StockScout.Core.Ai;
using StockScout.Core.Analysis;
using StockScout.Core.Data;
using StockScout.Core.Models;
using StockScout.Core.Pipeline;

namespace StockScout.Core.Tests;

public class PipelineTests
{
    private sealed class FakePrices : IPriceDataProvider
    {
        public Task<PriceHistory?> GetDailyHistoryAsync(string ticker, CancellationToken ct) => ticker switch
        {
            "BROKEN" => throw new HttpRequestException("boom"),
            "MISSING" => Task.FromResult<PriceHistory?>(null),
            "WINNER" => Task.FromResult<PriceHistory?>(TestData.History(ticker, 0.003)),
            _ => Task.FromResult<PriceHistory?>(TestData.History(ticker, 0.0005)),
        };
    }

    private sealed class FakeNews : INewsProvider
    {
        public Task<IReadOnlyList<NewsItem>> GetNewsAsync(StockInfo stock, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([TestData.News(stock.Ticker, $"{stock.Ticker} news", 1)]);
    }

    private sealed class FakeAnalyst : IStockAnalyst
    {
        public IReadOnlyList<Candidate>? Received { get; private set; }

        public Task<AnalystResult> AnalyzeAsync(IReadOnlyList<Candidate> shortlist, DateTimeOffset asOf, CancellationToken ct)
        {
            Received = shortlist;
            var picks = shortlist.Take(5).Select(c => new StockPick(c.Stock.Ticker, 6, "t", [], [], [0], 4)).ToList();
            return Task.FromResult(new AnalystResult("summary", picks, [], "fake", 100, 50, 0.01m));
        }
    }

    private static Universe MakeUniverse(params string[] tickers) => new()
    {
        Stocks = tickers.Select(t => new StockInfo(t, t, t.EndsWith(".L") ? Market.UK : Market.US, "Tech")).ToList(),
        Benchmarks = new Dictionary<Market, Benchmark> { [Market.US] = new("SPY", "S&P 500") },
    };

    [Fact]
    public async Task Run_screens_fetches_news_and_passes_shortlist_to_analyst()
    {
        var universe = MakeUniverse("WINNER", "A", "B", "C", "D", "E", "BROKEN", "MISSING", "UK.L");
        var analyst = new FakeAnalyst();
        var pipeline = new PickPipeline(universe, new FakePrices(), new FakeNews(), analyst,
            new Screener(new ScreenerOptions()), new FakeTimeProvider(TestData.Now));
        var stages = new List<string>();

        var report = await pipeline.RunAsync(MarketFilter.All, new SyncProgress(p => stages.Add(p.Stage)), CancellationToken.None);

        Assert.Equal(9, report.UniverseSize);
        Assert.Equal(7, report.PricedCount);                     // BROKEN and MISSING dropped, run continued
        Assert.Equal("WINNER", analyst.Received![0].Stock.Ticker); // best momentum ranks first
        Assert.Equal(5, report.Analysis.Picks.Count);
        Assert.True(report.Benchmarks.ContainsKey(Market.US));
        Assert.Contains("Reading the news", stages);
    }

    [Fact]
    public async Task Market_filter_limits_the_universe()
    {
        var universe = MakeUniverse("A", "B", "UK1.L", "UK2.L");
        var analyst = new FakeAnalyst();
        var pipeline = new PickPipeline(universe, new FakePrices(), new FakeNews(), analyst,
            new Screener(new ScreenerOptions()), new FakeTimeProvider(TestData.Now));

        var report = await pipeline.RunAsync(MarketFilter.UK, null, CancellationToken.None);

        Assert.Equal(2, report.UniverseSize);
        Assert.All(analyst.Received!, c => Assert.Equal(Market.UK, c.Stock.Market));
    }

    [Fact]
    public void Shipped_universe_file_is_valid()
    {
        var universe = Universe.Load(Path.Combine(AppContext.BaseDirectory, "universe.json"));

        Assert.True(universe.Stocks.Count >= 150);
        Assert.Contains(universe.Stocks, s => s.Market == Market.US);
        Assert.Contains(universe.Stocks, s => s.Market == Market.UK);
        Assert.All(universe.Stocks.Where(s => s.Market == Market.UK), s => Assert.EndsWith(".L", s.Ticker));
        Assert.Equal(2, universe.Benchmarks.Count);
    }

    /// <summary>Progress&lt;T&gt; posts to the thread pool; this one runs inline so the test sees every report.</summary>
    private sealed class SyncProgress(Action<PipelineProgress> onReport) : IProgress<PipelineProgress>
    {
        private readonly Lock _gate = new();
        public void Report(PipelineProgress value) { lock (_gate) onReport(value); }
    }
}
