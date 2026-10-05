namespace Qtranslator.Desktop;

public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            DesktopErrorLog.Write("Unhandled UI exception", eventArgs.Exception);
            if (MainWindow is MainWindow window)
            {
                window.ReportUnexpectedError(eventArgs.Exception);
                eventArgs.Handled = true;
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            DesktopErrorLog.Write("Unobserved task exception", eventArgs.Exception);
            eventArgs.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            DesktopErrorLog.Write(
                "Fatal application exception",
                eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject.ToString()));
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (e.Args.Contains("--register-bridge", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                BridgeRegistrationService.EnsureRegistered();
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 1;
            }
            Shutdown(Environment.ExitCode);
            return;
        }

        base.OnStartup(e);
    }
}
