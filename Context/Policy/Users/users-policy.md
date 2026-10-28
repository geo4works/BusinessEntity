# Политика пользовательской области

## 1. Назначение документа

Этот документ фиксирует архитектурную политику пользовательской области в системе `BusinessEntity`.

Документ описывает:

- источник пользовательской identity
- границу ответственности `Authentik`
- границу ответственности `UserMiniApp`
- локальную модель пользователя приложения
- хранение технических пользовательских данных
- правила доступа к текущему пользователю из UI, сервисов и других mini-app
- ограничения на развитие пользовательского CRUD внутри приложения

Документ является нормативным описанием текущего user-контура. При изменении login, claims, групп, локального user-storage или пользовательских properties он должен обновляться.

---

## 2. Главный принцип

`Authentik` является внешним identity provider и основным источником пользовательской identity.

`BusinessEntity` не должен становиться самостоятельной системой управления пользователями, паролями, группами и ролями до отдельного архитектурного решения.

В текущей модели приложение:

- показывает локальную форму login/password или, как fallback, перенаправляет пользователя на login в `Authentik`
- получает от `Authentik` tokens и claims
- создает локальную cookie-сессию
- нормализует claims в объект `BusinessEntityUser`
- при необходимости материализует пользователя в локальной таблице `Users`
- хранит app-specific пользовательские данные в `UserProperties`

Приложение не должно:

- хранить пароли пользователей
- самостоятельно менять группы и роли в `Authentik`
- дублировать полный user directory
- считать локальную таблицу `Users` источником прав доступа

---

## 3. Источник identity и прав

### 3.1. Authentik

`Authentik` отвечает за:

- логин
- logout
- password management
- primary user profile
- группы
- claims
- membership пользователя в административных группах

Если нужно создать пользователя, сменить пароль, отключить учетную запись, добавить пользователя в группу или изменить роль, источником истины остается `Authentik`.

В приложении допускается только явно описанная в разделе 11 тонкая интеграция с Authentik API для пользователей текущего приложения.

### 3.2. Cookie-сессия приложения

После успешного login приложение создает локальную cookie-сессию.

В cookie auth-properties могут храниться:

- `access_token`
- `refresh_token`
- `id_token`
- срок действия token flow

Cookie-сессия нужна только для работы приложения. Она не является отдельной пользовательской учетной записью.

### 3.3. Claims

Claims, полученные из `Authentik`, являются входным сырьем для локальной user-модели.

Raw claims не должны разбираться повторно в разных UI-компонентах и сервисах. Если нужна пользовательская информация, нужно обращаться к `UserMiniApp`.

---

## 4. UserMiniApp

`UserMiniApp` является единой user-подсистемой приложения.

Основная директория:

```text
BusinessEntity/MiniApps/UserMiniApp
```

Его ответственность:

- получить текущего пользователя из `AuthentikSessionManager`
- нормализовать claims
- извлечь группы
- собрать `BusinessEntityUser`
- отдать текущего пользователя через mini-app contract
- предоставить `IUserConnector` для UI, сервисов и других mini-app
- материализовать локального пользователя в `Users`, если для сценария нужны app-specific данные
- хранить технические пользовательские properties в `UserProperties`

`UserMiniApp` является границей user-домена внутри приложения. Новая логика, связанная с текущим пользователем, должна сначала рассматриваться как расширение `UserMiniApp`, а не как прямой разбор claims в новом месте.

---

## 5. Публичные контракты

Основные публичные контракты user mini-app:

- `BusinessEntityUser`
- `BusinessEntityClaim`
- типизированные Request/Response
- `IUserConnector`
- `GetUserRequest`
- `GetUserResponse`
- `UserDto`
- `UserPropertyDto`
- `UserPropertyTypeEnum`

### 5.1. BusinessEntityUser

`BusinessEntityUser` представляет текущего пользователя приложения, собранного из Authentik principal.

Он используется для runtime-сценариев:

- показать имя пользователя
- показать email
- проверить authenticated state
- получить группы
- показать claims на diagnostic page
- вычислить локальные boolean-флаги доступа

