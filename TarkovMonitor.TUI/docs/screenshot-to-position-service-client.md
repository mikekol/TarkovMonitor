# Screenshot-to-Position: Service & Client Architecture

This document covers how the C# service detects EFT screenshots and delivers the resulting `PlayerPosition` event to connected clients over gRPC.

---

## Components

| Component | File | Role |
|---|---|---|
| `GameWatcher` | `TarkovMonitor.Core/GameWatcher.cs` | Owns the `FileSystemWatcher`; fires `PlayerPosition` event |
| `GameEventBroadcasterService` | `TarkovMonitor.Service/Services/GameEventBroadcasterService.cs` | Serializes and broadcasts to gRPC streams |
| `GameEventClient` | `TarkovMonitor/Services/GameEventClient.cs` | Deserializes and re-fires as typed `PlayerPosition` event |
| proto `GameEvent` | `TarkovMonitor.Service/Protos/game_events.proto` | Wire format: `event_type` string + `data` map<string,string> |

---

## Data Flow

```
EFT saves PNG to disk
        │
        ▼
GameWatcher.screenshotWatcher (FileSystemWatcher)
  Filter: *.png, event: Created
        │
        ▼
GameWatcher.ScreenshotWatcher_Created()
  regex match on filename
  parse x, y, z, quaternion
  QuarternionsToYaw(rx, ry, rz, rw) → rotation (degrees)
  fires PlayerPosition event
        │
        ▼
GameEventBroadcasterService (subscribed in SubscribeToGameWatcherEvents)
  serializes to Dictionary<string,string>:
    x, y, z, rotation, filename, map, raidId
  Broadcast("PlayerPosition", data)
        │
        ▼
gRPC stream (GameEvent proto message)
  event_type = "PlayerPosition"
  data = { x, y, z, rotation, filename, map, raidId }
        │
        ▼
GameEventClient.DispatchGameEvent()
  case "PlayerPosition": BuildPlayerPositionArgs(d)
  fires PlayerPosition event (typed PlayerPositionEventArgs)
        │
        ▼
UI subscriber (MainBlazorUI.cs or TUI app.py via grpc_client.py)
```

---

## File Watching

`GameWatcher` contains two `FileSystemWatcher` instances. The screenshot one is set up in `SetupScreenshotWatcher()` (called from `Start()` if `EnableScreenshotWatching` is true):

```csharp
public bool EnableScreenshotWatching { get; set; } = true;
```

**When the screenshots directory exists** (`Directory.Exists(ScreenshotsPath)`):
- `screenshotWatcher.Path = ScreenshotsPath`
- `Filter = "*.png"`
- `IncludeSubdirectories = false`
- Subscribes to `Created` → `ScreenshotWatcher_Created`

**When the directory does not yet exist** (EFT never run, or wrong path):
- Falls back to watching `MyDocuments` with `IncludeSubdirectories = true`
- Subscribes to `Created` and `Renamed` → `ScreenshotWatcher_FolderCreated`
- `ScreenshotWatcher_FolderCreated` re-calls `SetupScreenshotWatcher()` once the expected path appears

`ScreenshotsPath` is a settable property. Setting it re-invokes `SetupScreenshotWatcher()` immediately (if the watcher is already running), so path changes take effect without a restart.

---

## Screenshot Path Configuration

The service runs as `LocalService` (or `NetworkService`). Neither account has access to the interactive user's Documents folder, so the service cannot resolve the default path itself.

The flow is:
1. UI (or TUI) calls `UpdateConfigAsync(screenshotsPath: ...)` at connect time
2. `GameEventBroadcasterService.UpdateConfig()` receives the RPC call
3. Sets `_config.ScreenshotsPath` (persists to `appsettings.json`) and `_gameWatcher.ScreenshotsPath` (live update)
4. `GameWatcher` re-runs `SetupScreenshotWatcher()` with the new path

`EnableScreenshotWatching` exists for the case where a client wants to handle screenshot detection itself rather than relying on the service. The TUI sets it to `false` (by not pushing a screenshots path and doing its own Python-side watching). The .NET UI sets it to `true` and pushes the path.

---

## Filename Parsing (`ScreenshotWatcher_Created`)

```csharp
// Validate EFT filename format
var match = Regex.Match(filename,
    @"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_?(?<position>.+) \(\d\)\.png");

// Extract coordinates and quaternion from the position substring
var position = Regex.Match(match.Groups["position"].Value,
    @"(?<x>-?[\d]+\.[\d]{2}), (?<y>-?[\d]+\.[\d]{2}), (?<z>-?[\d]+\.[\d]{2})_?" +
    @"(?<rx>-?[\d.]{1}\.[\d]{1,5}), (?<ry>-?[\d.]{1}\.[\d]{1,5}), " +
    @"(?<rz>-?[\d.]{1}\.[\d]{1,5}), (?<rw>-?[\d.]{1}\.[\d]{1,5})");
```

