using Grpc.Core;
using Grpc.Net.Client;
using TarkovMonitor.Service.Contracts;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TarkovMonitor.Services;

public class GameEventClient : IAsyncDisposable
{
    private GrpcChannel? _channel;
    private TarkovMonitorService.TarkovMonitorServiceClient? _grpcClient;
    private readonly string _serverAddress;
    private CancellationTokenSource? _cts;
    private Task? _subscriptionTask;

    // Connection events
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    // Raid events
    public event EventHandler<RaidInfoEventArgs>? RaidStarting;
    public event EventHandler<RaidInfoEventArgs>? RaidStarted;
    public event EventHandler<RaidExitedEventArgs>? RaidExited;
    public event EventHandler<RaidInfoEventArgs>? RaidEnded;
    public event EventHandler<RaidInfoEventArgs>? ExitedPostRaidMenus;
    public event EventHandler<RaidInfoEventArgs>? MapLoading;
    public event EventHandler<RaidInfoEventArgs>? MatchFound;

    // Task events
    public event EventHandler<TaskEventArgs>? TaskStarted;
    public event EventHandler<TaskEventArgs>? TaskFailed;
    public event EventHandler<TaskEventArgs>? TaskFinished;

    // Flea events
    public event EventHandler<FleaSaleEventArgs>? FleaSold;
    public event EventHandler<FleaExpiredEventArgs>? FleaOfferExpired;

    // Player/profile events
    public event EventHandler<PlayerPositionEventArgs>? PlayerPosition;
    public event EventHandler<ProfileEventArgs>? ProfileChanged;
    public event EventHandler<ProfileEventArgs>? InitialReadComplete;

    // Group events
    public event EventHandler<GroupInviteAcceptedEventArgs>? GroupInviteAccept;
    public event EventHandler<GroupUserLeaveEventArgs>? GroupUserLeave;

    // Misc events
    public event EventHandler<DebugEventArgs>? DebugMessage;
    public event EventHandler<ExceptionEventArgs>? ExceptionThrown;
    public event EventHandler<NewLogDataEventArgs>? NewLogData;
    public event EventHandler<ControlSettingsEventArgs>? ControlSettings;

    public GameEventClient(string serverAddress = "http://localhost:50051")
    {
        _serverAddress = serverAddress;
    }

