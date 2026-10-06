using StockScout.Core.Analysis;

namespace StockScout.Core.Tests;

public class IndicatorsTests
{
    [Fact]
    public void Sma_averages_the_trailing_window_and_is_NaN_before_it_fills()
    {
        var sma = Indicators.SmaSeries([1, 2, 3, 4, 5], 3);

        Assert.True(double.IsNaN(sma[0]));
        Assert.True(double.IsNaN(sma[1]));
        Assert.Equal(2, sma[2], 10);
        Assert.Equal(3, sma[3], 10);
        Assert.Equal(4, sma[4], 10);
    }

    [Fact]
    public void Rsi_matches_hand_calculated_wilder_value()
    {
        // Classic textbook series (StockCharts RSI example). First 14 changes:
        // gains sum 3.34 -> avg 0.23857, losses sum 1.40 -> avg 0.1, RS 2.3857, RSI 70.46.
        double[] closes = [44.34, 44.09, 44.15, 43.61, 44.33, 44.83, 45.10, 45.42, 45.84, 46.08, 45.89, 46.03, 45.61, 46.28, 46.28];

        var rsi = Indicators.RsiSeries(closes, 14);

        Assert.True(double.IsNaN(rsi[13]));
        Assert.Equal(70.46, rsi[14], 2);
    }

    [Fact]
    public void Rsi_is_100_when_price_only_rises()
    {
        var closes = Enumerable.Range(1, 30).Select(i => (double)i).ToArray();
        Assert.Equal(100, Indicators.RsiSeries(closes)[^1]);
    }

    [Fact]
    public void Return_compares_last_close_with_close_n_days_earlier()
    {
        double[] closes = [100, 105, 110, 120];
        Assert.Equal(0.20, Indicators.Return(closes, 3), 10);
        Assert.Equal(120.0 / 110 - 1, Indicators.Return(closes, 1), 10);
        Assert.Equal(0.20, Indicators.Return(closes, 50), 10); // clamps to available history
    }

    [Fact]
    public void Volatility_is_zero_for_constant_growth_and_positive_for_noisy_series()
    {
        var steady = Enumerable.Range(0, 100).Select(i => 100 * Math.Pow(1.01, i)).ToArray();
        var noisy = Enumerable.Range(0, 100).Select(i => 100 + (i % 2 == 0 ? 5.0 : -5.0)).ToArray();

        Assert.Equal(0, Indicators.AnnualisedVolatility(steady), 6);
        Assert.True(Indicators.AnnualisedVolatility(noisy) > 1);
    }

    [Fact]
    public void Rebase_starts_at_100()
    {
        var rebased = Indicators.Rebase([50, 55, 45]);
        Assert.Equal([100, 110, 90], rebased.Select(v => Math.Round(v, 6)));
    }

    [Fact]
    public void Snapshot_reports_relative_strength_against_benchmark()
    {
        var stock = TestData.History("UP", 0.003);
        var bench = TestData.History("BENCH", 0.001);

        var snap = Indicators.Snapshot(stock, bench);

        Assert.True(snap.Return3M > 0);
        Assert.True(snap.RelativeStrength3M > 0);
        Assert.True(snap.UpTrend);
        Assert.True(snap.AboveSma50(stock.LastClose));
        Assert.InRange(snap.Rsi14, 0, 100);
    }

    [Fact]
    public void Snapshot_detects_volume_surge()
    {
        var history = TestData.History("VOL", 0.001, volume: 1_000_000, recentVolume: 3_000_000);
        var snap = Indicators.Snapshot(history, null);
        Assert.True(snap.VolumeRatio > 2);
    }
}
