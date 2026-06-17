using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using TarkovMonitor.StreamerDashboard.Models;
using TarkovMonitor.StreamerDashboard.Services;

namespace TarkovMonitor.StreamerDashboard.Pages;

public record LogEntry(string Time, string EventType);

public sealed partial class DashboardPage : Page
{
    private readonly GrpcEventClient _grpcClient;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<LogEntry> _log = [];

    public DashboardPage()
    {
        InitializeComponent();

        _grpcClient = App.Current.Services.GetRequiredService<GrpcEventClient>();
        _settings = App.Current.Services.GetRequiredService<AppSettings>();

        LogList.ItemsSource = _log;

        var overlayUrl = $"http://localhost:{_settings.OverlayPort}/";
        OverlayUrlLabel.Text = overlayUrl;
        OpenOverlayBtn.NavigateUri = new Uri(overlayUrl);

        // Reflect any state already known when the page opens
        MapText.Text = _grpcClient.CurrentMap ?? "—";
        RaidTypeText.Text = _grpcClient.CurrentRaidType ?? "—";
        ProfileText.Text = _grpcClient.CurrentProfileType ?? "—";

        _grpcClient.EventReceived += OnEventReceived;
        _grpcClient.ConnectionStateChanged += OnConnectionStateChanged;

        Loaded += async (_, _) =>
        {
            await OverlayPreview.EnsureCoreWebView2Async();
            OverlayPreview.Source = new Uri(overlayUrl);
        };

        Unloaded += (_, _) =>
        {
            _grpcClient.EventReceived -= OnEventReceived;
            _grpcClient.ConnectionStateChanged -= OnConnectionStateChanged;
        };
    }

    private void OnEventReceived(object? sender, GameEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            MapText.Text = _grpcClient.CurrentMap ?? "—";
            RaidTypeText.Text = _grpcClient.CurrentRaidType ?? "—";
            ProfileText.Text = _grpcClient.CurrentProfileType ?? "—";

            _log.Insert(0, new LogEntry(DateTime.Now.ToString("HH:mm:ss"), e.EventType));
            while (_log.Count > 50) _log.RemoveAt(_log.Count - 1);
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateArgs e)
    {
        if (!e.IsConnected) return;
        // Reload overlay in WebView2 when the service reconnects
        DispatcherQueue.TryEnqueue(() => OverlayPreview.Reload());
    }
}
