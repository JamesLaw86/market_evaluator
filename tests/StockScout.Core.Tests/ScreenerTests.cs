using StockScout.Core.Analysis;

namespace StockScout.Core.Tests;

public class ScreenerTests
{
    [Fact]
    public void ZScores_are_zero_for_constant_input_and_clamped_for_outliers()
    {
        Assert.All(Screener.ZScores([5, 5, 5]), z => Assert.Equal(0, z));

        var values = Enumerable.Repeat(0.0, 50).Append(1000).ToArray();
        Assert.Equal(3, Screener.ZScores(values)[^1]);
    }

    [Fact]
    public void Strong_uptrend_outranks_downtrend()
    {
        var screener = new Screener(new ScreenerOptions { MomentumShortlist = 2, UnusualVolumeShortlist = 0 });
        var candidates = new[]
        {
            TestData.Candidate("DOWN", -0.002),
            TestData.Candidate("FLAT", 0.0),
            TestData.Candidate("UP", 0.002),
        };

        var shortlist = screener.ScoreAndShortlistByPrice(candidates);

        Assert.Equal(["UP", "FLAT"], shortlist.Select(c => c.Stock.Ticker));
        Assert.True(candidates[2].QuantScore > candidates[0].QuantScore);
    }

    [Fact]
    public void Unusual_volume_stocks_get_a_second_chance_even_with_weak_momentum()
    {
        var screener = new Screener(new ScreenerOptions { MomentumShortlist = 1, UnusualVolumeShortlist = 1 });
        var candidates = new[]
        {
            TestData.Candidate("UP", 0.002),
            TestData.Candidate("QUIET", -0.001),
            TestData.Candidate("BUSY", -0.001, recentVolume: 5_000_000),
        };

        var shortlist = screener.ScoreAndShortlistByPrice(candidates);

        Assert.Contains(shortlist, c => c.Stock.Ticker == "UP");
        Assert.Contains(shortlist, c => c.Stock.Ticker == "BUSY");
        Assert.DoesNotContain(shortlist, c => c.Stock.Ticker == "QUIET");
    }

    [Fact]
    public void Analyst_shortlist_drops_stale_or_missing_news_and_caps_headlines()
    {
        var screener = new Screener(new ScreenerOptions { NewsLookbackDays = 14, MaxHeadlinesPerStock = 3, AnalystShortlist = 10 });
        var busy = TestData.Candidate("BUSY", 0.001);
        busy.News = Enumerable.Range(0, 6).Select(i => TestData.News("BUSY", $"Story {i}", i)).ToList();
        var stale = TestData.Candidate("STALE", 0.001);
        stale.News = [TestData.News("STALE", "Old story", 30)];
        var none = TestData.Candidate("NONE", 0.001);

        var result = screener.ShortlistForAnalyst([busy, stale, none], TestData.Now);

        var only = Assert.Single(result);
        Assert.Equal("BUSY", only.Stock.Ticker);
        Assert.Equal(3, only.News.Count);
    }

    [Fact]
    public void News_attention_is_measured_before_headlines_are_trimmed()
    {
        // Both stocks have more headlines than are sent to the LLM; the one with more recent coverage must still score higher.
        var screener = new Screener(new ScreenerOptions { MaxHeadlinesPerStock = 3, NewsAttentionDays = 3 });
        var hot = TestData.Candidate("HOT", 0.001);
        hot.News = Enumerable.Range(0, 12).Select(i => TestData.News("HOT", $"Hot {i}", i % 3)).ToList();
        var mild = TestData.Candidate("MILD", 0.001);
        mild.News = Enumerable.Range(0, 12).Select(i => TestData.News("MILD", $"Mild {i}", i < 4 ? 1 : 8)).ToList();

        screener.ShortlistForAnalyst([hot, mild], TestData.Now);

        Assert.Equal(3, hot.News.Count);
        Assert.Equal(3, mild.News.Count);
        Assert.True(hot.NewsScore > mild.NewsScore);
    }

    [Fact]
    public void Analyst_shortlist_respects_size_limit_and_prefers_higher_scores()
    {
        var screener = new Screener(new ScreenerOptions { AnalystShortlist = 2 });
        var candidates = Enumerable.Range(0, 5).Select(i =>
        {
            var c = TestData.Candidate($"T{i}", 0.001);
            c.QuantScore = i;
            c.News = [TestData.News(c.Stock.Ticker, "Story", 1)];
            return c;
        }).ToList();

        var result = screener.ShortlistForAnalyst(candidates, TestData.Now);

        Assert.Equal(["T4", "T3"], result.Select(c => c.Stock.Ticker));
    }
}
