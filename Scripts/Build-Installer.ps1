<#
.SYNOPSIS
    Publishes all TarkovMonitor projects and builds the WiX MSI installer.

.DESCRIPTION
    1. Publishes TarkovMonitor.Service, TarkovMonitor (UI), StreamerDashboard,
       and the management scripts to publish\ subdirectories.
    2. Generates WiX harvest WXS files from the publish output.
    3. Invokes WiX to build the MSI.

    Prerequisites (no elevation required to build):
      dotnet SDK (net10.0)
      WiX 4 CLI: dotnet tool install --global wix --version "4.*"

.PARAMETER Version
    Version string embedded in the MSI (e.g. "1.2.0"). Auto-detected from
    Directory.Build.props (ProjectVersion trimmed to 3 parts) when omitted.

.PARAMETER OutputDir
    Directory for the finished MSI. Defaults to .\dist\

.PARAMETER SkipPublish
    Skip dotnet publish steps (use existing publish\ output). Useful during
    iterative WiX development when the binaries haven't changed.

.EXAMPLE
    .\Scripts\Build-Installer.ps1 -Version 1.2.0

.EXAMPLE
    .\Scripts\Build-Installer.ps1 -Version 1.2.0 -SkipPublish
#>
[CmdletBinding()]
param(
    [string] $Version    = "",
    [string] $OutputDir  = ".\dist",
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Script lives in Scripts\; root is one level up
$Root = Split-Path $PSScriptRoot -Parent

# Auto-detect version from Directory.Build.props when not explicitly provided
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $Root "Directory.Build.props")
    $raw     = $props.Project.PropertyGroup.ProjectVersion
    $parts   = $raw -split '\.'
    $Version = ($parts[0..[Math]::Min(2, $parts.Count - 1)] -join '.')
}
$PublishRoot      = Join-Path $Root "publish"
$InstallerDir     = Join-Path $Root "TarkovMonitor.Installer"
$InstallerProject = Join-Path $InstallerDir "TarkovMonitor.Installer.wixproj"
$MsiPath          = Join-Path $OutputDir "TarkovMonitor-$Version.msi"

function Step([string] $msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
#  Harvest helper — generates a WXS fragment from a publish output directory.
#
#  HarvestDirectory items in the wixproj are silently broken in WiX 4.0.6
#  (they produce no output). This function does the same job in PowerShell:
#  walks the source directory, emits <Component>/<File> pairs, and groups them
#  in a <ComponentGroup> that the Feature tree references.
# ---------------------------------------------------------------------------

function Get-FileHash8([string] $s) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($s.ToLowerInvariant())
    $hash  = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return [System.BitConverter]::ToString($hash).Replace('-', '').Substring(0, 8)
}

