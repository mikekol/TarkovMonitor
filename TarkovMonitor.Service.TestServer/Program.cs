using TarkovMonitor.Service.Contracts;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TarkovMonitor.Service.TestServer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton<TestGameEventService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(50051, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
});

var app = builder.Build();
app.MapGrpcService<TestGameEventService>();

var service = app.Services.GetRequiredService<TestGameEventService>();

// Start gRPC server in background
_ = app.RunAsync();

// Interactive CLI
Console.WriteLine("=== TarkovMonitor Test Server ===");
Console.WriteLine("Listening on http://localhost:50051");
Console.WriteLine();
Console.WriteLine("Commands:");
Console.WriteLine("  raid_start <raid_id>  - Simulate raid start");
Console.WriteLine("  raid_end <raid_id>    - Simulate raid end");
Console.WriteLine("  task <status>         - Simulate task finish");
Console.WriteLine("  custom <type> <data>  - Send custom event");
Console.WriteLine("  status                - Show client count");
Console.WriteLine("  exit                  - Shutdown");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine() ?? "";
    if (string.IsNullOrWhiteSpace(input)) continue;

    var parts = input.Split(' ', 2);

    try
    {
        switch (parts[0].ToLower())
        {
            case "raid_start":
                service.BroadcastEvent("RaidStarted", parts.Length > 1 ? parts[1] : "test-raid-001");
                Console.WriteLine("✓ RaidStarted broadcasted");
                break;

            case "raid_end":
                service.BroadcastEvent("RaidEnded", parts.Length > 1 ? parts[1] : "test-raid-001");
                Console.WriteLine("✓ RaidEnded broadcasted");
                break;

            case "task":
                service.BroadcastEvent("TaskFinished", parts.Length > 1 ? parts[1] : "Completed");
                Console.WriteLine("✓ TaskFinished broadcasted");
                break;

            case "custom":
                var customParts = input.Substring(6).Split(' ', 2);
                if (customParts.Length > 1)
                {
                    service.BroadcastEvent(customParts[0], customParts[1]);
                    Console.WriteLine($"✓ {customParts[0]} broadcasted");
                }
                else
                {
                    Console.WriteLine("Usage: custom <type> <data>");
                }
                break;

            case "status":
                Console.WriteLine($"Connected clients: {service.ActiveStreamCount}");
                break;

            case "exit":
                Console.WriteLine("Shutting down...");
                await app.StopAsync();
                return;

            default:
                Console.WriteLine("Unknown command. Try: raid_start, raid_end, task, custom, status, exit");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ Error: {ex.Message}");
    }
}
