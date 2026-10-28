# Контекст для раскатывающей модели (BusinessEntity)

Стартовая шпаргалка для модели, которая будет поднимать, пересобирать и раскатывать
BusinessEntity. Прочитай её целиком до первых команд. Состояние зафиксировано
2026-10-26 на коммите `5d34cda` (версия 0.16.2).

---

## 1. Где что лежит

### Репозиторий

| Что | Значение |
|---|---|
| Рабочая копия | `C:\Develop\BusinessEntityProduct\BusinessEntity` |
| Git remote | `git@github.com:geo4works/BusinessEntity.git` (SSH) |
| Ветка | `main` |
| Версия | `0.16.2` — `BusinessEntity/BusinessEntity.csproj` (`<Version>`) и верх `CHANGELOG.md` |

Репозиторий один. Других git-репозиториев у продукта нет.

### Соседние папки (не репозитории)

| Папка | Что там |
|---|---|
| `C:\Develop\BusinessEntityProduct\` | Родительская папка: feature map (`BusinessEntity-feature-map.md`, `.drawio`) и `StartupCleaner.exe`. Под git не лежит. |
| `C:\Develop\BusinessEntity\` | **Старое расположение репозитория, сейчас пустая папка.** Не работать здесь. Внимание: `docker-compose.yml` всё ещё ссылается на неё (см. §6, п. 3). |
| `C:\Develop\Skills\` | Навыки для LLM (например `feature-map\SKILL.md`). К раскатке не относится. |

---

## 2. Контекст для LLM внутри репо

Порядок онбординга задан в `Context\llmstart`:

1. `Context\index.md` — архитектурный обзор. **Частично устарел:** там написано, что
   хранилище in-memory. Фактически данные лежат в PostgreSQL (`business_entity`),
   доступ через `DataProviderMiniApp`, схема создаётся в `Program.cs`.
2. `Context\work_process_guide.md` — правила работы. Главное для раскатки:
   после изменений в коде приложения или логгера пересобрать и перезапустить
   соответствующие контейнеры.
3. `Context\routines.md` — короткие команды пользователя:
   - `RSTT` / `РСТТ` — пересобрать и перезапустить `business-entity` и `web_logger`
     (если с прошлого перезапуска код не менялся — сказать об этом и не перезапускать);
   - `INCVER n`, `UPCH`, `GETCH` — работа с версией и CHANGELOG (дата и время до секунд);
   - `CMMT n` = INCVER + коммит, `CMMTP n` = CMMT + push;
   - `А >> Б` — сделать Б после А;
   - `ANKO` — задай вопросы, если есть, и поехали.
4. `Context\Policy\**` — политики. **Для раскатки главный документ —
   `Context\Policy\Deployment\deployment-policy.md`:** release bundle, install/update,
   bootstrap Authentik, rollback, health/smoke checks, порядок рестарта.

Прочее в `Context\`: `Commands\` (макросы, например `cmmtp3_rstt` = `CMMTP 3 >> RSTT`),
`MiniApps\user-miniapp.md`, `Hist\`, `Misc\SqlDiagnostics\*.sql` (диагностика чанков),
`Misc\_reset_akadmin_authentik.py`, `checks.md`.

---

## 3. Состав стека (корневой `docker-compose.yml`)

Compose-проект: `businessentity-stack`. Внешняя сеть: `docker-business-entity-common-bridge`
(`external: true` — **её нужно создать до `up`**).

| Сервис | Контейнер | Порт хоста | Сборка | Назначение |
|---|---|---|---|---|
| `business-entity` | `business-entity-container` | `7000` → 80 | `BusinessEntity/Dockerfile`, context = корень репо (.NET 6) | основное Blazor Server приложение |
| `web_logger` | `web_logger-container` | `5080` → 80 | `BlazorServerWebLogger/Dockerfile`, context = корень | веб-логгер |
| `postgres-production-db` | `postgres-production-db` | `5470` → 5432 | `postgres_db/Dockerfile` | БД `business_entity` и `web_logger`, пользователь `adm01`, volume `pgdata` |
| `authentic_server` | (по compose) | `9000`, `9443` | образ `ghcr.io/goauthentik/server:2026.2.1` | Authentik UI/API/OIDC |
| `authentic_worker` | (по compose) | — | тот же образ | фоновые задачи; создаёт `akadmin` и API-токен из bootstrap-переменных |
| `authentic_postgresql` | (по compose) | — | `postgres:16-alpine` | БД Authentik, volume `authentic_database` |

Порядок зависимостей: `business-entity` ждёт healthy `postgres-production-db` и
`authentic_server` и запущенный `authentic_worker`. `web_logger` ждёт только БД.

Файловое хранилище приложения: `.\BusinessEntityStorage` → `/app/storage`
(сейчас около 147 МБ: `business-entities\`, `backups\`). Под git не лежит.

Адреса: приложение `http://localhost:7000`, Authentik `http://localhost:9000`,
логгер `http://localhost:5080`.

