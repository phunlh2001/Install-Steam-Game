using System.Text;

namespace InstallApp.SteamService;

public sealed class ManifestGenerator
{
    public bool GenerateManifestIfMissing(
        string targetSteamAppsDir,
        string appId,
        string displayName,
        string installDir,
        ulong sizeOnDisk,
        IDictionary<string, string>? installedDepots = null)
    {
        if (string.IsNullOrWhiteSpace(targetSteamAppsDir) || string.IsNullOrWhiteSpace(appId))
            return false;

        if (!Directory.Exists(targetSteamAppsDir))
            Directory.CreateDirectory(targetSteamAppsDir);

        var acfPath = Path.Combine(targetSteamAppsDir, $"appmanifest_{appId}.acf");

        // CRITICAL DIRECTIVE: If appmanifest_<appId>.acf already exists, skip generation!
        if (File.Exists(acfPath))
        {
            Console.WriteLine($"appmanifest_{appId}.acf already exists at {acfPath}. Skipping generation.");
            return false;
        }

        var sb = new StringBuilder();
        sb.AppendLine("\"AppState\"");
        sb.AppendLine("{");
        sb.AppendLine($"\t\"appid\"\t\t\"{appId}\"");
        sb.AppendLine("\t\"Universe\"\t\t\"1\"");
        sb.AppendLine($"\t\"name\"\t\t\"{EscapeVdfString(displayName)}\"");
        sb.AppendLine("\t\"StateFlags\"\t\t\"4\"");
        sb.AppendLine($"\t\"installdir\"\t\t\"{EscapeVdfString(installDir)}\"");
        sb.AppendLine($"\t\"LastUpdated\"\t\t\"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\"");
        sb.AppendLine("\t\"UpdateResult\"\t\t\"0\"");
        sb.AppendLine("\t\"BytesToDownload\"\t\t\"0\"");
        sb.AppendLine("\t\"BytesDownloaded\"\t\t\"0\"");
        sb.AppendLine("\t\"AutoUpdateBehavior\"\t\t\"1\"");
        sb.AppendLine($"\t\"SizeOnDisk\"\t\t\"{sizeOnDisk}\"");

        sb.AppendLine("\t\"InstalledDepots\"");
        sb.AppendLine("\t{");
        if (installedDepots != null && installedDepots.Count > 0)
        {
            foreach (var (depotId, manifestId) in installedDepots)
            {
                sb.AppendLine($"\t\t\"{depotId}\"");
                sb.AppendLine("\t\t{");
                if (!string.IsNullOrWhiteSpace(manifestId))
                {
                    sb.AppendLine($"\t\t\t\"manifest\"\t\t\"{manifestId}\"");
                }
                sb.AppendLine("\t\t}");
            }
        }
        sb.AppendLine("\t}");
        sb.AppendLine("}");

        WriteAllTextAtomically(acfPath, sb.ToString());
        Console.WriteLine($"Successfully generated appmanifest_{appId}.acf at {acfPath}");
        return true;
    }

    private static string EscapeVdfString(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void WriteAllTextAtomically(string path, string content)
    {
        var folder = Path.GetDirectoryName(path) ?? "";
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, content, Encoding.UTF8);

        if (File.Exists(path))
            File.Delete(path);

        File.Move(tempPath, path);
    }
}
