# BusinessEntity: общий обзор системы

> TASK_ARCH_001 находится в раскатке по [ТЗ v2.2](Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md).
> Описание in-memory хранения ниже устарело: бизнес-данные уже хранятся в PostgreSQL
> через DataProviderMiniApp. Целевые правила — одна шина, общий Core и транзакционное
> сохранение миниаппа — описаны в [архитектурной политике](Policy/architecture-principles.md).
> Полная актуализация этого обзора выполняется на этапе 9.

## Назначение

`BusinessEntity` — это серверное веб-приложение на `ASP.NET Core + Blazor Server`, в котором пользователь работает с графом бизнес-сущностей.  
Система сейчас ориентирована на следующие базовые сценарии:
- вход через `Authentik`
- выбор текущего пространства (`Space`)
- навигация по дереву сущностей
- открытие и редактирование документов
- просмотр технической диагностики
- просмотр auth-информации и пользовательских claims
- переход в администрирование пользователей через `Authentik`

Этот файл нужен как стартовый контекст для LLM, которая будет писать код в системе. Он описывает:
- что делает приложение
- какие основные сущности и ограничения в нём есть
- как устроены auth, storage, UI и mini-app слой
- где в коде находятся главные точки расширения

## Бизнес-смысл системы

Текущая предметная модель строится вокруг универсальной сущности `BusinessEntity`.

С практической точки зрения система представляет собой:
- пространства (`Space`) как верхний уровень работы
- папки (`Folder`) как структурные контейнеры
- документы (`Document`) как редактируемые узлы контента
- связи между сущностями как отдельный слой данных
- payload документа как отдельный слой данных

Главная идея: приложение работает не с жёсткой иерархией таблиц `Space -> Folder -> Document`, а с графовой моделью хранения.  
Это значит:
- сами объекты хранятся отдельно
- связи между объектами хранятся отдельно
- содержимое объектов тоже хранится отдельно

Сейчас основная бизнес-демонстрация системы — это навигация по дереву пространств и документов с возможностью создавать, переименовывать, перемещать и редактировать узлы.

## Функциональный обзор

### 1. Аутентификация

Пользователь логинится через `Authentik` по `OIDC authorization code flow`.

После логина приложение:
- обменивает `code` на токены
- создаёт локальную cookie-сессию
- хранит `access_token`, `refresh_token`, `id_token` в cookie auth-properties
- обновляет access token через refresh token до истечения срока локальной сессии
- при logout завершает локальную сессию и инициирует logout в `Authentik`

Текущая auth-модель:
- источник identity — `Authentik`
- приложение не ведёт собственную отдельную таблицу пользователей
- claims и группы приходят из `Authentik`
- локально они оборачиваются в `BusinessEntityUser`

### 2. Выбор пространства

После логина приложение должно работать в контексте выбранного `Space`.

Текущая логика такая:
- если в cookies уже есть выбранное пространство, оно восстанавливается в `UserContextService`
- если пространства в cookies нет, middleware отправляет пользователя на `/space-selection`
- автоматического выбора `Документы` или первого пространства больше нет
- если пользователь открыл `/space-selection`, но пространство уже есть в cookies, страница сразу уводит его обратно на `/`

### 3. Дерево сущностей

Слева в layout всегда отображается дерево текущего пространства.

Оно умеет:
- показывать корень пространства
- загружать дочерние сущности через helper-слой
- выбирать один или несколько узлов
- переименовывать узлы inline
- создавать папки и документы
- удалять узлы
- перемещать узлы drag-and-drop
- обновляться по сообщениям из `ReactiveUI IMessageBus`

### 4. Документы

Документ открывается на отдельной странице `/document/{id}`.

Текущие сценарии документа:
- загрузка `BusinessEntity`
- загрузка связанных `BusinessEntityData`
- просмотр текста
- переход в edit mode
- сохранение имени и тела документа
- публикация `EntityUpdatedMessage`, чтобы дерево и другие части UI обновились

### 5. Технические страницы

В приложении есть несколько служебных страниц:
- `/authinfo` — информация об авторизации, claims, группы, флаги пользователя
- `/diagnostics` — диагностика сущностей и связей
- `/logging` — служебная страница для логирования
- `/administration` — точка входа в администрирование, включая вкладку пользователей приложения из `Authentik`

## Архитектурная картина

### Общая схема

```text
Browser
  -> ASP.NET Core / Blazor Server host
     -> cookie auth
     -> Authentik OIDC integration
     -> Space-selection middleware
     -> Razor/Blazor UI
     -> service layer / helpers
     -> repositories
     -> in-memory storage
```

### Основные проекты

- `BusinessEntity`
  Главный ASP.NET Core / Blazor Server хост.

- `BusinessEntity.Core`
  Доменные типы, helper-слой, сидирование демо-данных, правила работы с сущностями и связями.

- `BusinessEntity.DataAccess`
  Репозиторные интерфейсы и реализации (`InMemoryRepository`, `EfAsyncRepository`).

- `BusinessEntity.Service`
  Сервисные и инфраструктурные компоненты, включая web logging.

