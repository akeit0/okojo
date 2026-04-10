$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\Okojo.Node.Cli\Okojo.Node.Cli.csproj'
$packageOutput = Join-Path $repoRoot 'artifacts\tool-packages'
$version = '0.1.0-local.' + (Get-Date -Format 'yyyyMMddHHmmss')

Write-Host "Packing okojonode $version from $projectPath"
dotnet pack $projectPath -c Debug -o $packageOutput /p:Version=$version
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Updating local tool manifest entry from $packageOutput"
Push-Location $repoRoot
try {
    dotnet tool update okojonode --add-source $packageOutput --version $version --local
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

Write-Host "okojonode local tool updated to $version."
