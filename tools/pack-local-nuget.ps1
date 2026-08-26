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
                    ProjectReferences = @(
                        $projectFile.Project.ItemGroup.ProjectReference |
                            ForEach-Object { [string]$_.Include } |
                            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
                    )
                }
            }
        }
) | Sort-Object Id

$availablePackageIds = $projects.Id -join ', '
$projectsByPath = @{}
foreach ($project in $projects) {
    $projectsByPath[[IO.Path]::GetFullPath($project.Path)] = $project
}

$selected = [Collections.Generic.List[object]]::new()
$selectedPaths = [Collections.Generic.HashSet[string]]::new()
$pending = [Collections.Generic.Queue[object]]::new()
foreach ($project in $projects) {
    $matchesFilter = $false
    foreach ($pattern in $Filter) {
        if ($project.Id -like $pattern) {
            $matchesFilter = $true
            break
        }
    }

    if ($matchesFilter) {
        $selected.Add($project)
        $selectedPaths.Add([IO.Path]::GetFullPath($project.Path)) | Out-Null
        $pending.Enqueue($project)
    }
}

if ($selected.Count -eq 0) {
    throw "No packable projects match filter '$($Filter -join ', ')'. Available packages: $availablePackageIds"
}

while ($pending.Count -gt 0) {
    $project = $pending.Dequeue()
    foreach ($reference in $project.ProjectReferences) {
        $referencePath = [IO.Path]::GetFullPath(
            (Join-Path (Split-Path -Parent $project.Path) $reference)
        )
        if (
            $projectsByPath.ContainsKey($referencePath) -and
            $selectedPaths.Add($referencePath)
        ) {
            $dependency = $projectsByPath[$referencePath]
            $selected.Add($dependency)
            $pending.Enqueue($dependency)
        }
    }
}

$projects = @($selected | Sort-Object Id)

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
