using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PortfolioManager.Core.Data;
using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Services;

public sealed class IBrokerAccountSyncClient(IDbContextFactory<AppDbContext> dbFactory, BrokerGatewayOptions options) : IBrokerSyncClient, IBrokerPositionImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string BrokerName => "IBroker";

    public async Task<BrokerSyncResult> SyncAccountAsync(Account account, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.BrokerAccountId))
            return BrokerSyncResult.Fail("IBroker account ID is not configured.");

        if (string.IsNullOrWhiteSpace(options.LoginUrl))
            return BrokerSyncResult.Fail("IBroker login URL is not configured.");

        var endpoint = BuildEndpoint(account.BrokerAccountId);
        List<IBrokerPositionDto>? brokerPositions;

        try
        {
            brokerPositions = await FetchPositionsAsync(endpoint, ct);
        }
        catch (Exception ex)
        {
            return BrokerSyncResult.Fail($"Failed to fetch IBroker positions: {ex.Message}");
        }

        if (brokerPositions is null)
            return BrokerSyncResult.Fail("IBroker positions response was empty.");

        return await PersistPositionsAsync(account, brokerPositions, ct);
    }

    public async Task<BrokerSyncResult> ImportPositionsAsync(Account account, string payloadJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.BrokerAccountId))
            return BrokerSyncResult.Fail("IBroker account ID is not configured.");

        if (string.IsNullOrWhiteSpace(payloadJson))
            return BrokerSyncResult.Fail("IBroker positions payload was empty.");

        List<IBrokerPositionDto>? brokerPositions;
        try
        {
            brokerPositions = JsonSerializer.Deserialize<List<IBrokerPositionDto>>(payloadJson, JsonOptions);
        }
        catch (Exception ex)
        {
            return BrokerSyncResult.Fail($"Failed to parse IBroker positions: {ex.Message}");
        }

        if (brokerPositions is null)
            return BrokerSyncResult.Fail("IBroker positions response was empty.");

        return await PersistPositionsAsync(account, brokerPositions, ct);
    }

    private async Task<List<IBrokerPositionDto>?> FetchPositionsAsync(string endpoint, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(options.LoginUrl, UriKind.Absolute)
        };

        return await client.GetFromJsonAsync<List<IBrokerPositionDto>>(endpoint, JsonOptions, ct);
    }

    private string BuildEndpoint(string brokerAccountId) =>
        $"/v1/api/portfolio/{Uri.EscapeDataString(brokerAccountId)}/positions/0";

    private async Task<BrokerSyncResult> PersistPositionsAsync(Account account, IReadOnlyCollection<IBrokerPositionDto> brokerPositions, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var dbAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == account.Id, ct);
        if (dbAccount is null)
            return BrokerSyncResult.Fail($"Account {account.Id} not found.");

        await ClearExistingPositionsAsync(db, dbAccount.Id, ct);

        var mappedPositions = new List<Position>();
        foreach (var brokerPosition in brokerPositions.Where(p => p.Position != 0))
        {
            var mapped = MapPosition(brokerPosition);
            if (mapped is not null)
                mappedPositions.Add(mapped);
        }

        ApplySpreadGrouping(mappedPositions);

        foreach (var position in mappedPositions)
        {
            position.AccountId = dbAccount.Id;
            db.Positions.Add(position);
        }

        await db.SaveChangesAsync(ct);
        return BrokerSyncResult.Ok($"Imported {mappedPositions.Count} IBroker positions for {dbAccount.Name}.");
    }

    private static void ApplySpreadGrouping(List<Position> positions)
    {
        var groupedPositions = positions
            .Where(p => p.AssetType != AssetType.Stock && p.ExpiryDate is not null)
            .GroupBy(p => new { p.Symbol, ExpiryDate = p.ExpiryDate!.Value.Date })
            .Where(g => g.Count() > 1);

        foreach (var group in groupedPositions)
        {
            var spreadGroupName = $"{group.Key.Symbol} {group.Key.ExpiryDate:yyyy-MM-dd}";
            foreach (var position in group)
                position.SpreadGroupName = spreadGroupName;
        }
    }

    private static async Task ClearExistingPositionsAsync(AppDbContext db, int accountId, CancellationToken ct)
    {
        var transactions = await db.Transactions.Where(t => t.AccountId == accountId).ToListAsync(ct);
        var positions = await db.Positions.Where(p => p.AccountId == accountId).ToListAsync(ct);

        if (transactions.Count > 0)
            db.Transactions.RemoveRange(transactions);
        if (positions.Count > 0)
            db.Positions.RemoveRange(positions);

        await db.SaveChangesAsync(ct);
    }

    private static Position? MapPosition(IBrokerPositionDto dto)
    {
        var contractInfo = ParseContractInfo(dto.ContractDesc);
        var symbol = contractInfo?.Symbol ?? ParseSymbol(dto.ContractDesc);
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var direction = dto.Position < 0 ? PositionDirection.Short : PositionDirection.Long;
        var quantity = Math.Abs(dto.Position);
        var currency = ParseCurrency(dto.Currency);
        var assetType = ParseAssetType(dto, contractInfo);
        var exchange = currency == Currency.CAD ? Exchange.CA : Exchange.US;
        var averageCost = dto.AvgPrice > 0 ? dto.AvgPrice : dto.AvgCost;

        var (strikePrice, expiryDate) = assetType == AssetType.Stock
            ? (null, null)
            : ParseOptionContract(dto.ContractDesc, dto.Strike);

        return new Position
        {
            Symbol = symbol,
            AssetType = assetType,
            Direction = direction,
            Exchange = exchange,
            Currency = currency,
            Quantity = quantity,
            AverageCost = averageCost,
            StrikePrice = strikePrice,
            ExpiryDate = expiryDate,
            IsOpen = true
        };
    }

    private static AssetType ParseAssetType(IBrokerPositionDto dto, ContractInfo? contractInfo)
    {
        var putOrCall = dto.PutOrCall ?? contractInfo?.PutOrCall;

        if (string.Equals(dto.AssetClass, "OPT", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(putOrCall))
            return string.Equals(putOrCall, "P", StringComparison.OrdinalIgnoreCase) ? AssetType.PutOption : AssetType.CallOption;

        return AssetType.Stock;
    }

    private static Currency ParseCurrency(string value) =>
        Enum.TryParse<Currency>(value, true, out var currency) ? currency : Currency.USD;

    private static string ParseSymbol(string contractDesc)
    {
        if (string.IsNullOrWhiteSpace(contractDesc))
            return string.Empty;

        var bracketIndex = contractDesc.IndexOf('[');
        var raw = (bracketIndex >= 0 ? contractDesc[..bracketIndex] : contractDesc).Trim();
        var match = Regex.Match(raw, @"^[A-Z0-9.\-]+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Trim() : raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static (decimal? StrikePrice, DateTime? ExpiryDate) ParseOptionContract(string contractDesc, decimal fallbackStrike)
    {
        var info = ParseContractInfo(contractDesc);
        decimal? strike = fallbackStrike > 0 ? fallbackStrike : null;
        DateTime? expiry = null;

        if (info is not null)
        {
            if (info.Strike > 0)
                strike = info.Strike;

            if (DateTime.TryParseExact(info.ExpiryCode, "yyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExpiry))
                expiry = parsedExpiry;
        }

        return (strike, expiry);
    }

    private static ContractInfo? ParseContractInfo(string contractDesc)
    {
        if (string.IsNullOrWhiteSpace(contractDesc))
            return null;

        var match = Regex.Match(contractDesc, @"\[(?<symbol>[A-Z0-9.\-]+)\s+(?<expiry>\d{6})(?<putcall>[PC])(?<strike>\d{8})\s+\d+\]", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return new ContractInfo(
            match.Groups["symbol"].Value,
            match.Groups["expiry"].Value,
            match.Groups["putcall"].Value.ToUpperInvariant(),
            ParseStrike(match.Groups["strike"].Value));
    }

    private static decimal ParseStrike(string rawStrike)
    {
        if (!decimal.TryParse(rawStrike, out var strike))
            return 0m;

        return strike / 1000m;
    }

    private sealed record ContractInfo(string Symbol, string ExpiryCode, string PutOrCall, decimal Strike);
}
