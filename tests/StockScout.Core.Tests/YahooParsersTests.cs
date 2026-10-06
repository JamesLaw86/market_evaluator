using StockScout.Core.Data;

namespace StockScout.Core.Tests;

public class YahooParsersTests
{
    private const string ChartJson =
        """
        {"chart":{"result":[{"meta":{"currency":"GBp","symbol":"BP.L","gmtoffset":3600},
          "timestamp":[1759132800,1759219200,1759305600,1759392000],
          "indicators":{"quote":[{
            "open":[500.0,505.0,null,512.0],
            "high":[506.0,510.0,null,515.0],
            "low":[498.0,503.0,null,509.0],
            "close":[504.0,508.5,null,514.2],
            "volume":[1000,2000,null,3000]}]}}],"error":null}}
        """;

    [Fact]
    public void ParseChart_reads_bars_and_skips_null_rows()
    {
        var history = YahooParsers.ParseChart("BP.L", ChartJson);

        Assert.NotNull(history);
        Assert.Equal("GBp", history.Currency);
        Assert.Equal(3, history.Bars.Count);
        Assert.Equal(514.2, history.LastClose);
        Assert.Equal(3000, history.Bars[^1].Volume);
        Assert.True(history.Bars.Zip(history.Bars.Skip(1)).All(p => p.First.Date < p.Second.Date));
    }

    [Fact]
    public void ParseChart_returns_null_for_error_response()
    {
        const string json = """{"chart":{"result":null,"error":{"code":"Not Found","description":"No data found"}}}""";
        Assert.Null(YahooParsers.ParseChart("NOPE", json));
    }

    [Fact]
    public void ParseRss_decodes_entities_strips_html_and_sorts_newest_first()
    {
        const string xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <rss version="2.0"><channel>
              <item><title>Older story</title><description>Old</description><link>https://a</link>
                    <pubDate>Mon, 05 Oct 2026 08:00:00 +0000</pubDate></item>
              <item><title>S&amp;P hits record</title><description>&lt;b&gt;Big&lt;/b&gt;   day</description><link> https://b </link>
                    <pubDate>Tue, 06 Oct 2026 14:08:45 +0000</pubDate></item>
              <item><title>S&amp;P hits record</title><description>duplicate</description><link>https://c</link>
                    <pubDate>Tue, 06 Oct 2026 13:00:00 +0000</pubDate></item>
            </channel></rss>
            """;

        var news = YahooParsers.ParseRss("SPY", xml);

        Assert.Equal(2, news.Count);
        Assert.Equal("S&P hits record", news[0].Title);
        Assert.Equal("Big day", news[0].Summary);
        Assert.Equal("https://b", news[0].Link);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 14, 8, 45, TimeSpan.Zero), news[0].Published);
    }
}