---

## 4. Конфигурация и секреты

| Файл | Роль | В git |
|---|---|---|
| `.env` (корень) | значения для подстановки в compose: пароли и ключи Authentik, bootstrap-токен, OIDC client secret, URL | нет (`.gitignore`) |
| `Authentic\.env` | `env_file` для сервисов Authentik | нет |
| `.env.example`, `Authentic\.env.example`, `Deployment\.env.example` | шаблоны; `__GENERATE__` заменяется инициализатором | да |

Генерация и дополнение `.env`: `Powershell\Initialize-AuthentikEnv.ps1`. Скрипт
идемпотентный: существующие значения сохраняет, недостающие `PG_PASS`,
`AUTHENTIK_SECRET_KEY`, `AUTHENTIK_BOOTSTRAP_TOKEN`, `AUTHENTIK_CLIENT_SECRET` генерирует.
`Authentic\.env` создаёт, только если его нет.

Правила:

- **Секреты не выводить в лог или чат.** Проверять наличие ключей, а не значения.
- **Не перегенерировать секреты существующей установки.** Пароль БД Authentik зашит
  в volume `authentic_database` при первой инициализации. Смена `AUTHENTIK_SECRET_KEY`
  ломает сессии и зашифрованные данные Authentik. Новый bootstrap-токен
  уже созданный токен не ротирует.
- Учётки приложения захардкожены в compose: `adm01` / `adm01pwd` (dev; по политике
  production их нужно вынести в `.env`).
- Стартовые логины: `akadmin` / `akadmin` (технический emergency), `admin` / `admin`
  (первый рабочий администратор).
- `EnsureAuthentikOnStartup` (из `ENSURE_AUTHENTIK_ON_STARTUP`, по умолчанию `true`):
  при старте приложение само создаёт в Authentik приложение и группу `BusinessEntity`,
  OIDC-провайдер `be-oidc`, callbacks и scope mappings. Если эта настройка не прошла,
  приложение **падает на старте** — смотри логи `business-entity-container`,
  а не health Authentik.

---

## 5. Как поднимать и пересобирать (dev-стек из исходников)

Команды выполнять из `C:\Develop\BusinessEntityProduct\BusinessEntity`.

### Первый запуск или полный подъём

```powershell
./Powershell/Initialize-AuthentikEnv.ps1
docker network create docker-business-entity-common-bridge   # ошибка "already exists" — норма
docker compose config --quiet
docker compose up -d --build
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
```

Authentik стартует долго (`start_period` 90 с). После него приложение ещё выполняет
bootstrap. HTTP 200 на `:7000` появляется только после завершения bootstrap.

### RSTT — пересборка приложения и логгера

БД и Authentik не трогать (§10.4 deployment-policy):

```powershell
docker compose up -d --build --no-deps business-entity web_logger
docker compose logs --tail 100 business-entity
```

### Остановить и посмотреть

```powershell
docker compose down --remove-orphans    # без --volumes: данные сохраняются
docker compose logs -f business-entity
docker compose ps
```

**Никогда без явной просьбы пользователя:** `down --volumes`, `--rmi all`,
`docker volume rm`, удаление `BusinessEntityStorage`. Это уничтожает БД
и пользовательские данные.

### Release bundle (поставка на другую машину)

