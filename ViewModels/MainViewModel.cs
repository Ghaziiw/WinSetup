using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinSetup.Models;
using WinSetup.Services;

namespace WinSetup.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly WingetService _winget = new();
    private readonly GitHubReleaseService _github = new();
    private readonly DebloatService _debloat = new();
    private readonly TweakService _tweaks = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "Ready";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool isBusy;

    public bool IsIdle => !IsBusy;

    public MainViewModel()
    {
        Logger.LineLogged += AppendLog;
        Logger.Info($"WinSetup started. Log file: {Logger.FilePath}");
        LoadCatalog();
    }

    // ---------------------------------------------------------------------------------------

    private void LoadCatalog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "catalog.json");
            var json = File.ReadAllText(path);
            var catalog = JsonSerializer.Deserialize<Catalog>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new Catalog();

            foreach (var c in catalog.Categories)
                Categories.Add(new CategoryViewModel(c));
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load Assets/catalog.json", ex);
            MessageBox.Show($"Could not load catalog.json:\n{ex.Message}", "WinSetup",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AppendLog(string line)
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(new Action(() =>
        {
            LogLines.Add(line);
            if (LogLines.Count > 3000) LogLines.RemoveAt(0);
        }));
    }

    // ---------------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StartAsync()
    {
        var queue = Categories
            .SelectMany(c => c.Items.Where(i => i.IsSelected).Select(i => (Category: c, Item: i)))
            .OrderBy(x => x.Item.Entry.Id == "restore-point" ? 0 : 1) // stable sort: restore point goes first
            .ToList();

        if (queue.Count == 0)
        {
            MessageBox.Show("Nothing is selected.", "WinSetup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var c in Categories)
            foreach (var i in c.Items) { i.State = ItemState.Idle; i.Detail = null; }
        foreach (var q in queue) q.Item.State = ItemState.Queued;

        IsBusy = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        bool cancelled = false;

        try
        {
            // winget is only needed when at least one app is selected
            bool needsWinget = queue.Any(q => q.Category.Kind == "app");
            bool wingetOk = false;
            if (needsWinget)
            {
                ProgressText = "Checking winget...";
                wingetOk = await _winget.EnsureAvailableAsync(Logger.Info, ct);
            }

            int done = 0;
            foreach (var (category, item) in queue)
            {
                if (ct.IsCancellationRequested)
                {
                    item.State = ItemState.Skipped;
                    item.Detail = "Cancelled";
                    cancelled = true;
                    continue;
                }

                ProgressText = $"({done + 1}/{queue.Count}) {item.Name}";
                Progress = done * 100.0 / queue.Count;
                item.State = ItemState.Running;
                Logger.Info($"=== {item.Name} ===");

                try
                {
                    var result = await RunItemAsync(category, item.Entry, wingetOk, ct);
                    item.State = result.State;
                    item.Detail = result.Message;
                }
                catch (OperationCanceledException)
                {
                    item.State = ItemState.Skipped;
                    item.Detail = "Cancelled";
                    cancelled = true;
                }
                catch (Exception ex) // one broken item must never stop the whole run
                {
                    item.State = ItemState.Failed;
                    item.Detail = ex.Message;
                    Logger.Error($"{item.Name} failed", ex);
                }

                Logger.Info($"[{item.State}] {item.Name}: {item.Detail}");
                done++;
                Progress = done * 100.0 / queue.Count;
            }

            ProgressText = cancelled ? "Cancelled" : "Finished";
            ShowSummary(queue.Select(q => q.Item).ToList(), cancelled,
                needsRestart: queue.Any(q => q.Category.Kind != "app"));
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        Logger.Warn("Cancel requested - stopping after the current process is killed.");
        _cts?.Cancel();
    }

    // ---------------------------------------------------------------------------------------

    private Task<StepResult> RunItemAsync(CategoryViewModel category, CatalogEntry entry, bool wingetOk, CancellationToken ct) =>
        category.Kind switch
        {
            "app"     => InstallAppAsync(entry, wingetOk, ct),
            "debloat" => _debloat.RemoveAsync(entry.Id, Logger.Info, ct),
            "tweak"   => _tweaks.ApplyAsync(entry.Id, Logger.Info, ct),
            _         => Task.FromResult(StepResult.Fail($"Unknown category kind '{category.Kind}'")),
        };

    private async Task<StepResult> InstallAppAsync(CatalogEntry entry, bool wingetOk, CancellationToken ct)
    {
        // No winget package: just open the official download page
        if (string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Url))
        {
            Process.Start(new ProcessStartInfo(entry.Url) { UseShellExecute = true });
            return StepResult.Skip("No winget package - download page opened in your browser");
        }

        StepResult? wingetResult = null;

        if (wingetOk && !string.IsNullOrWhiteSpace(entry.Id))
        {
            wingetResult = await _winget.InstallAsync(entry.Id, entry.Source, Logger.Info, ct);
            if (wingetResult.State != ItemState.Failed) return wingetResult;
            Logger.Warn($"winget failed for {entry.Name}: {wingetResult.Message}");
        }

        if (entry.GitHub is not null)
        {
            Logger.Info("Trying the GitHub release fallback...");
            return await _github.InstallLatestAsync(entry.GitHub, Logger.Info, ct);
        }

        return wingetResult ?? StepResult.Fail("winget is unavailable and no fallback is configured");
    }

    private static void ShowSummary(List<ItemViewModel> items, bool cancelled, bool needsRestart)
    {
        var failed = items.Where(i => i.State == ItemState.Failed).ToList();
        var ok = items.Count(i => i.State == ItemState.Succeeded);
        var skipped = items.Count(i => i.State == ItemState.Skipped);

        var sb = new StringBuilder();
        sb.AppendLine(cancelled ? "Run cancelled." : "Run finished.");
        sb.AppendLine();
        sb.AppendLine($"Succeeded: {ok}");
        sb.AppendLine($"Skipped / already present: {skipped}");
        sb.AppendLine($"Failed: {failed.Count}");

        if (failed.Count > 0)
        {
            sb.AppendLine().AppendLine("Failed items:");
            foreach (var f in failed) sb.AppendLine($"  - {f.Name}: {f.Detail}");
        }

        if (needsRestart) sb.AppendLine().AppendLine("A restart is recommended to finish removals and tweaks.");
        sb.AppendLine().Append($"Log: {Logger.FilePath}");

        MessageBox.Show(sb.ToString(), "WinSetup", MessageBoxButton.OK,
            failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
}
