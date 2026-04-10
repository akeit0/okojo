param(
    [string]$CasesDir = "",
    [string]$SnapshotsRoot = "",
    [string]$ToolProject = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
if ([string]::IsNullOrWhiteSpace($CasesDir)) {
    $CasesDir = Join-Path $repoRoot "artifacts/okojobytecodetool/cases"
}
if ([string]::IsNullOrWhiteSpace($SnapshotsRoot)) {
    $SnapshotsRoot = Join-Path $repoRoot "artifacts/okojobytecodetool/snapshots"
}
if ([string]::IsNullOrWhiteSpace($ToolProject)) {
    $ToolProject = Join-Path $repoRoot "tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj"
}

if (-not (Test-Path $CasesDir)) {
    throw "Cases directory not found: $CasesDir"
}

$caseFiles = Get-ChildItem $CasesDir -Filter *.js | Sort-Object Name
if ($caseFiles.Count -eq 0) {
    throw "No case files found in: $CasesDir"
}

New-Item -ItemType Directory -Force $SnapshotsRoot | Out-Null
$ts = Get-Date -Format "yyyyMMdd-HHmmss"
$currentSnapshotDir = Join-Path $SnapshotsRoot $ts
New-Item -ItemType Directory -Force $currentSnapshotDir | Out-Null

foreach ($caseFile in $caseFiles) {
    $outPath = Join-Path $currentSnapshotDir ($caseFile.BaseName + ".disasm.txt")
    dotnet run --project $ToolProject -- $caseFile.FullName --save $outPath | Out-Null
}

$expectedNames = $caseFiles | ForEach-Object { $_.BaseName + ".disasm.txt" } | Sort-Object
$snapshotDirs = Get-ChildItem $SnapshotsRoot -Directory | Where-Object { $_.Name -ne $ts } | Sort-Object Name -Descending
$previousCompatible = $null

foreach ($dir in $snapshotDirs) {
    $names = Get-ChildItem $dir.FullName -Filter *.disasm.txt | ForEach-Object { $_.Name } | Sort-Object
    if ($names.Count -ne $expectedNames.Count) { continue }
    $match = $true
    for ($i = 0; $i -lt $names.Count; $i++) {
        if ($names[$i] -ne $expectedNames[$i]) {
            $match = $false
            break
        }
    }
    if ($match) {
        $previousCompatible = $dir
        break
    }
}

if ($null -ne $previousCompatible) {
    $compare = dotnet run --project $ToolProject -- --compare $previousCompatible.FullName $currentSnapshotDir
    $compareNamedPath = Join-Path $currentSnapshotDir ("compare_to_" + $previousCompatible.Name + ".md")
    $comparePrevPath = Join-Path $currentSnapshotDir "compare_to_previous.md"
    Set-Content -Encoding UTF8 $compareNamedPath $compare
    Set-Content -Encoding UTF8 $comparePrevPath $compare
    Write-Host "Snapshot created: $currentSnapshotDir"
    Write-Host "Compared with:   $($previousCompatible.FullName)"
    Write-Host "Compare report:  $compareNamedPath"
} else {
    Write-Host "Snapshot created: $currentSnapshotDir"
    Write-Host "No compatible previous snapshot found (same case-file set)."
}
