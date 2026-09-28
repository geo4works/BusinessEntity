# Управление Docker-контейнерами
# 
# ВАЖНО: Пункт 30 и специализированные функции (31, 32, 33) используют БЕЗОПАСНУЮ пересборку
# которая НЕ нарушает сети других контейнеров. Это решает проблему с отвалом логгера
# при пересборке business-entity контейнера.
#
# Безопасная пересборка:
# 1. Запоминает все сети контейнера перед удалением
# 2. Пересобирает только выбранный контейнер
# 3. Подключает его обратно к тем же сетям
# 4. НЕ затрагивает другие контейнеры и их сетевые подключения
#
# Массовое переподключение (AjustNetworks) вызывается только через пункт 03

. "$PSScriptRoot\DockerNetworkInfo.ps1"
# . "$PSScriptRoot\AutoConnectContainers.ps1"  # Удалено, больше не требуется

# Функция для создания сети только если она не существует
function Ensure-Network {
    param([Parameter(Mandatory=$true)][string]$NetworkName)
    $exists = docker network ls --format '{{.Name}}' | Where-Object { $_ -eq $NetworkName }
    if (-not $exists) {
        Write-Host "Создаю сеть '$NetworkName'..." -ForegroundColor Yellow
        docker network create $NetworkName | Out-Null
        Write-Host "✓ Сеть '$NetworkName' создана." -ForegroundColor Green
    } else {
        Write-Host "Сеть '$NetworkName' уже существует." -ForegroundColor DarkGray
    }
}

# Простой импорт переменных из .env в переменные окружения текущего процесса (PS 5.1 совместимо)
function Import-DotEnv {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (-not (Test-Path -Path $Path)) { return }
    $lines = Get-Content -Path $Path
    foreach ($line in $lines) {
        if (-not $line) { continue }
        $trim = $line.Trim()
        if ($trim -eq '' -or $trim.StartsWith('#')) { continue }
        $idx = $trim.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trim.Substring(0, $idx).Trim()
        $val = $trim.Substring($idx + 1).Trim()
        if ($val.StartsWith('"') -and $val.EndsWith('"') -and $val.Length -ge 2) { $val = $val.Substring(1, $val.Length - 2) }
        if ($val.StartsWith("'") -and $val.EndsWith("'") -and $val.Length -ge 2) { $val = $val.Substring(1, $val.Length - 2) }
        Set-Item -Path "Env:$key" -Value $val
    }
}

# Прочитать .env в Hashtable (PS 5.1 совместимо)
function Read-DotEnvAsHashtable {
    param([Parameter(Mandatory=$true)][string]$Path)
    $map = @{}
    if (-not (Test-Path -Path $Path)) { return $map }
    $lines = Get-Content -Path $Path
    foreach ($line in $lines) {
        if (-not $line) { continue }
        $trim = $line.Trim()
        if ($trim -eq '' -or $trim.StartsWith('#')) { continue }
        $idx = $trim.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trim.Substring(0, $idx).Trim()
        $val = $trim.Substring($idx + 1).Trim()
        if ($val.StartsWith('"') -and $val.EndsWith('"') -and $val.Length -ge 2) { $val = $val.Substring(1, $val.Length - 2) }
        if ($val.StartsWith("'") -and $val.EndsWith("'") -and $val.Length -ge 2) { $val = $val.Substring(1, $val.Length - 2) }
        $map[$key] = $val
    }
    return $map
}

# Записать Hashtable в .env файл (без кавычек)
function Write-DotEnvFromHashtable {
    param(
        [Parameter(Mandatory=$true)][string]$Path,
        [Parameter(Mandatory=$true)][hashtable]$Data
    )
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($k in ($Data.Keys | Sort-Object)) {
        $v = [string]$Data[$k]
        $lines.Add("$k=$v") | Out-Null
    }
    $lines | Set-Content -Path $Path -Encoding ASCII -Force
}

# Функция для получения списка сетей контейнера
function Get-ContainerNetworks {
    param([Parameter(Mandatory=$true)][string]$ContainerName)

    try {
        $networksJson = docker inspect --format "{{json .NetworkSettings.Networks}}" $ContainerName 2>$null | ConvertFrom-Json
        if (-not $networksJson) { return @() }

        $result = @()
        foreach ($p in $networksJson.PSObject.Properties) {
            $netName = $p.Name
            $aliases = @()
            if ($p.Value -and $p.Value.Aliases) { $aliases = @($p.Value.Aliases) }
            $result += [pscustomobject]@{ Name = $netName; Aliases = $aliases }
        }
        return $result
    } catch {
        Write-Host "Контейнер '$ContainerName' не найден, будет использована fallback сеть." -ForegroundColor DarkGray
        return @()
    }
}

