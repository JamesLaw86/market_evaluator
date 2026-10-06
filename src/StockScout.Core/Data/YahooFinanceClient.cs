using System.Net;
using StockScout.Core.Models;

namespace StockScout.Core.Data;

/// <summary>
/// Free, keyless Yahoo Finance endpoints (chart JSON + headline RSS).
/// These are unofficial, so everything goes through the IPriceDataProvider / INewsProvider
/// interfaces and the rest of the app doesn't know Yahoo exists.
/// </summary>
public sealed class YahooFinanceClient : IPriceDataProvider, INewsProvider
{
    private readonly HttpClient _http;

    public YahooFinanceClient(HttpClient? http = null)
    {
        _http = http ?? CreateDefaultHttpClient();
    }

    public static HttpClient CreateDefaultHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        // Yahoo rejects requests without a browser-like user agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        return http;
    }

    public async Task<PriceHistory?> GetDailyHistoryAsync(string ticker, CancellationToken ct)
    {
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(ticker)}?range=1y&interval=1d";
        var body = await GetWithRetryAsync(url, ct);
        return body is null ? null : YahooParsers.ParseChart(ticker, body);
    }

    public async Task<IReadOnlyList<NewsItem>> GetNewsAsync(StockInfo stock, CancellationToken ct)
    {
        var region = stock.Market == Market.UK ? "GB&lang=en-GB" : "US&lang=en-US";
        var url = $"https://feeds.finance.yahoo.com/rss/2.0/headline?s={Uri.EscapeDataString(stock.Ticker)}&region={region}";
        var body = await GetWithRetryAsync(url, ct);
        if (body is null || !body.TrimStart().StartsWith("<?xml", StringComparison.Ordinal))
            return [];
        return YahooParsers.ParseRss(stock.Ticker, body);
    }

    /// <summary>GET with simple exponential backoff on 429 / 5xx. Returns null on 404 or persistent failure.</summary>
    private async Task<string?> GetWithRetryAsync(string url, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                bool retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (!retryable || attempt == maxAttempts)
                    return null;
            }
            catch (HttpRequestException) when (attempt < maxAttempts) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts) { } // timeout

            await Task.Delay(TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt)), ct);
        }
    }
}
