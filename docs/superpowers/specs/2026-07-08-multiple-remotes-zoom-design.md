# Design: Multiple Browser Remotes & Auto-Zoom on Location

**Date:** 2026-07-08  
**Status:** Approved  
**Scope:** Features to control multiple tarkov.dev browser instances and auto-zoom when location is detected

---

## Overview

Currently, TarkovMonitor controls a single tarkov.dev browser remote via WebSocket using one `remoteId`. This design adds:

1. **Multiple Remote Instances** — ability to send commands to 2+ browser instances simultaneously (all receive the same position/map updates)
2. **Auto-Zoom on Location** — when a screenshot is parsed and player location is extracted, automatically zoom the remote(s) to a configurable level

**Use Case:** Players with multi-monitor setups can have the map on multiple displays, or have backup instances, all staying in sync.

---

## Architecture

### New Type: `BrowserRemote`

A lightweight class to represent a single remote instance:

```csharp
public class BrowserRemote
{
    public string Id { get; set; }
    // Future extensibility: PerRemoteZoomLevel, Label, etc.
}
```

Location: `TarkovMonitor/BrowserRemote.cs` (new file)

**Rationale:** Encapsulates remote identity; enables per-remote configuration in future without changing message-sending logic.

---

### Modified: `SocketClient`

**Parsing & Storage:**
- On startup, parse `Properties.Settings.Default.remoteId` as comma/semicolon-separated string
- Store parsed list as `List<BrowserRemote>` in `SocketClient` (static member or instance field)
- Example: `remoteId = "abc123,def456,ghi789"` becomes three `BrowserRemote` objects

**Broadcasting:**
- Modify `Send(JsonObject message)` and `Send(List<JsonObject> messages)` to iterate all remotes:
  ```csharp
  foreach (var remote in GetAllRemotes())
  {
      message["sessionID"] = remote.Id;
      await socket.SendInstant(message.ToJsonString());
  }
  ```
- Ensure socket connection is established before sending (existing `VerifyClient()` logic applies)

**New Public Method: `SendPlayerPositionAndZoom(PlayerPositionEventArgs args)`**
- Sends player position command to all remotes
- Sends zoom command to all remotes
- Called from `MainBlazorUI.Eft_PlayerPosition()` instead of direct `SocketClient.Send()`

**Zoom Command Format:**
```json
{
  "type": "command",
  "data": {
    "type": "zoom",
    "value": 200
  },
  "sessionID": "<remoteId>"
}
```

(Confirm tarkov.dev socket API accepts this; if not, adjust format based on socket protocol.)

---

### Settings

**New Setting: `zoomLevelOnLocationUpdate`**
- Type: `int`
- Range: 100–400 (representing percentage)
- Default: 200
- Storage: `Properties.Settings.Default.zoomLevelOnLocationUpdate`
- Location: Auto-generated in `Properties/Settings.Designer.cs` from project settings

**Modified Setting: `remoteId`**
- Existing setting, but now parsed as comma/semicolon-delimited list
- Example: `"abc123,def456"`
- Backward compatible: single ID (no delimiter) still works

---

### UI: Settings Page

**Location:** `TarkovMonitor/Settings.razor` (or equivalent Blazor settings component)

**New Control: Zoom Level Slider**
- Label: "Zoom level when location is detected via screenshot (%)"
- Input: Numeric range input (min: 100, max: 400, step: 25)
- Binding: `@bind-Value="Properties.Settings.Default.zoomLevelOnLocationUpdate"`
- Help text: "Adjust how much the map zooms in when a screenshot reveals your player position."
- Default display: 200%

**Existing Control: Remote ID**
- Label: May need clarification: "Remote Browser IDs (comma-separated for multiple instances)"
- Help text: "Enter one or more remote IDs from tarkov.dev, separated by commas. Example: `abc123,def456`"
- Allows manual entry of multiple IDs

---

## Data Flow

### On Startup
```
Settings loaded
  → remoteId = "abc123,def456"
  → Parsed to List<BrowserRemote> in SocketClient
  → Stored as static or instance field
```

### On Screenshot Detection (Location Extracted)
```
ScreenshotWatcher_Created fires
  → Parse screenshot filename for position
  → Create PlayerPositionEventArgs
  → Call Eft_PlayerPosition()
  → Instead of SocketClient.Send(position), call:
    SocketClient.SendPlayerPositionAndZoom(args, zoomLevel)
  → SendPlayerPositionAndZoom():
    1. Send position command to all remotes
    2. Send zoom command (value = zoomLevelOnLocationUpdate) to all remotes
```

---

## Error Handling

**Multiple Remote Send Failures:**
- If one remote fails to receive, log the error but continue sending to others
- Existing `Broadcast` pattern in GameEventBroadcasterService (mark dead streams) is server-side; here, we just log and continue
- User sees message: "Position updated on X of Y remotes" (if desired feedback)

**Invalid Remote ID Format:**
- If parsing fails (malformed delimiter), fall back to treating entire string as a single ID and log a warning
- User can correct in Settings UI

**Missing Zoom Level:**
- If `zoomLevelOnLocationUpdate` is not set, use default 200%

---

## Testing Strategy

1. **Unit:** Parse comma-separated IDs into `List<BrowserRemote>`
2. **Integration:** Mock socket, verify each message gets sent with correct `sessionID` for 2–3 remotes
3. **Manual:**
   - Configure 2+ remote IDs in Settings
   - Take screenshot in-game with position data
   - Verify all remotes receive position + zoom command
   - Verify zoom slider in Settings UI persists value

---

## Files Modified / Created

**New:**
- `TarkovMonitor/BrowserRemote.cs`

**Modified:**
- `TarkovMonitor/SocketClient.cs` (parsing, multi-remote send, zoom command)
- `TarkovMonitor/MainBlazorUI.cs` (call `SendPlayerPositionAndZoom` instead of `Send`)
- `TarkovMonitor/Settings.razor` (add zoom slider UI)
- `TarkovMonitor/Properties/Settings.Designer.cs` (add `zoomLevelOnLocationUpdate` setting)

---

## Backward Compatibility

- Single remote ID (no delimiter) is treated as a list of one remote — no breaking change
- Existing code calling `SocketClient.Send()` continues to work; sends to all remotes
- New `SendPlayerPositionAndZoom()` method is additive; replaces only the one call site in `Eft_PlayerPosition()`

---

## Future Extensions

With `BrowserRemote` class in place:
- Per-remote zoom override (if needed)
- Remote labels/names (UI display: "Map 1", "Map 2")
- Enable/disable individual remotes (broadcast to a subset)
- Add a management UI (add/remove/reorder remotes without editing the settings string)
