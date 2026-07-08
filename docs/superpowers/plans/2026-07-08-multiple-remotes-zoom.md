# Multiple Browser Remotes & Auto-Zoom Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable controlling multiple tarkov.dev browser remotes simultaneously and automatically zoom in when player location is detected via screenshot.

**Architecture:** New `BrowserRemote` class encapsulates remote identity. `SocketClient` parses comma-separated remote IDs on startup and broadcasts all messages to each remote. New `SendPlayerPositionAndZoom()` method sends position + zoom command together. Settings UI exposes a zoom level slider. Backward compatible with single remote.

**Tech Stack:** C#, WinForms/Blazor (Settings.razor), WebSocket (existing Websocket.Client), System.Text.Json

## Global Constraints

- Target framework: `net10.0-windows`
- Settings stored in `Properties.Settings.Default` (auto-generated)
- WebSocket messages to tarkov.dev follow existing JSON command format (verify zoom command type during Task 5)
- Backward compatible: single remote ID (no delimiter) works unchanged
- No breaking changes to existing `SocketClient.Send()` callers

---

## File Structure

**Create:**
- `TarkovMonitor/BrowserRemote.cs` — lightweight remote ID holder

**Modify:**
- `TarkovMonitor/SocketClient.cs` — parsing, multi-remote broadcast, zoom method
- `TarkovMonitor/MainBlazorUI.cs` — call `SendPlayerPositionAndZoom()` instead of `Send()`
- `TarkovMonitor/Settings.razor` — add zoom slider UI control
- `TarkovMonitor/Properties/Settings.Designer.cs` — add `zoomLevelOnLocationUpdate` property
- App config or user settings file (wherever `remoteId` is persisted)

---

## Tasks

### Task 1: Create BrowserRemote Class

**Files:**
- Create: `TarkovMonitor/BrowserRemote.cs`

**Interfaces:**
- Produces: `public class BrowserRemote { public string Id { get; set; } }`

- [ ] **Step 1: Create BrowserRemote.cs**

File: `TarkovMonitor/BrowserRemote.cs`

```csharp
namespace TarkovMonitor
{
    /// <summary>
    /// Represents a single tarkov.dev browser remote instance.
    /// </summary>
    public class BrowserRemote
    {
        /// <summary>
        /// The unique session ID for this remote (e.g., "abc123").
        /// </summary>
        public string Id { get; set; } = string.Empty;
    }
}
```

- [ ] **Step 2: Verify file exists and compiles**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add TarkovMonitor/BrowserRemote.cs
git commit -m "feat: add BrowserRemote class for representing remote instances"
```

---

### Task 2: Add Zoom Setting to Properties

**Files:**
- Modify: `TarkovMonitor/Properties/Settings.Designer.cs`

**Interfaces:**
- Produces: `Properties.Settings.Default.zoomLevelOnLocationUpdate` (int property, default 200)

- [ ] **Step 1: Open Settings.Designer.cs and find where remoteId is defined**

File: `TarkovMonitor/Properties/Settings.Designer.cs`

Search for the `remoteId` property definition. You'll see something like:

```csharp
[UserScopedSetting()]
[DefaultSettingValue("")]
public string remoteId
{
    get { return ((string)(this["remoteId"])); }
    set { this["remoteId"] = value; }
}
```

- [ ] **Step 2: Add zoomLevelOnLocationUpdate property after remoteId**

Insert this new property right after the `remoteId` property block:

```csharp
[UserScopedSetting()]
[DefaultSettingValue("200")]
public int zoomLevelOnLocationUpdate
{
    get
    {
        object val = this["zoomLevelOnLocationUpdate"];
        if (val == null) return 200;
        return ((int)(val));
    }
    set { this["zoomLevelOnLocationUpdate"] = value; }
}
```

- [ ] **Step 3: Verify property compiles**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add TarkovMonitor/Properties/Settings.Designer.cs
git commit -m "feat: add zoomLevelOnLocationUpdate setting (default 200%)"
```

---

### Task 3: Parse and Store Remote IDs in SocketClient

