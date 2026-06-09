namespace TarkovMonitor.Service.Services;

public class TarkovMonitorOptions
{
    public const string SectionName = "TarkovMonitor";

    public string CustomLogsPath { get; set; } = "";
    public string TarkovTrackerToken { get; set; } = "";
    public string TarkovTrackerDomain { get; set; } = "tarkovtracker.io";
    public int GrpcPort { get; set; } = 50051;
    public int LogMonitorPollInterval { get; set; } = 5000;
    public int GameWatcherRecoveryWaitMs { get; set; } = 5000;
    public bool VerboseLogging { get; set; } = true;
}
