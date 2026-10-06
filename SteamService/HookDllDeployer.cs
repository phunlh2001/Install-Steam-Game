using InstallApp.AppService;
using InstallApp.Model;
using System.IO.Compression;
using System.Text.Json;

namespace InstallApp.SteamService;

public sealed class HookDllDeployer
{
    private static readonly string[] HookDllNames = ["dwmapi.dll", "xinput1_4.dll", "opensteamtool.toml", "OpenSteamTool.dll"];
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _steamRoot;
    private HookDllDeployer(string steamDir)
    {
        _steamRoot = steamDir;
    }

    public static HookDllDeployer? Create()
    {
        var stPath = new SteamPathsResolver().ResolveSteamInstall();
        if (string.IsNullOrWhiteSpace(stPath) || !Directory.Exists(stPath))
        {
            Console.WriteLine("Steam installation folder not found. Skipping setup hook DLL deployment.");
            return null;
        }

        return new HookDllDeployer(stPath);
    }

    public List<string> GetMissingDlls(string steamRoot)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(steamRoot) || !Directory.Exists(steamRoot))
            return missing;

        foreach (var dllName in HookDllNames)
        {
            var targetPath = Path.Combine(steamRoot, dllName);
            if (!File.Exists(targetPath))
                missing.Add(dllName);
        }
        return missing;
    }

    public async Task EnsureHookDllsDeployedAsync(HttpClient httpClient, CancellationToken ct = default)
    {
        // 1. Pre-check missing DLLs before making network request
        var missingDlls = GetMissingDlls(_steamRoot);
        if (missingDlls.Count == 0)
        {
            Console.WriteLine("All hook DLLs are already installed!.");
            return;
        }

        Console.WriteLine($"Missing hook DLLs: {string.Join(", ", missingDlls)}. Requesting setup package...");

        // 2. Call /third-party/setup endpoint
        var url = $"{Constants.BaseApiUrl}{Constants.Endpoints.ThirdPartySetup}";
        var resp = await httpClient.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine("Failed to fetch setup api");
            return;
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var res = await JsonSerializer.DeserializeAsync<BaseResponse<ThirdPartyResponse>>(stream, _jsonOptions, ct).ConfigureAwait(false);
        var fileUrl = res?.Data?.FileUrl;

        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            Console.WriteLine("Not found fileUrl for setup!");
            return;
        }

        // 3. Download and extract setup.zip
        var zipBytes = await httpClient.GetByteArrayAsync(fileUrl, ct).ConfigureAwait(false);
        var workRoot = Path.Combine(Path.GetTempPath(), "CentrixG", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var zipPath = Path.Combine(workRoot, "setup.zip");
            await WriteAllBytesAtomicallyAsync(zipPath, zipBytes, ct).ConfigureAwait(false);

            var extractRoot = Path.Combine(workRoot, "extract");
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

            var extractedFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in Directory.EnumerateFiles(extractRoot, "*.*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(filePath);
                if (HookDllNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                {
                    var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
                    extractedFiles[fileName] = bytes;
                }
            }

            // 4. Deploy missing files (DLLs & opensteamtool.toml)
            await DeployHookDllsAsync(_steamRoot, missingDlls, extractedFiles, ct).ConfigureAwait(false);
        }
        catch (Exception ex) { Console.WriteLine($"Error deploying setup hook files: {ex.Message}"); }
        finally { DeleteDirectoryQuietly(workRoot); }
    }

    private static async Task DeployHookDllsAsync(string steamRoot, List<string> missingDlls, Dictionary<string, byte[]> dllSources, CancellationToken ct)
    {
        foreach (var dllName in missingDlls)
        {
            ct.ThrowIfCancellationRequested();

            if (dllSources.TryGetValue(dllName, out var bytes) && bytes.Length > 0)
            {
                var targetPath = Path.Combine(steamRoot, dllName);
                try
                {
                    await WriteAllBytesAtomicallyAsync(targetPath, bytes, ct).ConfigureAwait(false);
                    Console.WriteLine($"Deployed {dllName} to {targetPath}");
                }
                catch (Exception ex) { Console.WriteLine($"Failed to deploy {dllName}: {ex.Message}"); }
            }
            else { Console.WriteLine($"Hook file {dllName} was not found in setup.zip"); }
        }
    }

    private static async Task WriteAllBytesAtomicallyAsync(string path, byte[] data, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }

    private static void DeleteDirectoryQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* ignore cleanup failures */ }
    }
}
