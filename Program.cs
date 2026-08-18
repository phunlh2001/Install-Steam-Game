using InstallApp;
using InstallApp.AppService;

if (args.Length < 2)
{
    Console.WriteLine("Missing arguments");
    return;
}

var token = args[0];
var appId = args[1];
var type = args.Length > 2 ? args[2] : null;

var installer = new Installer();
var thirdPartyService = new ThirdPartyService(installer);
var manifestService = new ManifestService(installer);
var appRunner = new AppRunner(thirdPartyService, manifestService);

await appRunner.RunAsync(token, appId, type);
