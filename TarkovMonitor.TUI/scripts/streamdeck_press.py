"""Trigger Stream Deck actions by command name via the Elgato MCP server.

Architecture
------------
This script sits on top of the Elgato MCP server (@elgato/mcp-server), a
Node.js process that must be running before this script is called. It bridges
between the MCP (Model Context Protocol) standard and Stream Deck's internal
IPC, translating JSON-RPC tool calls into actual button presses.

The call chain is:

    streamdeck_press.py
        │  JSON-RPC 2.0 over HTTP POST
        ▼
    Elgato MCP server (localhost:9090)
        │  named-pipe / unix socket IPC
        ▼
    Stream Deck application
        │  plugin API
        ▼
    com.kapmtools.schmotify plugin  →  Spotify

The MCP server exposes five generic Stream Deck tools:
    streamdeck__list_actions            All installed plugin actions + AI metadata
    streamdeck__get_executable_actions  Actions currently on the active profile
    streamdeck__get_context             Settings schema + callable methods for an action
    streamdeck__execute_action          Trigger an action by its instance id
    streamdeck__invoke_plugin_method    Call a named method exposed by a plugin

This script uses streamdeck__get_executable_actions to look up an action by
title, then streamdeck__execute_action to fire it.

Transport notes
---------------
The MCP server requires both "application/json" and "text/event-stream" in the
Accept header (HTTP 406 otherwise). Responses for tool calls arrive as SSE
(text/event-stream), with the JSON payload on a "data: ..." line. Initialize
responses may return an empty body, which is treated as success.

The server issues an Mcp-Session-Id header on the first response; subsequent
requests must echo it back so the server can route them to the correct session.

Prerequisites
-------------
- Stream Deck application must be running.
- Elgato MCP server must be running in HTTP mode:
      node "%APPDATA%\\npm\\node_modules\\@elgato\\mcp-server\\bin\\index.js" --http
  Run setup-elgato-mcp-autostart.ps1 once to register a logon Task Scheduler
  entry that starts this automatically.
- The target actions must exist on the active Stream Deck profile.

Usage
-----
    python streamdeck_press.py list     # print all executable actions as JSON
    python streamdeck_press.py play
    python streamdeck_press.py pause
"""

import json
import sys
import urllib.error
import urllib.request

# ── Configuration ─────────────────────────────────────────────────────────────

MCP_URL = "http://localhost:9090/mcp"

# Maps CLI command → action title as shown on the Stream Deck button.
# Run 'list' to see all available titles on the current profile.
BUTTONS = {
    "play":  "Resume",
    "pause": "Pause",
}

# ──────────────────────────────────────────────────────────────────────────────

# Persisted across calls within a single script run. The MCP server assigns
# this on the first response and expects it echoed on all subsequent requests.
_session_id: str | None = None


def _post(method: str, params: dict | None = None) -> dict:
    """Send a JSON-RPC 2.0 request to the MCP server and return the parsed response.

    Handles both plain JSON and SSE-formatted responses (text/event-stream)
    transparently. SSE payloads arrive as lines prefixed with "data: "; this
    function extracts the first such line as the JSON payload.

    Args:
        method: JSON-RPC method name, e.g. "tools/call" or "initialize".
        params: Optional parameters dict for the method.

    Returns:
        Parsed JSON response dict, or an empty dict if the response body is empty.

    Raises:
        RuntimeError: On HTTP error responses or connection failure.
    """
    global _session_id

    payload = json.dumps({
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
        "params": params or {},
    }).encode()

    headers = {
        "Content-Type": "application/json",
        # Both types required — the server returns HTTP 406 if either is absent.
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
                # initialize and notifications/initialized return empty bodies.
                return {}

            if resp.headers.get("Content-Type", "").startswith("text/event-stream"):
                # Tool call responses arrive as SSE. Extract the first data line.
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
    """Perform the MCP handshake required before any tool calls.

    Sends an initialize request declaring this client's protocol version and
    capabilities, then sends the notifications/initialized acknowledgement.
    Both steps are required by the MCP spec; skipping either causes the server
    to reject subsequent tool calls.
    """
    _post("initialize", {
        "protocolVersion": "2024-11-05",
        "capabilities": {},
        "clientInfo": {"name": "streamdeck-press", "version": "1.0"},
    })
    # Acknowledge completion of the handshake. No response is expected.
    _post("notifications/initialized")


def _call_sd_tool(tool_name: str, arguments: dict) -> dict:
    """Invoke a Stream Deck MCP tool and return its parsed result.

    Stream Deck tool responses embed their payload as a JSON string inside the
    MCP content[].text field. This function unwraps that double-encoding so
    callers receive a plain dict.

    Args:
        tool_name: One of the streamdeck__* tool names exposed by the MCP server.
        arguments: Tool-specific arguments dict.

    Returns:
        Parsed result dict from the tool response.

    Raises:
        RuntimeError: If the MCP server returns a JSON-RPC error.
    """
    result = _post("tools/call", {"name": tool_name, "arguments": arguments})

    if "error" in result:
        err = result["error"]
        raise RuntimeError(f"MCP error {err.get('code')}: {err.get('message')}")

    # Tool results are wrapped in a content array of typed items.
    content = result.get("result", {}).get("content", [])
    for item in content:
        if item.get("type") == "text":
            try:
                # Stream Deck encodes the actual payload as a JSON string.
                return json.loads(item["text"])
            except json.JSONDecodeError:
                return {"text": item["text"]}

    return result.get("result", {})


def list_actions() -> None:
    """Print all executable actions on the current Stream Deck profile as JSON.

    Useful for discovering action titles to add to the BUTTONS mapping, or for
    verifying that the MCP server is connected to Stream Deck correctly.
    """
    _initialize()
    data = _call_sd_tool("streamdeck__get_executable_actions", {})
    print(json.dumps(data, indent=2))


def press_button(command: str) -> None:
    """Execute the Stream Deck action mapped to the given command.

    Looks up the action by title in the current profile's executable action list,
    then fires it via streamdeck__execute_action. The action id (a UUID assigned
    per button placement) is used as the execution handle, not the action type id.

    Args:
        command: Key from BUTTONS, e.g. "play" or "pause".

    Raises:
        RuntimeError: If no action with the configured title is found, or if
            the MCP server returns an error during execution.
    """
    target_title = BUTTONS[command]
    _initialize()

    data = _call_sd_tool("streamdeck__get_executable_actions", {})
    actions = data if isinstance(data, list) else data.get("actions", [])

    # Match case-insensitively since Stream Deck titles can vary in casing.
    match = next(
        (a for a in actions if a.get("title", "").lower() == target_title.lower()),
        None,
    )
    if match is None:
        available = [a.get("title") for a in actions]
        raise RuntimeError(
            f"No action titled {target_title!r}. Available: {available}"
        )

    # "id" here is the per-placement instance UUID, distinct from description.id
    # which identifies the action type. execute_action requires the instance UUID.
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
