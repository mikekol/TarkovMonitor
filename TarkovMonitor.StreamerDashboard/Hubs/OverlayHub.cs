using Microsoft.AspNetCore.SignalR;

namespace TarkovMonitor.StreamerDashboard.Hubs;

// Server pushes events via IHubContext<OverlayHub>; clients only need to connect and listen.
public class OverlayHub : Hub { }