function Build-HarvestDirXml {
    param(
        [string]   $Dir,
        [string]   $RelPath,
        [string]   $Pad,
        [string]   $IdPrefix,
        [string]   $InstallerDir,
        [string[]] $ExcludeFiles,
        [System.Collections.Generic.List[string]] $ComponentIds
    )

    Get-ChildItem $Dir -File | Where-Object { $_.Name -notmatch '^\.' -and $_.Name -notin $ExcludeFiles } | Sort-Object Name | ForEach-Object {
        $fileRel = if ($RelPath) { "$RelPath\$($_.Name)" } else { $_.Name }
        $compId  = "c_${IdPrefix}_$(Get-FileHash8 $fileRel)"
        $ComponentIds.Add($compId) | Out-Null
        $srcPath = [System.IO.Path]::GetRelativePath($InstallerDir, $_.FullName)
        "${Pad}<Component Id=`"$compId`" Guid=`"*`">`n${Pad}  <File Source=`"$srcPath`" />`n${Pad}</Component>"
    }

    Get-ChildItem $Dir -Directory | Sort-Object Name | ForEach-Object {
        $subRel = if ($RelPath) { "$RelPath\$($_.Name)" } else { $_.Name }
        $dirId  = "d_${IdPrefix}_$(Get-FileHash8 $subRel)"
        "${Pad}<Directory Id=`"$dirId`" Name=`"$($_.Name)`">"
        Build-HarvestDirXml -Dir $_.FullName -RelPath $subRel -Pad "${Pad}  " `
            -IdPrefix $IdPrefix -InstallerDir $InstallerDir -ExcludeFiles $ExcludeFiles -ComponentIds $ComponentIds
        "${Pad}</Directory>"
    }
}

function New-WixHarvest {
    param(
        [string]   $SourceDir,
        [string]   $DirectoryRefId,
        [string]   $ComponentGroupId,
        [string]   $IdPrefix,
        [string[]] $ExcludeFiles = @()
    )

    if (-not (Test-Path $SourceDir)) {
        throw "Harvest source not found: $SourceDir"
    }

    $componentIds = [System.Collections.Generic.List[string]]::new()

    $innerLines = Build-HarvestDirXml `
        -Dir $SourceDir -RelPath '' -Pad '      ' `
        -IdPrefix $IdPrefix -InstallerDir $InstallerDir -ExcludeFiles $ExcludeFiles -ComponentIds $componentIds

    $cgLines = @("    <ComponentGroup Id=`"$ComponentGroupId`">")
    foreach ($id in $componentIds) { $cgLines += "      <ComponentRef Id=`"$id`" />" }
    $cgLines += "    </ComponentGroup>"

    $body = ($innerLines -join "`n")
    $cg   = ($cgLines   -join "`n")

    return @"
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <DirectoryRef Id="$DirectoryRefId">
$body
    </DirectoryRef>
$cg
  </Fragment>
</Wix>
"@
}

# ---------------------------------------------------------------------------
Step "Validate prerequisites"
# ---------------------------------------------------------------------------
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Write-Error "WiX CLI not found. Install with: dotnet tool install --global wix --version '4.*'"
    exit 1
}

$wixVersion = (wix --version 2>&1) -replace '\+.*', ''
Write-Host "  WiX version : $wixVersion"
Write-Host "  MSI version : $Version"
Write-Host "  Output      : $MsiPath"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# ---------------------------------------------------------------------------
if (-not $SkipPublish) {
    Step "Publish TarkovMonitor.Service"
    # ---------------------------------------------------------------------------
    $serviceOut = Join-Path $PublishRoot "Service"
    dotnet publish "$Root\TarkovMonitor.Service" `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $serviceOut `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw "Publish TarkovMonitor.Service failed." }

    # ---------------------------------------------------------------------------
    Step "Publish TarkovMonitor (UI)"
    # ---------------------------------------------------------------------------
    $uiOut = Join-Path $PublishRoot "UI"
    dotnet publish "$Root\TarkovMonitor" `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $uiOut `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw "Publish TarkovMonitor failed." }

    # ---------------------------------------------------------------------------
    Step "Publish TarkovMonitor.StreamerDashboard"
    # ---------------------------------------------------------------------------
    # WinUI3 (Windows App SDK) does not support PublishSingleFile without
    # EnableMsixTooling. Publish as a standard self-contained folder instead.
    $dashOut = Join-Path $PublishRoot "Dashboard"
    dotnet publish "$Root\TarkovMonitor.StreamerDashboard" `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $dashOut
    if ($LASTEXITCODE -ne 0) { throw "Publish TarkovMonitor.StreamerDashboard failed." }

    # ---------------------------------------------------------------------------
    Step "Stage management tools"
    # ---------------------------------------------------------------------------
    $toolsOut = Join-Path $PublishRoot "Tools"
    if (-not (Test-Path $toolsOut)) { New-Item -ItemType Directory -Path $toolsOut -Force | Out-Null }
    Copy-Item "$PSScriptRoot\Install-TarkovMonitorService.ps1"   -Destination $toolsOut -Force
    Copy-Item "$PSScriptRoot\Uninstall-TarkovMonitorService.ps1" -Destination $toolsOut -Force
    Write-Host "  Staged: Install-TarkovMonitorService.ps1"
    Write-Host "  Staged: Uninstall-TarkovMonitorService.ps1"
}

# ---------------------------------------------------------------------------
Step "Generate WiX harvest files"
# ---------------------------------------------------------------------------
# HarvestDirectory items in the wixproj are silently non-functional in WiX 4.0.6,
# so we generate the harvest WXS files here before the build.

$harvests = @(
    # TarkovMonitor.Service.exe is excluded: it's the KeyPath of the ServiceConfig component
    # in Service.wxs (required so InstallServices can derive the correct binary path).
    @{ Dir = 'Service';   RefId = 'ServiceDir';   GroupId = 'ServiceFileComponents';   Prefix = 'svc';   Exclude = @('TarkovMonitor.Service.exe') }
    @{ Dir = 'UI';        RefId = 'UIDir';         GroupId = 'UIFileComponents';         Prefix = 'ui';    Exclude = @() }
    @{ Dir = 'Dashboard'; RefId = 'DashboardDir';  GroupId = 'DashboardFileComponents';  Prefix = 'dash';  Exclude = @() }
    @{ Dir = 'Tools';     RefId = 'ToolsDir';      GroupId = 'ToolsFileComponents';      Prefix = 'tools'; Exclude = @() }
)

foreach ($h in $harvests) {
    $srcDir  = Join-Path $PublishRoot $h.Dir
    $outFile = Join-Path $InstallerDir "$($h.Dir)_harvest.wxs"

    Write-Host "  Harvesting $($h.Dir) ..."
    $fileCount = (Get-ChildItem $srcDir -Recurse -File -ErrorAction SilentlyContinue |
                  Where-Object { $_.Name -notmatch '^\.' -and $_.Name -notin $h.Exclude }).Count
    Write-Host "    $fileCount file(s) found$(if ($h.Exclude.Count) { " ($($h.Exclude.Count) excluded from harvest)" })"

    $wxs = New-WixHarvest `
        -SourceDir        $srcDir `
        -DirectoryRefId   $h.RefId `
        -ComponentGroupId $h.GroupId `
        -IdPrefix         $h.Prefix `
        -ExcludeFiles     $h.Exclude

    [System.IO.File]::WriteAllText($outFile, $wxs, [System.Text.Encoding]::UTF8)
    Write-Host "    Written: $outFile"
}

