using InstallApp;
using InstallApp.Models;
using InstallApp.SteamServices;
using System.Text.Json;

if (args.Length == 0)
{
    Console.WriteLine("Missing arguments");
    return;
}

var appId = args[0];
var apiUrl = "https://centrixg.onrender.com/api/manifest";

try
{
    using var httpClient = new HttpClient();
    var resp = await httpClient.GetAsync($"{apiUrl}/{appId}");
    if (!resp.IsSuccessStatusCode)
    {
        Console.WriteLine("Failed to fetch api");
        return;
    }

    using var stream = await resp.Content.ReadAsStreamAsync();
    var res = await JsonSerializer.DeserializeAsync<Response>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    var manifestUrl = res?.Data.ManifestUrl;
    if (!string.IsNullOrWhiteSpace(manifestUrl))
    {
        byte[] fileBytes = await httpClient.GetByteArrayAsync(manifestUrl);
        var installer = new Installer();
        await installer.InstallForAppAsync(fileBytes, CancellationToken.None);
        Console.WriteLine("Implemented manifest successfully!");

        // Restart steam.exe if it's running
        var stPath = new SteamPathsResolver().ResolveSteamInstall();
        if (stPath != null)
        {
            var result = await SteamClientRestart.TryRestartAsync(stPath, TimeSpan.FromSeconds(60));
            Console.WriteLine(result.Message);
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to get file: {ex.Message}");
}
