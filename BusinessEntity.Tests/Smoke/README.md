# Smoke AUTH_001 / API_001 / API_002

Текущие проверки используют настоящий OIDC/Authentik и отдельные явно подготовленные
служебные QA accounts. Секреты читаются из игнорируемого `.env`, в вывод не попадают.
Команды выполняются из корня репозитория; нужен Node.js, Playwright и Edge.
`BE_PLAYWRIGHT_MODULE` указывает на установленный модуль Playwright.

| Проверка | Что проверяет |
|---|---|
| `api-framework-smoke.cjs` | Реальный M2M, Bearer/session, typed self, scope isolation, filtered catalog, отзыв допуска, close |
| `api-crud-smoke.cjs` | Явные локальные роли/grants; Space/Folder/Document CRUD, authored snapshots, retries/restart, scopes, plans |
| `api-rich-smoke.cjs` | Два автора от старой базы, exact chunks/порядок, restore, publication, published-only reader |
| `api-upload-smoke.cjs` | Parts/restart/digest/ownership, image+версия, Media deferred create/CRUD/Range, внешний file reference |
| `api-job-smoke.cjs` | TXT/Markdown/HTML/emoji, append/replace, jobs, outcome после поздней отмены |
| `api-job-large-smoke.cjs` | Реальные 100 MiB и 1 GiB, session/restart/cancel, последовательность, RSS процесса, Range |
| `api-provider-switch-smoke.cjs --jobs` | A→B→A, доказанные identity, B при выключенном A, durable session, приостановка/reauthorize jobs, книга 100 MiB |
| `api-settings-smoke.cjs` | Общие UI/API settings, права system/space/self, отсутствие config/secrets, limits+1 |
| `api-ui-regression-smoke.cjs` | Создаёт собственное QA Space, запускает актуальные browser/navigation tests, удаляет только свои fixtures |
| `api-cleanup-owned-qa.cjs` | Удаляет только Space с точным ApiQA_ именем и автором QA service, через owner deletion plan |

Рекомендуемая последовательность: provider proof после полного CRUD grant;
затем framework → crud → rich → upload → job → settings → UI → owned cleanup.
Framework сознательно сужает QA grant до `user.self.read`; CRUD восстанавливает
полный QA grant. Helper ожидает загрузку формы перед выбором существующей записи.
Бизнес-вызовы используют plain fetch без browser cookie; UI нужен для явной
подготовки допуска/локальной роли и отдельно для browser regression.

`api-client-fixture.cjs` обновляет короткий access token, но не продлевает закрытую
session и не выполняет неявный job reauthorize. Parts передаются ограниченными
буферами; full SHA считается постепенно. `api-job-large-smoke` video — fixture
хранения original bytes: воспроизведение/кодек/транскодирование не проверяются.
Замер RSS отделён от cgroup page cache; точные выполненные результаты и старый/
новый размер чанка указаны в `Context/Tasks/TASK_AUTH_API_execution-log.md`.

Провайдерный smoke требует отдельного QA B из `identity-provider-b.compose.yml`.
Target users создаёт план BusinessEntity; совпадение имени не используется для
присвоения внешнего аккаунта. При прерывании перед возвратом A его контейнеры
возобновляются, но active provider нужно проверить: `--return-a` предназначен
только для явного возврата из уже активного B. Никаких volumes/reset не выполняется.
`provision-api-service-fixture --b --renew-plan-owned` допустим только для известных
QA service accounts с provisioning marker; сохраняет user/intent при PATCH.

## Браузерный редактор и навигация

`browser-smoke.cjs` использует OIDC A и `BE_SMOKE_SPACE` — имя заранее созданного
собственного QA Space. Проверяет дерево, оба редактора, сохранение/reload,
комментарий, изоляцию профильных уведомлений двух окон, administration и logger.
`BE_SMOKE_RESULT_FILE` сохраняет только идентификаторы/URL, не credentials.

`navigation-save-smoke.cjs` читает тот же JSON: Stay/Discard/Save, ошибка валидации,
независимые drafts двух окон, отмена beforeunload reload/close и forceLoad смены
пространства. Проще запускать оба через `api-ui-regression-smoke.cjs`, который
создаёт/удаляет QA Space и передаёт временный JSON результата.

