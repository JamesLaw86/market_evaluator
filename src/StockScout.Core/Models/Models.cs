namespace StockScout.Core.Models;

public enum Market { US, UK }

/// <summary>Which part of the universe to scan.</summary>
public enum MarketFilter { All, US, UK }

/// <summary>A stock the app is allowed to consider (loaded from universe.json).</summary>
public sealed record StockInfo(string Ticker, string Name, Market Market, string Sector);

/// <summary>One daily OHLCV bar.</summary>
public sealed record PriceBar(DateOnly Date, double Open, double High, double Low, double Close, long Volume);

/// <summary>Daily price history for one ticker, oldest bar first.</summary>
public sealed record PriceHistory(string Ticker, string Currency, IReadOnlyList<PriceBar> Bars)
{
    public double LastClose => Bars[^1].Close;
}

public sealed record NewsItem(string Ticker, string Title, string Summary, string Link, DateTimeOffset Published);

/// <summary>Price-derived signals used by the screener and shown to the LLM.</summary>
public sealed record TechnicalSnapshot(
    double Return1M,
    double Return3M,
    double RelativeStrength3M,   // 3M return minus benchmark 3M return
    double Sma20,
    double Sma50,
    double Rsi14,
    double Volatility,           // annualised stdev of daily returns
    double VolumeRatio,          // 5-day average volume / 50-day average volume
    double DistanceFrom52WHigh)  // last close / 52w high - 1 (0 = at the high)
{
    public bool AboveSma50(double lastClose) => lastClose > Sma50;
    public bool UpTrend => Sma20 > Sma50;
}

/// <summary>A stock that made it through price screening, with its data attached.</summary>
public sealed class Candidate
{
    public required StockInfo Stock { get; init; }
    public required PriceHistory History { get; init; }
    public required TechnicalSnapshot Technicals { get; init; }
    public double QuantScore { get; set; }
    public double NewsScore { get; set; }
    public double CombinedScore { get; set; }
    public IReadOnlyList<NewsItem> News { get; set; } = [];
}

/// <summary>One recommendation written by the analyst (LLM) for a candidate.</summary>
public sealed record StockPick(
    string Ticker,
    int Conviction,                       // 1-10
    string Thesis,
    IReadOnlyList<string> Catalysts,
    IReadOnlyList<string> Risks,
    IReadOnlyList<int> SupportingHeadlines, // indices into the candidate's News list
    int HorizonWeeks);

public sealed record AnalystResult(
    string MarketSummary,
    IReadOnlyList<StockPick> Picks,
    IReadOnlyList<string> Warnings,       // e.g. picks dropped by validation
    string Model,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

/// <summary>Everything one click of the button produced.</summary>
public sealed record PickReport(
    DateTimeOffset GeneratedAt,
    MarketFilter Filter,
    int UniverseSize,
    int PricedCount,
    int ShortlistSize,
    IReadOnlyList<Candidate> Screened,    // every stock that had price data, with scores
    IReadOnlyList<Candidate> SentToAnalyst,
    AnalystResult Analysis,
    IReadOnlyDictionary<Market, PriceHistory> Benchmarks);
