#Requires -Version 7
<#
.SYNOPSIS
Captures Okojo VM loop JIT evidence (IL/asm) for one optimization attempt.

.DESCRIPTION
Builds tools/VmLoopProbe in Release, runs each case under several JIT
configurations (Dynamic PGO on/off, tiered off), and saves stdout with
diffable JIT disassembly plus attempt metadata into a timestamped
snapshot directory under artifacts/vmloopopt/snapshots/.

JIT dump knobs used (see dotnet/runtime docs "Viewing JIT dumps"):
  DOTNET_JitDisasm=<method list>        wildcard patterns (* ?), NOT regex
                                        e.g. *JsRealm:Run* (':' => class-qualified)
  DOTNET_JitDisasmDiffable=1            stable, diff-friendly asm output
  DOTNET_JitStdOutFile=<file>           write JIT output to this file
  DOTNET_TieredPGO / DOTNET_TieredCompilation

Debug/Checked-runtime-only knobs (ignored by product runtime, set manually):
  DOTNET_JitDisasmAssemblies, DOTNET_JitPrintInlinedMethods,
  DOTNET_JitDisasmWithGC, DOTNET_JitDisasmWithDebugInfo

Snapshot dir: artifacts/vmloopopt/snapshots/<timestamp>-<AttemptId>

Default config: pgo-off - without profile-guided recompilation it gives stable
tiered A/B comparisons. Use tiered-off for one deterministic FullOpts assembly
body, or add pgo-on when studying profile-guided specialization.

.EXAMPLE
pwsh tools/VmLoopProbe/capture-jit.ps1 -AttemptId 0000-baseline -Cases smi-sum-loop,for-loop-sum

pwsh tools/VmLoopProbe/capture-jit.ps1 -AttemptId 0002-x -Configs pgo-off,pgo-on
#>
[CmdletBinding()]
param(
    [string]$AttemptId = "baseline",
    [string[]]$Cases = @("smi-sum-loop"),
    [ValidateSet("pgo-on", "pgo-off", "tiered-off")]
    [string[]]$Configs = @("pgo-off"),
    [string]$MethodFilter = "*JsRealm:Run*",
    [int]$Iterations = 200,
    [int]$Warmup = 400,
    [switch]$NoBuild,
    [switch]$SkipGitInfo,
    [switch]$Benchmark
)

$ErrorActionPreference = "Stop"

# Normalize "-Cases a,b,c" passed as a single string when invoked via pwsh -File.
$Cases = @($Cases | ForEach-Object { $_ -split "," } | Where-Object { $_ })

$repoRoot = (git rev-parse --show-toplevel).Trim()
if (-not $repoRoot) {
    throw "Not inside a git repository."
}

