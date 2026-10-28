# Модули и маршруты

Фактическая регистрация этапа 6 TASK_ARCH_001. ModuleCatalog singleton получает
декларации host-service; WindowModularityManager scoped создаёт UI-адреса.
Декларации с AppSingleton — только Headless; внутренних вкладочных placement нет.

## Оболочка

| Место | DefinitionKey | ComponentKey | Доступ |
|---|---|---|---|
| LeftNav | NavigationMiniApp:Navigation.Main | navigation.main | публичный; ссылки фильтруются отдельно |
| TopBar | SysParametersMiniApp:SysParameters.Header | sys.company | публичный |
| TopBar | UserMiniApp:User.Header | user.header | публичный |
| User.HeaderSpaces (в меню пользователя) | SpacesMiniApp:Spaces.Header | spaces.header | публичный, собственные варианты authenticated/anonymous |
| LeftTree | TreeMiniApp:tree | tree.main | пользовательская проекция с правами |
| RightPanel | MediaServerMiniApp:MediaServer.Uploads | media.uploads | Authenticated |
| RightPanel | TreeMiniApp:Tree.SelectionInfo | tree.selection | сведения своего окна; адресный selection — этап 7 |
| RightPanel | UserMessagesMiniApp:UserMessages.Panel | messages.panel | Authenticated |

Политика UI оболочки — WindowSingleton. Постоянная геометрия, CSS frame и @Body
принадлежат MainLayout; конкретные компоненты оболочка не импортирует.

## Страницы

| URL | Владелец / DefinitionId | ComponentKey | Policy / guard |
|---|---|---|---|
| / | SpacesMiniApp / Spaces.Home | spaces.home | WindowSingleton, публичный |
| /space-selection | SpacesMiniApp / Spaces.Selection | spaces.selection | WindowSingleton, публичный по content-access policy (включая anonymous выбор) |
| /document/{id} | DocumentsMiniApp / Documents.Document | documents.page | PerContext, ReadContent, SaveContext |
| /rich-document/{id} | RichTextDocumentMiniApp / RichText.Document | richtext.page | PerContext, ReadContent, SaveContext |
| /print/{id} | RichTextDocumentMiniApp / RichText.Print | richtext.print | PerContext, ReadContent, PrintLayout |
| /profile | UserMiniApp / User.Profile | user.profile | WindowSingleton, Authenticated |
| /authinfo | UserMiniApp / User.AuthInfo | user.authinfo | WindowSingleton, Authenticated |
| /multimedia | MediaServerMiniApp / MediaServer.Library | media.library | WindowSingleton, Authenticated |
| /diagnostics | DiagnosticsMiniApp / Diagnostics.Page | diagnostics.page | WindowSingleton, GlobalAdmin |
| /logging | DiagnosticsMiniApp / Diagnostics.Logging | diagnostics.logging | WindowSingleton, GlobalAdmin |
| /administration | AdministrationMiniApp / Administration.Page | administration.navigation | WindowSingleton, GlobalAdmin или применимый AdminSpace |
| /administration/general | SampleDataMiniApp / SampleData.Administration | sample.administration | WindowSingleton, GlobalAdmin |
| /administration/system | SysParametersMiniApp / SysParameters.Administration | sys.administration | WindowSingleton, GlobalAdmin |
| /administration/spaces | SpacesMiniApp / Spaces.Administration | spaces.administration | WindowSingleton, AdminSpace; список/мутации фильтруются по конкретному space |
| /administration/users, /administration/users/{section} | UserMiniApp / User.Administration | user.administration | WindowSingleton, GlobalAdmin |

User sections: general, access, roles, groups, members. Это обычные URL/NavLink,
не локальные вкладки. Административные разделы показывают навигационный модуль
Administration отдельно от своего page-модуля и проверяют оба guard.

Вложенный ActivityMiniApp:Activity.Comments (`activity.comments`) — PerContext,
ReadContent; host получает EntityId/параметры отображения. Его компоненты вызывают
собственный ActivityCommentService напрямую. Авторизованная отправка — отдельная
транзакция комментария. История/поиск/оглавление статьи остаются обычными
локальными контролами документа, а не системой вкладок страниц приложения.

Login/auth/error и PrintLayout остаются инфраструктурой. TestChangeParent,
TestRemoveRelation и часы удалены. HTTP file/auth adapters сохраняют свои URL.

## Контексты сохранения

HasSaveContext объявляют Documents.Document, RichText.Document, User.Profile,
User.Administration, SysParameters.Administration, Spaces.Administration и
MediaServer.Library. Участники рабочих наборов живут в MiniAppSaveContext,
а не в компонентах. Менеджер создаёт EditSessionId в пределах окна/владельца/
ContextId; несколько представлений могут явно использовать один edit session.
Возврат dirty статьи читает базовую версию её автора; новые версии других окон
не подменяют содержимое этого черновика. Навигационный guard — этап 8.
У пространства нет aggregate dirty или общей версии.

## Команды

Host-service владельца отвечает на GiveMeButtons/HereAreButtons. Приёмники:
Nav.Main, Administration.Navigation, User.Administration.Navigation,
Document.Toolbar, Tree.ContextMenu. Binding runtime хранится scoped отдельно
от декларации. По шине нет EventCallback, Type, RenderFragment или IBrowserFile.

ButtonStateSnapshot содержит окно, ModuleId, EditSessionId, ContextId,
владельца и BindingId; DeclaredButton хранит адресный UI-кэш. Выполнение —
ButtonCommandInvoked/Result с корреляцией, серверным автором и nonce binding.
После снятия UI nonce не исполняется. Несколько кнопок одной команды могут
принадлежать одному модулю, не перезаписывая callbacks друг друга.

Новые возможности регистрируются в AddXxxMiniApp владельца. После изменения
регистрации обновляются этот документ, реестры миниаппов/сообщений и политики.

UI bindings переводят выполнение на dispatcher владельца; registry допускает
фоновую доставку и независимые bindings одной команды в одном модуле.
