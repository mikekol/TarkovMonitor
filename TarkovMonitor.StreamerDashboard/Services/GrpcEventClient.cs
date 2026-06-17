using Grpc.Core;
using Grpc.Net.Client;
using TarkovMonitor.Service.Contracts;

namespace TarkovMonitor.StreamerDashboard.Services;

public record GameEventArgs(string EventType, IReadOnlyDictionary<string, string> Data);
public record ConnectionStateArgs(bool IsConnected, string? Message = null);

public sealed class GrpcEventClient : IDisposable
{
    private readonly string _grpcAddress;
    private CancellationTokenSource? _cts;

    public event EventHandler<GameEventArgs>? EventReceived;
    public event EventHandler<ConnectionStateArgs>? ConnectionStateChanged;

    public bool IsConnected { get; private set; }
    public string? CurrentMap { get; private set; }
    public string? CurrentRaidType { get; private set; }
    public string? CurrentProfileType { get; private set; }

    public GrpcEventClient(string grpcAddress)
    {
        _grpcAddress = grpcAddress;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ListenLoopAsync(_cts.Token);
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var channel = GrpcChannel.ForAddress(_grpcAddress);
                var client = new TarkovMonitorService.TarkovMonitorServiceClient(channel);
                var stream = client.SubscribeToGameEvents(
                    new SubscriptionRequest { ClientAgent = "StreamerDashboard/1.0" },
                    cancellationToken: ct);

                IsConnected = true;
                ConnectionStateChanged?.Invoke(this, new ConnectionStateArgs(true));

                await foreach (var evt in stream.ResponseStream.ReadAllAsync(ct))
                    DispatchEvent(evt);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                IsConnected = false;
                ConnectionStateChanged?.Invoke(this, new ConnectionStateArgs(false, ex.Message));
                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void DispatchEvent(GameEvent evt)
    {
        var data = (IReadOnlyDictionary<string, string>)evt.Data;

        switch (evt.EventType)
        {
            case "MapLoading":
            case "MatchFound":
            case "RaidStarting":
            case "RaidStarted":
            case "RaidEnded":
                if (data.TryGetValue("map", out var map)) CurrentMap = map;
                if (data.TryGetValue("raidType", out var rt)) CurrentRaidType = rt;
                break;
            case "ExitedPostRaidMenus":
                CurrentMap = null;
                CurrentRaidType = null;
                break;
            case "InitialReadComplete":
            case "ProfileChanged":
                if (data.TryGetValue("profileType", out var pt)) CurrentProfileType = pt;
                break;
        }

        EventReceived?.Invoke(this, new GameEventArgs(evt.EventType, data));
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
