using System.Text.Json.Nodes;
using Websocket.Client;

namespace TarkovMonitor
{
    internal static class SocketClient
    {
        public static event EventHandler<ExceptionEventArgs>? ExceptionThrown;
        private static readonly string wsUrl = "wss://socket.tarkov.dev";
        //private static readonly string wsUrl = "ws://localhost:8080";
        private static List<BrowserRemote> _remotes = new();
        private static WebsocketClient socket;
        private static System.Timers.Timer idleTimer = new()
        {
            AutoReset = false,
            Interval = TimeSpan.FromMinutes(30).TotalMilliseconds,
        };

        static SocketClient()
        {
            idleTimer.Elapsed += (sender, e) => {
                if (socket == null)
                {
                    return;
                }
                if (!socket.IsRunning)
                {
                    return;
                }
                socket.Stop(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "Idle").ContinueWith(t => {
                    socket.Dispose();
                    socket = null;
                });
            };
        }

        public static async Task StartClient()
        {
            ParseRemoteIds();
            var remoteid = Properties.Settings.Default.remoteId;
            socket = new(new Uri(wsUrl + $"?sessionid={remoteid}-tm"));
            socket.MessageReceived.Subscribe(msg => {
                if (msg.Text == null)
                {
                    return;
                }
                var message = JsonNode.Parse(msg.Text);
                if (message == null)
                {
                    return;
                }
                if (message["type"]?.ToString() == "ping")
                {
                    socket.Send(new JsonObject
                    {
                        ["type"] = "pong"
                    }.ToJsonString());
                }
            });
            await socket.Start();
            idleTimer.Stop();
            idleTimer.Start();
        }

        public static async Task VerifyClient()
        {
            if (socket != null)
            {
                if (socket.IsRunning)
                {
                    return;
                }
                socket.Dispose();
                socket = null;
            }
            await StartClient();
            return;
        }

        private static void ParseRemoteIds()
        {
            _remotes.Clear();
            var remoteIdString = Properties.Settings.Default.remoteId ?? "";

            if (string.IsNullOrWhiteSpace(remoteIdString))
            {
                return;
            }

            // Split by comma or semicolon
            var ids = remoteIdString.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var id in ids)
            {
                var trimmed = id.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    _remotes.Add(new BrowserRemote { Id = trimmed });
                }
            }
        }

        public static List<BrowserRemote> GetAllRemotes()
        {
            return _remotes;
        }

        public static async Task Send(List<JsonObject> messages)
        {
            var remoteIdString = Properties.Settings.Default.remoteId;
            if (remoteIdString == null || remoteIdString == "")
            {
                return;
            }

            await VerifyClient();

            var remotes = GetAllRemotes();
            if (remotes.Count == 0)
            {
                return;
            }

            foreach (var message in messages)
            {
                foreach (var remote in remotes)
                {
                    message["sessionID"] = remote.Id;
                    await socket.SendInstant(message.ToJsonString());
                }
            }

            idleTimer.Stop();
            idleTimer.Start();
        }
        public static Task Send(JsonObject message)
        {
            return Send(new List<JsonObject> { message });
        }

        public static async Task UpdatePlayerPosition(PlayerPositionEventArgs e)
        {
            var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map)?.normalizedName;
            if (map == null && e.RaidInfo.Map != null)
            {
                return;
            }
            var payload = GetPlayerPositionMessage(e);
            try
            {
                await Send(payload);
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(payload, new(ex, "updating player position"));
            }
        }

        public static async Task NavigateToMap(TarkovDev.Map map)
        {
            var payload = GetNavigateToMapMessage(map);
            try
            {
                await Send(payload);
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(payload, new(ex, $"navigating to map {map.name}"));
            }
        }

        public static JsonObject GetPlayerPositionMessage(PlayerPositionEventArgs e)
        {
            var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map)?.normalizedName;
            if (map == null && e.RaidInfo.Map != null)
            {
                throw new Exception($"Map {e.RaidInfo.Map} not found");
            }
            return new JsonObject
            {
                ["type"] = "command",
                ["data"] = new JsonObject
                {
                    ["type"] = "playerPosition",
                    ["map"] = map,
                    ["position"] = new JsonObject
                    {
                        ["x"] = e.Position.X,
                        ["y"] = e.Position.Y,
                        ["z"] = e.Position.Z,
                    },
                    ["rotation"] = e.Rotation,
                }
            };
        }

        public static JsonObject GetNavigateToMapMessage(TarkovDev.Map map)
        {
            return new JsonObject
            {
                ["type"] = "command",
                ["data"] = new JsonObject
                {
                    ["type"] = "map",
                    ["value"] = map.normalizedName
                }
            };
        }
    }
}
