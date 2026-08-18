namespace InstallApp.AppService;

public static class Constants
{
    public const string BaseApiUrl = "https://centrixg.onrender.com/api";

    public static class GameType
    {
        public const string UBISOFT = "Ubisoft";
        public const string ROCKSTAR = "Rockstar";
        public const string EA = "EA";
    }

    public static class Endpoints
    {
        public const string ThirdPartyUbisoft = "/third-party/ubisoft";
        public const string ThirdPartyRockstar = "/third-party/rockstar";
        public const string Manifest = "/manifest/{0}";
    }

    public static class ThirdPartyFiles
    {
        public const string UbisoftTargetDll = "uplay_r1_loader68.dll";
    }
}
