# TASK_ARCH_001 — итоговый аудит 0.17.0

Дата: 2026-10-29. Норма — ТЗ v2.2; фактическое выполнение и коммиты 0–9 —
[журнал](TASK_ARCH_001_execution-log.md). Исходная точка 5d34cda / 0.16.2.

## Итоговые проверки

- Полная Release-сборка решения: 0 ошибок. Исходные nullable/async и NU1903
  AutoMapper не подавлены; это не сборка без предупреждений.
- Полный dotnet test: 126 passed, 0 failed, 0 skipped. Из них 35 — реальные
  Postgres-тесты в отдельном кластере с одноразовыми БД; остальные — unit/архитектура.
- RSTT: оба образа собраны, приложение и logger пересозданы. Image ID приложения
  `5b7426965c7caf7fc2e02b3ec3514b3848820f4c309f465c6ea330e3ff14dd35`
  совпал с собранным образом. В контейнере подтверждён BusinessEntity/0.17.0.
- Один финальный browser runner завершился exit 0: 33 сценария — basic 9,
  selection 2, storage/history/files/backup 6, anonymous 3, settings/media 2,
  navigation/native 8, routes/guards 3. Проверки старых неуспешных попыток не засчитаны.
- Native reload/close предупреждает; отказ оставляет черновик. Смена пространства
  через forceLoad проходит диалог без второго browser-вопроса. Два окна независимы.
- Диалог визуально осмотрен при ширине 1600px. Кнопки помещаются, dirty читаем;
  его фактический цвет rgb(122,75,0). Локальный визуальный черновик отброшен через UI.
- Каталог сверен с 168 текущими records; пропущенных Request/Response нет.
  Битых относительных ссылок в index/rollout/политиках/реестрах нет.

Standalone database-failure-smoke текущим финальным browser-прогоном не запускался.
Отказ/rollback/retry БД проверены реальными SQL-тестами, Unknown и late input —
unit-тестами, сохранение редактора при отказе — также browser validation-сценарием.

## §6 — инварианты

| ID | Результат | Основание |
|---|---|---|
| CHK-01 / 01a | ✓ | один зарегистрированный IMessageBus; BusRequestClient, адресные endpoint-ы; BusRequestClientTests |
| CHK-02 | ✓ | BusError; absence отделено от failure; типизированные пары Request/Response |
| CHK-03 | ✓ | строгий codec/registry, required singleton, StoredPayloadFormatTests/EntityTypeExtensibilityTests |
| CHK-04 | ✓ | SQL DbContext только DataProvider/User; архитектурный тест; filesystem-аудит исключений ниже |
| CHK-05 | ✓ | canonical data events только DataProviderCommitPublisher после commit/cache; TransactionBusTests |
| CHK-06 | ✓ | immutable graph snapshot, отдельные права, bounded payload LRU; GraphCacheTests |
| CHK-07 | ✓ | один metadata host-service на владельца, без предметного runtime |
| CHK-08 | ✓ | new MiniAppModule только ModuleCatalog и WindowModularityManager; ModularityTests |
| CHK-09 | ✓ | startup через каталог/headless runtime; Program регистрирует владельцев, без ручных EnsureInitialized |
| CHK-10 | ✓ | MainLayout содержит именованные места; ShellModuleHostArea/реестр компонентов |
| CHK-11 | ✓ | URL/guard/PageModuleHost; auth/error/print исключены инфраструктурно; route smoke |
| CHK-12 | ✓ | декларации/state/binding/address; CommandTests, toolbar/context menu/browser |
| CHK-13 | ✓ | lifecycle, stale binding отказ, disposal подписок, отмена UI; tests + чистый runtime-журнал |
| CHK-14 | ✓ | DomainBoundaryTests проверяет using/тела C#/Razor; shared layers не обходят Internal владельцев |
| CHK-15 | ✓ | один BusinessEntityHelper в Core; общие Entity/Data/Chunk/Block/Relation, без копий |
| CHK-16 | ✓ | own UI/services вызывают владельца напрямую; self-connector test; UserHeader исправлен в аудите |
| CHK-17 | ✓ | immutable ObjectInContextSelected, окно/источник/поколение/ревизия; SelectionTests и 2 browser сценария |
| CHK-18 | ✓ | MiniAppSaveContext, рабочие наборы, Save всего набора, адресный dirty event; нет aggregate Space dirty |
| CHK-19 | ✓ | реестры 15 владельцев, маршрутов, 168 сообщений и рабочих наборов сверены с исходниками |
| CHK-20 | ✓ | единый SQL package/outcome, UserOwnedStorageWriter в общей транзакции; TransactionTests/UserOwnedTransactionTests |
| CHK-21 | ✓ | singleton writer сериализует Save; одинаковый OperationId не повторяет запись; SQL-тесты |
| CHK-22 | ✓ | v1 первого Save, content/title increments, no-op/publication без инкремента; TransactionTests |
| CHK-23 | ✓ | точные ChunkId/ChunkVersion состава; старые строки после вставки/удаления читаются; SQL/browser |
| CHK-24 | ✓ | immutable objects, manifest/FK, fault hooks/recovery/streaming; FileTransactionTests |
| CHK-25 | ✓ | страницам соответствуют URL, host вкладок отсутствует, часы/тестовые экраны удалены; routes smoke |
| CHK-26 | ✓ | revision acknowledgement, frozen retry, поздний ввод/Discard/ошибка/Unknown; рабочие наборы и guard tests |
| CHK-27 | ✓ | scoped drafts/master/helper/commands/status с window IDs; tests и browser двух окон |
| CHK-28 | ✓ | Core знает только Contracts DataProvider; предметное через описатели; extensibility test типа 8001 |
| CHK-29 | ✓ | DataProviderOperations в той же SQL-транзакции; новый writer читает исход после restart |
| CHK-30 | ✓ | A/B от одной базы дают полные независимые снимки; Restore создаёт новую версию; SQL/browser |
| CHK-31 | ✓ | PublishedVersion карточки вне payload history; публикация не меняет строки версий; SQL/browser |
| CHK-32 | ✓ | нет миграций/старых adapters/форматов; три совпадения поиска лишь явно отвергают другую схему |