`BusinessEntityUser` не является persisted entity.

### 5.2. UserDto

`UserDto` представляет локальную техническую учетную запись приложения.

Он используется для привязки app-specific данных к пользователю.

`UserDto` содержит:

- `Id`
- `ExternalId`
- `Payload`
- `DateCreated`
- `DateLastModified`

`ExternalId` должен соответствовать стабильному идентификатору пользователя из `Authentik`. Предпочтительный источник - claim name identifier. Если он отсутствует, допускается fallback на `BusinessEntityUser.UserId`.

`UserDto` не должен использоваться как источник authentication или authorization.

### 5.3. UserPropertyDto

`UserPropertyDto` представляет техническое пользовательское свойство.

Он используется для данных, которые:

- принадлежат конкретному локальному пользователю
- нужны только приложению `BusinessEntity`
- не являются identity/profile данными `Authentik`
- не должны попадать в граф бизнес-сущностей

Примеры:

- закладки rich-document
- пользовательские настройки интерфейса
- пользовательские пресеты печати
- персональные фильтры
- сохраненные состояния рабочих панелей

---

## 6. Локальное хранение пользователей

### 6.1. Назначение локальной таблицы Users

Таблица `Users` нужна для локальной материализации Authentik-пользователя.

Она решает задачу стабильной привязки app-specific данных к пользователю приложения.

Допустимые сценарии:

- создать локальную запись после успешного login
- найти локального пользователя по `ExternalId`
- обновить технический payload, если появились недостающие данные
- связать `UserProperties` с локальным `UserDto.Id`

Недопустимые сценарии:

- использовать `Users` как самостоятельный user directory
- хранить пароль или password hash
- хранить группы как источник прав
- редактировать права пользователя через `Users`
- строить административный CRUD пользователей поверх `Users` без отдельной политики

### 6.2. Payload UserDto

`UserDto.Payload` хранит сериализованный JSON с техническими данными локальной записи.

Текущий payload представлен `UserData`.

Он содержит:

- `AuthentikLogin`
- `DisplayedName`
- `ExtId`

Правила:

- payload должен быть JSON
- payload должен сериализоваться едиными options user mini-app
- payload не должен содержать secrets
- payload не должен дублировать полный набор claims
- расширение payload требует обратной совместимости чтения

---

## 7. Пользовательские properties

### 7.1. Назначение

`UserProperties` - это хранилище app-specific данных пользователя.

Каждая property привязана к локальному пользователю через `ParentEntityId`.

Тип property задается `UserPropertyTypeEnum`.

### 7.2. Когда использовать UserProperties

`UserProperties` нужно использовать, если данные:

- персональные для пользователя
- не являются частью документа, папки или пространства
- не должны быть общими для всех пользователей
- должны переживать restart приложения
- логически не принадлежат `BusinessEntityData`

### 7.3. Когда не использовать UserProperties

`UserProperties` нельзя использовать для:

- auth tokens
- passwords
- authorization groups
- глобальных системных параметров
- shared document payload
- данных, которые должны участвовать в общем бизнес-графе

### 7.4. Формат property payload

`UserPropertyDto.Data` должен хранить основной JSON payload.

`UserPropertyDto.Metadata` может хранить легковесную техническую метаинформацию:

- schema version
- kind
- counters
- short diagnostics

Payload property должен иметь:

- `schemaVersion`
- `kind`
- typed DTO/model на стороне кода
- tolerant read при поврежденном или устаревшем JSON

Если payload не читается, сервис должен безопасно вернуться к пустому состоянию или явно обработать ошибку без падения пользовательского сценария.

### 7.5. Уникальность property

Для property, которая должна существовать в одном экземпляре на пользователя и тип, сервис обязан обеспечивать upsert-семантику.

Если обнаружены дубликаты, допустимо:

- выбрать самый свежий по `DateLastModified`
- сохранить его как основной
- удалить или игнорировать остальные в зависимости от риска потери данных

Это правило уже применяется к rich-document bookmarks.

---

