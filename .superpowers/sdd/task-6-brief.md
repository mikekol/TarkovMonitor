# Task 6: Update MainBlazorUI to Use SendPlayerPositionAndZoom

**Position in project:** Sixth task — updates the existing `Eft_PlayerPosition()` handler in MainBlazorUI to call the new `SendPlayerPositionAndZoom()` method instead of manually building and sending messages. This integrates the auto-zoom feature into the main location update flow.

**Dependencies:** Requires Task 5 (`SendPlayerPositionAndZoom()` method).

**File to Modify:** `TarkovMonitor/MainBlazorUI.cs`

**Current Code to Find:**

Look for the `Eft_PlayerPosition` method (around line 445). The current implementation looks like:

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

**Exact Code to Replace It With:**

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

**Steps:**
1. Open `TarkovMonitor/MainBlazorUI.cs`
2. Find the `Eft_PlayerPosition` method (search for "private async void Eft_PlayerPosition")
3. Replace the entire method body with the code above
4. Key changes:
   - Remove the manual `List<JsonObject> socketMessages = new()` construction
   - Remove the manual calls to `GetPlayerPositionMessage()` and `GetNavigateToMapMessage()`
   - Replace with a single await call to `SendPlayerPositionAndZoom(e)`
   - Keep the optional navigate-to-map call (same behavior as before)
5. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
6. Commit with message: `"feat: use SendPlayerPositionAndZoom for location updates with auto-zoom"`

**Success Criteria:**
- Method body is replaced with the new code
- `SendPlayerPositionAndZoom(e)` is called and awaited
- Navigation logic is preserved (same as before)
- Log message remains unchanged
- Project compiles without errors
- Commit is made

**Behavior Change:**
- When a screenshot location is detected, both position AND zoom are now sent to all remotes automatically
- The zoom level used is the one configured in Settings (Task 7)
- Navigation to map still works if enabled in settings

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-6-report.md`
