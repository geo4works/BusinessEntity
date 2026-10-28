# BusinessEntity: запуск и раскатка

Актуализировано для архитектуры TASK_ARCH_001 и пакета 0.17.0, 2026-10-29.
Приёмка: [журнал этапов](Tasks/TASK_ARCH_001_execution-log.md).

## Рабочая копия и стек

Репозиторий: `C:\Develop\BusinessEntityProduct\BusinessEntity`, ветка `main`.
Remote: `git@github.com:geo4works/BusinessEntity.git`. Исходная точка миграции:
`5d34cda`, версия 0.16.2. Код приложения — `BusinessEntity/`, logger —
`BlazorServerWebLogger/`. Оба проекта и Docker build/runtime используют .NET 9.
Соседний `C:\Develop\Geo4Product\Geo4CodeBase\Geo4Web` — только чтение.

Compose project: `businessentity-stack`. Сеть:
`docker-business-entity-common-bridge` (external, нужна до первого up).

| Сервис | Назначение | Порт хоста |
|---|---|---|
| business-entity / business-entity-container | Blazor Server | 7000 → 80 |
| web_logger / web_logger-container | web-логгер | 5080 → 80 |
| postgres-production-db | business_entity и отдельная web_logger DB | 5470 → 5432 |
| authentic_server | Authentik API/OIDC/UI | 9000, 9443 |
| authentic_worker | bootstrap и фоновые задачи Authentik | — |
| authentic_postgresql | отдельная PostgreSQL Authentik | — |

Authentik image по умолчанию `ghcr.io/goauthentik/server:2026.2.1`, его БД —
Postgres 16 alpine. Релевантный конфиг — корневой docker-compose.yml.
Другие контейнеры компьютера не относятся к этому проекту.

`BusinessEntityStorage` репозитория смонтирован в `/app/storage`.
Storage:HostRootPath указывает на фактический путь этой рабочей копии; это
отображение/перевод путей backup, не другая копия файлов. Canonical файлы
находятся в `business-entities/<EntityId>/objects`, staging — `.staging`.
Backup имеет собственные папки, [формат 2](Policy/BackupRestore/README.md).

## Секреты

Локальные `.env` и `Authentic/.env` не входят в Git. Шаблоны `.env.example`,
`Authentic/.env.example`, `Deployment/.env.example` входят. Значения не выводить
в чат/логи; проверять наличие ключей, а не печатать compose config с секретами.

Powershell/Initialize-AuthentikEnv.ps1 сохраняет существующие значения и генерирует
недостающие. Не менять молча ключ Authentik/пароли БД существующей установки.
Compose environment перекрывает env_file; изменение ENV не меняет пароль уже
инициализированного PostgreSQL volume. Недоступную БД не «чинить» удалением volume.

Стартовые dev-пользователи `admin` и `akadmin` обслуживаются bootstrap владельца
User. Это dev-учётные записи, production настройки описаны в deployment policy.
EnsureAuthentikOnStartup управляет подготовкой приложения/групп/OIDC. Неверная
конфигурация должна завершить startup явной ошибкой, а не пустым рабочим UI.

## Первый запуск

Из корня репозитория, PowerShell:

```powershell
./Powershell/Initialize-AuthentikEnv.ps1
docker network create docker-business-entity-common-bridge
docker compose config --quiet
docker compose up -d --build
docker compose ps
```

Уже существующая сеть — норма. Ждать healthy PostgreSQL и Authentik, затем
завершения bootstrap приложения. HTTP 200 на 7000 появляется после запуска
обязательных runtime. Успех health Authentik сам по себе этого не доказывает.

## RSTT

Сначала закончить сборку, затем пересоздать только приложение и logger:

```powershell
docker compose build business-entity web_logger
docker compose up -d --no-build --no-deps --force-recreate business-entity web_logger
docker compose ps
docker compose logs --tail 100 business-entity
```

Не запускать вторую команду, пока первая работает. Проверить совпадение `.Image`
контейнера с `.Id` только что собранного образа: старый stopped-контейнер на этой
машине уже однажды запускался с прежним образом. Сборка использует общий NuGet
cache BuildKit с locking. Если код не менялся после последнего RSTT, повтор не нужен.
БД, Authentik и соседние проекты при RSTT не пересоздаются.

## Сборка и проверки

```powershell
dotnet build BusinessEntity.sln -c Release
dotnet test BusinessEntity.sln -c Release
```

Postgres-проверки требуют BE_TEST_POSTGRES отдельного тестового кластера.
Без него они явно skipped и не считаются приёмкой. Тесты создают/удаляют только
собственные `be_test_<guid>` и временные каталоги. В этой раскатке используется
`be-arch-test-db`, host 5471, временная PG17 alpine; это не production DB 5470.
Пароль тестового кластера передаётся окружением, не коммитится в код приложения.

Playwright сценарии описаны в [Smoke/README](../BusinessEntity.Tests/Smoke/README.md).
Базовый прогон пишет свежий BE_SMOKE_RESULT_FILE; storage использует те же fixtures.
Navigation-save проверяет документ после storage и создаёт его новые версии.
Anonymous fixture grant и тестовая учётная запись убираются через UI. Созданные
Smoke-документы и restore-пространства остаются для проверки сохранения данных.

Минимум после RSTT: 7000/5080 и `9000/-/health/live/` отвечают 200; стек healthy;
вход, выбор пространства, дерево, создание/Save/reload документа/статьи,
администрирование и logger работают; в журнале нет unhandled/circuit failures.
Для этапа нужен весь установленный тестовый цикл, не только HTTP health.

## Схема и восстановление

DataProvider schema 2, User schema 1, backup format 2. Старые схемы/архивы
не мигрируются и не читаются через adapters. Пользователь разрешил пересоздать
business_entity и очистить BusinessEntityStorage при этапе 2; это выполнено.
Authentik, logger и Docker volumes не удалялись. Это не разрешение повторять
сброс при последующих изменениях кода.

Только DataProvider пишет canonical граф/файлы и публикует data events после
commit. Outcome живёт в той же SQL-транзакции; Unknown нельзя объявлять rollback.
Файловая recovery использует достоверный manifest БД. Недоступная БД не является
основанием для удаления файлов. SQL и внешний Authentik API не образуют общую
транзакцию. Черновик UI находится в памяти своего circuit.

## Git и эксплуатационные ограничения

CMMTP n = версия + CHANGELOG (время до секунд) + русский коммит + push текущей ветки.
По TASK_ARCH_001 после 0–8 используется CMMTP 3, после 9 — CMMTP 2 с описанием
всей раскатки. Пользовательские подготовленные env/template-правки включены
в коммит этапа 0 с явного согласия. Force-push не выполнять; отказ push сообщить.

Тома и пользовательское файловое хранилище удалять только по прямому разрешению.
`down --volumes`, `docker volume rm` и удаление BusinessEntityStorage без него
запрещены. Падение Docker Desktop не доказывает неисправность данных; его рестарт
затрагивает другие проекты и требует соответствующего разрешения.

Старое меню `Powershell/СontainerManagement.ps1` имеет известные ошибки парсинга
и отсутствующий DockerNetworkInfo.ps1. Первая буква имени — кириллическая С.
Для раскатки использовать прямые compose-команды выше, не это меню.

Поставка на другую машину: Deployment/, Powershell/Build-ReleaseBundle.ps1,
артефакты в artifacts/dist. Инструкции install/update/rollback и ограничения —
[deployment policy](Policy/Deployment/deployment-policy.md).
