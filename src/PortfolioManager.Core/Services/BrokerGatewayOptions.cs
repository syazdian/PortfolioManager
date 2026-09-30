namespace PortfolioManager.Core.Services;

public sealed class BrokerGatewayOptions
{
    public string GatewayFolderPath { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = "https://localhost:5000/";
    public string LaunchCommand { get; set; } = @"bin\run.bat";
    public string LaunchArguments { get; set; } = @"root\conf.yaml";
    public string BrokerUsername { get; set; } = string.Empty;
    public string BrokerPassword { get; set; } = string.Empty;
}
