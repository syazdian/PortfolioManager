using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Core.Data;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Services;

public sealed class QuestradePositionImportService(IDbContextFactory<AppDbContext> dbFactory) : IBrokerPositionImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BrokerName => "Questrade";

    public async Task<BrokerSyncResult> ImportPositionsAsync(Account account, string payloadJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return BrokerSyncResult.Fail("Questrade positions payload was empty.");

        QuestradePositionsResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<QuestradePositionsResponse>(payloadJson, JsonOptions);
        }
        catch (Exception ex)
        {
            return BrokerSyncResult.Fail($"Failed to parse Questrade positions: {ex.Message}");
        }

        var sourcePositions = response?.Positions ?? [];
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dbAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id, ct);
        if (dbAccount is null)
            return BrokerSyncResult.Fail($"Account {account.Id} not found.");

        await ClearExistingPositionsAsync(db, dbAccount.Id, ct);

        var mappedPositions = sourcePositions
            .Where(p => p.OpenQuantity != 0)
            .Select(MapPosition)
            .Where(p => p is not null)
            .Cast<Position>()
            .ToList();

        ApplySpreadGrouping(mappedPositions);

        foreach (var position in mappedPositions)
        {
            position.AccountId = dbAccount.Id;
            db.Positions.Add(position);
        }

        await db.SaveChangesAsync(ct);
        return BrokerSyncResult.Ok($"Imported {mappedPositions.Count} Questrade positions for {dbAccount.Name}.");
    }

    private static async Task ClearExistingPositionsAsync(AppDbContext db, int accountId, CancellationToken ct)
    {
        var transactions = await db.Transactions.Where(t => t.AccountId == accountId).ToListAsync(ct);
        var positions = await db.Positions.Where(p => p.AccountId == accountId).ToListAsync(ct);

        if (transactions.Count > 0)
            db.Transactions.RemoveRange(transactions);
        if (positions.Count > 0)
            db.Positions.RemoveRange(positions);

        await db.SaveChangesAsync(ct);
    }

    private static Position? MapPosition(QuestradePositionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Symbol))
            return null;

        var optionMatch = Regex.Match(dto.Symbol, @"^(?<symbol>[A-Z.]+)(?<day>\d{1,2})(?<month>[A-Za-z]{3})(?<year>\d{2})(?<cp>[CP])(?<strike>\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);

        var direction = dto.OpenQuantity < 0 ? PositionDirection.Short : PositionDirection.Long;
        var quantity = Math.Abs(dto.OpenQuantity);
        var averageCost = dto.AverageEntryPrice;

        var position = new Position
        {
            Direction = direction,
            Quantity = quantity,
            AverageCost = averageCost,
            Exchange = Exchange.US,
            Currency = Currency.USD,
            IsOpen = true
        };

        if (optionMatch.Success)
        {
            position.Symbol = optionMatch.Groups["symbol"].Value.ToUpperInvariant();
            position.AssetType = optionMatch.Groups["cp"].Value.Equals("P", StringComparison.OrdinalIgnoreCase)
                ? AssetType.PutOption
                : AssetType.CallOption;

            if (decimal.TryParse(optionMatch.Groups["strike"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var strike))
                position.StrikePrice = strike;

            var expiryRaw = $"{optionMatch.Groups["day"].Value}{optionMatch.Groups["month"].Value}{optionMatch.Groups["year"].Value}";
            if (DateTime.TryParseExact(expiryRaw, "dMMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                position.ExpiryDate = expiry;
        }
        else
        {
            position.Symbol = dto.Symbol.ToUpperInvariant();
            position.AssetType = AssetType.Stock;
        }

        return position;
    }

    private static void ApplySpreadGrouping(List<Position> positions)
    {
        var optionGroups = positions
            .Where(p => p.AssetType != AssetType.Stock && p.ExpiryDate is not null)
            .GroupBy(p => new { p.Symbol, ExpiryDate = p.ExpiryDate!.Value.Date })
            .Where(g => g.Count() > 1);

        foreach (var group in optionGroups)
        {
            var groupName = $"{group.Key.Symbol} {group.Key.ExpiryDate:yyyy-MM-dd}";
            foreach (var position in group)
                position.SpreadGroupName = groupName;
        }
    }

    private sealed record QuestradePositionsResponse(List<QuestradePositionDto> Positions);

    private sealed record QuestradePositionDto(string Symbol, decimal OpenQuantity, decimal AverageEntryPrice);
}
