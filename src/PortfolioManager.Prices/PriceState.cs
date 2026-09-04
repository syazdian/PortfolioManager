namespace PortfolioManager.Prices;

/// <summary>
/// Singleton shared state so Blazor components can react to price updates
/// and display "Last updated X min ago".
/// </summary>
public class PriceState
{
    private DateTime? _lastUpdated;

    public DateTime? LastUpdatedUtc => _lastUpdated;

    public string LastUpdatedLabel =>
        _lastUpdated is null
            ? "Never"
            : $"{(int)(DateTime.UtcNow - _lastUpdated.Value).TotalMinutes} min ago";

    public event Action? OnUpdated;

    public void NotifyUpdated()
    {
        _lastUpdated = DateTime.UtcNow;
        OnUpdated?.Invoke();
    }
}
