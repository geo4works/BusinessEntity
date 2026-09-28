param(
    [switch]$NoStart,
    [switch]$SkipImageLoad,
    [switch]$SkipHealthWait
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$EnvPath = Join-Path $Root ".env"
$EnvExamplePath = Join-Path $Root ".env.example"
$ComposePath = Join-Path $Root "docker-compose.yml"
$ImagesPath = Join-Path $Root "images"
$InitialDataScript = Join-Path $Root "scripts/bootstrap-initial-data.ps1"

function Invoke-RequiredTool {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $FilePath $($Arguments -join ' ')"
    }
}

function Test-Tool {
    param([Parameter(Mandatory = $true)][string]$Name)
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    return $null -ne $command
}

function Get-RandomHex {
    param([int]$ByteCount = 32)
    $bytes = New-Object byte[] $ByteCount
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return -join ($bytes | ForEach-Object { $_.ToString("x2") })
}

function Read-DotEnv {
    param([Parameter(Mandatory = $true)][string]$Path)
    $map = [ordered]@{}
    if (-not (Test-Path $Path)) {
        return $map
    }

    foreach ($line in Get-Content -Path $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#")) {
            continue
        }

        $idx = $trimmed.IndexOf("=")
        if ($idx -lt 1) {
            continue
        }

        $key = $trimmed.Substring(0, $idx).Trim()
        $value = $trimmed.Substring($idx + 1).Trim()
        $map[$key] = $value
    }

    return $map
}

function Write-DotEnv {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Map
    )

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($key in $Map.Keys) {
        $lines.Add("$key=$($Map[$key])") | Out-Null
    }

    $lines | Set-Content -Path $Path -Encoding ASCII
}

function Initialize-EnvFile {
    if (-not (Test-Path $EnvPath)) {
        if (-not (Test-Path $EnvExamplePath)) {
            throw ".env.example not found: $EnvExamplePath"
        }

        Copy-Item -Path $EnvExamplePath -Destination $EnvPath -Force
        Write-Host "Created .env from .env.example" -ForegroundColor Green
    }

    $envMap = Read-DotEnv -Path $EnvPath
    foreach ($key in @("BE_DB_PASSWORD", "AUTHENTIK_PG_PASS", "AUTHENTIK_SECRET_KEY", "AUTHENTIK_BOOTSTRAP_TOKEN", "AUTHENTIK_CLIENT_SECRET")) {
        if (-not $envMap.Contains($key) -or [string]::IsNullOrWhiteSpace($envMap[$key]) -or $envMap[$key] -eq "__GENERATE__") {
            $envMap[$key] = Get-RandomHex -ByteCount 32
            Write-Host "Generated $key" -ForegroundColor DarkGreen
        }
    }

    if (-not $envMap.Contains("AUTHENTIK_REDIRECT_URIS") -or [string]::IsNullOrWhiteSpace($envMap["AUTHENTIK_REDIRECT_URIS"])) {
        $port = if ($envMap.Contains("BUSINESSENTITY_HTTP_PORT")) { $envMap["BUSINESSENTITY_HTTP_PORT"] } else { "7000" }
        $envMap["AUTHENTIK_REDIRECT_URIS"] = "http://localhost:$port/auth/callback"
    }

    if (-not $envMap.Contains("AUTHENTIK_BASE_URL_FOR_BROWSER") -or [string]::IsNullOrWhiteSpace($envMap["AUTHENTIK_BASE_URL_FOR_BROWSER"])) {
        $port = if ($envMap.Contains("AUTHENTIK_HTTP_PORT")) { $envMap["AUTHENTIK_HTTP_PORT"] } else { "9000" }
        $envMap["AUTHENTIK_BASE_URL_FOR_BROWSER"] = "http://localhost:$port"
    }

    Write-DotEnv -Path $EnvPath -Map $envMap
}

function Ensure-Directories {
    foreach ($relative in @(
        "storage",
        "backups",
        "runtime",
        "runtime/authentik",
        "runtime/authentik/data",
        "runtime/authentik/certs",
        "runtime/authentik/custom-templates"
    )) {
        $path = Join-Path $Root $relative
        if (-not (Test-Path $path)) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }
}

