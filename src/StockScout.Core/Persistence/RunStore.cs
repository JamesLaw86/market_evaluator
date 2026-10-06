using System.Text.Json;
using StockScout.Core.Models;

namespace StockScout.Core.Persistence;

/// <summary>
/// Saves a compact record of every run (picks + entry prices) as JSON under %LOCALAPPDATA%\StockScout\runs.
/// Keeping these makes it possible to check later how past picks actually performed.
/// </summary>
public sealed class RunStore(string? directory = null)
{
    public string Directory { get; } = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockScout", "runs");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> SaveAsync(PickReport report, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var byTicker = report.SentToAnalyst.ToDictionary(c => c.Stock.Ticker);

        var record = new SavedRun(
            report.GeneratedAt,
            report.Filter.ToString(),
            report.Analysis.Model,
            report.Analysis.EstimatedCostUsd,
            report.Analysis.MarketSummary,
            report.Analysis.Picks.Select(p =>
            {
                var c = byTicker[p.Ticker];
                return new SavedPick(p.Ticker, c.Stock.Name, c.History.Currency, c.History.LastClose,
                    p.Conviction, p.HorizonWeeks, p.Thesis);
            }).ToList());

        var path = Path.Combine(Directory, $"run-{report.GeneratedAt:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, JsonOptions), ct);
        return path;
    }

    public sealed record SavedRun(DateTimeOffset GeneratedAt, string Filter, string Model, decimal CostUsd,
        string MarketSummary, IReadOnlyList<SavedPick> Picks);

    public sealed record SavedPick(string Ticker, string Name, string Currency, double EntryPrice,
        int Conviction, int HorizonWeeks, string Thesis);
}