# Основная функция для безопасной пересборки контейнера с сохранением сетей
function Rebuild-And-Restart-ContainerKeepNetworks {
    param(
        [Parameter(Mandatory=$true)][string]$ContainerName,
        [Parameter(Mandatory=$true)][string]$ImageName,
        [Parameter(Mandatory=$true)][string]$DockerfilePath,
        [Parameter(Mandatory=$true)][string]$BuildContext,
        [string[]]$Ports = @(),              # пример: @("5000:80")
        [hashtable]$Env = @{},               # пример: @{ "ASPNETCORE_ENVIRONMENT"="Development" }
        [string]$FallbackNetwork = "docker-business-entity-common-bridge"
    )

    Write-Host "=== Безопасная пересборка контейнера '$ContainerName' ===" -ForegroundColor Cyan

    # 1) Запоминаем сети контейнера (если он существует)
    Write-Host "Запоминаю сети контейнера..." -ForegroundColor Yellow
    $nets = Get-ContainerNetworks -ContainerName $ContainerName
    if ($nets.Count -gt 0) {
        Write-Host "Найдены сети:" -ForegroundColor Green
        foreach ($n in $nets) {
            $aliasesStr = if ($n.Aliases) { "aliases: $($n.Aliases -join ',')" } else { "без aliases" }
            Write-Host "  - $($n.Name) ($aliasesStr)" -ForegroundColor Green
        }
    } else {
        Write-Host "Контейнер не найден или не подключен к сетям." -ForegroundColor DarkGray
    }

    # 2) Останавливаем/удаляем контейнер
    Write-Host "Останавливаю и удаляю контейнер..." -ForegroundColor Yellow
    docker rm -f $ContainerName 2>$null | Out-Null

    # 3) Собираем образ
    Write-Host "Собираю образ '$ImageName'..." -ForegroundColor Yellow
    docker build -t $ImageName -f $DockerfilePath $BuildContext
    if ($LASTEXITCODE -ne 0) {
        throw "Ошибка сборки Docker образа"
    }

    # 4) Определяем первичную сеть
    $primaryNet = if ($nets.Count -gt 0) { $nets[0].Name } else { $FallbackNetwork }
    Write-Host "Первичная сеть: $primaryNet" -ForegroundColor Green
    Ensure-Network -NetworkName $primaryNet

    # 5) Формируем параметры docker run
    $runArgs = @("run", "-d", "--name", $ContainerName, "--network", $primaryNet)

    foreach ($p in $Ports) { $runArgs += @("-p", $p) }
    foreach ($k in $Env.Keys) { $runArgs += @("-e", "$k=$($Env[$k])") }

    $runArgs += $ImageName

    # 6) Запускаем контейнер в первичной сети
    Write-Host "Запускаю контейнер в сети '$primaryNet'..." -ForegroundColor Yellow
    $proc = Start-Process -FilePath 'docker' -ArgumentList $runArgs -NoNewWindow -PassThru -Wait
    if ($proc.ExitCode -ne 0) {
        throw "Ошибка запуска контейнера"
    }

    # 7) Возвращаем контейнер в остальные сети (только этот контейнер, не массово)
    if ($nets.Count -gt 1) {
        Write-Host "Подключаю к дополнительным сетям..." -ForegroundColor Yellow
        foreach ($n in $nets | Where-Object { $_.Name -ne $primaryNet }) {
            Write-Host "  Подключаю к сети '$($n.Name)'" -ForegroundColor DarkGray
            $connectArgs = @("network", "connect")
            # если были алиасы — восстановим
            if ($n.Aliases -and $n.Aliases.Count -gt 0) {
                foreach ($a in $n.Aliases) { 
                    if ($a -ne $ContainerName) { # не дублируем имя контейнера
                        $connectArgs += @("--alias", $a) 
                    }
                }
            }
            $connectArgs += @($n.Name, $ContainerName)
            $proc2 = Start-Process -FilePath 'docker' -ArgumentList $connectArgs -NoNewWindow -PassThru -Wait
            if ($proc2.ExitCode -eq 0) {
                Write-Host "    ✓ Подключен к '$($n.Name)'" -ForegroundColor Green
            } else {
                Write-Host "    ⚠ Ошибка подключения к '$($n.Name)'" -ForegroundColor Yellow
            }
        }
    }

    # 8) Получаем финальный IP
    $containerIP = docker inspect -f '{{range.NetworkSettings.Networks}}{{.IPAddress}}{{end}}' $ContainerName
    Write-Host "✓ Контейнер '$ContainerName' пересобран и запущен. IP: $containerIP" -ForegroundColor Green
    Write-Host "✓ Сети других контейнеров НЕ затронуты." -ForegroundColor Green
}

