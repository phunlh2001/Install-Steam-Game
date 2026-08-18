using InstallApp.Model;
using System.Text.Json;

namespace InstallApp.AppService;

public interface IThirdPartyService
{
    Task ProcessAsync(HttpClient httpClient, string appId, string gameType, CancellationToken ct = default);
}

public sealed class ThirdPartyService : IThirdPartyService
{
    private readonly Installer _installer;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ThirdPartyService(Installer installer)
    {
        _installer = installer;
    }

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

        // Handle response which can be BaseResponse<List<ThirdPartyResponse>>
        List<ThirdPartyResponse>? fileList = null;
        try
        {
            var res = await JsonSerializer.DeserializeAsync<BaseResponse<List<ThirdPartyResponse>>>(stream, _jsonOptions, ct).ConfigureAwait(false);
            fileList = res?.Data;
        }
        catch
        {
            // Fallback for single object BaseResponse<ThirdPartyResponse> if needed
            stream.Position = 0;
            var singleRes = await JsonSerializer.DeserializeAsync<BaseResponse<ThirdPartyResponse>>(stream, _jsonOptions, ct).ConfigureAwait(false);
            if (singleRes?.Data != null)
            {
                fileList = [singleRes.Data];
            }
        }

        if (fileList == null || fileList.Count == 0)
        {
            Console.WriteLine("Not found fileUrl for rockstar!");
            return;
        }

        foreach (var obj in fileList)
        {
            if (!string.IsNullOrWhiteSpace(obj.FileUrl))
            {
                var zipBytes = await httpClient.GetByteArrayAsync(obj.FileUrl, ct).ConfigureAwait(false);
                if (await _installer.InstallRockstarZipAsync(appId, zipBytes, ct).ConfigureAwait(false))
                {
                    Console.WriteLine("Implemented rockstar third-party successfully!");
                }
            }
        }
    }
}
