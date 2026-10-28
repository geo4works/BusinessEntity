# Контексты сохранения

Фактические HasSaveContext и участники пакета 0.17.0. Менеджер создаёт контекст
в пределах LogicalWindowId + MiniAppId + ContextId. EditSessionId отличается
от ModuleId; несколько UI-модулей могут использовать один рабочий экземпляр.

| Владелец / определения | ContextId | Участник | Пакет |
|---|---|---|---|
| DocumentsMiniApp / Documents.Document | ID документа | DocumentsWorkingSet | полные изменённые Document payload, весь набор |
| RichTextDocumentMiniApp / RichText.Document | ID статьи | RichTextWorkingSet | авторская база, название, описание, все dirty HTML и файлы |
| UserMiniApp / User.Profile, User.Administration | null | UserFormsWorkingSet | изменённые profile/account/role/group/members forms, служебные DTO одной SQL-транзакцией |
| SysParametersMiniApp / SysParameters.Administration | null | EntityPayloadWorkingSet | полный изменённый singleton payload |
| SpacesMiniApp / Spaces.Administration | null | SpacePropertiesWorkingSet | свойства и переименования всех изменённых карточек пространства |
| MediaServerMiniApp / MediaServer.Library | null | MediaVideoWorkingSet | metadata всех изменённых видео |

WindowSingleton уникален для вида UI, а контекст сохранения — для владельца и
предметного ContextId. Поэтому User.Profile и User.Administration одного окна
могут разделять свой рабочий набор; другое окно получает другой EditSessionId.

MiniAppSaveStateChanged несёт окно, пользователя, владельца, edit/context,
StateRevision, EditRevision, IsDirty, Status и OperationId. StateRevision растёт
в окне при публикации новых значений; подписчик отсекает чужое окно/устаревшее.
Событие не содержит payload, паролей или ссылок на компоненты.

SaveStateIndicator читает тот же контекст. UnsavedChangesGuard до перехода ждёт
ввод, вызывает владельца всего набора и не разрешает уход при новом dirty.
Unknown удерживает пакет/OperationId; Discard до выяснения исхода запрещён.
Последний dirty-модуль нельзя молча закрыть. Финальный lifecycle закрывает адрес,
подписки и состояние окна, без автоматического Save при его выгрузке.

Контекст административной формы пространства не является aggregate dirty Space.
Общей версии пространства и глобального Save нет. Activity comments, создание,
удаление, публикация, upload и другие законченные сценарии могут отправлять
отдельные немедленные транзакции согласно политике владельца.

[Модули](modules-and-routes.md), [механика Save](../Policy/MiniApps/miniapp-saving-policy.md).
