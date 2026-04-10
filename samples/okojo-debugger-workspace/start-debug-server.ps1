param(
    [string[]]$Break = @(),
    [switch]$StopEntry = $true
)

$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = [System.IO.Path]::GetFullPath((Join-Path $workspace "..\..\src\Okojo.DebugServer\Okojo.DebugServer.csproj"))
$program = [System.IO.Path]::GetFullPath((Join-Path $workspace "dist\\entry.mjs"))

$dotnetArgs = @(
    "run",
    "--project",
    $project,
    "--",
    "--script",
    $program,
    "--cwd",
    $workspace,
    "--module-entry",
    "--enable-source-maps",
    "--check-interval",
    "1"
)

if ($StopEntry) {
    $dotnetArgs += "--stop-entry"
}

foreach ($breakpoint in $Break) {
    $dotnetArgs += "--break"
    $colonIndex = $breakpoint.LastIndexOf(':')
    if ($colonIndex -lt 0) {
        throw "Breakpoint must be sourcePath:line. Got '$breakpoint'."
    }

    $relativePath = $breakpoint.Substring(0, $colonIndex)
    $line = $breakpoint.Substring($colonIndex + 1)
    if ([System.IO.Path]::IsPathRooted($relativePath)) {
        $dotnetArgs += "${relativePath}:$line"
        continue
    }

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $workspace $relativePath))
    $dotnetArgs += "${fullPath}:$line"
}

Write-Host "Okojo debugger sample"
Write-Host "program: $program"
Write-Host "examples:"
Write-Host "  break $([System.IO.Path]::GetFullPath((Join-Path $workspace 'src\entry.mts'))):10"
Write-Host "  break $([System.IO.Path]::GetFullPath((Join-Path $workspace 'src\lib\math.mts'))):5"
Write-Host "  continue"
Write-Host "  step"
Write-Host "  bytecode"
Write-Host ""

& dotnet @dotnetArgs
