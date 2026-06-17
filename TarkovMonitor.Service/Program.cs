using System.Net;
using TarkovMonitor;
using TarkovMonitor.Service.Services;
using TarkovMonitor.Service.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.Configure<TarkovMonitorOptions>(builder.Configuration.GetSection(TarkovMonitorOptions.SectionName));
builder.Services.AddSingleton<IServiceConfiguration, JsonServiceConfiguration>();
builder.Services.AddSingleton<GameWatcher>();
builder.Services.AddGrpc();
builder.Services.AddSingleton<GameEventBroadcasterService>();
builder.Services.AddWindowsService();
builder.Services.AddHostedService<GameWatcherHostedService>();
// TarkovTrackerUpdaterService watches for task events and calls the TarkovTracker API server-side,
// ensuring progress is synced even when the UI is not running.
builder.Services.AddHostedService<TarkovTrackerUpdaterService>();

var grpcOptions = builder.Configuration
    .GetSection(TarkovMonitorOptions.SectionName)
    .Get<TarkovMonitorOptions>() ?? new TarkovMonitorOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    var port     = grpcOptions.GrpcPort;
    var protocol = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;

    switch (grpcOptions.GrpcListenMode)
    {
        case GrpcListenMode.AnyIP:
            options.ListenAnyIP(port, o => o.Protocols = protocol);
            break;

        case GrpcListenMode.SpecificIP:
            if (!IPAddress.TryParse(grpcOptions.GrpcListenAddress, out var address))
                throw new InvalidOperationException(
                    $"GrpcListenMode is SpecificIP but GrpcListenAddress '{grpcOptions.GrpcListenAddress}' is not a valid IP address.");
            options.Listen(address, port, o => o.Protocols = protocol);
            break;

        default: // Localhost
            options.ListenLocalhost(port, o => o.Protocols = protocol);
            break;
    }
});

var app = builder.Build();
app.MapGrpcService<GameEventBroadcasterService>();
await app.RunAsync();
