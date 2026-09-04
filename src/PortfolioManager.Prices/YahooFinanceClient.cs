using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Prices;

/// <summary>
/// Fetches delayed quotes from Yahoo Finance.
/// Yahoo now requires a crumb + cookie obtained from a consent/API page first.
/// </summary>
public class YahooFinanceClient(ILogger<YahooFinanceClient> logger)
{
    // Shared handler with cookie support — reused across calls
    private static readonly HttpClientHandler _handler = new()
    {
        CookieContainer = new CookieContainer(),
        UseCookies = true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    private static readonly HttpClient _http = new(_handler)
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36" },
            { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8" },
            { "Accept-Language", "en-US,en;q=0.9" }
        }
    };

    private string? _crumb;

    /// <summary>Fetches delayed quotes for all symbols. Returns empty list on failure.</summary>
    public async Task<List<PriceCache>> FetchPricesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var results = new List<PriceCache>();
        var symbolList = symbols.ToList();
        if (symbolList.Count == 0) return results;

        try
        {
            // Step 1: get crumb if we don't have one
            _crumb ??= await GetCrumbAsync(ct);
            if (_crumb is null)
            {
                logger.LogWarning("Could not obtain Yahoo Finance crumb — prices unavailable.");
                return results;
            }

            // Step 2: fetch quotes
            var joined = Uri.EscapeDataString(string.Join(",", symbolList));
            var url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={joined}&crumb={Uri.EscapeDataString(_crumb)}&fields=symbol,regularMarketPrice,regularMarketPreviousClose,currency";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Accept", "application/json");

            using var resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            {
                // Crumb expired — clear and retry once
                logger.LogInformation("Crumb expired, refreshing...");
                _crumb = null;
                _crumb = await GetCrumbAsync(ct);
                if (_crumb is null) return results;

                using var req2 = new HttpRequestMessage(HttpMethod.Get,
                    $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={joined}&crumb={Uri.EscapeDataString(_crumb)}&fields=symbol,regularMarketPrice,regularMarketPreviousClose,currency");
                req2.Headers.Add("Accept", "application/json");
                using var resp2 = await _http.SendAsync(req2, ct);
                resp2.EnsureSuccessStatusCode();
                results = await ParseQuoteResponseAsync(resp2, ct);
            }
            else
            {
                resp.EnsureSuccessStatusCode();
                results = await ParseQuoteResponseAsync(resp, ct);
            }

            logger.LogInformation("Fetched prices for {Count} symbols: {Symbols}",
                results.Count, string.Join(", ", results.Select(r => $"{r.Symbol}={r.LastPrice}")));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch prices for: {Symbols}", string.Join(",", symbolList));
            _crumb = null; // reset so next attempt re-authenticates
        }

        return results;
    }

    private async Task<string?> GetCrumbAsync(CancellationToken ct)
    {
        try
        {
            // Hit the main Yahoo Finance page to get session cookies
            using var pageReq = new HttpRequestMessage(HttpMethod.Get, "https://finance.yahoo.com/");
            using var pageResp = await _http.SendAsync(pageReq, ct);
            // Don't throw — just continue; we need the cookies set

            // Now get the crumb
            using var crumbReq = new HttpRequestMessage(HttpMethod.Get, "https://query1.finance.yahoo.com/v1/test/getcrumb");
            crumbReq.Headers.Add("Accept", "text/plain");
            using var crumbResp = await _http.SendAsync(crumbReq, ct);

            if (!crumbResp.IsSuccessStatusCode)
            {
                logger.LogWarning("Crumb endpoint returned {Status}", crumbResp.StatusCode);
                return null;
            }

            var crumb = await crumbResp.Content.ReadAsStringAsync(ct);
            logger.LogInformation("Yahoo Finance crumb obtained: {Crumb}", crumb);
            return string.IsNullOrWhiteSpace(crumb) ? null : crumb.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to obtain crumb.");
            return null;
        }
    }

    private static async Task<List<PriceCache>> ParseQuoteResponseAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var results = new List<PriceCache>();
        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("quoteResponse", out var qr)) return results;
        if (!qr.TryGetProperty("result", out var arr)) return results;

        foreach (var q in arr.EnumerateArray())
        {
            var symbol      = q.TryGetProperty("symbol", out var s)  ? s.GetString() ?? "" : "";
            var price       = q.TryGetProperty("regularMarketPrice", out var p) ? p.GetDecimal() : 0m;
            var prevClose   = q.TryGetProperty("regularMarketPreviousClose", out var pc) ? pc.GetDecimal() : 0m;
            var currencyStr = q.TryGetProperty("currency", out var c) ? c.GetString() : "USD";
            var currency    = currencyStr == "CAD" ? Currency.CAD : Currency.USD;

            if (!string.IsNullOrEmpty(symbol))
                results.Add(new PriceCache
                {
                    Symbol       = symbol,
                    LastPrice    = price,
                    PreviousClose = prevClose,
                    Currency     = currency,
                    FetchedAt    = DateTime.UtcNow
                });
        }

        return results;
    }
}
