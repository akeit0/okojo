[CmdletBinding()]
param(
    [string]$FeedPath = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'LocalNuget'),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = ('0.1.0-local.' + (Get-Date -Format 'yyyyMMddHHmmss')),
    [Alias('Package')]
    [string[]]$Filter = @('*'),
    [switch]$RegisterSource,
    [string]$SourceName = 'okojo-local'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'src'
$feedPath = [IO.Path]::GetFullPath($FeedPath)

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'Version must not be empty.'
}

$projects = @(
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.csproj' |
        ForEach-Object {
            [xml]$projectFile = Get-Content -LiteralPath $_.FullName -Raw
            $propertyGroups = @($projectFile.Project.PropertyGroup)
            $isPackable = @($propertyGroups | ForEach-Object { $_.IsPackable }) -contains 'true'
            $packageId = @($propertyGroups | ForEach-Object { $_.PackageId }) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -First 1

            if ($isPackable -and $packageId) {
                [pscustomobject]@{
                    Id = [string]$packageId
                    Path = $_.FullName
                }
            }
        }
) | Sort-Object Id

$availablePackageIds = $projects.Id -join ', '
$projects = @(
    $projects | Where-Object {
        $projectId = $_.Id
        $matchesFilter = $false
        foreach ($pattern in $Filter) {
            if ($projectId -like $pattern) {
                $matchesFilter = $true
                break
            }
        }
        $matchesFilter
    }
)

if (-not $projects) {
    throw "No packable projects match filter '$($Filter -join ', ')'. Available packages: $availablePackageIds"
}

New-Item -ItemType Directory -Force -Path $feedPath | Out-Null
Write-Host "Selected packages: $($projects.Id -join ', ')"

foreach ($project in $projects) {
    Write-Host "Packing $($project.Id) $Version"
    & dotnet pack $project.Path `
        --configuration $Configuration `
        --output $feedPath `
        "/p:PackageVersion=$Version" `
        "/p:Version=$Version" `
        '/p:ContinuousIntegrationBuild=true'

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $($project.Id) with exit code $LASTEXITCODE."
    }
}

if ($RegisterSource) {
    Write-Host "Registering NuGet source '$SourceName' at $feedPath"
    & dotnet nuget update source $SourceName --source $feedPath
    if ($LASTEXITCODE -ne 0) {
        & dotnet nuget add source $feedPath --name $SourceName
        if ($LASTEXITCODE -ne 0) {
            throw "Could not register NuGet source '$SourceName'."
        }
    }
}

Write-Host "Local NuGet packages are in $feedPath"
Write-Host "Use with: dotnet add <project.csproj> package <PackageId> --version $Version --source $feedPath"
