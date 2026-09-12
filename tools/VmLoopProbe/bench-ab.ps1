#Requires -Version 7
<#
.SYNOPSIS
A/B probe timing between two git revisions using isolated worktrees.

.DESCRIPTION
Creates temp worktrees for <BaseRef> and <AttemptRef>, builds VmLoopProbe in
each, then alternates probe runs (rounds x cases) to bound machine-state
drift. Prints median mean_ns per side and the delta. Use this before
accepting/rejecting an optimization attempt; single runs are too noisy.

.EXAMPLE
pwsh tools/VmLoopProbe/bench-ab.ps1 -Cases smi-sum-loop,closure-heavy -Rounds 5

.EXAMPLE
pwsh tools/VmLoopProbe/bench-ab.ps1 -BaseRef vm-opt -AttemptRef HEAD -Cases pure-function-call -Iterations 100

.EXAMPLE
pwsh tools/VmLoopProbe/bench-ab.ps1 -BaseRef HEAD -AttemptWorkingTree -Cases smi-sum-loop
#>
[CmdletBinding()]
param(
    [string]$BaseRef = "vm-opt",
    [string]$AttemptRef = "HEAD",
    [string[]]$Cases = @("smi-sum-loop", "closure-heavy"),
    [ValidateSet("pgo-on", "pgo-off", "tiered-off")]
    [string]$Config = "pgo-off",
    [int]$Iterations = 200,
    [int]$Warmup = 400,
    [int]$Rounds = 5,
    [string]$WorkRoot = "$env:TEMP\okojo-vmopt-ab",
    [switch]$AttemptWorkingTree
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel).Trim()
$Cases = @($Cases | ForEach-Object { $_ -split "," } | Where-Object { $_ })

function Invoke-WorktreeBuild([string]$refName, [string]$dirName) {
    $wtDir = Join-Path $WorkRoot $dirName
    if (Test-Path $wtDir) {
        git worktree remove --force $wtDir 2>$null | Out-Null
        Remove-Item -Recurse -Force $wtDir -ErrorAction SilentlyContinue
    }
    git worktree add $wtDir $refName | Out-Null
    dotnet build (Join-Path $wtDir "tools/VmLoopProbe/VmLoopProbe.csproj") `
        -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $refName." }
    return Join-Path $wtDir "tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll"
}

Write-Host "Preparing base ($BaseRef)..." -ForegroundColor Cyan
$baseDll = Invoke-WorktreeBuild $BaseRef "base"

if ($AttemptWorkingTree) {
    Write-Host "Preparing attempt (working tree)..." -ForegroundColor Cyan
    dotnet build (Join-Path $repoRoot "tools/VmLoopProbe/VmLoopProbe.csproj") `
        -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed for working tree." }
    $attemptDll = Join-Path $repoRoot "tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll"
}
else {
    Write-Host "Preparing attempt ($AttemptRef)..." -ForegroundColor Cyan
    $attemptDll = Invoke-WorktreeBuild $AttemptRef "attempt"
}

$savedTiered = [Environment]::GetEnvironmentVariable("DOTNET_TieredCompilation")
$savedPgo = [Environment]::GetEnvironmentVariable("DOTNET_TieredPGO")
[Environment]::SetEnvironmentVariable(
    "DOTNET_TieredCompilation",
    $(if ($Config -eq "tiered-off") { "0" } else { "1" })
)
[Environment]::SetEnvironmentVariable("DOTNET_TieredPGO", $(if ($Config -eq "pgo-on") { "1" } else { "0" }))

$results = @{}
foreach ($side in @("base", "attempt")) { $results[$side] = @{} }

try {
    for ($round = 1; $round -le $Rounds; $round++) {
        Write-Host "Round $round/$Rounds" -ForegroundColor Green
        foreach ($case in $Cases) {
            foreach ($side in @("base", "attempt")) {
                $dll = if ($side -eq "base") { $baseDll } else { $attemptDll }
                $out = & dotnet $dll $case $Iterations $Warmup 2>$null |
                    Select-String '^\[result\]' | ForEach-Object Line
                if (-not $out) { throw "Probe produced no result: $side $case" }
                $mean = [double]($out -replace '.*mean_ns=([\d.]+).*', '$1')
                if (-not $results[$side].ContainsKey($case)) {
                    $results[$side][$case] = New-Object System.Collections.Generic.List[double]
                }
                $results[$side][$case].Add($mean)
            }
        }
    }
}
finally {
    [Environment]::SetEnvironmentVariable("DOTNET_TieredCompilation", $savedTiered)
    [Environment]::SetEnvironmentVariable("DOTNET_TieredPGO", $savedPgo)
    git worktree remove --force (Join-Path $WorkRoot "base") 2>$null | Out-Null
    if (-not $AttemptWorkingTree) {
        git worktree remove --force (Join-Path $WorkRoot "attempt") 2>$null | Out-Null
    }
}

Write-Host ""
Write-Host ("{0,-22} {1,14} {2,14} {3,10}" -f "case", "base median", "attempt med", "delta") -ForegroundColor Green
foreach ($case in $Cases) {
    $b = ($results["base"][$case] | Sort-Object)[[int][Math]::Floor(($results["base"][$case].Count - 1) / 2)]
    $a = ($results["attempt"][$case] | Sort-Object)[[int][Math]::Floor(($results["attempt"][$case].Count - 1) / 2)]
    $delta = if ($b -gt 0) { "{0:P1}" -f (($a - $b) / $b) } else { "n/a" }
    Write-Host ("{0,-22} {1,14:F0} {2,14:F0} {3,10}" -f $case, $b, $a, $delta)
}