## 8. Rich-document bookmarks

Закладки rich-document являются первым текущим сценарием `UserProperties`.

Они хранятся как property:

```text
ParentEntityId = UserDto.Id
PropertyType = RichDocBookmarks
Data = RichTextDocumentBookmarksPayload JSON
```

Правила:

- закладка всегда принадлежит конкретному пользователю
- закладка всегда относится к конкретному `documentId`
- закладка создается только по валидному text selection
- пустой selected text не должен создавать закладку
- selected text должен нормализоваться и ограничиваться по длине
- label должен быть короткой производной от selected text
- чтение закладок должно фильтровать данные по `documentId`
- удаление закладки должно работать только в контексте текущего пользователя

Закладки не являются частью payload документа, потому что они персональные.

### 8.1. Пресеты печати документов

Пресеты печати документов хранятся как одна пользовательская property:

```text
ParentEntityId = UserDto.Id
PropertyType = DocPrintPresets
Data = DocPrintSettingsPresetCollection JSON
```

Правила:

- пресеты принадлежат конкретному пользователю;
- payload содержит коллекцию именованных пресетов;
- сохранение пресета с существующим именем перезаписывает старые значения;
- удаление пресета удаляет запись из коллекции, а не отдельную property.

---

## 9. Доступ к текущему пользователю из кода

### 9.1. Предпочтительный путь

Если UI, сервису или другой mini-app нужен текущий пользователь, нужно использовать:

```text
IUserConnector
```

Примеры допустимых операций:

- `GetCurrentUserAsync`
- `EnsureCurrentUserAsync`
- `GetGroupsAsync`
- `IsInGroupAsync`

Если нужен bus-level доступ, используется контракт `GetUserRequest` / `GetUserResponse`.

### 9.2. Нежелательный путь

Нельзя в новых местах напрямую разбирать:

- `HttpContext.User`
- raw claims
- auth cookie properties
- tokens
- `AuthentikSessionManager` internals

Исключения допустимы только в auth-инфраструктуре:

- `AuthentikSessionManager`
- `AuthController`
- диагностические страницы, если они явно показывают raw auth information

Даже в диагностике предпочтительно рядом показывать нормализованный `BusinessEntityUser`.

---

## 10. Авторизация и административные признаки

Текущие локальные признаки:

- `IsAkadmin`
- `IsGeneralAdmin`

Правила:

- `IsAkadmin` является специальным техническим признаком для username `akadmin`
- `IsGeneralAdmin` определяется через membership в группе `BusinessEntityAdmins`
- источник группы `BusinessEntityAdmins` - `Authentik`
- локальная таблица `Users` не должна переопределять эти признаки

Новая модель ролей приложения для доступа к контенту описана отдельно:

- `Context/Policy/Users/content-access-rights-policy.md`
- `Context/Policy/Users/admin-users-policy.md`

Эта модель не меняет источник identity: `Authentik` остается владельцем логина, пароля и внешнего пользователя, а `UserMiniApp` становится владельцем application authorization для контента.

---

## 11. Admin UI пользователей

Admin UI пользователей является тонкой интеграцией приложения с `Authentik`.

Решение по текущей версии:

- список пользователей читается из `Authentik`;
- пользователями приложения считаются Authentik-пользователи из configured application users group (`AuthentikAuth:ApplicationUsersGroupName`, сейчас `GeoUsers`);
- локальная таблица `Users` остается материализацией Authentik-пользователя для app-specific данных;
- вся внутренняя логика находится внутри `UserMiniApp`;
- UI работает через `IUserConnector`, а не напрямую через Authentik API, DbContext или repositories.

### 11.1. Authentik CRUD пользователей приложения

В приложении допускается ограниченный CRUD Authentik-пользователей, относящихся к приложению.

Назначение такого CRUD:

- просмотреть пользователей приложения из `Authentik`;
- создать нового Authentik-пользователя приложения;
- изменить Authentik username;
- удалить Authentik-пользователя приложения;
- изменить локальное отображаемое имя пользователя.

Правила:

