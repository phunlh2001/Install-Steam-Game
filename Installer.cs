using InstallApp.SteamServices;
using System.IO.Compression;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]
namespace InstallApp;

public sealed class Installer
{
    public async Task InstallForAppAsync(byte[] zipBytes, CancellationToken ct)
    {
        var pathResolver = new SteamPathsResolver();

        var plugin = pathResolver.ResolveStPluginFolder() ?? pathResolver.DefaultStPlugin();
        var depot = pathResolver.ResolveDepotCacheFolder() ?? pathResolver.DefaultStPlugin();

        var workRoot = Path.Combine(Path.GetTempPath(), "CentrixG", Guid.NewGuid().ToString("N"));
        if (!Directory.Exists(workRoot))
            Directory.CreateDirectory(workRoot);

        try
        {
            await WriteAllBytesAutomicallyAsync(Path.Combine(workRoot, "manifest.zip"), zipBytes, ct)
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
                    targetDir = plugin;
                else if (string.Equals(ext, ".manifest", StringComparison.OrdinalIgnoreCase))
                    targetDir = depot;
                else
                    continue;

                var dest = Path.Combine(targetDir, Path.GetFileName(filePath));
                File.Copy(filePath, dest, overwrite: true);
            }
        }
        catch { /* ignore cleanup failures */ }
        finally { DeleteDirectoryQuietly(workRoot); }
    }

    private async Task WriteAllBytesAutomicallyAsync(string path, byte[] data, CancellationToken ct)
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
