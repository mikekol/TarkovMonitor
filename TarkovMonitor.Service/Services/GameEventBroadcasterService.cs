using Grpc.Core;
using TarkovMonitor;
using TarkovMonitor.Service.Contracts;
using System.Collections.Generic;
using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace TarkovMonitor.Service.Services;

public class GameEventBroadcasterService : TarkovMonitorService.TarkovMonitorServiceBase
{
    private readonly GameWatcher _gameWatcher;
    private readonly IServiceConfiguration _config;
    private readonly ILogger<GameEventBroadcasterService> _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly bool _verboseLogging;

    private readonly List<IServerStreamWriter<GameEvent>> _activeStreams = new();
    private readonly object _streamsLock = new();

    public GameEventBroadcasterService(
        GameWatcher gameWatcher,
        IServiceConfiguration config,
        ILogger<GameEventBroadcasterService> logger,
        IOptions<TarkovMonitorOptions> options,
        IHostApplicationLifetime appLifetime)
    {
        _gameWatcher = gameWatcher;
        _config = config;
        _logger = logger;
        _appLifetime = appLifetime;
        _verboseLogging = options.Value.VerboseLogging;

        SubscribeToGameWatcherEvents();
    }

    public override async Task SubscribeToGameEvents(
        SubscriptionRequest request,
        IServerStreamWriter<GameEvent> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Client subscribed to game events: {ClientAgent}", request.ClientAgent);
        lock (_streamsLock) { _activeStreams.Add(responseStream); }

        // Push current profile immediately so late-joining clients don't miss InitialReadComplete
        if (!string.IsNullOrEmpty(GameWatcher.CurrentProfile.Id))
            await SendToStream(responseStream, "InitialReadComplete", ProfileData(GameWatcher.CurrentProfile));

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, _appLifetime.ApplicationStopping);
            while (!linkedCts.Token.IsCancellationRequested)
                await Task.Delay(1000, linkedCts.Token);
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

    /// <summary>
    /// Returns the current service configuration to the caller.
    /// Reloads from disk first so the caller always sees the latest persisted values.
    /// </summary>
    public override async Task<ServiceConfig> GetConfig(GetConfigRequest request, ServerCallContext context)
    {
        await _config.LoadAsync();
        var response = new ServiceConfig
        {
            CustomLogsPath       = _config.CustomLogsPath ?? "",
            CustomMap            = _config.CustomMap ?? "",
            ScreenshotsPath      = _config.ScreenshotsPath ?? "",
            TarkovTrackerEnabled = _config.TarkovTrackerEnabled,
        };
        foreach (var kvp in _config.TarkovTrackerTokens)
            response.TarkovTrackerTokens[kvp.Key] = kvp.Value;
        foreach (var kvp in _config.TarkovTrackerDomains)
            response.TarkovTrackerDomains[kvp.Key] = kvp.Value;
        return response;
    }

    /// <summary>
    /// Applies configuration changes received from a client and persists them to disk.
    /// Also applies the custom logs path to the running GameWatcher immediately so a restart
    /// is not required.
    /// </summary>
    public override async Task<UpdateConfigResponse> UpdateConfig(UpdateConfigRequest request, ServerCallContext context)
    {
        try
        {
            _config.CustomLogsPath = request.CustomLogsPath;

            // CustomMap is always written (empty string = clear the fallback).
            _config.CustomMap      = request.CustomMap;
            _gameWatcher.CustomMap = string.IsNullOrEmpty(request.CustomMap) ? null : request.CustomMap;

            // ScreenshotsPath: the service can't resolve the user's Documents folder, so
            // the UI pushes the correct path at connect time.
            if (!string.IsNullOrEmpty(request.ScreenshotsPath))
            {
                _config.ScreenshotsPath = request.ScreenshotsPath;
                _gameWatcher.ScreenshotsPath = request.ScreenshotsPath;
            }

            // Merge token map: the client sends only the keys it wants to change
            foreach (var kvp in request.TarkovTrackerTokens)
                _config.TarkovTrackerTokens[kvp.Key] = kvp.Value;

            // Merge domain map: same partial-update semantics as tokens
            foreach (var kvp in request.TarkovTrackerDomains)
                _config.TarkovTrackerDomains[kvp.Key] = kvp.Value;

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
                // Use source-gen context to keep serialization AOT-safe
                ["receivedItemsJson"] = JsonSerializer.Serialize(
                    args.LogContent.ReceivedItems, CoreJsonContext.Default.DictionaryStringInt32),
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
                ["rotation"] = args.Rotation.ToString(CultureInfo.InvariantCulture),
                ["filename"] = args.Filename,
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
        // Use source-gen context for AOT-safe serialization
        data["screenshotsJson"] = JsonSerializer.Serialize(
            args.RaidInfo.Screenshots, CoreJsonContext.Default.ListString);
        if (args.RaidInfo.StartingTime.HasValue)
            data["startingTimeMs"] = new DateTimeOffset(args.RaidInfo.StartingTime.Value).ToUnixTimeMilliseconds().ToString();
        if (args.RaidInfo.StartedTime.HasValue)
            data["startedTimeMs"] = new DateTimeOffset(args.RaidInfo.StartedTime.Value).ToUnixTimeMilliseconds().ToString();
        return data;
    }

    private static async Task SendToStream(IServerStreamWriter<GameEvent> stream, string eventType, Dictionary<string, string> data)
    {
        var protoEvent = new GameEvent
        {
            EventType = eventType,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        foreach (var kvp in data)
            protoEvent.Data[kvp.Key] = kvp.Value;
        await stream.WriteAsync(protoEvent);
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

        if (_verboseLogging)
            _logger.LogInformation("Broadcasting {EventType} to {ClientCount} client(s)", eventType, snapshot.Count);

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
