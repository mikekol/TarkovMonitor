using System.Net;
using System.Text.Json;
using System.Transactions;
using Refit;

// TO DO: Implement rate limit policy of 15 requests per minute

namespace TarkovMonitor
{
    internal class TarkovTracker
    {
        internal interface ITarkovTrackerAPI
        {
            HttpClient Client { get; }

            [Get("/token")]
            [Headers("Authorization: Bearer {token}")]
            Task<TokenResponse> TestToken(string token);

            [Get("/progress")]
            [Headers("Authorization: Bearer")]
            Task<ProgressResponse> GetProgress();

            [Post("/progress/task/{id}")]
            [Headers("Authorization: Bearer")]
            Task<string> SetTaskStatus(string id, [Body] TaskStatusBody body);

            [Post("/progress/tasks")]
            [Headers("Authorization: Bearer")]
            Task<string> SetTaskStatuses([Body] List<TaskStatusBody> body);
        }

        private static ITarkovTrackerAPI api;

        public static ProgressResponse Progress { get; private set; } = new();
        public static bool ValidToken { get; private set; } = false;
        private static Dictionary<string, string> tokens = new();
        private static string currentProfile = "";
        public static string CurrentProfileId { get { return currentProfile; } }

        public static event EventHandler<EventArgs>? TokenValidated;
        public static event EventHandler<EventArgs>? TokenInvalid;
        public static event EventHandler<EventArgs>? ProgressRetrieved;

        /// <summary>Display names for the selectable TarkovTracker service hosts.</summary>
        public static Dictionary<string, string> Domains = new() {
            { "tarkovtracker.io", "TarkovTracker.io" },
            { "tarkovtracker.org", "TarkovTracker.org" },
        };

        /// <summary>Per-profile host override keyed by EFT profile ID.</summary>
        private static Dictionary<string, string> _profileDomains = new();

        static TarkovTracker() {
            tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(Properties.Settings.Default.tarkovTrackerTokens) ?? tokens;
            _profileDomains = JsonSerializer.Deserialize<Dictionary<string, string>>(Properties.Settings.Default.tarkovTrackerDomains) ?? _profileDomains;
            api = InitAPI();
        }

        /// <summary>
        /// Returns the TarkovTracker domain for <paramref name="profileId"/>, defaulting to
        /// <c>tarkovtracker.io</c> when no override has been stored.
        /// </summary>
        public static string GetDomain(string profileId) =>
            _profileDomains.TryGetValue(profileId, out var d) && !string.IsNullOrEmpty(d)
                ? d : "tarkovtracker.io";

        /// <summary>
        /// Stores a domain override for <paramref name="profileId"/> and persists it to
        /// <see cref="Properties.Settings.Default.tarkovTrackerDomains"/>.
        /// Reinitialises the API client when the override affects the currently active profile.
        /// </summary>
        public static void SetDomain(string profileId, string domain)
        {
            _profileDomains[profileId] = domain;
            Properties.Settings.Default.tarkovTrackerDomains = JsonSerializer.Serialize(_profileDomains);
            Properties.Settings.Default.Save();
            if (profileId == currentProfile)
                InitAPI();
        }

        /// <summary>Read-only snapshot of all stored per-profile domain overrides.</summary>
        public static IReadOnlyDictionary<string, string> AllProfileDomains => _profileDomains;

