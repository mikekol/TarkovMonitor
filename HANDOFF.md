# TarkovMonitor — Session Handoff

**Branch:** `UVB76` (ahead of `fork/UVB76` by 3 commits, not yet pushed)

## What happened this session

### 1. RaidInfo.Map architecture fix (`c7489b0`)

Upstream merged a change making `RaidInfo.Map` a rich `TarkovDev.Map` object instead of a plain `string nameId`. This broke compilation because `GameWatcher` lives in `TarkovMonitor.Core`, which cannot reference the UI project where `TarkovDev` is defined.

**Resolution:**
- Moved `Map`, `BossSpawn`, `BossEscort` POCOs into a new `TarkovMonitor.Core/MapTypes.cs` (same `TarkovMonitor` namespace — no import changes elsewhere)
- Added injectable `Maps` property to `GameWatcher` (replaces static `TarkovDev.Maps` calls)
- Service now fetches its own maps from `https://json.tarkov.dev/regular/maps` on startup
- gRPC wire format uses `nameId` string; client reconstructs full `Map` object on arrival
- `TarkovMonitor.csproj` TFM bumped to `net10.0-windows10.0.18362.0` for WinRT API support

### 2. TUI scav cooldown bug fix (`76d205d`)

`_on_raid_ended` and `_on_raid_exited` in `app.py` were calling `_start_scav_countdown()` unconditionally — PMC and PvE raids were incorrectly triggering the scav timer.

**Fix:** both handlers now check `info.raid_type == RaidType.SCAV` before starting the countdown. `_on_raid_exited` was also updated to use `parse_raid_info(data)` instead of `data.get("map")` so the raid type is available.

## Before you push

- **Review `TarkovMonitor.Core/MapTypes.cs`** — new file, make sure the POCOs match upstream's `TarkovDev` inner classes exactly (id, name, nameId, normalizedName, scenePath, bosses, HasGoons)
- **Review `TarkovMonitor.Service/Services/GameWatcherHostedService.cs`** — added HTTP fetch of maps on startup; check the JSON parsing is robust
- **Review `TarkovMonitor/Services/GameEventClient.cs`** — map reconstruction from `nameId` string; confirm it handles unknown maps gracefully (returns `null`, doesn't throw)
- **Installer harvest files** (`TarkovMonitor.Installer/*_harvest.wxs`) have uncommitted changes from a prior build run — these are auto-generated and gitignored-by-convention; do not commit them

## Uncommitted noise to ignore

```
TarkovMonitor.Installer/Service_harvest.wxs   ← auto-generated, don't commit
TarkovMonitor.Installer/Tools_harvest.wxs     ← auto-generated, don't commit
TarkovMonitor.Installer/UI_harvest.wxs        ← auto-generated, don't commit
```

## Push target

```bash
git push fork UVB76
```