- поле UI "Код в аутентик" соответствует Authentik `uid`, хранится в `UserDto.ExternalId` и `UserData.ExtId`, в UI не редактируется;
- поле UI "Логин в аутентик" соответствует Authentik `username`;
- поле UI "Отображаемое имя" соответствует `UserData.DisplayedName` и хранится только в `UserMiniApp`;
- при создании Authentik username генерируется как `user-[5 буквенный код]`;
- созданный пользователь должен добавляться в configured application users group, чтобы после перечитывания попасть в список пользователей приложения;
- после создания список перечитывается из `Authentik`, а UI выбирает созданного пользователя;
- если при редактировании изменен Authentik username, сначала выполняется изменение в `Authentik`;
- если изменение username в `Authentik` завершилось ошибкой, локальные `UserDto` / `UserData` не сохраняются;
- локальное отображаемое имя можно менять независимо от Authentik username;
- при материализации пользователя `UserData.DisplayedName` по умолчанию равен Authentik username, но может быть изменен в приложении;
- вкладка "Права доступа" может быть показана как пустой placeholder до появления отдельной политики прав.

Запрещено в рамках этого CRUD:

- менять пароли;
- назначать роли приложения;
- использовать локальную запись `UserDto` как источник authorization;
- управлять пользователями вне configured application users group.

---

## 12. Storage и schema

`UserMiniApp` использует собственный `UserMiniAppDbContext`.

Физически таблицы могут находиться в той же Postgres-базе, что и остальные mini-app, но user mini-app не должен зависеть от внутренних DTO data-provider mini-app.

Текущие таблицы:

- `Users`
- `UserProperties`

Требования:

- schema должна создаваться при старте приложения через явную startup-процедуру
- `Users.ExternalId` должен иметь unique index
- `UserProperties.ParentEntityId` должен индексироваться
- `(ParentEntityId, PropertyType)` должен индексироваться
- новые user tables и indexes должны быть описаны в этой политике

---

## 13. Обработка login flow

После успешного callback из `Authentik` приложение должно:

1. Завершить token exchange.
2. Создать cookie-сессию.
3. Установить текущий `ClaimsPrincipal` в request context.
4. Вызвать `EnsureCurrentUserAsync`.
5. Перенаправить пользователя на return URL.

Это нужно, чтобы локальный `UserDto` был доступен сразу после login, а app-specific сценарии не создавали пользователя лениво в неожиданных местах.

Если пользователь уже authenticated и снова открывает login endpoint, допустимо также вызвать `EnsureCurrentUserAsync` перед redirect.

### 13.1. Локальная форма логина

На login gate допускается локальная форма логин/пароль, чтобы пользователь входил без видимого перехода на страницу `Authentik`.

Правила:

- форма передает credentials только в `AuthController`;
- приложение не сохраняет пароль и не пишет его в логи;
- `AuthController` использует credentials только для server-side authentication через Authentik flow API;
- после успешной проверки Authentik приложение читает текущего Authentik-пользователя и создает локальную cookie-сессию с нормализованными claims;
- после создания cookie-сессии обязательно вызывается `EnsureCurrentUserAsync`;
- при ошибке Authentik локальная сессия не создается, пользователь возвращается на login gate с нейтральным сообщением об ошибке;
- password-flow сессия не хранит `access_token`, `refresh_token`, `id_token` и живет до срока действия локальной cookie;
- `/auth/login` сохраняется как fallback на browser redirect-flow Authentik.

---

## 14. Debug re-seed и пользовательские данные

Debug re-seed должен очищать не только business storage, но и локальную запись текущего пользователя в user mini-app storage.

При re-seed приложение должно:

1. Найти текущего authenticated пользователя через `UserMiniApp`.
2. Найти локальный `UserDto` по `ExternalId`.
3. Удалить все `UserProperties`, привязанные к этому `UserDto`.
4. Удалить сам `UserDto`.
5. Очистить business storage.
6. Запустить sample data seed заново.

Re-seed не должен удалять пользователя из `Authentik`, менять пароль, группы или роли.

