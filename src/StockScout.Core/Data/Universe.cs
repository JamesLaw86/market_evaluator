using System.Text.Json;
using System.Text.Json.Serialization;
using StockScout.Core.Models;

namespace StockScout.Core.Data;

public sealed record Benchmark(string Ticker, string Name);

/// <summary>The set of stocks to scan plus a benchmark per market.</summary>
public sealed class Universe
{
    public required IReadOnlyList<StockInfo> Stocks { get; init; }
    public required IReadOnlyDictionary<Market, Benchmark> Benchmarks { get; init; }

    public IReadOnlyList<StockInfo> Filter(MarketFilter filter) => filter switch
    {
        MarketFilter.US => Stocks.Where(s => s.Market == Market.US).ToList(),
        MarketFilter.UK => Stocks.Where(s => s.Market == Market.UK).ToList(),
        _ => Stocks,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Universe Load(string path) => Parse(File.ReadAllText(path));

    public static Universe Parse(string json)
    {
        var file = JsonSerializer.Deserialize<UniverseFile>(json, JsonOptions)
                   ?? throw new InvalidDataException("universe.json is empty.");

        var duplicates = file.Stocks.GroupBy(s => s.Ticker, StringComparer.OrdinalIgnoreCase)
                                    .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidDataException($"Duplicate tickers in universe: {string.Join(", ", duplicates)}");

        return new Universe
        {
            Stocks = file.Stocks,
            Benchmarks = file.Benchmarks,
        };
    }

    private sealed record UniverseFile(Dictionary<Market, Benchmark> Benchmarks, List<StockInfo> Stocks);
}
