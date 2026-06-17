namespace TarkovMonitor.Service.Services;

/// <summary>
/// Hosted service that listens for EFT task events from <see cref="GameWatcher"/> and calls the
/// TarkovTracker API to keep the user's quest progress in sync — even when the UI is not running.
/// </summary>
/// <remarks>
/// <para>
/// This service solves the "missing client" problem: previously, TarkovTracker was only updated
/// while the UI was open.  If the user completed a quest with the UI closed, the progress was
/// permanently lost until manually corrected.
/// </para>
/// <para>
/// A new <see cref="TarkovTrackerClient"/> is constructed per event so there is no stale-token
/// issue when the user rotates credentials at runtime.  The network round-trip cost is negligible
/// compared to the infrequency of task events.
/// </para>
/// <para>
/// The updater is a no-op if no token is configured for the active profile
/// (<see cref="IServiceConfiguration.HasTokenForProfile"/>), so it is always safe to register
/// even before TarkovTracker is set up.
/// </para>
/// </remarks>
public class TarkovTrackerUpdaterService : IHostedService
{
    private readonly GameWatcher _gameWatcher;
    private readonly IServiceConfiguration _config;
    private readonly ILogger<TarkovTrackerUpdaterService> _logger;

    /// <summary>
    /// Initialises the updater with its dependencies injected by the DI container.
    /// </summary>
    /// <param name="gameWatcher">
    /// The singleton <see cref="GameWatcher"/> instance.  Events are subscribed on
    /// <see cref="StartAsync"/> and unsubscribed on <see cref="StopAsync"/>.
    /// </param>
    /// <param name="config">Service configuration providing per-profile tokens and domain.</param>
    /// <param name="logger">Structured logger for API call outcomes.</param>
    public TarkovTrackerUpdaterService(
        GameWatcher gameWatcher,
        IServiceConfiguration config,
        ILogger<TarkovTrackerUpdaterService> logger)
    {
        _gameWatcher = gameWatcher;
        _config      = config;
        _logger      = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _gameWatcher.TaskFinished += OnTaskFinished;
        _gameWatcher.TaskFailed   += OnTaskFailed;
        _gameWatcher.TaskStarted  += OnTaskStarted;
        _logger.LogInformation("TarkovTrackerUpdaterService started — listening for task events");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gameWatcher.TaskFinished -= OnTaskFinished;
        _gameWatcher.TaskFailed   -= OnTaskFailed;
        _gameWatcher.TaskStarted  -= OnTaskStarted;
        return Task.CompletedTask;
    }

    // ── Event handlers ────────────────────────────────────────────────────────
    // async void is intentional for event handlers: exceptions are caught and logged,
    // and the fire-and-forget pattern is acceptable here since task events are rare and
    // the caller (GameWatcher) does not await the handler.

    private async void OnTaskFinished(object? sender, LogContentEventArgs<TaskStatusMessageLogContent> e)
    {
        await UpdateTarkovTracker(e.LogContent.TaskId, async client =>
        {
            await client.SetTaskComplete(e.LogContent.TaskId);
            _logger.LogInformation("Marked task {TaskId} as complete in TarkovTracker", e.LogContent.TaskId);
        });
    }

    private async void OnTaskFailed(object? sender, LogContentEventArgs<TaskStatusMessageLogContent> e)
    {
        await UpdateTarkovTracker(e.LogContent.TaskId, async client =>
        {
            await client.SetTaskFailed(e.LogContent.TaskId);
            _logger.LogInformation("Marked task {TaskId} as failed in TarkovTracker", e.LogContent.TaskId);
        });
    }

    private async void OnTaskStarted(object? sender, LogContentEventArgs<TaskStatusMessageLogContent> e)
    {
        await UpdateTarkovTracker(e.LogContent.TaskId, async client =>
        {
            await client.SetTaskStarted(e.LogContent.TaskId);
            _logger.LogInformation("Marked task {TaskId} as started in TarkovTracker", e.LogContent.TaskId);
        });
    }

    /// <summary>
    /// Resolves the token for the current profile and, if present, invokes
    /// <paramref name="action"/> with a freshly constructed <see cref="TarkovTrackerClient"/>.
    /// Swallows and logs any exceptions so that a transient API failure does not crash the service.
    /// </summary>
    /// <param name="taskId">Task identifier, used in error log messages.</param>
    /// <param name="action">The async operation to perform against the client.</param>
    private async Task UpdateTarkovTracker(string taskId, Func<TarkovTrackerClient, Task> action)
    {
        var profileId = GameWatcher.CurrentProfile.Id;
        if (string.IsNullOrEmpty(profileId))
        {
            _logger.LogDebug("Skipping TarkovTracker update for task {TaskId}: no active profile", taskId);
            return;
        }

        var token = _config.GetTokenForProfile(profileId);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("Skipping TarkovTracker update for task {TaskId}: no token for profile {ProfileId}", taskId, profileId);
            return;
        }

        try
        {
            var client = new TarkovTrackerClient(_config.GetDomainForProfile(profileId), token);
            await action(client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TarkovTracker API call failed for task {TaskId} (profile {ProfileId})", taskId, profileId);
        }
    }
}