Назначение этого поведения - сбросить app-specific пользовательское состояние вместе с демо-данными, например rich-document bookmarks и другие персональные properties, которые могли ссылаться на пересоздаваемые документы.

---

## 15. Безопасность и приватность

В user storage нельзя хранить:

- passwords
- password hashes
- refresh tokens
- access tokens
- id tokens
- client secrets
- полные raw token payload без необходимости
- данные, которые должны удаляться или регулироваться только в identity provider

В user properties нельзя без отдельного решения хранить чувствительные персональные данные.

Логи не должны содержать:

- token values
- полные claims, если они могут содержать sensitive data
- payload пользовательских properties целиком

Допустимо логировать:

- локальный `UserDto.Id`
- `ExternalId`, если он не является secret
- факт создания или обновления пользователя
- count пользовательских properties

---

## 16. Правила расширения

При добавлении новой пользовательской функции нужно определить, к какой категории она относится.

### 16.1. Identity/profile

Если функция касается логина, пароля, email как primary identity, групп или ролей, она относится к `Authentik`.

В приложении можно добавить только интеграцию или ссылку, если не принято отдельное решение о локальном user-management.

### 16.2. Runtime current user

Если функция только читает текущего пользователя, нужно расширять или использовать:

- `BusinessEntityUser`
- `IUserConnector`
- `UserIdentityService` (текущая identity-сессия владельца)

### 16.3. App-specific persistent user data

Если функция хранит персональные данные приложения, нужно использовать:

- `UserDto`
- `UserPropertyDto`
- новый `UserPropertyTypeEnum`
- typed payload model
- upsert/read/delete методы в `UserPreferencesService`
- публичные методы в типизированных Request/Response и `IUserConnector`, если функция нужна снаружи mini-app

### 16.4. Shared business data

Если данные должны быть видны всем пользователям или участвовать в графе бизнес-сущностей, они не относятся к user mini-app.

Такие данные должны храниться через business storage / data-provider контур.

---

## 17. Запрещенные архитектурные решения

Запрещено:

- напрямую инжектить user mini-app internal services вне `UserMiniApp`
- обращаться из UI к `UserMiniAppDbContext`
- обращаться из UI к user repositories
- создавать отдельные разрозненные user services вне mini-app без явной причины
- дублировать разбор claims в компонентах
- хранить user-specific данные в document payload, если они персональные
- хранить shared document данные в user properties
- использовать локальный `UserDto` сам по себе для проверки прав без расчета effective permissions через `UserMiniApp`
- добавлять локальный user CRUD без обновления этой политики

---

## 18. Где смотреть код

Основные файлы:

- `BusinessEntity/Services/AuthentikSessionManager.cs`
- `BusinessEntity/Controllers/AuthController.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Components/UsersAdministration.razor`
- `BusinessEntity/MiniApps/UserMiniApp/Contracts/...`
- `BusinessEntity/MiniApps/UserMiniApp/Connectors/UserConnector.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Registration/UserMiniAppRegistration.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/UserIdentityService.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/UserAccountsService.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/UserAccessService.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/UserPreferencesService.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/UserFormsWorkingSet.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/BusinessEntityUserFactory.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Storage/UserMiniAppDbContext.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Storage/UserMiniAppStorageSchema.cs`
- `BusinessEntity/MiniApps/UserMiniApp/Repositories/EfPostgres/...`
- `BusinessEntity/Program.cs`

Связанные документы:

- `Context/index.md`
- `Context/MiniApps/user-miniapp.md`
- `Context/Policy/miniapp-reactivebus-architecture-guide.md`
- `Context/Policy/graph-storage-policy.md`

---

## 19. Короткая итоговая карта

```text
Authentik
  = identity, login, logout, password, groups, claims

Cookie auth
  = локальная web-сессия приложения

BusinessEntityUser
  = runtime-модель текущего пользователя из claims

UserDto
  = локальная техническая материализация Authentik-пользователя

UserProperties
  = persistent app-specific данные пользователя

UserMiniApp
  = единая граница user-домена внутри BusinessEntity
```
