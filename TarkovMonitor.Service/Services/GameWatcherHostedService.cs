using System.Text.Json;
using TarkovMonitor;
using TarkovMonitor.Service.Contracts;

namespace TarkovMonitor.Service.Services;

public class GameWatcherHostedService : BackgroundService
{
    private readonly GameWatcher _gameWatcher;
    private readonly IServiceConfiguration _config;
    private readonly ILogger<GameWatcherHostedService> _logger;

    private static readonly HttpClient _tarkovDevClient = new()
    {
        BaseAddress = new Uri("https://json.tarkov.dev"),
    };

    public GameWatcherHostedService(GameWatcher gameWatcher, IServiceConfiguration config, ILogger<GameWatcherHostedService> logger)
    {
        _gameWatcher = gameWatcher;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _config.LoadAsync();

            // Only override if the user has configured a custom path in appsettings.json.
            // Otherwise GameWatcher auto-detects via the EFT install registry key.
            if (!string.IsNullOrEmpty(_config.CustomLogsPath))
                _gameWatcher.LogsPath = _config.CustomLogsPath;

            // Apply the custom map fallback — used when a screenshot is taken outside an active raid.
            _gameWatcher.CustomMap = _config.CustomMap;

            // Screenshot watching runs in the UI process (interactive user session) because
            // LocalService cannot access the user's Documents folder.
            _gameWatcher.EnableScreenshotWatching = false;

            // Populate the map list so GameWatcher can resolve scene paths and nameIds.
            await TryLoadMapsAsync(stoppingToken);

            _gameWatcher.Start();
            _logger.LogInformation("GameWatcher started, logs path: {LogsPath}", _gameWatcher.LogsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting GameWatcher");
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task TryLoadMapsAsync(CancellationToken ct)
    {
        try
        {
            var response = await _tarkovDevClient.GetAsync("/regular/maps", ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var mapsDict = doc.RootElement.GetProperty("data").GetProperty("maps");
            var maps = new List<Map>();
            foreach (var entry in mapsDict.EnumerateObject())
            {
                var m = entry.Value;
                var bosses = new List<BossSpawn>();
                if (m.TryGetProperty("bosses", out var bossesEl))
                {
                    foreach (var b in bossesEl.EnumerateArray())
                    {
                        var escorts = new List<BossEscort>();
                        if (b.TryGetProperty("escorts", out var escortsEl))
                            foreach (var e in escortsEl.EnumerateArray())
                                escorts.Add(new BossEscort { mob = e.GetProperty("mob").GetString() ?? "" });
                        bosses.Add(new BossSpawn
                        {
                            mob = b.GetProperty("mob").GetString() ?? "",
                            escorts = escorts
                        });
                    }
                }
                maps.Add(new Map
                {
                    id = m.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    name = m.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    nameId = m.TryGetProperty("nameId", out var nameId) ? nameId.GetString() ?? "" : "",
                    normalizedName = m.TryGetProperty("normalizedName", out var nn) ? nn.GetString() ?? "" : "",
                    scenePath = m.TryGetProperty("scenePath", out var sp) ? sp.GetString() ?? "" : "",
                    bosses = bosses,
                });
            }
            _gameWatcher.Maps = maps;
            _logger.LogInformation("Loaded {Count} maps from tarkov.dev", maps.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load maps from tarkov.dev; map-dependent features (MapLoading events, goon detection) will be unavailable until next restart");
        }
    }
}