# ---------------------------------------------------------------------------
Step "Build MSI (WiX)"
# ---------------------------------------------------------------------------
$installerObj = Join-Path $InstallerDir "obj"
if (Test-Path $installerObj) { Remove-Item $installerObj -Recurse -Force }

Write-Host "  Building $InstallerProject ..."

dotnet build $InstallerProject `
    --configuration Release `
    -p:ProductVersion=$Version `
    -p:OutputPath="$((Resolve-Path $OutputDir).Path)\\" `
    -p:SuppressIces=true

if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

# WiX SDK puts the MSI in the output path; rename to match our convention
$builtMsi = Get-ChildItem $OutputDir -Filter "*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($builtMsi -and $builtMsi.FullName -ne $MsiPath) {
    Move-Item $builtMsi.FullName -Destination $MsiPath -Force
}

if (-not (Test-Path $MsiPath)) {
    throw "MSI not found at '$MsiPath' after build."
}

# ---------------------------------------------------------------------------
Step "Compute SHA256 for winget manifest"
# ---------------------------------------------------------------------------
$hash = (Get-FileHash $MsiPath -Algorithm SHA256).Hash
Write-Host "  SHA256: $hash"
Write-Host ""
Write-Host "  Update winget-manifest\TarkovMonitor.installer.yaml:" -ForegroundColor Yellow
Write-Host "    InstallerSha256: $hash" -ForegroundColor Yellow

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "  MSI : $MsiPath"
Write-Host "  Size: $([math]::Round((Get-Item $MsiPath).Length / 1MB, 1)) MB"
Write-Host ""