> **Note:** The C# regex requires exactly 2 decimal digits for xyz (`[\d]{2}`). Real EFT screenshots use 1 decimal place (`-106.0`), which causes this regex to not match. The Python TUI fixes this with `[\d]+`. The C# version has the same underlying bug.

**Map resolution:** `ScreenshotWatcher_Created` uses `raidInfo` (the active raid state maintained by log parsing). If `raidInfo.Map` is empty and `CustomMap` is set, it substitutes a synthetic `RaidInfo` with `Map = CustomMap`. If both are empty, the event is dropped.

**Event fired:**
```csharp
PlayerPosition?.Invoke(this, new(
    raidInfo, CurrentProfile,
    new Position(x, y, z),
    rotation,
    filename));
```

The filename is stored on `raidInfo.Screenshots` as well.

---

## Quaternion Conversion

```csharp
public static float QuarternionsToYaw(float x, float z, float y, float w)
{
    float siny_cosp = 2.0f * (w * z + x * y);
    float cosy_cosp = 1.0f - 2.0f * (y * y + z * z);
    float yaw = (float)Math.Atan2(siny_cosp, cosy_cosp);
    yaw *= (180f / (float)Math.PI);
    return yaw;
}
```

The parameter names are `(x, z, y, w)` — note that the EFT quaternion's `ry` is passed as `z` and `rz` is passed as `y`. This is the axis remapping for EFT's coordinate system. The result is yaw in degrees.

---

## gRPC Serialization (`GameEventBroadcasterService`)

`SubscribeToGameWatcherEvents()` wires the `PlayerPosition` handler:

```csharp
_gameWatcher.PlayerPosition += (_, args) =>
{
    Broadcast("PlayerPosition", new()
    {
        ["x"]        = args.Position.X.ToString(CultureInfo.InvariantCulture),
        ["y"]        = args.Position.Y.ToString(CultureInfo.InvariantCulture),
        ["z"]        = args.Position.Z.ToString(CultureInfo.InvariantCulture),
        ["rotation"] = args.Rotation.ToString(CultureInfo.InvariantCulture),
        ["filename"] = args.Filename,
        ["map"]      = args.RaidInfo.Map,
        ["raidId"]   = args.RaidInfo.RaidId
    });
};
```

`Broadcast()` wraps the dictionary into a `GameEvent` proto message and calls `stream.WriteAsync()` on each active client stream. Dead streams are removed under a lock.

---

## gRPC Deserialization (`GameEventClient`)

```csharp
case "PlayerPosition":
    PlayerPosition?.Invoke(this, BuildPlayerPositionArgs(d));
    break;
```

`BuildPlayerPositionArgs` reconstructs the typed event args:

```csharp
private static PlayerPositionEventArgs BuildPlayerPositionArgs(IReadOnlyDictionary<string, string> d)
{
    var profile = BuildProfile(d);
    var raidInfo = new RaidInfo { Map = d["map"], RaidId = d["raidId"], Profile = profile };
    float.TryParse(d["x"], NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
    float.TryParse(d["y"], NumberStyles.Float, CultureInfo.InvariantCulture, out var y);
    float.TryParse(d["z"], NumberStyles.Float, CultureInfo.InvariantCulture, out var z);
    float.TryParse(d["rotation"], NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation);
    return new PlayerPositionEventArgs(raidInfo, profile, new Position(x, y, z), rotation, d["filename"]);
}
```

`CultureInfo.InvariantCulture` is used throughout to ensure decimal points are parsed correctly regardless of the user's locale.

---

## Wire Format

`GameEvent` proto message:

```
event_type = "PlayerPosition"
timestamp_ms = <unix ms>
data = {
    "x":        "-106.0",
    "y":        "28.6",
    "z":        "190.6",
    "rotation": "-172.34567",
    "filename": "2020-07-02[01-49]_-106.0, 28.6, 190.6_0.0, -0.9, 0.1, -0.4 (0).png",
    "map":      "Woods",
    "raidId":   "EAR6"
}
```

All values are strings. Numeric values use invariant culture (`.` decimal separator).

---

## Config Push at Connect Time

The UI calls `UpdateConfigAsync` immediately after `ConnectAsync` succeeds:

```csharp
await client.UpdateConfigAsync(
    customLogsPath:  settings.CustomLogsPath,
    customMap:       settings.CustomMap,
    screenshotsPath: resolvedScreenshotsPath);
```

Without this push, the service's `GameWatcher` would watch either the default `MyDocuments\Escape From Tarkov\Screenshots` (wrong for OneDrive-redirected profiles) or whatever path was last persisted in `appsettings.json`.

The Python TUI does its own file watching and does **not** push a `screenshots_path` to the service — it leaves `EnableScreenshotWatching` at its default (`true` in `GameWatcher`) but never sends a valid path, so the service's watcher silently falls back to the `MyDocuments` default or does nothing useful. The TUI's own `ScreenshotWatcher` handles detection instead.
