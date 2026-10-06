using System.Windows;
using System.Windows.Controls;
using StockScout.App.Charts;
using StockScout.App.ViewModels;

namespace StockScout.App.Views;

/// <summary>Four charts for the selected pick. Redraws whenever its DataContext (a PickViewModel) changes.</summary>
public partial class PickChartsView : UserControl
{
    public PickChartsView()
    {
        InitializeComponent();
        foreach (var plot in new[] { PricePlot, RelativePlot, VolumePlot, RsiPlot })
            ChartRenderer.Configure(plot);
        DataContextChanged += (_, _) => Render();
    }

    private void Render()
    {
        if (DataContext is not PickViewModel pick) return;
        ChartRenderer.RenderPrice(PricePlot, pick);
        ChartRenderer.RenderRelative(RelativePlot, pick);
        ChartRenderer.RenderVolume(VolumePlot, pick);
        ChartRenderer.RenderRsi(RsiPlot, pick);
    }
}
