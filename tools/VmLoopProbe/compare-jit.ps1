#Requires -Version 7
<#
.SYNOPSIS
Easy diff of JIT dumps between two vmloopopt snapshots.

.DESCRIPTION
Compares jit/<case>.<config>.jit.txt between a -From and -To snapshot,
saves a unified diff into the -To snapshot, prints a code-size table
per compilation listing, and shows the diff stat.

Snapshot names resolve under artifacts/vmloopopt/snapshots/. Defaults:
-To = newest snapshot, -From = newest snapshot ending in 'baseline'.

.EXAMPLE
pwsh tools/VmLoopProbe/compare-jit.ps1 -Case smi-sum-loop

.EXAMPLE
pwsh tools/VmLoopProbe/compare-jit.ps1 -Case smi-sum-loop -Config pgo-on `
    -From 20260823-015059-0000-baseline -To 20260824-101000-0002-split
#>
[CmdletBinding()]
param(
    [string]$Case = "smi-sum-loop",
    [ValidateSet("pgo-on", "pgo-off", "tiered-off")]
    [string]$Config = "pgo-off",
    [string]$From,
    [string]$To,
    [int]$ContextLines = 3
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel).Trim()
$snapshotsRoot = Join-Path $repoRoot "artifacts/vmloopopt/snapshots"
if (-not (Test-Path $snapshotsRoot)) {
    throw "No snapshots found under $snapshotsRoot."
}

function Resolve-Snapshot([string]$Name, [string]$Fallback) {
    if ($Name) {
        $path = if (Test-Path $Name) { $Name } else { Join-Path $snapshotsRoot $Name }
        if (-not (Test-Path $path)) {
            throw "Snapshot '$Name' not found."
        }
        return (Resolve-Path $path).Path
    }
    return $Fallback
}

$allSnapshots = Get-ChildItem $snapshotsRoot -Directory | Sort-Object LastWriteTime
$toDefault = ($allSnapshots | Select-Object -Last 1).FullName
$fromDefault = (
    $allSnapshots |
    Where-Object Name -like "*baseline" |
    Select-Object -Last 1
).FullName
if (-not $fromDefault) {
    $fromDefault = ($allSnapshots | Select-Object -First 1).FullName
}

$fromDir = Resolve-Snapshot $From $fromDefault
$toDir = Resolve-Snapshot $To $toDefault

if ((Resolve-Path $fromDir).Path -eq (Resolve-Path $toDir).Path) {
    throw "From and To snapshots are the same."
}

$fromFile = Join-Path $fromDir "jit/$Case.$Config.jit.txt"
$toFile = Join-Path $toDir "jit/$Case.$Config.jit.txt"
foreach ($f in @($fromFile, $toFile)) {
    if (-not (Test-Path $f)) {
        throw "Missing dump: $f"
    }
}

Write-Host "From: $(Split-Path -Leaf $fromDir)" -ForegroundColor Cyan
Write-Host "To:   $(Split-Path -Leaf $toDir)" -ForegroundColor Cyan
Write-Host "File: jit/$Case.$Config.jit.txt" -ForegroundColor Cyan
Write-Host ""

function Get-CodeSizes([string]$Path) {
    Get-Content $Path |
        Where-Object { $_ -match '^; Assembly listing for method|^; Total bytes of code' } |
        ForEach-Object { $_.Trim() }
}

function Get-JitListingSummary([string]$Path) {
    $lines = @(Get-Content $Path)
    $headers = @(
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^; Assembly listing for method') {
                [pscustomobject]@{ Index = $i; Text = $lines[$i].Trim() }
            }
        }
    )

    $summaries = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $headers.Count; $i++) {
        $start = $headers[$i].Index
        $end = if ($i + 1 -lt $headers.Count) { $headers[$i + 1].Index } else { $lines.Count }
        $listing = $lines[$start..($end - 1)]
        $stackLine = $listing |
            Where-Object { $_ -match '^\s*sub\s+rsp,\s+(0x[0-9A-Fa-f]+|\d+)' } |
            Select-Object -First 1
        $stackBytes = $null
        if ($stackLine) {
            $stackValue = [regex]::Match(
                $stackLine,
                '^\s*sub\s+rsp,\s+(?<value>0x[0-9A-Fa-f]+|\d+)'
            ).Groups['value'].Value
            $stackBytes = if ($stackValue.StartsWith('0x')) {
                [Convert]::ToInt32($stackValue.Substring(2), 16)
            } else {
                [int]$stackValue
            }
        }

        $codeLine = $listing | Where-Object { $_ -match '^; Total bytes of code' } | Select-Object -First 1
        $codeBytes = if ($codeLine) { [int](($codeLine -replace '\D', '')) } else { $null }
        $summaries.Add(
            [pscustomobject]@{
                Name = ($headers[$i].Text -replace '^; Assembly listing for method ', '')
                CodeBytes = $codeBytes
                StackBytes = $stackBytes
                Calls = @($listing | Where-Object { $_ -match '^\s*call\s+' }).Count
            }
        )
    }
    return $summaries
}

function Get-RunLocalSummary([string]$Path) {
    $line = Get-Content $Path |
        Where-Object { $_ -match '^\[run\]' } |
        Select-Object -First 1
    if (-not $line) { return $null }

    $match = [regex]::Match(
        $line,
        'il_bytes=(?<il>\d+)\s+max_stack=(?<stack>\d+)\s+init_locals=(?<init>\w+)\s+locals=(?<locals>\d+)'
    )
    if (-not $match.Success) { return $null }

    return [pscustomobject]@{
        IlBytes = [int]$match.Groups['il'].Value
        MaxStack = [int]$match.Groups['stack'].Value
        InitLocals = $match.Groups['init'].Value
        Locals = [int]$match.Groups['locals'].Value
    }
}

$sizesFrom = Get-CodeSizes $fromFile
$sizesTo = Get-CodeSizes $toFile
$listingCount = [Math]::Max(@($sizesFrom | Where-Object { $_ -like '; Assembly listing*' }).Count,
    @($sizesTo | Where-Object { $_ -like '; Assembly listing*' }).Count)

Write-Host "=== Code size per listing ===" -ForegroundColor Green
for ($i = 0; $i -lt $listingCount; $i++) {
    $headerF = @($sizesFrom | Where-Object { $_ -like '; Assembly listing*' })[$i]
    $bytesF = (@($sizesFrom | Where-Object { $_ -like '; Total bytes*' })[$i] -replace '\D', '')
    $bytesT = (@($sizesTo | Where-Object { $_ -like '; Total bytes*' })[$i] -replace '\D', '')
    $name = if ($headerF) { ($headerF -replace '^; Assembly listing for method ', '') } else { "<no listing #$i>" }
    $delta = ""
    if ($bytesF -and $bytesT) {
        $d = [int]$bytesT - [int]$bytesF
        $sign = if ($d -gt 0) { "+" } elseif ($d -eq 0) { "=" } else { "" }
        $delta = "  [$sign$d bytes]"
    }
    Write-Host ("  {0,-80} from={1,-6} to={2,-6}{3}" -f $name, $bytesF, $bytesT, $delta)
}
Write-Host ""

$asmFrom = Get-JitListingSummary $fromFile
$asmTo = Get-JitListingSummary $toFile
$asmNames = @(
    @($asmFrom | Where-Object { $_.Name -match 'Tier1' } | ForEach-Object Name) +
    @($asmTo | Where-Object { $_.Name -match 'Tier1' } | ForEach-Object Name) |
    Sort-Object -Unique
)
if ($asmNames.Count -gt 0) {
    Write-Host "=== Tier1/OSR JIT summary ===" -ForegroundColor Green
    foreach ($name in $asmNames) {
        $a = $asmFrom | Where-Object Name -eq $name | Select-Object -First 1
        $b = $asmTo | Where-Object Name -eq $name | Select-Object -First 1
        if (-not $a -or -not $b) { continue }
        $label = if ($name -match '\((?<tier>Tier1(?:-OSR)?)\)$') {
            $Matches['tier']
        } else {
            $name
        }
        Write-Host (
            "  {0,-9} code {1,6}->{2,6} [{3:+#;-#;=0}]  stack {4,5}->{5,5} [{6:+#;-#;=0}]  calls {7,3}->{8,3} [{9:+#;-#;=0}]" -f
                $label,
                $a.CodeBytes,
                $b.CodeBytes,
                ($b.CodeBytes - $a.CodeBytes),
                $a.StackBytes,
                $b.StackBytes,
                ($b.StackBytes - $a.StackBytes),
                $a.Calls,
                $b.Calls,
                ($b.Calls - $a.Calls)
        )
    }
    Write-Host ""
}

$fromLocalFile = Join-Path $fromDir "run-locals.txt"
$toLocalFile = Join-Path $toDir "run-locals.txt"
if ((Test-Path $fromLocalFile) -and (Test-Path $toLocalFile)) {
    $localFrom = Get-RunLocalSummary $fromLocalFile
    $localTo = Get-RunLocalSummary $toLocalFile
    if ($localFrom -and $localTo) {
        Write-Host "=== Run IL/local summary ===" -ForegroundColor Green
        Write-Host (
            "  IL bytes       from={0,-6} to={1,-6}  [{2:+#;-#;=0} bytes]" -f
                $localFrom.IlBytes,
                $localTo.IlBytes,
                ($localTo.IlBytes - $localFrom.IlBytes)
        )
        Write-Host (
            "  max stack      from={0,-6} to={1,-6}" -f
                $localFrom.MaxStack,
                $localTo.MaxStack
        )
        Write-Host (
            "  IL locals      from={0,-6} to={1,-6}  [{2:+#;-#;=0} locals]" -f
                $localFrom.Locals,
                $localTo.Locals,
                ($localTo.Locals - $localFrom.Locals)
        )
        Write-Host "  Per-type counts: see run-locals.txt in each snapshot."
        Write-Host ""
    }
}

$diffFile = Join-Path $toDir "jit/$Case.$Config.vs-$(Split-Path -Leaf $fromDir).diff.txt"
# 2>$null: untracked artifact files trigger noisy CRLF normalization warnings.
git -c core.autocrlf=false diff --no-index --unified=$ContextLines --no-color -- $fromFile $toFile > $diffFile 2>$null
$exit = $LASTEXITCODE
if ($exit -ne 0 -and $exit -ne 1) {
    throw "git diff failed with exit code $exit."
}

$diffLines = @(Get-Content $diffFile)
if ($diffLines.Count -eq 0) {
    Remove-Item $diffFile
    Write-Host "Dumps are identical." -ForegroundColor Green
    return
}

$added = @($diffLines | Where-Object { $_.StartsWith('+') -and $_ -notmatch '^\+\+\+' }).Count
$removed = @($diffLines | Where-Object { $_.StartsWith('-') -and $_ -notmatch '^---' }).Count

Write-Host "Diff stat: +$added / -$removed lines" -ForegroundColor Green
Write-Host "Saved: $(Split-Path -Leaf $diffFile)" -ForegroundColor Green
Write-Host ""

$diffLines | Select-Object -First ([Math]::Min(60, $diffLines.Count)) | ForEach-Object { $_ }
if ($diffLines.Count -gt 60) {
    Write-Host "... $($diffLines.Count - 60) more lines in $diffFile" -ForegroundColor DarkGray
}
