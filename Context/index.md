# BusinessEntity — устройство приложения

Фактическая архитектура пакета 0.17.0 после TASK_ARCH_001. Источник требований —
[ТЗ v2.2](Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md), приёмка и
история раскатки — [журнал](Tasks/TASK_ARCH_001_execution-log.md).

BusinessEntity — Confluence-подобная база знаний: пространства, папки, обычные
документы, статьи с rich-text редактором, комментарии, мультимедиа и управление
доступом. Приложение работает на ASP.NET Core / Blazor Server (.NET 9).
Постоянные данные находятся в PostgreSQL; это не in-memory хранилище.

## Перед работой

Пройти `llmstart`, [правила работы](work_process_guide.md), [команды](routines.md),
политики и [контекст запуска](rollout-context.md). Код находится в `BusinessEntity/`,
миниаппы — в `BusinessEntity/MiniApps/`, logger — в `BlazorServerWebLogger/`.
Репозиторий Windows: `C:\Develop\BusinessEntityProduct\BusinessEntity`, ветка main.
Соседний Geo4Web разрешён только для чтения.

## Миниапп, шина, интерфейс

Миниапп изолирует предметную логику, а не обязательно отдельный .NET-проект.
Все миниаппы приложения остаются в одном проекте. Их публичная поверхность —
Contracts; чужие Internal, Components, Facade, Storage и Repositories недоступны.
Внутри владельца сервисы вызываются напрямую. Между владельцами — только
типизированные сообщения единственного singleton ReactiveUI.IMessageBus.

Коннектор использует BusRequestClient: подписка до отправки, RequestId,
таймаут, отмена, освобождение ожидания и BusError. Он не вызывает чужой фасад.
Обычное отсутствие необязательного объекта отделено от отказа. Отсутствующие
обязательные данные, неизвестные тип/формат или повреждённый payload не подменяются
пустыми объектами или чтением через другую реализацию.

Сообщения адресуются серверными UserId, LogicalWindowId, ModuleId,
EditSessionId и ContextId. Одного UserId недостаточно для двух окон. Scoped
endpoint проверяет адрес до работы и освобождает подписки. HTTP/фоновые операции
имеют собственный scope и явного пользователя либо системного автора.

[Правила шины](Policy/miniapp-reactivebus-architecture-guide.md),
[границы](Policy/architecture-principles.md), [сообщения](Registry/bus-messages-catalog.md).

## Общий движок

Core содержит BusinessEntity, BusinessEntityData, BusinessEntityDataChunk,
BusinessEntityDataBlock, связи, общие контракты и сервисы движка. Единственный
BusinessEntityHelper обеспечивает общие операции графа и подготовку пакетов.
Миниаппы используют его через DI, без копирования алгоритмов.

Core не знает Document, RichTextDocument, Folder, Space, MediaVideo или
SysParameters как предметные классы. Владельцы регистрируют IEntityTypeDescriptor:
ключ/ID, начальные данные, правила имени/вложения, page route и cloning.
Типизированные payload находятся в Contracts владельцев, их конвертеры — у
владельцев. Из миниаппов Core может ссылаться только на Contracts DataProvider.

Infrastructure содержит шину, модульность, команды, master/helper, dirty/Save,
навигационный guard и общие UI-компоненты. Это общий механизм, а не обход
межминиаппных границ. Auth/HTTP adapters и транспорт web-логгера — инфраструктура.

[Политика Core](Policy/core-engine-policy.md).

## Страницы и модули

MiniAppHostService объявляет metadata модулей и кнопок. ModuleCatalog собирает
декларации и последовательно запускает фоновые runtime. WindowModularityManager
создаёт UI ModuleId своего circuit, связывает модули с edit session и публикует
Opened/Activated/Deactivated/Closed.

Размещения: Headless, ShellModuleHostArea, Page. Политики экземпляров:
AppSingleton для фоновых модулей, WindowSingleton, PerContext и MultiInstance.
Пользовательский редактор не становится AppSingleton.

MainLayout предоставляет LeftNav, TopBar, LeftTree, Body и RightPanel.
Компоненты выбираются через реестр ComponentKey владельца. Страница содержит
маршрут, guard и PageModuleHost. Документы и статьи имеют отдельные URL;
переход открывает другую страницу. Для одновременной работы открывается другое
окно браузера. Внутренних вкладочных host-ов, часов и тестовых страниц нет.
Локальные инструменты статьи не являются системой вкладок приложения.

GiveMeButtons/HereAreButtons передают декларации; ButtonStateSnapshot — адресное
состояние. ButtonCommandInvoked проверяет живой binding, окно, модуль, edit/context,
доступность и актуальное право. По шине не передаются Razor, delegates или IBrowserFile.

[Модули/маршруты](Registry/modules-and-routes.md),
[модульность](Policy/MiniApps/modularity-policy.md),
[команды](Policy/MiniApps/buttons-declaration-policy.md).

## Сохранение

