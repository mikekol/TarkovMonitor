# Claude Code Instructions for TarkovMonitor Service Refactor

## Project Goals

Extract log monitoring and TarkovTracker API updates into a Windows usermode gRPC service. UI becomes a client. This project is Phase 4: testing and integration — implementation is complete.

## Tool Usage Preferences

**✅ Preferred:**
- Use Read, Edit, Write tools directly for file operations
- Use native Bash/PowerShell for git and dotnet commands
- Use Bash/PowerShell to run build, test, and debug

**❌ Avoid:**
- Desktop_Commander for most operations (use Read/Edit/Write instead)
- Excessive PowerShell escaping issues (prefer native Windows paths in PowerShell)

## Solution Structure

```
Q:\repos\TarkovMonitor\
├── TarkovMonitor\                          (UI project — pure gRPC client)
│   ├── TarkovMonitor.csproj
│   ├── Services\
│   │   └── GameEventClient.cs              ← gRPC client with typed events
│   ├── MainBlazorUI.cs                     ← Uses GameEventClient, no GameWatcher
│   ├── TimersManager.cs                    ← Takes GameEventClient (not GameWatcher)
│   ├── Stats.cs                            ← AddFleaSale takes plain params
│   └── ...
├── TarkovMonitor.Service\                  ← Service project
│   ├── TarkovMonitor.Service.csproj
│   ├── Program.cs
│   ├── Services\
│   │   ├── GameEventBroadcasterService.cs  ← Broadcasts all game events
│   │   ├── GameWatcherHostedService.cs     ← Starts GameWatcher on service startup
│   │   ├── IServiceConfiguration.cs
│   │   └── JsonServiceConfiguration.cs
│   ├── Protos\
│   │   └── game_events.proto
│   └── appsettings.json
├── TarkovMonitor.Service.TestServer\       ← Test server for UI testing
│   ├── TarkovMonitor.Service.TestServer.csproj
│   ├── Program.cs
│   └── TestGameEventService.cs
└── ...
```

## Target Framework

All projects: **net10.0-windows** (upgraded from net6.0-windows)

## Build & Run

```bash
# Build all projects
dotnet build

# Run service (in one terminal)
cd TarkovMonitor.Service && dotnet run

# Run test server (alternative to real service, for UI testing)
cd TarkovMonitor.Service.TestServer && dotnet run

# Run UI (in another terminal)
cd TarkovMonitor && dotnet run
```

## Key Concepts

### gRPC Service (TarkovMonitor.Service)
- **Entry point:** `Program.cs` — Host, DI, Kestrel on port 50051
- **Startup:** `GameWatcherHostedService` — starts GameWatcher; logs path auto-detected from EFT registry key, overridden only if `customLogsPath` set in appsettings.json
- **Service impl:** `GameEventBroadcasterService.cs` — broadcasts 18+ game events via gRPC streaming
  - All `GameWatcher` events serialized into proto `GameEvent.Data` (map<string, string>)
  - Stream list managed with lock for correct cleanup
  - `GetConfig()`, `UpdateConfig()`, `GetStatus()` RPC methods
- **Config:** `IServiceConfiguration` + `JsonServiceConfiguration` handles token/logs path persistence

### gRPC Client (TarkovMonitor/Services/GameEventClient.cs)
- **Typed events:** Mirrors GameWatcher event surface — `RaidStarted`, `TaskFinished`, `FleaSold`, etc.
- **Dispatch:** `DispatchGameEvent()` reads proto data map and fires typed events with reconstructed event args
- **Reconnection:** `ConnectionStateChanged` with `IsConnected = false` triggers retry in UI
- **RPC passthrough:** `GetConfigAsync()`, `UpdateConfigAsync()` for config management
- **Event args types defined in file:** `TaskEventArgs`, `FleaSaleEventArgs`, `FleaExpiredEventArgs`, `GroupInviteAcceptedEventArgs`, `GroupUserLeaveEventArgs`

### UI (TarkovMonitor/MainBlazorUI.cs)
- No `GameWatcher` — uses `GameEventClient` exclusively
- `GameWatcher.CurrentProfile` kept in sync via `ProfileChanged` / `InitialReadComplete` handlers (needed for static references elsewhere in UI code)
- Connects asynchronously on startup with 5-second retry loop
- Reconnects on `ConnectionStateChanged` with `IsConnected = false`
- Settings change for `customLogsPath` calls `UpdateConfigAsync()` to push to service

### Proto Definitions (Protos/game_events.proto)
- Generic `GameEvent` message with `event_type` string and `data` map<string, string>
- All event-specific data serialized into the map (JSON used for complex types like screenshots list, receivedItems dict)
- `RaidType` reconstructed from timing hack: PMC gets `StartingTime = StartedTime - 10s` to satisfy the computed property

### Test Server (TarkovMonitor.Service.TestServer)
- Same gRPC interface as real service
- Interactive CLI: `raid_start`, `raid_end`, `task`, `custom`, `status`, `exit`

## Common Tasks

### Add a New gRPC RPC Method
1. Define message and service method in `Protos/game_events.proto`
2. Implement in `GameEventBroadcasterService.cs`
3. Call from UI via `GameEventClient` (add public method)

### Add a New Game Event Type
1. Subscribe to `GameWatcher` event in `GameEventBroadcasterService.SubscribeToGameWatcherEvents()`
2. Serialize data into `Dictionary<string, string>` and call `Broadcast()`
3. Add typed event + dispatch case in `GameEventClient.DispatchGameEvent()`
4. Subscribe in `MainBlazorUI.cs`

### Test Event Flow
1. Start TestServer: `dotnet run --project TarkovMonitor.Service.TestServer`
2. Start UI: `dotnet run --project TarkovMonitor`
3. In TestServer CLI: `raid_start test-raid-123`
4. Observe event in UI message log

### Debug gRPC Issues
```bash
# Check if service is listening
netstat -ano | findstr :50051

# Check proto code generation
# Look in obj/Debug/net10.0/Protos/ for generated files
```

## Git Workflow

**Branches:**
- Current: `UVB76` (working branch on your fork)
- Upstream: `master` (the-hideout/TarkovMonitor)

**Remotes:**
- `origin` = the-hideout/TarkovMonitor (upstream — pull to sync)
- `fork` = mikekol/TarkovMonitor (your fork — push here)

**Workflow:**
```bash
git pull origin master        # sync with upstream
git push fork UVB76           # push to your fork
# PR: fork/UVB76 → the-hideout/master
```

## Next Steps (Phase 4 — Testing)

1. **End-to-end test** — Start service + UI, verify events flow through
2. **Test with TestServer** — Use TestServer CLI to fire events, verify UI reacts
3. **Verify reconnection** — Kill service while UI is running, restart service, verify UI reconnects
4. **Stretch goal** — Windows Service installation script (SC.exe)

## Notes

- Proto code is auto-generated by Grpc.Tools. Don't edit generated files.
- GameWatcher, LogMonitor, TarkovTracker live in original TarkovMonitor project. Service uses project reference.
- Service listens on HTTP/2 (gRPC requirement). Use `http://localhost:50051` (not https).
- `RaidInfo.RaidType` is a computed property — can't be set directly. Client reconstructs timing values to make it compute to the right enum value (PMC: StartingTime = StartedTime - 10s; Scav: StartingTime = null; PVE: Profile.Type = PVE).
- `WFO1000` warning suppressed in TarkovMonitor.csproj — pre-existing `Splash.cs` issue with .NET 10 WinForms analyzer.
