#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the TestList RimWorld mod.

.DESCRIPTION
    Locates the RimWorld 1.6 installation, locates a 0Harmony.dll (shipped by the
    Harmony workshop mod), and compiles Source\**\*.cs into
    1.6\Assemblies\TestList.dll using the .NET SDK.

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
        if ($c -and (Test-Path (Join-Path $c 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'))) {
            return (Resolve-Path $c).Path
        }
    }
    throw "Could not locate RimWorld. Pass -RimWorldDir <path-to-RimWorld> or set `$env:RIMWORLD_DIR."
}

function Find-HarmonyDll {
    param([string]$Explicit, [string]$RwDir)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Explicit) { $candidates.Add($Explicit) }
    if ($env:HARMONY_DLL_PATH) { $candidates.Add($env:HARMONY_DLL_PATH) }
    # Sibling mods in this installation bundle 0Harmony.dll.
    foreach ($sibling in @('Rukaya Race', 'Rukaya Race_Test_AntiAir')) {
        $candidates.Add((Join-Path $RwDir "Mods\$sibling\1.6\Assemblies\0Harmony.dll"))
    }
    # The Harmony workshop mod itself.
    foreach ($id in @('2009463077')) {
        foreach ($sub in @('Current', '1.6')) {
            $candidates.Add("E:\SteamLibrary\steamapps\workshop\content\294100\$id\$sub\Assemblies\0Harmony.dll")
        }
    }
    # RimWorld ships 0Harmony as part of the managed folder in some builds.
    $candidates.Add((Join-Path $RwDir 'RimWorldWin64_Data\Managed\0Harmony.dll'))
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path }
    }
    throw "Could not locate 0Harmony.dll. Pass -HarmonyDllPath <path> or set `$env:HARMONY_DLL_PATH."
}

$rwDir = Find-RimWorldDir -Explicit $RimWorldDir
$managedDir = Join-Path $rwDir 'RimWorldWin64_Data\Managed'
$harmony = Find-HarmonyDll -Explicit $HarmonyDllPath -RwDir $rwDir

Write-Host "RimWorld    : $rwDir"
Write-Host "Managed dir : $managedDir"
Write-Host "0Harmony    : $harmony"
Write-Host "Config      : $Configuration"

$dll = Join-Path $root '1.6\Assemblies\TestList.dll'
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