- `BlazorServerWebLogger`
  Отдельный сервис логирования.

- `Context`
  Документация и архитектурные договорённости.

## Текущее устройство хранилища

### Фактический storage path

Сейчас бизнес-данные хранятся **in-memory**, а не в PostgreSQL.

Используемые репозитории:
- `IAsyncRepository<BusinessEntity.Core.Classes.BusinessEntity>`
- `IAsyncRepository<BusinessEntity.Core.Classes.Relation>`
- `IAsyncRepository<BusinessEntity.Core.Classes.BusinessEntityData>`

В `Program.cs` они зарегистрированы как:
- `InMemoryRepository<BusinessEntity>`
- `InMemoryRepository<Relation>`
- `InMemoryRepository<BusinessEntityData>`

Следствия:
- данные живут в памяти процесса
- при рестарте контейнера теряются
- после старта заново создаются сидером

### Графовая модель хранения

Текущая модель хранения:

```text
BusinessEntity
  = экземпляры объектов

Relation
  = связи между объектами

BusinessEntityData
  = payload/данные объектов
```

Это ключевая архитектурная идея системы.

Сейчас:
- `Space`, `Folder`, `Document` — это не отдельные таблицы runtime-хранилища
- это один тип `BusinessEntity`
- различие задаётся через `EntityType`

Например:
- `Space` = `BusinessEntity` с `EntityType == Space`
- `Folder` = `BusinessEntity` с `EntityType == Folder`
- `Document` = `BusinessEntity` с `EntityType == Document`

Дерево строится не через вложенные коллекции, а через `Relation` типа визуального вложения.

## Сидирование данных

При старте приложения выполняется `SampleDataService.InitializeSampleDataAsync()`.

Он:
- создаёт пространства `Документы` и `Новости`
- создаёт папки и документы
- создаёт связи между ними
- наполняет документы текстом через `DataFillLineProvider`

Сидер работает идемпотентно настолько, насколько это возможно в текущей in-memory модели:
- если уже есть пространства и связи визуального дерева, он повторно не наполняет систему

## Authentik и пользовательская модель

### Что является источником user data

`Authentik` — это внешний identity provider и текущий источник:
- логина
- logout
- групп
- claims пользователя

Приложение получает пользователя через `id_token` и дополнительные токены, а затем строит локальный объект `BusinessEntityUser`.

### UserMiniApp

В системе есть отдельный mini-app для пользователя:
- `BusinessEntity/MiniApps/UserMiniApp/...`

Его задача:
- забирать текущего пользователя из `AuthentikSessionManager`
- нормализовать claims
- извлекать группы
- выдавать единый DTO `BusinessEntityUser`
- позволять другим частям системы получать пользователя через bus/connector, а не разбирать claims вручную

Ключевые публичные контракты:
- `BusinessEntityUser`
- `BusinessEntityClaim`
- `IUserMiniApp`
- `IUserConnector`
- `GetUserRequest`
- `GetUserResponse`

Схема работы:

```text
UI / Service
  -> IUserConnector
  -> IMessageBus
  -> UserMiniApp handler
  -> BusinessEntityUserFactory
  -> AuthentikSessionManager
  -> BusinessEntityUser
```

### Что лежит в BusinessEntityUser

`BusinessEntityUser` сейчас содержит:
- `UserId`
- `UserName`
- `Email`
- `IsAuthenticated`
- `IsAkadmin`
- `IsGeneralAdmin`
- `Groups`
- `Claims`

### Текущая логика admin-признаков

- `IsAkadmin`
  определяется только по username `akadmin`

- `IsGeneralAdmin`
  определяется по membership в группе `BusinessEntityAdmins`

Это важно: общий администратор приложения сейчас определяется не по роли приложения и не по локальной БД, а по группе из `Authentik`.

## UI-структура

### Корневой UI

`App.razor`:
- поднимает `CascadingAuthenticationState`
- настраивает роутинг
- использует `MainLayout` как default layout

`MainLayout.razor`:
- слева `NavMenu`
- сверху строка с текущим пространством и ссылкой на выбор пространства
- слева панель дерева
- по центру текущая страница
- справа `RightSidebar`

### Основные маршруты

- `/`
  главная страница

- `/authinfo`
  auth-диагностика, claims, группы, признаки пользователя

- `/logging`
  служебная страница логирования

- `/diagnostics`
  диагностика сущностей и связей

- `/administration`
  страница администрирования с переходом в `Authentik`

- `/space-selection`
  ручной выбор текущего пространства

- `/document/{id}`
  просмотр и редактирование документа

### Важные визуальные компоненты

- `TreeComponent`
  основная навигация по структуре пространства

- `Document`
  просмотр и редактирование документа

- `NavMenu`
  иконное меню слева

- `RightSidebar`
  правая боковая панель

## Service / Helper слой

### BusinessEntityHelper

Главный helper для работы с предметными данными.

Через него проходят основные операции:
- чтение сущностей
- чтение пространств
- получение дочерних элементов
- создание документов и папок
- rename
- delete
- смена визуального родителя
- получение и сохранение `BusinessEntityData`

