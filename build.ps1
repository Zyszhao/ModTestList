#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the TestList RimWorld mod.

.DESCRIPTION
    Locates the RimWorld installation, locates a 0Harmony.dll (shipped by the
    Harmony workshop mod), and compiles Source\**\*.cs into
    <version>\Assemblies\TestList.dll using the .NET SDK (10.0+ for RimWorld 1.7).

    Every path can be overridden with a parameter. The script fails loudly with an
    actionable message when a prerequisite is missing; it never silently produces a
    stale DLL.

.EXAMPLE
    pwsh -File build.ps1
    pwsh -File build.ps1 -Configuration Release -RimWorldDir "D:\Steam\...\RimWorld"
#>
[CmdletBinding()]
param(
    [string]$RimWorldDir,
    [string]$HarmonyDllPath,
    [ValidateSet('1.6', '1.7')]
    [string]$RimWorldVersion,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Find-RimWorldDir {
    param([string]$Explicit)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Explicit) { $candidates.Add($Explicit) }
    if ($env:RIMWORLD_DIR) { $candidates.Add($env:RIMWORLD_DIR) }
    # The mod lives at <RimWorld>\Mods\TestList, so the install root is two levels up.
    $candidates.Add((Split-Path -Parent (Split-Path -Parent $root)))
    foreach ($c in $candidates) {
        if ($c -and ((Test-Path (Join-Path $c 'Assembly-CSharp.dll')) -or
                     (Test-Path (Join-Path $c 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll')))) {
            return (Resolve-Path $c).Path
        }
    }
    throw "Could not locate RimWorld. Pass -RimWorldDir <path-to-RimWorld> or set `$env:RIMWORLD_DIR."
}

function Find-HarmonyDll {
    param([string]$Explicit, [string]$RwDir, [string]$Version, [string]$ManagedDir)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Explicit) { $candidates.Add($Explicit) }
    if ($env:HARMONY_DLL_PATH) { $candidates.Add($env:HARMONY_DLL_PATH) }
    # Prefer the matching official Harmony workshop assembly.
    $steamApps = Split-Path -Parent (Split-Path -Parent $RwDir)
    $workshop = Join-Path $steamApps 'workshop\content\294100\2009463077'
    $candidates.Add((Join-Path $workshop "$Version\Assemblies\0Harmony.dll"))
    $candidates.Add((Join-Path $workshop 'Current\Assemblies\0Harmony.dll'))
    # Sibling mods in this installation may bundle 0Harmony.dll.
    foreach ($sibling in @('Rukaya Race', 'Rukaya Race_Test_AntiAir')) {
        $candidates.Add((Join-Path $RwDir "Mods\$sibling\$Version\Assemblies\0Harmony.dll"))
    }
    # RimWorld ships 0Harmony as part of the managed folder in some builds.
    $candidates.Add((Join-Path $ManagedDir '0Harmony.dll'))
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path }
    }
    throw "Could not locate 0Harmony.dll. Pass -HarmonyDllPath <path> or set `$env:HARMONY_DLL_PATH."
}

$rwDir = Find-RimWorldDir -Explicit $RimWorldDir
$versionFile = Join-Path $rwDir 'Version.txt'
if (-not (Test-Path $versionFile)) { throw "RimWorld Version.txt not found in $rwDir." }
$installedVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($installedVersion -notmatch '^(1\.[67])(?:\.|\s|$)') {
    throw "Unsupported RimWorld version: $installedVersion. Supported: 1.6, 1.7."
}
$detectedVersion = $Matches[1]
if ($RimWorldVersion -and $RimWorldVersion -ne $detectedVersion) {
    throw "Requested $RimWorldVersion but installation is $installedVersion. Use a matching -RimWorldDir."
}
$RimWorldVersion = $detectedVersion
$managedDir = if ($RimWorldVersion -eq '1.7') { $rwDir } else { Join-Path $rwDir 'RimWorldWin64_Data\Managed' }
if (-not (Test-Path (Join-Path $managedDir 'Assembly-CSharp.dll'))) {
    throw "Game assembly missing from $managedDir."
}
$harmony = Find-HarmonyDll -Explicit $HarmonyDllPath -RwDir $rwDir -Version $RimWorldVersion -ManagedDir $managedDir

Write-Host "RimWorld    : $rwDir"
Write-Host "Version     : $installedVersion"
Write-Host "Managed dir : $managedDir"
Write-Host "0Harmony    : $harmony"
Write-Host "Config      : $Configuration"

$dll = Join-Path $root "$RimWorldVersion\Assemblies\TestList.dll"
if (Test-Path $dll) {
    Write-Host "Removing previous build output to avoid stale artifacts."
    Remove-Item $dll -Force
}

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $dotnet) { throw "dotnet SDK not found on PATH. Install the .NET SDK (8.0+)." }

# The workload resolver tries to reach the network for workload manifests; we have
# no workloads and no PackageReferences, so keep restore fully offline.
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$buildArgs = @(
    'build', (Join-Path $root 'TestList.csproj'),
    '-c', $Configuration,
    "-p:RimWorldManagedDir=$managedDir",
    "-p:RimWorldVersion=$RimWorldVersion",
    "-p:HarmonyDllPath=$harmony",
    '-v:minimal'
)
if ($NoRestore) { $buildArgs += '--no-restore' }

& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

if (-not (Test-Path $dll)) { throw "Build reported success but $dll does not exist." }

$info = Get-Item $dll
Write-Host ""
Write-Host ("Build OK -> {0} ({1:N0} bytes, {2})" -f $info.FullName, $info.Length, $info.LastWriteTime)
