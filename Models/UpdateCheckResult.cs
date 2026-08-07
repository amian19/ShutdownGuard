namespace ShutdownGuard.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    public Version? LatestVersion { get; init; }
    public string? DownloadUrl { get; init; }
    public string? ReleaseUrl { get; init; }
    public string? ErrorMessage { get; init; }
    public string? TagName { get; init; }

    public static UpdateCheckResult UpToDate(Version latest) => new()
    {
        Status = UpdateCheckStatus.UpToDate,
        LatestVersion = latest
    };

    public static UpdateCheckResult Available(
        Version latest, string downloadUrl, string? releaseUrl, string? tagName) => new()
    {
        Status = UpdateCheckStatus.UpdateAvailable,
        LatestVersion = latest,
        DownloadUrl = downloadUrl,
        ReleaseUrl = releaseUrl,
        TagName = tagName
    };

    public static UpdateCheckResult Failed(string message) => new()
    {
        Status = UpdateCheckStatus.Failed,
        ErrorMessage = message
    };
}
