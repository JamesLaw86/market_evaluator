using System.Globalization;
using StockScout.Core.Models;

namespace StockScout.App.ViewModels;

public sealed record HeadlineViewModel(string Title, string Date, string Link, bool IsSupporting);

/// <summary>One pick as the UI needs it: the LLM's reasoning joined with the price/news data it was based on.</summary>
public sealed class PickViewModel
{
    public PickViewModel(StockPick pick, Candidate candidate, PriceHistory? benchmark, string benchmarkName)
    {
        Pick = pick;
        Candidate = candidate;
        Benchmark = benchmark;
        BenchmarkName = benchmarkName;

        var supporting = pick.SupportingHeadlines.ToHashSet();
        Headlines = candidate.News
            .Select((n, i) => new HeadlineViewModel(n.Title, n.Published.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture), n.Link, supporting.Contains(i)))
            .OrderByDescending(h => h.IsSupporting)
            .ToList();
    }

    public StockPick Pick { get; }
    public Candidate Candidate { get; }
    public PriceHistory? Benchmark { get; }
    public string BenchmarkName { get; }

    public string Ticker => Pick.Ticker;
    public string Name => Candidate.Stock.Name;
    public string MarketAndSector => $"{Candidate.Stock.Market} · {Candidate.Stock.Sector}";
    public int Conviction => Pick.Conviction;
    public string ConvictionText => $"{Pick.Conviction}/10";
    public string Thesis => Pick.Thesis;
    public IReadOnlyList<string> Catalysts => Pick.Catalysts;
    public IReadOnlyList<string> Risks => Pick.Risks;
    public IReadOnlyList<HeadlineViewModel> Headlines { get; }
    public string HorizonText => Pick.HorizonWeeks == 1 ? "~1 week" : $"~{Pick.HorizonWeeks} weeks";

    public string LastPriceText => FormatPrice(Candidate.History.LastClose, Candidate.History.Currency);
    public double Return1M => Candidate.Technicals.Return1M;
    public double Return3M => Candidate.Technicals.Return3M;
    public string Return1MText => Candidate.Technicals.Return1M.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture);
    public string Return3MText => Candidate.Technicals.Return3M.ToString("+0.0%;-0.0%", CultureInfo.InvariantCulture);
    public string RsiText => Candidate.Technicals.Rsi14.ToString("0", CultureInfo.InvariantCulture);
    public string VolatilityText => Candidate.Technicals.Volatility.ToString("0%", CultureInfo.InvariantCulture);

    internal static string FormatPrice(double value, string currency) => currency switch
    {
        "USD" => value.ToString("$#,0.00", CultureInfo.InvariantCulture),
        "GBP" => value.ToString("£#,0.00", CultureInfo.InvariantCulture),
        "GBp" => value.ToString("#,0.0", CultureInfo.InvariantCulture) + "p", // London prices are usually quoted in pence
        _ => value.ToString("#,0.00", CultureInfo.InvariantCulture) + " " + currency,
    };
}
