using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinSetup.Models;

namespace WinSetup.Services;

/// <summary>Fallback installer: queries the GitHub Releases API at runtime for the newest asset.</summary>
public sealed class GitHubReleaseService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WinSetup/1.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN"); // optional: higher rate limit
        if (!string.IsNullOrEmpty(token))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public async Task<StepResult> InstallLatestAsync(GitHubFallback gh, Action<string> log, CancellationToken ct)
    {
        log($"Querying latest GitHub release of {gh.Repo}...");
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{gh.Repo}/releases/latest", ct)
                             .ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "?";

        var rx = new Regex(gh.AssetPattern, RegexOptions.IgnoreCase);
        string? name = null, url = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var n = asset.GetProperty("name").GetString() ?? "";
            if (!rx.IsMatch(n)) continue;
            name = n;
            url = asset.GetProperty("browser_download_url").GetString();
            break;
        }
        if (name is null || url is null)
            return StepResult.Fail($"No asset matching '{gh.AssetPattern}' in release {tag}");

        var dir = Path.Combine(Path.GetTempPath(), "WinSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);

        try
        {
            log($"Downloading {name} ({tag})...");
            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(file);
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            var timeout = TimeSpan.FromMinutes(15);
            ProcessResult r;
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".msi":
                    r = await ProcessRunner.RunAsync("msiexec.exe", $"/i \"{file}\" /qn /norestart", log, timeout, ct);
                    break;
                case ".msix" or ".msixbundle" or ".appx":
                    r = await PowerShellHost.RunAsync($"Add-AppxPackage -Path '{file}'", log, timeout, ct);
                    break;
                default:
                    r = await ProcessRunner.RunAsync(file, gh.SilentArgs ?? "/S", log, timeout, ct);
                    break;
            }

            return r.ExitCode is 0 or 3010 // 3010 = success, reboot required
                ? StepResult.Ok($"Installed {tag} from GitHub")
                : StepResult.Fail($"Installer exit code {r.ExitCode}");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp cleanup is best-effort */ }
        }
    }
}