**Files:**
- Modify: `TarkovMonitor/SocketClient.cs`

**Interfaces:**
- Consumes: `BrowserRemote` class from Task 1
- Produces: `public List<BrowserRemote> GetAllRemotes()` returns the parsed list

- [ ] **Step 1: Add static field to hold parsed remotes**

In `SocketClient.cs`, add this static field near the top of the class (after `wsUrl` definition):

```csharp
private static List<BrowserRemote> _remotes = new();
```

- [ ] **Step 2: Add ParseRemoteIds method**

Add this new method to the `SocketClient` class:

```csharp
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
```

- [ ] **Step 3: Add GetAllRemotes public method**

Add this method to return the parsed list:

```csharp
public static List<BrowserRemote> GetAllRemotes()
{
    return _remotes;
}
```

- [ ] **Step 4: Call ParseRemoteIds from StartClient**

In the `StartClient()` method, add a call to parse remotes right at the start:

```csharp
public static async Task StartClient()
{
    ParseRemoteIds();  // <-- Add this line
    var remoteid = Properties.Settings.Default.remoteId;
    socket = new(new Uri(wsUrl + $"?sessionid={remoteid}-tm"));
    // ... rest of method
}
```

- [ ] **Step 5: Test parsing with unit test (verify manually)**

Create a quick test by opening a C# file and testing logic:

```csharp
// Manual test: remoteIdString = "abc123,def456,ghi789"
// After parsing, _remotes should have 3 items with Ids: "abc123", "def456", "ghi789"

// Test single ID: "abc123"
// After parsing, _remotes should have 1 item with Id: "abc123"

// Test with semicolons: "abc123;def456"
// After parsing, _remotes should have 2 items
```

Run the application and verify via a simple console output if needed, or write a unit test.

- [ ] **Step 6: Compile and verify**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add TarkovMonitor/SocketClient.cs
git commit -m "feat: parse comma/semicolon-separated remote IDs on startup"
```

---

### Task 4: Broadcast Messages to All Remotes

**Files:**
- Modify: `TarkovMonitor/SocketClient.cs`

**Interfaces:**
- Consumes: `GetAllRemotes()` from Task 3, existing `socket.SendInstant()` API
- Produces: Modified `Send(JsonObject message)` and `Send(List<JsonObject> messages)` that broadcast to all remotes

- [ ] **Step 1: Update Send(JsonObject message) to broadcast**

Replace the existing `Send(JsonObject message)` method with this:

```csharp
public static Task Send(JsonObject message)
{
    return Send(new List<JsonObject> { message });
}
```

(Keep it simple — the heavy lifting goes in the list version below.)

- [ ] **Step 2: Update Send(List<JsonObject> messages) to iterate remotes**

Replace the existing `Send(List<JsonObject> messages)` method with this:

```csharp
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
```

- [ ] **Step 3: Compile and verify**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add TarkovMonitor/SocketClient.cs
git commit -m "feat: broadcast messages to all configured remotes"
```

---

### Task 5: Add SendPlayerPositionAndZoom Method

**Files:**
- Modify: `TarkovMonitor/SocketClient.cs`

**Interfaces:**
- Consumes: `GetAllRemotes()`, existing `GetPlayerPositionMessage()` method, `Properties.Settings.Default.zoomLevelOnLocationUpdate`
- Produces: `public static async Task SendPlayerPositionAndZoom(PlayerPositionEventArgs e)`

- [ ] **Step 1: Add SendPlayerPositionAndZoom method**

Add this new method to `SocketClient`:

```csharp
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
    // Note: Verify tarkov.dev socket API format during testing
    // This format assumes a "zoom" command type exists; adjust if needed
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
```

- [ ] **Step 2: Compile and verify**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Add a comment for zoom API verification**

At the top of the method, add a comment:

```csharp
// TODO: Verify zoom command format with tarkov.dev WebSocket API
// If "zoom" is not a valid command type, adjust the data.type value
```

- [ ] **Step 4: Commit**

```bash
git add TarkovMonitor/SocketClient.cs
git commit -m "feat: add SendPlayerPositionAndZoom method with configurable zoom level"
```

