namespace InstallApp.Model;

public class SteamLibraryFolder
{
    public required string Index { get; init; }

    public required string Path { get; init; }

    public string SteamAppsPath => System.IO.Path.Combine(Path, "steamapps");

    public Dictionary<string, string> Apps { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
