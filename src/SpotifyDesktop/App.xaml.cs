using System.Threading;
using System.Windows;
using Application = System.Windows.Application;

namespace SpotifyDesktop;

public partial class App : Application
{
    private const string MutexName = "Local\\SpotifyDesktop_SingleInstance_Mutex";
    private const string WakeUpEventName = "Local\\SpotifyDesktop_WakeUp_Event";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _wakeUpEvent;
    private RegisteredWaitHandle? _registeredWaitHandle;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            // Signal the existing instance (even if minimized to System Tray) to restore its window
            try
            {
                using EventWaitHandle existingEvent = EventWaitHandle.OpenExisting(WakeUpEventName);
                existingEvent.Set();
            }
            catch
            {
                // Ignore if event handle is not ready yet
            }

            Shutdown();
            return;
        }

        _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeUpEventName);

        MainWindow mainWindow = new();
        MainWindow = mainWindow;
        mainWindow.Show();

        SessionEnding += (_, _) => mainWindow.AllowSessionEndingClose();

        _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _wakeUpEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.InvokeAsync(mainWindow.RestoreFromTray);
                }
            },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _registeredWaitHandle?.Unregister(null);
        _wakeUpEvent?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if mutex was not owned
            }
            _singleInstanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
