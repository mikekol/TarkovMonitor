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
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Client connected: {request.ClientAgent} (total: {_activeStreams.Count + 1})");
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

    // Waits until at least one client is connected, up to timeoutMs
    public async Task<bool> WaitForClientAsync(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (_activeStreams.Count > 0) return true;
            await Task.Delay(200);
        }
        return false;
    }

    // Sends a proper RaidStarting then RaidStarted sequence to reproduce the double-sound bug.
    // For a scav raid where RaidStarting fires, StartingTime must be propagated to RaidStarted.
    public async Task BroadcastRaidSequenceAsync(string raidType = "Scav")
    {
        var now = DateTimeOffset.UtcNow;
        var startingTimeMs = now.ToUnixTimeMilliseconds().ToString();
        var startedTimeMs = now.AddSeconds(3).ToUnixTimeMilliseconds().ToString();

        // RaidStarting: StartingTime set, StartedTime not yet set, RaidType=Unknown (as GameWatcher does it)
        var raidStartingEvent = new GameEvent
        {
            EventType = "RaidStarting",
            TimestampMs = now.ToUnixTimeMilliseconds(),
            Data =
            {
                { "profileId", "test-profile" },
                { "profileType", "Regular" },
                { "profileAccountId", "12345" },
                { "map", "Woods" },
                { "raidId", "test-raid-001" },
                { "raidType", "Unknown" },
                { "reconnected", "False" },
                { "queueTime", "45.0" },
                { "screenshotsJson", "[]" },
                { "startingTimeMs", startingTimeMs }
            }
        };

        // RaidStarted: both StartingTime and StartedTime set, RaidType=Scav/PMC
        var raidStartedEvent = new GameEvent
        {
            EventType = "RaidStarted",
            TimestampMs = now.AddSeconds(3).ToUnixTimeMilliseconds(),
            Data =
            {
                { "profileId", "test-profile" },
                { "profileType", "Regular" },
                { "profileAccountId", "12345" },
                { "map", "Woods" },
                { "raidId", "test-raid-001" },
                { "raidType", raidType },
                { "reconnected", "False" },
                { "queueTime", "45.0" },
                { "screenshotsJson", "[]" },
                { "startingTimeMs", startingTimeMs },
                { "startedTimeMs", startedTimeMs }
            }
        };

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending RaidStarting (raidType=Unknown, startingTime set)...");
        BroadcastProtoEvent(raidStartingEvent);
        await Task.Delay(500);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sending RaidStarted (raidType={raidType}, startingTime+startedTime set)...");
        BroadcastProtoEvent(raidStartedEvent);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Sequence complete. Expect sound to play ONCE.");
    }

    private void BroadcastProtoEvent(GameEvent protoEvent)
    {
        var inactiveStreams = new List<IServerStreamWriter<GameEvent>>();
        foreach (var stream in _activeStreams)
        {
            try { stream.WriteAsync(protoEvent).Wait(TimeSpan.FromSeconds(2)); }
            catch { inactiveStreams.Add(stream); }
        }
        foreach (var stream in inactiveStreams)
            _activeStreams.TryTake(out _);
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