function Ensure-DockerNetwork {
    $envMap = Read-DotEnv -Path $EnvPath
    $networkName = if ($envMap.Contains("DOCKER_NETWORK_NAME")) { $envMap["DOCKER_NETWORK_NAME"] } else { "docker-business-entity-common-bridge" }
    $existing = docker network ls --format "{{.Name}}" | Where-Object { $_ -eq $networkName }
    if (-not $existing) {
        Invoke-RequiredTool -FilePath "docker" -Arguments @("network", "create", $networkName)
        Write-Host "Created Docker network: $networkName" -ForegroundColor Green
    }
}

function Import-OfflineImages {
    if ($SkipImageLoad) {
        return
    }

    if (-not (Test-Path $ImagesPath)) {
        return
    }

    $imageTars = Get-ChildItem -Path $ImagesPath -Filter "*.tar" -File | Sort-Object Name
    foreach ($imageTar in $imageTars) {
        Write-Host "Loading Docker image: $($imageTar.Name)" -ForegroundColor Cyan
        Invoke-RequiredTool -FilePath "docker" -Arguments @("load", "-i", $imageTar.FullName)
    }
}

function Wait-HttpOk {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Host "Ready: $Url -> $($response.StatusCode)" -ForegroundColor Green
                return
            }
        }
        catch {
            Start-Sleep -Seconds 3
        }
    }

    throw "Health wait timed out: $Url"
}

if (-not (Test-Tool -Name "docker")) {
    throw "Docker CLI was not found. Install Docker Desktop, Docker Engine, or another compatible runtime."
}

Invoke-RequiredTool -FilePath "docker" -Arguments @("compose", "version")

if (-not (Test-Path $ComposePath)) {
    throw "docker-compose.yml not found: $ComposePath"
}

Initialize-EnvFile
Ensure-Directories
Ensure-DockerNetwork
Import-OfflineImages

Push-Location $Root
try {
    Invoke-RequiredTool -FilePath "docker" -Arguments @("compose", "--env-file", ".env", "-f", "docker-compose.yml", "config", "--quiet")

    if (-not $NoStart) {
        Invoke-RequiredTool -FilePath "docker" -Arguments @("compose", "--env-file", ".env", "-f", "docker-compose.yml", "up", "-d")
    }
}
finally {
    Pop-Location
}

if (-not $NoStart -and -not $SkipHealthWait) {
    $envMap = Read-DotEnv -Path $EnvPath
    $appPort = if ($envMap.Contains("BUSINESSENTITY_HTTP_PORT")) { $envMap["BUSINESSENTITY_HTTP_PORT"] } else { "7000" }
    $loggerPort = if ($envMap.Contains("WEB_LOGGER_HTTP_PORT")) { $envMap["WEB_LOGGER_HTTP_PORT"] } else { "5080" }
    $authPort = if ($envMap.Contains("AUTHENTIK_HTTP_PORT")) { $envMap["AUTHENTIK_HTTP_PORT"] } else { "9000" }

    Wait-HttpOk -Url "http://localhost:$appPort" -TimeoutSeconds 180
    Wait-HttpOk -Url "http://localhost:$loggerPort" -TimeoutSeconds 120
    Wait-HttpOk -Url "http://localhost:$authPort/-/health/live/" -TimeoutSeconds 180
}

if (-not $NoStart -and (Test-Path $InitialDataScript)) {
    & $InitialDataScript -SkipHealthWait
}

Write-Host ""
Write-Host "BusinessEntity installation script completed." -ForegroundColor Green
Write-Host "Application: http://localhost:$((Read-DotEnv -Path $EnvPath)["BUSINESSENTITY_HTTP_PORT"])" -ForegroundColor Cyan
Write-Host "Web logger:  http://localhost:$((Read-DotEnv -Path $EnvPath)["WEB_LOGGER_HTTP_PORT"])" -ForegroundColor Cyan
Write-Host "Authentik:   http://localhost:$((Read-DotEnv -Path $EnvPath)["AUTHENTIK_HTTP_PORT"])" -ForegroundColor Cyan
Write-Host ""
Write-Host "Application startup configures Authentik OIDC, groups and initial users. Initial logins: akadmin / akadmin and admin / admin." -ForegroundColor Yellow
