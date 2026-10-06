using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace AIControl;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\Ronny.AIControl", out var first);
        if (!first)
        {
            using var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName)) using (process)
                if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                { ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); break; }
            Shutdown(); return;
        }
        try { MainWindow = new MainWindow(SettingsStore.Load()); MainWindow.Show(); }
        catch (Exception ex) { MessageBox.Show(ex.Message + "\n\nEinstellungen: " + SettingsStore.SettingsPath, "AI Control", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
