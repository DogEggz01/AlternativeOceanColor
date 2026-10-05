[CmdletBinding()]
param(
    [string] $GameDir = (Join-Path $PSScriptRoot '..\..'),
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$GameDir = [System.IO.Path]::GetFullPath($GameDir)
$project = Join-Path $PSScriptRoot 'AlternativeOceanColor.csproj'
[xml] $projectXml = Get-Content -LiteralPath $project -Raw
$version = $projectXml.Project.PropertyGroup.Version

dotnet build $project -c Release "-p:GameDir=$GameDir\"
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
if (-not $SkipTests) {
    dotnet run --project (Join-Path $PSScriptRoot 'Tests\AlternativeOceanColor.Tests.csproj') -c Release "-p:GameDir=$GameDir\"
    if ($LASTEXITCODE -ne 0) { throw 'Managed regression tests failed.' }
}

$dist = Join-Path $PSScriptRoot 'dist'
$staging = Join-Path $dist "AlternativeOceanColor-$version\AlternativeOceanColor"
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.0\AlternativeOceanColor.dll') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $staging
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $staging
$presetDestination = Join-Path $staging 'Presets'
New-Item -ItemType Directory -Path $presetDestination -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Presets') -Filter '*.cfg' |
    Copy-Item -Destination $presetDestination
$zip = Join-Path $dist "AlternativeOceanColor-$version.zip"
Compress-Archive -LiteralPath $staging -DestinationPath $zip -Force
Write-Output "Built $zip"
Get-FileHash -LiteralPath $zip -Algorithm SHA256
