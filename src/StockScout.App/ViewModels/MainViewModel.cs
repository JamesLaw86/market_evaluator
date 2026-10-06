using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockScout.Core.Ai;
using StockScout.Core.Data;
using StockScout.Core.Models;
using StockScout.Core.Persistence;
using StockScout.Core.Pipeline;

namespace StockScout.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PickPipeline _pipeline;
    private readonly RunStore _runStore;
    private readonly Universe _universe;

    public MainViewModel(PickPipeline pipeline, RunStore runStore, Universe universe, string modelName, bool offlineMode)
    {
        _pipeline = pipeline;
        _runStore = runStore;
        _universe = universe;
        ModelName = modelName;
        ApiKeyMissing = offlineMode;
    }

    public IReadOnlyList<MarketFilter> Markets { get; } = Enum.GetValues<MarketFilter>();
    public ObservableCollection<PickViewModel> Picks { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];
    public string ModelName { get; }
    public bool ApiKeyMissing { get; }
    public string UniverseText => $"{_universe.Stocks.Count} stocks in universe.json";

    [ObservableProperty] private MarketFilter _selectedMarket = MarketFilter.All;
    [ObservableProperty] private PickViewModel? _selectedPick;
    [ObservableProperty] private PickReport? _report;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _marketSummary = "";
    [ObservableProperty] private string _funnelText = "";
    [ObservableProperty] private string _runInfo = "";

    public bool HasResults => Picks.Count > 0;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FindPicksAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        Progress = 0;
        var sw = Stopwatch.StartNew();

        var progress = new Progress<PipelineProgress>(p =>
        {
            Status = p.Total > 1 ? $"{p.Stage}... {p.Done}/{p.Total}" : $"{p.Stage}...";
            Progress = StageOffset(p.Stage) + StageWeight(p.Stage) * p.Fraction;
        });

        try
        {
            var report = await _pipeline.RunAsync(SelectedMarket, progress, ct);
            ShowReport(report);
            var savedTo = await _runStore.SaveAsync(report, CancellationToken.None);
            Status = $"Done in {sw.Elapsed.TotalSeconds:0}s. Saved to {savedTo}";
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (AnalystException ex)
        {
            ErrorMessage = ex.Message;
            Status = "Claude step failed.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Something went wrong: {ex.Message}";
            Status = "Failed.";
        }
        finally
        {
            Progress = 0;
        }
    }

    // Rough progress-bar layout: prices ~60%, news ~25%, Claude the rest.
    private static double StageOffset(string stage) =>
        stage.StartsWith("Downloading") ? 0.02 : stage.StartsWith("Reading") ? 0.62 : stage.StartsWith("Asking") ? 0.87 : stage == "Done" ? 1 : 0;

    private static double StageWeight(string stage) =>
        stage.StartsWith("Downloading") ? 0.60 : stage.StartsWith("Reading") ? 0.25 : 0;

    private void ShowReport(PickReport report)
    {
        Report = report;
        var byTicker = report.SentToAnalyst.ToDictionary(c => c.Stock.Ticker);

        Picks.Clear();
        foreach (var pick in report.Analysis.Picks.OrderByDescending(p => p.Conviction))
        {
            var candidate = byTicker[pick.Ticker];
            var market = candidate.Stock.Market;
            Picks.Add(new PickViewModel(pick, candidate,
                report.Benchmarks.GetValueOrDefault(market),
                _universe.Benchmarks.TryGetValue(market, out var b) ? b.Name : "Benchmark"));
        }
        SelectedPick = Picks.FirstOrDefault();

        Warnings.Clear();
        foreach (var w in report.Analysis.Warnings) Warnings.Add(w);

        MarketSummary = report.Analysis.MarketSummary;
        FunnelText = $"{report.UniverseSize} in universe  →  {report.PricedCount} priced  →  {report.ShortlistSize} news checked  →  " +
                     $"{report.SentToAnalyst.Count} sent to Claude  →  {report.Analysis.Picks.Count} picked";
        RunInfo = $"{report.GeneratedAt.ToLocalTime():g} · {report.Analysis.Model} · " +
                  $"{report.Analysis.InputTokens:N0} in / {report.Analysis.OutputTokens:N0} out tokens · ≈${report.Analysis.EstimatedCostUsd:0.000}";
        OnPropertyChanged(nameof(HasResults));
    }

    [RelayCommand]
    private static void OpenLink(string? url)
    {
        // Links come from a third-party feed: only ever hand http(s) URLs to the shell.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
