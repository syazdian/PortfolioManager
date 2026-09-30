using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortfolioManager.Core.Data;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Prices;

/// <summary>
/// Background service that refreshes all position prices every 15 minutes.
/// • Stocks  → Yahoo Finance (crumb-based API, no key required)
/// • Options → Yahoo Finance via RapidAPI (live option premium prices)
/// Notifies <see cref="PriceState"/> so the UI can update "Last updated X min ago".
/// </summary>
public class PriceRefreshService(
    IServiceScopeFactory scopeFactory,
    YahooFinanceClient yahooClient,
    RapidApiOptionsClient optionsClient,
    PriceState priceState,
    ILogger<PriceRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public async Task ForceRefreshAsync()
    {
        await RefreshAsync(CancellationToken.None);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Price refresh service started in manual-only mode. Automatic refresh is disabled to conserve API quota.");
        return Task.CompletedTask;
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var openPositions = await db.Positions
                .Where(p => p.IsOpen)
                .ToListAsync(ct);

            // ── 1. Stock prices from Yahoo Finance ───────────────────────────
            var stockSymbols = openPositions
                .Where(p => p.AssetType == AssetType.Stock)
                .Select(p => p.Symbol)
                .Distinct()
                .ToList();

            if (stockSymbols.Count > 0)
            {
                var fetched = await yahooClient.FetchPricesAsync(stockSymbols, ct);
                foreach (var price in fetched)
                {
                    var existing = await db.PriceCache.FindAsync([price.Symbol], ct);
                    if (existing is null)
                        db.PriceCache.Add(price);
                    else
                    {
                        existing.LastPrice    = price.LastPrice;
                        existing.PreviousClose = price.PreviousClose;
                        existing.Currency     = price.Currency;
                        existing.FetchedAt    = price.FetchedAt;
                    }
                }
                logger.LogInformation("Stock prices refreshed for {Count} symbols.", fetched.Count);
            }

            // ── 2. Option prices from RapidAPI Yahoo Finance ─────────────────
            var optionPositions = openPositions
                .Where(p => p.AssetType != AssetType.Stock &&
                            p.ExpiryDate.HasValue &&
                            p.StrikePrice.HasValue)
                .ToList();

            if (optionPositions.Count > 0)
            {
                var optionPrices = await optionsClient.FetchOptionPricesAsync(optionPositions, ct);

                foreach (var (positionId, price) in optionPrices)
                {
                    var pos = openPositions.FirstOrDefault(p => p.Id == positionId);
                    if (pos is not null)
                        pos.LastBrokerPrice = price;
                }

                logger.LogInformation("Option prices refreshed for {Count} positions.", optionPrices.Count);
            }

            await db.SaveChangesAsync(ct);
            priceState.NotifyUpdated();
            logger.LogInformation("Price refresh complete at {Time:u}.", DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error refreshing prices.");
        }
    }
}
