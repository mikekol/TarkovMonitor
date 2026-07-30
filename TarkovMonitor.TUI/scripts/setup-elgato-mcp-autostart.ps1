<#
.SYNOPSIS
    Registers a Task Scheduler logon task that starts the Elgato MCP server in
    HTTP mode automatically when the current user logs on.

.DESCRIPTION
    streamdeck_press.py depends on the Elgato MCP server (@elgato/mcp-server)
    running as a local HTTP server on port 9090. The server is a Node.js process
    launched with the --http flag; it is NOT started automatically by Stream Deck.

    This script creates a Task Scheduler task under the current user account that
    runs the server at logon, before any interactive session is available, so it
    is ready by the time Stream Deck and any calling scripts start.

    The task is idempotent: running this script again updates the existing task
    rather than creating a duplicate.

    To remove the task:
        Unregister-ScheduledTask -TaskName "ElgatoMcpServer" -Confirm:$false

.NOTES
    Requires the @elgato/mcp-server npm package installed globally:
        npm install -g @elgato/mcp-server
#>

$TaskName = "ElgatoMcpServer"

# Resolve node.exe from the PATH so this works regardless of nvm or direct installs.
$NodeExe = (Get-Command node -ErrorAction Stop).Source

# The globally installed package lands in %APPDATA%\npm\node_modules on Windows.
$ServerScript = Join-Path $env:APPDATA "npm\node_modules\@elgato\mcp-server\bin\index.js"

if (-not (Test-Path $ServerScript)) {
    Write-Error "Elgato MCP server not found at: $ServerScript`nRun: npm install -g @elgato/mcp-server"
    exit 1
}

# Run hidden so no console window appears at logon.
$Action = New-ScheduledTaskAction `
    -Execute $NodeExe `
    -Argument "`"$ServerScript`" --http"

# Trigger at logon for the current user only.
$Trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME

$Settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0) `  # No timeout — runs indefinitely.
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable

# Register (or overwrite) the task scoped to the current user, no elevation needed.
Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $Action `
    -Trigger $Trigger `
    -Settings $Settings `
    -RunLevel Limited `
    -Force | Out-Null

Write-Host "Task '$TaskName' registered. It will start the Elgato MCP server at next logon."
Write-Host "To start it now without logging off:"
Write-Host "  Start-ScheduledTask -TaskName '$TaskName'"
