using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Checks GitHub Releases for a newer ShutdownGuard.exe and downloads it.
/// </summary>
public sealed class GitHubUpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private readonly string _latestApiUrl;
    private readonly string _assetFileName;
    private readonly Version _currentVersion;

    public GitHubUpdateService(
        string? latestApiUrl = null,
        string? assetFileName = null,
        Version? currentVersion = null)
    {
        _latestApiUrl = latestApiUrl ?? AppUpdateEndpoints.LatestReleaseApiUrl;
        _assetFileName = assetFileName ?? AppUpdateEndpoints.ReleaseAssetFileName;
        _currentVersion = currentVersion ?? AppVersion.Current;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _latestApiUrl);
            using var resp = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if ((int)resp.StatusCode == 404)
                    return UpdateCheckResult.Failed("尚未发布任何版本（GitHub 上还没有 Release）。");

                return UpdateCheckResult.Failed(
                    $"检查更新失败：HTTP {(int)resp.StatusCode}. {Trim(body)}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (!AppVersion.TryParse(tag, out var latest))
                return UpdateCheckResult.Failed($"无法解析版本号：{tag ?? "(空)"}");

            if (!AppVersion.IsNewerThan(latest, _currentVersion))
                return UpdateCheckResult.UpToDate(latest);

            var releaseUrl = root.TryGetProperty("html_url", out var html)
                ? html.GetString()
                : null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return UpdateCheckResult.Failed("最新 Release 没有附件。请上传 ShutdownGuard.exe。");

            string? downloadUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.Equals(name, _assetFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                downloadUrl = asset.TryGetProperty("browser_download_url", out var u)
                    ? u.GetString()
                    : null;
                break;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
                return UpdateCheckResult.Failed(
                    $"最新 Release 中未找到 {_assetFileName}。请把该文件挂到 Release Assets。");

            return UpdateCheckResult.Available(latest, downloadUrl, releaseUrl, tag);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Update check failed: {ex.Message}");
            return UpdateCheckResult.Failed($"检查更新失败：{ex.Message}");
        }
    }

    public async Task DownloadAsync(
        string downloadUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        using var resp = await Http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        // Bypass system/env proxies (e.g. stale Clash 127.0.0.1:7890) so update
        // checks work on machines where a local proxy is configured but not running.
        var handler = new HttpClientHandler { UseProxy = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ShutdownGuard", AppVersion.Display));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = s.Trim();
        return s.Length <= 180 ? s : s[..180] + "…";
    }
}
