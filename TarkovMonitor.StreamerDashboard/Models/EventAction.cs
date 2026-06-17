namespace TarkovMonitor.StreamerDashboard.Models;

public class EventAction
{
    public string EventType { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string CommandArgs { get; set; } = string.Empty;
}
