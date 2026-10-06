using ScottPlot;
using ScottPlot.WPF;
using StockScout.App.ViewModels;
using StockScout.Core.Analysis;
using StockScout.Core.Models;

namespace StockScout.App.Charts;

/// <summary>
/// Draws the charts with ScottPlot. Plot controls are imperative (not data-bindable), so this lives
/// in the view layer and is driven from small code-behind handlers rather than from the view models.
/// </summary>
public static class ChartRenderer
{
    private const int DisplayBars = 126; // ~6 months of trading days

    private static readonly Color Blue = Color.FromHex("#2563EB");
    private static readonly Color Orange = Color.FromHex("#F59E0B");
    private static readonly Color Purple = Color.FromHex("#8B5CF6");
    private static readonly Color Grey = Color.FromHex("#9CA3AF");
    private static readonly Color Green = Color.FromHex("#10B981");
    private static readonly Color Red = Color.FromHex("#EF4444");

    public static void Configure(WpfPlot control)
    {
        // Charts sit inside a scrolling page; letting them capture the mouse wheel for zoom is maddening.
        control.UserInputProcessor.Disable();
    }

    public static void RenderPrice(WpfPlot control, PickViewModel pick)
    {
        var plot = Reset(control, $"Price, last 6 months ({pick.Candidate.History.Currency})");
        var bars = pick.Candidate.History.Bars;
        var closes = bars.Select(b => b.Close).ToArray();
        var xs = Dates(bars);
        int start = Math.Max(0, bars.Count - DisplayBars);

        AddLine(plot, xs, closes, start, Blue, "Close", width: 2);
        AddLine(plot, xs, Indicators.SmaSeries(closes, 20), start, Orange, "20-day avg");
        AddLine(plot, xs, Indicators.SmaSeries(closes, 50), start, Purple, "50-day avg");

        Finish(control, legend: true);
    }