# Специализированная функция для Business Entity
function Rebuild-BusinessEntity-Safe {
    $container = "business-entity-container"
    $image = "business-entity-container_image:latest"
    $dockerfile = Join-Path (Split-Path $PSScriptRoot -Parent) "BusinessEntity\Dockerfile"
    $context = Split-Path $PSScriptRoot -Parent

    # Порты и переменные окружения
    $ports = @("7000:80")
    $env = @{ 
        "ASPNETCORE_URLS" = "http://*:80"
        "ASPNETCORE_ENVIRONMENT" = "Development"
        "IS_DOCKER" = "true"
    }

    Rebuild-And-Restart-ContainerKeepNetworks `
        -ContainerName $container `
        -ImageName $image `
        -DockerfilePath $dockerfile `
        -BuildContext $context `
        -Ports $ports `
        -Env $env `
        -FallbackNetwork "docker-business-entity-common-bridge"
        
    # ВАЖНО: Перезапускаем логгер для обновления пула соединений с БД
    Write-Host "Перезапускаю web-logger для обновления соединений с БД..." -ForegroundColor Yellow
    docker restart web_logger-container 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Web-logger перезапущен для обновления соединений." -ForegroundColor Green
    } else {
        Write-Host "⚠ Не удалось перезапустить web-logger. Сделайте это вручную через пункт 32." -ForegroundColor Yellow
    }
}

# Специализированная функция для Web Logger
function Rebuild-WebLogger-Safe {
    $container = "web_logger-container"
    $image = "web_logger-container_image:latest"
    $dockerfile = Join-Path (Split-Path $PSScriptRoot -Parent) "BlazorServerWebLogger\Dockerfile"
    $context = Split-Path $PSScriptRoot -Parent

    # Порты и переменные окружения
    $ports = @("5080:80")
    $env = @{ 
        "ASPNETCORE_URLS" = "http://*:80"
        "ASPNETCORE_ENVIRONMENT" = "Development"
        "IS_DOCKER" = "true"
    }

    Rebuild-And-Restart-ContainerKeepNetworks `
        -ContainerName $container `
        -ImageName $image `
        -DockerfilePath $dockerfile `
        -BuildContext $context `
        -Ports $ports `
        -Env $env `
        -FallbackNetwork "docker-business-entity-common-bridge"
}

