using InstallApp.Model;
using System.Text.Json;

namespace InstallApp.AppService;

public interface IManifestService
{
    Task ProcessAsync(HttpClient httpClient, string appId, CancellationToken ct = default);
}

public sealed class ManifestService(Installer installer) : IManifestService
{
    private readonly Installer _installer = installer;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessAsync(HttpClient httpClient, string appId, CancellationToken ct = default)
    {
        try
        {
            var manifestEndpoint = string.Format(Constants.Endpoints.Manifest, appId);
            var url = $"{Constants.BaseApiUrl}{manifestEndpoint}";

            var resp = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to fetch api");
                return;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var res = await JsonSerializer.DeserializeAsync<BaseResponse<ManifestResponse>>(stream, _jsonOptions, ct).ConfigureAwait(false);
            var manifestUrl = res?.Data?.ManifestUrl;

            if (!string.IsNullOrWhiteSpace(manifestUrl))
            {
                byte[] fileBytes = await httpClient.GetByteArrayAsync(manifestUrl, ct).ConfigureAwait(false);
                await _installer.InstallManifestForAppAsync(fileBytes, ct).ConfigureAwait(false);
                Console.WriteLine("Implemented manifest successfully!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get file: {ex.Message}");
        }
    }
}
