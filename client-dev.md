# TarkovMonitor Service — Client Developer Guide

The TarkovMonitor Service is a Windows gRPC service that monitors Escape from Tarkov log files and broadcasts game events to connected clients. This document covers everything a client developer needs to connect, subscribe to events, and manage service configuration.

---

## Connection

The service listens on **`http://localhost:50051`** (HTTP/2, plain text — not HTTPS).

The port is configurable via `GrpcPort` in the service's `appsettings.json`. The default is `50051`.

**Important:** gRPC requires HTTP/2. Do not use `https://` — Kestrel is configured for plain HTTP/2 only.

---

## Proto Definition

```protobuf
syntax = "proto3";
package tarkovmonitor.v1;

service TarkovMonitorService {
  rpc SubscribeToGameEvents(SubscriptionRequest) returns (stream GameEvent);
  rpc GetConfig(GetConfigRequest)               returns (ServiceConfig);
  rpc UpdateConfig(UpdateConfigRequest)         returns (UpdateConfigResponse);
  rpc GetStatus(GetStatusRequest)               returns (ServiceStatus);
}

message SubscriptionRequest {
  string client_agent = 1;  // e.g. "MyApp/1.0.0"
}

message GameEvent {
  string            event_type   = 1;
  int64             timestamp_ms = 2;  // Unix ms (UTC)
  map<string,string> data        = 3;
}

message GetConfigRequest {}

message ServiceConfig {
  string custom_logs_path                    = 1;
  string tarkov_tracker_token               = 2;  // deprecated — use tarkov_tracker_tokens
  bool   tarkov_tracker_enabled             = 3;
  string tarkov_tracker_domain              = 4;  // deprecated — use tarkov_tracker_domains
  map<string, string> tarkov_tracker_tokens  = 5;  // keyed by EFT profile ID
  string custom_map                          = 6;  // fallback map for screenshot detection
  map<string, string> tarkov_tracker_domains = 7;  // keyed by EFT profile ID
}

message UpdateConfigRequest {
  string custom_logs_path                    = 1;
  string tarkov_tracker_token               = 2;  // deprecated — use tarkov_tracker_tokens
  string tarkov_tracker_domain              = 3;  // deprecated — use tarkov_tracker_domains
  map<string, string> tarkov_tracker_tokens  = 4;  // partial update: only keys present are changed
  string custom_map                          = 5;
  map<string, string> tarkov_tracker_domains = 6;  // partial update: only keys present are changed
}

message UpdateConfigResponse {
  bool   success       = 1;
  string error_message = 2;
}

message GetStatusRequest {}

message ServiceStatus {
  bool  is_game_watcher_running = 1;
  bool  is_log_monitor_active   = 2;
  int64 uptime_seconds          = 3;
}
```

---

## RPCs

### `SubscribeToGameEvents`

Server-side streaming. Call once; the stream stays open and delivers `GameEvent` messages as game activity occurs.

**Late-join behaviour:** If the service has already completed its initial log read before you connect, it immediately pushes an `InitialReadComplete` event with the current player profile so you don't miss it.

**Reconnection:** The stream ends if the service restarts. Clients should detect the stream ending and reconnect with a back-off loop.

---

### `GetConfig`

Returns the current service configuration. Always reflects the live on-disk `appsettings.json`.

Key fields in the response:
- `tarkov_tracker_tokens` — map of EFT profile ID → API token
- `tarkov_tracker_domains` — map of EFT profile ID → TarkovTracker host (e.g. `tarkovtracker.io`)
- `tarkov_tracker_enabled` — true if at least one profile has a token configured
- `custom_map` — fallback map name used for screenshot position detection when no raid is in progress

---

### `UpdateConfig`

Persists config changes to disk and applies them immediately.

- Token and domain maps use **partial-update semantics**: only the keys you send are changed; omitted keys are left as-is. Send `{ "profileId123": "myToken" }` to update a single profile without touching others.
- Passing an empty string for `custom_logs_path` clears the override and reverts to auto-detection via the EFT registry key.

---

### `GetStatus`

Lightweight health-check. Use this before subscribing to verify the service is reachable.

---

## Event Reference

All events arrive as a `GameEvent` with an `event_type` string and a `data` map. The sections below list every key the service places in the map for each event type.

---

### Profile / Session

#### `InitialReadComplete`

Fired once when the service finishes reading past log files on startup, and again whenever Tarkov restarts. Also pushed immediately to any client that connects after this point (late-join).

