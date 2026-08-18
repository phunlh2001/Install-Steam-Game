using InstallApp.Model;
using System.Text.Json;

namespace InstallApp.AppService;

public interface IThirdPartyService
{
    Task ProcessAsync(HttpClient httpClient, string appId, string gameType, CancellationToken ct = default);
}

public sealed class ThirdPartyService(Installer installer) : IThirdPartyService
{
    private readonly Installer _installer = installer;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessAsync(HttpClient httpClient, string appId, string gameType, CancellationToken ct = default)
    {
        if (string.Equals(gameType, Constants.GameType.UBISOFT, StringComparison.OrdinalIgnoreCase))
        {
            await ProcessUbisoftAsync(httpClient, appId, ct).ConfigureAwait(false);
        }
        else if (string.Equals(gameType, Constants.GameType.ROCKSTAR, StringComparison.OrdinalIgnoreCase))
        {
            await ProcessRockstarAsync(httpClient, appId, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessUbisoftAsync(HttpClient httpClient, string appId, CancellationToken ct)
    {
        var url = $"{Constants.BaseApiUrl}{Constants.Endpoints.ThirdPartyUbisoft}";
        var resp = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch api");
            return;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var res = await JsonSerializer.DeserializeAsync<BaseResponse<ThirdPartyResponse>>(stream, _jsonOptions, ct).ConfigureAwait(false);
        var fileUrl = res?.Data?.FileUrl;

        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            var fileBytes = await httpClient.GetByteArrayAsync(fileUrl, ct).ConfigureAwait(false);
            if (_installer.OverrideUbisoftDll(appId, fileBytes))
            {
                Console.WriteLine("Override ubisoft dll successfully!");
            }
        }
        else
        {
            Console.WriteLine("Not found fileUrl for ubisoft!");
        }
    }

    private async Task ProcessRockstarAsync(HttpClient httpClient, string appId, CancellationToken ct)
    {
        var url = $"{Constants.BaseApiUrl}{Constants.Endpoints.ThirdPartyRockstar}/{appId}";
        var resp = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch api");
            return;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var res = await JsonSerializer.DeserializeAsync<BaseResponse<ThirdPartyResponse>>(stream, _jsonOptions, ct).ConfigureAwait(false);
        var fileUrl = res?.Data?.FileUrl;

        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            var zipBytes = await httpClient.GetByteArrayAsync(fileUrl, ct).ConfigureAwait(false);
            if (await _installer.InstallRockstarZipAsync(appId, zipBytes, ct).ConfigureAwait(false))
            {
                Console.WriteLine("Implemented rockstar third-party successfully!");
            }
        }
        else
        {
            Console.WriteLine("Not found fileUrl for rockstar!");
        }
    }
}
