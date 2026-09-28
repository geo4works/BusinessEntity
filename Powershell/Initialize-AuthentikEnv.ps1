param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Reads existing settings without printing credentials.
function Read-Settings {
    param([string]$Path)
    $settings = [ordered]@{}
    if (Test-Path $Path) {
        foreach ($line in Get-Content $Path) {
            if ($line -match '^\s*([^#=\s]+)\s*=(.*)$') {
                $settings[$Matches[1]] = $Matches[2].Trim()
            }
        }
    }
    return $settings
}

# Generates a secret compatible with dotenv and PowerShell 5.1/7.
function New-Secret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return -join ($bytes | ForEach-Object { $_.ToString("x2") })
}

$envPath = Join-Path $RepositoryRoot ".env"
$authPath = Join-Path $RepositoryRoot "Authentic/.env"
$settings = Read-Settings $envPath
$authSettings = Read-Settings $authPath

# Keeps credentials of an existing installation when adding bootstrap settings.
foreach ($key in $authSettings.Keys) {
    if (-not $settings.Contains($key) -or [string]::IsNullOrWhiteSpace($settings[$key])) {
        $settings[$key] = $authSettings[$key]
    }
}

$defaults = [ordered]@{
    PG_DB = "authentik"
    PG_USER = "authentik"
    COMPOSE_PORT_HTTP = "9000"
    COMPOSE_PORT_HTTPS = "9443"
    AUTHENTIK_BOOTSTRAP_EMAIL = "akadmin@businessentity.local"
    AUTHENTIK_BOOTSTRAP_PASSWORD = "akadmin"
    AUTHENTIK_CLIENT_ID = "business-entity"
    ENSURE_AUTHENTIK_ON_STARTUP = "true"
}
foreach ($key in $defaults.Keys) {
    if (-not $settings.Contains($key) -or [string]::IsNullOrWhiteSpace($settings[$key])) {
        $settings[$key] = $defaults[$key]
    }
}
foreach ($key in @("PG_PASS", "AUTHENTIK_SECRET_KEY", "AUTHENTIK_BOOTSTRAP_TOKEN", "AUTHENTIK_CLIENT_SECRET")) {
    if (-not $settings.Contains($key) -or [string]::IsNullOrWhiteSpace($settings[$key]) -or $settings[$key] -eq "__GENERATE__") {
        $settings[$key] = New-Secret
        Write-Host "Generated $key"
    }
}

# Writes root interpolation settings; existing passwords and tokens remain stable.
$lines = foreach ($key in $settings.Keys) { "$key=$($settings[$key])" }
$lines | Set-Content -Path $envPath -Encoding UTF8
if (-not (Test-Path $authPath)) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $authPath) | Out-Null
    $authLines = foreach ($key in @("PG_DB", "PG_USER", "PG_PASS", "AUTHENTIK_SECRET_KEY")) {
        "$key=$($settings[$key])"
    }
    $authLines | Set-Content -Path $authPath -Encoding UTF8
}
Write-Host "Authentik environment is ready. Application startup configures OIDC and initial users."
