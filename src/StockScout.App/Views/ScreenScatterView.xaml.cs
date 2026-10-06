using System.Windows;
using System.Windows.Controls;
using StockScout.App.Charts;
using StockScout.Core.Models;

namespace StockScout.App.Views;

/// <summary>Scatter of every screened stock. Bind <see cref="Report"/> to the current PickReport.</summary>
public partial class ScreenScatterView : UserControl
{
    public static readonly DependencyProperty ReportProperty = DependencyProperty.Register(
        nameof(Report), typeof(PickReport), typeof(ScreenScatterView),
        new PropertyMetadata(null, (d, _) => ((ScreenScatterView)d).Render()));

    public PickReport? Report
    {
        get => (PickReport?)GetValue(ReportProperty);
        set => SetValue(ReportProperty, value);
    }

    public ScreenScatterView()
    {
        InitializeComponent();
        ChartRenderer.Configure(ScatterPlot);
        // Unlike the price charts this one is not inside a scroller, so allow zoom/pan to explore crowded areas.
        ScatterPlot.UserInputProcessor.Enable();
    }

    private void Render()
    {
        if (Report is { } report) ChartRenderer.RenderScreen(ScatterPlot, report);
    }
}
