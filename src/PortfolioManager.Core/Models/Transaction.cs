namespace PortfolioManager.Core.Models;

public class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public int? PositionId { get; set; }
    public Position? Position { get; set; }

    public DateTime Date { get; set; }
    public TransactionAction Action { get; set; }

    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public Currency Currency { get; set; }

    public string? Notes { get; set; }
}
