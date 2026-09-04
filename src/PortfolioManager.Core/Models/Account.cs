namespace PortfolioManager.Core.Models;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string Owner { get; set; } = string.Empty;

    public decimal CashCAD { get; set; }
    public decimal CashUSD { get; set; }

    public ICollection<Position> Positions { get; set; } = [];
    public ICollection<Transaction> Transactions { get; set; } = [];
}
