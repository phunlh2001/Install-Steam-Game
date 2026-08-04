namespace InstallApp.SteamServices;

public static class SteamPathsResolver
{
    public static string? ResolveSteamInstall()
    {
        foreach (var sub in new[] { @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" })
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(sub);
            var path = key?.GetValue("InstallPath") as string;
            path = path?.Trim().TrimEnd('\\');
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                return path;
        }

        return null;
    }

    public static string? ResolveStPluginFolder()
    {
        var steam = ResolveSteamInstall();
        if (string.IsNullOrEmpty(steam))
            return null;

        return Path.Combine(steam, "config", "stplug-in");
    }

    public static string? ResolveDepotCacheFolder()
    {
        var steam = ResolveSteamInstall();
        if (string.IsNullOrEmpty(steam))
            return null;

        return Path.Combine(steam, "config", "depotcache");
    }

    public static string DefaultStPlugin(string steamRoot) =>
        Path.Combine(steamRoot, "config", "stplug-in");

    public static string DefaultDepotCache(string steamRoot) =>
        Path.Combine(steamRoot, "config", "depotcache");
}
