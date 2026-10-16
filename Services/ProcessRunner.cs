using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace WinSetup.Services;

public sealed record ProcessResult(int ExitCode, bool TimedOut);

/// <summary>Runs a child process without blocking the UI and streams its output line by line.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        Action<string>? onLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };

        void Emit(string? raw)
        {
            if (onLine is null || string.IsNullOrWhiteSpace(raw)) return;
            var t = raw.Trim();
            // Skip winget's spinner / progress-bar noise
            if (t.Length <= 2 && "-\\|/".Contains(t[0])) return;
            if (t.Contains('█') || t.Contains('▒')) return;
            onLine(t);
        }

        proc.OutputDataReceived += (_, e) => Emit(e.Data);
        proc.ErrorDataReceived += (_, e) => Emit(e.Data);

        proc.Start(); // throws Win32Exception if the executable does not exist
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await proc.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return new ProcessResult(proc.ExitCode, false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;      // user pressed Cancel
            return new ProcessResult(-1, TimedOut: true); // our own timeout
        }
    }
}

public static class PowerShellHost
{
    /// <summary>Runs a script in Windows PowerShell 5.1 (present on every Windows install).</summary>
    public static Task<ProcessResult> RunAsync(
        string script, Action<string>? onLine = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        // $ProgressPreference avoids noisy CLIXML progress records on stderr
        var full = "$ProgressPreference='SilentlyContinue'\n" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return ProcessRunner.RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            onLine, timeout, ct);
    }
}