        /// <summary>
        /// (Re)initialises the Refit API client using the domain configured for the currently
        /// active profile.  Call this when the active profile changes or its domain is updated.
        /// </summary>
        public static ITarkovTrackerAPI InitAPI()
        {
            var domain = GetDomain(currentProfile);
            api = RestService.For<ITarkovTrackerAPI>($"https://{domain}/api/v2",
                new RefitSettings {
                    AuthorizationHeaderValueGetter = (rq, cr) => {
                        return Task.Run<string>(() => {
                            return GetToken(currentProfile ?? "");
                        });
                    },
                }
            );
            api.Client.DefaultRequestHeaders.UserAgent.TryParseAdd($"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name} {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            return api;
        }

        /// <summary>Read-only snapshot of all stored per-profile tokens.</summary>
        public static IReadOnlyDictionary<string, string> AllTokens => tokens;

        public static string GetToken(string profileId) =>
            tokens.TryGetValue(profileId, out var t) ? t : "";

        public static void SetToken(string profileId, string token)
        {
            if (profileId == "")
            {
                throw new Exception("No PVP or PVE profile initialized, please launch Escape from Tarkov first");
            }
            tokens[profileId] = token;
            Properties.Settings.Default.tarkovTrackerTokens = JsonSerializer.Serialize(tokens);
            Properties.Settings.Default.Save();
        }

        public static async Task<ProgressResponse> SetProfile(string profileId)
        {
            if (profileId == "")
                return Progress; // EFT hasn't provided a profile yet — not an error, just not ready

            // One-time migration: if this profile has no domain stored yet but the legacy
            // single-string setting has a value, seed the per-profile entry from it.
            if (!_profileDomains.ContainsKey(profileId) && !string.IsNullOrEmpty(Properties.Settings.Default.tarkovTrackerDomain))
                SetDomain(profileId, Properties.Settings.Default.tarkovTrackerDomain);

            if (currentProfile == profileId)
                return Progress;

            var oldDomain = GetDomain(currentProfile);
            var newToken  = GetToken(profileId);
            var oldToken  = GetToken(currentProfile);
            currentProfile = profileId;

            // Reinit the API client when switching to a profile that uses a different domain.
            if (GetDomain(profileId) != oldDomain)
                InitAPI();

            if (oldToken == newToken)
                return Progress;

            if (newToken == "" || newToken.Length != 22)
            {
                ValidToken = false;
                Progress = new();
                return Progress;
            }
            await TestToken(newToken);
            return Progress;
        }

        private static void SyncStoredStatus(string questId, TaskStatus status)
        {
            var storedStatus = Progress.data.tasksProgress.Find(ts => ts.id == questId);
            if (storedStatus == null)
            {
                storedStatus = new()
                {
                    id = questId,
                };
                Progress.data.tasksProgress.Add(storedStatus);
            }
            if (status == TaskStatus.Finished && !storedStatus.complete)
            {
                storedStatus.complete = true;
                storedStatus.failed = false;
                storedStatus.invalid = false;
            }
            if (status == TaskStatus.Failed && !storedStatus.failed)
            {
                storedStatus.complete = false;
                storedStatus.failed = true;
                storedStatus.invalid = false;
            }
            if (status == TaskStatus.Started && (storedStatus.failed || storedStatus.invalid || storedStatus.complete))
            {
                storedStatus.complete = false;
                storedStatus.failed = false;
                storedStatus.invalid = false;
            }
        }

        public static async Task<string> SetTaskStatus(string questId, TaskStatus status)
        {
            if (!ValidToken)
            {
                throw new Exception("Invalid token");
            }
            try
            {
                await api.SetTaskStatus(questId, TaskStatusBody.From(status));
                SyncStoredStatus(questId, status);
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"TarkovTracker API error: {ex.Message}");
            }
            return "success";
        }

        public static async Task<string> SetTaskComplete(string questId)
        {
            await SetTaskStatus(questId, TaskStatus.Finished);
            try
            {
                TarkovDev.Tasks.ForEach(task => {
                    foreach (var failCondition in task.failConditions)
                    {
                        if (failCondition.task == null)
                        {
                            continue;
                        }
                        if (failCondition.task == questId && failCondition.status.Contains("complete"))
                        {
                            foreach (var taskStatus in Progress.data.tasksProgress)
                            {
                                if (taskStatus.id == failCondition.task)
                                {
                                    taskStatus.failed = true;
                                    break;
                                }
                            }
                            break;
                        }
                    }
                });
            } 
            catch (Exception)
            {
                // do something?
            }
            return "success";
        }

        public static async Task<string> SetTaskFailed(string questId)
        {
            return await SetTaskStatus(questId, TaskStatus.Failed);
        }

        public static async Task<string> SetTaskStarted(string questId)
        {
            foreach (var taskStatus in Progress.data.tasksProgress)
            {
                if (taskStatus.id != questId)
                {
                    continue;
                }
                if (taskStatus.failed)
                {
                    return await SetTaskStatus(questId, TaskStatus.Started);
                }
                break;
            }
            return "task not marked as failed";
        }

