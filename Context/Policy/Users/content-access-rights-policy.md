# Content Access Rights Policy

Статус: целевая политика. В TASK_ARCH_001 реализованы расчёт прав, page/command guards и draft/published UI. Полная проверка доступа к файлам по прямому GET не заявляется; обнаруженная развилка описана в итоговом аудите.

Этот документ фиксирует гипотезу и целевое правило применения ролей `UserMiniApp` к документам, папкам, rich-text данным и связанным файловым объектам.

Политика описывает целевую модель. Если код еще не реализует отдельные пункты, новая реализация должна двигаться к этой модели, а не вводить параллельную схему прав.

---

## 1. Граница ответственности

`Authentik` остается источником identity:

- логин;
- пароль;
- внешний идентификатор пользователя;
- базовая аутентификация;
- внешние группы, если они нужны для входа и общей административной роли.

`UserMiniApp` является владельцем application authorization внутри `BusinessEntity`:

- локальные пользователи приложения;
- локальные группы пользователей приложения;
- роли приложения;
- права роли;
- назначения ролей в разрезе пространств;
- расчет effective permissions текущего пользователя.

Контентные права не должны размазываться по страницам, rich-document сервисам, дереву или storage-репозиториям. Эти слои должны спрашивать права через публичный контракт `UserMiniApp`.

---

## 2. Базовая матрица прав

Минимальная матрица прав приложения:

| Право | Смысл |
|---|---|
| `ViewPublished` | Пользователь может видеть опубликованную версию контента. |
| `ViewDraft` | Пользователь может видеть draft/current рабочую версию контента. |
| `EditDraft` | Пользователь может изменять draft/current рабочую версию. |
| `PublishDraft` | Пользователь может публиковать draft/current версию. |
| `AdminItems` | Пользователь может администрировать элементы пространства: создавать, переименовывать, перемещать и удалять папки/документы/items. |
| `AdminSpace` | Пользователь может администрировать само пространство: настройки пространства и назначения прав в этом пространстве. |
| `GlobalAdmin` | Пользователь является глобальным администратором приложения и может выполнять системные административные действия вне границ одного пространства. |

Хранение прав внутри роли может быть техническим:

- boolean-поля;
- строка кодов, например `100;300;400;`;
- bit mask;
- отдельная таблица.

Но публичный контракт должен работать с именованными правами, а не заставлять остальной код знать коды.

Нормативные имена прав:

```text
ViewPublished
ViewDraft
EditDraft
PublishDraft
AdminItems
AdminSpace
GlobalAdmin
```

Если используются коды, они должны быть локальной деталью `UserMiniApp`.

### 2.1. Базовая матрица ролей

Базовые роли `UserMiniApp` должны соответствовать следующей матрице.

В таблице `x` означает `true`, пустая ячейка означает `false`.

| Имя роли | System | ViewPublished | ViewDraft | EditDraft | PublishDraft | AdminItems | AdminSpace | GlobalAdmin |
|---|---|---|---|---|---|---|---|---|
| `Админ` | Да | x | x | x | x | x | x | x |
| `Гость` | Нет | x |  |  |  |  |  |  |
| `Ридерс` | Нет | x |  |  |  |  |  |  |

Правила:

- `Админ` является системной ролью и содержит все права матрицы.
- `Гость` является ролью только для чтения опубликованного общего контента.
- `Ридерс` является ролью только для чтения опубликованного общего контента.
- `Гость` и `Ридерс` имеют одинаковый набор прав, но разные смысловые назначения: `Гость` предназначен для гостевого/анонимного доступа, `Ридерс` - для обычных авторизованных читателей.
- роли с правами draft/edit/publish/admin должны добавляться явно и не должны появляться как скрытое расширение `Гость` или `Ридерс`.

---

## 3. Область действия прав

Права применяются на уровне `Space`.

`Space` - это `BusinessEntity` с `EntityType == Space`. Его `Id` является `spaceId` для расчета прав.

