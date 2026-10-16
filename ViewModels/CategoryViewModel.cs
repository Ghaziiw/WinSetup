using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinSetup.Models;

namespace WinSetup.ViewModels;

public partial class CategoryViewModel : ObservableObject
{
    public CategoryViewModel(CatalogCategory category)
    {
        Name = category.Name;
        Kind = category.Kind.ToLowerInvariant();
        foreach (var entry in category.Items)
            Items.Add(new ItemViewModel(entry));
    }

    public string Name { get; }
    public string Kind { get; }
    public ObservableCollection<ItemViewModel> Items { get; } = new();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in Items) i.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var i in Items) i.IsSelected = false;
    }
}
