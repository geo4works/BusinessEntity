# Политика развертывания

## 1. Назначение

Документ фиксирует правила первичной установки, первого запуска, обновления версий и эксплуатационной раскатки `BusinessEntity`.

Политика нужна, чтобы новая функциональность развивалась с учетом install/deploy lifecycle, а не только локального запуска разработчика.

---

## 2. Базовая модель развертывания

Целевой базовый сценарий:

```text
один хост Linux или Windows
    |
    v
Docker + Docker Compose
    |
    v
BusinessEntity stack
```

На текущем этапе система не проектируется как Kubernetes-first или multi-node установка. Это может появиться позже, но базовый контракт должен оставаться простым:

- скачать release/bundle;
- подготовить `.env`;
- запустить один install/deploy script;
- получить работающий стек.

Скрипты развертывания должны быть не интерактивным "меню для разработчика", а повторяемым operator-интерфейсом:

```text
install
start
stop
update
backup
restore
status
logs
```

Для Windows допустимы `.ps1` и тонкий `.bat` wrapper. Для Linux допустим `.sh`. Логика не должна расходиться между платформами: platform-specific wrappers должны вызывать одну и ту же последовательность Docker Compose действий.

---

## 3. Release bundle

Поддерживаемая модель поставки - самодостаточный архив release bundle.

Пользовательский сценарий:

```text
скачать BusinessEntity-<version>-linux-x64.tar.gz
или BusinessEntity-<version>-win-x64.zip
    |
    v
распаковать
    |
    v
запустить install.sh / install.bat / install.ps1
    |
    v
получить поднятый Docker Compose stack
```

Важно: архив не отменяет необходимость Docker-compatible runtime. На Linux это обычно Docker Engine + Compose plugin. На Windows это Docker Desktop, Docker Engine внутри WSL2 или другой поддерживаемый Docker-compatible runtime. Без контейнерного runtime Docker-образы физически не запустятся.

### 3.1. Online bundle

Online bundle содержит только deployment assets:

```text
docker-compose.yml
docker-compose.prod.yml
.env.example
install.sh
install.ps1
install.bat
deploy.sh
deploy.ps1
release-manifest.json
README.md
```

При запуске script делает:

```text
docker compose pull
docker compose up -d
```

Плюсы:

- маленький архив;
- проще обновлять images;
- подходит для серверов с доступом к container registry.

Минусы:

- нужен доступ к registry;
- registry credentials должны быть настроены отдельно, если images private.

### 3.2. Offline bundle

Offline bundle дополнительно содержит Docker images, сохраненные через `docker save`:

```text
images/
    business-entity.<version>.tar
    web-logger.<version>.tar
    postgres.<version>.tar
    authentik-server.<version>.tar
```

При запуске script делает:

```text
docker load -i images/business-entity.<version>.tar
docker load -i images/web-logger.<version>.tar
docker load -i images/postgres.<version>.tar
docker load -i images/authentik-server.<version>.tar
docker compose up -d
```

Плюсы:

- можно установить без доступа к registry;
- release полностью переносим одним архивом;
- оператор не собирает код на сервере.

Минусы:

- архив большой;
- images platform-specific;
- нужно собирать отдельные bundles для нужных архитектур.

### 3.3. Platform variants

Минимальные варианты release bundle:

```text
BusinessEntity-<version>-linux-x64.tar.gz
BusinessEntity-<version>-win-x64.zip
```

Если будут поддерживаться ARM-серверы:

```text
BusinessEntity-<version>-linux-arm64.tar.gz
```

Различие Windows/Linux bundle должно быть только в wrapper scripts, путях и проверках окружения. Состав сервисов, имена контейнеров, env keys и compose topology должны оставаться одинаковыми.

Windows bundle не означает Windows container images. Базовая поставка использует Linux containers. На Windows они запускаются через Docker Desktop Linux backend, WSL2 Docker Engine или совместимый runtime. Отдельные Windows container images не являются целью текущей политики.

### 3.4. Требуемый layout архива

Целевой layout:

