namespace InstallApp.Models;
public sealed class SteamGameInfo : IComparable<SteamGameInfo>
{
    public uint AppId { get; set; }
    public string DisplayName { get; set; } = null!;

    public int CompareTo(SteamGameInfo? other)
    {
        if (other is null) return 1;
        return string.Compare(DisplayName, other.DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
