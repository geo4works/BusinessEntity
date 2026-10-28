# Реестр миниаппов

Фактическое состояние итогового пакета TASK_ARCH_001, 0.17.0, 2026-10-29.
15 логических владельцев внутри одного проекта. Каждый AddXxxMiniApp регистрирует
один декларативный singleton MiniAppHostService. Core/Infrastructure — общие слои.

| Владелец | Сценарии и состояние | Транспорт и storage |
|---|---|---|
| DataProviderMiniApp | Singleton writer, graph snapshot и bounded payload LRU, история, файлы | Все canonical записи пакетами; scoped connector → общая шина |
| UserMiniApp | Scoped identity, accounts/profile, roles, groups, assignments, permissions, preferences | Внешние клиенты — IUserConnector; собственные вызовы прямые; свои служебные таблицы — исключение |
| TreeMiniApp | Scoped дерево, внутренние VM узлов и операции над выделенным набором | ITreeConnector; создание через общий Core и описатели; Move/Delete всего набора атомарны |
| DocumentsMiniApp | Модель документа, редактор и DocumentsWorkingSet в edit session | Общий SaveContext → DataProvider; версия по имени/тексту |
| RichTextDocumentMiniApp | Модель статьи, вложенные компоненты и client source, reader/edit/search/outline/import/files/print | RichTextWorkingSet фиксирует HTML до await; подготовка файлов и один Save пакет |
| SpacesMiniApp | Контекст выбранного пространства, home/selection/admin, формы имён и свойств | ISpacesConnector; SpacePropertiesWorkingSet принадлежит административному модулю |
| SysParametersMiniApp | Startup singleton, header и форма параметров | Тип и codec у владельца; EntityPayloadWorkingSet сохраняет параметры без истории |
| MediaServerMiniApp | Каталог, HTTP/files, upload queue и библиотека | Внешнее чтение списка — IMediaLibraryConnector; MediaVideoWorkingSet сохраняет metadata набора одним пакетом |
| ActivityMiniApp | Scoped комментарии | IActivityConnector; создание комментария — самостоятельная завершённая транзакция |
| SampleDataMiniApp | Начальная заливка, административная команда | Startup runtime; внешний ISampleDataConnector, собственный UI вызывает свой сервис |
| BackupRestoreMiniApp | Singleton scheduler и scoped restore, собственные archive/path services | IBackupRestoreConnector; canonical graph export/import через DataProvider |
| UserMessagesMiniApp | Scoped state/handler/service, singleton flash-store | Post/Clear адресованы пользователю и окну; свой UI вызывает сервис напрямую |
| NavigationMiniApp | Главное меню и ссылки объявленных владельцами возможностей | LeftNav, WindowSingleton |
| AdministrationMiniApp | Административные ссылки | Page, WindowSingleton |
| DiagnosticsMiniApp | Диагностика и просмотр логов | Page, WindowSingleton; чужие сценарии через коннекторы |

## Общий движок и типы

Core содержит Entity/Data/Chunk/Block/Relation, BusinessEntityHelper, фабрику,
реестр IEntityTypeDescriptor, общие графовые операции и архивный remapper.
В Core нет конкретных payload-классов и enum-списка предметных типов.
BusinessEntityTypeEnum оставляет технический Undefined; стабильные числовые
идентификаторы объявляют владельцы. Новый тип не требует правки Core.

| ID | TypeKey / владелец | Публичная модель |
|---|---|---|
| 100 | Space / SpacesMiniApp | Contracts/Entities/Space |
| 200 | Folder / TreeMiniApp | Contracts/Entities/Folder |
| 300 | Document / DocumentsMiniApp | Contracts/Entities/Document |
| 350 | RichTextDocument / RichTextDocumentMiniApp | Contracts/Entities/RichTextDocument |
| 400 | SysParametersTp / SysParametersMiniApp | Contracts/Entities/SysParameters |
| 500 | MediaVideo / MediaServerMiniApp | Contracts/Entities/MediaVideo |

Описатель задаёт initial payload/chunks, имя, видимость, допустимых родителей,
singleton и route. Порядок/имена physical kind и текущие JSON не менялись от переноса.
Typed storage converters, version/archive policies регистрируют владельцы.
DataProvider хранит общий codec/envelope/chunk contract и техническую фабрику.

## Запуск и рабочие экземпляры

ModuleCatalog запускает headless runtime по порядку: DataProvider storage 0,
User identity 10, User schema/defaults 20, SysParameters defaults 30, Sample seed 40,
Media queue 50, Backup scheduler 60. Остановка — в обратном порядке.
Host-service не создаёт UI и не исполняет сценарии.

WindowModularityManager создаёт ModuleId и edit session. Контекст сохранения
держит участника-владельца, переживающего снятие dirty UI. AppSingleton не держит
пользовательские черновики. Новый circuit после аварии не восстанавливает память.

Documents, RichText, User profile/admin, SysParameters admin, Spaces admin и Media
library используют Save/Discard рабочего набора. Успех подтверждает отправленную
ревизию; поздние правки остаются dirty. Навигационный guard и beforeunload подключены общим слоем.
Dirty пространства и общий Save приложения отсутствуют.

Служебные формы User применяются UserOwnedStorageWriter через открытую транзакцию
DataProvider; outcome и строки User коммитятся вместе. Writer не получает DbContext
бизнес-графа. Authentik API — внешняя операция подготовки, вне SQL rollback;
повтор готового пакета не вызывает её ещё раз. Пароли в SQL-пакет не входят.

Выбранное пространство — scoped контекст окружения из Contracts Spaces; его
значения читают другие владельцы. Предметные операции Spaces/Backup/Media/Sample
передаются через шину. Выделение передаётся immutable событиями master/helper с адресом окна, источником, поколением и ревизией. Старый selection API удалён.

[Маршруты, размещения и компоненты](modules-and-routes.md).
[Каталог сообщений](bus-messages-catalog.md). [ТЗ](../Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md).
