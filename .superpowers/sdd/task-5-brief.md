# Task 5: Add SendPlayerPositionAndZoom Method

**Position in project:** Fifth task — adds the new `SendPlayerPositionAndZoom()` public method to `SocketClient` that sends both player position AND zoom command to all remotes. This method is called by Task 6.

**Dependencies:** Requires Task 4 (`Send()` methods updated) and Task 2 (zoom setting property).

**File to Modify:** `TarkovMonitor/SocketClient.cs`

**Exact Code to Add:**

Add this new public method to `SocketClient` (can be placed near other public methods like `NavigateToMap`):

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

At the top of the method, you may add a TODO comment if desired:

```csharp
// TODO: Verify zoom command format with tarkov.dev WebSocket API
// If "zoom" is not a valid command type, adjust the data.type value
```

**Steps:**
1. Open `TarkovMonitor/SocketClient.cs`
2. Add the new public method `SendPlayerPositionAndZoom()` to the class
3. Verify it uses `GetPlayerPositionMessage()` (existing method, do not modify)
4. Verify it reads `Properties.Settings.Default.zoomLevelOnLocationUpdate` (added in Task 2)
5. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
6. Commit with message: `"feat: add SendPlayerPositionAndZoom method with configurable zoom level"`

**Success Criteria:**
- Method signature is: `public static async Task SendPlayerPositionAndZoom(PlayerPositionEventArgs e)`
- Zoom level is read from settings with clamping (100-400 range)
- Position message is sent using existing `GetPlayerPositionMessage()` method
- Zoom message is sent as a command with type "zoom" and value as the zoom level
- Both messages are sent together via `Send(messages)`
- Exception handling uses `ExceptionThrown` event (existing pattern)
- Project compiles without errors
- Commit is made

**Note on Zoom Command Format:**
The zoom command format (`type: "zoom"`) is a specification assumption. During Task 8 (integration testing), this may need adjustment if tarkov.dev's WebSocket API doesn't support this exact format. The TODO comment marks this as a verification point.

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-5-report.md`