```text
BusinessEntity/
    install.sh
    install.ps1
    install.bat
    deploy.sh
    deploy.ps1
    docker-compose.yml
    docker-compose.prod.yml
    .env.example
    release-manifest.json
    README.md
    images/
        *.tar
    scripts/
        lib/
        doctor.*
        backup.*
        restore.*
    storage/
        .gitkeep
    backups/
        .gitkeep
```

`images/` обязателен только для offline bundle.

### 3.5. Install script contract

`install.*` должен выполнять:

1. Проверить Docker runtime.
2. Проверить Docker Compose.
3. Проверить свободные ports.
4. Создать `.env` из `.env.example`, если `.env` отсутствует.
5. Сгенерировать недостающие secrets.
6. Создать external docker network.
7. В offline mode выполнить `docker load` для всех images.
8. Выполнить `docker compose config`.
9. Запустить stack.
10. Дождаться health checks.
11. Выполнить application bootstrap.
12. Вывести URL приложения, URL Authentik и стартовые учетные записи.

`install.*` должен быть идемпотентным. Повторный запуск не должен удалять volumes, storage и пользовательские данные.

### 3.6. Update bundle

Тот же архив должен поддерживать update существующей installation:

```text
deploy.ps1 update --bundle .
deploy.sh update --bundle .
```

Update из bundle:

1. Читает текущий deployed version.
2. Проверяет совместимость.
3. Создает installation backup.
4. Загружает images из bundle или registry.
5. Применяет migrations.
6. Перезапускает stack.
7. Запускает smoke checks.
8. Записывает deploy report.

Если update не прошел preflight, он не должен менять текущую installation.

### 3.7. Текущие артефакты сборки bundle

Первый практический вариант строится вокруг PowerShell, потому что разработка сейчас идет под Windows.

Текущие исходные deployment assets:

```text
Deployment/docker-compose.yml
Deployment/.env.example
Deployment/install.ps1
Deployment/install.bat
Deployment/deploy.ps1
Deployment/scripts/bootstrap-initial-data.ps1
Deployment/README.md
```

Сборщик bundle:

```text
Powershell/Build-ReleaseBundle.ps1
```

Базовая команда:

```powershell
.\Powershell\Build-ReleaseBundle.ps1 -Version 0.1.0 -Platform win-x64 -BundleMode Online
```

Offline bundle:

```powershell
.\Powershell\Build-ReleaseBundle.ps1 -Version 0.1.0 -Platform win-x64 -BundleMode Offline
```

Online bundle не хранит images и требует `docker compose pull`/доступ к registry.
Offline bundle сохраняет images в `images/*.tar` через `docker save`, а `install.ps1` загружает их через `docker load`.

Альтернативы на будущее:

- `build-release-bundle.sh` для сборки Linux bundle на Linux runner;
- CI job, который собирает оба bundle-варианта и публикует их как release artifacts;
- единый PowerShell 7 script, работающий и на Windows, и на Linux.

---

## 4. Состав installation

Одна installation - это один развернутый стек приложения с общей БД и файловым storage.

Текущий состав stack:

```text
business-entity
web_logger
postgres-production-db
authentic_server
authentic_worker
authentic_postgresql
docker-business-entity-common-bridge
BusinessEntityStorage
Docker volumes
```

Роли компонентов:

| Компонент | Роль |
|---|---|
| `business-entity` | основное Blazor Server приложение |
| `web_logger` | отдельный web logger |
| `postgres-production-db` | БД приложения и web logger |
| `authentic_server` | Authentik UI/API/OIDC server |
| `authentic_worker` | фоновые задачи Authentik |
| `authentic_postgresql` | отдельная БД Authentik |
| `BusinessEntityStorage` | файловое storage приложения |
| external docker network | связность контейнеров |

Граница installation сейчас выше, чем `Space`. Несколько `Space` живут внутри одной application DB. `Space` не является отдельной БД и не должен становиться единицей деплоймента.

---

## 5. Конфигурация и секреты

Production deployment не должен полагаться на секреты, зашитые в `docker-compose.yml`.

Все значения ниже должны приходить из `.env`, secret store или параметров deploy script:

- пароли PostgreSQL;
- `AUTHENTIK_SECRET_KEY`;
- `AUTHENTIK_API_TOKEN`;
- публичные URL приложения и Authentik;
- redirect URIs;
- root path файлового storage;
- порты наружу;
- environment name.