| Key                | Type   | Notes                         |
|--------------------|--------|-------------------------------|
| `profileId`        | string | Hex profile ID                |
| `profileType`      | string | `Regular` or `PVE`            |
| `profileAccountId` | string | Numeric account ID as string  |

#### `ProfileChanged`

Fired when the player switches profiles without restarting (e.g. switching between PMC/Scav while the game is running). Same fields as `InitialReadComplete`.

| Key                | Type   |
|--------------------|--------|
| `profileId`        | string |
| `profileType`      | string |
| `profileAccountId` | string |

---

### Raid Lifecycle

The following events all carry the **RaidInfo** field set. Optional fields are absent if not yet known at the time the event fires.

**RaidInfo fields:**

| Key              | Type   | Notes                                               |
|------------------|--------|-----------------------------------------------------|
| `profileId`      | string |                                                     |
| `profileType`    | string | `Regular` or `PVE`                                  |
| `profileAccountId`| string |                                                    |
| `map`            | string | e.g. `bigmap`, `Shoreline`, `Woods`                 |
| `raidId`         | string | 6-char uppercase short ID, e.g. `AB12CD`            |
| `raidType`       | string | `Unknown`, `PMC`, `Scav`, or `PVE`                  |
| `reconnected`    | string | `True` or `False`                                   |
| `queueTime`      | string | Seconds as invariant float, e.g. `42.5`             |
| `screenshotsJson`| string | JSON array of screenshot filenames, e.g. `["a.png"]`|
| `startedTimeMs`  | string | Unix ms (UTC); absent until `GameStarted` log line  |

#### `MapLoading`

Map bundle is loading. `raidType` is typically `Unknown` at this point.

#### `MatchFound`

Matching complete and a server is locked in. Only fires on fresh raids (not reconnects). `queueTime` is populated.

#### `RaidStarting`

PMC countdown has started. `startedTimeMs` not yet set for scavs at this point.

#### `RaidStarted`

Raid has begun (countdown ended for PMC; immediate for scav). `startedTimeMs` is set.

#### `RaidExited`

Player has left the raid (extracted, died, etc.).

| Key     | Type   |
|---------|--------|
| `map`   | string |
| `raidId`| string |

#### `RaidEnded`

Post-raid screen has been shown. Full RaidInfo fields available.

#### `ExitedPostRaidMenus`

Player has navigated back to the main menu after post-raid screens. Data map is empty.

---

### Tasks

#### `TaskStarted` / `TaskFailed` / `TaskFinished`

| Key      | Type   | Notes                          |
|----------|--------|--------------------------------|
| `taskId` | string | Quest/task ID from the game DB |

---

### Flea Market

#### `FleaSold`

| Key                | Type   | Notes                                                   |
|--------------------|--------|---------------------------------------------------------|
| `buyer`            | string | Buyer's in-game name                                    |
| `soldItemId`       | string | Item template ID                                        |
| `soldItemCount`    | string | Integer as string                                       |
| `receivedItemsJson`| string | JSON object: `{"itemTemplateId": count, ...}`           |
| `profileId`        | string |                                                         |

#### `FleaOfferExpired`

| Key         | Type   |
|-------------|--------|
| `itemId`    | string |
| `itemCount` | string |

---

### Group

#### `GroupInviteAccept`

Fired when someone you invited accepts.

| Key        | Type   |
|------------|--------|
| `nickname` | string |
| `side`     | string | `Bear` or `Usec` |
| `level`    | string | Integer as string |

#### `GroupUserLeave`

| Key        | Type   |
|------------|--------|
| `nickname` | string |

---

### Player Position

#### `PlayerPosition`

Fired when a screenshot is taken in-raid. Position is extracted from the screenshot filename.

| Key        | Type   | Notes                              |
|------------|--------|------------------------------------|
| `x`        | string | Invariant float                    |
| `y`        | string | Invariant float                    |
| `z`        | string | Invariant float                    |
| `rotation` | string | Yaw in degrees, invariant float    |
| `filename` | string | Screenshot filename (not full path)|
| `map`      | string |                                    |
| `raidId`   | string |                                    |

---

### Remote Map Control

When TarkovMonitor detects a screenshot and extracts player position, it sends a `playerPosition` message to tarkov-dev via WebSocket. The following describes the assumed order of operations and zoom behavior:

#### Map Load and Location Update Flow

