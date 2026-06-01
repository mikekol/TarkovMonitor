namespace TarkovMonitor.Service.Services;

public interface IServiceConfiguration
{
    string? CustomLogsPath { get; set; }
    string? TarkovTrackerToken { get; set; }
    bool TarkovTrackerEnabled { get; }
    Task SaveAsync();
    Task LoadAsync();
}
