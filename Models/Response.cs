namespace InstallApp.Models;

public class Response
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? Message { get; set; }
    public ManifestResponse Data { get; set; } = default!;
}

public class ManifestResponse
{
    public string ManifestUrl { get; set; } = null!;
}
