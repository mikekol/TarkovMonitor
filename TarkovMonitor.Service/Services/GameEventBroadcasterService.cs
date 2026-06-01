using Grpc.Core;
using TarkovMonitor;
using TarkovMonitor.Service.Contracts;
using System.Collections.Concurrent;

namespace TarkovMonitor.Service.Services;

public class GameEventBroadcasterService : TarkovMonitorService.TarkovMonitorServiceBase
{
    private readonly GameWatcher _gameWatcher;
    private readonly LogMonitor _logMonitor;
    private readonly IServiceConfiguration _config;
    private readonly ILogger<GameEventBroadcasterService> _logger;
    private readonly ConcurrentBag<IServerStreamWriter<GameEvent>> _activeStreams = new();

    public GameEventBroadcasterService(
        GameWatcher gameWatcher,
        LogMonitor logMonitor,
        IServiceConfiguration config,
        ILogger<GameEventBroadcasterService> logger)
    {
        _gameWatcher = gameWatcher;
        _logMonitor = logMonitor;
        _config = config;
        _logger = logger;

        SubscribeToGameWatcherEvents();
    }

    public override async Task SubscribeToGameEvents(
        SubscriptionRequest request,
        IAsyncStreamWriter<GameEvent> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Client subscribed to game events");
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
            _logger.LogInformation("Client unsubscribed");
        }
    }

    public override async Task<ServiceConfig> GetConfig(GetConfigRequest request, ServerCallContext context)
    {
        await _config.LoadAsync();
        return new ServiceConfig
        {
            CustomLogsPath = _config.CustomLogsPath ?? "",
            TarkovTrackerToken = _config.TarkovTrackerToken ?? "",
            TarkovTrackerEnabled = _config.TarkovTrackerEnabled
        };
    }

    public override async Task<UpdateConfigResponse> UpdateConfig(UpdateConfigRequest request, ServerCallContext context)
    {
        try
        {
            _config.CustomLogsPath = request.CustomLogsPath;
            _config.TarkovTrackerToken = request.TarkovTrackerToken;
            await _config.SaveAsync();

            return new UpdateConfigResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error updating config: {ex.Message}");
            return new UpdateConfigResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
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

    private void SubscribeToGameWatcherEvents()
    {
        _gameWatcher.RaidStarted += (sender, args) =>
            BroadcastEvent("RaidStarted", args?.RaidInfo.RaidId ?? "");

        _gameWatcher.RaidEnded += (sender, args) =>
            BroadcastEvent("RaidEnded", args?.RaidInfo.RaidId ?? "");

        _gameWatcher.TaskFinished += (sender, args) =>
            BroadcastEvent("TaskFinished", args?.LogContent.Status.ToString() ?? "");
    }

    private void BroadcastEvent(string eventType, string data)
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
                stream.WriteAsync(protoEvent).Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                inactiveStreams.Add(stream);
            }
        }

        foreach (var stream in inactiveStreams)
        {
            _activeStreams.TryTake(out _);
        }
    }
}