Все дочерние элементы пространства наследуют права пространства:

```text
Space
  -> Folder
     -> Document
        -> RichText chunks
        -> Embedded files
        -> File objects
```

Для MVP не вводятся отдельные ACL на папки, документы, чанки или файлы.

Если позже понадобится per-document или per-folder override, это должно быть отдельным расширением политики. До этого момента нельзя добавлять локальные исключения в документный payload.

### 3.1. Признак `Общее`

`Общее` - это порог доступности контентной бизнес-сущности.

На текущем этапе `Общее` применяется только к:

- `Document`;
- `RichTextDocument`.

`Общее` не применяется к:

- `Space`;
- `Folder`.

Для `Space` и `Folder` нельзя закладывать логику, связанную с признаком `Общее`. Пространства являются областью расчета прав, а папки являются навигационными контейнерами.

Для всех новых `Document` и `RichTextDocument`, создаваемых через `BusinessEntityFactory`, значение `Общее` по умолчанию должно быть:

```text
Общее = true
```

Если `Общее = false`, объект является приватным контентом пользователя.

Приватный контент видят только:

- создатель объекта;
- администраторы пространства;
- глобальные/system администраторы, если для них явно включен emergency bypass.

Для приватного контента создатель и администратор пространства имеют права на все операции с объектом:

- чтение;
- редактирование;
- публикация;
- удаление;
- административные действия.

Ролевые права обычных пользователей на приватный объект не распространяются. Даже если пользователь или группа имеют `ViewDraft`, `ViewPublished`, `EditDraft` или `PublishDraft` в пространстве, они не получают доступ к объекту с `Общее = false`, если не являются создателем или администратором пространства.

Если `Общее = true`, объект считается потенциально доступным другим пользователям. После этого порога доступ определяется матрицей прав пространства и фактом опубликованности.

---

## 4. Назначения ролей

Назначение роли хранится в `UserMiniApp`.

Минимальная запись назначения:

```text
Id
SpaceId
Subject
SubjectId
AssignmentType
RoleId
```

Где:

- `SpaceId` - GUID пространства или `00000000-0000-0000-0000-000000000000` для `AllSpaces`;
- `Subject` - область действия назначения;
- `SubjectId` - GUID группы или пользователя;
- `AssignmentType` - маркер назначения;
- `RoleId` - GUID роли.

Текущие маркеры `Subject`:

| Marker | Смысл |
|---|---|
| `Space` | Назначение действует только в выбранном пространстве. |
| `AllSpaces` | Назначение действует во всех пространствах. В UI отображается как `[ВсеПространства]`. |

Текущие маркеры `AssignmentType`:

| Marker | Смысл |
|---|---|
| `group-to-role` | Роль назначена группе пользователей в пространстве. |
| `user-to-role` | Роль назначена конкретному пользователю в пространстве. Пока резерв на будущее. |

На текущем этапе активным считается `group-to-role`.

---

## 5. Расчет effective permissions

Для текущего пользователя и пространства система должна уметь получить один объект effective permissions:

```text
UserId
SpaceId
CanViewPublished
CanViewDraft
CanEditDraft
CanPublishDraft
CanAdminItems
CanAdminSpace
CanGlobalAdmin
```

Алгоритм:

1. Получить текущего локального пользователя через `UserMiniApp`.
2. Получить локальные группы пользователя из `UserMiniApp`.
3. Найти все `group-to-role` назначения для этих групп в выбранном `Space`.
4. Найти все `group-to-role` назначения для этих групп с `Subject = AllSpaces`.
5. Когда появится `user-to-role`, добавить прямые назначения пользователя.
6. Объединить права через OR.

Пример:

```text
Group Readers -> Role Reader    -> ViewPublished
Group Editors -> Role Editor    -> ViewPublished, ViewDraft, EditDraft

User in Readers + Editors
  => ViewPublished = true
  => ViewDraft = true
  => EditDraft = true
  => PublishDraft = false
  => AdminItems = false
  => AdminSpace = false
  => GlobalAdmin = false
```