    public static void RenderVolume(WpfPlot control, PickViewModel pick)
    {
        var plot = Reset(control, "Daily volume");
        var history = pick.Candidate.History.Bars;
        int start = Math.Max(1, history.Count - DisplayBars);

        var barList = new List<Bar>();
        for (int i = start; i < history.Count; i++)
        {
            barList.Add(new Bar
            {
                Position = history[i].Date.ToDateTime(TimeOnly.MinValue).ToOADate(),
                Value = history[i].Volume,
                FillColor = (history[i].Close >= history[i - 1].Close ? Green : Red).WithAlpha(0.7),
                LineWidth = 0,
                Size = 0.8,
            });
        }
        plot.Add.Bars(barList);

        var avg = Indicators.SmaSeries(history.Select(b => (double)b.Volume).ToArray(), 50);
        AddLine(plot, Dates(history), avg, start, Grey, "50-day avg");

        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = v => v >= 1e9 ? $"{v / 1e9:0.#}B" : v >= 1e6 ? $"{v / 1e6:0.#}M" : v >= 1e3 ? $"{v / 1e3:0}K" : $"{v:0}",
        };
        Finish(control, legend: true);
        plot.Axes.SetLimitsY(0, barList.Count == 0 ? 1 : barList.Max(b => b.Value) * 1.1);
        control.Refresh();
    }

    public static void RenderRsi(WpfPlot control, PickViewModel pick)
    {
        var plot = Reset(control, "RSI (14): above 70 = overbought, below 30 = oversold");
        var bars = pick.Candidate.History.Bars;
        var rsi = Indicators.RsiSeries(bars.Select(b => b.Close).ToArray());
        int start = Math.Max(0, bars.Count - DisplayBars);

        AddLine(plot, Dates(bars), rsi, start, Purple, "RSI", width: 1.5f);
        foreach (var level in new[] { 30.0, 70.0 })
        {
            var line = plot.Add.HorizontalLine(level);
            line.Color = Grey;
            line.LinePattern = LinePattern.Dashed;
            line.LineWidth = 1;
        }

        Finish(control, legend: false);
        plot.Axes.SetLimitsY(0, 100);
        control.Refresh();
    }

    public static void RenderRelative(WpfPlot control, PickViewModel pick)
    {
        var plot = Reset(control, $"Performance vs {pick.BenchmarkName} (rebased to 100)");
        var stockBars = pick.Candidate.History.Bars.TakeLast(DisplayBars).ToList();
        if (stockBars.Count == 0) { Finish(control, false); return; }

        AddLine(plot, Dates(stockBars), Indicators.Rebase(stockBars.Select(b => b.Close).ToList()), 0, Blue, pick.Ticker, width: 2);

        if (pick.Benchmark is { } bench)
        {
            // Align the benchmark to the same start date so both lines begin at 100 together.
            var firstDate = stockBars[0].Date;
            var benchBars = bench.Bars.Where(b => b.Date >= firstDate).ToList();
            if (benchBars.Count > 1)
                AddLine(plot, Dates(benchBars), Indicators.Rebase(benchBars.Select(b => b.Close).ToList()), 0, Grey, pick.BenchmarkName, width: 2);
        }

        var hundred = plot.Add.HorizontalLine(100);
        hundred.Color = Grey.WithAlpha(0.5);
        hundred.LineWidth = 1;
        Finish(control, legend: true);
    }

    /// <summary>Every screened stock as a dot: 3-month strength vs benchmark against 1-month return. Shows how the funnel narrowed.</summary>
    public static void RenderScreen(WpfPlot control, PickReport report)
    {
        var plot = Reset(control, "Every stock scanned: 3-month return vs benchmark (x) against 1-month return (y)");
        var picked = report.Analysis.Picks.Select(p => p.Ticker).ToHashSet();
        var shortlisted = report.SentToAnalyst.Select(c => c.Stock.Ticker).ToHashSet();

        void AddGroup(IEnumerable<Candidate> group, Color color, float size, string label)
        {
            var list = group.ToList();
            if (list.Count == 0) return;
            var points = plot.Add.ScatterPoints(
                list.Select(c => c.Technicals.RelativeStrength3M * 100).ToArray(),
                list.Select(c => c.Technicals.Return1M * 100).ToArray());
            points.Color = color;
            points.MarkerSize = size;
            points.LegendText = label;
        }

        AddGroup(report.Screened.Where(c => !shortlisted.Contains(c.Stock.Ticker)), Grey.WithAlpha(0.6), 5, "Screened out");
        AddGroup(report.SentToAnalyst.Where(c => !picked.Contains(c.Stock.Ticker)), Blue.WithAlpha(0.7), 7, "Sent to Claude, not picked");
        var picks = report.SentToAnalyst.Where(c => picked.Contains(c.Stock.Ticker)).ToList();
        AddGroup(picks, Orange, 11, "Picked");

        foreach (var c in picks)
        {
            var label = plot.Add.Text(c.Stock.Ticker, c.Technicals.RelativeStrength3M * 100, c.Technicals.Return1M * 100);
            label.LabelFontSize = 12;
            label.LabelBold = true;
            label.LabelOffsetX = 6;
            label.LabelOffsetY = -6;
        }

        var zeroX = plot.Add.VerticalLine(0);
        zeroX.Color = Grey.WithAlpha(0.5);
        zeroX.LineWidth = 1;
        var zeroY = plot.Add.HorizontalLine(0);
        zeroY.Color = Grey.WithAlpha(0.5);
        zeroY.LineWidth = 1;

        plot.XLabel("3-month return minus benchmark (%)");
        plot.YLabel("1-month return (%)");
        Finish(control, legend: true, dateAxis: false);
    }

    private static Plot Reset(WpfPlot control, string title)
    {
        var plot = control.Plot;
        plot.Clear();
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        plot.Title(title, size: 13);
        plot.Axes.Title.Label.Bold = false;
        plot.XLabel("");
        plot.YLabel("");
        return plot;
    }

    private static void Finish(WpfPlot control, bool legend, bool dateAxis = true)
    {
        var plot = control.Plot;
        if (dateAxis)
        {
            var axis = plot.Axes.DateTimeTicksBottom();
            if (axis.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic dates)
                dates.LabelFormatter = d => d.ToString("d MMM");
        }
        plot.Legend.IsVisible = legend;
        plot.Legend.Alignment = Alignment.UpperLeft;
        plot.Legend.FontSize = 11;
        plot.Axes.AutoScale();
        control.Refresh();
    }

    private static double[] Dates(IReadOnlyList<PriceBar> bars) =>
        bars.Select(b => b.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();

    /// <summary>Adds a line from <paramref name="start"/> onwards, skipping leading NaNs (indicator warm-up).</summary>
    private static void AddLine(Plot plot, double[] xs, IReadOnlyList<double> ys, int start, Color color, string label, float width = 1.5f)
    {
        var px = new List<double>();
        var py = new List<double>();
        for (int i = start; i < ys.Count; i++)
        {
            if (double.IsNaN(ys[i])) continue;
            px.Add(xs[i]);
            py.Add(ys[i]);
        }
        if (px.Count < 2) return;

        var line = plot.Add.ScatterLine(px.ToArray(), py.ToArray());
        line.Color = color;
        line.LineWidth = width;
        line.LegendText = label;
    }
}
