using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Services;

/// <summary>
/// Orchestrates on-demand broker syncs. Add any number of IBrokerSyncClient
/// implementations to DI — this service picks the right one at runtime.
/// </summary>
public class BrokerSyncService(IEnumerable<IBrokerSyncClient> clients)
{
    /// <summary>
    /// Returns all registered broker names (for display/validation in the UI).
    /// </summary>
    public IReadOnlyList<string> RegisteredBrokers =>
        clients.Select(c => c.BrokerName).ToList();

    /// <summary>
    /// Runs a sync for the given account using the matching IBrokerSyncClient.
    /// Returns Fail if no client is registered for the account's BrokerName.
    /// </summary>
    public async Task<BrokerSyncResult> SyncAccountAsync(Account account, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account.BrokerName))
            return BrokerSyncResult.Fail("No broker configured for this account.");

        var client = clients.FirstOrDefault(
            c => c.BrokerName.Equals(account.BrokerName, StringComparison.OrdinalIgnoreCase));

        if (client is null)
            return BrokerSyncResult.Fail($"No sync client registered for broker \"{account.BrokerName}\".");

        return await client.SyncAccountAsync(account, ct);
    }
}
