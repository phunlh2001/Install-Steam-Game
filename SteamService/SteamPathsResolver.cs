using Microsoft.Win32;

namespace InstallApp.SteamService;

public class SteamPathsResolver
{
    private const string SteamRootPath = @"HKEY_CURRENT_USER\Software\Valve\Steam";

    public string? ResolveSteamInstall()
    {
        foreach (var sub in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            using var key = Registry.LocalMachine.OpenSubKey(sub);
            var path = key?.GetValue("InstallPath") as string;
            path = path?.Trim().TrimEnd('\\');
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                return path;
        }

        return null;
    }

    public string? ResolveStPluginFolder()
    {
        var steam = ResolveSteamInstall();
        if (string.IsNullOrEmpty(steam))
            return null;

        return Path.Combine(steam, "config", "stplug-in");
    }

    public string? ResolveDepotCacheFolder()
    {
        var steam = ResolveSteamInstall();
        if (string.IsNullOrEmpty(steam))
            return null;

        return Path.Combine(steam, "config", "depotcache");
    }

    public string DefaultStPlugin()
    {
        var stPath = Registry.GetValue(SteamRootPath, "SteamPath", "") as string;
        return Path.Combine(stPath!, "config", "stplug-in");
    }

    public string DefaultDepotCache()
    {
        var stPath = Registry.GetValue(SteamRootPath, "SteamPath", "") as string;
        return Path.Combine(stPath!, "config", "depotcache");
    }
}