- Шаблон: `Deployment\` — `docker-compose.yml` (только `image:`, переменные
  `BE_IMAGE`, `WEB_LOGGER_IMAGE`, `POSTGRES_PRODUCTION_IMAGE`), `.env.example`,
  `install.ps1` / `install.bat`, `deploy.ps1`
  (`install|start|stop|restart|status|logs|doctor`), `scripts\bootstrap-initial-data.ps1`,
  `README.md`.
- Сборщик: `Powershell\Build-ReleaseBundle.ps1 -Version <x.y.z> -Platform win-x64|linux-x64|linux-arm64 -BundleMode Online|Offline`.
  Результат кладётся в `artifacts\dist`.
- `artifacts\` содержит также старые `*_publish_test` (под git не лежат).

---

## 6. Известные проблемы и ловушки (найдено при подготовке этого файла)

1. **`Powershell\СontainerManagement.ps1` сейчас не запустится.**
   - Первая буква имени файла — **кириллическая «С»** (байты `D0 A1`). При наборе
     с клавиатуры латинской C файл не найдётся.
   - В `switch` меню (около строк 891–893) битые ветки
     `"54" { Action53 -` и `"53" { Action53 - }`. Открывающих фигурных скобок 183,
     закрывающих 182 — PowerShell не разберёт скрипт.
   - Строка 15 подключает `DockerNetworkInfo.ps1`, которого в репо нет
     (пункты меню 00 и 03).

   До починки пользуйся прямыми `docker compose` командами из §5. По политике
   это меню — dev-инструмент, а не production deploy-интерфейс.
2. **`PG_PASS` и `AUTHENTIK_SECRET_KEY` в корневом `.env` и в `Authentic\.env` различаются.**
   В compose блок `environment:` перекрывает `env_file`, поэтому фактически
   действуют значения из корневого `.env`. Риск: если volume `authentic_database`
   был инициализирован с другим паролем, `authentic_server` не подключится к своей БД.
   При такой ошибке **не пересоздавай volume молча** — доложи пользователю.
3. **`Storage__HostRootPath` в compose = `C:\Develop\BusinessEntity\BusinessEntityStorage`** —
   это старое, ныне пустое расположение. Значение используется в
   `SpaceBackupService` и `SpaceRestoreService` для хостовых путей бэкапов.
   Фактическое хранилище: `C:\Develop\BusinessEntityProduct\BusinessEntity\BusinessEntityStorage`.
4. В git-индексе на момент записи лежат незакоммиченные изменения (вывод `.env`
   из-под git, добавление `.env.example`, правки `.gitignore` и README).
   Перед коммитом посмотри `git status` и не смешивай их со своими изменениями
   без согласования.
5. `Context\index.md` устарел в части хранилища (см. §2).

---

## 7. Чек-лист после подъёма (минимальный smoke)

- `docker compose ps` — все сервисы `running`, у БД и `authentic_server` статус `healthy`.
- `http://localhost:9000/-/health/live/` → 200.
- `http://localhost:7000` → 200 (редирект на логин Authentik — норма).
- `http://localhost:5080` → 200.
- В логах `business-entity-container` нет исключений bootstrap Authentik или `UserMiniApp`.
- Ручной smoke (§13 deployment-policy): вход под `admin`, выбор пространства, открыть
  документ, создать и сохранить тестовый документ, открыть администрирование, проверить
  запись в веб-логгере.

### Уточнение раскатки TASK_ARCH_001, этап 2

После `compose up --build` проверить, что `.Image` контейнера совпадает с ID
только что собранного образа. На этой машине stopped-контейнер однажды запустился
с прежним image ID. Надёжная последовательность: `docker compose build
business-entity web_logger`, дождаться завершения, затем `docker compose up -d
--no-build --no-deps --force-recreate business-entity web_logger`.

DataProvider теперь использует схему 2, UserMiniApp — собственную схему 1.
Запуск на прежней БД отклоняется, миграций и backfill нет. Пользователь разрешил
сброс business_entity и BusinessEntityStorage для этапа 2; он выполнен. Authentik,
web_logger и volumes не удалялись. Новые storage-пути и backup описаны в политиках.
