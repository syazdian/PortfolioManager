using Microsoft.EntityFrameworkCore;
using PortfolioManager.Core.Data;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Services;

public class PortfolioService(IDbContextFactory<AppDbContext> dbFactory)
{
    // ── Accounts ────────────────────────────────────────────────────────────

    public async Task<List<Account>> GetAccountsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Accounts.Include(a => a.Positions).ToListAsync();
    }

    public async Task UpdateCashAsync(int accountId, decimal cashCAD, decimal cashUSD)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");
        account.CashCAD = cashCAD;
        account.CashUSD = cashUSD;
        await db.SaveChangesAsync();
    }

    public async Task<Account> AddAccountAsync(string name, AccountType type, string owner)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = new Account { Name = name, Type = type, Owner = owner };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task UpdateAccountAsync(int accountId, string name, AccountType type, string owner)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(accountId)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");
        account.Name = name;
        account.Type = type;
        account.Owner = owner;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAccountAsync(int accountId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts
            .Include(a => a.Positions).ThenInclude(p => p.Transactions)
            .Include(a => a.Transactions)
            .FirstOrDefaultAsync(a => a.Id == accountId)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");
        db.Transactions.RemoveRange(account.Transactions);
        db.Positions.RemoveRange(account.Positions);
        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
    }

    // ── Positions ────────────────────────────────────────────────────────────

    public async Task<List<Position>> GetOpenPositionsAsync(int accountId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Positions.Where(p => p.AccountId == accountId && p.IsOpen).ToListAsync();
    }

    /// <summary>Add a new position or average-up/down an existing open one.</summary>
    public async Task<Position> AddOrUpdatePositionAsync(int accountId, string symbol,
        AssetType assetType, PositionDirection direction, Exchange exchange, Currency currency,
        decimal quantity, decimal price, DateTime date,
        decimal? strikePrice = null, DateTime? expiryDate = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var account = await db.Accounts.FindAsync(accountId)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");

        ValidateAccountRule(account, assetType, direction);

        var existing = await db.Positions.FirstOrDefaultAsync(p =>
            p.AccountId == accountId &&
            p.Symbol == symbol &&
            p.AssetType == assetType &&
            p.Direction == direction &&
            p.IsOpen);

        if (existing is not null)
        {
            var totalCost = existing.AverageCost * existing.Quantity + price * quantity;
            existing.Quantity += quantity;
            existing.AverageCost = totalCost / existing.Quantity;
        }
        else
        {
            existing = new Position
            {
                AccountId = accountId,
                Symbol = symbol,
                AssetType = assetType,
                Direction = direction,
                Exchange = exchange,
                Currency = currency,
                Quantity = quantity,
                AverageCost = price,
                StrikePrice = strikePrice,
                ExpiryDate = expiryDate,
                IsOpen = true
            };
            db.Positions.Add(existing);
        }

        db.Transactions.Add(new Transaction
        {
            AccountId = accountId,
            Position = existing,
            Date = date,
            Action = direction == PositionDirection.Short ? TransactionAction.WriteOption : TransactionAction.Buy,
            Quantity = quantity,
            Price = price,
            Currency = currency
        });

        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>Close or reduce a position (sell shares / close option).</summary>
    public async Task ClosePositionAsync(int positionId, decimal quantity, decimal price, DateTime date)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var position = await db.Positions.FindAsync(positionId)
            ?? throw new InvalidOperationException($"Position {positionId} not found.");

        if (quantity >= position.Quantity)
        {
            position.Quantity = 0;
            position.IsOpen = false;
        }
        else
        {
            position.Quantity -= quantity;
        }

        db.Transactions.Add(new Transaction
        {
            AccountId = position.AccountId,
            PositionId = positionId,
            Date = date,
            Action = TransactionAction.Sell,
            Quantity = quantity,
            Price = price,
            Currency = position.Currency
        });

        await db.SaveChangesAsync();
    }

    // ── P&L ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates unrealized P&L for a position given a current market price.
    /// Options: multiplied by 100 (1 contract = 100 shares).
    /// Short positions: P&L is reversed.
    /// </summary>
    public static decimal CalculateUnrealizedPnL(Position position, decimal currentPrice)
    {
        var multiplier = position.AssetType == AssetType.Stock ? 1m : 100m;
        var pnl = (currentPrice - position.AverageCost) * position.Quantity * multiplier;
        return position.Direction == PositionDirection.Short ? -pnl : pnl;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    public static void ValidateAccountRule(Account account, AssetType assetType, PositionDirection direction)
    {
        if (assetType == AssetType.PutOption &&
            direction == PositionDirection.Short &&
            account.Type != AccountType.Margin)
        {
            throw new InvalidOperationException(
                $"Writing (selling) put options is only allowed in a Margin account. '{account.Name}' is {account.Type}.");
        }
    }

    public static bool IsRuleViolation(Account account, AssetType assetType, PositionDirection direction) =>
        assetType == AssetType.PutOption &&
        direction == PositionDirection.Short &&
        account.Type != AccountType.Margin;

    // ── Price Cache ───────────────────────────────────────────────────────────

    public async Task<Dictionary<string, PriceCache>> GetPriceCacheAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.PriceCache.ToListAsync();
        return all.ToDictionary(p => p.Symbol);
    }

    // ── Edit / Delete ─────────────────────────────────────────────────────────

    public async Task UpdatePositionAsync(int positionId, decimal quantity, decimal averageCost,
        decimal? strikePrice, DateTime? expiryDate, string? spreadGroupName)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var pos = await db.Positions.FindAsync(positionId)
            ?? throw new InvalidOperationException($"Position {positionId} not found.");
        pos.Quantity = quantity;
        pos.AverageCost = averageCost;
        pos.StrikePrice = strikePrice;
        pos.ExpiryDate = expiryDate;
        pos.SpreadGroupName = string.IsNullOrWhiteSpace(spreadGroupName) ? null : spreadGroupName.Trim();
        await db.SaveChangesAsync();
    }

    public async Task DeletePositionAsync(int positionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var pos = await db.Positions.Include(p => p.Transactions).FirstOrDefaultAsync(p => p.Id == positionId)
            ?? throw new InvalidOperationException($"Position {positionId} not found.");
        db.Transactions.RemoveRange(pos.Transactions);
        db.Positions.Remove(pos);
        await db.SaveChangesAsync();
    }

    public async Task<List<Transaction>> GetTransactionsAsync(int accountId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Transactions
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.Date)
            .ToListAsync();
    }

    public async Task DeleteTransactionAsync(int transactionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var tx = await db.Transactions.FindAsync(transactionId)
            ?? throw new InvalidOperationException($"Transaction {transactionId} not found.");
        db.Transactions.Remove(tx);
        await db.SaveChangesAsync();
    }
}
