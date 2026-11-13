using System.Runtime.InteropServices;
using AudioSwap.Config;
using AudioSwap.UI;

namespace AudioSwap;

internal static class Program
{
    const string InstanceMutexName = @"Local\AudioSwap.Instance";
    const string ShowWindowEventName = @"Local\AudioSwap.ShowWindow";
    const string QuitEventName = @"Local\AudioSwap.Quit";

    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        if (DevTools.TryRun(args, out int exitCode)) return exitCode;

        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        if (!isFirstInstance)
        {
            if (Has("--quit"))
            {
                quit.Set();
                return 0;
            }
            // Already running (maybe with its tray icon hidden): bring its window up instead.
            AllowSetForegroundWindow(ASFW_ANY);
            showWindow.Set();
            return 0;
        }
        if (Has("--quit")) return 0;

        Theme.Watch();
        var config = ConfigStore.Load(out var loadError);
        // Launched by Windows at sign-in: start quietly in the tray.
        using var app = new TrayApp(config, loadError, showWindow: !Has("--startup"));

        var ui = SynchronizationContext.Current!;
        var showRegistration = ThreadPool.RegisterWaitForSingleObject(showWindow,
            (_, _) => ui.Post(_ => app.ShowWindow(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        var quitRegistration = ThreadPool.RegisterWaitForSingleObject(quit,
            (_, _) => ui.Post(_ => app.Quit(), null), null, Timeout.Infinite, executeOnlyOnce: true);

        Application.Run(app);
        showRegistration.Unregister(null);
        quitRegistration.Unregister(null);
        Theme.Unwatch();
        return 0;
    }

    const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);
}
