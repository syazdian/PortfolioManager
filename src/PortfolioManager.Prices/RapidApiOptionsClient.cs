using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Prices;

/// <summary>
/// Fetches live option prices from the Yahoo Finance RapidAPI endpoint.
/// Endpoint: GET https://yahoo-finance15.p.rapidapi.com/api/v1/markets/options
///   ?ticker=AAPL&amp;expiration={unixTimestamp}&amp;display=list
/// </summary>
public class RapidApiOptionsClient(RapidApiOptions options, ILogger<RapidApiOptionsClient> logger)
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    /// <summary>
    /// Fetches the last price for all open option positions grouped by (symbol, expiry).
    /// Returns a mapping from position Id → last market price.
    /// </summary>
    public async Task<Dictionary<int, decimal>> FetchOptionPricesAsync(
        IEnumerable<Position> optionPositions,
        CancellationToken ct = default)
    {
        var results = new Dictionary<int, decimal>();

        if (string.IsNullOrWhiteSpace(options.Key))
        {
            logger.LogWarning("RapidAPI key is not configured. Option prices will not be refreshed.");
            return results;
        }

        // Group by (underlying symbol, expiry date) to minimise API calls
        var groups = optionPositions
            .Where(p => p.ExpiryDate.HasValue && p.StrikePrice.HasValue)
            .GroupBy(p => new { p.Symbol, ExpiryDate = p.ExpiryDate!.Value.Date })
            .ToList();

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (i > 0)
            {
                // Respect Basic tier rate limit (1 req/sec)
                await Task.Delay(1100, ct);
            }

            try
            {
                var expiryUnix = new DateTimeOffset(
                    group.Key.ExpiryDate, TimeSpan.Zero).ToUnixTimeSeconds();

                var url = $"https://{options.Host}/api/v1/markets/options" +
                          $"?ticker={Uri.EscapeDataString(group.Key.Symbol)}" +
                          $"&expiration={expiryUnix}&display=list";

                using var resp = await SendWithRetryAsync(url, ct);
                if (resp is null || !resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("RapidAPI returned {Status} for {Symbol} exp {Expiry:yyyy-MM-dd}",
                        resp?.StatusCode, group.Key.Symbol, group.Key.ExpiryDate);
                    continue;
                }

                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc   = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                // Parse both calls and puts from the response
                var priceMap = ParseOptionPrices(doc, group.Key.Symbol, group.Key.ExpiryDate);

                foreach (var pos in group)
                {
                    var key = BuildLookupKey(pos.AssetType, pos.StrikePrice!.Value);
                    if (priceMap.TryGetValue(key, out var price) && price > 0)
                    {
                        results[pos.Id] = price;
                        logger.LogInformation("Option price matched: {Symbol} {Type} strike {Strike} exp {Expiry:yyyy-MM-dd} = {Price}",
                            pos.Symbol, pos.AssetType, pos.StrikePrice, pos.ExpiryDate, price);
                    }
                    else
                    {
                        logger.LogWarning("No contract match for {Symbol} {Type} strike {Strike} exp {Expiry:yyyy-MM-dd} in chain of {Count} options",
                            pos.Symbol, pos.AssetType, pos.StrikePrice, pos.ExpiryDate, priceMap.Count);
                    }
                }

                logger.LogInformation("RapidAPI: fetched option chain for {Symbol} exp {Expiry:yyyy-MM-dd} ({Count} contracts available)",
                    group.Key.Symbol, group.Key.ExpiryDate, priceMap.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to fetch option prices for {Symbol} exp {Expiry:yyyy-MM-dd}",
                    group.Key.Symbol, group.Key.ExpiryDate);
            }
        }

        return results;
    }

    private async Task<HttpResponseMessage?> SendWithRetryAsync(string url, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("x-rapidapi-key",  options.Key);
            req.Headers.Add("x-rapidapi-host", options.Host);
            req.Headers.Add("Accept",           "application/json");

            var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == (HttpStatusCode)429 && attempt == 1)
            {
                logger.LogWarning("RapidAPI rate limited (429). Retrying after 2 seconds...");
                await Task.Delay(2000, ct);
                continue;
            }
            return resp;
        }
        return null;
    }

    /// <summary>
    /// Parses the RapidAPI response JSON and builds a (type+strike) → lastPrice map.
    /// Supports the actual Yahoo Finance RapidAPI structure:
    /// { "body": [ { "options": [ { "calls": [...], "puts": [...] } ] } ] }
    /// as well as alternative / flat layouts.
    /// </summary>
    private static Dictionary<string, decimal> ParseOptionPrices(
        JsonDocument doc, string symbol, DateTime expiry)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // Layout 1: root.body
        if (doc.RootElement.TryGetProperty("body", out var body))
        {
            if (body.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in body.EnumerateArray())
                {
                    // Body item has "options" array: [ { "calls": [...], "puts": [...] } ]
                    if (item.TryGetProperty("options", out var optionsArr) && optionsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var optChain in optionsArr.EnumerateArray())
                        {
                            ExtractContracts(map, optChain, "calls", isCall: true);
                            ExtractContracts(map, optChain, "puts",  isCall: false);
                        }
                    }

                    // Body item has direct "calls" / "puts"
                    ExtractContracts(map, item, "calls", isCall: true);
                    ExtractContracts(map, item, "puts",  isCall: false);

                    // Flat array where each item is a contract itself
                    if (item.TryGetProperty("strike", out _) || item.TryGetProperty("contractSymbol", out _))
                    {
                        TryAddContract(map, item, detectTypeFromField: true);
                    }
                }
            }
            else if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("options", out var optionsArr) && optionsArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var optChain in optionsArr.EnumerateArray())
                    {
                        ExtractContracts(map, optChain, "calls", isCall: true);
                        ExtractContracts(map, optChain, "puts",  isCall: false);
                    }
                }
                ExtractContracts(map, body, "calls", isCall: true);
                ExtractContracts(map, body, "puts",  isCall: false);
            }
        }

        // Layout 2: root has "options" directly
        if (doc.RootElement.TryGetProperty("options", out var rootOptions))
        {
            if (rootOptions.ValueKind == JsonValueKind.Array)
            {
                foreach (var optChain in rootOptions.EnumerateArray())
                {
                    ExtractContracts(map, optChain, "calls", isCall: true);
                    ExtractContracts(map, optChain, "puts",  isCall: false);
                }
            }
            else if (rootOptions.ValueKind == JsonValueKind.Object)
            {
                ExtractContracts(map, rootOptions, "calls", isCall: true);
                ExtractContracts(map, rootOptions, "puts",  isCall: false);
            }
        }

        return map;
    }

    private static void ExtractContracts(
        Dictionary<string, decimal> map, JsonElement parent, string arrayName, bool isCall)
    {
        if (!parent.TryGetProperty(arrayName, out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var contract in arr.EnumerateArray())
            TryAddContract(map, contract, detectTypeFromField: false, isCall: isCall);
    }

    private static void TryAddContract(
        Dictionary<string, decimal> map, JsonElement contract,
        bool detectTypeFromField, bool isCall = true)
    {
        // Detect call vs put
        bool contractIsCall = isCall;
        if (detectTypeFromField)
        {
            if (contract.TryGetProperty("type", out var typeProp))
            {
                var typeStr = typeProp.GetString() ?? string.Empty;
                contractIsCall = typeStr.StartsWith("C", StringComparison.OrdinalIgnoreCase);
            }
            else if (contract.TryGetProperty("optionType", out var ot))
            {
                var typeStr = ot.GetString() ?? string.Empty;
                contractIsCall = typeStr.StartsWith("C", StringComparison.OrdinalIgnoreCase);
            }
            else if (contract.TryGetProperty("contractSymbol", out var cs))
            {
                var sym = cs.GetString() ?? string.Empty;
                var cIdx = sym.LastIndexOf('C');
                var pIdx = sym.LastIndexOf('P');
                contractIsCall = cIdx > pIdx;
            }
        }

        // Extract strike
        decimal strike = 0m;
        foreach (var name in new[] { "strike", "strikePrice", "Strike" })
        {
            if (contract.TryGetProperty(name, out var sp))
            {
                if (sp.ValueKind == JsonValueKind.Number) { strike = sp.GetDecimal(); break; }
                if (sp.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(sp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sv)) { strike = sv; break; }
            }
        }
        if (strike <= 0) return;

        // Extract last price — prefer lastPrice, fall back to bid/ask midpoint
        decimal lastPrice = 0m;
        foreach (var name in new[] { "lastPrice", "last", "lastTrade", "regularMarketPrice" })
        {
            if (contract.TryGetProperty(name, out var lp))
            {
                if (lp.ValueKind == JsonValueKind.Number) { lastPrice = lp.GetDecimal(); break; }
                if (lp.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(lp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lv)) { lastPrice = lv; break; }
            }
        }
        if (lastPrice <= 0)
        {
            // Try bid/ask midpoint
            decimal bid = 0, ask = 0;
            if (contract.TryGetProperty("bid", out var b) && b.ValueKind == JsonValueKind.Number) bid = b.GetDecimal();
            if (contract.TryGetProperty("ask", out var a) && a.ValueKind == JsonValueKind.Number) ask = a.GetDecimal();
            if (bid > 0 && ask > 0) lastPrice = (bid + ask) / 2m;
        }
        if (lastPrice <= 0) return;

        var key = BuildLookupKey(contractIsCall ? AssetType.CallOption : AssetType.PutOption, strike);
        map.TryAdd(key, lastPrice);
    }

    private static string BuildLookupKey(AssetType assetType, decimal strike) =>
        $"{(assetType == AssetType.CallOption ? "C" : "P")}:{strike.ToString("0.####", CultureInfo.InvariantCulture)}";
}
