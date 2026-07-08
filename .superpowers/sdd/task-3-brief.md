# Task 3: Parse and Store Remote IDs in SocketClient

**Position in project:** Third task — adds parsing logic to `SocketClient` to read the comma-separated remote ID list from settings and convert it into a list of `BrowserRemote` objects. This is consumed by Task 4 (broadcasting) and Task 5 (zoom method).

**Dependencies:** Requires `BrowserRemote` class from Task 1 to exist.

**File to Modify:** `TarkovMonitor/SocketClient.cs`

**Exact Code to Add:**

Add this static field near the top of the `SocketClient` class (after the `wsUrl` definition, around line 9-10):

```csharp
private static List<BrowserRemote> _remotes = new();
```

Add this parsing method to the class (can be anywhere, but logically near `StartClient`):

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

Add this public method to return the parsed list:

```csharp
public static List<BrowserRemote> GetAllRemotes()
{
    return _remotes;
}
```

Modify the existing `StartClient()` method to call `ParseRemoteIds()` at the start:

```csharp
public static async Task StartClient()
{
    ParseRemoteIds();  // <-- Add this line
    var remoteid = Properties.Settings.Default.remoteId;
    socket = new(new Uri(wsUrl + $"?sessionid={remoteid}-tm"));
    socket.MessageReceived.Subscribe(msg => {
        // ... rest of existing code ...
    });
    await socket.Start();
    idleTimer.Stop();
    idleTimer.Start();
}
```

**Steps:**
1. Open `TarkovMonitor/SocketClient.cs`
2. Add the `_remotes` field after `wsUrl` definition
3. Add the `ParseRemoteIds()` method
4. Add the `GetAllRemotes()` method
5. Modify `StartClient()` to call `ParseRemoteIds()` at the beginning
6. Run `dotnet build TarkovMonitor/TarkovMonitor.csproj` to verify it compiles
7. Commit with message: `"feat: parse comma/semicolon-separated remote IDs on startup"`

**Success Criteria:**
- `_remotes` field is static List<BrowserRemote>
- `ParseRemoteIds()` correctly splits by comma and semicolon
- `GetAllRemotes()` returns the parsed list
- `StartClient()` calls `ParseRemoteIds()` at the start
- Project compiles without errors
- Commit is made

**Test Verification (manual):**
- Single ID "abc123" should parse to 1 BrowserRemote with Id "abc123"
- Multiple IDs "abc123,def456" should parse to 2 BrowserRemotes
- Semicolon delimiters "abc123;def456" should also work
- Whitespace trimming should work: " abc123 , def456 " should trim to "abc123" and "def456"

**Report to:** `Q:\repos\TarkovMonitor\.superpowers\sdd\task-3-report.md`