Правило:

```text
docker-compose.yml       = структура stack
.env / secret store      = значения конкретной installation
deploy script            = проверка и применение
```

Production `.env` не должен коммититься в репозиторий. В репозитории допустимы только `.env.example` или шаблоны с пустыми значениями.

Для development compose может иметь удобные значения по умолчанию, но production policy считает их небезопасными, если они не переопределены.

---

## 6. Первый запуск

Первый запуск состоит из двух разных фаз:

```text
infrastructure bootstrap
    |
    v
application bootstrap
```

### 6.1. Infrastructure bootstrap

На этом этапе deploy script должен:

1. Проверить наличие Docker и Docker Compose.
2. Проверить свободные порты.
3. Создать внешнюю docker-сеть, если она отсутствует.
4. Сгенерировать или принять `.env`.
5. Поднять БД Authentik и БД приложения.
6. Поднять Authentik server/worker.
7. Поднять `business-entity` и `web_logger`.
8. Проверить health endpoints.

Infrastructure bootstrap должен быть идемпотентным: повторный запуск не должен удалять данные и не должен пересоздавать volumes.

### 6.1.1. Реализованный bootstrap Authentik

Для исходного репозитория `Powershell/Initialize-AuthentikEnv.ps1` добавляет недостающие
bootstrap/OIDC-секреты в `.env`, сохраняя существующие значения. Меню запуска всего стека
вызывает этот скрипт автоматически. В release bundle ту же подготовку выполняет `install.ps1`.

`authentic_worker` получает `AUTHENTIK_BOOTSTRAP_TOKEN`, `AUTHENTIK_BOOTSTRAP_PASSWORD`
и `AUTHENTIK_BOOTSTRAP_EMAIL`; штатный bootstrap Authentik создает `akadmin`, группу
`authentik Admins` и API-токен. Приложению передается тот же токен через `AUTHENTIK_API_TOKEN`.
Существующие bootstrap-токены не ротируются повторной генерацией `.env`.

При `EnsureAuthentikOnStartup=true` `AuthentikBootstrapService` выполняется **до**
`UserMiniApp.EnsureInitialized()`: ожидает API, токен и default blueprints, создает/обновляет
приложение и группу каталога `BusinessEntity`, OAuth2/OIDC provider `be-oidc`, callbacks и
scope mappings `openid`, `profile`, `email`, `groups`. Это группа каталога приложений,
а не отдельный tenant Authentik. Повторный запуск использует существующие идентификаторы.

Далее `UserMiniApp` выполняет действующую `Users/admin-users-policy.md`: `akadmin/akadmin`,
новый `admin/admin`, группы и локальное назначение роли. Пароль и профиль существующего
`admin` сохраняются. Ошибка обязательной настройки останавливает запуск приложения;
HTTP 200 становится доступен только после завершения bootstrap.

### 6.2. Application bootstrap

На этом этапе приложение должно:

1. Создать или обновить DB schema.
2. Создать системные роли.
3. Создать системных пользователей.
4. Создать стартовых пользователей или связать их с Authentik.
5. Создать минимальное стартовое пространство.
6. Создать стартовую страницу/документ.
7. Записать факт успешного bootstrap.

Application bootstrap также должен быть идемпотентным.

Начальные данные должны создаваться приложением, а не внешним SQL/PowerShell скриптом.

Причины:

- создание пользователей, ролей, пространств и документов должно идти через доменные сервисы;
- bootstrap должен уважать текущую storage schema и mini-app контракты;
- idempotency проще обеспечить внутри приложения;
- внешний скрипт не должен знать внутренние DTO и таблицы.

Поэтому script внутри дистрибутива `scripts/bootstrap-initial-data.ps1` является orchestration/diagnostic wrapper:

```text
поднять stack
дождаться business-entity
при необходимости restart business-entity
проверить, что application startup/bootstrap прошел
```

Целевое место логики:

```text
InstallationBootstrapService
    -> UserMiniApp system defaults
    -> minimal users/admin binding
    -> minimal Space seed
    -> InstallationBootstrapState
```

До появления `InstallationBootstrapService` текущий startup seed остается временным механизмом.

