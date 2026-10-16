using System.Collections.Specialized;
using System.Windows;
using WinSetup.ViewModels;

namespace WinSetup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        // keep the log scrolled to the newest line
        vm.LogLines.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        };
    }
}