Если назначений нет, действует default deny:

```text
ViewPublished = false
ViewDraft = false
EditDraft = false
PublishDraft = false
AdminItems = false
AdminSpace = false
GlobalAdmin = false
```

Исключение для bootstrap/general admin должно быть отдельным явным решением, а не неявным побочным эффектом Authentik-группы.

---

## 6. Системная роль Admin

В `UserMiniApp` должна существовать системная роль `Админ`.

Правила:

- роль создается/проверяется при старте `UserMiniApp`;
- роль не удаляется пользователем;
- роль содержит все права базовой матрицы ролей;
- сама по себе роль не дает доступ ко всем пространствам;
- доступ появляется через назначение роли группе или пользователю в конкретном пространстве.

Такое правило не смешивает глобальное администрирование системы и контентные права пространства.

Если нужен emergency bypass для `IsGeneralAdmin`, он должен быть реализован явно и задокументирован отдельным пунктом этой политики.

---

## 6.1. Анонимный доступ

Анонимный доступ должен быть частью той же матрицы прав, а не отдельным обходным механизмом.

Целевая модель:

- в `UserMiniApp` существует системный локальный пользователь `anonymous`;
- пользователь `anonymous` создается/проверяется при старте системы;
- пользователь `anonymous` не существует как обычная учетная запись `Authentik`;
- у пользователя `anonymous` нет пароля, login flow и интерактивного профиля;
- пользователь `anonymous` не редактируется и не удаляется через обычный CRUD пользователей;
- пользователь `anonymous` используется только для расчета прав неаутентифицированного посетителя.

Нормативный идентификатор/код:

```text
system-anonymous
```

Отображаемое имя:

```text
Анонимус
```

Точное имя в UI может быть локализовано, но внутренний код должен быть стабильным.

### 6.1.1. Назначение прав анонимусу

Для анонимного доступа предпочтительно использовать прямое назначение:

```text
SpaceId
Subject = Space
SubjectId = anonymous user id
AssignmentType = user-to-role
RoleId
```

Типовая роль для такого назначения:

```text
Гость
```

Роль `Гость` должна содержать только `ViewPublished`.

Если UI назначения ролей пока поддерживает только `group-to-role`, допустим переходный вариант с системной группой:

```text
Group = Анонимные
Subject = Space
AssignmentType = group-to-role
```

Но целевая модель проще: `anonymous` является системным user-subject и получает роль через `user-to-role`.

### 6.1.2. Ограничения анонимных прав

Анонимному пользователю можно давать только права чтения published-контента.

Разрешено:

- `ViewPublished`

Запрещено:

- `ViewDraft`
- `EditDraft`
- `PublishDraft`
- `AdminItems`
- `AdminSpace`
- `GlobalAdmin`

Если администратор технически назначил анонимусу роль с draft/edit/publish правами, runtime должен нормализовать effective permissions:

```text
Anonymous CanViewPublished = role.ViewPublished
Anonymous CanViewDraft = false
Anonymous CanEditDraft = false
Anonymous CanPublishDraft = false
Anonymous CanAdminItems = false
Anonymous CanAdminSpace = false
Anonymous CanGlobalAdmin = false
```

Это правило защищает систему от случайного открытия draft-данных наружу.

### 6.1.3. Поведение login gate

При неаутентифицированном запросе система должна сначала определить, может ли запрос быть обслужен в anonymous mode.

Правило:

```text
if anonymous has ViewPublished for target/current Space
    allow read-only published UI
else
    show login gate
```

Если пространство не выбрано и маршрут не дает однозначного `spaceId`, система может показывать login gate.

Анонимный режим не должен открывать:

- страницу профиля;
- администрирование;
- диагностику;
- auth-info;
- любые draft/edit/publish endpoints;
- любые операции изменения дерева или документа.

### 6.1.4. UI в anonymous mode

