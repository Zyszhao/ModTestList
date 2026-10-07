#Requires -Version 5.1
<#
.SYNOPSIS
    分析 RimWorld TestList 反馈文件，给出清理建议。

.DESCRIPTION
    读取一个 TestList 文件夹（或单个 .feedback.md 文件）里的反馈文件，按
    pass / fail / untested 分组输出，并把带反馈文字的失败项正文列出来，
    最后给出清理建议计数。

    只读，不会修改任何文件。

.PARAMETER Path
    模组的 TestList 文件夹，或单个 .feedback.md 文件路径。

.PARAMETER OnlyFail
    只输出 fail 项。

.PARAMETER Format
    Text（默认）或 Markdown。

.EXAMPLE
    pwsh -NoProfile -File tools\analyze-feedback.ps1 -Path "E:\...\Mods\Rukaya Race_Test_AntiAir\TestList"
    pwsh -NoProfile -File tools\analyze-feedback.ps1 -Path ".\TestList" -Format Markdown > report.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [switch]$OnlyFail,

    [ValidateSet('Text', 'Markdown')]
    [string]$Format = 'Text'
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Locate the feedback files
# ---------------------------------------------------------------------------
$files = @()
if (Test-Path -Path $Path -PathType Container) {
    $all = @(Get-ChildItem -Path $Path -Recurse -File -Filter '*.feedback.md' -ErrorAction SilentlyContinue |
             Sort-Object FullName)
    # The mod writes BOTH a per-file <name>.feedback.md and a combined TestList.feedback.md
    # containing the same entries. Counting both would double every number, so prefer the
    # per-file ones and only fall back to the combined file when it is all we have.
    $perFile = @($all | Where-Object { $_.Name -ne 'TestList.feedback.md' })
    $files = if ($perFile.Count -gt 0) { $perFile } else { $all }
} elseif (Test-Path -Path $Path -PathType Leaf) {
    $files = @(Get-Item -Path $Path)
} else {
    throw "路径不存在: $Path"
}

if ($files.Count -eq 0) {
    Write-Host "没有找到任何 *.feedback.md：$Path"
    Write-Host "（还没在游戏里标记过任何测试项，或路径指错了。）"
    exit 0
}

# ---------------------------------------------------------------------------
# Parse
# ---------------------------------------------------------------------------
$entries = New-Object System.Collections.Generic.List[object]
$seenIds = New-Object System.Collections.Generic.HashSet[string]
$duplicateCount = 0

foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    $current = $null
    $inDoc = $false

    foreach ($line in $lines) {
        # First non-empty line must be the marker; otherwise this is not our file.
        if (-not $inDoc) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            if (-not $line.StartsWith('<!-- TestList feedback', [StringComparison]::Ordinal)) {
                Write-Warning "跳过（首行不是反馈标记）: $($file.Name)"
            }
            $inDoc = $true
            continue
        }

        $t = $line.Trim()

        # A new entry starts at an id marker.
        if ($t.StartsWith('<!-- testlist:id=', [StringComparison]::Ordinal)) {
            if ($current) { $entries.Add($current) }
            $id = $t.Substring('<!-- testlist:id='.Length).TrimEnd('-', '>', ' ')

            # An id can only legitimately appear once. When a per-file and a combined feedback
            # document are both present, the combined one repeats every entry; drop repeats so
            # the counts stay truthful.
            if (-not $seenIds.Add($id.Trim())) {
                $duplicateCount++
                $current = $null
                continue
            }

            $current = [pscustomobject]@{
                File     = $file.Name
                Id       = $id.Trim()
                Title    = ''
                Path     = ''
                Status   = 'untested'
                Updated  = ''
                Feedback = ''
            }
            continue
        }

        if (-not $current) { continue }

        if ($t.StartsWith('- Title:', [StringComparison]::Ordinal)) {
            $current.Title = $t.Substring(8).Trim()
        } elseif ($t.StartsWith('- Path:', [StringComparison]::Ordinal)) {
            $current.Path = $t.Substring(7).Trim()
        } elseif ($t.StartsWith('- Status:', [StringComparison]::Ordinal)) {
            $current.Status = $t.Substring(9).Trim().ToLowerInvariant()
        } elseif ($t.StartsWith('- Updated:', [StringComparison]::Ordinal)) {
            $current.Updated = $t.Substring(10).Trim()
        } elseif ($t.StartsWith('- Feedback:', [StringComparison]::Ordinal)) {
            $fb = $t.Substring(11).Trim()
            if ($fb -eq '-') { $fb = '' }
            # The writer escapes real newlines as the two characters \n.
            $current.Feedback = $fb.Replace('\n', "`n")
        }
    }
    if ($current) { $entries.Add($current) }
}

