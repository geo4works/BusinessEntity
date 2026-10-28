# Реестр миниаппов

Фактическое состояние этапа 5 TASK_ARCH_001, 2026-10-28.
Приложение содержит владельцев из таблицы и регистрации типов. Каждый AddXxxMiniApp регистрирует один декларативный singleton MiniAppHostService. Общий runtime сохранения реализован; предметные редакторы подключаются в этапах 6/8.

| Миниапп | Назначение | Текущее время жизни | Доступ и состояние |
|---|---|---|---|
| DataProviderMiniApp | Граф, payload, чанки, properties, файлы | Writer/readers/cache/handlers singleton; connector scoped | Типизированные Bus Request/Response; codec только внутри DataProvider, connector использует BusRequestClient; SQL и постоянные файлы закрыты внутри Internal; единственная запись — SaveEntityBatch |
| UserMiniApp | Identity, локальные пользователи, роли, группы, права | Scoped | Connector через BusRequestClient; отдельный UserIdentitySession, scoped session/general handlers с фильтром адреса |
| TreeMiniApp | Дерево пространства и действия над узлами | Scoped | Bus connector без facade; scoped handler проверяет scope/окно перед исполнением |
| ActivityMiniApp | Комментарии | Scoped | Bus connector без facade; scoped handler; комментарии пишет и читает DataProvider через шину |
| MediaServerMiniApp | Видео и фоновые загрузки | Service scoped; registry/manager singleton, runtime каталога | Прямой API; фоновая очередь передаёт серверный UserId в отдельный scope; постоянные файлы и metadata пишет DataProvider; UI/контроллер — этапы 5–6 |
| SampleDataMiniApp | Начальные данные | Scoped | Сервис запускает SampleDataStartupRuntime каталога после defaults |
| BackupRestoreMiniApp | Архивы и расписание | Scheduler singleton runtime; restore scoped | Каталог запускает background после seed; export/import через DataProvider |
| UserMessagesMiniApp | Адресованные пользователю сообщения | State/handler/facade scoped; flash-store singleton | PostUserMessage/ClearUserMessages адресуются пользователю и окну; корреляция исключает повторную доставку |

Регистрации: [MiniApps](../../BusinessEntity/MiniApps).
Фактические контракты: [каталог сообщений](bus-messages-catalog.md).

## Регистрации владельцев типов этапа 2

DocumentsMiniApp и RichTextDocumentMiniApp объявляют ownership, правила версий,
архивов и URL. SpacesMiniApp и SysParametersMiniApp объявляют ownership и архивные
правила. Их UI-модули зарегистрированы на этапе 5; SaveParticipant подключаются на этапе 6.
TreeMiniApp объявляет владельца Folder, MediaServerMiniApp — MediaVideo.
Core использует общие EntitySaveBatchFactory и GraphArchiveRemapper; конкретные
типы в оставшихся моделях и helper устраняются на этапе 6.

## Общий слой

Core содержит модели, фабрики и BusinessEntityHelper. Миниаппами они не являются.
До этапа 6 helper ещё знает конкретные типы; К-1 заменяет эти зависимости
описателями от владельцев. Infrastructure предоставляет единый транспорт,
жизненный цикл и механизм сохранения. Внутренние вкладочные хосты не вводятся.

## Обновление

Новый миниапп, module definition, handler, время жизни или публичный контракт
фиксируется здесь вместе с кодом. После этапа 4 реестр содержит фактические
host-service, размещения и владельцев контекстов сохранения.
Источник: [ТЗ v2.2](../Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md).

Кэш DataProvider: согласованный structural snapshot, ограниченный payload LRU,
публичные проекции навигации и статистика. Владельцы UI применяют права к копиям
проекций. User SQL учитывается отдельно от storage counters.

## Фактические модули этапа 4

| DefinitionKey | Размещение / политика | Запуск |
|---|---|---|
| DataProviderMiniApp:storage | Headless / AppSingleton | schema/files/cache, порядок 0 |
| UserMiniApp:identity-bootstrap | Headless / AppSingleton | Authentik, 10 |
| UserMiniApp:defaults | Headless / AppSingleton | собственная схема/defaults, 20 |
| SysParametersMiniApp:defaults | Headless / AppSingleton | обязательный singleton, 30 |
| SampleDataMiniApp:seed | Headless / AppSingleton | seed, 40 |
| MediaServerMiniApp:uploads | Headless / AppSingleton | background queue, 50 |
| BackupRestoreMiniApp:scheduler | Headless / AppSingleton | background backup, 60 |
| TreeMiniApp:tree | ShellModuleHostArea LeftTree / WindowSingleton | tree.main |

Другие host-service пока объявляют пустой набор UI (перенос этапа 5).
ModuleCatalog singleton принимает поздние объявления; WindowModularityManager
scoped держит адреса и общие MiniAppSaveContext. AppSingleton не допускает UI/dirty.
IModuleUiFactory проверяет локальный реестр типов; шина переносит только ключи.
Снятие последнего dirty UI сохраняет runtime неактивным до возврата/guard;
старый неактивный адрес не исполняет команду. Reconnect сохраняет circuit;
новый circuit после аварии не восстанавливает локальные черновики.

## UI-владельцы этапа 5

NavigationMiniApp (LeftNav), AdministrationMiniApp (навигационный вход),
DiagnosticsMiniApp (diagnostics/logging) имеют свои декларативные host-service.
Spaces предоставляет home/selection/admin и меню пространств; User — identity
header/profile/authinfo/admin; SysParameters — company header/admin; Documents
и RichText — собственные страницы; Activity — вложенный page комментариев.
Media — library/uploads; Tree — tree/selection helper; UserMessages — общая панель.
Всего 15 логических владельцев, без отдельных проектов на каждый.

Полный актуальный список UI definition/component keys, URL, guard и save-context
находится в [реестре модулей и маршрутов](modules-and-routes.md). Таблица headless
выше продолжает действовать. Остатки смешанных Services/Core/Models и вложенных
rich-text компонентов переносятся/разделяются этапом 6, старый selection — этапом 7.
