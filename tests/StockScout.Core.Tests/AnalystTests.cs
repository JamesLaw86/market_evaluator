using System.Text.Json;
using StockScout.Core.Ai;
using StockScout.Core.Models;

namespace StockScout.Core.Tests;

public class AnalystTests
{
    private static List<Candidate> Shortlist()
    {
        var aapl = TestData.Candidate("AAPL", 0.001);
        aapl.News = [TestData.News("AAPL", "Apple beats estimates", 1), TestData.News("AAPL", "New iPhone demand strong", 2)];
        var bp = TestData.Candidate("BP.L", 0.001, Market.UK);
        bp.News = [TestData.News("BP.L", "BP raises dividend", 1)];
        return [aapl, bp];
    }

    private static string Pick(string ticker, int conviction = 6, string headlines = "[0]") =>
        $$"""{"ticker":"{{ticker}}","conviction":{{conviction}},"thesis":"t","catalysts":["c"],"risks":["r"],"supporting_headlines":{{headlines}},"horizon_weeks":4}""";

    [Fact]
    public void Parser_keeps_valid_picks_and_drops_hallucinated_tickers()
    {
        var json = $$"""{"market_summary":"Calm.","picks":[{{Pick("AAPL")}},{{Pick("TSLA")}},{{Pick("bp.l")}}]}""";

        var (summary, picks, warnings) = AnalystResponseParser.Parse(json, Shortlist());

        Assert.Equal("Calm.", summary);
        Assert.Equal(["AAPL", "BP.L"], picks.Select(p => p.Ticker));
        Assert.Contains(warnings, w => w.Contains("TSLA"));
    }

    [Fact]
    public void Parser_clamps_conviction_and_filters_invalid_headline_indices()
    {
        var json = $$"""{"market_summary":"","picks":[{{Pick("AAPL", conviction: 42, headlines: "[0, 1, 1, 7, -1]")}}]}""";

        var pick = Assert.Single(AnalystResponseParser.Parse(json, Shortlist()).Picks);

        Assert.Equal(10, pick.Conviction);
        Assert.Equal([0, 1], pick.SupportingHeadlines);
    }

    [Fact]
    public void Parser_drops_duplicates_and_warns_when_fewer_than_five()
    {
        var json = $$"""{"market_summary":"","picks":[{{Pick("AAPL")}},{{Pick("AAPL")}}]}""";

        var (_, picks, warnings) = AnalystResponseParser.Parse(json, Shortlist());

        Assert.Single(picks);
        Assert.Contains(warnings, w => w.Contains("duplicate"));
        Assert.Contains(warnings, w => w.Contains("Only 1"));
    }

    [Fact]
    public void Parser_keeps_top_ten_by_conviction_when_model_returns_too_many()
    {
        var shortlist = Enumerable.Range(0, 12).Select(i =>
        {
            var c = TestData.Candidate($"T{i}", 0.001);
            c.News = [TestData.News(c.Stock.Ticker, "x", 1)];
            return c;
        }).ToList();
        var picksJson = string.Join(",", shortlist.Select((c, i) => Pick(c.Stock.Ticker, conviction: (i % 10) + 1)));

        var (_, picks, _) = AnalystResponseParser.Parse($$"""{"market_summary":"","picks":[{{picksJson}}]}""", shortlist);

        Assert.Equal(10, picks.Count);
        Assert.DoesNotContain(picks, p => p.Conviction == 1);
    }

    [Fact]
    public void Prompt_lists_every_candidate_with_indexed_headlines()
    {
        var message = AnalystPrompt.BuildUserMessage(Shortlist(), TestData.Now);

        Assert.Contains("## AAPL", message);
        Assert.Contains("## BP.L", message);
        Assert.Contains("[1] 2026-10-04 New iPhone demand strong", message);
        Assert.Contains("<shortlist>", message);
    }

    [Fact]
    public void Output_schema_is_strict_and_requires_picks()
    {
        var schema = JsonSerializer.Serialize(AnalystPrompt.OutputSchema());

        using var doc = JsonDocument.Parse(schema);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("picks", root.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        var pick = root.GetProperty("properties").GetProperty("picks").GetProperty("items");
        Assert.False(pick.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(7, pick.GetProperty("required").GetArrayLength());
    }
}

public class RuleBasedAnalystTests
{
    [Fact]
    public async Task Picks_top_scores_with_at_most_two_per_sector()
    {
        var shortlist = Enumerable.Range(0, 12).Select(i =>
        {
            var c = TestData.Candidate($"T{i}", 0.001);
            c.CombinedScore = 12 - i; // T0 best
            c.News = [TestData.News(c.Stock.Ticker, "headline", 1)];
            return c;
        }).ToList(); // all "Technology"

        var result = await new RuleBasedAnalyst().AnalyzeAsync(shortlist, TestData.Now, CancellationToken.None);

        Assert.Equal(["T0", "T1"], result.Picks.Select(p => p.Ticker));
        Assert.Equal(0m, result.EstimatedCostUsd);
    }
}
