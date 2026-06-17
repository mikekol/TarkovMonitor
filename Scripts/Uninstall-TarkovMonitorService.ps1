#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls the TarkovMonitor Windows service.

.DESCRIPTION
    Stops and removes the TarkovMonitor service from SCM, removes the associated
    firewall rule, and optionally deletes the install directory.

    SeServiceLogonRight is NOT revoked — the service account may be used by
    other services, and silently removing a logon right could cause surprises.

.PARAMETER InstallDir
    Directory where the service binary was installed.
    Default: C:\Program Files\TarkovMonitor\Service

.PARAMETER GrpcPort
    Port used when the service was installed. Used to locate the firewall rule.
    Default: 50051

.PARAMETER RemoveFiles
    If specified, deletes the install directory and all its contents after
    removing the service. Requires confirmation unless -Confirm:$false is passed.

.EXAMPLE
    # Standard uninstall — keeps files in place
    .\Uninstall-TarkovMonitorService.ps1

.EXAMPLE
    # Remove files too
    .\Uninstall-TarkovMonitorService.ps1 -RemoveFiles

.EXAMPLE
    # Non-interactive full removal
    .\Uninstall-TarkovMonitorService.ps1 -RemoveFiles -Confirm:$false
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = "High")]
param(
    [string] $InstallDir = "C:\Program Files\TarkovMonitor\Service",
    [int]    $GrpcPort   = 50051,
    [switch] $RemoveFiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ServiceName = "TarkovMonitorService"
$FwRuleName  = "TarkovMonitor gRPC (port $GrpcPort)"

Write-Host ""
Write-Host "=== TarkovMonitor Service Uninstaller ===" -ForegroundColor Cyan
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: Stop and remove service
# ---------------------------------------------------------------------------
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stopping service ..."
        Stop-Service -Name $ServiceName -Force
        Write-Host "  Done."
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, "Remove service from SCM")) {
        Write-Host "Removing service ..."
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        Write-Host "  Done."
    }
} else {
    Write-Host "Service '$ServiceName' not registered — skipping."
}

# ---------------------------------------------------------------------------
# Step 2: Remove firewall rule
# ---------------------------------------------------------------------------
if (Get-NetFirewallRule -DisplayName $FwRuleName -ErrorAction SilentlyContinue) {
    if ($PSCmdlet.ShouldProcess($FwRuleName, "Remove firewall rule")) {
        Write-Host "Removing firewall rule '$FwRuleName' ..."
        Remove-NetFirewallRule -DisplayName $FwRuleName
        Write-Host "  Done."
    }
} else {
    Write-Host "Firewall rule '$FwRuleName' not found — skipping."
}

# ---------------------------------------------------------------------------
# Step 3: Remove files (optional)
# ---------------------------------------------------------------------------
if ($RemoveFiles) {
    if (Test-Path $InstallDir) {
        if ($PSCmdlet.ShouldProcess($InstallDir, "Delete install directory")) {
            Write-Host "Removing install directory '$InstallDir' ..."
            Remove-Item -Path $InstallDir -Recurse -Force
            Write-Host "  Done."
        }
    } else {
        Write-Host "Install directory '$InstallDir' not found — skipping."
    }
} else {
    Write-Host ""
    Write-Host "NOTE: Files remain in '$InstallDir'." -ForegroundColor Yellow
    Write-Host "      Re-run with -RemoveFiles to delete them." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== Uninstall complete ===" -ForegroundColor Green
Write-Host ""
