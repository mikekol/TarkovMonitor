using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using TarkovMonitor.StreamerDashboard.Hubs;

namespace TarkovMonitor.StreamerDashboard.Services;

public sealed class OverlayWebServer : IDisposable
{
    private WebApplication? _app;
    private IHubContext<OverlayHub>? _hubContext;

    public int Port { get; }

    public OverlayWebServer(int port)
    {
        Port = port;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });

        builder.WebHost.UseSetting("urls", $"http://localhost:{Port}");
        builder.Services.AddSignalR();

        _app = builder.Build();
        _hubContext = _app.Services.GetRequiredService<IHubContext<OverlayHub>>();

        _app.UseDefaultFiles();
        _app.UseStaticFiles();
        _app.MapHub<OverlayHub>("/overlayhub");

        await _app.StartAsync(ct);
    }

    public async Task BroadcastEventAsync(string eventType, IReadOnlyDictionary<string, string> data)
    {
        if (_hubContext is null) return;
        await _hubContext.Clients.All.SendAsync("GameEvent", eventType, data);
    }

    public void Dispose()
    {
        _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
