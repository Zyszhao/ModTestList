#Requires -Version 5.1
<#
.SYNOPSIS
    End-to-end acceptance run against a REAL checklist folder on disk
    (default: Verify\fixtures).

.DESCRIPTION
    Compiles Verify/E2E.cs together with the mod's pure-logic sources and runs the full
    real-world loop against a live checklist folder:

        scan -> assert discovery and entry count -> mark an entry + write feedback
             -> rescan (the generated feedback file must NOT be re-ingested)
             -> read the marks back and assert the ids reconnected
             -> delete the generated feedback file so the folder is left untouched

    This is a read-mostly check: the only file it creates is the feedback file, and it
    removes that again on the way out.

.EXAMPLE
    pwsh -NoProfile -File Verify\e2e.ps1
    pwsh -NoProfile -File Verify\e2e.ps1 -Folder "E:\SteamLibrary\...\Rukaya Race_Test_AntiAir\TestList"
#>
[CmdletBinding()]
param(
    [string]$Folder,
    [int]$MinEntries = 1,
    [string]$TitleContains,
    [string]$Net48RefDir,
    [string]$CscPath
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent $here

if (-not $Folder) {
    # Default to the fixture checklist under Verify\fixtures. It is deliberately NOT inside
    # a /TestList folder: the game scans every enabled mod's /TestList, so a sample kept
    # there would appear in the panel mixed in with real checklists. The fixture is still
    # inside the workspace, so the verification process can write the feedback file there.
    # (A feedback file written to a mod OUTSIDE the workspace may be blocked when the
    #  harness runs under a restricted file sandbox - that is a sandbox property, not a
    #  mod property: RimWorld itself is not sandboxed and writes wherever it is told.)
    $Folder = Join-Path $root 'Verify\fixtures'
}

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
    throw "Could not locate the .NET Framework 4.8 reference assemblies. Pass -Net48RefDir <path> or set `$env:NET48_REF_DIR."
}

function Find-Compiler {
    param([string]$Explicit)
    if ($Explicit) {
        if (-not (Test-Path $Explicit)) { throw "Compiler not found at $Explicit" }
        if ($Explicit.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) { return @{ Kind = 'dll'; Path = (Resolve-Path $Explicit).Path } }
        return @{ Kind = 'exe'; Path = (Resolve-Path $Explicit).Path }
    }
    if ($env:CSC_PATH) { return (Find-Compiler -Explicit $env:CSC_PATH) }
    foreach ($vsRoot in @("$env:ProgramFiles\Microsoft Visual Studio", "${env:ProgramFiles(x86)}\Microsoft Visual Studio")) {
        if (-not $vsRoot -or -not (Test-Path $vsRoot)) { continue }
        $hit = Get-ChildItem $vsRoot -Recurse -Filter csc.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\Roslyn\\csc\.exe$' } | Select-Object -First 1
        if ($hit) { return @{ Kind = 'exe'; Path = $hit.FullName } }
    }
    $hit = Get-ChildItem "$env:ProgramFiles\dotnet\sdk" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($hit) { return @{ Kind = 'dll'; Path = $hit } }
    throw "Could not locate a Roslyn C# compiler. Install the .NET SDK or pass -CscPath."
}

$refDir = Find-Net48RefDir -Explicit $Net48RefDir
$compiler = Find-Compiler -Explicit $CscPath

if (-not (Test-Path $Folder)) { throw "Checklist folder not found: $Folder" }
$Folder = (Resolve-Path $Folder).Path

$sources = New-Object System.Collections.Generic.List[string]
$sources.Add((Join-Path $here 'E2E.cs'))
$sources.Add((Join-Path $root 'Source\Core\TestListModels.cs'))
$sources.Add((Join-Path $root 'Source\Core\TestListLog.cs'))
foreach ($dir in @('Source\Parsing', 'Source\Feedback')) {
    Get-ChildItem (Join-Path $root $dir) -Filter *.cs -Recurse -File |
        Sort-Object FullName | ForEach-Object { $sources.Add($_.FullName) }
}
foreach ($s in $sources) { if (-not (Test-Path $s)) { throw "Required source file is missing: $s" } }

$outDir = Join-Path $here 'out-e2e'
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$exe = Join-Path $outDir 'TestList.E2E.exe'

$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll')
$cscArgs = New-Object System.Collections.Generic.List[string]
foreach ($a in @('/nologo', '/target:exe', '/platform:anycpu', '/langversion:latest',
                 '/define:TESTLIST_NO_VERSE', '/nostdlib+', '/noconfig', '/warn:4', ('/out:' + $exe))) {
    $cscArgs.Add($a)
}
foreach ($r in $refs) {
    $p = Join-Path $refDir $r
    if (-not (Test-Path $p)) { throw "Reference assembly missing: $p" }
    $cscArgs.Add('/reference:' + $p)
}
foreach ($s in $sources) { $cscArgs.Add($s) }

Write-Host "net48 refs : $refDir"
Write-Host "compiler   : $($compiler.Kind) $($compiler.Path)"
Write-Host "folder     : $Folder"
Write-Host "sources    : $($sources.Count) file(s)"
Write-Host ""

if ($compiler.Kind -eq 'dll') { & dotnet exec $compiler.Path @cscArgs } else { & $compiler.Path @cscArgs }
if ($LASTEXITCODE -ne 0) { throw "E2E driver failed to compile (exit code $LASTEXITCODE)." }

$runArgs = @('--folder', $Folder, '--min-entries', $MinEntries)
if ($TitleContains) { $runArgs += @('--title-contains', $TitleContains) }

Write-Host ""
& $exe @runArgs
$code = $LASTEXITCODE

Write-Host ""
if ($code -eq 0) { Write-Host "E2E OK (exit code 0)" } else { Write-Host "E2E FAILED (exit code $code)" }
exit $code
