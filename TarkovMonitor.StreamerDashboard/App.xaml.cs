using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using TarkovMonitor.StreamerDashboard.Models;
using TarkovMonitor.StreamerDashboard.Services;

namespace TarkovMonitor.StreamerDashboard;

public partial class App : Application
{
    public static string LogFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "TarkovMonitor.StreamerDashboard", "startup.log");

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, line + Environment.NewLine);
        }
        catch { }
    }

    public static new App Current => (App)Application.Current;
    public IServiceProvider Services { get; }

    public App()
    {
        UnhandledException += (_, e) =>
        {
            Log($"UNHANDLED: {e.Exception}");
            e.Handled = true; // keep alive so we can read the log
        };

        Log("App() ctor start");
        InitializeComponent();
        Services = BuildServices();
        Log("App() ctor done");
    }

    private static IServiceProvider BuildServices()
    {
        Log("BuildServices start");
        var settings = AppSettings.Load();
        var services = new ServiceCollection();

        services.AddSingleton(settings);
        services.AddSingleton(new GrpcEventClient(settings.GrpcAddress));
        services.AddSingleton(new OverlayWebServer(settings.OverlayPort));
        services.AddSingleton<EventActionService>();

        var provider = services.BuildServiceProvider();
        Log("BuildServices done");
        return provider;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log("OnLaunched: creating MainWindow");
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
        Log("OnLaunched: window activated, starting services");
        _ = StartServicesAsync();
    }

    private MainWindow? _mainWindow;

    private async Task StartServicesAsync()
    {
        try
        {
            Log("StartServicesAsync: starting overlay server");
            var overlayServer = Services.GetRequiredService<OverlayWebServer>();
            await overlayServer.StartAsync();
            Log("StartServicesAsync: overlay server started");

            var grpcClient = Services.GetRequiredService<GrpcEventClient>();
            grpcClient.Start();
            Log("StartServicesAsync: gRPC client started");

            _ = Services.GetRequiredService<EventActionService>();
            Log("StartServicesAsync: EventActionService ready");
        }
        catch (Exception ex)
        {
            Log($"StartServicesAsync FAILED: {ex}");
        }
    }
}
