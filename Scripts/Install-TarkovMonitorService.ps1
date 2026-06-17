#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs TarkovMonitor.Service as a Windows usermode service.

.DESCRIPTION
    Publishes the service binary, grants "Log on as a service" to the chosen
    account, registers the service with SCM, configures automatic restart on
    failure, configures the gRPC listen mode, and starts it.

    With no parameters, installs as the current user listening on localhost
    only and prompts for credentials via a secure dialog.

    SECURITY NOTE: The -Credential parameter accepts a PSCredential object.
    Always supply credentials via Get-Credential or a SecureString source.
    Do NOT use ConvertTo-SecureString with -AsPlainText — that exposes the
    password in command history and process listings.

.PARAMETER InstallDir
    Directory where the published service binary will be placed.
    Default: C:\Program Files\TarkovMonitor\Service

.PARAMETER ServiceAccount
    Username to run the service as (e.g. ".\mikey" or "DOMAIN\user").
    Defaults to the current user. Pass "LocalSystem" to run as LocalSystem
    (no credentials needed, but AppData auto-detection won't work — you'll
    need to set CustomLogsPath in appsettings.json or via UpdateConfig RPC).

.PARAMETER Credential
    PSCredential for the service account. Accepts pipeline input so you can
    pipe Get-Credential directly. If omitted and a non-LocalSystem account
    is used, a secure credential dialog is shown.

    # Pipe credentials:
    Get-Credential .\mikey | .\Install-TarkovMonitorService.ps1

    # Or pass explicitly:
    .\Install-TarkovMonitorService.ps1 -Credential (Get-Credential .\mikey)

.PARAMETER ListenMode
    How the gRPC server binds its socket:
      Localhost  — 127.0.0.1 and ::1 only. No firewall rule. (default)
      AnyIP      — All interfaces (0.0.0.0 / ::). Dual-stack. Firewall rule added.
      SpecificIP — The address given by -ListenAddress. Firewall rule added.

.PARAMETER ListenAddress
    IP address (IPv4 or IPv6) to bind when -ListenMode is SpecificIP.
    Required when ListenMode is SpecificIP; ignored otherwise.

.PARAMETER GrpcPort
    Port the gRPC server listens on. Default: 50051

.PARAMETER FirewallRemoteAddress
    Scope of the inbound firewall rule added for AnyIP and SpecificIP modes.
    Accepts anything New-NetFirewallRule -RemoteAddress accepts:
      LocalSubnet            — only LAN neighbors (default)
      Any                    — unrestricted
      192.168.1.0/24         — specific subnet
      192.168.1.10           — specific host
      192.168.1.10,10.0.0.0/8  — comma-separated list
    Ignored when ListenMode is Localhost.

.EXAMPLE
    # Current user, localhost only — prompts for password
    .\Install-TarkovMonitorService.ps1

.EXAMPLE
    # Pipe credentials (no separate -ServiceAccount needed — inferred from credential)
    Get-Credential .\mikey | .\Install-TarkovMonitorService.ps1

.EXAMPLE
    # LAN-accessible, specific subnet restriction
    .\Install-TarkovMonitorService.ps1 -ListenMode AnyIP -FirewallRemoteAddress "192.168.1.0/24"

.EXAMPLE
    # Bind to a specific NIC
    .\Install-TarkovMonitorService.ps1 -ListenMode SpecificIP -ListenAddress 192.168.1.10

.EXAMPLE
    # Fully non-interactive: LocalSystem, localhost only
    .\Install-TarkovMonitorService.ps1 -ServiceAccount LocalSystem

.EXAMPLE
    # Fully scripted with pre-supplied credential
    $cred = Get-Credential -UserName ".\mikey"
    .\Install-TarkovMonitorService.ps1 -Credential $cred -ListenMode AnyIP
#>
[CmdletBinding()]
param(
    [string] $InstallDir            = "C:\Program Files\TarkovMonitor\Service",
    [string] $ServiceAccount        = ".\$env:USERNAME",

    [Parameter(ValueFromPipeline = $true)]
    [PSCredential] $Credential      = $null,

    [ValidateSet("Localhost", "AnyIP", "SpecificIP")]
    [string] $ListenMode            = "Localhost",

    [string] $ListenAddress         = "",
    [int]    $GrpcPort              = 50051,
    [string] $FirewallRemoteAddress = "LocalSubnet"
)

