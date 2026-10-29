param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [string]$StagingRoot = ""
)

$ErrorActionPreference = "Stop"

function Write-Utf8NoBomFile(
    [string]$Path,
    [string]$Content) {

    $utf8NoBom =
        New-Object System.Text.UTF8Encoding($false)

    [System.IO.File]::WriteAllText(
        $Path,
        $Content,
        $utf8NoBom)
}


$scriptDirectory =
    Split-Path -Parent $MyInvocation.MyCommand.Path

$testScript =
    Join-Path $scriptDirectory "Test-InitiativeTrackerRelease.ps1"

if (-not (Test-Path $testScript)) {
    throw "Test-InitiativeTrackerRelease.ps1 was not found beside this script."
}

& $testScript -ManifestPath $ManifestPath

if ($LASTEXITCODE -ne 0) {
    throw "Release failed validation and was not staged."
}

$ManifestPath =
    (Resolve-Path $ManifestPath).Path

$sourceDirectory =
    Split-Path $ManifestPath -Parent

$manifest =
    Get-Content $ManifestPath -Raw |
    ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($StagingRoot)) {
    $StagingRoot =
        Join-Path $sourceDirectory "..\..\staged"
}

$StagingRoot =
    [IO.Path]::GetFullPath($StagingRoot)

$stageName =
    "$($manifest.version)-$($manifest.platform)"

$destination =
    Join-Path $StagingRoot $stageName

if (Test-Path $destination) {
    Remove-Item $destination -Recurse -Force
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null

$packagePath =
    Join-Path $sourceDirectory $manifest.package.fileName

Copy-Item `
    $ManifestPath `
    (Join-Path $destination "release-manifest.json")

Copy-Item `
    $packagePath `
    (Join-Path $destination $manifest.package.fileName)

$stagedMetadata = [ordered]@{
    stagedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    version = $manifest.version
    platform = $manifest.platform
    packageFileName = $manifest.package.fileName
    packageSha256 = $manifest.package.sha256
}

$stagedMetadataJson =
    $stagedMetadata |
    ConvertTo-Json -Depth 4

Write-Utf8NoBomFile `
    -Path (Join-Path $destination "staged-release.json") `
    -Content $stagedMetadataJson

Write-Host ""
Write-Host "Release staged successfully:" -ForegroundColor Green
Write-Host "  $destination"
Write-Host ""
Write-Host "Nothing has been installed or activated." -ForegroundColor Yellow
