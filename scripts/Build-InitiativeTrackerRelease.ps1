param(
    [string]$SolutionRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$Project = "InitiativeTracker.Web",
    [string]$Runtime = "linux-arm64",
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [string[]]$ReleaseNotes = @()
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


function Fail([string]$Message) {
    throw $Message
}

function Assert-Tool([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Fail "Required command '$Name' was not found on PATH."
    }
}

function Get-ReleaseVersion([string]$Root) {
    $propsPath = Join-Path $Root "Directory.Build.props"

    if (-not (Test-Path $propsPath)) {
        Fail "Directory.Build.props was not found at '$propsPath'."
    }

    [xml]$props = Get-Content $propsPath -Raw

    $versionPrefix =
        $props.Project.PropertyGroup.VersionPrefix |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
        Fail "VersionPrefix was not found in Directory.Build.props."
    }

    return $versionPrefix.Trim()
}

Assert-Tool "dotnet"
Assert-Tool "tar"

$SolutionRoot = (Resolve-Path $SolutionRoot).Path

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot =
        Join-Path $SolutionRoot "artifacts\releases"
}

$Version =
    Get-ReleaseVersion -Root $SolutionRoot

$ProjectPath =
    Join-Path $SolutionRoot $Project

if (-not (Test-Path $ProjectPath)) {
    Fail "Project path was not found: '$ProjectPath'."
}

$releaseName =
    "InitiativeTracker-$Version-$Runtime"

$releaseDirectory =
    Join-Path $OutputRoot $releaseName

$workDirectory =
    Join-Path $OutputRoot ".work\$releaseName"

$publishDirectory =
    Join-Path $workDirectory "publish"

if (Test-Path $workDirectory) {
    Remove-Item $workDirectory -Recurse -Force
}

if (Test-Path $releaseDirectory) {
    Remove-Item $releaseDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

Write-Host ""
Write-Host "Publishing InitiativeTracker $Version for $Runtime..." -ForegroundColor Cyan

& dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDirectory

if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish failed."
}

$expectedExecutable =
    Join-Path $publishDirectory "InitiativeTracker.Web"

if (-not (Test-Path $expectedExecutable)) {
    Fail "Published output does not contain InitiativeTracker.Web."
}

$packageFileName =
    "$releaseName.tar.gz"

$packagePath =
    Join-Path $releaseDirectory $packageFileName

Write-Host "Creating $packageFileName..." -ForegroundColor Cyan

Push-Location $publishDirectory

try {
    & tar -czf $packagePath .

    if ($LASTEXITCODE -ne 0) {
        Fail "tar failed while creating the release package."
    }
}
finally {
    Pop-Location
}

$packageInfo =
    Get-Item $packagePath

$hash =
    (Get-FileHash -Path $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

$manifest = [ordered]@{
    formatVersion = 1
    product = "InitiativeTracker"
    version = $Version
    platform = $Runtime
    publishedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    hubProtocolVersion = 1
    healthPath = "/system/health"
    requiresDatabaseBackup = $true
    package = [ordered]@{
        fileName = $packageFileName
        sizeBytes = $packageInfo.Length
        sha256 = $hash
    }
    releaseNotes = @($ReleaseNotes)
}

$manifestFileName =
    "$releaseName.manifest.json"

$manifestPath =
    Join-Path $releaseDirectory $manifestFileName

$manifestJson =
    $manifest |
    ConvertTo-Json -Depth 8

Write-Utf8NoBomFile `
    -Path $manifestPath `
    -Content $manifestJson

Write-Host ""
Write-Host "Release created:" -ForegroundColor Green
Write-Host "  Version : $Version"
Write-Host "  Platform: $Runtime"
Write-Host "  Package : $packagePath"
Write-Host "  Manifest: $manifestPath"
Write-Host "  Size    : $($packageInfo.Length) bytes"
Write-Host "  SHA-256 : $hash"
