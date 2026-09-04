using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortfolioManager.Core.Data;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Prices;

/// <summary>
/// Background service that refreshes all position prices from Yahoo Finance every 15 minutes.
/// Notifies <see cref="PriceState"/> so the UI can update "Last updated X min ago".
/// </summary>
public class PriceRefreshService(
    IServiceScopeFactory scopeFactory,
    YahooFinanceClient yahooClient,
    PriceState priceState,
    ILogger<PriceRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public async Task ForceRefreshAsync()
    {
        await RefreshAsync(CancellationToken.None);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Price refresh service started. Interval: {Interval}", Interval);

        // Refresh immediately on startup, then every 15 min
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var symbols = await db.Positions
                .Where(p => p.IsOpen)
                .Select(p => p.Symbol)
                .Distinct()
                .ToListAsync(ct);

            if (symbols.Count == 0)
            {
                priceState.NotifyUpdated();
                return;
            }

            var fetched = await yahooClient.FetchPricesAsync(symbols, ct);

            foreach (var price in fetched)
            {
                var existing = await db.PriceCache.FindAsync([price.Symbol], ct);
                if (existing is null)
                    db.PriceCache.Add(price);
                else
                {
                    existing.LastPrice = price.LastPrice;
                    existing.PreviousClose = price.PreviousClose;
                    existing.Currency = price.Currency;
                    existing.FetchedAt = price.FetchedAt;
                }
            }

            await db.SaveChangesAsync(ct);
            priceState.NotifyUpdated();
            logger.LogInformation("Prices refreshed for {Count} symbols at {Time:u}", fetched.Count, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error refreshing prices.");
        }
    }
}
