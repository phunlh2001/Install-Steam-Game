using InstallApp.SteamService;
using System.Net.Http.Headers;

namespace InstallApp.AppService;

public sealed class AppRunner(IThirdPartyService thirdPartyService, IManifestService manifestService)
{
    private readonly IThirdPartyService _thirdPartyService = thirdPartyService;
    private readonly IManifestService _manifestService = manifestService;

    public async Task RunAsync(string token, string appId, string? gameType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(appId))
        {
            Console.WriteLine("Missing token or appId.");
            return;
        }

        var hookDllDeployer = HookDllDeployer.Create();
        var hookDllUpdater = HookDllUpdater.Create();
        if (hookDllDeployer == null || hookDllUpdater == null)
            return;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Process third-party files if gameType is specified (Hook DLL deployer is skipped)
        if (!string.IsNullOrWhiteSpace(gameType))
        {
            await _thirdPartyService.ProcessAsync(httpClient, appId, gameType, ct).ConfigureAwait(false);
        }
        else
        {
            // Execute Hook DLL deployment (pre-checking missing DLLs) BEFORE manifest installation
            await hookDllDeployer.EnsureHookDllsDeployedAsync(httpClient, ct).ConfigureAwait(false);

            // Check updater
            var isApplyPatternToml = await hookDllUpdater.EnsurePatternTomlAsync(ct).ConfigureAwait(false);
            if (isApplyPatternToml)
            {
                var resultUpdater = await hookDllUpdater.CheckAndApplyUpdateAsync(ct);
                Console.WriteLine(resultUpdater.Message);
            }

            // Process manifest files
            await _manifestService.ProcessAsync(httpClient, appId, ct).ConfigureAwait(false);

            // Restart Steam client if active
            var stPath = new SteamPathsResolver().ResolveSteamInstall();
            if (stPath != null)
            {
                var result = await SteamClientRestart.TryRestartAsync(stPath, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                Console.WriteLine(result.Message);
            }
        }
    }
}
