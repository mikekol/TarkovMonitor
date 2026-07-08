# Task 4: Broadcast Messages to All Remotes

**Position in project:** Fourth task — modifies the `Send()` methods in `SocketClient` to broadcast each message to ALL configured remotes instead of just one. This enables the core multi-remote feature.

**Dependencies:** Requires Task 3 (`GetAllRemotes()` method) to exist.

**File to Modify:** `TarkovMonitor/SocketClient.cs`

**Exact Code Changes:**

Find and REPLACE the existing `Send(JsonObject message)` method:

```csharp
public static Task Send(JsonObject message)
{
    return Send(new List<JsonObject> { message });
}
```

Find and REPLACE the existing `Send(List<JsonObject> messages)` method:

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

**Steps:**
1. Open `TarkovMonitor/SocketClient.cs`
2. Find the existing `Send(JsonObject message)` method (single parameter version)
3. Replace its entire body with the code above (delegates to list version)
4. Find the existing `Send(List<JsonObject> messages)` method (list parameter version)
5. Replace its entire body with the code above (nested loop over remotes)
6. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
7. Commit with message: `"feat: broadcast messages to all configured remotes"`

**Success Criteria:**
- `Send(JsonObject message)` delegates to the list version
- `Send(List<JsonObject> messages)` iterates all remotes from `GetAllRemotes()`
- Each message gets the remote's ID in the `sessionID` field
- Existing callers of `Send()` continue to work unchanged (backward compatible)
- Project compiles without errors
- Commit is made

**Key Behavior:**
- If no remotes configured, method returns early (empty remoteId string)
- If remotes list is empty after parsing, method returns early
- Each message is sent to each remote (nested loop)
- idleTimer is still updated once at the end (not per-remote)

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-4-report.md`