1. **Raid starts** — TarkovMonitor tracks raid state
2. **Map is loaded** — tarkov-dev loads the map via its own raid-start logic
3. **Screenshot detected** — TarkovMonitor extracts position and map from the screenshot
4. **Position message sent** — TarkovMonitor broadcasts `playerPosition` via WebSocket:
   ```json
   {
     "type": "command",
     "data": {
       "type": "playerPosition",
       "map": "interchange",
       "position": {"x": 100, "y": 200, "z": 50},
       "rotation": 45,
       "zoomLevel": 0  // Only included if map changed
     }
   }
   ```
5. **Player location displayed** — tarkov-dev updates the marker on the map
6. **Map centered on player** — The map pans to the player's location (always happens on each update)
7. **Zoom applied conditionally** — If `zoomLevel` is included in the message (map has changed), tarkov-dev applies the zoom. Otherwise, the user's manual zoom adjustments are preserved.

#### Zoom Behavior

**When zoom level is applied:**
- When the map changes between raids (new raid or different map from previous raid)
- The zoom defaults to the configured `zoomLevelOnLocationUpdate` setting (0-20 Leaflet zoom level; default 0 = see whole map)

**When zoom is NOT touched:**
- During subsequent position updates on the same map within the same raid
- Any manual zoom adjustments the user makes on the map are preserved until the map changes

This design allows users to manually adjust zoom for better player icon visibility while ensuring the zoom resets to a sensible default when the context changes (new raid or different map).

---

### Control Settings

#### `ControlSettings`

Fired when the game logs its input binding configuration.

| Key                  | Type   | Notes                  |
|----------------------|--------|------------------------|
| `controlSettingsJson`| string | Raw JSON object string |

---

### Diagnostics

#### `DebugMessage`

| Key       | Type   |
|-----------|--------|
| `message` | string |

#### `ExceptionThrown`

An exception was caught inside the service's GameWatcher.

| Key          | Type   |
|--------------|--------|
| `context`    | string | Short description of what was happening |
| `message`    | string | Exception message |
| `stackTrace` | string | May be empty |

#### `NewLogData`

Raw log data forwarded as-is from the log files. Useful for building your own log parser on top of the stream.

| Key          | Type   | Notes                                     |
|--------------|--------|-------------------------------------------|
| `data`       | string | Raw log text chunk                        |
| `logType`    | string | `Application`, `Notifications`, or `Traces`|
| `initialRead`| string | `True` if from past-log replay at startup |

---

## C# Client Example

Add the NuGet packages:
```
Grpc.Net.Client
Google.Protobuf
Grpc.Tools
```

Reference the proto file in your `.csproj`:
```xml
<ItemGroup>
  <Protobuf Include="game_events.proto" GrpcServices="Client" />
</ItemGroup>
```

### Connect and subscribe

```csharp
using Grpc.Net.Client;
using TarkovMonitor.Service.Contracts;  // generated namespace

var channel = GrpcChannel.ForAddress("http://localhost:50051");
var client  = new TarkovMonitorService.TarkovMonitorServiceClient(channel);

// Health-check before subscribing
await client.GetStatusAsync(new GetStatusRequest());

using var cts    = new CancellationTokenSource();
var       stream = client.SubscribeToGameEvents(
    new SubscriptionRequest { ClientAgent = "MyApp/1.0.0" },
    cancellationToken: cts.Token);

await foreach (var gameEvent in stream.ResponseStream.ReadAllAsync(cts.Token))
{
    var d = gameEvent.Data;
    switch (gameEvent.EventType)
    {
        case "InitialReadComplete":
        case "ProfileChanged":
            Console.WriteLine($"Profile: {d["profileId"]} ({d["profileType"]})");
            break;

        case "RaidStarted":
            Console.WriteLine($"Raid started on {d["map"]} as {d["raidType"]}");
            break;

        case "RaidExited":
            Console.WriteLine($"Left raid {d["raidId"]} on {d["map"]}");
            break;

        case "TaskFinished":
            Console.WriteLine($"Task completed: {d["taskId"]}");
            break;

        case "FleaSold":
            Console.WriteLine($"{d["buyer"]} bought {d["soldItemCount"]}x {d["soldItemId"]}");
            break;

        case "PlayerPosition":
            float.TryParse(d["x"], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var x);
            float.TryParse(d["y"], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var y);
            float.TryParse(d["z"], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var z);
            float.TryParse(d["rotation"], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var yaw);
            Console.WriteLine($"Position: ({x}, {y}, {z}), Yaw: {yaw}°");
            break;
    }
}
```

### Reconnect loop