## PostgreSQL tests

`dotnet test BusinessEntity.sln -c Release` с `BE_TEST_POSTGRES` на отдельный
тестовый кластер. Каждый test создаёт/удаляет только `be_test_<guid>` и свой
временный каталог. Без переменной реальные SQL checks явно skipped; это не
считается приёмкой. Не направлять runner на пользовательскую БД.

## Исторические smoke TASK_ARCH_001

Остальные scripts ниже документируют старую приёмку ARCH и её fixtures/login.
Их login/fixtures не адаптированы к AUTH_001; они не объявлены запущенной текущей
приёмкой. Текущие scenario evidence — таблица выше и execution log.

## Отказ БД на изолированном экземпляре

`database-failure-smoke.cjs` принимает только `BE_FAULT_URL=http://localhost:7001`.
Нужны отдельное приложение на порту 7001 и контейнер PostgreSQL `be-arch-test-db`,
БД `be_stage1`, тестовый пользователь `adm01`. Для этого PostgreSQL используется
`--tmpfs /var/lib/postgresql/data`, для приложения — `--tmpfs /app/test-storage`.
Конфигурация Authentik берётся из локального dev-окружения без вывода секретов.
Основная БД и файловое хранилище приложения не подключаются.

После загрузки документа и ввода черновика скрипт приостанавливает только тестовый
PostgreSQL, нажимает Save, проверяет явный отказ хранилища, открытый редактор
и сохранённый текст. `finally` снимает приостановку. Скрипт не удаляет volumes
и не запускается автоматически против пользовательской БД.

## Проверки хранения этапа 2

После успешного `browser-smoke.cjs` с `BE_SMOKE_RESULT_FILE` выполнить
`storage-smoke.cjs` с тем же файлом результата. Нужны свежие fixtures базового
прогона: расширение сознательно сохраняет и восстанавливает их версии.

Проверяются история Document, обновление второго окна, два автора статьи от
одной базы, выбор публикации и Restore, HTTP image upload/read, video upload и
Range-read, markdown import, backup и атомарный restore нового пространства с
проверкой исторического содержимого. Видео fixture проверяет хранение и bytes,
а не воспроизведение кодека. Скрипт оставляет новое тестовое пространство.

PostgreSQL-тесты запускаются с `BE_TEST_POSTGRES`, указывающей на отдельный
тестовый кластер, которому разрешено создавать/удалять БД `be_test_<guid>`.
Каждый тест сам создаёт и удаляет только свою БД и временный каталог. Без этой
переменной PostgreSQL-проверки явно skipped, что не считается приёмкой этапа.

`access-smoke.cjs` выполняется после basic + storage на том же
`BE_SMOKE_RESULT_FILE`: анонимный обычный документ показывает последнее содержимое,
статья и print — выбранную опубликованную версию; команды редактирования недоступны.

`route-guards-smoke.cjs` проверяет URL/reload/browser history административных
разделов, создаёт временного обычного пользователя, проверяет deny шести прямых
адресов, удаляет его в finally. Пароль генерируется в памяти и не выводится.
`access-smoke.cjs` создаёт отдельную guest-группу, сохраняет состав/назначение,
а затем очищает состав и удаляет группу. data-group-id подтверждает загруженный
сервером состав; надёжность cleanup дополнительно проверяется SQL после прогона.


## Master/helper

selection-smoke.cjs использует BE_SMOKE_RESULT_FILE после basic. Проверяет
первичный запрос выделения страницы, два окна одного пользователя и быстрое
Ctrl-переключение без изменения выделения второго окна. Структурные проверки
позднего helper, смены поколения, старой ревизии и disposal находятся в SelectionTests.


## Сохранение перед уходом

navigation-save-smoke.cjs выполняется после storage-smoke: меняет собственный
документ fixtures новыми Save. Проверяет Stay/Discard/Save, отказ валидации,
два окна с независимым dirty, отмену native beforeunload при reload и close,
Save/Discard/Cancel при forceLoad выбора пространства без второго предупреждения.
Unit-сценарии проверяют также Unknown/retry, поздний ввод при Save/Discard,
отзыв прав, отсутствие участника и ожидание незавершённой обработки ввода.
