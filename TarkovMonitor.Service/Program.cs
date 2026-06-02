using TarkovMonitor;
using TarkovMonitor.Service.Services;
using TarkovMonitor.Service.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.AddSingleton<IServiceConfiguration, JsonServiceConfiguration>();
builder.Services.AddSingleton<GameWatcher>();
builder.Services.AddGrpc();
builder.Services.AddWindowsService();
builder.Services.AddHostedService<GameWatcherHostedService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

var app = builder.Build();
app.MapGrpcService<GameEventBroadcasterService>();
await app.RunAsync();
