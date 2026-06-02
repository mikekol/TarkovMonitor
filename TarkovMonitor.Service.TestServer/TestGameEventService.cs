using Grpc.Core;
using TarkovMonitor.Service.Contracts;
using System.Collections.Concurrent;

namespace TarkovMonitor.Service.TestServer;

public class TestGameEventService : TarkovMonitorService.TarkovMonitorServiceBase
{
    private readonly ConcurrentBag<IServerStreamWriter<GameEvent>> _activeStreams = new();

    public int ActiveStreamCount => _activeStreams.Count;

    public override async Task SubscribeToGameEvents(
        SubscriptionRequest request,
        IServerStreamWriter<GameEvent> responseStream,
        ServerCallContext context)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected (total: {_activeStreams.Count + 1})");
        _activeStreams.Add(responseStream);

        try
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, context.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client disconnected (total: {_activeStreams.Count - 1})");
        }
    }

    public override async Task<ServiceConfig> GetConfig(GetConfigRequest request, ServerCallContext context)
    {
        return new ServiceConfig
        {
            CustomLogsPath = "",
            TarkovTrackerToken = "",
            TarkovTrackerEnabled = false
        };
    }

    public override async Task<UpdateConfigResponse> UpdateConfig(UpdateConfigRequest request, ServerCallContext context)
    {
        return new UpdateConfigResponse { Success = true };
    }

    public override async Task<ServiceStatus> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        return new ServiceStatus
        {
            IsGameWatcherRunning = true,
            IsLogMonitorActive = true,
            UptimeSeconds = (long)DateTime.Now.TimeOfDay.TotalSeconds
        };
    }

    public void BroadcastEvent(string eventType, string data)
    {
        var protoEvent = new GameEvent
        {
            EventType = eventType,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = { { "payload", data } }
        };

        var inactiveStreams = new List<IServerStreamWriter<GameEvent>>();

        foreach (var stream in _activeStreams)
        {
            try
            {
                stream.WriteAsync(protoEvent).Wait(TimeSpan.FromSeconds(2));
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] → {eventType}: {data}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ Failed to broadcast: {ex.Message}");
                inactiveStreams.Add(stream);
            }
        }

        foreach (var stream in inactiveStreams)
        {
            _activeStreams.TryTake(out _);
        }
    }
}
