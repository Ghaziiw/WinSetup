using CommunityToolkit.Mvvm.ComponentModel;
using WinSetup.Models;

namespace WinSetup.ViewModels;

public partial class ItemViewModel : ObservableObject
{
    public ItemViewModel(CatalogEntry entry)
    {
        Entry = entry;
        isSelected = entry.Default;
    }

    public CatalogEntry Entry { get; }
    public string Name => Entry.Name;
    public string? Description => Entry.Description;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ItemState state = ItemState.Idle;

    /// <summary>Last message (shown as tooltip on the status text).</summary>
    [ObservableProperty]
    private string? detail;

    public string StatusText => State switch
    {
        ItemState.Queued    => "queued",
        ItemState.Running   => "working...",
        ItemState.Succeeded => "done",
        ItemState.Skipped   => "skipped",
        ItemState.Failed    => "FAILED",
        _                   => "",
    };
}
