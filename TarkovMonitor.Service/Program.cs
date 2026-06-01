using TarkovMonitor;
using TarkovMonitor.Service.Services;
using TarkovMonitor.Service.Contracts;

var builder = Host.CreateApplicationBuilder(args);

// Configuration
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Services
builder.Services.AddSingleton<IServiceConfiguration, JsonServiceConfiguration>();
builder.Services.AddSingleton<GameWatcher>();
builder.Services.AddSingleton<LogMonitor>();
builder.Services.AddSingleton<TarkovTracker>();
builder.Services.AddGrpc();

// Windows Service support
builder.Services.AddWindowsService();

// Kestrel for gRPC
builder.WebHost.ConfigureKestrel(options =>
{
 options.ListenLocalhost(50051, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

var app = builder.Build();
app.MapGrpcService<GameEventBroadcasterService>();
await app.RunAsync();
