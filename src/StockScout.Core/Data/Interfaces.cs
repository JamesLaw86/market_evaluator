using StockScout.Core.Models;

namespace StockScout.Core.Data;

public interface IPriceDataProvider
{
    /// <summary>Daily bars for roughly the last year. Returns null if the ticker has no usable data.</summary>
    Task<PriceHistory?> GetDailyHistoryAsync(string ticker, CancellationToken ct);
}

public interface INewsProvider
{
    /// <summary>Recent headlines for a ticker, newest first.</summary>
    Task<IReadOnlyList<NewsItem>> GetNewsAsync(StockInfo stock, CancellationToken ct);
}
