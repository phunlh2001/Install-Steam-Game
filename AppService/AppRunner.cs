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

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Process third-party files if type is specified
        if (!string.IsNullOrWhiteSpace(gameType))
        {
            await _thirdPartyService.ProcessAsync(httpClient, appId, gameType, ct).ConfigureAwait(false);
        }

        // Process manifest files (always executed)
        await _manifestService.ProcessAsync(httpClient, appId, ct).ConfigureAwait(false);

        // Restart Steam if it is installed/running
        var stPath = new SteamPathsResolver().ResolveSteamInstall();
        if (stPath != null)
        {
            var result = await SteamClientRestart.TryRestartAsync(stPath, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            Console.WriteLine(result.Message);
        }
    }
}
