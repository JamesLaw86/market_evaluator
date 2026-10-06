using System.IO;
using System.Windows;
using StockScout.App.ViewModels;
using StockScout.Core.Ai;
using StockScout.Core.Analysis;
using StockScout.Core.Data;
using StockScout.Core.Persistence;
using StockScout.Core.Pipeline;

namespace StockScout.App;

public partial class App : Application
{
    /// <summary>Composition root: the only place that knows which concrete implementations are used.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Universe universe;
        try
        {
            universe = Universe.Load(Path.Combine(AppContext.BaseDirectory, "universe.json"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load universe.json:\n{ex.Message}", "StockScout", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var yahoo = new YahooFinanceClient();
        var analystOptions = new ClaudeAnalystOptions();

        // Without a key, fall back to the offline analyst so the app still works end to end.
        bool hasKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
        IStockAnalyst analyst = hasKey ? new ClaudeStockAnalyst(analystOptions) : new RuleBasedAnalyst();
        string analystName = hasKey ? analystOptions.Model : RuleBasedAnalyst.ModelName;

        var pipeline = new PickPipeline(
            universe,
            prices: yahoo,
            news: yahoo,
            analyst: analyst,
            screener: new Screener(new ScreenerOptions()));

        var viewModel = new MainViewModel(pipeline, new RunStore(), universe, analystName, offlineMode: !hasKey);
        new MainWindow { DataContext = viewModel }.Show();
    }
}
