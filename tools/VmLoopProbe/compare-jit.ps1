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
