using System.Windows;

namespace Softkeys;

/// <summary>
/// WPF application with the single-instance guard (§4.1). The main window is
/// created in code rather than via StartupUri so startup order is explicit.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(
            initiallyOwned: true, @"Local\softkeys-single-instance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
