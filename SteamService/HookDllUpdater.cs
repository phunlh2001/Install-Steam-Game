using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace InstallApp.SteamService;

public class HookUpdateResult
{
    public bool Success { get; set; }
    public bool UpdateAvailable { get; set; }
    public string OldVersion { get; set; } = string.Empty;
    public string NewVersion { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class HookDllUpdater
{
    // Allowed payload size bounds (200 KB to 8 MB)
    private const int MinDllBytes = 200 * 1024;
    private const int MaxDllBytes = 8 * 1024 * 1024;

    // Default mirror fallbacks
    private const string MirrorTemplate = "https://raw.githubusercontent.com/madoiscool/BetterSteamTools/updates/{0}";
    private const string TomlTemplate = "https://raw.githubusercontent.com/madoiscool/steam-monitor/pattern/steamclient/{0}";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _steamDir;

    private HookDllUpdater(string steamDir)
    {
        _steamDir = steamDir;
    }

    public static HookDllUpdater? Create()
    {
        var stPath = new SteamPathsResolver().ResolveSteamInstall();
        if (string.IsNullOrWhiteSpace(stPath) || !Directory.Exists(stPath))
        {
            Console.WriteLine("Steam installation folder not found. Skipping setup hook DLL deployment.");
            return null;
        }

        return new HookDllUpdater(stPath);
    }

    public async Task<bool> EnsurePatternTomlAsync(CancellationToken ct = default)
    {
        string steamClientPath = Path.Combine(_steamDir, "steamclient64.dll");
        if (!File.Exists(steamClientPath))
            return false;

        // 1. Calculate SHA-256 of steamclient64.dll
        byte[] bytes = await File.ReadAllBytesAsync(steamClientPath, ct);
        string steamClientSha = ComputeSha256(bytes);

        // 2. Target cache path on disk
        string patternDir = Path.Combine(_steamDir, "opensteamtool", "pattern", "steamclient");
        Directory.CreateDirectory(patternDir);
        string tomlPath = Path.Combine(patternDir, $"{steamClientSha}.toml");

        // 3. Force download the freshest pattern TOML directly from madoiscool/steam-monitor
        var url = string.Format(TomlTemplate, $"{steamClientSha}.toml");

        using var http = new HttpClient();
        var response = await http.GetAsync(url, ct);
        if (response.IsSuccessStatusCode)
        {
            string remoteToml = await response.Content.ReadAsStringAsync(ct);

            // Write/Overwrite to disk so BST has the newest signature file
            await File.WriteAllTextAsync(tomlPath, remoteToml, ct);
            Console.WriteLine($"[Pattern] Successfully registered latest signature TOML: {tomlPath}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Step 1: Cleanup any leftover '.old' files from a previous update.
    /// </summary>
    private void CleanupStagedBackup(string steamDir, string dllName = "OpenSteamTool.dll")
    {
        string oldPath = Path.Combine(steamDir, dllName + ".old");
        try
        {
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
                Console.WriteLine($"[Cleanup] Removed stale backup: {oldPath}");
            }
        }
        catch (Exception ex)
        {
            // If Steam is currently running with the old image, Windows will throw; safe to ignore.
            Console.WriteLine($"[Cleanup] Could not delete {oldPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Main workflow: Checks for updates, downloads, verifies SHA-256, and stages the DLL.
    /// </summary>
    public async Task<HookUpdateResult> CheckAndApplyUpdateAsync(CancellationToken ct = default)
    {
        var currentVersion = "1.0.0";
        var result = new HookUpdateResult { OldVersion = currentVersion };

        // 1. Clean up old backups first
        CleanupStagedBackup(_steamDir);

        // 2. Fetch 'latest.toml' across mirror chain
        string? tomlContent = await FetchFromMirrorsAsync("opensteamtool/latest.toml", ct);
        if (string.IsNullOrEmpty(tomlContent))
        {
            result.Message = "Failed to fetch latest.toml from all mirrors.";
            return result;
        }

        // Parse version, path, and sha256 from toml (using regex or Tomlet/Tomlyn)
        string remoteVersion = ExtractTomlValue(tomlContent, "version");
        string relativePath = ExtractTomlValue(tomlContent, "path");
        string expectedSha = ExtractTomlValue(tomlContent, "sha256");

        if (string.IsNullOrEmpty(remoteVersion) || string.IsNullOrEmpty(relativePath) || string.IsNullOrEmpty(expectedSha))
        {
            result.Message = "latest.toml is missing required keys (version/path/sha256).";
            return result;
        }

        result.NewVersion = remoteVersion;

        // 3. Compare version
        if (string.Equals(currentVersion, remoteVersion, StringComparison.OrdinalIgnoreCase))
        {
            result.Message = $"Already up to date (v{currentVersion}).";
            return result;
        }

        result.UpdateAvailable = true;
        Console.WriteLine($"[Update] New version available: {currentVersion} -> {remoteVersion}");

        // 4. Download new DLL payload into RAM
        byte[]? dllBytes = await DownloadBytesFromMirrorsAsync(relativePath, ct);
        if (dllBytes == null || dllBytes.Length == 0)
        {
            result.Message = "Failed to download new DLL payload.";
            return result;
        }

        // 5. Validation A: File Size Bounds
        if (dllBytes.Length < MinDllBytes || dllBytes.Length > MaxDllBytes)
        {
            result.Message = $"Rejected DLL: suspicious size ({dllBytes.Length} bytes).";
            return result;
        }

        // 5. Validation B: Windows PE MZ Header Check
        if (dllBytes.Length < 2 || dllBytes[0] != 0x4D || dllBytes[1] != 0x5A) // 'M' and 'Z'
        {
            result.Message = "Rejected DLL: missing valid Windows PE executable (MZ) header.";
            return result;
        }

        // 5. Validation C: SHA-256 Checksum Verification
        string actualSha = ComputeSha256(dllBytes);
        if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            result.Message = $"SHA-256 mismatch! Expected {expectedSha}, but got {actualSha}.";
            return result;
        }

        Console.WriteLine($"[Verify] SHA-256 verified successfully: {actualSha}");

        // 6. Safe Staging using NTFS Rename-While-Mapped Trick
        string targetDllPath = Path.Combine(_steamDir, "OpenSteamTool.dll");
        bool staged = StageNewDll(targetDllPath, dllBytes);
        if (!staged)
        {
            result.Message = "Failed to stage new DLL on disk.";
            return result;
        }

        result.Success = true;
        result.Message = $"Successfully staged v{remoteVersion} to {targetDllPath}. Applies upon Steam restart.";
        return result;
    }

    /// <summary>
    /// Renames running DLL to .old and writes new bytes in its place.
    /// </summary>
    private bool StageNewDll(string targetDllPath, byte[] newBytes)
    {
        string backupPath = targetDllPath + ".old";

        try
        {
            if (File.Exists(targetDllPath))
            {
                // NTFS allows moving/renaming an actively loaded DLL!
                File.Move(targetDllPath, backupPath, overwrite: true);
            }

            // Write the fresh bytes to target path
            File.WriteAllBytes(targetDllPath, newBytes);
            Console.WriteLine($"[Stage] Wrote {newBytes.Length} bytes to {targetDllPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Stage Error] {ex.Message}");

            // Rollback if backup exists and target failed to write
            if (File.Exists(backupPath) && !File.Exists(targetDllPath))
            {
                try { File.Move(backupPath, targetDllPath, overwrite: true); } catch { }
            }
            return false;
        }
    }

    // --- Helper Methods ---

    private static string ComputeSha256(byte[] data)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string?> FetchFromMirrorsAsync(string relativePath, CancellationToken ct = default)
    {
        string url = string.Format(MirrorTemplate, relativePath);
        try
        {
            var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync(ct);
        }
        catch { /* Try next mirror */ }

        return null;
    }

    private async Task<byte[]?> DownloadBytesFromMirrorsAsync(string relativePath, CancellationToken ct = default)
    {
        string url = string.Format(MirrorTemplate, relativePath);
        try
        {
            var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch { /* Try next mirror */ }

        return null;
    }

    private static string ExtractTomlValue(string toml, string key)
    {
        var match = Regex.Match(toml, $@"{key}\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