# Специализированная функция для Admin Node
function Rebuild-AdminNode-Safe {
    $container = "admin-node"
    $image = "admin-node_image:latest"
    $dockerfile = Join-Path (Split-Path $PSScriptRoot -Parent) "ControlNode\Dockerfile"
    $context = Split-Path $PSScriptRoot -Parent

    # Порты и переменные окружения
    $ports = @("5020:80")
    $env = @{ 
        "ASPNETCORE_URLS" = "http://*:80"
        "ASPNETCORE_ENVIRONMENT" = "Development"
        "IS_DOCKER" = "true"
    }

    Rebuild-And-Restart-ContainerKeepNetworks `
        -ContainerName $container `
        -ImageName $image `
        -DockerfilePath $dockerfile `
        -BuildContext $context `
        -Ports $ports `
        -Env $env `
        -FallbackNetwork "docker-business-entity-common-bridge"
}

# Список контейнеров
$containers = @(
    [PSCustomObject]@{
        Name = "admin-node"
        PortInt = 80
        PortExt = 5020
        ProjectPath = "C:\Develop\BusinessEntity\ControlNode"
        ContextPath = "C:\Develop\BusinessEntity"
        LogPath = "C:\Develop\Logs\"
    },
    [PSCustomObject]@{
        Name = "business-entity-container"
        PortInt = 80
        PortExt = 7000
        ProjectPath = "C:\Develop\BusinessEntity\BusinessEntity"
        ContextPath = "C:\Develop\BusinessEntity"
        LogPath = "C:\Develop\Logs\"
    },
    [PSCustomObject]@{
        Name = "web_logger-container"
        PortInt = 80
        PortExt = 5080
        ProjectPath = "C:\Develop\BusinessEntity\BlazorServerWebLogger"
        ContextPath = "C:\Develop\BusinessEntity"
        LogPath = "C:\Develop\Logs\"
    },
    [PSCustomObject]@{
        Name = "postgres-production-db"
        PortInt = 5432
        PortExt = 5470
        DockerfilePath = "C:\Develop\BusinessEntity\postgres_db\Dockerfile"
        EnvDbName = "main"
        EnvDbUser = "adm01"
        EnvDbPassword = "adm01pwd"
        LogPath = "C:\Develop\Logs\PostgresAssort"
    }
)

function Select-Container {
    Write-Host "Выберите контейнер:" -ForegroundColor Cyan
    $i = 1
    foreach ($container in $containers) {
        Write-Host "$i. $($container.Name) (Port: $($container.PortExt))"
        $i++
    }
    $choice = Read-Host "Введите номер контейнера"
    return $containers[$choice - 1]
}

function GetContainerByName {
    param (
        [string]$containerName
    )

    # Поиск контейнера по имени
    $container = $containers | Where-Object { $_.Name -eq $containerName }

    if ($null -eq $container) {
        Write-Host "Контейнер с именем '$containerName' не найден." -ForegroundColor Red
        return $null
    }

    Write-Host "Контейнер '$containerName' найден." -ForegroundColor Green
    return $container
}

function Action10 {
    $container = Select-Container
    if ($null -eq $container) {
        Write-Host "Неверный выбор" -ForegroundColor Red
        return
    }
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "docker exec -it $($container.Name) sh"
}

function Action20 {
    $container = Select-Container
    if ($null -eq $container) {
        Write-Host "Неверный выбор" -ForegroundColor Red
        return
    }
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "docker exec -it $($container.Name) /bin/bash -c 'apt-get update && apt-get install -y procps curl net-tools mc && echo && ps aux | grep -v /bin/bash && echo && (curl -s http://localhost:80 || echo CURLfailed) && echo && netstat -tuln'"
}

function BuildAndRunBusinessLogicContainers {
    Write-Host "=== Безопасная пересборка всех бизнес-контейнеров ===" -ForegroundColor Cyan
    
    # Сначала убеждаемся что база данных запущена
    Write-Host "Проверяю и запускаю production_db..." -ForegroundColor Yellow
    BuildAndRunPostgresContainer
    Start-Sleep -Seconds 3
    
    # Пересобираем контейнеры безопасно
    Write-Host "Пересобираю admin-node..." -ForegroundColor Yellow
    Rebuild-AdminNode-Safe
    Start-Sleep -Seconds 2
    
    Write-Host "Пересобираю business-entity..." -ForegroundColor Yellow  
    Rebuild-BusinessEntity-Safe
    Start-Sleep -Seconds 2
    
    Write-Host "Пересобираю web-logger..." -ForegroundColor Yellow
    Rebuild-WebLogger-Safe
    
    Write-Host "✓ Все бизнес-контейнеры пересобраны безопасно!" -ForegroundColor Green
}

function SelectContainerAndRun {
    $container = Select-Container
    if ($null -eq $container) {
        Write-Host "Неверный выбор контейнера" -ForegroundColor Red
        return $null
    }
    
    # Используем безопасную пересборку для всех контейнеров
    Write-Host "Использую безопасную пересборку для '$($container.Name)'..." -ForegroundColor Cyan
    
    # Определяем параметры для каждого типа контейнера
    $dockerfile = ""
    $context = ""
    $ports = @()
    $env = @{}
    $imageName = ""
    
    switch ($container.Name) {
        "business-entity-container" {
            $dockerfile = Join-Path (Split-Path $PSScriptRoot -Parent) "BusinessEntity\Dockerfile"
            $context = Split-Path $PSScriptRoot -Parent
            $ports = @("$($container.PortExt):$($container.PortInt)")
            $env = @{ 
                "ASPNETCORE_URLS" = "http://*:80"
                "ASPNETCORE_ENVIRONMENT" = "Development"
                "IS_DOCKER" = "true"
            }
            $imageName = "business-entity-container_image:latest"
            
            # Пересобираем business-entity
            Rebuild-And-Restart-ContainerKeepNetworks `
                -ContainerName $container.Name `
                -ImageName $imageName `
                -DockerfilePath $dockerfile `
                -BuildContext $context `
                -Ports $ports `
                -Env $env `
                -FallbackNetwork "docker-business-entity-common-bridge"
                
            # Перезапускаем логгер для обновления соединений с БД
            Write-Host "Перезапускаю web-logger для обновления соединений с БД..." -ForegroundColor Yellow
            docker restart web_logger-container 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ Web-logger перезапущен для обновления соединений." -ForegroundColor Green
            } else {
                Write-Host "⚠ Не удалось перезапустить web-logger. Сделайте это вручно через пункт 32." -ForegroundColor Yellow
            }
            return
        }
        "web_logger-container" {
            $dockerfile = Join-Path (Split-Path $PSScriptRoot -Parent) "BlazorServerWebLogger\Dockerfile"
            $context = Split-Path $PSScriptRoot -Parent
            $ports = @("$($container.PortExt):$($container.PortInt)")
            $env = @{ 
                "ASPNETCORE_URLS" = "http://*:80"
                "ASPNETCORE_ENVIRONMENT" = "Development"
                "IS_DOCKER" = "true"
            }
            $imageName = "web_logger-container_image:latest"
            
            # Пересобираем web-logger
            Rebuild-And-Restart-ContainerKeepNetworks `
                -ContainerName $container.Name `
                -ImageName $imageName `
                -DockerfilePath $dockerfile `
                -BuildContext $context `
                -Ports $ports `
                -Env $env `
                -FallbackNetwork "docker-business-entity-common-bridge"
            return
        }
        "admin-node" {
            $dockerfile = Join-Path (Split-Path $PSScriptRoot -Parent) "ControlNode\Dockerfile"
            $context = Split-Path $PSScriptRoot -Parent
            $ports = @("$($container.PortExt):$($container.PortInt)")
            $env = @{ 
                "ASPNETCORE_URLS" = "http://*:80"
                "ASPNETCORE_ENVIRONMENT" = "Development"
                "IS_DOCKER" = "true"
            }
            $imageName = "admin-node_image:latest"
            
            # Пересобираем admin-node
            Rebuild-And-Restart-ContainerKeepNetworks `
                -ContainerName $container.Name `
                -ImageName $imageName `
                -DockerfilePath $dockerfile `
                -BuildContext $context `
                -Ports $ports `
                -Env $env `
                -FallbackNetwork "docker-business-entity-common-bridge"
            return
        }
        "postgres-production-db" {
            # Для Postgres используем специальную функцию
            BuildAndRunPostgresContainer
            return
        }
        default {
            Write-Host "❌ Неизвестный контейнер '$($container.Name)'!" -ForegroundColor Red
            Write-Host "Поддерживаются только: business-entity-container, web_logger-container, admin-node, postgres-production-db" -ForegroundColor Yellow
            Write-Host "Используйте пункты меню 31, 32, 33 для конкретных контейнеров." -ForegroundColor Yellow
            return
        }
    }
    
    # AjustNetworks вызывается только вручную через пункт меню 03
}
function BuildAndRunPostgresContainer {
    param(
        [string]$Network    = 'docker-business-entity-common-bridge',
        [string]$DataVolume = 'pgdata',
        [string]$ImageTag   = 'latest'
    )

    # найдём наш контейнер
    $c = $containers | Where-Object Name -eq 'postgres-production-db'
    if (-not $c) {
        Write-Error "Конфиг для 'postgres-production-db' не найден."
        return
    }

    # удаляем старый контейнер (том останется нетронутым)
    docker rm -f $c.Name --volumes | Out-Null

    # проверяем, что Dockerfile действительно есть
    if (-not (Test-Path $c.DockerfilePath)) {
        Write-Error "Dockerfile не найден по пути: $($c.DockerfilePath)"
        return
    }

    # контекст сборки — папка, где лежит Dockerfile
    $ctx = Split-Path -Parent $c.DockerfilePath

    # собираем образ
    docker build `
        -t "$($c.Name):$ImageTag" `
        -f $c.DockerfilePath `
        $ctx

    # убеждаемся, что папка для логов есть
    if (-not (Test-Path $c.LogPath)) {
        New-Item -ItemType Directory -Path $c.LogPath -Force | Out-Null
    }

    # запускаем контейнер с network alias
    docker run -d `
        --name      $c.Name `
        --network   $Network `
        --network-alias postgres-production-db `
        -p          "$($c.PortExt):$($c.PortInt)" `
        -e          "POSTGRES_DB=$($c.EnvDbName)" `
        -e          "POSTGRES_USER=$($c.EnvDbUser)" `
        -e          "POSTGRES_PASSWORD=$($c.EnvDbPassword)" `
        -v          "$($DataVolume):/var/lib/postgresql/data" `
        -v          "$($c.LogPath):/var/log/postgresql" `
        "$($c.Name):$ImageTag"
        
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ PostgreSQL контейнер запущен с alias 'postgres-production-db'" -ForegroundColor Green
    } else {
        Write-Host "❌ Ошибка запуска PostgreSQL контейнера" -ForegroundColor Red
    }
}

# Функция для автоматического подключения контейнера к общей сети
function Connect-ContainerToSharedNetwork {
    param(
        [string]$containerName
    )
    
    $bridgeNetwork = "docker-business-entity-common-bridge"  # обновлённое имя сети
    
    # Проверяем, существует ли общая сеть
    $existingNetwork = docker network ls --filter "name=$bridgeNetwork" --format "{{.Name}}"
    
    if ($existingNetwork) {
        # Проверяем, не подключен ли уже контейнер
        $networkInfo = docker network inspect $bridgeNetwork | ConvertFrom-Json
        $isConnected = $false
        
        if ($networkInfo[0].Containers) {
            foreach ($container in $networkInfo[0].Containers.PSObject.Properties.Value) {
                if ($container.Name -eq $containerName) {
                    $isConnected = $true
                    break
                }
            }
        }
        
        if (-not $isConnected) {
            Write-Host "Подключаем контейнер '$containerName' к общей сети '$bridgeNetwork'..." -ForegroundColor Yellow
            docker network connect $bridgeNetwork $containerName
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ Контейнер подключен к общей сети." -ForegroundColor Green
            } else {
                Write-Host "⚠ Не удалось подключить к общей сети." -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "⚠ Общая сеть не найдена. Запустите функцию AjustNetworks (пункт меню 03) для её создания." -ForegroundColor Yellow
    }
}

# --- Новая функция для создания/настройки общей сети и подключения всех контейнеров ---
function AjustNetworks {
    param(
        [string]$networkName = "docker-business-entity-common-bridge"
    )

    Write-Host "=== Настройка общей сети '$networkName' ===" -ForegroundColor Cyan

    # Создать сеть, если она отсутствует
    $existingNetwork = docker network ls --filter "name=$networkName" --format "{{.Name}}"
    if (-not $existingNetwork) {
        Write-Host "Создаём сеть '$networkName'..." -ForegroundColor Yellow
        docker network create --driver bridge $networkName | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Сеть '$networkName' создана." -ForegroundColor Green
        } else {
            Write-Error "Ошибка создания сети '$networkName'"
            return
        }
    } else {
        Write-Host "Сеть '$networkName' уже существует." -ForegroundColor Green
    }

    # Подключить все существующие контейнеры к сети
    $containers = docker ps -q
    foreach ($containerId in $containers) {
        $networksJson = docker inspect --format "{{json .NetworkSettings.Networks}}" $containerId | ConvertFrom-Json
        if ($networksJson.PSObject.Properties.Name -notcontains $networkName) {
            Write-Host "Подключаю контейнер $containerId к сети '$networkName'..." -ForegroundColor Yellow
            docker network connect $networkName $containerId | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ $containerId подключен." -ForegroundColor Green
            } else {
                Write-Host "⚠ Не удалось подключить $containerId." -ForegroundColor Red
            }
        }
    }

    Write-Host "Настройка сети завершена." -ForegroundColor Cyan
}

# Функция для очистки кеша Docker
function Action378 {
    Write-Host "Очистка кеша Docker..." -ForegroundColor Cyan
    Write-Host "Эта операция удалит все неиспользуемые образы, контейнеры, сети и кеш сборки." -ForegroundColor Yellow
    $confirmation = Read-Host "Вы уверены, что хотите продолжить? (y/N)"
    
    if ($confirmation -eq 'y' -or $confirmation -eq 'Y') {
        Write-Host "Выполняется docker system prune -a..." -ForegroundColor Yellow
        docker system prune -a
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Кеш Docker успешно очищен." -ForegroundColor Green
        } else {
            Write-Host "⚠ Произошла ошибка при очистке кеша Docker." -ForegroundColor Red
        }
    } else {
        Write-Host "Операция отменена." -ForegroundColor Yellow
    }
}

# Функция для запуска сервиса Authentik
function Action37 {
  
}

# Функция для генерации .env файла для Authentik
function Action371 {
  Write-Host "=== Генерация Authentic\\.env для Authentik ===" -ForegroundColor Cyan
  $root = Split-Path $PSScriptRoot -Parent
  $envPath = Join-Path -Path $root -ChildPath "Authentic\.env"

  if (Test-Path -Path $envPath) {
    $ans = Read-Host "Файл уже существует. Перезаписать? (y/N)"
    if ($ans -notin @('y','Y')) { Write-Host "Отмена." -ForegroundColor Yellow; return }
  }

  # Генерация безопасных значений (PowerShell 5.1 совместимо)
  $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
  try {
    $bytes = New-Object byte[] 32
    $rng.GetBytes($bytes)
    $authSecret = [Convert]::ToBase64String($bytes)

    $bytes2 = New-Object byte[] 16
    $rng.GetBytes($bytes2)
    $pgPass = ([Convert]::ToBase64String($bytes2)).TrimEnd('=')
  } finally {
    if ($rng) { $rng.Dispose() }
  }

  $content = @(
    "# Generated at $(Get-Date -Format s)",
    "# Authentik core",
    "AUTHENTIK_SECRET_KEY=$authSecret",
    "",
    "# Internal Postgres used by Authentik",
    "PG_USER=authentik",
    "PG_DB=authentik",
    "PG_PASS=$pgPass",
    "",
    "# Host ports for Authentik UI",
    "COMPOSE_PORT_HTTP=9000",
    "COMPOSE_PORT_HTTPS=9443"
  )

  $dir = Split-Path -Parent $envPath
  if (-not (Test-Path -Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
  }
  $content | Set-Content -Path $envPath -Encoding ASCII -Force
  Write-Host "✓ Файл создан: $envPath" -ForegroundColor Green

  # Дополнительно: создадим/обновим корневой .env для интерполяции compose
  $rootEnvPath = Join-Path -Path $root -ChildPath ".env"
  $rootVars = Read-DotEnvAsHashtable -Path $rootEnvPath
  $rootVars["AUTHENTIK_SECRET_KEY"] = $authSecret
  $rootVars["PG_PASS"] = $pgPass
  if (-not $rootVars.ContainsKey("PG_USER")) { $rootVars["PG_USER"] = "authentik" }
  if (-not $rootVars.ContainsKey("PG_DB"))   { $rootVars["PG_DB"]   = "authentik" }
  if (-not $rootVars.ContainsKey("COMPOSE_PORT_HTTP"))  { $rootVars["COMPOSE_PORT_HTTP"]  = "9000" }
  if (-not $rootVars.ContainsKey("COMPOSE_PORT_HTTPS")) { $rootVars["COMPOSE_PORT_HTTPS"] = "9443" }
  Write-DotEnvFromHashtable -Path $rootEnvPath -Data $rootVars
  Write-Host "✓ Обновлён корневой .env для compose: $rootEnvPath" -ForegroundColor Green
}

function Action50 {
  Write-Host "=== Запуск root docker-compose (весь стек) ===" -ForegroundColor Cyan
  $root = Split-Path $PSScriptRoot -Parent
  $composePath = Join-Path $root "docker-compose.yml"

  if (-not (Test-Path $composePath)) {
    Write-Host "❌ Файл docker-compose.yml не найден: $composePath" -ForegroundColor Red
    return
  }

  # Сохраняет существующие секреты и добавляет недостающую конфигурацию первого запуска.
  & (Join-Path $PSScriptRoot "Initialize-AuthentikEnv.ps1") -RepositoryRoot $root
  Ensure-Network -NetworkName "docker-business-entity-common-bridge"
  docker compose -f $composePath config --quiet
  if ($LASTEXITCODE -ne 0) {
    throw "Ошибка проверки конфигурации docker compose."
  }

  Write-Host "Запускаю стек: docker compose up -d --build" -ForegroundColor Yellow
  docker compose -f $composePath up -d --build
  if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Стек запущен." -ForegroundColor Green
    Write-Host "Краткий статус контейнеров:" -ForegroundColor Cyan
    docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
  } else {
    Write-Host "❌ Ошибка запуска стека." -ForegroundColor Red
  }
}

function Action51 {
  Write-Host "=== Остановка root docker-compose (весь стек) ===" -ForegroundColor Cyan
  $root = Split-Path $PSScriptRoot -Parent
  $composePath = Join-Path $root "docker-compose.yml"

  if (-not (Test-Path $composePath)) {
    Write-Host "❌ Файл docker-compose.yml не найден: $composePath" -ForegroundColor Red
    return
  }

  Write-Host "Выполняю: docker compose down --remove-orphans" -ForegroundColor Yellow
  docker compose -f $composePath down --remove-orphans
  if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Стек остановлен." -ForegroundColor Green
  } else {
    Write-Host "⚠ Ошибка при остановке стека." -ForegroundColor Yellow
  }
}

function Action53 {
    Write-Host "=== ПОЛНЫЙ СНОС стека businessentity-stack (контейнеры, локальные образы, тома, сеть) ===" -ForegroundColor Red
    $root = Split-Path $PSScriptRoot -Parent
    $composePath = Join-Path $root "docker-compose.yml"
    $projectName = "businessentity-stack"
    $bridgeNetwork = "docker-business-entity-common-bridge"
  
    $confirm = Read-Host "ВНИМАНИЕ: Будут УДАЛЕНЫ контейнеры, ЛОКАЛЬНЫЕ образы, ТОМA и СЕТЬ проекта. Для подтверждения введите: DELETE"
    if ($confirm -ne "DELETE") { Write-Host "Отмена." -ForegroundColor Yellow; return }
  
    if (Test-Path $composePath) {
      Write-Host "Выполняю: docker compose down --remove-orphans --volumes --rmi all" -ForegroundColor Yellow
      docker compose -f $composePath down --remove-orphans --volumes --rmi all
    } else {
      Write-Host "⚠ Файл docker-compose.yml не найден: $composePath (пропускаю compose down)" -ForegroundColor DarkYellow
    }
  
    # Удаляем оставшиеся контейнеры проекта
    Write-Host "Удаляю оставшиеся контейнеры проекта..." -ForegroundColor Yellow
    docker ps -a --format "{{.Names}}" | ForEach-Object {
      if ($_ -like "$projectName*") {
        docker rm -f $_ 2>$null | Out-Null
      }
    }
  
    # Удаляем все тома проекта по label и по имени
    Write-Host "Удаляю тома проекта..." -ForegroundColor Yellow
    $volumesByLabel = docker volume ls -q --filter "label=com.docker.compose.project=$projectName"
    foreach ($v in $volumesByLabel) {
      docker volume rm -f $v 2>$null | Out-Null
    }
  
    $candidateVolumes = @(
      "$projectName`_pgdata","pgdata",
      "$projectName`_database","database",
      "$projectName`_redis","redis"
    )
    foreach ($v in $candidateVolumes) {
      $exists = docker volume ls --format "{{.Name}}" | Where-Object { $_ -eq $v }
      if ($exists) {
        docker volume rm -f $v 2>$null | Out-Null
      }
    }
  
    # Удаляем локальные образы проекта
    Write-Host "Удаляю локальные образы compose-проекта..." -ForegroundColor Yellow
    $images = docker images --format "{{.Repository}}:{{.Tag}}|{{.ID}}"
    foreach ($rec in $images) {
      $parts = $rec -split '\|'
      if ($parts.Count -lt 2) { continue }
      $repoTag = $parts[0]; $imgId = $parts[1]
      $repo = $repoTag.Split(':')[0]
      if ($repo -like "$projectName*") {
        docker rmi -f $imgId 2>$null | Out-Null
      }
    }
  
    # Удаляем default и внешнюю сеть
    docker network rm "$projectName`_default" 2>$null | Out-Null
    $netExists = docker network ls --format "{{.Name}}" | Where-Object { $_ -eq $bridgeNetwork }
    if ($netExists) {
      try { docker network rm $bridgeNetwork 2>$null | Out-Null } catch {}
    }
  
    # Чистим папку media
    $mediaDir = Join-Path $root "Authentic\media"
    if (Test-Path -Path $mediaDir) {
      try {
        Get-ChildItem -Path $mediaDir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "✓ Папка Authentic\\media очищена." -ForegroundColor DarkGray
      } catch {
        Write-Host "⚠ Не удалось очистить Authentic\\media: $_" -ForegroundColor DarkYellow
      }
    }
  
    Write-Host "✓ Полный снос завершён. Следующий запуск создаст пустую систему." -ForegroundColor Green
  }
  