if (-not $NoBuild) {
    Write-Host "Building VmLoopProbe (Release)..." -ForegroundColor Cyan
    dotnet build "$repoRoot/tools/VmLoopProbe/VmLoopProbe.csproj" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$probeDll = Join-Path $repoRoot "tools/VmLoopProbe/bin/Release/net10.0/VmLoopProbe.dll"
if (-not (Test-Path $probeDll)) {
    throw "Probe not found at $probeDll. Build first or drop -NoBuild."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$snapshotDir = Join-Path $repoRoot "artifacts/vmloopopt/snapshots/$timestamp-$AttemptId"
New-Item -ItemType Directory -Force -Path (Join-Path $snapshotDir "jit") | Out-Null

Write-Host "Snapshot dir: $snapshotDir" -ForegroundColor Green

if (-not $SkipGitInfo) {
    git -C $repoRoot rev-parse HEAD | Set-Content (Join-Path $snapshotDir "commit.txt")
    git -C $repoRoot status --short | Set-Content (Join-Path $snapshotDir "status.txt")
    git -C $repoRoot diff HEAD -- src tools benchmarks tests |
        Set-Content (Join-Path $snapshotDir "patch.diff")
}

dotnet --info | Set-Content (Join-Path $snapshotDir "env.txt")

$noticeTemplate = Join-Path $repoRoot "tools/VmLoopProbe/notice-template.md"
$noticePath = Join-Path $snapshotDir "notice.md"
if ((Test-Path $noticeTemplate) -and -not (Test-Path $noticePath)) {
    Copy-Item $noticeTemplate $noticePath
}

$runLocalsPath = Join-Path $snapshotDir "run-locals.txt"
& dotnet $probeDll --inspect-run | Set-Content $runLocalsPath
if ($LASTEXITCODE -ne 0) { throw "Run local inspection failed." }

$configSettings = @{
    "pgo-on"     = @{ "DOTNET_TieredCompilation" = "1"; "DOTNET_TieredPGO" = "1" }
    "pgo-off"    = @{ "DOTNET_TieredCompilation" = "1"; "DOTNET_TieredPGO" = "0" }
    "tiered-off" = @{ "DOTNET_TieredCompilation" = "0"; "DOTNET_TieredPGO" = "0" }
}

$savedEnv = @{}
foreach ($key in @(
    "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_JitDisasm",
    "DOTNET_JitDisasmDiffable", "DOTNET_JitStdOutFile"
)) {
    $savedEnv[$key] = [Environment]::GetEnvironmentVariable($key)
}

$results = New-Object System.Collections.Generic.List[string]

try {
    foreach ($config in $Configs) {
        $settings = $configSettings[$config]
        foreach ($kv in $settings.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($kv.Key, $kv.Value)
        }
        [Environment]::SetEnvironmentVariable("DOTNET_JitDisasm", $MethodFilter)
        [Environment]::SetEnvironmentVariable("DOTNET_JitDisasmDiffable", "1")

        foreach ($case in $Cases) {
            Write-Host "Running config=$config case=$case ..." -ForegroundColor Cyan
            $jitFile = Join-Path $snapshotDir "jit/$case.$config.jit.txt"
            $outFile = Join-Path $snapshotDir "jit/$case.$config.stdout.txt"
            [Environment]::SetEnvironmentVariable("DOTNET_JitStdOutFile", $jitFile)
            & dotnet $probeDll $case $Iterations $Warmup > $outFile
            if ($LASTEXITCODE -ne 0) { throw "Probe failed: config=$config case=$case" }

            Select-String -Path $outFile -Pattern '^\[result\]|^\[mode\]' |
                ForEach-Object Line |
                Set-Content (Join-Path $snapshotDir "jit/$case.$config.result.txt")

            $resultLine = (Get-Content (Join-Path $snapshotDir "jit/$case.$config.result.txt") |
                Where-Object { $_ -match '^\[result\]' } | Select-Object -First 1)
            if ($resultLine) { $results.Add($resultLine) }

            $dumps = @(Select-String -Path $jitFile -Pattern '^; Assembly listing for method' -ErrorAction SilentlyContinue)
            if ($dumps.Count -eq 0) {
                Write-Host "  WARNING: no JIT dumps matched filter '$MethodFilter'" -ForegroundColor Yellow
            } else {
                Write-Host "  $($dumps.Count) method dump(s):" -ForegroundColor DarkGray
                $dumps | ForEach-Object { Write-Host "    $($_.Line)" -ForegroundColor DarkGray }
            }
        }
    }
}
finally {
    foreach ($kv in $savedEnv.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($kv.Key, $kv.Value)
    }
}

$results | Set-Content (Join-Path $snapshotDir "results.txt")
Write-Host ""
Write-Host "Results:" -ForegroundColor Green
$results | ForEach-Object { Write-Host "  $_" }

if ($Benchmark) {
    Write-Host ""
    Write-Host "Running BenchmarkDotNet confirmation (VmLoopDispatchBenchmarks)..." -ForegroundColor Cyan
    Push-Location $repoRoot
    try {
        dotnet run -c Release --project benchmarks/Okojo.Benchmarks --no-build -- `
            --filter '*VmLoopDispatchBenchmarks*'
        if ($LASTEXITCODE -ne 0) { throw "BenchmarkDotNet run failed." }
    }
    finally {
        Pop-Location
    }
    $benchDir = Join-Path $snapshotDir "bench"
    New-Item -ItemType Directory -Force -Path $benchDir | Out-Null
    Get-ChildItem "$repoRoot/benchmarks/Okojo.Benchmarks/BenchmarkDotNet.Artifacts/results" |
        Where-Object Name -like "Okojo.Benchmarks.VmLoopDispatchBenchmarks*" |
        Copy-Item -Destination $benchDir
    Write-Host "BDN reports copied -> bench/" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done. Fill in notice.md and record findings." -ForegroundColor Green