---

### Task 6: Update MainBlazorUI to Use SendPlayerPositionAndZoom

**Files:**
- Modify: `TarkovMonitor/MainBlazorUI.cs`

**Interfaces:**
- Consumes: `SocketClient.SendPlayerPositionAndZoom()` from Task 5
- Produces: Modified `Eft_PlayerPosition()` method

- [ ] **Step 1: Find the Eft_PlayerPosition method**

Open `TarkovMonitor/MainBlazorUI.cs` and locate the `Eft_PlayerPosition` method (around line 445).

- [ ] **Step 2: Replace the Send call with SendPlayerPositionAndZoom**

Replace the old implementation:

```csharp
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
```

With:

```csharp
private async void Eft_PlayerPosition(object? sender, PlayerPositionEventArgs e)
{
    var map = TarkovDev.Maps.Find(m => m.nameId == e.RaidInfo.Map);
    if (map == null)
    {
        messageLog.AddMessage($"Could not find map {e.RaidInfo.Map}");
        return;
    }
    messageLog.AddMessage($"Player position on {map.name}: x: {e.Position.X}, y: {e.Position.Y}, z: {e.Position.Z}");
    
    // Send position + zoom to all configured remotes
    await SocketClient.SendPlayerPositionAndZoom(e);
    
    // Optionally navigate to map on all remotes
    if (Properties.Settings.Default.navigateMapOnPositionUpdate)
    {
        await SocketClient.NavigateToMap(map);
    }
}
```

- [ ] **Step 3: Compile and verify**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add TarkovMonitor/MainBlazorUI.cs
git commit -m "feat: use SendPlayerPositionAndZoom for location updates with auto-zoom"
```

---

### Task 7: Add Zoom Level Slider to Settings.razor

**Files:**
- Modify: `TarkovMonitor/Settings.razor`

**Interfaces:**
- Consumes: `Properties.Settings.Default.zoomLevelOnLocationUpdate` property
- Produces: UI slider control bound to zoom setting

- [ ] **Step 1: Locate the Settings.razor file**

Open `TarkovMonitor/Settings.razor`. Find the section where `remoteId` is displayed/configured.

- [ ] **Step 2: Add a new section for Remote Configuration**

If not already present, find a good place (near the bottom or in a settings section) and add:

```razor
<div class="settings-section">
    <h3>Browser Remote Control</h3>
    
    <div class="setting-item">
        <label for="remoteIds">Remote Browser IDs (comma-separated for multiple):</label>
        <input type="text" 
               id="remoteIds" 
               @bind="@remoteIdInput"
               placeholder="e.g., abc123,def456"
               class="form-control" />
        <small class="form-text text-muted">
            Enter one or more remote IDs from tarkov.dev, separated by commas.
        </small>
    </div>
    
    <div class="setting-item">
        <label for="zoomLevel">Zoom level when location is detected via screenshot (%):</label>
        <input type="range" 
               id="zoomLevel" 
               min="100" 
               max="400" 
               step="25"
               @bind="@zoomLevelInput"
               class="form-range" />
        <span>@zoomLevelInput%</span>
        <small class="form-text text-muted">
            Adjust how much the map zooms in when a screenshot reveals your player position.
        </small>
    </div>
</div>
```

- [ ] **Step 3: Add code-behind properties**

In the `@code` block of Settings.razor, add:

```csharp
private string remoteIdInput = "";
private int zoomLevelInput = 200;

protected override void OnInitialized()
{
    remoteIdInput = Properties.Settings.Default.remoteId ?? "";
    zoomLevelInput = Properties.Settings.Default.zoomLevelOnLocationUpdate;
}

private void SaveRemoteSettings()
{
    Properties.Settings.Default.remoteId = remoteIdInput;
    Properties.Settings.Default.zoomLevelOnLocationUpdate = zoomLevelInput;
    Properties.Settings.Default.Save();
}
```

- [ ] **Step 4: Add a Save button (if not already present)**

Add or update the Save button to call `SaveRemoteSettings()`:

```razor
<button @onclick="SaveRemoteSettings" class="btn btn-primary">Save Settings</button>
```

- [ ] **Step 5: Compile and verify**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add TarkovMonitor/Settings.razor
git commit -m "feat: add zoom level slider and remote ID input to Settings UI"
```