Единица dirty — рабочий экземпляр миниаппа в окне и предметном контексте.
Один набор может включать несколько связанных сущностей и несколько UI-модулей.
MiniAppSaveContext удерживает участника, отправленную ревизию, frozen пакет,
OperationId и состояние Idle/Saving/Saved/Failed/Unknown.

Участник фиксирует значения через Capture до await; Prepare работает с этим
снимком. Save отправляет весь набор в DataProvider. Подтверждается только
отправленная ревизия, новые изменения остаются dirty. Неизвестный исход сохраняет
тот же пакет для проверки/повтора. MiniAppSaveStateChanged публикует адресный
immutable статус рабочего экземпляра на общей шине.

Диалог ухода предлагает сохранить, отбросить или остаться. Переход и Save ждут
обработки ввода; отказ, отсутствие участника, новые правки и неизвестный исход
не очищают черновик. beforeunload предупреждает при reload/close; подтверждённый
forceLoad не задаёт вопрос повторно. Черновики находятся в памяти своего circuit.

Отдельные завершённые действия, например комментарий, могут сохраняться сразу.
Dirty, версия, история или общий Save пространства не вводятся.

[Сохранение](Policy/MiniApps/miniapp-saving-policy.md),
[контексты](Registry/save-contexts.md).

## Хранение и история

Только DataProvider пишет канонический граф и постоянные файлы. Его EF DTO,
репозитории, DbContext и codec — внутренние. Исключение: UserMiniApp владеет
своими служебными таблицами пользователей/ролей/групп. Формы User передают изменения
своему writer, выполняемому в SQL-транзакции DataProvider вместе с outcome.
Внешние операции Authentik не участвуют в SQL-транзакции.

Каждый пакет применяет связанные изменения БД атомарно. DataProviderOperations
сохраняет OperationId, hash, автора, время и результат той же транзакцией.
Потерянный ответ не доказывает откат; исход доступен после рестарта. Повтор
одинакового пакета не дублирует запись. Retention по умолчанию — 7 дней.

Структурный кэш DataProvider — immutable snapshot карточек/связей/свойств и
версий. Прогретые структурные запросы не идут в SQL. Права рассчитываются отдельно.
Тяжёлые payload имеют ограниченный LRU; не все тела хранятся в памяти постоянно.
После commit публикуется подготовленный кэш, затем события изменений DataProvider.

Первое сохранение статьи создаёт v1. Название/контент увеличивают версию;
no-op и публикация — нет. Каждая версия — полное состояние автора от его базы.
Сохранение B от v1 после сохранения A не смешивает правки A с правками B.
Точный состав версии ссылается на конкретные строки чанков. Публикация хранится
отдельно от строк истории. Восстановление версии создаёт новую версию.
Обычный Document хранит свой полный payload версии; типы без истории обновляют v1.

Неизменяемые файлы подготавливаются до SQL commit. Manifest, исторические
зависимости и recovery обеспечивают согласованность после сбоя. Backup формата 2
включает подграф, всю историю, публикацию, свойства, комментарии и файлы. Restore
атомарно создаёт новые идентичности. Старые схемы/архивы/форматы не мигрируются.

[Граф и Postgres](Policy/graph-storage-policy.md),
[история](Policy/business-entity-versioning-policy.md),
[файлы](Policy/file-object-storage-policy.md), [backup/restore](Policy/BackupRestore/README.md).

## Доступ и владельцы

Identity предоставляет Authentik; локальная cookie-сессия обновляет токены.
UserMiniApp владеет локальными пользователями, ролями, группами, assignments,
профилем и правилами доступа. Он предоставляет публичные контракты остальным.
Скрытие ссылки/кнопки не заменяет серверную проверку. Anonymous получает только
разрешённые страницы и опубликованное содержимое статьи; обычный Document читает
последний сохранённый payload по своей политике.

Прежняя раздача файлов по прямым HTTP-ссылкам не получила полного ACL-контроля;
по решению пользователя это отдельная
[задача TASK_SECURITY_001](Tasks/TASK_SECURITY_001_file-download-authorization.md).

[Права](Policy/Users/content-access-rights-policy.md),
[миниаппы и старт](Registry/miniapps-registry.md).

Master/helper использует immutable ObjectInContextSelected с окном, источником,
его поколением и ревизией. Helper отвергает устаревшее/чужое, очищается при закрытии
источника и запрашивает снимок при позднем создании. Общего mutable selection нет.

## Проверки и эксплуатация

`BusinessEntity.Tests` содержит unit, архитектурные и реальные Postgres-тесты.
Postgres-тесты требуют BE_TEST_POSTGRES отдельного тестового кластера; skipped
не считается приёмкой. Playwright smoke запускается против Docker отдельно.
[Инструкция smoke](../BusinessEntity.Tests/Smoke/README.md).

[Docker и RSTT](rollout-context.md), [deployment](Policy/Deployment/README.md),
[capability toolset-ы](Policy/MiniApps/capability-toolsets-policy.md).
Коммиты/версия/CHANGELOG — по командам пользователя. Тома и пользовательское
файловое хранилище физически удаляются только по явному разрешению.
