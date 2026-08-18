namespace InstallApp.Model;

public class SteamGameInfo : IComparable<string>
{
    public required uint AppId { get; init; }

    public required string DisplayName { get; init; }

    public string? InstallDir { get; init; }

    public string? GameDirectory { get; init; }

    public int CompareTo(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return 1;
        return string.Compare(DisplayName, displayName, StringComparison.OrdinalIgnoreCase);
    }
}
