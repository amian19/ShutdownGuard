namespace ShutdownGuard.Services;

/// <summary>
/// Baked-in GitHub Releases endpoint for out-of-the-box updates.
/// Users only need the exe; they never interact with GitHub.
/// </summary>
public static class AppUpdateEndpoints
{
    public const string GitHubOwner = "amian19";
    public const string GitHubRepo = "ShutdownGuard";
    public const string ReleaseAssetFileName = "ShutdownGuard.exe";

    public static string LatestReleaseApiUrl =>
        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
}
