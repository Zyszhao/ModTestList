#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the headless TestList verification harness.

.DESCRIPTION
    Compiles the mod's pure-logic sources (Source/Core/TestListModels.cs,
    Source/Core/TestListLog.cs, Source/Parsing/**, Source/Feedback/**) together with
    Verify/Program.cs and Verify/Suites.cs, with TESTLIST_NO_VERSE defined, into a
    console application - then runs it and reports the exit code.

    Two deliberate design choices:

    1. Reference assemblies are the STANDARD .NET Framework 4.8 targeting pack, which is
       the API surface the game's own CLR provides. RimWorld ships Mono's mscorlib
       (FileVersion 4.6.57.0) purely for compile-time reference; it exposes APIs the
       desktop runtime does not have - e.g. String.Split(char, StringSplitOptions) - so
       compiling against RimWorld's folder lets runtime-crashing calls through unnoticed.
       Using the real net48 references turns that class of bug into a COMPILE error here.

    2. csc.exe is invoked directly instead of going through the .NET SDK. The SDK insists
       on restoring the Microsoft.NETFramework.ReferenceAssemblies NuGet package for a
       net48 target, which fails on an offline machine. csc has no package resolution at
       all, so this script is fully offline and deterministic.

.EXAMPLE
    pwsh -NoProfile -File Verify\verify.ps1
#>
[CmdletBinding()]
param(
    [string]$Net48RefDir,
    [string]$CscPath,
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent $here

# ---------------------------------------------------------------------------
# Locate the .NET Framework 4.8 reference assemblies
# ---------------------------------------------------------------------------
function Find-Net48RefDir {
    param([string]$Explicit)
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($Explicit) { $candidates.Add($Explicit) }
    if ($env:NET48_REF_DIR) { $candidates.Add($env:NET48_REF_DIR) }
    $candidates.Add('C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8')
    $candidates.Add('C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8')
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'mscorlib.dll'))) { return (Resolve-Path $c).Path }
    }
    throw "Could not locate the .NET Framework 4.8 reference assemblies. Pass -Net48RefDir <path> or set `$env:NET48_REF_DIR. Install the .NET Framework 4.8 Developer Pack / targeting pack."
}

# ---------------------------------------------------------------------------
# Locate a MODERN (Roslyn) C# compiler.
# The legacy csc.exe in Microsoft.NET\Framework64\v4.0.30319 only understands C# 5,
# which is far too old for this code. Prefer:
#   1. a Roslyn csc.exe shipped with Visual Studio / MSBuild
#   2. the Roslyn csc.dll shipped with the .NET SDK, invoked via `dotnet exec`
# ---------------------------------------------------------------------------
function Find-Compiler {
    param([string]$Explicit)
    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "Compiler not found at $Explicit" }
        if ($Explicit.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
            return @{ Kind = 'dll'; Path = (Resolve-Path $Explicit).Path }
        }
        return @{ Kind = 'exe'; Path = (Resolve-Path $Explicit).Path }
    }
    if ($env:CSC_PATH) { return (Find-Compiler -Explicit $env:CSC_PATH) }

    foreach ($vsRoot in @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
        if (-not $vsRoot -or -not (Test-Path $vsRoot)) { continue }
        $hit = Get-ChildItem $vsRoot -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\Roslyn\\csc\.exe$' } |
            Select-Object -First 1
        if ($hit) { return @{ Kind = 'exe'; Path = $hit.FullName } }
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $hit = Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($hit) { return @{ Kind = 'dll'; Path = $hit } }
    }

    throw "Could not locate a Roslyn C# compiler. Install Visual Studio or the .NET SDK, or pass -CscPath <path to csc.exe or csc.dll>."
}

$refDir = Find-Net48RefDir -Explicit $Net48RefDir
$compiler = Find-Compiler -Explicit $CscPath

# ---------------------------------------------------------------------------
# Assemble the source list. These are exactly the files the mod itself compiles,
# minus the Verse/Unity-dependent ones, plus the harness driver.
# ---------------------------------------------------------------------------
$sources = New-Object System.Collections.Generic.List[string]
$sources.Add((Join-Path $here 'Program.cs'))
$sources.Add((Join-Path $here 'Suites.cs'))
$sources.Add((Join-Path $root 'Source\Core\TestListModels.cs'))
$sources.Add((Join-Path $root 'Source\Core\TestListLog.cs'))
foreach ($dir in @('Source\Parsing', 'Source\Feedback')) {
    Get-ChildItem (Join-Path $root $dir) -Filter *.cs -Recurse -File |
        Sort-Object FullName | ForEach-Object { $sources.Add($_.FullName) }
}

foreach ($s in $sources) {
    if (-not (Test-Path $s)) { throw "Required source file is missing: $s" }
}

$outDir = Join-Path $here 'out'
$dataDir = Join-Path $here 'tmp\data'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$exe = Join-Path $outDir 'TestList.Verify.exe'

# ---------------------------------------------------------------------------
# Compile
# ---------------------------------------------------------------------------
$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll')
$cscArgs = New-Object System.Collections.Generic.List[string]
$cscArgs.Add('/nologo')
$cscArgs.Add('/target:exe')
$cscArgs.Add('/platform:anycpu')
$cscArgs.Add('/langversion:latest')
$cscArgs.Add('/define:TESTLIST_NO_VERSE')
$cscArgs.Add('/nostdlib+')
$cscArgs.Add('/noconfig')
$cscArgs.Add('/warn:4')
$cscArgs.Add('/out:' + $exe)
foreach ($r in $refs) {
    $p = Join-Path $refDir $r
    if (-not (Test-Path $p)) { throw "Reference assembly missing: $p" }
    $cscArgs.Add('/reference:' + $p)
}
foreach ($s in $sources) { $cscArgs.Add($s) }

Write-Host "net48 refs : $refDir"
Write-Host "compiler   : $($compiler.Kind) $($compiler.Path)"
Write-Host "sources    : $($sources.Count) file(s)"
Write-Host ""

if ($compiler.Kind -eq 'dll') {
    & dotnet exec $compiler.Path @cscArgs
} else {
    & $compiler.Path @cscArgs
}
if ($LASTEXITCODE -ne 0) { throw "Verification harness failed to compile (compiler exit code $LASTEXITCODE)." }
if (-not (Test-Path $exe)) { throw "Harness executable not produced at $exe." }

if (-not $KeepData -and (Test-Path $dataDir)) { Remove-Item $dataDir -Recurse -Force }

# ---------------------------------------------------------------------------
# Run. NOTE: the harness itself cleans and recreates its data directory at start-up,
# so this script must never touch $dataDir after launching it - deleting the fixtures of
# the earlier suites while the process is still running silently breaks the later ones.
# ---------------------------------------------------------------------------
Write-Host ""
& $exe --data $dataDir
$code = $LASTEXITCODE

Write-Host ""
if ($code -eq 0) {
    Write-Host "VERIFY OK (exit code 0)"
} else {
    Write-Host "VERIFY FAILED (exit code $code)"
}
exit $code
