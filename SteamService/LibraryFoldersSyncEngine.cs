using System.Text;

namespace InstallApp.SteamService;

public sealed class LibraryFoldersSyncEngine
{
    private readonly SteamPathsResolver _pathsResolver = new();

    public bool RegisterAppInLibraryFolders(string targetFolderPath, string appId, ulong sizeOnDisk = 0)
    {
        var steamRoot = _pathsResolver.ResolveSteamInstall();
        if (string.IsNullOrEmpty(steamRoot))
            return false;

        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            return false;

        try
        {
            var lines = File.ReadAllLines(vdfPath, Encoding.UTF8);
            var normalizedTarget = NormalizePath(targetFolderPath);

            var updatedLines = SynchronizeVdfLines(lines, normalizedTarget, appId, sizeOnDisk);

            var bakPath = $"{vdfPath}.bak";
            try
            {
                File.Copy(vdfPath, bakPath, overwrite: true);
            }
            catch { /* ignore backup error */ }

            var tempVdf = $"{vdfPath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllLines(tempVdf, updatedLines, Encoding.UTF8);

            if (File.Exists(vdfPath))
                File.Delete(vdfPath);

            File.Move(tempVdf, vdfPath);
            Console.WriteLine($"Successfully updated libraryfolders.vdf for AppId: {appId}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to update libraryfolders.vdf: {ex.Message}");
            return false;
        }
    }

    private static List<string> SynchronizeVdfLines(string[] originalLines, string targetPathNormalized, string appId, ulong sizeOnDisk)
    {
        var result = new List<string>(originalLines);

        int targetSectionStart = -1;
        int currentSectionStart = -1;
        int braceLevel = 0;
        string? currentPathInSection = null;

        for (int i = 0; i < result.Count; i++)
        {
            var line = result[i].Trim();

            if (line.StartsWith("{"))
            {
                braceLevel++;
            }
            else if (line.StartsWith("}"))
            {
                if (braceLevel == 2 && currentSectionStart != -1)
                {
                    if (currentPathInSection != null && NormalizePath(currentPathInSection).Equals(targetPathNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        targetSectionStart = currentSectionStart;
                    }
                    currentSectionStart = -1;
                    currentPathInSection = null;
                }
                braceLevel--;
            }
            else if (braceLevel == 1 && (line.StartsWith("\"0\"") || line.StartsWith("\"1\"") || line.StartsWith("\"2\"") || line.StartsWith("\"3\"") || line.StartsWith("\"4\"") || line.StartsWith("\"5\"")))
            {
                currentSectionStart = i;
            }
            else if (braceLevel == 2 && line.StartsWith("\"path\""))
            {
                var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    currentPathInSection = parts[1].Replace(@"\\", @"\", StringComparison.Ordinal);
                }
            }
        }

        if (targetSectionStart == -1)
        {
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Trim().StartsWith("\"0\""))
                {
                    targetSectionStart = i;
                    break;
                }
            }
        }

        if (targetSectionStart == -1)
            return result;

        int appsBlockHeaderLine = -1;
        int appsBlockOpenBraceLine = -1;
        int appsBlockCloseBraceLine = -1;

        braceLevel = 0;
        bool insideTargetSection = false;

        for (int i = targetSectionStart; i < result.Count; i++)
        {
            var line = result[i].Trim();
            if (line.StartsWith("{"))
            {
                braceLevel++;
                if (insideTargetSection && braceLevel == 2 && appsBlockHeaderLine != -1 && appsBlockOpenBraceLine == -1)
                {
                    appsBlockOpenBraceLine = i;
                }
            }
            else if (line.StartsWith("}"))
            {
                if (insideTargetSection && braceLevel == 2 && appsBlockOpenBraceLine != -1)
                {
                    appsBlockCloseBraceLine = i;
                    break;
                }
                braceLevel--;
                if (braceLevel == 0)
                {
                    break;
                }
            }
            else if (i == targetSectionStart)
            {
                insideTargetSection = true;
            }
            else if (insideTargetSection && braceLevel == 1 && line.StartsWith("\"apps\""))
            {
                appsBlockHeaderLine = i;
            }
        }

        var appLinePattern = $"\"{appId}\"";

        if (appsBlockOpenBraceLine != -1 && appsBlockCloseBraceLine != -1)
        {
            bool updated = false;
            for (int j = appsBlockOpenBraceLine + 1; j < appsBlockCloseBraceLine; j++)
            {
                if (result[j].Contains(appLinePattern))
                {
                    result[j] = $"\t\t\"{appId}\"\t\t\"{sizeOnDisk}\"";
                    updated = true;
                    break;
                }
            }
            if (!updated)
            {
                result.Insert(appsBlockCloseBraceLine, $"\t\t\"{appId}\"\t\t\"{sizeOnDisk}\"");
            }
        }
        else
        {
            int sectionCloseBraceIndex = -1;
            braceLevel = 0;
            for (int i = targetSectionStart; i < result.Count; i++)
            {
                var line = result[i].Trim();
                if (line.StartsWith("{")) braceLevel++;
                else if (line.StartsWith("}"))
                {
                    braceLevel--;
                    if (braceLevel == 0)
                    {
                        sectionCloseBraceIndex = i;
                        break;
                    }
                }
            }

            if (sectionCloseBraceIndex != -1)
            {
                var newAppsBlock = new List<string>
                {
                    "\t\t\"apps\"",
                    "\t\t{",
                    $"\t\t\t\"{appId}\"\t\t\"{sizeOnDisk}\"",
                    "\t\t}"
                };
                result.InsertRange(sectionCloseBraceIndex, newAppsBlock);
            }
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd('\\', '/').Replace(@"\\", @"\", StringComparison.Ordinal);
    }
}