---

## 7. Системные и стартовые пользователи

В installation должны различаться:

- технические системные записи;
- emergency/admin учетная запись;
- первый рабочий администратор;
- обычные пользователи.

### 7.1. Системные записи

Системные записи не являются людьми и не должны использоваться для интерактивного входа:

| Запись | Назначение |
|---|---|
| `system-seed` | владелец seed/bootstrap-данных, созданных без HTTP-пользователя |
| `system-anonymous` | anonymous/access модель для публичного чтения |

Эти записи хранятся в user storage приложения и не обязаны существовать как Authentik users.

### 7.2. Emergency admin

`akadmin` - технический emergency account.

Правила:

- нужен для аварийного входа и починки установки;
- не должен быть владельцем пользовательского контента по умолчанию;
- не должен использоваться как ежедневный рабочий пользователь;
- должен иметь сильный пароль и ограниченный доступ на уровне операционной процедуры.

Текущий код уже считает username `akadmin` специальным admin-признаком. Новая deploy-логика должна не размазывать этот смысл по другим местам, а явно документировать его как emergency механизм.

### 7.3. Первый рабочий администратор

`admin` - первый рабочий администратор installation.

Правила:

- создается или проверяется при первом запуске;
- должен быть членом Authentik group `BusinessEntityAdmins` или иметь локальное назначение роли с `GlobalAdmin`;
- используется для первичной настройки пространств, пользователей и прав;
- может быть отключен после создания реальных администраторов команды, если есть другая учетная запись с `GlobalAdmin`.

### 7.4. Базовые роли

Application bootstrap должен гарантировать наличие ролей:

| Роль | Назначение |
|---|---|
| `Админ` | полный набор прав, системная роль |
| `Гость` | чтение опубликованного |
| `Ридерс` | чтение опубликованного |

Роль `Админ` не должна удаляться из UI. Пользовательские роли могут создаваться после установки.

---

## 8. Начальное содержимое

Production installation должна создавать минимальное полезное содержимое, а не большую demo-структуру.

Целевое production seed-содержимое:

```text
Space: Документы
    Document: Добро пожаловать
```

Допустимо добавить второй документ:

```text
Document: Быстрый старт
```

Содержимое стартового документа должно коротко объяснять:

- что это пространство;
- как создать папку;
- как создать обычный документ;
- как создать rich-document;
- где находится администрирование пользователей;
- что опубликованный и черновой контент могут иметь разные права доступа.

Большие демонстрационные данные вроде `Новости`, `Folder 1`, `Document 1-1` должны быть отдельным demo-seed режимом. Production bootstrap не должен создавать их автоматически.

Текущее `SampleDataService` является development/demo seed. Для production нужен отдельный `InstallationBootstrapService` или явный режим seed:

```text
SeedMode = None | Minimal | Demo
```

Рекомендуемое значение для production:

```text
SeedMode = Minimal
```

Внешний install script не должен напрямую создавать эти данные в БД.

Правильная последовательность:

```text
install.ps1
    -> docker compose up -d
    -> business-entity startup
    -> InstallationBootstrapService
    -> minimal seed через helper/connector/service слой
```

Если нужен ручной повтор bootstrap, deploy script должен вызывать приложение или рестартовать application bootstrap, но не выполнять прямые insert/update в таблицы.

---

## 9. Маркер первого запуска

Приложение должно хранить факт завершенного application bootstrap.

Целевой payload:

```json
{
  "schemaVersion": 1,
  "kind": "InstallationBootstrapState",
  "installationId": "guid",
  "completedAtUtc": "2026-05-27T00:00:00Z",
  "appVersion": "x.y.z",
  "seedMode": "Minimal"
}
```

Хранилище может быть отдельной таблицей installation settings или системной property. Главное правило: bootstrap state принадлежит installation, а не конкретному `Space` и не конкретному user.

Повторный запуск приложения:

- может обновлять schema;
- может добавлять отсутствующие системные роли;
- может чинить отсутствующие системные записи;
- не должен повторно создавать стартовое пространство и стартовые документы, если bootstrap уже завершен.

---

## 10. Обновление версии

Обновление версии состоит из этапов:

