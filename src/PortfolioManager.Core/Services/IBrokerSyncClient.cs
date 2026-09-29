using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Services;

/// <summary>
/// Implement this interface for each broker you want to support.
/// Register all implementations in DI and BrokerSyncService will
/// automatically route syncs to the right one based on Account.BrokerName.
/// </summary>
public interface IBrokerSyncClient
{
    /// <summary>
    /// The broker name this client handles (case-insensitive match against Account.BrokerName).
    /// E.g. "IBroker", "Questrade", "Wealthsimple".
    /// </summary>
    string BrokerName { get; }

    /// <summary>
    /// Connect to the broker, fetch current positions/balances and persist them.
    /// Should disconnect / release resources before returning.
    /// </summary>
    Task<BrokerSyncResult> SyncAccountAsync(Account account, CancellationToken ct = default);
}

public record BrokerSyncResult(bool Success, string? Message = null)
{
    public static BrokerSyncResult Ok(string? msg = null) => new(true, msg);
    public static BrokerSyncResult Fail(string msg) => new(false, msg);
}
