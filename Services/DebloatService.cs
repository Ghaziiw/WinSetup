using WinSetup.Models;

namespace WinSetup.Services;

/// <summary>Removes pre-installed UWP/AppX apps for all users and de-provisions them for new users.</summary>
public sealed class DebloatService
{
    private const string Template = """
        $p = '__PATTERN__'
        $failed = 0
        $found = @(Get-AppxPackage -AllUsers -Name $p)
        foreach ($pkg in $found) {
            Write-Output ("Removing " + $pkg.PackageFullName)
            try { Remove-AppxPackage -Package $pkg.PackageFullName -AllUsers -ErrorAction Stop }
            catch { Write-Output ("  failed: " + $_.Exception.Message); $failed++ }
        }
        $prov = @(Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $p })
        foreach ($pp in $prov) {
            Write-Output ("De-provisioning " + $pp.DisplayName)
            try { Remove-AppxProvisionedPackage -Online -PackageName $pp.PackageName -ErrorAction Stop | Out-Null }
            catch { Write-Output ("  failed: " + $_.Exception.Message); $failed++ }
        }
        if ($found.Count -eq 0 -and $prov.Count -eq 0) { exit 2 }
        if ($failed -gt 0) { exit 1 }
        exit 0
        """;

    /// <param name="patterns">One or several AppX name patterns separated by ';'.</param>
    public async Task<StepResult> RemoveAsync(string patterns, Action<string> log, CancellationToken ct)
    {
        int removed = 0, absent = 0, failed = 0;

        foreach (var pattern in patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var script = Template.Replace("__PATTERN__", pattern.Replace("'", "''"));
            var r = await PowerShellHost.RunAsync(script, log, TimeSpan.FromMinutes(10), ct);

            switch (r.ExitCode)
            {
                case 0: removed++; break;
                case 2: absent++; break;
                default: failed++; log($"Pattern '{pattern}' failed (exit {r.ExitCode})"); break;
            }
        }

        if (failed > 0) return StepResult.Fail($"{failed} package pattern(s) could not be removed");
        if (removed > 0) return StepResult.Ok("Removed");
        return StepResult.Skip("Not installed on this PC");
    }
}