if ($entries.Count -eq 0) {
    Write-Host "反馈文件里没有解析到任何测试项。"
    exit 0
}

# ---------------------------------------------------------------------------
# Tally
# ---------------------------------------------------------------------------
$pass     = @($entries | Where-Object { $_.Status -eq 'pass' })
$fail     = @($entries | Where-Object { $_.Status -eq 'fail' })
$untested = @($entries | Where-Object { $_.Status -ne 'pass' -and $_.Status -ne 'fail' })
$failWithText = @($fail | Where-Object { $_.Feedback -ne '' })

$total = $entries.Count
$tested = $pass.Count + $fail.Count
$pct = if ($total -gt 0) { [math]::Round(100.0 * $tested / $total, 1) } else { 0 }

function Render {
    param([string]$Text)
    if ($Format -eq 'Markdown') { Write-Output $Text } else { Write-Host $Text }
}

Render ""
Render "TestList 反馈分析"
Render ("输入      : {0}" -f (Resolve-Path $Path).Path)
Render ("反馈文件  : {0}" -f $files.Count)
if ($duplicateCount -gt 0) {
    Render ("已去重    : {0} 条重复项（per-file 与合并反馈文件内容重叠）" -f $duplicateCount)
}
Render ("测试项    : {0}" -f $total)
Render ("进度      : {0}/{1} 已测试 ({2}%)" -f $tested, $total, $pct)
Render ("正常 {0}   异常 {1}   未测试 {2}" -f $pass.Count, $fail.Count, $untested.Count)
Render ""

if ($fail.Count -gt 0) {
    Render "===== 异常项（必须保留）====="
    foreach ($e in $fail) {
        Render ("- [{0}] {1}" -f $e.Status, $e.Title)
        if ($Format -eq 'Markdown') { Render ("  - `Path`: {0}" -f $e.Path) } else { Render ("    路径: {0}" -f $e.Path) }
        if ($e.Feedback -ne '') {
            $fbLines = $e.Feedback -split "`n"
            foreach ($l in $fbLines) {
                if ($Format -eq 'Markdown') { Render ("  - 反馈: {0}" -f $l) } else { Render ("    反馈: {0}" -f $l) }
            }
        }
        if ($e.Updated -ne '') { Render ("    更新: {0}" -f $e.Updated) }
    }
    Render ""
}

if (-not $OnlyFail) {
    if ($pass.Count -gt 0) {
        Render "===== 正常项（可清理）====="
        foreach ($e in $pass) {
            $suffix = if ($e.Feedback -ne '') { '  <- 有反馈，清理前先看内容' } else { '' }
            Render ("- {0}{1}" -f $e.Title, $suffix)
        }
        Render ""
    }
    if ($untested.Count -gt 0) {
        Render "===== 未测试项 ====="
        foreach ($e in $untested) { Render ("- {0}" -f $e.Title) }
        Render ""
    }
}

Render "===== 清理建议 ====="
Render ("- 删除（正常）        : {0} 项" -f $pass.Count)
Render ("- 保留（异常）        : {0} 项，其中 {1} 项带反馈正文" -f $fail.Count, $failWithText.Count)
Render ("- 待定（未测试）      : {0} 项 —— 功能仍在开发就保留，已废弃则删除" -f $untested.Count)
Render ""
Render "注意："
Render "  1. 删测试项前先确认游戏已退出（游戏运行时会持续写 .feedback.md）。"
Render "  2. 不要修改已测项的标题文字或文件名 —— id 会变，标记全部作废。"
Render "  3. 改完源清单后，在游戏里点一次「重新读取」。"
Render "  4. 建议把'容易再坏'的断言移入精简的 回归.md，而不是全部删掉。"

exit 0
