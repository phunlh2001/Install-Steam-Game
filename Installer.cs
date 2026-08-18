using InstallApp.AppService;
using InstallApp.SteamService;
using System.IO.Compression;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]
namespace InstallApp;

public sealed class Installer
{
    private readonly string _plugin;
    private readonly string _depot;
    private readonly SteamLibrary _steamLibrary;

    public Installer()
    {
        var pathResolver = new SteamPathsResolver();
        _plugin = pathResolver.ResolveStPluginFolder() ?? pathResolver.DefaultStPlugin();
        _depot = pathResolver.ResolveDepotCacheFolder() ?? pathResolver.DefaultStPlugin();
        _steamLibrary = new SteamLibrary();
    }

    public bool OverrideUbisoftDll(string appId, byte[] dllBytes)
    {
        var gamePath = _steamLibrary.GetGameDirectory(appId);
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
        {
            Console.WriteLine($"Game folder not found for AppId: {appId}");
            return false;
        }

        var targetFileName = Constants.ThirdPartyFiles.UbisoftTargetDll;
        var existingFiles = Directory.EnumerateFiles(gamePath, targetFileName, SearchOption.AllDirectories).ToList();

        if (existingFiles.Count > 0)
        {
            foreach (var filePath in existingFiles)
            {
                File.WriteAllBytes(filePath, dllBytes);
            }
        }
        else
        {
            var destination = Path.Combine(gamePath, targetFileName);
            File.WriteAllBytes(destination, dllBytes);
        }

        return true;
    }

    public async Task<bool> InstallRockstarZipAsync(string appId, byte[] zipBytes, CancellationToken ct)
    {
        var gamePath = _steamLibrary.GetGameDirectory(appId);
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
        {
            Console.WriteLine($"Game folder not found for AppId: {appId}");
            return false;
        }

        var workRoot = Path.Combine(Path.GetTempPath(), "CentrixG", Guid.NewGuid().ToString("N"));
        if (!Directory.Exists(workRoot))
            Directory.CreateDirectory(workRoot);

        try
        {
            var zipPath = Path.Combine(workRoot, "rockstar.zip");
            await WriteAllBytesAtomicallyAsync(zipPath, zipBytes, ct).ConfigureAwait(false);

            var extractRoot = Path.Combine(workRoot, "extract");
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

            CopyDirectoryRecursively(extractRoot, gamePath, ct);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error installing Rockstar files: {ex.Message}");
            return false;
        }
        finally
        {
            DeleteDirectoryQuietly(workRoot);
        }
    }

    public async Task InstallManifestForAppAsync(byte[] zipBytes, CancellationToken ct)
    {
        var workRoot = Path.Combine(Path.GetTempPath(), "CentrixG", Guid.NewGuid().ToString("N"));
        if (!Directory.Exists(workRoot))
            Directory.CreateDirectory(workRoot);

        try
        {
            await WriteAllBytesAtomicallyAsync(Path.Combine(workRoot, "manifest.zip"), zipBytes, ct)
                .ConfigureAwait(false);

            var extractRoot = Path.Combine(workRoot, "extract");
            if (!Directory.Exists(extractRoot))
                Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(Path.Combine(workRoot, "manifest.zip"), extractRoot, overwriteFiles: true);

            foreach (var filePath in Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(filePath);
                string targetDir;

                if (string.Equals(ext, ".lua", StringComparison.OrdinalIgnoreCase))
                    targetDir = _plugin;
                else if (string.Equals(ext, ".manifest", StringComparison.OrdinalIgnoreCase))
                    targetDir = _depot;
                else
                    continue;

                var dest = Path.Combine(targetDir, Path.GetFileName(filePath));
                File.Copy(filePath, dest, overwrite: true);
            }
        }
        catch { /* ignore cleanup failures */ }
        finally { DeleteDirectoryQuietly(workRoot); }
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir, CancellationToken ct)
    {
        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, dirPath);
            Directory.CreateDirectory(Path.Combine(targetDir, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceDir, filePath);
            var destPath = Path.Combine(targetDir, relativePath);
            var destFolder = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }
            File.Copy(filePath, destPath, overwrite: true);
        }
    }

    private async Task WriteAllBytesAtomicallyAsync(string path, byte[] data, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        Directory.CreateDirectory(folder);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temp, data, ct).ConfigureAwait(false);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temp, path);
    }

    private void DeleteDirectoryQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* ignore cleanup failures */ }
    }
}
