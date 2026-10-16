using System.ComponentModel;
using WinSetup.Models;

namespace WinSetup.Services;

public sealed class WingetService
{
    // winget exit codes (HRESULTs)
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    private const int NoApplicableUpgrade     = unchecked((int)0x8A15002B);

    public bool IsAvailable { get; private set; }

    /// <summary>Makes sure winget works; tries to register App Installer on a fresh Windows if it does not.</summary>
    public async Task<bool> EnsureAvailableAsync(Action<string> log, CancellationToken ct)
    {
        if (!await ProbeAsync(ct))
        {
            log("winget not found - trying to register App Installer...");
            await PowerShellHost.RunAsync(
                "Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
                log, TimeSpan.FromMinutes(5), ct);

            if (!await ProbeAsync(ct))
            {
                log("winget is still unavailable. Open Microsoft Store > Library > update 'App Installer', then retry.");
                return false;
            }
        }

        // Refresh the package index so we get the newest manifests
        log("Refreshing winget sources...");
        await ProcessRunner.RunAsync("winget", "source update --disable-interactivity", log,
            TimeSpan.FromMinutes(3), ct);
        return true;
    }

    private async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            var r = await ProcessRunner.RunAsync("winget", "--version", null, TimeSpan.FromSeconds(30), ct);
            IsAvailable = r is { ExitCode: 0, TimedOut: false };
        }
        catch (Win32Exception) { IsAvailable = false; }
        return IsAvailable;
    }

    public async Task<StepResult> InstallAsync(string id, string? source, Action<string> log, CancellationToken ct)
    {
        var src = string.IsNullOrWhiteSpace(source) ? "winget" : source;
        var args = $"install --id {id} --exact --source {src} --silent " +
                   "--accept-package-agreements --accept-source-agreements --disable-interactivity";

        log($"> winget {args}");
        var r = await ProcessRunner.RunAsync("winget", args, log, TimeSpan.FromMinutes(45), ct);

        if (r.TimedOut) return StepResult.Fail("Timed out after 45 minutes");

        return r.ExitCode switch
        {
            0 => StepResult.Ok("Installed (latest version)"),
            PackageAlreadyInstalled or NoApplicableUpgrade => StepResult.Skip("Already installed / up to date"),
            _ => StepResult.Fail($"winget exit code 0x{r.ExitCode:X8}"),
        };
    }
}
