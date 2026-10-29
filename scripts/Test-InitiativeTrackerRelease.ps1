param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)

$ErrorActionPreference = "Stop"

function Add-ErrorMessage(
    [System.Collections.Generic.List[string]]$Errors,
    [string]$Message) {

    $Errors.Add($Message)
}

function Test-SemVer([string]$Value) {
    return $Value -match '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$'
}

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest was not found: '$ManifestPath'."
}

$ManifestPath =
    (Resolve-Path $ManifestPath).Path

$releaseDirectory =
    Split-Path $ManifestPath -Parent

try {
    $manifest =
        Get-Content $ManifestPath -Raw |
        ConvertFrom-Json
}
catch {
    throw "Manifest JSON could not be parsed: $($_.Exception.Message)"
}

$errors =
    [System.Collections.Generic.List[string]]::new()

if ($manifest.formatVersion -ne 1) {
    Add-ErrorMessage $errors "Unsupported manifest format version '$($manifest.formatVersion)'."
}

if ($manifest.product -ne "InitiativeTracker") {
    Add-ErrorMessage $errors "Manifest product must be 'InitiativeTracker'."
}

if (-not (Test-SemVer ([string]$manifest.version))) {
    Add-ErrorMessage $errors "Manifest version is not valid semantic version syntax."
}

if ($manifest.platform -ne "linux-arm64") {
    Add-ErrorMessage $errors "Unsupported platform '$($manifest.platform)'."
}

if ($manifest.healthPath -ne "/system/health") {
    Add-ErrorMessage $errors "Health path must be '/system/health'."
}

$packageFileName =
    [string]$manifest.package.fileName

if ([string]::IsNullOrWhiteSpace($packageFileName)) {
    Add-ErrorMessage $errors "Package file name is missing."
}
elseif ([IO.Path]::GetFileName($packageFileName) -ne $packageFileName -or
        $packageFileName.Contains("..")) {
    Add-ErrorMessage $errors "Package file name contains a path or traversal sequence."
}

$packagePath =
    Join-Path $releaseDirectory $packageFileName

if (-not (Test-Path $packagePath)) {
    Add-ErrorMessage $errors "Package file was not found beside the manifest."
}
else {
    $packageInfo =
        Get-Item $packagePath

    if ($packageInfo.Length -ne [long]$manifest.package.sizeBytes) {
        Add-ErrorMessage $errors "Package byte size does not match the manifest."
    }

    $actualHash =
        (Get-FileHash -Path $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()

    $expectedHash =
        ([string]$manifest.package.sha256).ToLowerInvariant()

    if ($actualHash -ne $expectedHash) {
        Add-ErrorMessage $errors "Package SHA-256 does not match the manifest."
    }

    $entries =
        @(& tar -tzf $packagePath)

    if ($LASTEXITCODE -ne 0) {
        Add-ErrorMessage $errors "Package archive could not be inspected."
    }
    else {
        if ($entries.Count -eq 0) {
            Add-ErrorMessage $errors "Package archive is empty."
        }

        $foundExecutable = $false

        foreach ($rawEntry in $entries) {
            $entry =
                ([string]$rawEntry).Replace("\", "/").Trim()

            if ([string]::IsNullOrWhiteSpace($entry)) {
                continue
            }

            if ($entry.StartsWith("/") -or
                $entry -match '^[A-Za-z]:' -or
                ($entry.Split("/", [System.StringSplitOptions]::RemoveEmptyEntries) -contains "..")) {

                Add-ErrorMessage $errors "Unsafe archive path found: '$entry'."
                continue
            }

            if ($entry.StartsWith("./")) {
                $entry = $entry.Substring(2)
            }

            if ($entry -eq "InitiativeTracker.Web") {
                $foundExecutable = $true
            }
        }

        if (-not $foundExecutable) {
            Add-ErrorMessage $errors "Package does not contain InitiativeTracker.Web at the archive root."
        }
    }
}

Write-Host ""

if ($errors.Count -gt 0) {
    Write-Host "Release validation FAILED" -ForegroundColor Red

    foreach ($errorMessage in $errors) {
        Write-Host "  - $errorMessage" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Release validation PASSED" -ForegroundColor Green
Write-Host "  Version : $($manifest.version)"
Write-Host "  Platform: $($manifest.platform)"
Write-Host "  Package : $packageFileName"
Write-Host "  SHA-256 : $($manifest.package.sha256)"

exit 0
