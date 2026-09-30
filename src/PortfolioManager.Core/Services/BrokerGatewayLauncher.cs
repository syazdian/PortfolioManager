using System.Diagnostics;

namespace PortfolioManager.Core.Services;

public sealed class BrokerGatewayLauncher(BrokerGatewayOptions options) : IBrokerGatewayLauncher
{
    public Task<BrokerGatewayLaunchResult> LaunchAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.GatewayFolderPath))
            return Task.FromResult(BrokerGatewayLaunchResult.Fail("IBroker gateway folder is not configured."));

        var gatewayFolder = Path.GetFullPath(options.GatewayFolderPath);
        var launchFile = Path.Combine(gatewayFolder, options.LaunchCommand);

        if (!File.Exists(launchFile))
            return Task.FromResult(BrokerGatewayLaunchResult.Fail($"IBroker launch script was not found: {launchFile}"));

        var loginUrl = string.IsNullOrWhiteSpace(options.LoginUrl) ? "https://localhost:5000/" : options.LoginUrl;
        var command = $"cd /d \"{gatewayFolder}\" && {options.LaunchCommand} {options.LaunchArguments}";
        var process = StartWindowsTerminal(command) ?? StartCommandPrompt(command, gatewayFolder);

        if (process is null)
            return Task.FromResult(BrokerGatewayLaunchResult.Fail("IBroker gateway process could not be started."));

        _ = Task.Run(async () =>
        {
            try
            {
                await WaitForEndpointAsync(loginUrl, ct);
                Process.Start(new ProcessStartInfo
                {
                    FileName = loginUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // ignored on purpose; the command window already shows the gateway output
            }
        }, ct);

        return Task.FromResult(BrokerGatewayLaunchResult.Ok("IBroker gateway launch started.", process.Id, loginUrl));
    }

    private static Process? StartWindowsTerminal(string command)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"new-tab -p \"Command Prompt\" --title IBroker cmd /k \"{command}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static Process? StartCommandPrompt(string command, string gatewayFolder)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"{command}\"",
                WorkingDirectory = gatewayFolder,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> WaitForEndpointAsync(string loginUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(loginUrl, UriKind.Absolute, out var uri))
            return false;

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        for (var i = 0; i < 30; i++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode || (int)response.StatusCode is >= 300 and < 500)
                    return true;
            }
            catch
            {
                // keep waiting for the gateway to finish starting
            }

            await Task.Delay(1000, ct);
        }

        return false;
    }
}