function Show-Menu {
    Write-Host "Меню:" -ForegroundColor Green
    Write-Host "00   -- Вывести статус докер-сетей"
    Write-Host "03   -- Настроить общую сеть и переподключить ВСЕ контейнеры (только при проблемах с сетью)"
    Write-Host "10   -- Подключиться к выбранному контейнеру"
    Write-Host "20   -- Вывести диагностическую информацию для выбранного контейнера"
    Write-Host "30   -- Пересобрать и запустить отдельный контейнер (БЕЗОПАСНО + авто-перезапуск логгера)"
    Write-Host "31   -- Безопасная пересборка ТОЛЬКО business-entity (+ перезапуск логгера)"
    Write-Host "32   -- Безопасная пересборка ТОЛЬКО web-logger"
    Write-Host "33   -- Безопасная пересборка ТОЛЬКО admin-node"
    Write-Host "35   -- Сбилдить и запустить production_db контейнер"
    Write-Host "37   -- Запустить сервис Authentik"
    Write-Host "371  -- Сгенерировать .env для Authentik"
    Write-Host "378  -- Очистить кеш докера"
    Write-Host "38   -- Запушить ветку basic_add_elements_and_tree_mechanism как есть"
    Write-Host "40   -- Сбилдить и запустить бизнес-логику"
    Write-Host "50   -- Запустить root docker-compose (весь стек)"
    Write-Host "51   -- Остановить root docker-compose (весь стек)"
    Write-Host "53   -- ПОЛНЫЙ СНОС стека (контейнеры, локальные образы, тома, сеть)"
    Write-Host "80   -- Очистить экран (CLS)"
    Write-Host "99   -- Выход"
}

# Основной цикл работы
do {
    Show-Menu
    $choice = Read-Host "Выберите пункт меню"

    switch ($choice) {
        "00"  { Show-MultipleContainersNetworks }
        "03"  { AjustNetworks }
        "10"  { Action10 }
        "20"  { Action20 }
        "30"  { SelectContainerAndRun }
        "31"  { Rebuild-BusinessEntity-Safe }
        "32"  { Rebuild-WebLogger-Safe }
        "33"  { Rebuild-AdminNode-Safe }
        "35"  { BuildAndRunPostgresContainer }
        "37"  { Action37 }
        "371" { Action371 }
        "378" { Action378 }
        "38"  { git push --force-with-lease origin basic_add_elements_and_tree_mechanism }
        "40"  { BuildAndRunBusinessLogicContainers }
        "50"  { Action50 }
        "51"  { Action51 }
        "53"  { Action53 }
        "54"  { Action53 -
        "53"  { Action53 - }
        "80"  { Clear-Host }
        "99"  { break }
        default {
            Write-Host "Некорректный выбор. Повторите ввод." -ForegroundColor Red
        }
    }
} while ($choice -ne "99")

Write-Host "Программа завершена." -ForegroundColor Green

