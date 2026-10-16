namespace WinSetup.Models;

public enum ItemState { Idle, Queued, Running, Succeeded, Skipped, Failed }

/// <summary>Outcome of one install / removal / tweak step.</summary>
public sealed record StepResult(ItemState State, string Message)
{
    public static StepResult Ok(string message) => new(ItemState.Succeeded, message);
    public static StepResult Skip(string message) => new(ItemState.Skipped, message);
    public static StepResult Fail(string message) => new(ItemState.Failed, message);
}