```text
[1] preflight
[2] backup
[3] fetch/build release
[4] apply schema/data migrations
[5] restart services
[6] smoke check
[7] finalize or rollback
```

### 10.1. Preflight

Перед обновлением deploy script должен проверить:

- текущий git/image version;
- целевой version;
- доступность Docker;
- доступность БД;
- наличие свободного места;
- доступность Authentik;
- health текущих контейнеров;
- наличие backup-пути.

Если preflight не проходит, update не должен начинаться.

### 10.2. Backup перед обновлением

Перед каждым production update должен создаваться backup:

- PostgreSQL dump application DB;
- PostgreSQL dump Authentik DB или snapshot volume;
- файловое storage приложения;
- `.env`/deployment config без публикации секретов в логи;
- release manifest текущей версии.

Backup должен быть атомарно помечен как связанный с конкретным update:

```text
backup-before-update-<fromVersion>-to-<toVersion>-<timestamp>
```

### 10.3. Release source of truth

Source of truth для release:

- git tag или commit SHA;
- Docker image tag;
- release manifest.

Release manifest должен содержать:

- app version;
- image tags;
- минимальную поддерживаемую DB schema version;
- список миграций;
- инструкции rollback;
- список ручных post-install действий, если они есть.

### 10.4. Restart order

Базовый порядок обновления:

```text
postgres-production-db      stays running
authentic_postgresql        stays running
authentic_server/worker     update only if Authentik version changes
web_logger                  rebuild/restart
business-entity             rebuild/restart
```

Если меняется только `business-entity`, не нужно пересоздавать БД и Authentik.

Если меняется contract логирования или DB connection pool, допустим перезапуск `web_logger` вместе с `business-entity`.

---

## 11. Миграции БД и схемы

Startup schema update должен быть безопасным и идемпотентным.

Разрешено:

- `CREATE TABLE IF NOT EXISTS`;
- `CREATE INDEX IF NOT EXISTS`;
- `ALTER TABLE ADD COLUMN IF NOT EXISTS`;
- заполнение новых nullable/default полей;
- добавление новых enum/property values без удаления старых.

Запрещено в обычном startup:

- удалять таблицы;
- удалять колонки;
- массово перезаписывать пользовательские данные без backup;
- выполнять необратимые data migrations без version ledger;
- менять смысл существующих enum values.

Для сложных изменений нужен отдельный migration step:

```text
MigrationId
Description
FromSchemaVersion
ToSchemaVersion
IsReversible
StartedAtUtc
CompletedAtUtc
Result
```

Если migration необратимая, rollback должен требовать restore backup, а не просто возврат старого контейнера.

---

## 12. Rollback

Rollback бывает двух типов.

### 12.1. Image rollback

Если БД и storage не менялись необратимо:

```text
stop new containers
start previous images
run smoke check
```

### 12.2. Data rollback

Если были schema/data migrations:

```text
stop stack
restore DB backup
restore file storage backup
start previous images
run smoke check
```

Deploy script должен заранее знать, какой rollback допустим для конкретного release.

---

## 13. Health checks и smoke checks

Минимальные health checks:

- `business-entity` отвечает HTTP 200;
- `web_logger` отвечает HTTP 200;
- Authentik health endpoint отвечает;
- PostgreSQL healthcheck healthy;
- приложение видит storage path;
- приложение может подключиться к DB.

Минимальные smoke checks после установки или update:

- открыть главную страницу;
- пройти login через Authentik;
- выбрать `Space`;
- открыть стартовый документ;
- создать тестовый документ;
- сохранить документ;
- открыть администрирование пользователей;
- проверить запись в web logger.

Smoke checks могут быть ручными на раннем этапе, но их список должен быть стабильным. Позже они должны стать автоматизированными.

---

## 14. Backup/restore как часть deploy lifecycle

Deploy policy не заменяет политики backup/restore пространств.

Есть два уровня:

| Уровень | Что сохраняет |
|---|---|
| installation backup | БД, Authentik, storage, env/config, все spaces |
| space backup | один `Space` как переносимый бизнес-подграф |

Перед production update нужен installation backup.

Space backup используется для бизнес-операций:

