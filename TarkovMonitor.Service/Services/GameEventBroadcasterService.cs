using Grpc.Core;
using TarkovMonitor;
using TarkovMonitor.Service.Contracts;
using System.Collections.Generic;
using System.Text.Json;
using System.Globalization;

namespace TarkovMonitor.Service.Services;

public class GameEventBroadcasterService : TarkovMonitorService.TarkovMonitorServiceBase
{
    private readonly GameWatcher _gameWatcher;
    private readonly IServiceConfiguration _config;
    private readonly ILogger<GameEventBroadcasterService> _logger;

    private readonly List<IServerStreamWriter<GameEvent>> _activeStreams = new();
    private readonly object _streamsLock = new();

    public GameEventBroadcasterService(
        GameWatcher gameWatcher,
        IServiceConfiguration config,
        ILogger<GameEventBroadcasterService> logger)
    {
        _gameWatcher = gameWatcher;
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
        lock (_streamsLock) { _activeStreams.Add(responseStream); }

        try
        {
            while (!context.CancellationToken.IsCancellationRequested)
                await Task.Delay(1000, context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Client unsubscribed");
        }
        finally
        {
            lock (_streamsLock) { _activeStreams.Remove(responseStream); }
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

            if (!string.IsNullOrEmpty(request.CustomLogsPath))
                _gameWatcher.LogsPath = request.CustomLogsPath;

            return new UpdateConfigResponse { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating config");
            return new UpdateConfigResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public override Task<ServiceStatus> GetStatus(GetStatusRequest request, ServerCallContext context)
    {
        return Task.FromResult(new ServiceStatus
        {
            IsGameWatcherRunning = true,
            IsLogMonitorActive = true,
            UptimeSeconds = (long)DateTime.Now.TimeOfDay.TotalSeconds
        });
    }

    private void SubscribeToGameWatcherEvents()
    {
        _gameWatcher.RaidStarting += (_, args) =>
        {
            if (args == null) return;
            Broadcast("RaidStarting", RaidInfoData(args));
        };

        _gameWatcher.RaidStarted += (_, args) =>
        {
            if (args == null) return;
            Broadcast("RaidStarted", RaidInfoData(args));
        };

        _gameWatcher.RaidExited += (_, args) =>
        {
            if (args == null) return;
            Broadcast("RaidExited", new()
            {
                ["map"] = args.Map,
                ["raidId"] = args.RaidId ?? ""
            });
        };

        _gameWatcher.RaidEnded += (_, args) =>
        {
            if (args == null) return;
            Broadcast("RaidEnded", RaidInfoData(args));
        };

        _gameWatcher.ExitedPostRaidMenus += (_, args) =>
            Broadcast("ExitedPostRaidMenus", new());

        _gameWatcher.TaskStarted += (_, args) =>
        {
            if (args == null) return;
            Broadcast("TaskStarted", new() { ["taskId"] = args.LogContent.TaskId });
        };

        _gameWatcher.TaskFailed += (_, args) =>
        {
            if (args == null) return;
            Broadcast("TaskFailed", new() { ["taskId"] = args.LogContent.TaskId });
        };

        _gameWatcher.TaskFinished += (_, args) =>
        {
            if (args == null) return;
            Broadcast("TaskFinished", new() { ["taskId"] = args.LogContent.TaskId });
        };

        _gameWatcher.FleaSold += (_, args) =>
        {
            if (args == null) return;
            Broadcast("FleaSold", new()
            {
                ["buyer"] = args.LogContent.Buyer,
                ["soldItemId"] = args.LogContent.SoldItemId,
                ["soldItemCount"] = args.LogContent.SoldItemCount.ToString(),
                ["receivedItemsJson"] = JsonSerializer.Serialize(args.LogContent.ReceivedItems),
                ["profileId"] = args.Profile.Id
            });
        };

        _gameWatcher.FleaOfferExpired += (_, args) =>
        {
            if (args == null) return;
            Broadcast("FleaOfferExpired", new()
            {
                ["itemId"] = args.LogContent.ItemId,
                ["itemCount"] = args.LogContent.ItemCount.ToString()
            });
        };

        _gameWatcher.DebugMessage += (_, args) =>
        {
            if (args == null) return;
            Broadcast("DebugMessage", new() { ["message"] = args.Message });
        };

        _gameWatcher.ExceptionThrown += (_, args) =>
        {
            if (args == null) return;
            Broadcast("ExceptionThrown", new()
            {
                ["context"] = args.Context,
                ["message"] = args.Exception.Message,
                ["stackTrace"] = args.Exception.StackTrace ?? ""
            });
        };

        _gameWatcher.NewLogData += (_, args) =>
        {
            if (args == null) return;
            Broadcast("NewLogData", new()
            {
                ["data"] = args.Data,
                ["logType"] = args.Type.ToString(),
                ["initialRead"] = args.InitialRead.ToString()
            });
        };

        _gameWatcher.GroupInviteAccept += (_, args) =>
        {
            if (args == null) return;
            Broadcast("GroupInviteAccept", new()
            {
                ["nickname"] = args.LogContent.Info.Nickname,
                ["side"] = args.LogContent.Info.Side,
                ["level"] = args.LogContent.Info.Level.ToString()
            });
        };

        _gameWatcher.GroupUserLeave += (_, args) =>
        {
            if (args == null) return;
            Broadcast("GroupUserLeave", new() { ["nickname"] = args.LogContent.Nickname });
        };

        _gameWatcher.MapLoading += (_, args) =>
        {
            if (args == null) return;
            Broadcast("MapLoading", RaidInfoData(args));
        };

        _gameWatcher.MatchFound += (_, args) =>
        {
            if (args == null) return;
            Broadcast("MatchFound", RaidInfoData(args));
        };

        _gameWatcher.PlayerPosition += (_, args) =>
        {
            if (args == null) return;
            Broadcast("PlayerPosition", new()
            {
                ["x"] = args.Position.X.ToString(CultureInfo.InvariantCulture),
                ["y"] = args.Position.Y.ToString(CultureInfo.InvariantCulture),
                ["z"] = args.Position.Z.ToString(CultureInfo.InvariantCulture),
                ["map"] = args.RaidInfo.Map,
                ["raidId"] = args.RaidInfo.RaidId
            });
        };

        _gameWatcher.ProfileChanged += (_, args) =>
        {
            if (args == null) return;
            Broadcast("ProfileChanged", ProfileData(args.Profile));
        };

        _gameWatcher.ControlSettings += (_, args) =>
        {
            if (args == null) return;
            Broadcast("ControlSettings", new()
            {
                ["controlSettingsJson"] = args.ControlSettings.ToJsonString()
            });
        };

        _gameWatcher.InitialReadComplete += (_, args) =>
        {
            if (args == null) return;
            Broadcast("InitialReadComplete", ProfileData(args.Profile));
        };
    }

    private static Dictionary<string, string> ProfileData(Profile profile) => new()
    {
        ["profileId"] = profile.Id,
        ["profileType"] = profile.Type.ToString(),
        ["profileAccountId"] = profile.AccountId
    };

    private static Dictionary<string, string> RaidInfoData(RaidInfoEventArgs args)
    {
        var data = ProfileData(args.Profile);
        data["map"] = args.RaidInfo.Map;
        data["raidId"] = args.RaidInfo.RaidId;
        data["raidType"] = args.RaidInfo.RaidType.ToString();
        data["reconnected"] = args.RaidInfo.Reconnected.ToString();
        data["queueTime"] = args.RaidInfo.QueueTime.ToString(CultureInfo.InvariantCulture);
        data["screenshotsJson"] = JsonSerializer.Serialize(args.RaidInfo.Screenshots);
        if (args.RaidInfo.StartedTime.HasValue)
            data["startedTimeMs"] = new DateTimeOffset(args.RaidInfo.StartedTime.Value).ToUnixTimeMilliseconds().ToString();
        return data;
    }

    private void Broadcast(string eventType, Dictionary<string, string> data)
    {
        var protoEvent = new GameEvent
        {
            EventType = eventType,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        foreach (var kvp in data)
            protoEvent.Data[kvp.Key] = kvp.Value;

        List<IServerStreamWriter<GameEvent>> snapshot;
        lock (_streamsLock) { snapshot = new List<IServerStreamWriter<GameEvent>>(_activeStreams); }

        var dead = new List<IServerStreamWriter<GameEvent>>();
        foreach (var stream in snapshot)
        {
            try { stream.WriteAsync(protoEvent).Wait(TimeSpan.FromSeconds(5)); }
            catch { dead.Add(stream); }
        }

        if (dead.Count > 0)
            lock (_streamsLock)
                foreach (var s in dead) _activeStreams.Remove(s);
    }
}