    public async Task ConnectAsync()
    {
        try
        {
            _channel = GrpcChannel.ForAddress(_serverAddress);
            _grpcClient = new TarkovMonitorService.TarkovMonitorServiceClient(_channel);

            await _grpcClient.GetStatusAsync(new GetStatusRequest());
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs { IsConnected = true });

            _cts = new CancellationTokenSource();
            _subscriptionTask = SubscribeToEventsAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                IsConnected = false,
                Error = ex.Message
            });
            throw;
        }
    }

    private async Task SubscribeToEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_grpcClient == null) return;
            var version = typeof(GameEventClient).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            var clientAgent = $"TarkovMonitor.UI/{version}";
            var stream = _grpcClient.SubscribeToGameEvents(new SubscriptionRequest { ClientAgent = clientAgent }, cancellationToken: cancellationToken);

            await foreach (var gameEvent in stream.ResponseStream.ReadAllAsync(cancellationToken))
                DispatchGameEvent(gameEvent);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"Subscription error: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                IsConnected = false,
                Error = ex.Message
            });
        }
    }

    private void DispatchGameEvent(GameEvent gameEvent)
    {
        var d = gameEvent.Data;
        switch (gameEvent.EventType)
        {
            case "RaidStarting":        RaidStarting?.Invoke(this, BuildRaidInfoArgs(d)); break;
            case "RaidStarted":         RaidStarted?.Invoke(this, BuildRaidInfoArgs(d)); break;
            case "RaidEnded":           RaidEnded?.Invoke(this, BuildRaidInfoArgs(d)); break;
            case "ExitedPostRaidMenus": ExitedPostRaidMenus?.Invoke(this, BuildRaidInfoArgs(d)); break;
            case "MapLoading":          MapLoading?.Invoke(this, BuildRaidInfoArgs(d)); break;
            case "MatchFound":          MatchFound?.Invoke(this, BuildRaidInfoArgs(d)); break;

            case "RaidExited":
                RaidExited?.Invoke(this, new RaidExitedEventArgs
                {
                    Map = d.GetValueOrDefault("map", ""),
                    RaidId = d.GetValueOrDefault("raidId", null)
                });
                break;

            case "TaskStarted":  TaskStarted?.Invoke(this, new TaskEventArgs { TaskId = d.GetValueOrDefault("taskId", "") }); break;
            case "TaskFailed":   TaskFailed?.Invoke(this, new TaskEventArgs { TaskId = d.GetValueOrDefault("taskId", "") }); break;
            case "TaskFinished": TaskFinished?.Invoke(this, new TaskEventArgs { TaskId = d.GetValueOrDefault("taskId", "") }); break;

            case "FleaSold":         FleaSold?.Invoke(this, BuildFleaSaleArgs(d)); break;
            case "FleaOfferExpired":
                FleaOfferExpired?.Invoke(this, new FleaExpiredEventArgs
                {
                    ItemId = d.GetValueOrDefault("itemId", ""),
                    ItemCount = int.TryParse(d.GetValueOrDefault("itemCount", "1"), out var ic) ? ic : 1
                });
                break;

            case "DebugMessage":
                DebugMessage?.Invoke(this, new DebugEventArgs(d.GetValueOrDefault("message", "")));
                break;

            case "ExceptionThrown":
                ExceptionThrown?.Invoke(this, new ExceptionEventArgs(
                    new Exception(d.GetValueOrDefault("message", "")),
                    d.GetValueOrDefault("context", "")));
                break;

            case "NewLogData":
                NewLogData?.Invoke(this, new NewLogDataEventArgs
                {
                    Data = d.GetValueOrDefault("data", ""),
                    Type = Enum.TryParse<GameLogType>(d.GetValueOrDefault("logType", ""), out var glt) ? glt : GameLogType.Application,
                    InitialRead = bool.TryParse(d.GetValueOrDefault("initialRead", "false"), out var ir) && ir
                });
                break;

            case "GroupInviteAccept":
                GroupInviteAccept?.Invoke(this, new GroupInviteAcceptedEventArgs
                {
                    Nickname = d.GetValueOrDefault("nickname", ""),
                    Side = d.GetValueOrDefault("side", ""),
                    Level = int.TryParse(d.GetValueOrDefault("level", "0"), out var lvl) ? lvl : 0
                });
                break;

            case "GroupUserLeave":
                GroupUserLeave?.Invoke(this, new GroupUserLeaveEventArgs
                {
                    Nickname = d.GetValueOrDefault("nickname", "")
                });
                break;

            case "PlayerPosition":
                PlayerPosition?.Invoke(this, BuildPlayerPositionArgs(d));
                break;

            case "ProfileChanged":
                ProfileChanged?.Invoke(this, BuildProfileArgs(d));
                break;

            case "InitialReadComplete":
                InitialReadComplete?.Invoke(this, BuildProfileArgs(d));
                break;

            case "ControlSettings":
                var json = d.GetValueOrDefault("controlSettingsJson", "{}");
                var node = JsonNode.Parse(json);
                if (node != null)
                    ControlSettings?.Invoke(this, new ControlSettingsEventArgs { ControlSettings = node });
                break;
        }
    }

    // ── Builder helpers ───────────────────────────────────────────────────────

    private static Profile BuildProfile(IReadOnlyDictionary<string, string> d) => new()
    {
        Id = d.GetValueOrDefault("profileId", ""),
        AccountId = d.GetValueOrDefault("profileAccountId", ""),
        Type = Enum.TryParse<ProfileType>(d.GetValueOrDefault("profileType", ""), out var pt)
            ? pt : ProfileType.Regular
    };

    private static ProfileEventArgs BuildProfileArgs(IReadOnlyDictionary<string, string> d) =>
        new(BuildProfile(d));

    private static RaidInfoEventArgs BuildRaidInfoArgs(IReadOnlyDictionary<string, string> d)
    {
        var profile = BuildProfile(d);
        var raidTypeStr = d.GetValueOrDefault("raidType", "");
        Enum.TryParse<RaidType>(raidTypeStr, out var raidType);

        // Reconstruct timing so RaidInfo.RaidType computes correctly
        DateTime? startedTime = null;
        DateTime? startingTime = null;

        if (long.TryParse(d.GetValueOrDefault("startedTimeMs", ""), out var stMs) && stMs > 0)
            startedTime = DateTimeOffset.FromUnixTimeMilliseconds(stMs).UtcDateTime;

        if (long.TryParse(d.GetValueOrDefault("startingTimeMs", ""), out var stingMs) && stingMs > 0)
            startingTime = DateTimeOffset.FromUnixTimeMilliseconds(stingMs).UtcDateTime;

        switch (raidType)
        {
            case RaidType.PVE:
                profile.Type = ProfileType.PVE;
                startedTime ??= DateTime.UtcNow;
                break;
            case RaidType.PMC:
                startedTime ??= DateTime.UtcNow;
                startingTime ??= startedTime.Value.AddSeconds(-10);  // fallback if not serialized
                break;
            case RaidType.Scav:
                startedTime ??= DateTime.UtcNow;
                break;
        }

        List<string> screenshots;
        try { screenshots = JsonSerializer.Deserialize<List<string>>(d.GetValueOrDefault("screenshotsJson", "[]")) ?? new(); }
        catch { screenshots = new(); }

        var raidInfo = new RaidInfo
        {
            Map = d.GetValueOrDefault("map", ""),
            RaidId = d.GetValueOrDefault("raidId", ""),
            Reconnected = bool.TryParse(d.GetValueOrDefault("reconnected", "false"), out var rc) && rc,
            QueueTime = float.TryParse(d.GetValueOrDefault("queueTime", "0"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var qt) ? qt : 0f,
            Profile = profile,
            StartedTime = startedTime,
            StartingTime = startingTime,
            Screenshots = screenshots
        };

        return new RaidInfoEventArgs(raidInfo, profile);
    }

    private static PlayerPositionEventArgs BuildPlayerPositionArgs(IReadOnlyDictionary<string, string> d)
    {
        var profile = BuildProfile(d);
        var raidInfo = new RaidInfo
        {
            Map = d.GetValueOrDefault("map", ""),
            RaidId = d.GetValueOrDefault("raidId", ""),
            Profile = profile
        };
        float.TryParse(d.GetValueOrDefault("x", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
        float.TryParse(d.GetValueOrDefault("y", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var y);
        float.TryParse(d.GetValueOrDefault("z", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var z);
        float.TryParse(d.GetValueOrDefault("rotation", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation);
        var filename = d.GetValueOrDefault("filename", "");
        return new PlayerPositionEventArgs(raidInfo, profile, new Position(x, y, z), rotation, filename);
    }

    private static FleaSaleEventArgs BuildFleaSaleArgs(IReadOnlyDictionary<string, string> d)
    {
        int.TryParse(d.GetValueOrDefault("soldItemCount", "0"), out var count);
        Dictionary<string, int> received;
        try { received = JsonSerializer.Deserialize<Dictionary<string, int>>(d.GetValueOrDefault("receivedItemsJson", "{}")) ?? new(); }
        catch { received = new(); }

        return new FleaSaleEventArgs
        {
            Buyer = d.GetValueOrDefault("buyer", ""),
            SoldItemId = d.GetValueOrDefault("soldItemId", ""),
            SoldItemCount = count,
            ReceivedItems = received,
            Profile = new Profile { Id = d.GetValueOrDefault("profileId", "") }
        };
    }

    // ── RPC passthrough ───────────────────────────────────────────────────────

    public async Task<ServiceConfig> GetConfigAsync()
    {
        if (_grpcClient == null) throw new InvalidOperationException("Not connected");
        return await _grpcClient.GetConfigAsync(new GetConfigRequest());
    }

    public async Task UpdateConfigAsync(string customLogsPath, string tarkovTrackerToken)
    {
        if (_grpcClient == null) throw new InvalidOperationException("Not connected");
        var response = await _grpcClient.UpdateConfigAsync(new UpdateConfigRequest
        {
            CustomLogsPath = customLogsPath,
            TarkovTrackerToken = tarkovTrackerToken
        });
        if (!response.Success)
            throw new InvalidOperationException($"Failed to update config: {response.ErrorMessage}");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_subscriptionTask != null)
        {
            try { await _subscriptionTask; } catch { }
        }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Dispose();
        if (_channel != null)
        {
            await _channel.ShutdownAsync();
            _channel.Dispose();
        }
    }
}

// ── Event args types ──────────────────────────────────────────────────────────

public class ConnectionStateChangedEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? Error { get; set; }
}

public class TaskEventArgs : EventArgs
{
    public string TaskId { get; set; } = "";
}

public class FleaSaleEventArgs : EventArgs
{
    public string Buyer { get; set; } = "";
    public string SoldItemId { get; set; } = "";
    public int SoldItemCount { get; set; }
    public Dictionary<string, int> ReceivedItems { get; set; } = new();
    public Profile Profile { get; set; } = new();
}

public class FleaExpiredEventArgs : EventArgs
{
    public string ItemId { get; set; } = "";
    public int ItemCount { get; set; }
}

public class GroupInviteAcceptedEventArgs : EventArgs
{
    public string Nickname { get; set; } = "";
    public string Side { get; set; } = "";
    public int Level { get; set; }
}

public class GroupUserLeaveEventArgs : EventArgs
{
    public string Nickname { get; set; } = "";
}
