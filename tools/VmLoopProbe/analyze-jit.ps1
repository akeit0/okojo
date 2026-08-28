#Requires -Version 7
<#
.SYNOPSIS
Reports `JsRealm.Run` JIT costs per opcode arm.

.DESCRIPTION
Parses the RWD00 opcode table and G_M000_IG blocks from a JIT listing. The
report is intentionally structural: it counts instructions, approximate
memory loads/stores, calls, indirect jumps, and private rbp slots. Per-block
code bytes require a listing with `;; offset=...` annotations (the normal
non-diffable JIT listing); diffable listings still provide the other metrics.

Use -ComparePath to compare the same tier from another listing. Comparison is
keyed by opcode, not by IG label, because block labels can move after a source
change.

.EXAMPLE
pwsh tools/VmLoopProbe/analyze-jit.ps1 `
  -Path artifacts/vmloopopt/snapshots/<snapshot>/jit/smi-sum-loop.tiered-off.jit.txt

.EXAMPLE
pwsh tools/VmLoopProbe/analyze-jit.ps1 `
  -Path <attempt>.jit.txt -ComparePath <baseline>.jit.txt -Tier Tier1 `
  -ChangedOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path,
    [string]$ComparePath,
    [string]$AddressPath,
    [string]$Tier,
    [string]$CompareTier,
    [string]$OutputPath,
    [switch]$ChangedOnly
)

$ErrorActionPreference = "Stop"

function Get-JitListings([string]$FilePath) {
    if (-not (Test-Path -LiteralPath $FilePath)) {
        throw "JIT listing not found: $FilePath"
    }

    $lines = @(Get-Content -LiteralPath $FilePath)
    $headers = @(
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $match = [regex]::Match(
                $lines[$i],
                '^; Assembly listing for method (?<method>.+) \((?<tier>[^()]+)\)$'
            )
            if ($match.Success) {
                [pscustomobject]@{
                    Index = $i
                    Method = $match.Groups['method'].Value
                    Tier = $match.Groups['tier'].Value
                }
            }
        }
    )

    $listings = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $headers.Count; $i++) {
        $start = $headers[$i].Index
        $end = if ($i + 1 -lt $headers.Count) {
            $headers[$i + 1].Index
        }
        else {
            $lines.Count
        }
        $section = @($lines[$start..($end - 1)])
        $totalBytes = $null
        $totalLine = $section |
            Where-Object { $_ -match '^; Total bytes of code (?<bytes>\d+)' } |
            Select-Object -First 1
        if ($totalLine) {
            $totalBytes = [int]([regex]::Match(
                    $totalLine,
                    '^; Total bytes of code (?<bytes>\d+)'
                ).Groups['bytes'].Value)
        }

        $blockHeaders = @(
            for ($j = 0; $j -lt $section.Count; $j++) {
                $match = [regex]::Match(
                    $section[$j],
                    '^\s*(?<label>G_M\d+_IG\d+):(?:\s*;;\s*offset=(?<offset>0x[0-9A-Fa-f]+))?'
                )
                if ($match.Success) {
                    $offset = $null
                    if ($match.Groups['offset'].Success) {
                        $offset = [Convert]::ToInt32(
                            $match.Groups['offset'].Value.Substring(2),
                            16
                        )
                    }
                    [pscustomobject]@{
                        Index = $j
                        Label = $match.Groups['label'].Value
                        Offset = $offset
                    }
                }
            }
        )

        $blocks = New-Object System.Collections.Generic.List[object]
        for ($j = 0; $j -lt $blockHeaders.Count; $j++) {
            $blockStart = $blockHeaders[$j].Index
            $blockEnd = if ($j + 1 -lt $blockHeaders.Count) {
                $blockHeaders[$j + 1].Index
            }
            else {
                $section.Count
            }
            for ($k = $blockStart; $k -lt $blockEnd; $k++) {
                if ($section[$k] -match '^\s*RWD\d+\b|^; Total bytes of code') {
                    $blockEnd = $k
                    break
                }
            }
            $blocks.Add([pscustomobject]@{
                    Label = $blockHeaders[$j].Label
                    Offset = $blockHeaders[$j].Offset
                    Lines = @($section[$blockStart..($blockEnd - 1)])
                })
        }

        for ($j = 0; $j -lt $blocks.Count; $j++) {
            $bytes = $null
            if ($null -ne $blocks[$j].Offset) {
                $nextOffset = $null
                if ($j + 1 -lt $blocks.Count) {
                    $nextOffset = $blocks[$j + 1].Offset
                }
                if ($null -ne $nextOffset) {
                    $bytes = $nextOffset - $blocks[$j].Offset
                }
                elseif ($null -ne $totalBytes) {
                    $bytes = $totalBytes - $blocks[$j].Offset
                }
            }
            $blocks[$j] | Add-Member -NotePropertyName CodeBytes -NotePropertyValue $bytes
        }

        $listings.Add([pscustomobject]@{
                Method = $headers[$i].Method
                Tier = $headers[$i].Tier
                TotalBytes = $totalBytes
                Lines = $section
                Blocks = $blocks.ToArray()
            })
    }
    return $listings.ToArray()
}

function Select-JitListing($Listings, [string]$RequestedTier) {
    if ($RequestedTier) {
        $selected = @($Listings | Where-Object { $_.Tier -eq $RequestedTier })
        if ($selected.Count -eq 0) {
            $available = ($Listings | ForEach-Object Tier | Sort-Object -Unique) -join ', '
            throw "Tier '$RequestedTier' not found. Available tiers: $available"
        }
        return $selected[0]
    }

    if ($Listings.Count -eq 1) {
        return $Listings[0]
    }
    foreach ($preferred in @('Tier1', 'FullOpts', 'Tier1-OSR', 'Tier0')) {
        $selected = @($Listings | Where-Object { $_.Tier -eq $preferred })
        if ($selected.Count -gt 0) {
            return $selected[0]
        }
    }
    return $Listings[0]
}

function Get-OpcodeMap($Listing) {
    $start = -1
    for ($i = 0; $i -lt $Listing.Lines.Count; $i++) {
        if ($Listing.Lines[$i] -match '^\s*RWD00\s+dd\b') {
            $start = $i
            break
        }
    }
    if ($start -lt 0) {
        throw "RWD00 opcode table not found in $($Listing.Tier) listing."
    }

    $map = New-Object System.Collections.Generic.List[object]
    for ($i = $start; $i -lt $Listing.Lines.Count; $i++) {
        $line = $Listing.Lines[$i]
        if ($i -gt $start -and $line -match '^\s*RWD\d+\b') {
            break
        }
        if ($line -notmatch '^\s*(?:RWD00\s+)?dd\b') {
            if ($i -gt $start) { break }
            continue
        }
        $target = [regex]::Match($line, 'G_M\d+_IG\d+').Value
        if (-not $target) {
            throw "RWD00 entry has no IG target: $line"
        }
        $map.Add([pscustomobject]@{
                Opcode = $map.Count
                Target = $target
            })
    }
    if ($map.Count -eq 0) {
        throw "RWD00 opcode table is empty."
    }
    return $map.ToArray()
}

function Get-Instruction([string]$Line) {
    $trimmed = $Line.Trim()
    if (-not $trimmed -or $trimmed.StartsWith(';') -or $trimmed -match '^G_M\d+_IG\d+:') {
        return $null
    }
    $match = [regex]::Match($Line, '^\s+(?<mnemonic>[A-Za-z][A-Za-z0-9]*)(?:\s+(?<operands>.*?))?\s*$')
    if (-not $match.Success) {
        return $null
    }
    $mnemonic = $match.Groups['mnemonic'].Value.ToLowerInvariant()
    if ($mnemonic -in @('align', 'dd', 'dq')) {
        return $null
    }
    [pscustomobject]@{
        Mnemonic = $mnemonic
        Operands = $match.Groups['operands'].Value
        Text = $trimmed
    }
}

function Get-BlockMetrics($Block) {
    $instructions = @(
        foreach ($line in $Block.Lines) {
            Get-Instruction $line
        }
    )
    $slots = New-Object 'System.Collections.Generic.HashSet[string]'
    $loads = 0
    $stores = 0
    $calls = 0
    $indirectJumps = 0

    foreach ($instruction in $instructions) {
        $operands = $instruction.Operands
        foreach ($match in [regex]::Matches($operands, '\[rbp-(?:0x[0-9A-Fa-f]+|\d+)\]')) {
            [void]$slots.Add($match.Value.ToLowerInvariant())
        }
        if ($instruction.Mnemonic -eq 'call') {
            $calls++
            continue
        }
        if ($instruction.Mnemonic -eq 'jmp') {
            $target = ($operands -replace '^SHORT\s+', '').Trim()
            if ($target -notmatch '^G_M\d+_IG\d+$') {
                $indirectJumps++
            }
            continue
        }
        if ($operands -notmatch '\[' -or $instruction.Mnemonic -eq 'lea') {
            continue
        }
        $firstOperand = ($operands -split ',', 2)[0].Trim()
        if ($instruction.Mnemonic -in @('cmp', 'test', 'ucomisd', 'vucomisd', 'comisd', 'vcomisd')) {
            $loads++
        }
        elseif ($firstOperand -match '\[') {
            $stores++
        }
        else {
            $loads++
        }
    }

    [pscustomobject]@{
        CodeBytes = $Block.CodeBytes
        Instructions = $instructions.Count
        Loads = $loads
        Stores = $stores
        Calls = $calls
        IndirectJumps = $indirectJumps
        StackSlots = (($slots | Sort-Object) -join ',')
    }
}

function Get-ArmMetrics($Listing, $AddressListing) {
    if ($AddressListing) {
        $addressByLabel = @{}
        foreach ($block in $AddressListing.Blocks) {
            $addressByLabel[$block.Label] = $block.CodeBytes
        }
    }

    $blocksByLabel = @{}
    foreach ($block in $Listing.Blocks) {
        if ($AddressListing -and $addressByLabel.ContainsKey($block.Label)) {
            $block | Add-Member -Force -NotePropertyName CodeBytes -NotePropertyValue $addressByLabel[$block.Label]
        }
        $blocksByLabel[$block.Label] = $block
    }

    $metricsByTarget = @{}
    $opcodeMap = Get-OpcodeMap $Listing
    $metrics = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $opcodeMap) {
        if (-not $metricsByTarget.ContainsKey($entry.Target)) {
            $block = $blocksByLabel[$entry.Target]
            if ($null -eq $block) {
                throw "RWD00 target $($entry.Target) has no code block."
            }
            $metricsByTarget[$entry.Target] = Get-BlockMetrics $block
        }
        $metric = $metricsByTarget[$entry.Target]
        $metrics.Add([pscustomobject]@{
                Opcode = $entry.Opcode
                Target = $entry.Target
                CodeBytes = $metric.CodeBytes
                Instructions = $metric.Instructions
                Loads = $metric.Loads
                Stores = $metric.Stores
                Calls = $metric.Calls
                IndirectJumps = $metric.IndirectJumps
                StackSlots = $metric.StackSlots
            })
    }
    return $metrics.ToArray()
}

function Format-Number($Value) {
    if ($null -eq $Value) { return '-' }
    return [string]$Value
}

function Same-Metric($Left, $Right) {
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    if ($Left.Target -ne $Right.Target) { return $false }
    foreach ($name in @('CodeBytes', 'Instructions', 'Loads', 'Stores', 'Calls', 'IndirectJumps', 'StackSlots')) {
        if ($Left.$name -ne $Right.$name) { return $false }
    }
    return $true
}

function Get-ArmReport([string]$FilePath, [string]$RequestedTier, [string]$AddressFilePath) {
    $listing = Select-JitListing (Get-JitListings $FilePath) $RequestedTier
    $addressListing = $null
    if ($AddressFilePath) {
        $addressListing = Select-JitListing (Get-JitListings $AddressFilePath) $RequestedTier
    }
    [pscustomobject]@{
        FilePath = $FilePath
        Listing = $listing
        Metrics = @(Get-ArmMetrics $listing $addressListing)
    }
}

$primary = Get-ArmReport $Path $Tier $AddressPath
$comparison = $null
if ($ComparePath) {
    $comparisonTier = if ($CompareTier) { $CompareTier } else { $Tier }
    $comparison = Get-ArmReport $ComparePath $comparisonTier $null
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("JIT arm report: $($primary.Listing.Method)")
$report.Add("Path: $($primary.FilePath)")
$report.Add("Tier: $($primary.Listing.Tier)  total-code-bytes=$(Format-Number $primary.Listing.TotalBytes)")
if (-not $AddressPath -and $null -eq $primary.Metrics[0].CodeBytes) {
    $report.Add("Note: per-arm code bytes require a non-diffable listing with ;; offset annotations; other counts are available from diffable input.")
}
$report.Add("")

if (-not $comparison) {
    $report.Add("opcodes        target             bytes instr loads stores calls indjmp  private-rbp-slots")
    $groups = @($primary.Metrics | Group-Object Target | Sort-Object { [int]$_.Group[0].Opcode })
    foreach ($group in $groups) {
        $item = $group.Group[0]
        $opcodes = ($group.Group | ForEach-Object Opcode) -join ','
        $report.Add(("{0,-14} {1,-18} {2,5} {3,5} {4,5} {5,6} {6,5} {7,6}  {8}" -f
                $opcodes,
                $item.Target,
                (Format-Number $item.CodeBytes),
                $item.Instructions,
                $item.Loads,
                $item.Stores,
                $item.Calls,
                $item.IndirectJumps,
                $item.StackSlots))
    }
}
else {
    $report.Add("Compare: $($comparison.FilePath)")
    $report.Add("Compare tier: $($comparison.Listing.Tier)")
    $report.Add("")
    $report.Add("opcode from-target        to-target          bytes instr loads stores calls indjmp")
    $toByOpcode = @{}
    foreach ($item in $comparison.Metrics) { $toByOpcode[$item.Opcode] = $item }
    foreach ($from in $primary.Metrics) {
        $to = $toByOpcode[$from.Opcode]
        if ($ChangedOnly -and (Same-Metric $from $to)) { continue }
        $delta = @(
            foreach ($name in @('CodeBytes', 'Instructions', 'Loads', 'Stores', 'Calls', 'IndirectJumps')) {
                if ($null -eq $from.$name -or $null -eq $to.$name) { '-' }
                else { [string]($from.$name - $to.$name) }
            }
        )
        $report.Add(("{0,5} {1,-18} {2,-18} {3,5} {4,5} {5,5} {6,6} {7,5} {8,6}" -f
                $from.Opcode,
                $from.Target,
                $(if ($to) { $to.Target } else { '-' }),
                $delta[0],
                $delta[1],
                $delta[2],
                $delta[3],
                $delta[4],
                $delta[5]))
    }
}

$text = $report -join [Environment]::NewLine
if ($OutputPath) {
    Set-Content -LiteralPath $OutputPath -Value $text
    Write-Output "Saved: $OutputPath"
}
else {
    Write-Output $text
}