## Область проверок и находки

Скан собственных C#/Razor/JS по legacy/compat/совместим/для старого/ALTER TABLE/
IF NOT EXISTS/NormalizeVersion: три строки в DataProviderSchema/UserStorageSchema
про отказ несовместимой схемы. Generated vendor bundle не считается кодом адаптера
старого API приложения. Старых selection API/EntityMessages в действующем коде нет.

Вне DataProvider разрешённые IO: служебная БД User; входящие временные uploads
Media; внешние backup-копии из export leases и restore reports; seed-trace/логи.
Это не запись canonical entity objects в обход DataProvider. CommonOperationResult
сохранён: он действительно используется отдельным web-логгером и не является
bus-конвертом миниаппов. Неиспользуемый Functions и tracked .objtmp cache удалены.

Проверка конструкторов всех зарегистрированных implementation types не нашла
семи и более DI-зависимостей. Шесть у оставшихся сервисов пояснены в коде.
Metadata/factory-аргументы EntityTypeDescriptor не являются DI-конструктором.

Итоговый аудит дополнительно выявил и исправил отсутствие отдельного typed dirty
события и один own UserHeader→UserConnector самозапрос. Добавлены автоматические
проверки state/address/revision, self-connectors, владельцев SQL и DI budget.

## Данные и ограничения

После разрешённого этапа 2 данные/volumes не сбрасывались. Тесты не используют
production DB для своих SQL fault cases. Финальные smoke-документы и restore
пространства оставлены; тестовые пользователи/группы очищены через UI.

Черновики живут в памяти circuit: потеря процесса не обещает восстановить локальный
ввод. beforeunload предупреждает, но не выполняет ненадёжный async Save при выгрузке.
SQL outcome переживает restart и доступен по OperationId в установленный retention.
Внешний Authentik API не откатывается общей SQL-транзакцией. Текущий контур рассчитан
на один серверный процесс с singleton DataProvider; кластерный writer/cache не вводился.
Конкретные capability toolset-ы, dirty/history Space и вкладочный UI вне объёма.

Отдельная обнаруженная развилка: прямой GET тестового video original без cookie
вернул 200. GET изображений также не спрашивает права документа в controller.
Это прежний режим HTTP-раздачи, сохранённый раскаткой, а не новый контроль доступа.
Объём его усиления вынесен пользователю; до решения публичная семантика файлов
не меняется. Страничные guards, draft/published UI и серверные права Save проверены.
Полная авторизация файлов по ссылке не объявляется реализованной этим пакетом.