В anonymous mode UI должен быть только read-only.

Правила:

- показывать только published-состояние документов;
- не показывать draft markers;
- не показывать toolbar редактирования;
- не показывать кнопки save/import/publish;
- не показывать профиль пользователя;
- показывать кнопку/ссылку входа;
- не создавать локальную cookie-сессию;
- не создавать обычного пользователя в `Authentik`.

Если пользователь нажимает login из anonymous mode, после успешного входа система пересчитывает права уже для authenticated локального пользователя.

### 6.1.5. Effective permissions для anonymous

Целевой контракт должен уметь считать права и для authenticated, и для anonymous сценария.

Возможная форма:

```csharp
Task<UserEffectivePermissions> GetAnonymousPermissionsForSpaceAsync(
    Guid spaceId,
    CancellationToken cancellationToken = default);

Task<UserEffectivePermissions> GetEffectivePermissionsForSpaceAsync(
    Guid? userId,
    Guid spaceId,
    CancellationToken cancellationToken = default);
```

Где `userId == null` или `Guid.Empty` означает anonymous mode только если вызывающий код явно работает с public/anonymous access.

Нельзя случайно подставлять anonymous вместо сломанной authenticated-сессии. Если пользователь должен быть authenticated, отсутствие пользователя является ошибкой авторизации.

### 6.1.6. Сообщения и аудит

Анонимный доступ не должен писать пользовательские сообщения в стек обычного пользователя.

Допустимые варианты:

- не показывать `UserMessagesMiniApp` в anonymous mode;
- показывать только transient UI-сообщения без сохранения в стек;
- вести server-side аудит anonymous reads отдельно, если это понадобится.

Ошибки доступа anonymous mode должны быть нейтральными:

```text
Для просмотра требуется вход.
```

Не нужно раскрывать, существует ли draft или какие права отсутствуют.

---

## 7. Применение к дереву

Дерево текущего пространства должно учитывать effective permissions пользователя.

Правила:

- если нет `ViewPublished` и нет `ViewDraft`, пользователь не должен видеть контент пространства;
- приватный контент с `Общее = false` является исключением из общей матрицы: его видят создатель и администраторы пространства;
- если есть только `ViewPublished`, пользователь видит только общий опубликованный контент;
- для неверсионных общих объектов опубликованность считается истинной всегда;
- для версионных общих объектов требуется опубликованная версия;
- если есть `ViewDraft`, пользователь может видеть draft/current состояние;
- создание, переименование, удаление и перемещение узлов считаются администрированием элементов и требуют `AdminItems`.
- изменение содержимого draft/current версии требует `EditDraft`.
- папки показываются только если внутри них или внутри их подпапок есть видимый пользователю контент, если не включен отдельный флаг "показывать все папки".

UI может скрывать недоступные команды, но это не считается защитой. Команды дерева должны проверяться на server/helper/service уровне.

---

## 8. Применение к документу

Для `Document` и `RichTextDocument` проверка доступа начинается с признака `Общее`.

Первый порог:

```text
if Общее = false
    allow only creator + space admins
else
    apply published/draft permission matrix
```

Если документ приватный, то матрица ролей пространства не открывает его другим пользователям. Создатель и администратор пространства могут выполнять все операции с таким документом.

Если документ общий, то применяется матрица:

| Сценарий | Требуемое право |
|---|---|
| Открыть опубликованную версию | `ViewPublished` |
| Открыть draft/current версию | `ViewDraft` |
| Смотреть список draft-версий | `ViewDraft` |
| Изменить текст, чанки, свойства draft | `EditDraft` |
| Импортировать файл в документ | `EditDraft` |
| Изменить описание сохраняемой версии | `EditDraft` |
| Сохранить draft | `EditDraft` |
| Опубликовать draft | `PublishDraft` |
| Создать, переименовать, переместить или удалить item | `AdminItems` |
| Изменить настройки пространства | `AdminSpace` |
| Назначить роли в пространстве | `AdminSpace` |
| Выполнять глобальные административные действия вне конкретного пространства | `GlobalAdmin` |

