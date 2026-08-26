namespace InstallApp.Model;

public class SteamGameInfo
{
    public required uint AppId { get; init; }

    public required string DisplayName { get; init; }

    public string? InstallDir { get; init; }

    public string? GameDirectory { get; init; }

    public ulong SizeOnDisk { get; init; }
}
