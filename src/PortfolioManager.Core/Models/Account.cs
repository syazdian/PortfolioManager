namespace PortfolioManager.Core.Models;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string Owner { get; set; } = string.Empty;

    public decimal CashCAD { get; set; }
    public decimal CashUSD { get; set; }

    /// <summary>Free-text broker identifier, e.g. "IBroker", "Questrade". Empty = no broker linked.</summary>
    public string BrokerName { get; set; } = string.Empty;

    /// <summary>The account number / ID as used by the broker.</summary>
    public string BrokerAccountId { get; set; } = string.Empty;

    public ICollection<Position> Positions { get; set; } = [];
    public ICollection<Transaction> Transactions { get; set; } = [];
}