Если пользователь имеет `EditDraft`, но не имеет `ViewDraft`, это считается некорректной ролью. UI может позволить такую конфигурацию технически, но runtime должен трактовать `EditDraft` как требующее `ViewDraft`.

То же правило для `PublishDraft`: публикация требует возможности видеть draft.

Нормализация effective permissions может быть такой:

```text
if EditDraft then ViewDraft = true
if PublishDraft then ViewDraft = true
```

Либо система может валидировать роль и запрещать неконсистентные комбинации. Предпочтительно валидировать роль.

---

## 9. Published vs Draft

Опубликованность применяется только к BusinessEntity, у которых есть версионность.

На текущем этапе:

| Тип | Версионность | Правило опубликованности |
|---|---|---|
| `Document` | Нет | Если `Общее = true`, считается опубликованным всегда. |
| `RichTextDocument` | Да | Если `Общее = true`, видимость по `ViewPublished` требует опубликованной версии. |

`ViewPublished` и `ViewDraft` должны быть разными режимами чтения для версионных сущностей.

Если пользователь имеет только `ViewPublished`:

- он не должен получать draft payload;
- он не должен видеть draft-only изменения;
- он не должен видеть кнопки сохранения, публикации, импорта и редактирования;
- если у версионного документа опубликованной версии нет, документ считается недоступным.

Если пользователь имеет `ViewDraft`:

- он может видеть текущую рабочую версию;
- опубликованная версия может отображаться как отдельное состояние/маркер;
- доступ к редактированию все равно требует `EditDraft`.

--- 

## 10. Public / общий документ

`Общее` не подменяет матрицу прав, а определяет, можно ли вообще применять матрицу к объекту.

Нормативное правило:

```text
Общее = false
  => private content
  => visible only to creator + space admins
  => role permissions of other users are ignored

Общее = true
  => shared content candidate
  => access depends on effective permissions and published state
```

Для `Document`:

```text
Общее = true
  => published state is always true
  => users with ViewPublished can read it
```

Для `RichTextDocument`:

```text
Общее = true
  => published state depends on selected published version
  => users with ViewPublished can read only if a published version exists
```

Для `Folder` и `Space`:

```text
Do not check Общее.
Do not add private/shared behavior through Общее.
```

Папка видима в дереве только как путь к видимому контенту по правилам дерева. Пространство является границей расчета прав и не является private/shared объектом через `Общее`.

---

## 11. Enforcement points

Проверки прав должны быть на двух уровнях.

UI уровень:

- скрывает или disables кнопки;
- выбирает режим отображения published/draft;
- показывает понятное сообщение в правой колонке пользовательских сообщений.

Server/helper/service уровень:

- запрещает чтение draft без `ViewDraft`;
- запрещает чтение published без `ViewPublished`;
- запрещает save/import/edit mutations без `EditDraft`;
- запрещает publish без `PublishDraft`;
- запрещает create/rename/move/delete items без `AdminItems`;
- запрещает администрирование пространства и назначение ролей без `AdminSpace`;
- запрещает глобальные административные операции без `GlobalAdmin`;
- не полагается на то, что UI спрятал кнопку.

Нормативное правило:

```text
UI hints are not authorization.
Authorization lives in service/helper/connector boundary.
```

---

## 12. Публичные контракты UserMiniApp

Целевые публичные методы должны жить в типизированных Request/Response и `IUserConnector`.

Примерная форма:

```csharp
Task<UserEffectivePermissions> GetCurrentUserPermissionsForSpaceAsync(
    Guid spaceId,
    CancellationToken cancellationToken = default);

Task<bool> CurrentUserHasPermissionAsync(
    Guid spaceId,
    UserContentPermission permission,
    CancellationToken cancellationToken = default);

Task EnsureCurrentUserHasPermissionAsync(
    Guid spaceId,
    UserContentPermission permission,
    CancellationToken cancellationToken = default);
```

