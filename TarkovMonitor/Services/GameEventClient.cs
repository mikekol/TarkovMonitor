using Grpc.Net.Client;
using TarkovMonitor.Service.Contracts;
using System.Diagnostics;

namespace TarkovMonitor.Services;

public class GameEventClient : IAsyncDisposable
{
    private GrpcChannel? _channel;
    private TarkovMonitorService.TarkovMonitorServiceClient? _client;
    private readonly string _serverAddress;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _subscriptionTask;

    public event EventHandler<GameEventArgs>? GameEventReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public GameEventClient(string serverAddress = "http://localhost:50051")
    {
        _serverAddress = serverAddress;
    }

    public async Task ConnectAsync()
    {
        try
        {
            _channel = GrpcChannel.ForAddress(_serverAddress);
            _client = new TarkovMonitorService.TarkovMonitorServiceClient(_channel);

            // Verify connection
            var status = await _client.GetStatusAsync(new GetStatusRequest());
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs { IsConnected = true });

            // Start subscription loop
            _cancellationTokenSource = new CancellationTokenSource();
            _subscriptionTask = SubscribeToEventsAsync(_cancellationTokenSource.Token);
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
            if (_client == null) return;

            var stream = _client.SubscribeToGameEvents(new SubscriptionRequest(), cancellationToken: cancellationToken);

            await foreach (var gameEvent in stream.ResponseStream.ReadAllAsync(cancellationToken))
            {
                GameEventReceived?.Invoke(this, new GameEventArgs
                {
                    EventType = gameEvent.EventType,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(gameEvent.TimestampMs).DateTime,
                    Data = gameEvent.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in subscription: {ex.Message}");
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
            {
                IsConnected = false,
                Error = ex.Message
            });
        }
    }

    public async Task<ServiceConfig> GetConfigAsync()
    {
        if (_client == null) throw new InvalidOperationException("Not connected");
        return await _client.GetConfigAsync(new GetConfigRequest());
    }

    public async Task UpdateConfigAsync(string customLogsPath, string tarkovTrackerToken)
    {
        if (_client == null) throw new InvalidOperationException("Not connected");

        var response = await _client.UpdateConfigAsync(new UpdateConfigRequest
        {
            CustomLogsPath = customLogsPath,
            TarkovTrackerToken = tarkovTrackerToken
        });

        if (!response.Success)
        {
            throw new InvalidOperationException($"Failed to update config: {response.ErrorMessage}");
        }
    }

    public async Task DisconnectAsync()
    {
        _cancellationTokenSource?.Cancel();
        if (_subscriptionTask != null)
        {
            try
            {
                await _subscriptionTask;
            }
            catch { }
        }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource?.Dispose();
        if (_channel != null)
        {
            await _channel.ShutdownAsync();
            _channel.Dispose();
        }
    }
}

public class GameEventArgs : EventArgs
{
    public string EventType { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public Dictionary<string, string> Data { get; set; } = new();
}

public class ConnectionStateChangedEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? Error { get; set; }
}