```csharp
async Task ConnectWithRetryAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try
        {
            var channel = GrpcChannel.ForAddress("http://localhost:50051");
            var client  = new TarkovMonitorService.TarkovMonitorServiceClient(channel);
            var stream  = client.SubscribeToGameEvents(
                new SubscriptionRequest { ClientAgent = "MyApp/1.0.0" },
                cancellationToken: ct);

            await foreach (var evt in stream.ResponseStream.ReadAllAsync(ct))
                HandleEvent(evt);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"Disconnected: {ex.Message}. Retrying in 5s...");
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
```

### Read and update config

```csharp
// Read
var config = await client.GetConfigAsync(new GetConfigRequest());
Console.WriteLine($"Logs path override: '{config.CustomLogsPath}'");
foreach (var (profileId, token) in config.TarkovTrackerTokens)
    Console.WriteLine($"  Profile {profileId}: token set = {!string.IsNullOrEmpty(token)}");

// Update a single profile's token and domain (partial update — other profiles unchanged)
var request = new UpdateConfigRequest
{
    CustomLogsPath = @"C:\custom\path\to\Logs",
};
request.TarkovTrackerTokens["profileId123"]  = "my-22-char-api-token";
request.TarkovTrackerDomains["profileId123"] = "tarkovtracker.io";

var response = await client.UpdateConfigAsync(request);
if (!response.Success)
    Console.WriteLine($"Config update failed: {response.ErrorMessage}");
```

---

## TypeScript Client Example

Install dependencies:
```bash
npm install @grpc/grpc-js @grpc/proto-loader
npm install --save-dev @types/node
```

### Load the proto and connect

```typescript
import * as grpc from '@grpc/grpc-js';
import * as protoLoader from '@grpc/proto-loader';
import path from 'path';

const PROTO_PATH = path.resolve(__dirname, 'game_events.proto');

const packageDef = protoLoader.loadSync(PROTO_PATH, {
    keepCase: false,
    longs: String,
    enums: String,
    defaults: true,
    oneofs: true,
});

const protoDescriptor = grpc.loadPackageDefinition(packageDef) as any;
const TarkovMonitorService = protoDescriptor.tarkovmonitor.v1.TarkovMonitorService;

const client = new TarkovMonitorService(
    'localhost:50051',
    grpc.credentials.createInsecure()
);
```

### Subscribe to events

```typescript
function subscribeToEvents(): void {
    const stream = client.subscribeToGameEvents({ clientAgent: 'MyApp/1.0.0' });

    stream.on('data', (gameEvent: { eventType: string; timestampMs: string; data: Record<string, string> }) => {
        const { eventType, data } = gameEvent;

        switch (eventType) {
            case 'InitialReadComplete':
            case 'ProfileChanged':
                console.log(`Profile: ${data.profileId} (${data.profileType})`);
                break;

            case 'RaidStarted':
                console.log(`Raid started on ${data.map} as ${data.raidType}`);
                break;

            case 'RaidExited':
                console.log(`Left raid ${data.raidId} on ${data.map}`);
                break;

            case 'TaskFinished':
                console.log(`Task completed: ${data.taskId}`);
                break;

            case 'FleaSold': {
                const received = JSON.parse(data.receivedItemsJson ?? '{}') as Record<string, number>;
                console.log(`${data.buyer} bought ${data.soldItemCount}x ${data.soldItemId}`);
                console.log('  Received:', received);
                break;
            }

            case 'PlayerPosition':
                console.log(
                    `Position: (${data.x}, ${data.y}, ${data.z}), Yaw: ${data.rotation}°`,
                    `on ${data.map}`
                );
                break;
        }
    });

    stream.on('error', (err: Error) => {
        console.error('Stream error:', err.message);
    });

    stream.on('end', () => {
        console.log('Stream ended — service may have stopped');
    });
}
```

### Reconnect loop

```typescript
async function connectWithRetry(signal: AbortSignal): Promise<void> {
    while (!signal.aborted) {
        await new Promise<void>((resolve) => {
            const stream = client.subscribeToGameEvents({ clientAgent: 'MyApp/1.0.0' });

            stream.on('data', handleEvent);
            stream.on('error', (err: Error) => {
                console.error(`Disconnected: ${err.message}`);
                resolve();
            });
            stream.on('end', resolve);

            signal.addEventListener('abort', () => stream.cancel(), { once: true });
        });

        if (!signal.aborted) {
            console.log('Reconnecting in 5s...');
            await new Promise(r => setTimeout(r, 5000));
        }
    }
}

// Usage
const controller = new AbortController();
connectWithRetry(controller.signal);
// To stop: controller.abort();
```