Для LLM это один из главных файлов системы. Если задача касается дерева, документов, пространств или связей, в первую очередь нужно смотреть сюда.

### SpaceHelper

Отвечает за операции вокруг текущего `Space`, в частности за получение пространства по id.

### UserContextService

Хранит текущий выбранный `Space` в рамках пользовательского контекста:
- `CurrentSpaceId`
- `CurrentSpaceName`
- восстановление из cookies
- запись cookies
- очистка текущего пространства

### AuthentikSessionManager

Единая точка auth-логики приложения.

Он отвечает за:
- построение login URL
- завершение callback
- создание локального principal
- хранение токенов в cookie auth-properties
- refresh token flow
- logout

Если задача касается login/logout/claims/tokens/Auth cookie — это главный файл.

## Message bus

В приложении используется `ReactiveUI.IMessageBus`.

Он используется как минимум для двух сценариев:
- обмен сообщениями в `UserMiniApp`
- уведомления об изменении сущностей, например `EntityUpdatedMessage`

Это уже действующий архитектурный паттерн системы: не всё должно ходить напрямую через тяжёлые сервисы; для межмодульного обмена допустим bus.

## Что важно помнить при разработке

### 1. Storage сейчас не persistent

Несмотря на наличие `Npgsql` и `DbContext`, бизнес-операции сейчас работают через `InMemoryRepository`.

PostgreSQL в текущем состоянии:
- подключён инфраструктурно
- `EnsureCreated()` вызывается
- но основное business storage path не переведено на EF

### 2. Space — это BusinessEntity

`Space` не является отдельной сущностью runtime-слоя хранения.  
Это обычный `BusinessEntity` с `EntityType == Space`.

### 3. GUI не должен ходить в repository напрямую

Желательный путь получения данных из UI:

```text
GUI
  -> helper / service / connector
  -> repository
```

Прямые обращения к `IAsyncRepository<...>` из Razor/UI считаются нежелательными и по возможности должны выноситься в helper-слой.

### 4. Пользовательские данные лучше брать через UserMiniApp

Если нужен текущий пользователь:
- предпочтительно использовать `IUserConnector`
- не разбирать raw claims заново в каждом месте

### 5. Authentik остаётся источником user management

Сейчас CRUD пользователей живёт не в приложении, а в `Authentik`.

Приложение:
- логинит пользователя
- получает claims и groups
- вычисляет локальные флаги пользователя

## Где смотреть код в первую очередь

Если задача про auth:
- `BusinessEntity/Services/AuthentikSessionManager.cs`
- `BusinessEntity/Controllers/AuthController.cs`
- `BusinessEntity/Pages/AuthInfo.razor(.cs)`

Если задача про пользователя и группы:
- `BusinessEntity/MiniApps/UserMiniApp/...`
- `BusinessEntity/MiniApps/UserMiniApp/Internal/BusinessEntityUserFactory.cs`

Если задача про space selection:
- `BusinessEntity/Middleware/SpaceSelectionMiddleware.cs`
- `BusinessEntity/Services/UserContextService.cs`
- `BusinessEntity/Pages/SpaceSelection.razor`

Если задача про дерево и структуру:
- `BusinessEntity/Components/TreeComponent.razor.cs`
- `BusinessEntity.Core/Services/BusinessEntityHelper.cs`

Если задача про документы:
- `BusinessEntity/Pages/DocumentPage.razor.cs`
- `BusinessEntity/Components/Document.razor.cs`
- `BusinessEntity.Core/Services/BusinessEntityHelper.cs`

Если задача про storage:
- `BusinessEntity.DataAccess/Repositories/InMemoryRepository.cs`
- `BusinessEntity.DataAccess/Repositories/EfAsyncRepository.cs`
- `BusinessEntity.Core/Classes/BusinessEntity.cs`
- `BusinessEntity.Core/Classes/Relation.cs`
- `BusinessEntity.Core/Classes/BusinessEntityData.cs`

Если задача про startup / DI:
- `BusinessEntity/Program.cs`

## Ключевые ограничения текущего состояния

- storage бизнес-данных пока `in-memory`
- реальные пользовательские данные и права живут в `Authentik`
- часть инфраструктуры уже готовит почву под более постоянное хранилище, но migration ещё не завершён
- дерево и документы — это текущий основной рабочий сценарий системы
- mini-app архитектура уже начата, но пока не покрывает всё приложение

## Короткая итоговая карта

```text
BusinessEntity
  = Blazor Server приложение

Business model
  = граф бизнес-сущностей

Storage
  = BusinessEntity + Relation + BusinessEntityData
  = сейчас in-memory

Auth
  = Authentik OIDC + local cookie session

User model
  = BusinessEntityUser через UserMiniApp

Current work context
  = Space -> Tree -> Document
```

## Связанные документы

Если нужен более узкий контекст, дополнительно смотреть:
- `Context/Policy/graph-storage-policy.md`
- `Context/Policy/Deployment/deployment-policy.md`
- `Context/Policy/miniapp-reactivebus-architecture-guide.md`
- `Context/Policy/Comments/comments-policy.md`
- `Context/MiniApps/user-miniapp.md`
