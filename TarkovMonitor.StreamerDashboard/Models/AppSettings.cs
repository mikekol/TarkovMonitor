using System.Text.Json;

namespace TarkovMonitor.StreamerDashboard.Models;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TarkovMonitor.StreamerDashboard",
        "appsettings.json");

    public string GrpcAddress { get; set; } = "http://localhost:50051";
    public int OverlayPort { get; set; } = 7891;
    public List<EventAction> Actions { get; set; } = BuildDefaultActions();

    private static List<EventAction> BuildDefaultActions() =>
        KnownEventTypes.All.Select(t => new EventAction { EventType = t }).ToList();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    // Ensure every known event type has an entry (handles new event types after upgrade)
                    foreach (var eventType in KnownEventTypes.All)
                    {
                        if (!loaded.Actions.Any(a => a.EventType == eventType))
                            loaded.Actions.Add(new EventAction { EventType = eventType });
                    }
                    return loaded;
                }
            }
        }
        catch { /* fall through to defaults */ }

        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class KnownEventTypes
{
    public static readonly string[] All =
    [
        "MapLoading", "MatchFound", "RaidStarting", "RaidStarted",
        "RaidExited", "RaidEnded", "ExitedPostRaidMenus",
        "TaskStarted", "TaskFailed", "TaskFinished",
        "FleaSold", "FleaOfferExpired",
        "GroupInviteAccept", "GroupUserLeave",
        "InitialReadComplete", "ProfileChanged",
        "PlayerPosition", "ControlSettings",
    ];
}