        public static async Task<string> SetTaskStatuses(Dictionary<string, TaskStatus> statuses)
        {
			if (!ValidToken)
			{
				throw new Exception("Invalid token");
			}
            List<TaskStatusBody> body = new();
            foreach (var kvp in statuses)
            {
                TaskStatusBody status = TaskStatusBody.From(kvp.Value);
                status.id = kvp.Key;
                body.Add(status);
            }
			try
			{
				await api.SetTaskStatuses(body);
                foreach( var kvp in statuses)
                {
                    SyncStoredStatus(kvp.Key, kvp.Value);
                }
			}
			catch (ApiException ex)
			{
				if (ex.StatusCode == HttpStatusCode.Unauthorized)
				{
					InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
			}
			catch (Exception ex)
			{
				throw new Exception($"TarkovTracker API error: {ex.Message}");
			}
			return "success";
		}

        public static async Task<ProgressResponse> GetProgress()
		{
			if (!ValidToken)
			{
				throw new Exception("Invalid token");
			}
            try
            {
                Progress = await api.GetProgress();
                ProgressRetrieved?.Invoke(null, new EventArgs());
                return Progress;
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker response code: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"TarkovTracker API error: {ex.Message}");
            }
        }

        public static async Task<TokenResponse> TestToken(string apiToken)
        {
            try
            {
                var response = await api.TestToken(apiToken);
                if (response.permissions.Contains("WP"))
                {
                    ValidToken = true;
                    GetProgress();
                    TokenValidated?.Invoke(null, new EventArgs());
                }
                else
                {
                    Progress = new();
                    ValidToken = false;
                    TokenInvalid?.Invoke(null, new EventArgs());
                }
                return response;
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    InvalidTokenException();
                }
                if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throw new Exception("Rate limited by Tarkov Tracker API");
                }
                throw new Exception($"Invalid TarkovTracker API response code: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new Exception($"TarkovTracker API error: {ex.Message}");
            }
        }

        private static void InvalidTokenException()
        {
            Progress = new();
            ValidToken = false;
            TokenInvalid?.Invoke(null, new EventArgs());
            throw new Exception("Tarkov Tracker API token is invalid");
        }

        public static bool HasAirFilter()
        {
            if (Progress == null)
            {
                return false;
            }
            var airFilterStation = TarkovDev.Stations.Find(s => s.normalizedName == "air-filtering-unit");
            if (airFilterStation == null)
            {
                return false;
            }
            var stationLevel = airFilterStation.levels.FirstOrDefault();
            if (stationLevel == null)
            {
                return false;
            }
            var built = Progress.data.hideoutModulesProgress.Find(m => m.id == stationLevel.id && m.complete);
            return built != null;
        }

        public class TokenResponse
        {
            public List<string> permissions { get; set; }
            public string token { get; set; }
        }

        public class ProgressResponse
        {
            public ProgressResponseData data { get; set; } = new();
            public ProgressResponseMeta meta { get; set; } = new();
        }

        public class ProgressResponseData
        {
            public List<ProgressResponseTask> tasksProgress { get; set; } = new();
            public List<ProgressResponseHideoutModules> hideoutModulesProgress { get; set; } = new();
            public string? displayName { get; set; }
            public string userId { get; set; }
            public int playerLevel { get; set; }
            public int gameEdition { get; set; }
            public string pmcFaction { get; set; }
        }

        public class ProgressResponseTask
        {
            public string id { get; set; }
            public bool complete { get; set; }
            public bool invalid { get; set; }
            public bool failed { get; set; }
        }
        public class ProgressResponseHideoutModules    
        {
            public string id { get; set; }
            public bool complete { get; set; }
        }
        public class ProgressResponseMeta
        {
            public string self { get; set; }
        }
        public class TaskStatusBody
        {
            public string? id { get; set; }
            public string state { get; private set; }
            private TaskStatusBody(string newState)
            {
                state = newState;
            }
            public static TaskStatusBody Completed => new("completed");
            public static TaskStatusBody Uncompleted => new("uncompleted");
            public static TaskStatusBody Failed => new("failed");
            public static TaskStatusBody From(TaskStatus code)
            {
                if (code == TaskStatus.Finished)
                {
                    return TaskStatusBody.Completed;
                }
                if (code == TaskStatus.Failed)
                {
                    return TaskStatusBody.Failed;
                }
                return TaskStatusBody.Uncompleted;
            }
            public static TaskStatusBody From(MessageType messageType)
            {
                return TaskStatusBody.From((TaskStatus)messageType);
            }
        }
    }
}
