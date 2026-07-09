using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace TarkovMonitor
{
    internal static class SocketClient
    {
        public static event EventHandler<ExceptionEventArgs>? ExceptionThrown;
        private static readonly string wsUrl = "wss://socket.tarkov.dev";
        //private static readonly string wsUrl = "ws://localhost:8080";
        private static List<BrowserRemote> _remotes = new();
        private static ClientWebSocket socket;
        private static CancellationTokenSource cancellationToken;
        private static Task receiveTask;
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
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }
                socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Idle", CancellationToken.None).ContinueWith(t => {
                    socket.Dispose();
                    socket = null;
                });
            };
        }

        private static Task SendSocketMessage(JsonNode payload)
        {
            byte[] byteBuffer = Encoding.UTF8.GetBytes(payload.ToJsonString());
            return socket.SendAsync(new ArraySegment<byte>(byteBuffer), WebSocketMessageType.Text, true, cancellationToken.Token);
        }

        public static async Task StartClient()
        {
            if (cancellationToken != null && !cancellationToken.IsCancellationRequested)
            {
                cancellationToken.Cancel();
            }
            if (receiveTask != null)
            {
                try
                {
                    await receiveTask;
                }
                catch { }
            }
            cancellationToken = new();
            ParseRemoteIds();
            var remoteid = Properties.Settings.Default.remoteId;
            socket = new();
            socket.Options.SetRequestHeader("User-Agent", $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}/{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            await socket.ConnectAsync(new Uri(wsUrl + $"?sessionid={remoteid}-tm"), cancellationToken.Token);
            idleTimer.Stop();
            idleTimer.Start();

            receiveTask = Task.Run(async () =>
            {
                byte[] buffer = new byte[1024];
                while (socket != null && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        }
                        break;
                    }

                    JsonNode message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (message == null)
                    {
                        return;
                    }
                    if (message["type"]?.ToString() == "ping")
                    {
                        SendSocketMessage(new JsonObject
                        {
                            ["type"] = "pong"
                        });
                    }
                }
            }, cancellationToken.Token);
        }

        public static async Task VerifyClient()
        {
            if (socket != null)
            {
                if (socket.State == WebSocketState.Open)
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
                    // Clone the message to avoid modifying the same object reference
                    // across multiple remotes, which could cause race conditions
                    var messageForRemote = new JsonObject(message);
                    messageForRemote["sessionID"] = remote.Id;
                    await SendSocketMessage(messageForRemote);
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

        public static async Task SendPlayerPositionAndZoom(PlayerPositionEventArgs e)
        {
            var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map)?.normalizedName;
            if (map == null && e.RaidInfo.Map != null)
            {
                return;
            }

            // Get zoom level from settings (default 200)
            int zoomLevel = Properties.Settings.Default.zoomLevelOnLocationUpdate;
            if (zoomLevel < 100) zoomLevel = 100;
            if (zoomLevel > 400) zoomLevel = 400;

            var messages = new List<JsonObject>();

            // Add position message
            messages.Add(GetPlayerPositionMessage(e));

            // Add zoom message
            // TODO: Verify zoom command format with tarkov.dev WebSocket API
            // If "zoom" is not a valid command type, adjust the data.type value
            messages.Add(new JsonObject
            {
                ["type"] = "command",
                ["data"] = new JsonObject
                {
                    ["type"] = "zoom",
                    ["value"] = zoomLevel
                }
            });

            try
            {
                await Send(messages);
            }
            catch (Exception ex)
            {
                ExceptionThrown?.Invoke(messages, new(ex, "sending player position and zoom"));
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
