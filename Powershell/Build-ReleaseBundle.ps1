param(
    [string]$Version = "",
    [ValidateSet("win-x64", "linux-x64", "linux-arm64")]
    [string]$Platform = "win-x64",
    [ValidateSet("Online", "Offline")]
    [string]$BundleMode = "Online",
    [string]$ImageRepository = "businessentity",
    [string]$OutputRoot = "",
    [switch]$SkipImageBuild,
    [switch]$SkipArchive,
    [switch]$SkipImageSave
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptRoot
$DeploymentRoot = Join-Path $RepoRoot "Deployment"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot "artifacts\dist"
}

function Invoke-RequiredTool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $RepoRoot
    )

    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Command failed: $FilePath $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Resolve-DefaultVersion {
    try {
        $gitVersion = (& git -C $RepoRoot describe --tags --always --dirty 2>$null)
        if (-not [string]::IsNullOrWhiteSpace($gitVersion)) {
            return ($gitVersion.Trim() -replace "[^0-9A-Za-z._-]", "-")
        }
    }
    catch {
    }

    return (Get-Date -Format "yyyyMMddHHmmss")
}

function ConvertTo-ImageTarName {
    param([Parameter(Mandatory = $true)][string]$ImageName)
    return ($ImageName -replace "[/:@]", "_") + ".tar"
}

function Copy-DeploymentAssets {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$BusinessEntityImage,
        [Parameter(Mandatory = $true)][string]$WebLoggerImage,
        [Parameter(Mandatory = $true)][string]$PostgresImage
    )

    foreach ($item in @("docker-compose.yml", ".env.example", "identity.initial.example.json", "identity-secrets.example.env", "install.ps1", "deploy.ps1", "install.bat", "README.md")) {
        Copy-Item -Path (Join-Path $DeploymentRoot $item) -Destination (Join-Path $TargetRoot $item) -Force
    }

    $sourceScripts = Join-Path $DeploymentRoot "scripts"
    $targetScripts = Join-Path $TargetRoot "scripts"
    if (Test-Path $sourceScripts) {
        Copy-Item -Path $sourceScripts -Destination $targetScripts -Recurse -Force
    }

    foreach ($dir in @("images", "storage", "backups", "runtime")) {
        $path = Join-Path $TargetRoot $dir
        if (-not (Test-Path $path)) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }

    $envExamplePath = Join-Path $TargetRoot ".env.example"
    $envText = Get-Content -Path $envExamplePath -Raw
    $envText = $envText.Replace("__BE_IMAGE__", $BusinessEntityImage)
    $envText = $envText.Replace("__WEB_LOGGER_IMAGE__", $WebLoggerImage)
    $envText = $envText.Replace("__POSTGRES_PRODUCTION_IMAGE__", $PostgresImage)
    Set-Content -Path $envExamplePath -Value $envText -Encoding ASCII
}

function Write-ReleaseManifest {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$BusinessEntityImage,
        [Parameter(Mandatory = $true)][string]$WebLoggerImage,
        [Parameter(Mandatory = $true)][string]$PostgresImage
    )

    $manifest = [ordered]@{
        schemaVersion = 1
        kind = "BusinessEntityReleaseBundle"
        version = $Version
        platform = $Platform
        bundleMode = $BundleMode
        createdAtUtc = [DateTime]::UtcNow.ToString("o")
        images = [ordered]@{
            businessEntity = $BusinessEntityImage
            webLogger = $WebLoggerImage
            postgresProduction = $PostgresImage
        }
        entrypoints = @(
            "install.ps1",
            "install.bat",
            "deploy.ps1"
        )
        composeFiles = @(
            "docker-compose.yml"
        )
        notes = @(
            "Docker-compatible runtime is required on the target machine.",
            "Initial application data is owned by application startup/bootstrap.",
            "Offline bundles include images/*.tar and load them during install."
        )
    }

    $json = $manifest | ConvertTo-Json -Depth 8
    Set-Content -Path (Join-Path $TargetRoot "release-manifest.json") -Value $json -Encoding UTF8
}

function Save-OfflineImages {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string[]]$Images
    )

    if ($SkipImageSave) {
        return
    }

    $imagesPath = Join-Path $TargetRoot "images"
    foreach ($image in $Images) {
        Write-Host "Saving image: $image" -ForegroundColor Cyan
        $targetPath = Join-Path $imagesPath (ConvertTo-ImageTarName -ImageName $image)
        Invoke-RequiredTool -FilePath "docker" -Arguments @("save", "-o", $targetPath, $image)
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-DefaultVersion
}

$Version = $Version -replace "[^0-9A-Za-z._-]", "-"
$BusinessEntityImage = "$ImageRepository/business-entity:$Version"
$WebLoggerImage = "$ImageRepository/web-logger:$Version"
$PostgresImage = "$ImageRepository/postgres-production-db:$Version"

if (-not (Test-Path $DeploymentRoot)) {
    throw "Deployment assets folder not found: $DeploymentRoot"
}

New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$BundleName = "BusinessEntity-$Version-$Platform"
if ($BundleMode -eq "Offline") {
    $BundleName += "-offline"
}

$BundleRoot = Join-Path $OutputRoot $BundleName
if (Test-Path $BundleRoot) {
    Remove-Item -Path $BundleRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $BundleRoot -Force | Out-Null

if (-not $SkipImageBuild) {
    Write-Host "Building Docker images for $Version" -ForegroundColor Cyan
    Invoke-RequiredTool -FilePath "docker" -Arguments @("build", "-t", $BusinessEntityImage, "-f", "BusinessEntity/Dockerfile", ".")
    Invoke-RequiredTool -FilePath "docker" -Arguments @("build", "-t", $WebLoggerImage, "-f", "BlazorServerWebLogger/Dockerfile", ".")
    Invoke-RequiredTool -FilePath "docker" -Arguments @("build", "-t", $PostgresImage, "-f", "Dockerfile", ".") -WorkingDirectory (Join-Path $RepoRoot "postgres_db")
}

Copy-DeploymentAssets `
    -TargetRoot $BundleRoot `
    -BusinessEntityImage $BusinessEntityImage `
    -WebLoggerImage $WebLoggerImage `
    -PostgresImage $PostgresImage

Write-ReleaseManifest `
    -TargetRoot $BundleRoot `
    -BusinessEntityImage $BusinessEntityImage `
    -WebLoggerImage $WebLoggerImage `
    -PostgresImage $PostgresImage

if ($BundleMode -eq "Offline") {
    Write-Host "Preparing offline Docker image tars" -ForegroundColor Cyan
    Save-OfflineImages -TargetRoot $BundleRoot -Images @(
        $BusinessEntityImage,
        $WebLoggerImage,
        $PostgresImage
    )
}

if (-not $SkipArchive) {
    if ($Platform -eq "win-x64") {
        $archivePath = Join-Path $OutputRoot "$BundleName.zip"
        if (Test-Path $archivePath) {
            Remove-Item -Path $archivePath -Force
        }

        Compress-Archive -Path $BundleRoot -DestinationPath $archivePath -Force
        Write-Host "Created archive: $archivePath" -ForegroundColor Green
    }
    else {
        $archivePath = Join-Path $OutputRoot "$BundleName.tar.gz"
        if (Test-Path $archivePath) {
            Remove-Item -Path $archivePath -Force
        }

        Invoke-RequiredTool -FilePath "tar" -Arguments @("-czf", $archivePath, "-C", $OutputRoot, $BundleName)
        Write-Host "Created archive: $archivePath" -ForegroundColor Green
    }
}

Write-Host "Bundle directory: $BundleRoot" -ForegroundColor Green
