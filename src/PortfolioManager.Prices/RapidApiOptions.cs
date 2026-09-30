namespace PortfolioManager.Prices;

/// <summary>
/// Configuration options for the RapidAPI Yahoo Finance integration.
/// Bound from the "RapidApi" section of appsettings.json.
/// </summary>
public class RapidApiOptions
{
    public string Key  { get; set; } = string.Empty;
    public string Host { get; set; } = "yahoo-finance15.p.rapidapi.com";
}
