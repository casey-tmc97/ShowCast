using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShowCast.Core;

public record UpdateInfo(string Version, string AssetName, string DownloadUrl)
{
    public string InstallerPath => Path.Combine(Path.GetTempPath(), AssetName);
}

public static class UpdateChecker
{
    static readonly HttpClient _http = new();
    const string ApiUrl = "https://api.github.com/repos/casey-tmc97/ShowCast/releases/latest";

    static UpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ShowCast",
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0"));
    }

    // Throws on network/HTTP failure; returns null when already up to date.
    public static async Task<UpdateInfo?> CheckAsync()
    {
        var json    = await _http.GetStringAsync(ApiUrl);
        var current = Assembly.GetExecutingAssembly().GetName().Version!;
        return ParseRelease(json, current);
    }

    // Internal so tests can call it without touching the network.
    internal static UpdateInfo? ParseRelease(string json, Version currentVersion)
    {
        using var doc  = JsonDocument.Parse(json);
        var root       = doc.RootElement;
        var tag        = root.GetProperty("tag_name").GetString() ?? "";
        var versionStr = tag.TrimStart('v');

        if (!Version.TryParse(versionStr, out var latest)) return null;
        if (!IsNewer(latest, currentVersion)) return null;

        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(a =>
                a.GetProperty("name").GetString()?.EndsWith("-win-x64-setup.exe") == true);

        if (asset.ValueKind == JsonValueKind.Undefined) return null;

        var name = asset.GetProperty("name").GetString()!;
        var url  = asset.GetProperty("browser_download_url").GetString()!;
        return new UpdateInfo(versionStr, name, url);
    }

    // Internal so tests can call it directly.
    internal static bool IsNewer(Version latest, Version current)
    {
        if (latest.Major != current.Major) return latest.Major > current.Major;
        if (latest.Minor != current.Minor) return latest.Minor > current.Minor;
        return latest.Build > current.Build;
    }

    public static async Task DownloadAsync(
        UpdateInfo info, IProgress<double> progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file   = new FileStream(
            info.InstallerPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);

        try
        {
            var buffer    = new byte[81920];
            long received = 0;
            int  read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (total > 0) progress.Report((double)received / total);
            }
        }
        catch
        {
            await file.DisposeAsync();
            try { File.Delete(info.InstallerPath); } catch { /* ignore */ }
            throw;
        }
    }
}
