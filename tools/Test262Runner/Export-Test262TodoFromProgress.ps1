param(
    [string]$InputPath = "TEST262_PROGRESS_INCREMENTAL.md",
    [string]$OutputPath = "TEST262_TODO.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Parse-MarkdownTable {
    param(
        [string[]]$Lines,
        [int]$StartIndex
    )

    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = $StartIndex; $i -lt $Lines.Length; $i++) {
        $line = $Lines[$i]
        if ([string]::IsNullOrWhiteSpace($line)) {
            break
        }

        if (-not $line.StartsWith("|")) {
            break
        }

        if ($line -match '^\|\s*---') {
            continue
        }

        $cells = $line.Trim().Trim("|").Split("|") | ForEach-Object { $_.Trim() }
        if ($cells.Count -lt 11) {
            continue
        }

        if ($cells[0] -eq "Scope") {
            continue
        }

        $rows.Add([pscustomobject]@{
            Scope       = $cells[0]
            LastUpdated = $cells[1]
            Total       = [int]$cells[2]
            Passed      = [int]$cells[3]
            Failed      = [int]$cells[4]
            Skipped     = [int]$cells[5]
            NotYet      = [int]$cells[6]
            PassedPct   = [double]($cells[7].TrimEnd('%'))
            FailedPct   = [double]($cells[8].TrimEnd('%'))
            SkippedPct  = [double]($cells[9].TrimEnd('%'))
            NotYetPct   = [double]($cells[10].TrimEnd('%'))
        })
    }

    return $rows
}

function Get-SectionTable {
    param(
        [string[]]$Lines,
        [string]$SectionName
    )

    for ($i = 0; $i -lt $Lines.Length; $i++) {
        if ($Lines[$i] -eq $SectionName) {
            return Parse-MarkdownTable -Lines $Lines -StartIndex ($i + 2)
        }
    }

    throw "Section not found: $SectionName"
}

function Is-CoreScope {
    param([string]$Scope)

    return -not (
        $Scope.StartsWith("annexB/") -or
        $Scope.StartsWith("intl402/") -or
        $Scope.StartsWith("staging/") -or
        $Scope -like "*Temporal*" -or
        $Scope -like "*Atomics*" -or
        $Scope -like "*ShadowRealm*" -or
        $Scope -like "*SharedArrayBuffer*" -or
        $Scope -like "*AsyncDisposableStack*" -or
        $Scope -like "*DisposableStack*"
    )
}

function Add-Table {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Title,
        [object[]]$Rows
    )

    [void]$Builder.AppendLine("## $Title")
    [void]$Builder.AppendLine()

    if ($Rows.Count -eq 0) {
        [void]$Builder.AppendLine("_None_")
        [void]$Builder.AppendLine()
        return
    }

    [void]$Builder.AppendLine("| Scope | Last Updated | Total | Passed | Failed | Skipped | Not Yet | Passed % |")
    [void]$Builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |")
    foreach ($row in $Rows) {
        [void]$Builder.AppendLine("| $($row.Scope) | $($row.LastUpdated) | $($row.Total) | $($row.Passed) | $($row.Failed) | $($row.Skipped) | $($row.NotYet) | $([string]::Format('{0:0.0}%', $row.PassedPct)) |")
    }
    [void]$Builder.AppendLine()
}

$resolvedInput = Join-Path (Get-Location) $InputPath
if (-not (Test-Path $resolvedInput)) {
    throw "Input file not found: $resolvedInput"
}

$lines = Get-Content $resolvedInput
$categories = @(Get-SectionTable -Lines $lines -SectionName "## By Category")
$folders = @(Get-SectionTable -Lines $lines -SectionName "## By Folder")
$features = @(Get-SectionTable -Lines $lines -SectionName "## By Feature")

$categorySummary = $categories |
    Sort-Object Scope |
    Where-Object { $_.Scope -in @("built-ins", "language", "annexB", "intl402", "staging") }

$nearFinish = $folders |
    Where-Object {
        (Is-CoreScope $_.Scope) -and
        $_.Failed -gt 0 -and
        $_.Failed -le 20 -and
        $_.PassedPct -ge 80 -and
        $_.Skipped -le 2 -and
        $_.NotYet -eq 0
    } |
    Sort-Object Failed, Scope |
    Select-Object -First 20

$goodNext = $folders |
    Where-Object {
        (Is-CoreScope $_.Scope) -and
        $_.Failed -gt 20 -and
        $_.PassedPct -ge 70 -and
        $_.Skipped -le 25 -and
        $_.NotYet -eq 0
    } |
    Sort-Object @{ Expression = "PassedPct"; Descending = $true }, Failed, Scope |
    Select-Object -First 20

$largeCore = $folders |
    Where-Object {
        (Is-CoreScope $_.Scope) -and
        $_.Failed -ge 100
    } |
    Sort-Object @{ Expression = "Failed"; Descending = $true }, Scope |
    Select-Object -First 20

$deferredFeatures = $features |
    Where-Object {
        $_.Failed -gt 0 -and (
            $_.Scope -like "*Temporal*" -or
            $_.Scope -like "*Atomics*" -or
            $_.Scope -like "Intl*" -or
            $_.Scope -eq "tail-call-optimization" -or
            $_.Scope -eq "canonical-tz" -or
            $_.Scope -eq "ShadowRealm" -or
            $_.Scope -eq "explicit-resource-management"
        )
    } |
    Sort-Object @{ Expression = "Failed"; Descending = $true }, Scope |
    Select-Object -First 25

$completedHighlights = $folders |
    Where-Object {
        (Is-CoreScope $_.Scope) -and
        $_.Total -ge 40 -and
        $_.Failed -eq 0 -and
        $_.Skipped -eq 0 -and
        $_.NotYet -eq 0
    } |
    Sort-Object @{ Expression = "Total"; Descending = $true }, Scope |
    Select-Object -First 20

$generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine("# Test262 TODO")
[void]$builder.AppendLine()
[void]$builder.AppendLine("Generated from `TEST262_PROGRESS_INCREMENTAL.md` on $generatedAt.")
[void]$builder.AppendLine()
[void]$builder.AppendLine("This file is the readable worklist. It intentionally drops the full raw progress tables and keeps only the scopes that are useful for planning.")
[void]$builder.AppendLine()

Add-Table -Builder $builder -Title "Category Snapshot" -Rows @($categorySummary)
Add-Table -Builder $builder -Title "Near Finish Core Folders" -Rows @($nearFinish)
Add-Table -Builder $builder -Title "Good Next Core Folders" -Rows @($goodNext)
Add-Table -Builder $builder -Title "Large Core Workstreams" -Rows @($largeCore)
Add-Table -Builder $builder -Title "Deferred Or Explicitly Large Feature Buckets" -Rows @($deferredFeatures)
Add-Table -Builder $builder -Title "Completed Core Highlights" -Rows @($completedHighlights)

[void]$builder.AppendLine("## Notes")
[void]$builder.AppendLine()
[void]$builder.AppendLine('- Source: `TEST262_PROGRESS_INCREMENTAL.md`')
[void]$builder.AppendLine('- Regenerate with: `powershell -ExecutionPolicy Bypass -File tools/Test262Runner/Export-Test262TodoFromProgress.ps1`')
[void]$builder.AppendLine('- This is planning-oriented, not a replacement for the full progress report.')
[void]$builder.AppendLine()

$resolvedOutput = Join-Path (Get-Location) $OutputPath
[System.IO.File]::WriteAllText($resolvedOutput, $builder.ToString(), [System.Text.UTF8Encoding]::new($false))
