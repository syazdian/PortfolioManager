namespace PortfolioManager.Core.Models;

public class PriceCache
{
    public string Symbol { get; set; } = string.Empty;
    public decimal LastPrice { get; set; }
    public decimal PreviousClose { get; set; }
    public Currency Currency { get; set; }
    public DateTime FetchedAt { get; set; }
}
