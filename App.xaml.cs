using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using WinSetup.Services;

namespace WinSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "WinSetup - unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // The manifest already triggers UAC. This is a safety net (e.g. if the manifest is stripped).
        if (!IsAdministrator())
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    UseShellExecute = true,
                    Verb = "runas", // UAC prompt
                });
            }
            catch (Win32Exception) // user clicked "No" on the UAC dialog
            {
                MessageBox.Show("WinSetup needs administrator rights to install software and change system settings.",
                    "WinSetup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
