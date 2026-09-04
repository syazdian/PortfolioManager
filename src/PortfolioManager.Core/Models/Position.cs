namespace PortfolioManager.Core.Models;

public class Position
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public string Symbol { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public PositionDirection Direction { get; set; }
    public Exchange Exchange { get; set; }
    public Currency Currency { get; set; }

    /// <summary>Number of shares for stocks; number of contracts (1 contract = 100 shares) for options.</summary>
    public decimal Quantity { get; set; }

    public decimal AverageCost { get; set; }

    /// <summary>For options: strike price. Null for stocks.</summary>
    public decimal? StrikePrice { get; set; }

    /// <summary>For options: expiry date. Null for stocks.</summary>
    public DateTime? ExpiryDate { get; set; }

    public bool IsOpen { get; set; } = true;

    /// <summary>Optional label that groups related legs into a spread (e.g., "SNDK Bull Spread").</summary>
    public string? SpreadGroupName { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
