"""Trigger Stream Deck buttons by action title via the Elgato MCP server.

Usage:
    python streamdeck_press.py list          # show all executable actions
    python streamdeck_press.py play
    python streamdeck_press.py pause
"""

import json
import sys
import urllib.error
import urllib.request

# ── Configuration ─────────────────────────────────────────────────────────────

MCP_URL = "http://localhost:9090/mcp"

# Map command → action title as it appears in the Stream Deck MCP profile.
# Run 'list' to see available titles.
BUTTONS = {
    "play":  "Resume",
    "pause": "Pause",
}

# ──────────────────────────────────────────────────────────────────────────────

_session_id: str | None = None


def _post(method: str, params: dict | None = None) -> dict:
    global _session_id
    payload = json.dumps({
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
        "params": params or {},
    }).encode()
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
    }
    if _session_id:
        headers["Mcp-Session-Id"] = _session_id
    req = urllib.request.Request(MCP_URL, data=payload, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req) as resp:
            if sid := resp.headers.get("Mcp-Session-Id"):
                _session_id = sid
            body = resp.read().decode()
            if not body.strip():
                return {}
            if resp.headers.get("Content-Type", "").startswith("text/event-stream"):
                for line in body.splitlines():
                    if line.startswith("data:"):
                        return json.loads(line[5:].strip())
                return {}
            return json.loads(body)
    except urllib.error.HTTPError as e:
        body = e.read().decode(errors="replace")
        raise RuntimeError(f"HTTP {e.code}: {body}") from e
    except urllib.error.URLError as e:
        raise RuntimeError(
            f"Cannot reach {MCP_URL} — is the Elgato MCP server running? ({e.reason})"
        ) from e


def _initialize() -> None:
    _post("initialize", {
        "protocolVersion": "2024-11-05",
        "capabilities": {},
        "clientInfo": {"name": "streamdeck-press", "version": "1.0"},
    })
    _post("notifications/initialized")


def _call_sd_tool(tool_name: str, arguments: dict) -> dict:
    result = _post("tools/call", {"name": tool_name, "arguments": arguments})
    if "error" in result:
        err = result["error"]
        raise RuntimeError(f"MCP error {err.get('code')}: {err.get('message')}")
    content = result.get("result", {}).get("content", [])
    for item in content:
        if item.get("type") == "text":
            try:
                return json.loads(item["text"])
            except json.JSONDecodeError:
                return {"text": item["text"]}
    return result.get("result", {})


def list_actions() -> None:
    _initialize()
    data = _call_sd_tool("streamdeck__get_executable_actions", {})
    print(json.dumps(data, indent=2))


def press_button(command: str) -> None:
    target_title = BUTTONS[command]
    _initialize()

    data = _call_sd_tool("streamdeck__get_executable_actions", {})
    actions = data if isinstance(data, list) else data.get("actions", [])

    match = next(
        (a for a in actions if a.get("title", "").lower() == target_title.lower()),
        None,
    )
    if match is None:
        available = [a.get("title") for a in actions]
        raise RuntimeError(
            f"No action titled {target_title!r}. Available: {available}"
        )

    action_id = match["id"]
    _call_sd_tool("streamdeck__execute_action", {"id": action_id})
    print(f"OK: {command} → {target_title!r} (id={action_id})")


if __name__ == "__main__":
    commands = ["list", *BUTTONS]
    if len(sys.argv) != 2 or sys.argv[1] not in commands:
        print(f"Usage: {sys.argv[0]} [{'|'.join(commands)}]", file=sys.stderr)
        sys.exit(1)

    try:
        if sys.argv[1] == "list":
            list_actions()
        else:
            press_button(sys.argv[1])
    except RuntimeError as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)
