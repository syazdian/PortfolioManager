using System.Text.Json.Serialization;

namespace PortfolioManager.Core.Services;

public sealed record IBrokerPositionDto
{
    [JsonPropertyName("acctId")]
    public string AcctId { get; init; } = string.Empty;

    [JsonPropertyName("conid")]
    public long Conid { get; init; }

    [JsonPropertyName("contractDesc")]
    public string ContractDesc { get; init; } = string.Empty;

    [JsonPropertyName("position")]
    public decimal Position { get; init; }

    [JsonPropertyName("mktPrice")]
    public decimal MktPrice { get; init; }

    [JsonPropertyName("mktValue")]
    public decimal MktValue { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = string.Empty;

    [JsonPropertyName("avgCost")]
    public decimal AvgCost { get; init; }

    [JsonPropertyName("avgPrice")]
    public decimal AvgPrice { get; init; }

    [JsonPropertyName("assetClass")]
    public string AssetClass { get; init; } = string.Empty;

    [JsonPropertyName("strike")]
    public decimal Strike { get; init; }

    [JsonPropertyName("putOrCall")]
    public string? PutOrCall { get; init; }
}
