# Message Explorer — Tool Overview

`docs/tools/message-explorer.html` is a standalone, no-install debugging tool for inspecting the WebSocket messages flowing between TarkovMonitor and tarkov-dev browser clients. Open it directly in any browser — no server or build step required.

---

## What it connects to

The tool connects to `wss://socket.tarkov.dev?sessionid={id}{suffix}` — the same relay that TarkovMonitor and the tarkov-dev map page use. You supply your **Session ID** (the Remote ID configured in TarkovMonitor Settings) and pick a connection mode:

| Mode | Suffix | What you see |
|------|--------|--------------|
| browser (receive commands) | *(none)* | Commands arriving at the browser — same view as tarkov-dev |
| TarkovMonitor sender | `-tm` | Messages being sent by TarkovMonitor |
| passive listener | `-spy` | Relay-specific passive view |
| custom | anything | Any other session suffix |

The tool responds to server `ping` frames with `pong` automatically, so the connection stays alive indefinitely.

---

## Layout

```
┌─────────────────────────────────────────────────────────┐
│ Header: Session ID · Connect as · Connect/Disconnect     │
├─────────────────────────────────────────────────────────┤
│ Toolbar: Filter · Type · Auto-scroll · Hide ping/pong    │
│          Clear · Export JSON · [Total / Shown / Last]    │
├──────────────────────────────┬──┬──────────────────────┤
│ Message list (scrollable)    │▐▌│ Detail panel         │
│                              │  │ (JSON tree)          │
│  timestamp · dir · type      │  │                      │
│  inline preview              │  │ Expand all           │
│                              │  │ Collapse all  Copy   │
└──────────────────────────────┴──┴──────────────────────┘
```

The divider between the list and detail panel is **drag-resizable** — hover it to see it highlight blue, then drag left/right. Each pane has a 200px minimum width.

---

## Message list

Each row shows:
- **Timestamp** — `HH:MM:SS.mmm`
- **Direction** — `↓` incoming / `·` system event
- **Type badge** — colour-coded: `playerPosition` (green), `map` (blue), `ping`/`pong` (grey), others (default)
- **Inline preview** — for `playerPosition`: `map=interchange  x=100.0 y=50.0 z=200.0  rot=45.0°  radius=200m`; for `map`: `navigate → rezervbase`

`playerPosition` rows are additionally highlighted with an orange left border so they stand out during live capture.

Click any row to load the full payload in the detail panel.

---

## Detail panel

Displays the selected message as an **interactive JSON tree**:

- **Collapse** any object or array by clicking its `{` / `[` toggle — it collapses to a `3 keys` / `2 items` summary inline
- **Expand** by clicking again
- **Expand all / Collapse all** buttons in the panel header
- **Copy** — copies the full prettified JSON to the clipboard
- Syntax-highlighted: keys (blue), strings (green), numbers (orange), booleans (red), null (grey)

---

## Toolbar controls

| Control | Function |
|---------|----------|
| Filter text box | Live full-text search across raw JSON |
| Type dropdown | Narrow to `playerPosition`, `map`, `ping/pong`, or `command` |
| Auto-scroll | Keep the list scrolled to the latest message |
| Hide ping/pong | Suppress keepalive noise (on by default) |
| Clear | Wipe the captured session |
| Export JSON | Download all captured messages as a `.json` file |

The stats bar (top right of toolbar) shows **Total** messages captured, **Shown** after filters, and the **Last** message timestamp.

---

## Typical debugging workflow

1. Open `message-explorer.html` in a browser.
2. Enter your Remote ID and choose **browser (receive commands)**.
3. Connect. Start a raid and take a screenshot in TarkovMonitor.
4. Watch for an orange-highlighted `playerPosition` row to appear.
5. Click it and inspect the detail panel — confirm whether `viewRadius` is present in `data` (it appears only on the first screenshot of each raid).
6. If you want to see what TarkovMonitor is *sending*, reconnect with the `-tm` suffix.
7. Export the session if you want to share or diff the captures.

---

## File location

```
Q:\repos\TarkovMonitor\docs\tools\message-explorer.html
```

Self-contained — no external dependencies, no CDN calls. Safe to copy anywhere.
