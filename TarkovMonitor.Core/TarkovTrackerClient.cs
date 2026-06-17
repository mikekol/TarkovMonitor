using System.Net;
using Refit;

namespace TarkovMonitor;

/// <summary>
/// Stateless HTTP client for the TarkovTracker v2 API.
/// Each instance is bound to a single (domain, token) pair; create a new instance when either changes.
/// </summary>
/// <remarks>
/// The Service's <c>TarkovTrackerUpdaterService</c> constructs a fresh instance per event using the
/// profile-specific token from <c>IServiceConfiguration.TarkovTrackerTokens</c>.  This avoids the
/// static-state and <c>Properties.Settings</c> coupling in the UI's <c>TarkovTracker</c> class,
/// and makes the client suitable for Native AOT (Refit v11 is source-generator based).
/// </remarks>
public class TarkovTrackerClient
{
    private readonly ITarkovTrackerApi _api;

    /// <summary>
    /// Initialises the client for the given <paramref name="domain"/> and bearer <paramref name="token"/>.
    /// </summary>
    /// <param name="domain">Host name of the TarkovTracker instance, e.g. <c>tarkovtracker.io</c>.</param>
    /// <param name="token">API bearer token for the profile being tracked.</param>
    public TarkovTrackerClient(string domain, string token)
    {
        _api = RestService.For<ITarkovTrackerApi>(
            $"https://{domain}/api/v2",
            new RefitSettings
            {
                // Inject the token on every request; avoids per-method header boilerplate.
                AuthorizationHeaderValueGetter = (_, _) => Task.FromResult(token)
            });
    }

    /// <summary>Marks the specified task as completed in TarkovTracker.</summary>
    /// <param name="taskId">TarkovDev task identifier (e.g. <c>"5e383a6386f77465910ce1f3"</c>).</param>
    public Task SetTaskComplete(string taskId) =>
        _api.SetTaskStatus(taskId, TaskStatusBody.Completed);

    /// <summary>Marks the specified task as failed in TarkovTracker.</summary>
    /// <param name="taskId">TarkovDev task identifier.</param>
    public Task SetTaskFailed(string taskId) =>
        _api.SetTaskStatus(taskId, TaskStatusBody.Failed);

    /// <summary>
    /// Resets the specified task to the uncompleted/started state in TarkovTracker.
    /// Only meaningful when the task was previously marked as failed.
    /// </summary>
    /// <param name="taskId">TarkovDev task identifier.</param>
    public Task SetTaskStarted(string taskId) =>
        _api.SetTaskStatus(taskId, TaskStatusBody.Uncompleted);

    // ── Refit interface ──────────────────────────────────────────────────────

    /// <summary>Refit interface for the TarkovTracker v2 REST API.</summary>
    internal interface ITarkovTrackerApi
    {
        /// <summary>Updates the completion state of a single task.</summary>
        [Post("/progress/task/{id}")]
        [Headers("Authorization: Bearer")]
        Task<string> SetTaskStatus(string id, [Body] TaskStatusBody body);
    }
}

/// <summary>
/// Request body for the TarkovTracker <c>POST /progress/task/{id}</c> endpoint.
/// Use the static factory properties (<see cref="Completed"/>, <see cref="Failed"/>,
/// <see cref="Uncompleted"/>) rather than constructing directly.
/// </summary>
public sealed class TaskStatusBody
{
    /// <summary>Gets the API state string sent in the request body.</summary>
    public string state { get; }

    private TaskStatusBody(string newState) { state = newState; }

    /// <summary>Body that marks a task as completed.</summary>
    public static TaskStatusBody Completed => new("completed");

    /// <summary>Body that marks a task as failed.</summary>
    public static TaskStatusBody Failed => new("failed");

    /// <summary>Body that resets a task to uncompleted/not-started.</summary>
    public static TaskStatusBody Uncompleted => new("uncompleted");

    /// <summary>
    /// Returns the body that corresponds to the given <see cref="TaskStatus"/> code.
    /// </summary>
    /// <param name="code">The task status from the EFT log message.</param>
    public static TaskStatusBody From(TaskStatus code) => code switch
    {
        TaskStatus.Finished => Completed,
        TaskStatus.Failed   => Failed,
        _                   => Uncompleted
    };
}
