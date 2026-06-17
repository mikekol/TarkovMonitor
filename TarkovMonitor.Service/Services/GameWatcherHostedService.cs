using TarkovMonitor;
using TarkovMonitor.Service.Contracts;

namespace TarkovMonitor.Service.Services;

public class GameWatcherHostedService : BackgroundService
{
    private readonly GameWatcher _gameWatcher;
    private readonly IServiceConfiguration _config;
    private readonly ILogger<GameWatcherHostedService> _logger;

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

            _gameWatcher.Start();
            _logger.LogInformation("GameWatcher started, logs path: {LogsPath}", _gameWatcher.LogsPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting GameWatcher");
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
