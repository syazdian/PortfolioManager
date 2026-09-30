using PortfolioManager.Core.Models;

namespace PortfolioManager.Core.Services;

public interface IBrokerPositionImportService
{
    string BrokerName { get; }
    Task<BrokerSyncResult> ImportPositionsAsync(Account account, string payloadJson, CancellationToken ct = default);
}
