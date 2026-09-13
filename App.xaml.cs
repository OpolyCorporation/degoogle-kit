using System.Linq;
using System.Threading;
using System.Windows;
using DeGoogleKit.Services;

namespace DeGoogleKit;

public partial class App : Application
{
    private static Mutex? _instance;
    public static string? LaunchPayload { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ProtocolRegistration.EnsureCurrentUser();

        var payload = e.Args.FirstOrDefault(ProtocolRegistration.LooksLikePayload);
        _instance = new Mutex(true, @"Local\DeGoogleKit.SingleInstance", out var created);
        if (!created)
        {
            if (!string.IsNullOrWhiteSpace(payload))
                ProtocolRegistration.WritePending(payload);
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }

        if (!string.IsNullOrWhiteSpace(payload))
            LaunchPayload = payload;

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "DeGoogle Kit", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!PrivacyStore.HasValidConsent())
        {
            var first = new FirstRunWindow();
            if (first.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var main = new MainWindow();
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
