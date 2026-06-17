using System.Diagnostics;
using TarkovMonitor.StreamerDashboard.Models;

namespace TarkovMonitor.StreamerDashboard.Services;

public sealed class EventActionService
{
    private readonly GrpcEventClient _grpcClient;
    private readonly OverlayWebServer _overlayServer;
    private readonly AppSettings _settings;

    public EventActionService(GrpcEventClient grpcClient, OverlayWebServer overlayServer, AppSettings settings)
    {
        _grpcClient = grpcClient;
        _overlayServer = overlayServer;
        _settings = settings;
        _grpcClient.EventReceived += OnEventReceived;
    }

    private void OnEventReceived(object? sender, GameEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            await _overlayServer.BroadcastEventAsync(e.EventType, e.Data);

            foreach (var action in _settings.Actions)
            {
                if (action.EventType != e.EventType) continue;
                if (string.IsNullOrWhiteSpace(action.CommandLine)) continue;

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = action.CommandLine,
                        Arguments = action.CommandArgs ?? string.Empty,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[EventAction] Failed to run command for {e.EventType}: {ex.Message}");
                }
            }
        });
    }
}