### Read and update config

```typescript
function getConfig(): Promise<{
    customLogsPath: string;
    tarkovTrackerEnabled: boolean;
    tarkovTrackerTokens: Record<string, string>;
    tarkovTrackerDomains: Record<string, string>;
    customMap: string;
}> {
    return new Promise((resolve, reject) => {
        client.getConfig({}, (err: grpc.ServiceError | null, response: any) => {
            if (err) reject(err);
            else resolve(response);
        });
    });
}

// Partial update — only the keys you provide are changed
function updateConfig(params: {
    customLogsPath?: string;
    tarkovTrackerTokens?: Record<string, string>;
    tarkovTrackerDomains?: Record<string, string>;
    customMap?: string;
}): Promise<{ success: boolean; errorMessage: string }> {
    return new Promise((resolve, reject) => {
        client.updateConfig(params, (err: grpc.ServiceError | null, response: any) => {
            if (err) reject(err);
            else resolve(response);
        });
    });
}

// Example
const config = await getConfig();
console.log('Current logs path:', config.customLogsPath);
console.log('Configured profiles:', Object.keys(config.tarkovTrackerTokens));

const result = await updateConfig({
    tarkovTrackerTokens:  { profileId123: 'my-22-char-api-token' },
    tarkovTrackerDomains: { profileId123: 'tarkovtracker.io' },
});
if (!result.success) console.error('Update failed:', result.errorMessage);
```

### Get service status

```typescript
function getStatus(): Promise<{ isGameWatcherRunning: boolean; isLogMonitorActive: boolean; uptimeSeconds: string }> {
    return new Promise((resolve, reject) => {
        client.getStatus({}, (err: grpc.ServiceError | null, response: any) => {
            if (err) reject(err);
            else resolve(response);
        });
    });
}

const status = await getStatus();
console.log(`Service uptime: ${status.uptimeSeconds}s`);
```

---

## Parsing Notes

### Floats

All float values (`x`, `y`, `z`, `rotation`, `queueTime`) are serialized with the **invariant culture** (`.` as decimal separator). Always parse with `CultureInfo.InvariantCulture` in C# or `parseFloat()` in TypeScript.

### Timestamps

`timestamp_ms` on `GameEvent` and `startedTimeMs` in raid data are **Unix milliseconds in UTC**.

```csharp
// C#
var time = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(data["startedTimeMs"])).UtcDateTime;
```

```typescript
// TypeScript
const time = new Date(parseInt(data.startedTimeMs, 10));
```

### JSON-encoded fields

Some fields contain embedded JSON:

| Field              | Event          | Type                       |
|--------------------|----------------|----------------------------|
| `receivedItemsJson`| `FleaSold`     | `{ [itemId: string]: number }` |
| `screenshotsJson`  | Raid events    | `string[]`                 |
| `controlSettingsJson`| `ControlSettings` | object                |

### `profileType` values

| Value     | Meaning          |
|-----------|------------------|
| `Regular` | Standard PvP PMC |
| `PVE`     | PvE mode         |

### `raidType` values

| Value     | Meaning                          |
|-----------|----------------------------------|
| `Unknown` | Not yet determined (pre-match)   |
| `PMC`     | Standard PMC raid                |
| `Scav`    | Scav run                         |
| `PVE`     | PvE co-op mode                   |

---

## Event Sequence: Normal PMC Raid

A typical session produces events in roughly this order:

```
InitialReadComplete     ← on first connect (profile established)
MapLoading              ← player clicked "Ready"
MatchFound              ← server locked in, queueTime available
RaidStarting            ← PMC countdown begins
RaidStarted             ← countdown ended, in raid
  PlayerPosition        ← repeated as screenshots are taken
RaidExited              ← player extracted or died
RaidEnded               ← post-raid screen shown
ExitedPostRaidMenus     ← back at main menu
```

Scav runs omit `RaidStarting` (no countdown). Reconnects set `reconnected=True` and skip `MatchFound`.

---

## Running the Service

```bash
# Start the real service (watches actual EFT logs)
cd TarkovMonitor.Service && dotnet run

# Start the test server (interactive CLI for firing synthetic events)
cd TarkovMonitor.Service.TestServer && dotnet run
```

The test server accepts commands like `raid_start`, `raid_end`, `task`, `custom`, and `status` — useful for exercising your client without needing EFT running.