Названия могут отличаться, но идея обязательна:

- остальной код не должен сам собирать роли, группы и назначения;
- UI не должен ходить в `UserMiniAppDbContext`;
- rich-document код не должен знать storage-формат назначений ролей;
- дерево не должно читать user role tables напрямую.

---

## 13. Кэширование

Effective permissions можно кэшировать на короткий срок в рамках circuit/request/user session.

Ключ кэша:

```text
UserId + SpaceId
```

Кэш должен сбрасываться при изменении:

- ролей;
- прав роли;
- групп пользователя;
- назначений ролей;
- текущего пользователя;
- текущего пространства.

Пока система небольшая, предпочтительна простая реализация без агрессивного кэша.

---

## 14. Сообщения пользователю

Ошибки доступа должны попадать в пользовательский message stack справа через `UserMessagesMiniApp`.

Примеры:

- `Нет прав на просмотр опубликованной версии.`
- `Нет прав на просмотр draft-версии.`
- `Нет прав на редактирование draft.`
- `Нет прав на публикацию.`
- `Нет прав на администрирование элементов.`
- `Нет прав на администрирование пространства.`
- `Нет прав глобального администратора.`

Inline-ошибки допустимы только там, где они являются частью формы валидации. Authorization/result messages должны идти через правую колонку.

---

## 15. Запрещенные решения

Запрещено:

- проверять контентные права прямым чтением `UserMiniAppDbContext` из UI;
- проверять контентные права прямым чтением user repositories из document/tree/rich-doc компонентов;
- хранить ACL в payload документа;
- хранить ACL в rich-text manifest;
- давать edit/publish/admin только через скрытие кнопок;
- считать Authentik-группы прямыми ролями контента без mapping в `UserMiniApp`;
- добавлять per-document права без отдельного расширения политики;
- использовать `UserDto` сам по себе как признак прав без расчета effective permissions.
- открывать anonymous-доступ к draft/edit/publish правам;
- считать anonymous fallback заменой обязательной authenticated-сессии;
- создавать anonymous-пользователя в `Authentik`;
- хранить anonymous access flags в document payload вместо назначения роли в `UserMiniApp`.

---

## 16. План внедрения

Рекомендуемый порядок:

1. Добавить доменный контракт effective permissions в `UserMiniApp`.
2. Реализовать расчет прав по `group-to-role` назначениям.
3. Добавить системного пользователя `anonymous` и нормализацию его прав до `ViewPublished`.
4. Подключить проверку к выбору текущего пространства и login gate.
5. Ограничить дерево: read/create/rename/delete/move.
6. Ограничить document/rich-document open: published vs draft.
7. Ограничить save/import/edit operations через `EditDraft`.
8. Ограничить publish operation через `PublishDraft`.
9. Ограничить create/rename/move/delete items через `AdminItems`.
10. Ограничить настройки пространства и назначения ролей через `AdminSpace`.
11. Ограничить системные административные операции через `GlobalAdmin`.
12. Перенести все authorization messages в `UserMessagesMiniApp`.
13. Добавить тесты на расчет effective permissions, default deny и anonymous published-only доступ.

---

## 17. Итоговое правило

Контентные права в `BusinessEntity` считаются так:

```text
Authenticated:
  Current user
    -> local UserMiniApp groups
    -> role assignments for current Space + AllSpaces
    -> roles
    -> OR permissions
    -> effective permissions
    -> private/common content gate
    -> published/draft gate
    -> tree/document/rich-doc enforcement

Anonymous:
  system-anonymous user
    -> anonymous role assignment for current/target Space + AllSpaces
    -> ViewPublished only
    -> only common content
    -> only published content
    -> published read-only UI or login gate
```

Единственный владелец этой логики - `UserMiniApp`.

Контентные компоненты не вычисляют права сами, а запрашивают готовый результат через публичный connector.