- перенести пространство;
- восстановить пространство рядом;
- сохранить снимок контента.

Эти уровни нельзя смешивать. Restore одного `Space` не является rollback всей installation.

---

## 15. Окружения

Должны различаться минимум три окружения:

```text
dev
stage
prod
```

Правила:

- `dev` может использовать demo seed, development URLs и упрощенные секреты;
- `stage` должен быть максимально похож на prod, но с тестовыми данными;
- `prod` не должен использовать demo seed и hardcoded secrets;
- compose overrides допустимы, но базовая структура stack должна оставаться общей.

Production environment должен работать за reverse proxy с TLS. Публичные URL приложения и Authentik должны быть согласованы с OIDC redirect URIs.

---

## 16. Deploy scripts

Целевые scripts:

```text
deploy.ps1
deploy.sh
deploy.bat
```

Минимальные команды:

```text
install
start
stop
restart
update --version <version>
backup
restore --backup <path>
status
logs --service <name>
doctor
```

`Powershell/СontainerManagement.ps1` сейчас является development/operator меню. Оно полезно локально, но не должно считаться production deploy interface.

Production deploy script должен:

- быть запускаемым из CI/CD;
- иметь non-interactive mode;
- явно завершаться non-zero exit code при ошибке;
- писать понятный deploy log;
- не печатать секреты;
- проверять `docker compose config` перед запуском;
- поддерживать dry-run для опасных действий.

---

## 17. CI/CD и release gate

Перед созданием release должны проходить:

- build приложения;
- build docker images;
- unit/integration tests, если они есть;
- проверка `docker compose config`;
- smoke на stage;
- проверка, что миграции применяются на копии prod-like БД;
- проверка rollback plan.

Release нельзя помечать production-ready, если:

- нет backup plan;
- нет rollback plan;
- есть ручная миграция без инструкции;
- меняются secrets/env keys без обновления `.env.example`;
- меняется storage schema без migration note.

---

## 18. Логирование и диагностика

Deploy/update должен оставлять диагностический след:

- version до обновления;
- version после обновления;
- commit/image tags;
- время старта и окончания;
- результат preflight;
- путь backup;
- список примененных migrations;
- результат smoke checks.

Логи deploy script хранятся отдельно от business logs. `web_logger` остается приложенческим логгером, но сбой `web_logger` не должен блокировать аварийный запуск `business-entity`, если основная БД доступна.

---

## 19. Текущее состояние и целевые доработки

Текущее состояние:

- есть `docker-compose.yml` для локального/development stack;
- есть `Powershell/СontainerManagement.ps1` с operator-меню;
- есть первый release-bundle контур `Deployment/*`;
- есть `Powershell/Build-ReleaseBundle.ps1` для сборки zip/tar.gz bundle из текущих файлов;
- `Program.cs` выполняет идемпотентное создание части schema через `EnsureCreated` и SQL;
- `UserMiniApp` создает базовые роли и `system-anonymous`;
- `Program.cs` может создать `system-seed`;
- `SampleDataService` создает demo-пространства и demo-документы;
- production bootstrap как отдельная механика пока не выделен.

Целевые доработки:

1. Вынести production bootstrap из demo seed.
2. Добавить `InstallationBootstrapState`.
3. Добавить `SeedMode`.
4. Реализовать `InstallationBootstrapService`.
5. Убрать production secrets из compose в `.env`/secret store.
6. Дорастить `deploy.ps1` до backup/update/restore.
7. Сделать Linux `install.sh`/`deploy.sh`.
8. Добавить release manifest.
9. Добавить versioned migration ledger.
10. Разделить dev/demo seed и minimal production seed.
11. Описать и автоматизировать smoke checks.

---

## 20. Короткая итоговая схема

```text
release bundle
    |
    v
deploy script
    |
    +--> validate env/secrets
    +--> docker compose config
    +--> backup current installation
    +--> pull/build images
    +--> apply migrations
    +--> start/restart containers
    +--> run health/smoke checks
    +--> write deploy report

first install
    |
    +--> infrastructure bootstrap
    +--> application bootstrap
    +--> akadmin/admin/system users
    +--> minimal space "Документы"
    +--> welcome document
    +--> InstallationBootstrapState
```
