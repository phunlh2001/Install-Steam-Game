namespace InstallApp.Model;

public sealed class BaseResponse<T>
{
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T Data { get; set; } = default!;
}

public sealed class ManifestResponse
{
    public string ManifestUrl { get; set; } = null!;
}

public sealed class ThirdPartyResponse
{
    public string FileUrl { get; set; } = null!;
}
