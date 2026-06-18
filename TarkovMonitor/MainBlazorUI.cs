using MudBlazor.Services;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using TarkovMonitor.GroupLoadout;
using System.Globalization;
using System.ComponentModel;
using MudBlazor;
using Microsoft.Extensions.Localization;
using System.Text.Json.Nodes;
using TarkovMonitor.Services;

namespace TarkovMonitor
{
    public partial class MainBlazorUI : Form
    {
        private readonly GameEventClient _gameClient;
        private readonly MessageLog messageLog;
        private readonly LogRepository logRepository;
        private readonly GroupManager groupManager;
        private readonly TimersManager timersManager;
        private readonly System.Timers.Timer runthroughTimer;
        private readonly System.Timers.Timer scavCooldownTimer;
        private LocalizationService localizationService;
        private bool inRaid;
        private RaidInfo _currentRaidInfo = new();
        private FileSystemWatcher? _screenshotWatcher;

        private static string ScreenshotsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Escape From Tarkov", "Screenshots");

        public MainBlazorUI()
        {
            InitializeComponent();
            if (Properties.Settings.Default.upgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.upgradeRequired = false;
                Properties.Settings.Default.Save();
            }
            this.TopMost = Properties.Settings.Default.stayOnTop;
            inRaid = false;

            messageLog = new MessageLog();
            messageLog.AddMessage($"TarkovMonitor v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");

            logRepository = new LogRepository();
            groupManager = new GroupManager();

            _gameClient = new GameEventClient();
            timersManager = new TimersManager(_gameClient);

            var services = new ServiceCollection();
            services.AddWindowsFormsBlazorWebView();
            services.AddMudServices();
            services.AddLocalization();
            services.AddSingleton<LocalizationService>();
            services.AddSingleton<MessageLog>(messageLog);
            services.AddSingleton<LogRepository>(logRepository);
            services.AddSingleton<GroupManager>(groupManager);
            services.AddSingleton<TimersManager>(timersManager);

            blazorWebView1.HostPage = "wwwroot\\index.html";
            var serviceProvider = services.BuildServiceProvider();
            blazorWebView1.Services = serviceProvider;
            localizationService = serviceProvider.GetRequiredService<LocalizationService>();
            blazorWebView1.RootComponents.Add<TarkovMonitor.Blazor.App>("#app");

            // Wire up all events before connecting
            _gameClient.RaidStarting += Eft_RaidStarting;
            _gameClient.RaidStarted += Eft_RaidStart;
            _gameClient.RaidExited += Eft_RaidExited;
            _gameClient.RaidEnded += Eft_RaidEnded;
            _gameClient.ExitedPostRaidMenus += Eft_ExitedPostRaidMenus;
            _gameClient.TaskStarted += Eft_TaskStarted;
            _gameClient.TaskFailed += Eft_TaskFailed;
            _gameClient.TaskFinished += Eft_TaskFinished;
            _gameClient.FleaSold += Eft_FleaSold;
            _gameClient.FleaOfferExpired += Eft_FleaOfferExpired;
            _gameClient.DebugMessage += Eft_DebugMessage;
            _gameClient.ExceptionThrown += Eft_ExceptionThrown;
            _gameClient.NewLogData += Eft_NewLogData;
            _gameClient.GroupInviteAccept += Eft_GroupInviteAccept;
            _gameClient.GroupUserLeave += Eft_GroupUserLeave;
            _gameClient.MapLoading += Eft_MapLoading;
            _gameClient.MapLoading += Eft_MapLoading_NavigateToMap;
            _gameClient.MatchFound += Eft_MatchFound;
            _gameClient.PlayerPosition += Eft_PlayerPosition;
            _gameClient.ProfileChanged += Eft_ProfileChanged;
            _gameClient.ControlSettings += Eft_ControlSettings;

            SetupScreenshotWatcher();

            _gameClient.InitialReadComplete += (object? sender, ProfileEventArgs e) =>
            {
                GameWatcher.CurrentProfile = e.Profile;
                UpdateTarkovDevApiData();
                TarkovDev.StartAutoUpdates();
                TarkovDev.UpdatePlayerNames();

                if (Properties.Settings.Default.tarkovTrackerToken != "" && e.Profile.Id != "")
                {
                    try
                    {
                        TarkovTracker.SetToken(e.Profile.Id, Properties.Settings.Default.tarkovTrackerToken);
                    }
                    catch (Exception ex)
                    {
                        messageLog.AddMessage($"Error setting token from previously saved settings {ex.Message}", "exception");
                    }
                    Properties.Settings.Default.tarkovTrackerToken = "";
                    Properties.Settings.Default.Save();
                }
                InitializeProgress();
            };

            _gameClient.ConnectionStateChanged += async (object? sender, ConnectionStateChangedEventArgs e) =>
            {
                if (e.IsConnected)
                {
                    messageLog.AddMessage("Connected to TarkovMonitor service", "info");
                }
                else
                {
                    messageLog.AddMessage($"Service disconnected: {e.Error}. Reconnecting...", "exception");
                    await Task.Delay(5000);
                    _ = ConnectToServiceAsync();
                }
            };

            Properties.Settings.Default.PropertyChanged += (object? sender, PropertyChangedEventArgs e) =>
            {
                if (e.PropertyName == "stayOnTop")
                    this.TopMost = Properties.Settings.Default.stayOnTop;

                if (e.PropertyName == "customLogsPath" || e.PropertyName == "customMap")
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _gameClient.UpdateConfigAsync(
                                Properties.Settings.Default.customLogsPath,
                                Properties.Settings.Default.customMap);
                        }
                        catch { }
                    });
                }

                if (e.PropertyName == "tarkovTrackerTokens")
                {
                    var tokens = TarkovTracker.AllTokens;
                    if (tokens.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _gameClient.UpdateConfigAsync(
                                    Properties.Settings.Default.customLogsPath,
                                    tarkovTrackerTokens: new Dictionary<string, string>(tokens));
                            }
                            catch { }
                        });
                    }
                }

                if (e.PropertyName == "tarkovTrackerDomains")
                {
                    var domains = TarkovTracker.AllProfileDomains;
                    if (domains.Count > 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _gameClient.UpdateConfigAsync(
                                    Properties.Settings.Default.customLogsPath,
                                    tarkovTrackerDomains: new Dictionary<string, string>(domains));
                            }
                            catch { }
                        });
                    }
                }
            };

            TarkovTracker.ProgressRetrieved += TarkovTracker_ProgressRetrieved;
            UpdateCheck.NewVersion += UpdateCheck_NewVersion;
            UpdateCheck.Error += UpdateCheck_Error;
            SocketClient.ExceptionThrown += SocketClient_ExceptionThrown;
            UpdateCheck.CheckForNewVersion();

            blazorWebView1.WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

            runthroughTimer = new System.Timers.Timer(Properties.Settings.Default.runthroughTime.TotalMilliseconds)
            {
                AutoReset = false,
                Enabled = false
            };
            runthroughTimer.Elapsed += RunthroughTimer_Elapsed;

            scavCooldownTimer = new System.Timers.Timer(TimeSpan.FromSeconds(TarkovDev.ScavCooldownSeconds()).TotalMilliseconds)
            {
                AutoReset = false,
                Enabled = false
            };
            scavCooldownTimer.Elapsed += ScavCooldownTimer_Elapsed;

            // Connect to service asynchronously
            _ = ConnectToServiceAsync();
        }

        private async Task ConnectToServiceAsync()
        {
            while (true)
            {
                try
                {
                    messageLog.AddMessage("Connecting to TarkovMonitor service...", "info");
                    await _gameClient.ConnectAsync();

                    // Push user-session config that the service can't resolve on its own.
                    await _gameClient.UpdateConfigAsync(
                        Properties.Settings.Default.customLogsPath,
                        Properties.Settings.Default.customMap);

                    return;
                }
                catch (Exception ex)
                {
                    messageLog.AddMessage($"Could not connect to service: {ex.Message}. Retrying in 5 seconds...", "exception");
                    await Task.Delay(5000);
                }
            }
        }

        private void Eft_ControlSettings(object? sender, ControlSettingsEventArgs e)
        {
            try
            {
                JsonArray keyBindings = e.ControlSettings["keyBindings"].AsArray();
                JsonNode screenshotBind = keyBindings.FirstOrDefault((n) => n.AsObject()["keyName"].ToString() == "MakeScreenshot" && n.AsObject()["variants"].AsArray().Any(variant => variant.AsObject()["isAxis"]?.GetValue<bool>() == true || variant.AsObject()["keyCode"].AsArray().Count > 0));
                if (screenshotBind == null)
                {
                    messageLog.AddMessage($"Screenshot key is not bound in EFT. Using this keybind is required to update tarkov.dev map position.", "info");
                    return;
                }
                var variant = screenshotBind["variants"].AsArray().FirstOrDefault(variant => variant.AsObject()["keyCode"].AsArray().Count > 0);
                if (variant == null) return;
                var keys = variant["keyCode"].AsArray().Select(n => n.GetValue<string>());
                if (keys.Any(key => key == "SysReq"))
                    messageLog.AddMessage($"Screenshot key is not properly bound in EFT. Please re-bind your screenshot key in EFT for use with updating tarkov.dev map position.", "info");
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error checking screenshot keybind: {ex.Message} {ex.StackTrace}", "exception");
            }
        }

        private void Eft_ProfileChanged(object? sender, ProfileEventArgs e)
        {
            GameWatcher.CurrentProfile = e.Profile;
            if (e.Profile.Id == TarkovTracker.CurrentProfileId) return;
            messageLog.AddMessage(string.Format(localizationService.GetString("UsingProfile"), e.Profile.Type));
            _ = TarkovTracker.SetProfile(e.Profile.Id);
        }

        private void Eft_ExitedPostRaidMenus(object? sender, RaidInfoEventArgs e)
        {
            if (Properties.Settings.Default.airFilterAlert && TarkovTracker.HasAirFilter())
                Sound.Play("air_filter_off");
        }

        private void ScavCooldownTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (!Properties.Settings.Default.scavCooldownAlert) return;
            if (!inRaid) Sound.Play("scav_available");
            messageLog.AddMessage("Player scav available", "info");
        }

        private void RunthroughTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (Properties.Settings.Default.runthroughAlert)
            {
                Sound.Play("runthrough_over");
                messageLog.AddMessage("Runthrough period over", "info");
            }
        }

        private void Delete_Screenshots(RaidInfoEventArgs e, MonitorMessage? monMessage = null, MonitorMessageButton? screenshotButton = null)
        {
            try
            {
                foreach (var filename in e.RaidInfo.Screenshots)
                    File.Delete(Path.Combine(ScreenshotsPath, filename));
                messageLog.AddMessage($"Deleted {e.RaidInfo.Screenshots.Count} screenshots");
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error deleting screenshot: {ex.Message} {ex.StackTrace}", "exception");
            }

            if (monMessage is null || screenshotButton is null) return;
            monMessage.Buttons.Remove(screenshotButton);
        }

        private void Handle_Screenshots(RaidInfoEventArgs e, MonitorMessage monMessage)
        {
            if (Properties.Settings.Default.automaticallyDeleteScreenshotsAfterRaid)
            {
                Delete_Screenshots(e);
                return;
            }

            MonitorMessageButton screenshotButton = new($"Delete {e.RaidInfo.Screenshots.Count} Screenshots", Icons.Material.Filled.Delete);
            screenshotButton.OnClick = () => Delete_Screenshots(e, monMessage, screenshotButton);
            screenshotButton.Timeout = TimeSpan.FromMinutes(120).TotalMilliseconds;
            monMessage.Buttons.Add(screenshotButton);
        }

        private void Eft_RaidEnded(object? sender, RaidInfoEventArgs e)
        {
            inRaid = false;
            _currentRaidInfo = e.RaidInfo;
            var mapName = e.RaidInfo.Map;
            var map = TarkovDev.Maps.Find(m => m.nameId == mapName);
            if (map != null) mapName = map.name;
            MonitorMessage monMessage = new($"Ended {mapName} raid");

            if (e.RaidInfo.Screenshots.Count > 0)
                Handle_Screenshots(e, monMessage);

            messageLog.AddMessage(monMessage);
            runthroughTimer.Stop();
            if (Properties.Settings.Default.scavCooldownAlert && (e.RaidInfo.RaidType == RaidType.Scav || e.RaidInfo.RaidType == RaidType.PVE))
            {
                scavCooldownTimer.Stop();
                scavCooldownTimer.Interval = TimeSpan.FromSeconds(TarkovDev.ResetScavCoolDown()).TotalMilliseconds;
                scavCooldownTimer.Start();
            }
        }

        private void Eft_GroupUserLeave(object? sender, GroupUserLeaveEventArgs e)
        {
            return;
        }

        private void SocketClient_ExceptionThrown(object? sender, ExceptionEventArgs e)
        {
            messageLog.AddMessage($"Error {e.Context}: {e.Exception.Message}\n{e.Exception.StackTrace}", "exception");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                if (Properties.Settings.Default.minimizeAtStartup)
                    WindowState = FormWindowState.Minimized;
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error minimizing at startup: {ex.Message} {ex.StackTrace}", "exception");
            }
        }

        private void SetupScreenshotWatcher()
        {
            try
            {
                bool pathExists = Directory.Exists(ScreenshotsPath);
                if (pathExists)
                {
                    _screenshotWatcher = new FileSystemWatcher(ScreenshotsPath, "*.png");
                    _screenshotWatcher.Created += ScreenshotWatcher_Created;
                    _screenshotWatcher.EnableRaisingEvents = true;
                }
                else
                {
                    var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    _screenshotWatcher = new FileSystemWatcher(docsPath)
                    {
                        IncludeSubdirectories = true
                    };
                    _screenshotWatcher.Created += ScreenshotWatcher_FolderCreated;
                    _screenshotWatcher.Renamed += ScreenshotWatcher_FolderCreated;
                    _screenshotWatcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error initializing screenshot watcher: {ex.Message}", "exception");
            }
        }

        private void ScreenshotWatcher_FolderCreated(object sender, FileSystemEventArgs e)
        {
            if (string.Equals(e.FullPath, ScreenshotsPath, StringComparison.OrdinalIgnoreCase))
            {
                _screenshotWatcher?.Dispose();
                SetupScreenshotWatcher();
            }
        }

        private void ScreenshotWatcher_Created(object sender, FileSystemEventArgs e)
        {
            try
            {
                string filename = e.Name ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(filename,
                    @"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_?(?<position>.+) \(\d\)\.png");
                if (!match.Success) return;

                var position = System.Text.RegularExpressions.Regex.Match(match.Groups["position"].Value,
                    @"(?<x>-?[\d]+\.[\d]{2}), (?<y>-?[\d]+\.[\d]{2}), (?<z>-?[\d]+\.[\d]{2})_?(?<rx>-?[\d.]{1}\.[\d]{1,5}), (?<ry>-?[\d.]{1}\.[\d]{1,5}), (?<rz>-?[\d.]{1}\.[\d]{1,5}), (?<rw>-?[\d.]{1}\.[\d]{1,5})");
                if (!position.Success) return;

                var raid = _currentRaidInfo;
                if (string.IsNullOrEmpty(raid.Map) && !string.IsNullOrEmpty(Properties.Settings.Default.customMap))
                    raid = new RaidInfo { Map = Properties.Settings.Default.customMap };
                if (string.IsNullOrEmpty(raid.Map)) return;

                var rotation = GameWatcher.QuarternionsToYaw(
                    float.Parse(position.Groups["rx"].Value, CultureInfo.InvariantCulture),
                    float.Parse(position.Groups["ry"].Value, CultureInfo.InvariantCulture),
                    float.Parse(position.Groups["rz"].Value, CultureInfo.InvariantCulture),
                    float.Parse(position.Groups["rw"].Value, CultureInfo.InvariantCulture));

                var args = new PlayerPositionEventArgs(
                    raid, GameWatcher.CurrentProfile,
                    new Position(position.Groups["x"].Value, position.Groups["y"].Value, position.Groups["z"].Value),
                    rotation, filename);

                raid.Screenshots.Add(filename);
                Eft_PlayerPosition(this, args);
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error parsing screenshot {e.Name}: {ex.Message}", "exception");
            }
        }

        private async void Eft_PlayerPosition(object? sender, PlayerPositionEventArgs e)
        {
            var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map);
            if (map == null)
            {
                messageLog.AddMessage($"Could not find map {e.RaidInfo.Map}");
                return;
            }
            messageLog.AddMessage($"Player position on {map.name}: x: {e.Position.X}, y: {e.Position.Y}, z: {e.Position.Z}");
            List<JsonObject> socketMessages = new();
            socketMessages.Add(SocketClient.GetPlayerPositionMessage(e));
            if (Properties.Settings.Default.navigateMapOnPositionUpdate)
                socketMessages.Add(SocketClient.GetNavigateToMapMessage(map));
            SocketClient.Send(socketMessages);
        }

        private void UpdateCheck_Error(object? sender, ExceptionEventArgs e)
        {
            messageLog.AddMessage($"Error {e.Context}: {e.Exception.Message}", "exception");
        }

        private void UpdateCheck_NewVersion(object? sender, NewVersionEventArgs e)
        {
            messageLog.AddMessage($"New TarkovMonitor version available ({e.Version})! Click here to open the download page. Please update to this new version before reporting any bugs.", null, e.Uri.ToString());
        }

        private async void Eft_MapLoading(object? sender, RaidInfoEventArgs e)
        {
            if (TarkovTracker.Progress?.data?.tasksProgress == null) return;
            try
            {
                var failedTasks = new List<TarkovDev.Task>();
                foreach (var taskStatus in TarkovTracker.Progress.data.tasksProgress)
                {
                    if (!taskStatus.failed) continue;
                    var task = TarkovDev.Tasks.Find(t => t.id == taskStatus.id);
                    if (task == null || !task.restartable) continue;
                    failedTasks.Add(task);
                }
                if (Properties.Settings.Default.airFilterAlert && TarkovTracker.HasAirFilter())
                    await Sound.Play("air_filter_on");
                if (Properties.Settings.Default.questItemsAlert)
                    await Sound.Play("quest_items");
                if (failedTasks.Count == 0) return;
                foreach (var task in failedTasks)
                    messageLog.AddMessage($"Failed task {task.name} should be restarted", "quest", task.wikiLink);
                if (Properties.Settings.Default.restartTaskAlert)
                    await Sound.Play("restart_failed_tasks");
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error on matching started: {ex.Message}");
            }
        }

        private void Eft_MapLoading_NavigateToMap(object? sender, RaidInfoEventArgs e)
        {
            if (!Properties.Settings.Default.autoNavigateMap) return;
            var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map);
            if (map == null) return;
            SocketClient.NavigateToMap(map);
        }

        private void Eft_GroupInviteAccept(object? sender, GroupInviteAcceptedEventArgs e)
        {
            messageLog.AddMessage($"{e.Nickname} ({e.Side.ToUpper()} {e.Level}) accepted group invite.", "group");
        }

        private void TarkovTracker_ProgressRetrieved(object? sender, EventArgs e)
        {
            messageLog.AddMessage(string.Format(localizationService.GetString("RetrievedDataFromTarkovTracker"), TarkovTracker.Progress.data.displayName, TarkovTracker.Progress.data.playerLevel, TarkovTracker.Progress.data.pmcFaction), "update", $"https://{TarkovTracker.GetDomain(GameWatcher.CurrentProfile.Id)}");
        }

        private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (Debugger.IsAttached) blazorWebView1.WebView.CoreWebView2.OpenDevToolsWindow();
        }

        private async Task UpdateTarkovDevApiData()
        {
            try
            {
                await TarkovDev.UpdateApiData();
                messageLog.AddMessage(string.Format(localizationService.GetString("RetrievedDataFromTarkovDev"), String.Format("{0:n0}", TarkovDev.Items.Count), TarkovDev.Maps.Count, TarkovDev.Traders.Count, TarkovDev.Tasks.Count, TarkovDev.Stations.Count), "update");
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error updating tarkov.dev API data: {ex.Message}");
            }
        }

        private async Task InitializeProgress()
        {
            // Profile ID is empty until EFT starts — silently skip until it's available.
            if (string.IsNullOrEmpty(GameWatcher.CurrentProfile.Id))
                return;

            await TarkovTracker.SetProfile(GameWatcher.CurrentProfile.Id);

            messageLog.AddMessage(string.Format(localizationService.GetString("UsingProfile"), GameWatcher.CurrentProfile.Type));
            if (TarkovTracker.GetToken(GameWatcher.CurrentProfile.Id) == "")
            {
                messageLog.AddMessage(localizationService.GetString("ToAutomaticallyTrackTaskProgress"));
                return;
            }
        }

        private void Eft_MatchFound(object? sender, RaidInfoEventArgs e)
        {
            if (Properties.Settings.Default.matchFoundAlert)
                Sound.Play("match_found");
            var mapName = e.RaidInfo.Map;
            var map = TarkovDev.Maps.Find(m => m.nameId == mapName);
            if (map != null) mapName = map.name;
            messageLog.AddMessage($"Matching complete on {mapName} after {e.RaidInfo.QueueTime} seconds");
        }

        private void Eft_NewLogData(object? sender, NewLogDataEventArgs e)
        {
            TarkovDev.LastActivity = DateTime.Now;
            try
            {
                logRepository.AddLog(e.Data, e.Type.ToString());
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"{ex.GetType().Name} adding raw log to repository: " + ex.StackTrace, "exception");
            }
        }

        private void Eft_TaskFinished(object? sender, TaskEventArgs e)
        {
            var task = TarkovDev.Tasks.Find(t => t.id == e.TaskId);
            if (task == null) return;
            messageLog.AddMessage($"Completed task {task.name}", "quest", $"https://tarkov.dev/task/{task.normalizedName}");
            // TarkovTracker API update is now handled server-side by TarkovTrackerUpdaterService;
            // the Service receives the same TaskFinished event and calls the API even when the UI is closed.
        }

        private void Eft_TaskFailed(object? sender, TaskEventArgs e)
        {
            var task = TarkovDev.Tasks.Find(t => t.id == e.TaskId);
            if (task == null) return;
            messageLog.AddMessage($"Failed task {task.name}", "quest", $"https://tarkov.dev/task/{task.normalizedName}");
            // TarkovTracker API update handled server-side by TarkovTrackerUpdaterService.
        }

        private void Eft_TaskStarted(object? sender, TaskEventArgs e)
        {
            var task = TarkovDev.Tasks.Find(t => t.id == e.TaskId);
            if (task == null) return;
            messageLog.AddMessage($"Started task {task.name}", "quest", $"https://tarkov.dev/task/{task.normalizedName}");
            // TarkovTracker API update handled server-side by TarkovTrackerUpdaterService.
        }

        private void Eft_FleaSold(object? sender, FleaSaleEventArgs e)
        {
            Stats.AddFleaSale(e.Profile.Id, e.SoldItemId, e.Buyer, e.SoldItemCount, e.ReceivedItems);
            if (TarkovDev.Items == null) return;

            List<string> received = new();
            foreach (var receivedId in e.ReceivedItems.Keys)
            {
                if (receivedId == "5449016a4bdc2d6f028b456f")
                {
                    received.Add(e.ReceivedItems[receivedId].ToString("C0", CultureInfo.CreateSpecificCulture("ru-RU")));
                    continue;
                }
                else if (receivedId == "5696686a4bdc2da3298b456a")
                {
                    received.Add(e.ReceivedItems[receivedId].ToString("C0", CultureInfo.CreateSpecificCulture("en-US")));
                    continue;
                }
                else if (receivedId == "569668774bdc2da2298b4568")
                {
                    received.Add(e.ReceivedItems[receivedId].ToString("C0", CultureInfo.CreateSpecificCulture("de-DE")));
                    continue;
                }
                var receivedItem = TarkovDev.Items.Find(item => item.id == receivedId);
                if (receivedItem == null) continue;
                received.Add($"{String.Format("{0:n0}", e.ReceivedItems[receivedId])} {receivedItem.name}");
            }

            var soldItem = TarkovDev.Items.Find(item => item.id == e.SoldItemId);
            if (soldItem == null) return;
            messageLog.AddMessage($"{e.Buyer} purchased {String.Format("{0:n0}", e.SoldItemCount)} {soldItem.name} for {String.Join(", ", received.ToArray())}", "flea", soldItem.link);
        }

        private void Eft_FleaOfferExpired(object? sender, FleaExpiredEventArgs e)
        {
            if (TarkovDev.Items == null) return;
            var unsoldItem = TarkovDev.Items.Find(item => item.id == e.ItemId);
            if (unsoldItem == null) return;
            messageLog.AddMessage($"Your offer for {unsoldItem.name} (x{e.ItemCount}) expired", "flea", unsoldItem.link);
        }

        private void Eft_DebugMessage(object? sender, DebugEventArgs e)
        {
            messageLog.AddMessage(e.Message, "debug");
        }

        private void Eft_ExceptionThrown(object? sender, ExceptionEventArgs e)
        {
            messageLog.AddMessage($"Error {e.Context}: {e.Exception.Message}\n{e.Exception.StackTrace}", "exception");
        }

        private void Eft_RaidStarting(object? sender, RaidInfoEventArgs e)
        {
            if (Properties.Settings.Default.raidStartAlert)
                Sound.Play("raid_starting");
        }

        private async void Eft_RaidStart(object? sender, RaidInfoEventArgs e)
        {
            inRaid = true;
            _currentRaidInfo = e.RaidInfo;
            Stats.AddRaid(e);
            var mapName = e.RaidInfo.Map;
            var map = TarkovDev.Maps.Find(m => m.nameId == mapName);
            if (map != null) mapName = map.name;
            if (!e.RaidInfo.Reconnected && e.RaidInfo.RaidType != RaidType.Unknown)
            {
                MonitorMessage monMessage = new($"Starting {e.RaidInfo.RaidType} raid on {mapName}");
                if (map != null && e.RaidInfo.StartedTime != null && map.HasGoons())
                {
                    AddGoonsButton(monMessage, e.RaidInfo);
                }
                else if (map == null)
                {
                    monMessage.Message = $"Starting {e.RaidInfo.RaidType} raid on:";
                    MonitorMessageSelect select = new();
                    foreach (var gameMap in TarkovDev.Maps)
                        select.Options.Add(new(gameMap.name, gameMap.nameId));
                    select.Placeholder = "Select map";
                    monMessage.Selects.Add(select);
                    MonitorMessageButton mapButton = new("Set map", Icons.Material.Filled.Map);
                    mapButton.OnClick += () =>
                    {
                        if (select.Selected == null) return;
                        e.RaidInfo.Map = select.Selected.Value;
                        monMessage.Message = $"Starting {e.RaidInfo.RaidType} raid on {select.Selected.Text}";
                        monMessage.Buttons.Clear();
                        monMessage.Selects.Clear();
                        if (Properties.Settings.Default.autoNavigateMap)
                        {
                            var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map);
                            if (map == null) return;
                            SocketClient.NavigateToMap(map);
                        }
                    };
                    monMessage.Buttons.Add(mapButton);
                }
                messageLog.AddMessage(monMessage);
                if (Properties.Settings.Default.raidStartAlert && e.RaidInfo.StartingTime == null)
                    Sound.Play("raid_starting");
            }
            else
            {
                messageLog.AddMessage($"Re-entering raid on {mapName}");
            }

            if (Properties.Settings.Default.runthroughAlert && !e.RaidInfo.Reconnected && (e.RaidInfo.RaidType == RaidType.PMC || e.RaidInfo.RaidType == RaidType.PVE))
            {
                runthroughTimer.Stop();
                runthroughTimer.Start();
            }

            if (Properties.Settings.Default.submitQueueTime && e.RaidInfo.QueueTime > 0 && e.RaidInfo.RaidType != RaidType.Unknown)
            {
                try
                {
                    await TarkovDev.PostQueueTime(e.RaidInfo.Map, (int)Math.Round(e.RaidInfo.QueueTime), e.RaidInfo.RaidType.ToString().ToLower(), GameWatcher.CurrentProfile.Type);
                }
                catch (Exception ex)
                {
#if DEBUG
                    messageLog.AddMessage($"Error submitting queue time: {ex.Message}", "exception");
#endif
                }
            }
        }

        private void AddGoonsButton(MonitorMessage monMessage, RaidInfo raidInfo)
        {
            var mapName = raidInfo.Map;
            var map = TarkovDev.Maps.Find(m => m.nameId == mapName);
            if (map != null) mapName = map.name;
            if (map != null && raidInfo.StartedTime != null && map.HasGoons())
            {
                MonitorMessageButton goonsButton = new($"Report Goons", Icons.Material.Filled.Groups);
                goonsButton.OnClick = async () =>
                {
                    try
                    {
                        await TarkovDev.PostGoonsSighting(raidInfo.Map, (DateTime)raidInfo.StartedTime, Int32.Parse(raidInfo.Profile.AccountId), GameWatcher.CurrentProfile.Type);
                        messageLog.AddMessage($"Goons reported on {mapName}", "info");
                    }
                    catch (Exception ex)
                    {
                        messageLog.AddMessage($"Error reporting goons: {ex.Message} {ex.StackTrace}", "exception");
                    }
                    monMessage.Buttons.Remove(goonsButton);
                };
                goonsButton.Confirm = new(
                    $"Report Goons on {mapName}",
                    "<p>Please only submit a report if you saw the goons in this raid.</p><p><strong>Notice:</strong> By submitting a goons report, you consent to collection of your IP address and EFT account id for report verification purposes.</p>",
                    "Submit report", "Cancel"
                );
                goonsButton.Timeout = TimeSpan.FromMinutes(120).TotalMilliseconds;
                monMessage.Buttons.Add(goonsButton);
            }
        }

        private void Eft_RaidExited(object? sender, RaidExitedEventArgs e)
        {
            runthroughTimer.Stop();
            inRaid = false;
            try
            {
                var mapName = e.Map;
                var map = TarkovDev.Maps.Find(m => m.nameId == mapName);
                if (map != null) mapName = map.name;
                messageLog.AddMessage($"Exited {mapName} raid", "raidleave");
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error updating log message from event: {ex.Message}", "exception");
            }
        }

        private void MainBlazorUI_Resize(object sender, EventArgs e)
        {
            try
            {
                if (this.WindowState == FormWindowState.Minimized && Properties.Settings.Default.minimizeToTray)
                {
                    Hide();
                    notifyIconTarkovMonitor.Visible = true;
                }
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error minimizing to tray: {ex.Message} {ex.StackTrace}", "exception");
            }
        }

        private void notifyIconTarkovMonitor_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            try
            {
                Show();
                this.WindowState = FormWindowState.Normal;
                notifyIconTarkovMonitor.Visible = false;
            }
            catch (Exception ex)
            {
                messageLog.AddMessage($"Error restoring from tray: {ex.Message} {ex.StackTrace}", "exception");
            }
        }

        private void menuItemQuit_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
