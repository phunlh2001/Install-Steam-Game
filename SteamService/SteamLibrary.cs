using InstallApp.Model;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ValveKeyValue;

namespace InstallApp.SteamService;

public sealed class SteamLibrary
{
    private static readonly ImmutableHashSet<uint> ExcludedAppIds =
        [228980U, 107056U, 1110390U]; // common redistributables / shader tools

    public IReadOnlyList<SteamGameInfo> ListGames()
    {
        var steamRoot = new SteamPathsResolver().ResolveSteamInstall();
        if (string.IsNullOrEmpty(steamRoot))
            return [];

        var steamappsDirs = CollectSteamAppsDirectories(steamRoot);
        var list = new List<SteamGameInfo>();

        foreach (var dir in steamappsDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var acf in Directory.EnumerateFiles(dir, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                if (TryParseAppManifest(acf, out var game) && !ExcludedAppIds.Contains(game.AppId))
                    list.Add(game);
            }
        }

        list.Sort();
        return list;
    }

    public string? GetGameDirectory(uint appId)
    {
        var steamRoot = new SteamPathsResolver().ResolveSteamInstall();
        if (string.IsNullOrEmpty(steamRoot))
            return null;

        var steamappsDirs = CollectSteamAppsDirectories(steamRoot);
        var acfFileName = $"appmanifest_{appId}.acf";

        foreach (var dir in steamappsDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            var acfPath = Path.Combine(dir, acfFileName);
            if (File.Exists(acfPath) && TryParseAppManifest(acfPath, out var game) && !string.IsNullOrEmpty(game.GameDirectory))
            {
                return game.GameDirectory;
            }
        }

        foreach (var dir in steamappsDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (var acf in Directory.EnumerateFiles(dir, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                if (TryParseAppManifest(acf, out var game) && game.AppId == appId && !string.IsNullOrEmpty(game.GameDirectory))
                {
                    return game.GameDirectory;
                }
            }
        }

        return null;
    }

    public string? GetGameDirectory(string appIdStr)
    {
        if (uint.TryParse(appIdStr?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
        {
            return GetGameDirectory(appId);
        }
        return null;
    }

    private static List<string> CollectSteamAppsDirectories(string steamInstall)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var main = Path.Combine(steamInstall, "steamapps");
        if (Directory.Exists(main))
            set.Add(main);

        var vdf = Path.Combine(main, "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (var libRoot in ReadLibraryRootsFromVdf(vdf))
            {
                var normalized = NormalizeToSteamApps(libRoot);
                if (Directory.Exists(normalized))
                    set.Add(normalized);
            }
        }

        return [.. set];
    }

    private static IEnumerable<string> ReadLibraryRootsFromVdf(string path)
    {
        using var fs = File.OpenRead(path);
        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var root = serializer.Deserialize(fs);
        var acc = new List<string>();
        CollectPathStrings(root, acc);
        foreach (var p in acc)
        {
            if (!string.IsNullOrWhiteSpace(p))
                yield return p.Replace(@"\\", @"\", StringComparison.Ordinal);
        }
    }

    private static void CollectPathStrings(KVObject node, List<string> acc)
    {
        if (node.TryGetValue("path", out var pathChild)
            && pathChild.ValueType == KVValueType.String)
            acc.Add((string)pathChild);

        foreach (var kvp in node)
            CollectPathStrings(kvp.Value, acc);
    }

    private static string NormalizeToSteamApps(string pathFromVdf)
    {
        var p = pathFromVdf.Trim().TrimEnd('\\');
        if (p.EndsWith("steamapps", StringComparison.OrdinalIgnoreCase))
            return p;
        return Path.Combine(p, "steamapps");
    }

    private static bool TryParseAppManifest(string acfPath, [NotNullWhen(true)] out SteamGameInfo? game)
    {
        game = null;
        try
        {
            using var fs = File.OpenRead(acfPath);
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            var root = serializer.Deserialize(fs);
            var app = FindAppState(root);
            if (app is null)
                return false;

            if (!app.TryGetValue("appid", out var idChild))
                return false;

            var idText = idChild.ValueType == KVValueType.String
                ? (string)idChild
                : Convert.ToString(idChild, CultureInfo.InvariantCulture) ?? "";

            if (!uint.TryParse(idText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId))
                return false;

            if (!app.TryGetValue("name", out var nameChild) || nameChild.ValueType != KVValueType.String)
                return false;

            var name = ((string)nameChild).Trim();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string? installDir = null;
            if (app.TryGetValue("installdir", out var installChild) && installChild.ValueType == KVValueType.String)
            {
                installDir = ((string)installChild).Trim();
            }

            var steamappsDir = Path.GetDirectoryName(acfPath);
            string? gameDir = null;

            if (!string.IsNullOrEmpty(steamappsDir))
            {
                var folderName = !string.IsNullOrEmpty(installDir) ? installDir : name;
                gameDir = Path.Combine(steamappsDir, "common", folderName);
            }

            game = new SteamGameInfo
            {
                AppId = appId,
                DisplayName = name,
                InstallDir = installDir,
                GameDirectory = gameDir
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static KVObject? FindAppState(KVObject? node)
    {
        if (node is null)
            return null;

        if (node.TryGetValue("appid", out _) && node.TryGetValue("name", out _))
            return node;

        foreach (var kvp in node)
        {
            var found = FindAppState(kvp.Value);
            if (found is not null)
                return found;
        }

        return null;
    }
}
