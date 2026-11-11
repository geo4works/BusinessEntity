# Интерфейс и настройки BusinessEntity 0.18.0

TASK_UI_001, 11 ноября 2026 года.

`RightPanelMiniApp` размещает `AuxiliaryTabHost` в shell-slot RightPanel. Владельцы
объявляют AuxiliaryTab-модули с HostArea, Order, permission и WindowSingleton.
Состав и идентичности остаются у WindowModularityManager, выбранная вкладка —
состояние host-а. Новая вкладка добавляется регистрацией владельца без правки shell.
Первая постоянная вкладка — UserMessages.Panel. Старые selection/upload widgets
справа сняты; очередь загрузок с отменой осталась в Multimedia.

Collector UserMessages запускается барьером WindowRuntimeStartup до render Router и живёт в circuit.
PostUserMessage проверяет UserId и LogicalWindowId, журнал хранит последние 50
карточек и устраняет повтор CorrelationId. OperationNotice — renderless adapter
для прежних status-полей, собственный User UI продолжает публиковать своим handler.
Field validation, подтверждения и одноразовые credentials остаются в защищённой
форме. Ошибки и результаты не дублируются inline.

AdministrationLayout содержит один AdministrationMiniApp. SettingsPageDefinition
в Registration владельца связывает URL, публичный module key, контекст, порядок
и permission. Посещённые формы сохраняются смонтированными между разделами.
SettingsDraftSession координирует не-SQL drafts своего окна. Общий Apply включает
только явно отмеченные ApplyOnCenter page-модули и настройки оформления;
операционные формы User/Identity/API требуют своих Save/команд. После частичного
отказа подтверждённые участники имеют новую baseline, остальные сохраняют draft.
Уход из центра использует существующий Save/Discard/Cancel и beforeunload guard.

ThemeManager имеет одну JSON-схему: встроенный ресурс theme-schema.json одновременно
передаётся раннему browser bootstrap. BeTheme и ThemeCatalog валидируются до записи.
Builtin BusinessEntity Light неизменяема. Custom catalog, active ID и 16 palette slots
хранятся атомарно в localStorage `be.themes.v1:<local-user-guid>` своего origin.
Проверенный cookie UserId задаёт namespace раннего применения, предыдущий login
не запоминается. Единственный JS mapper применяется в bootstrap, runtime и preview.

Файл .betheme: marker BusinessEntityTheme, schemaVersion 1, полный theme; лимит
256 KiB, неизвестные/отсутствующие поля и значения вне диапазонов отклоняются.
Geo4 schema 19 не импортируется как BE. Каталог при повреждении сохраняется для
диагностики, UI остаётся на встроенной теме и выдаёт адресную ошибку. Ошибка quota
не подтверждает новую in-memory baseline. Preview локален окну и отменяем.

В редакторе Общий/Скроллбары/Таблицы/Сообщения. Геологические роли Geo4,
project themes, сплиттеры и центральный workspace не перенесены. Тема не меняет
содержимое пользовательских rich-text документов. Read-only builtin можно
дублировать; копия и импорт получают новые theme/style IDs и свободные имена.

Компоновка standard — user property типа ShellLayoutId=301, metadata Ui.ShellLayout,
значение standard. UserMiniApp проверяет контракт и пишет через свой штатный путь.
Отсутствие свойства означает standard; неизвестное значение наблюдаемо и не стирается.

Удалены /authinfo, /diagnostics, /logging и их регистрации. Профиль доступен через
аватар, штатные auth endpoints и web logger сохранены. Middleware пропускает
неизвестный Blazor URL в Router.NotFound без запроса выбора пространства.