begin {
    Set-StrictMode -Version Latest
    $ErrorActionPreference = "Stop"

    $ServiceName = "TarkovMonitorService"
    $DisplayName = "TarkovMonitor Service"
    $Description = "Monitors Escape from Tarkov log files and broadcasts game events over gRPC."
    $BinaryName  = "TarkovMonitor.Service.exe"
    $BinaryPath  = Join-Path $InstallDir $BinaryName
    $ProjectPath = Join-Path (Split-Path $PSScriptRoot -Parent) "TarkovMonitor.Service"
    $AppSettings = Join-Path $InstallDir "appsettings.json"
    $FwRuleName  = "TarkovMonitor gRPC (port $GrpcPort)"

    # Validate parameters that don't depend on the credential
    if ($ListenMode -eq "SpecificIP" -and [string]::IsNullOrWhiteSpace($ListenAddress)) {
        throw "-ListenAddress is required when -ListenMode is SpecificIP."
    }

    if (-not [string]::IsNullOrWhiteSpace($ListenAddress)) {
        $parsedAddress = $null
        if (-not [System.Net.IPAddress]::TryParse($ListenAddress, [ref] $parsedAddress)) {
            throw "'$ListenAddress' is not a valid IPv4 or IPv6 address."
        }
    }

    # Rollback stack — push undo actions as steps succeed; pop on failure
    $rollback = [System.Collections.Generic.Stack[scriptblock]]::new()

    # -----------------------------------------------------------------------
    function Grant-ServiceLogonRight {
        param([string] $Account)

        Write-Host "  Granting 'Log on as a service' to $Account ..."

        $tmpInf = [IO.Path]::GetTempFileName() + ".inf"
        $tmpDb  = [IO.Path]::GetTempFileName() + ".sdb"
        $tmpLog = [IO.Path]::GetTempFileName() + ".log"

        try {
            secedit /export /cfg $tmpInf /quiet

            $content = Get-Content $tmpInf -Raw

            if ($content -match "SeServiceLogonRight\s*=\s*(.*)") {
                $existing = $Matches[1].Trim()
                if ($existing -notlike "*$Account*") {
                    $content = $content -replace "SeServiceLogonRight\s*=\s*.*",
                        "SeServiceLogonRight = $existing,$Account"
                } else {
                    Write-Host "  Account already has SeServiceLogonRight — skipping."
                    return
                }
            } else {
                $content = $content -replace "(\[Privilege Rights\])",
                    "`$1`nSeServiceLogonRight = $Account"
            }

            Set-Content -Path $tmpInf -Value $content -Encoding Unicode
            secedit /configure /db $tmpDb /cfg $tmpInf /log $tmpLog /quiet
            Write-Host "  Done."
        } finally {
            Remove-Item -ErrorAction SilentlyContinue $tmpInf, $tmpDb, $tmpLog
        }
    }

    function Set-ServiceFailureActions {
        param([string] $Name)
        # Restart after 5s on first two failures; 60s thereafter.
        # Reset failure count after 1 day of clean operation.
        sc.exe failure $Name reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
        Write-Host "  Failure recovery configured."
    }

    function Set-AppSettings {
        param([string] $Path, [string] $Mode, [string] $Address, [int] $Port)
        $json = Get-Content $Path -Raw | ConvertFrom-Json
        $json.TarkovMonitor.GrpcListenMode    = $Mode
        $json.TarkovMonitor.GrpcListenAddress = $Address
        $json.TarkovMonitor.GrpcPort          = $Port
        $json | ConvertTo-Json -Depth 10 | Set-Content $Path -Encoding UTF8
    }

    function Set-GrpcFirewallRule {
        param([string] $Name, [int] $Port, [string] $RemoteAddress)
        Remove-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue
        New-NetFirewallRule `
            -DisplayName   $Name `
            -Direction     Inbound `
            -Protocol      TCP `
            -LocalPort     $Port `
            -RemoteAddress $RemoteAddress `
            -Action        Allow `
            -Profile       Private | Out-Null
        Write-Host "  Firewall rule '$Name' added (Private, port $Port, remote: $RemoteAddress)."
    }
}

process {
    # Pipeline binding for $Credential occurs here automatically.
}

end {
    # Infer service account from credential if -ServiceAccount was not explicitly supplied
    if ($null -ne $Credential -and -not $PSBoundParameters.ContainsKey('ServiceAccount')) {
        $ServiceAccount = $Credential.UserName
    }

    $useLocalSystem = ($ServiceAccount -eq "" -or $ServiceAccount -ieq "LocalSystem")

    # Prompt for credentials if running as a user account and none were supplied
    if (-not $useLocalSystem -and $null -eq $Credential) {
        $Credential = Get-Credential -UserName $ServiceAccount `
            -Message "Enter the password for service account '$ServiceAccount'"
    }

    # Banner
    Write-Host ""
    Write-Host "=== TarkovMonitor Service Installer ===" -ForegroundColor Cyan
    Write-Host "  Service account : $(if ($useLocalSystem) { 'LocalSystem' } else { $ServiceAccount })"
    Write-Host "  Listen mode     : $ListenMode$(if ($ListenAddress) { " ($ListenAddress)" })"
    Write-Host "  gRPC port       : $GrpcPort"
    if ($ListenMode -ne "Localhost") {
        Write-Host "  Firewall scope  : $FirewallRemoteAddress (Private profile)"
    }
    Write-Host ""

    try {
        # -------------------------------------------------------------------
        # Step 1: Stop and remove any existing installation
        # -------------------------------------------------------------------
        if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
            Write-Host "Removing existing service ..."
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            sc.exe delete $ServiceName | Out-Null
            Start-Sleep -Seconds 2
            Write-Host "  Done."
        }

        # Remove any leftover firewall rule from a previous install
        Remove-NetFirewallRule -DisplayName $FwRuleName -ErrorAction SilentlyContinue

        # -------------------------------------------------------------------
        # Step 2: Publish binary
        # -------------------------------------------------------------------
        Write-Host "Publishing to '$InstallDir' ..."

        if (-not (Test-Path $InstallDir)) {
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
        }

        dotnet publish $ProjectPath `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --output $InstallDir `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true | Out-Null

        if (-not (Test-Path $BinaryPath)) {
            throw "Publish failed — '$BinaryPath' not found after dotnet publish."
        }

        Write-Host "  Done."

        # -------------------------------------------------------------------
        # Step 3: Patch appsettings.json with listen config
        # -------------------------------------------------------------------
        Write-Host "Writing listen config to appsettings.json ..."
        Set-AppSettings -Path $AppSettings -Mode $ListenMode -Address $ListenAddress -Port $GrpcPort
        Write-Host "  Done."

        # -------------------------------------------------------------------
        # Step 4: Firewall rule (AnyIP and SpecificIP only)
        # -------------------------------------------------------------------
        if ($ListenMode -ne "Localhost") {
            Write-Host "Adding firewall rule ..."
            Set-GrpcFirewallRule -Name $FwRuleName -Port $GrpcPort -RemoteAddress $FirewallRemoteAddress
            $rollback.Push({ Remove-NetFirewallRule -DisplayName $FwRuleName -ErrorAction SilentlyContinue })
        }

        # -------------------------------------------------------------------
        # Step 5: Grant logon right and register service
        # -------------------------------------------------------------------
        if (-not $useLocalSystem) {
            Grant-ServiceLogonRight -Account $Credential.UserName
        }

        Write-Host "Registering service ..."
        if ($useLocalSystem) {
            New-Service `
                -Name           $ServiceName `
                -DisplayName    $DisplayName `
                -Description    $Description `
                -BinaryPathName $BinaryPath `
                -StartupType    Automatic | Out-Null
        } else {
            New-Service `
                -Name           $ServiceName `
                -DisplayName    $DisplayName `
                -Description    $Description `
                -BinaryPathName $BinaryPath `
                -StartupType    Automatic `
                -Credential     $Credential | Out-Null
        }
        $rollback.Push({ sc.exe delete $ServiceName | Out-Null })
        Write-Host "  Done."

        # -------------------------------------------------------------------
        # Step 6: Failure recovery
        # -------------------------------------------------------------------
        Write-Host "Configuring failure recovery ..."
        Set-ServiceFailureActions -Name $ServiceName

        # -------------------------------------------------------------------
        # Step 7: Start
        # -------------------------------------------------------------------
        Write-Host "Starting service ..."
        Start-Service -Name $ServiceName
        Write-Host "  Status: $((Get-Service -Name $ServiceName).Status)"

    } catch {
        Write-Warning "Install failed: $($_.Exception.Message)"
        if ($rollback.Count -gt 0) {
            Write-Host "Rolling back ..."
            while ($rollback.Count -gt 0) { & $rollback.Pop() }
            Write-Host "Rollback complete. Published files remain in '$InstallDir'."
        }
        throw
    }

    # Summary
    $listenSummary = switch ($ListenMode) {
        "AnyIP"      { "all interfaces, dual-stack (0.0.0.0 / ::), port $GrpcPort" }
        "SpecificIP" { "$ListenAddress, port $GrpcPort" }
        default      { "localhost only (127.0.0.1 / ::1), port $GrpcPort" }
    }

    Write-Host ""
    Write-Host "=== Installation complete ===" -ForegroundColor Green
    Write-Host "  Binary   : $BinaryPath"
    Write-Host "  Startup  : Automatic"
    Write-Host "  gRPC     : $listenSummary"
    if ($ListenMode -ne "Localhost") {
        Write-Host "  Firewall : $FwRuleName (Private profile, remote: $FirewallRemoteAddress)"
        Write-Host "  NOTE: To change the firewall scope, edit '$FwRuleName' in Windows Defender" -ForegroundColor Yellow
        Write-Host "        Firewall, or re-run this script with a different -FirewallRemoteAddress." -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Commands:"
    Write-Host "  Start    : Start-Service $ServiceName"
    Write-Host "  Stop     : Stop-Service $ServiceName"
    Write-Host "  Status   : Get-Service $ServiceName"
    Write-Host "  Logs     : Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
    Write-Host "  Uninstall: .\Scripts\Uninstall-TarkovMonitorService.ps1"
    Write-Host ""
}
