namespace PortfolioManager.Core.Services;

public interface IBrokerGatewayLauncher
{
    Task<BrokerGatewayLaunchResult> LaunchAsync(CancellationToken ct = default);
}

public record BrokerGatewayLaunchResult(bool Success, string? Message = null, int? ProcessId = null, string? LoginUrl = null)
{
    public static BrokerGatewayLaunchResult Ok(string? message = null, int? processId = null, string? loginUrl = null)
        => new(true, message, processId, loginUrl);

    public static BrokerGatewayLaunchResult Fail(string message)
        => new(false, message);
}
