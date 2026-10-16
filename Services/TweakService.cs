using Microsoft.Win32;
using WinSetup.Models;

namespace WinSetup.Services;

/// <summary>Privacy / telemetry tweaks. Everything is policy-based or a documented service/task toggle.</summary>
public sealed class TweakService
{
    private delegate Task<StepResult> Tweak(Action<string> log, CancellationToken ct);
    private sealed record RegValue(string Path, string Name, int Data);

    private readonly Dictionary<string, Tweak> _tweaks;

    public TweakService()
    {
        _tweaks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["restore-point"] = RestorePointAsync,

            ["telemetry-policy"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1)),

            ["telemetry-services"] = (log, ct) => DisableServicesAsync(log, ct, "DiagTrack", "dmwappushservice"),

            ["telemetry-tasks"] = (log, ct) => DisableTasksAsync(log, ct,
                @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
                @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                @"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector"),

            ["advertising-id"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1)),

            ["consumer-features"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1),
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableCloudOptimizedContent", 1)),

            ["activity-history"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)),

            ["bing-search"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1)),

            ["error-reporting"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\Windows Error Reporting", "Disabled", 1)),

            ["copilot-recall"] = (log, _) => SetPoliciesAsync(log,
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
                new RegValue(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1)),
        };
    }

    public Task<StepResult> ApplyAsync(string key, Action<string> log, CancellationToken ct) =>
        _tweaks.TryGetValue(key, out var tweak)
            ? tweak(log, ct)
            : Task.FromResult(StepResult.Fail($"Unknown tweak '{key}' (check catalog.json)"));

    // ---- implementations -------------------------------------------------------------------

    private static async Task<StepResult> RestorePointAsync(Action<string> log, CancellationToken ct)
    {
        const string script = """
            try {
                Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction Stop
                Checkpoint-Computer -Description 'WinSetup' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
                exit 0
            } catch { Write-Output $_.Exception.Message; exit 1 }
            """;
        var r = await PowerShellHost.RunAsync(script, log, TimeSpan.FromMinutes(5), ct);
        return r.ExitCode == 0
            ? StepResult.Ok("Restore point created")
            : StepResult.Fail("Could not create a restore point (Windows allows one per 24h by default) - continuing");
    }

    private static Task<StepResult> SetPoliciesAsync(Action<string> log, params RegValue[] values) =>
        Task.Run(() =>
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (var v in values)
            {
                using var key = hklm.CreateSubKey(v.Path, writable: true);
                key.SetValue(v.Name, v.Data, RegistryValueKind.DWord);
                log($"HKLM\\{v.Path}\\{v.Name} = {v.Data}");
            }
            return StepResult.Ok("Applied");
        });

    private static async Task<StepResult> DisableServicesAsync(Action<string> log, CancellationToken ct, params string[] services)
    {
        var failed = new List<string>();
        foreach (var svc in services)
        {
            await ProcessRunner.RunAsync("sc.exe", $"stop {svc}", log, TimeSpan.FromMinutes(1), ct); // may already be stopped
            var r = await ProcessRunner.RunAsync("sc.exe", $"config {svc} start= disabled", log, TimeSpan.FromMinutes(1), ct);
            if (r.ExitCode == 1060) log($"Service {svc} does not exist on this Windows build - skipped");
            else if (r.ExitCode != 0) failed.Add(svc);
        }
        return failed.Count == 0 ? StepResult.Ok("Services disabled") : StepResult.Fail("Failed: " + string.Join(", ", failed));
    }

    private static async Task<StepResult> DisableTasksAsync(Action<string> log, CancellationToken ct, params string[] tasks)
    {
        int disabled = 0;
        foreach (var task in tasks)
        {
            var r = await ProcessRunner.RunAsync("schtasks.exe", $"/Change /TN \"{task}\" /DISABLE", log, TimeSpan.FromMinutes(1), ct);
            if (r.ExitCode == 0) disabled++;
            else log($"Task not found / not changed: {task}");
        }
        return disabled > 0 ? StepResult.Ok($"{disabled} task(s) disabled") : StepResult.Skip("No matching tasks on this Windows build");
    }
}