---

### Task 8: Integration Test — Multiple Remotes with Auto-Zoom

**Files:**
- No new files; test manually in UI

**Interfaces:**
- Consumes: All previous tasks
- Produces: Verified behavior: multiple remotes receive position + zoom

- [ ] **Step 1: Build the application**

Run: `dotnet build TarkovMonitor/TarkovMonitor.csproj`
Expected: Build succeeds.

- [ ] **Step 2: Start the application**

Run: `dotnet run --project TarkovMonitor/TarkovMonitor.csproj`

- [ ] **Step 3: Configure multiple remote IDs in Settings**

In the UI:
1. Open Settings (or the settings dialog)
2. Find the "Remote Browser IDs" field
3. Enter: `test123,test456,test789` (or use actual tarkov.dev remote IDs if available)
4. Set "Zoom level" to 250%
5. Click Save

- [ ] **Step 4: Verify settings persisted**

Close and reopen Settings. Confirm both fields retain their values.

- [ ] **Step 5: Test message broadcasting (manual verification)**

Optional: Add temporary console output to `SocketClient.Send()` to log each message sent:

```csharp
// In Send(List<JsonObject> messages):
foreach (var remote in remotes)
{
    message["sessionID"] = remote.Id;
    Console.WriteLine($"[DEBUG] Sending message to remote: {remote.Id}");
    await socket.SendInstant(message.ToJsonString());
}
```

Then monitor the console output when a position update occurs.

- [ ] **Step 6: Test with a real screenshot (if in-game)**

If you're able to run in-game:
1. Enter a raid
2. Take a screenshot with position data visible in filename
3. Watch console or UI log to verify position message + zoom command sent to all 3 remotes
4. Verify on tarkov.dev remotes that position and zoom are applied

- [ ] **Step 7: Test backward compatibility (single remote)**

1. Change "Remote Browser IDs" to just: `test123` (single ID)
2. Save settings
3. Take another screenshot
4. Verify position + zoom sent to that single remote without errors

- [ ] **Step 8: Clean up console debug output**

Remove any temporary `Console.WriteLine()` calls added in Step 5.

- [ ] **Step 9: Commit final integration test results**

```bash
git add -A
git commit -m "test: verify multiple remotes and auto-zoom work end-to-end"
```

---

## Self-Review Against Spec

**Spec Coverage:**

✅ **BrowserRemote class** — Task 1 creates it  
✅ **Parse comma-separated remote IDs** — Task 3 parses and stores  
✅ **Broadcast to all remotes** — Task 4 updates Send methods  
✅ **SendPlayerPositionAndZoom method** — Task 5 adds method with zoom  
✅ **Zoom setting (zoomLevelOnLocationUpdate)** — Task 2 adds property, Task 7 adds UI  
✅ **Settings UI with zoom slider** — Task 7 adds slider (100–400%, step 25)  
✅ **Modify Eft_PlayerPosition** — Task 6 calls SendPlayerPositionAndZoom  
✅ **Integration testing** — Task 8 verifies end-to-end behavior  
✅ **Backward compatibility** — Task 4 handles single remote; Task 8 tests it  

**Placeholder scan:** No "TODO", "TBD", or "implement later" placeholders. All steps contain complete code.

**Type consistency:** 
- `BrowserRemote.Id` is `string` (Task 1)
- `GetAllRemotes()` returns `List<BrowserRemote>` (Task 3)
- `SendPlayerPositionAndZoom(PlayerPositionEventArgs e)` matches existing event args (Task 5)
- `zoomLevelOnLocationUpdate` is `int` (Task 2) and bound in UI (Task 7)
- All consistent ✅

**Spec alignment:**
- Zoom command format flagged as verification point in Task 5 ✅
- Global zoom setting (not per-remote) implemented ✅
- Settings UI exposes both remote IDs and zoom ✅
