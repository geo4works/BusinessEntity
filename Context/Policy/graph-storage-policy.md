# Политика хранения данных

> TASK_ARCH_001: целевая норма — [ТЗ v2.2](../Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md),
> [Core](core-engine-policy.md) и [сохранение](MiniApps/miniapp-saving-policy.md).
> Ниже описан исходный код до этапов 2–6. Прямой доступ к репозиториям, независимые
> CRUD-коммиты и догоняющие изменения старой схемы удаляются в своих этапах.

## 1. Назначение документа

Этот документ фиксирует фактическую модель хранения бизнес-объектов в системе `BusinessEntity`.

Документ описывает:

- базовые runtime-сущности
- DTO-слой хранения
- физическое хранение в Postgres
- канонический формат payload `BusinessEntityData`
- цепочку записи документа
- разделение основной базы и базы логгера

Документ является нормативным описанием текущего storage-контура. При изменении модели хранения он должен обновляться.

---

## 2. Базовые runtime-сущности

В текущей бизнес-модели есть три базовые runtime-сущности:

1. `BusinessEntity`
2. `BusinessEntityData`
3. `BusinessEntityRelation`

### `BusinessEntity`

`BusinessEntity` представляет сам объект в системе.

Это:

- узел дерева
- identity объекта
- объект, участвующий в связях
- runtime-модель, которая хранится как `BusinessEntityDto`

`BusinessEntity` содержит:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `Name`
- `BusinessEntityType`
- `EntityType`

`BusinessEntity` может существовать без payload.

Примеры:

- `Space`
- `Folder`

### `BusinessEntityData`

`BusinessEntityData` представляет payload-часть объекта.

Это:

- подчиненный объект по отношению к `BusinessEntity`
- runtime-модель содержимого data-backed сущности
- базовый тип для тяжеловесных бизнес-объектов

Он содержит:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `Name`
- `EntityType`
- `Tag`

`BusinessEntityData` не является самостоятельным корневым объектом.

### `BusinessEntityRelation`

`BusinessEntityRelation` представляет связь между двумя `BusinessEntity`.

Она содержит:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `ObjectAId`
- `ObjectBId`
- `RelationType`
- `RelationParams`

---

## 3. Специализированные runtime-типы

### `Document`

`Document` является наследником `BusinessEntityData`.

В текущей модели документ содержит:

- `Text`
- `Tag`
- базовые поля `BusinessEntityData`

### `BusinessEntity<T>`

В системе существует обобщенный runtime-агрегат:

- `BusinessEntity<T> where T : IBusinessEntityData`

Его назначение:

- держать рядом базовую entity и typed payload
- упростить runtime-работу с data-backed объектами

Пример:

- `BusinessEntity<Document>`

---

## 4. DTO-слой хранения

В storage-слое используются три базовых DTO графа:

1. `BusinessEntityDto`
2. `BusinessEntityDataDto`
3. `BusinessEntityRelationDto`

Кроме них в storage-слое есть технические DTO, которые не являются самостоятельными runtime-сущностями графа:

1. `BusinessEntityDataChunkDto`
2. `BusinessEntityPropertyDto`
3. `BusinessEntityDataPropertyDto`
4. `BusinessEntityDataChunkPropertyDto`

Соответствие runtime -> storage:

- `BusinessEntity` <-> `BusinessEntityDto`
- `BusinessEntityData` <-> `BusinessEntityDataDto`
- `BusinessEntityRelation` <-> `BusinessEntityRelationDto`

Технические DTO не имеют прямого runtime-аналога уровня бизнес-объекта. Они обслуживают хранение частей данных, индексов, кешей и вспомогательных свойств.

### `BusinessEntityDto`

Хранит метаданные сущности:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `Name`
- `BusinessEntityType`
- `EntityType`

### `BusinessEntityDataDto`

Хранит payload сущности:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `BusinessEntityId`
- `Data : string`

Важно:

- `BusinessEntityDataDto.Data` хранит не raw text и не `byte[]`
- `Data` хранит minified JSON string
- JSON string обязан иметь форму versioned envelope

Канонический формат:

```json
{"schemaVersion":1,"kind":"Document","payload":{"text":"Документ","tag":"Document"}}
```

### `BusinessEntityRelationDto`

Хранит связь между сущностями:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `ObjectAId`
- `ObjectBId`
- `RelationType`
- `RelationParams`

### `BusinessEntityDataChunkDto`

Технический DTO одного чанка данных, сейчас используется для chunked rich-text storage.

Он не является четвертой базовой runtime-сущностью графа и не должен трактоваться как самостоятельный бизнес-объект.

Он содержит:

- `Id`
- `CreatedDate`
- `LastModifiedDate`
- `BusinessEntityId`
- `SortOrder`
- `Data : string`
- `PlainText`
- `HtmlCache`
- `BlockCount`
- `CharCount`
- `DataSizeBytes`
- `Version`
- `Checksum`

### `IPropertyDto`

`IPropertyDto` — общий storage-контракт для технических property-строк.

Property DTO нужны для хранения вспомогательных частей данных и признаков объектов storage-слоя, которые не являются отдельными бизнес-объектами графа.

`IPropertyDto` содержит базовые поля `IBaseEntity` и дополнительно:

- `ParentEntityId : Guid`
- `PropertyType : int`
- `Data : string`
- `Metadata : string`

`PropertyType` физически хранится как `int`, но его значения должны задаваться через enum соответствующего property-слоя:

- `BusinessEntityPropertyTypeEnum`
- `BusinessEntityDataPropertyTypeEnum`
- `BusinessEntityDataChunkPropertyTypeEnum`

Числовые значения enum являются частью storage-контракта. После записи данных в БД их нельзя переиспользовать для другого смысла.

Текущие значения:

- `BusinessEntityPropertyTypeEnum.Undefined = 0`
- `BusinessEntityPropertyTypeEnum.GenericSpaceProperties = 1`
- `BusinessEntityDataPropertyTypeEnum.Undefined = 0`
- `BusinessEntityDataChunkPropertyTypeEnum.Undefined = 0`
- `BusinessEntityDataChunkPropertyTypeEnum.RichDocTableOfContents = 100`

`RichDocTableOfContents` предназначен для хранения содержания текстовых кусков, которые образуются при формировании `RichTextDocument`.

Для `RichDocTableOfContents` действуют правила:

- в оглавление включаются только heading-блоки уровней H1-H3;
- H4-H6 и остальные типы блоков не попадают в это property;
- если в чанке нет H1-H3, `BusinessEntityDataChunkPropertyDto` с типом `RichDocTableOfContents` для него не создается;
- property создается при сохранении `BusinessEntityDataChunkDto`;
- property может быть пересоздана отдельной асинхронной процедурой: процедура проходит по всем чанкам документа, удаляет старые properties типа `RichDocTableOfContents`, заново строит их из сохраненных блоков чанка, затем читает все properties из БД и возвращает дерево содержания;
- при пересоздании содержания процедура также обновляет `HtmlCache` чанка, чтобы в HTML были актуальные stable anchors для H1-H3;
- чтение оглавления выполняется отдельной асинхронной процедурой строго из property-таблиц БД, а не через парсинг HTML.

Формат `BusinessEntityDataChunkPropertyDto.Data` для `RichDocTableOfContents`:

```json
{"schemaVersion":1,"kind":"RichDocChunkTableOfContents","entries":[{"chunkId":"00000000-0000-0000-0000-000000000000","chunkSortOrder":0,"blockIndex":0,"level":1,"title":"Heading","anchor":"rt-chunk-00000000000000000000000000000000-block-0"}]}
```

Каждая entry должна содержать достаточно данных для точной навигации:

- `chunkId` — идентификатор `BusinessEntityDataChunkDto`;
- `chunkSortOrder` — порядок чанка внутри rich-text документа;
- `blockIndex` — zero-based индекс heading-блока внутри чанка;
- `level` — уровень heading, только 1..3;
- `title` — plain-text заголовок для отображения в дереве оглавления;
- `anchor` — стабильный DOM id, построенный из `chunkId` и `blockIndex`.

Формат `Metadata` для этого property:

```json
{"schemaVersion":1,"kind":"RichDocChunkTableOfContentsMetadata","entryCount":1}
```

HTML-cache чанка обязан ставить тот же `anchor` в атрибут `id` соответствующего H1-H3, чтобы клик по оглавлению мог точно прокрутить viewport к нужному блоку.

### Virtualized RichTextDocument viewport

Просмотр больших `RichTextDocument` не должен склеивать все `BusinessEntityDataChunkDto.HtmlCache` в одну строку и загружать весь документ в DOM.

Правильная runtime-механика просмотра:

- открытие документа загружает только shell (`BusinessEntity` + manifest), содержание из property-таблиц и начальное окно чанков;
- начальное окно содержит первые 2 чанка;
- публичные доменные операции чтения окон чанков находятся в `RichTextDocumentHelper`, а не в DataProvider connector/message bus;
- DataProvider/storage layer может предоставлять только generic repository операции вроде filtered count и ordered page без знания rich-doc домена;
- viewport рендерит только текущее окно чанков, каждый чанк отдельным DOM-контейнером;
- сверху и снизу окна стоят spacer-элементы, высота которых имитирует невидимые чанки документа;
- высоты реально отрендеренных чанков измеряются в браузере и кешируются в runtime-состоянии viewport;
- для неизвестных высот используется оценочная средняя высота чанка;
- при scroll/drag scrollbar viewport вычисляет примерный `chunkSortOrder` по `scrollTop` и загружает окно вокруг нужной позиции;
- при клике по содержанию используется `chunkSortOrder + anchor`: viewport сначала загружает окно вокруг нужного чанка, затем скроллит к `anchor`;
- `PageUp`/`PageDown` работают через тот же scroll pipeline и не должны требовать загрузки всего документа.

Для больших документов запрещено на странице делать:

```csharp
HtmlContent = string.Join(Environment.NewLine, allChunks.Select(x => x.HtmlCache));
```

Такой full-DOM путь допустим только как legacy/debug fallback, но не как основной механизм просмотра.

### Property DTO

В storage-слое есть три конкретных property DTO:

- `BusinessEntityPropertyDto`
- `BusinessEntityDataPropertyDto`
- `BusinessEntityDataChunkPropertyDto`

Правила привязки:

- `BusinessEntityPropertyDto.ParentEntityId` ссылается на `BusinessEntityDto.Id`
- `BusinessEntityDataPropertyDto.ParentEntityId` ссылается на `BusinessEntityDataDto.Id`
- `BusinessEntityDataChunkPropertyDto.ParentEntityId` ссылается на `BusinessEntityDataChunkDto.Id`

Жизненный цикл property-строк подчинен родительской storage-строке:

- при удалении `BusinessEntityDto` должны удаляться его `BusinessEntityPropertyDto`
- при удалении `BusinessEntityDataDto` должны удаляться его `BusinessEntityDataPropertyDto`
- при удалении `BusinessEntityDataChunkDto` должны удаляться его `BusinessEntityDataChunkPropertyDto`
- при полной замене chunk-набора старые `BusinessEntityDataChunkPropertyDto` должны удаляться вместе со старыми chunk DTO

Property DTO не должны использоваться для хранения новых бизнес-сущностей, связей графа или основного payload. Для бизнес-сущностей используется `BusinessEntityDto`, для связей — `BusinessEntityRelationDto`, для payload — `BusinessEntityDataDto` и технические chunk DTO.

---

## 5. Физическое хранение в Postgres

### Разделение баз данных

Основное приложение и веб-логгер используют один Postgres-сервер, но разные базы данных.

Текущее разделение:

- `business_entity` — основная база приложения `BusinessEntity`
- `web_logger` — отдельная база `BlazorServerWebLogger`

Это обязательное правило архитектуры:

- бизнес-объекты не должны храниться в базе логгера
- таблицы логгера не должны смешиваться с таблицами `BusinessEntity`

### Таблицы основной базы `business_entity`

В основной базе используются базовые таблицы графа:

1. `BusinessEntities`
2. `BusinessEntityRelations`
3. `BusinessEntityDataItems`

Также используются технические таблицы storage-слоя:

1. `BusinessEntityDataChunks`
2. `BusinessEntityProperties`
3. `BusinessEntityDataProperties`
4. `BusinessEntityDataChunkProperties`

Соответствие:

- `BusinessEntities` <- `BusinessEntityDto`
- `BusinessEntityRelations` <- `BusinessEntityRelationDto`
- `BusinessEntityDataItems` <- `BusinessEntityDataDto`
- `BusinessEntityDataChunks` <- `BusinessEntityDataChunkDto`
- `BusinessEntityProperties` <- `BusinessEntityPropertyDto`
- `BusinessEntityDataProperties` <- `BusinessEntityDataPropertyDto`
- `BusinessEntityDataChunkProperties` <- `BusinessEntityDataChunkPropertyDto`

### Таблицы базы логгера `web_logger`

В базе логгера живут только таблицы логгера.

Например:

- `LogEntries`
- `AppSettingsDbStorable`

Они не относятся к storage-контуру бизнес-объектов.

---

## 6. Идентичность и подчиненность data-объекта

`BusinessEntityData` подчинен `BusinessEntity`.

Фактические правила текущей системы:

- `BusinessEntityData.Id == BusinessEntity.Id`
- `BusinessEntityDataDto.Id == BusinessEntity.Id`
- `BusinessEntityDataDto.BusinessEntityId == BusinessEntity.Id`

Payload-объект не имеет собственной независимой identity.

---

## 7. Канонический формат payload

### Общее правило

Payload хранится отдельно от `BusinessEntity` в `BusinessEntityDataDto.Data`.

Payload не лежит в `BusinessEntityDto`.

Payload не хранится как `byte[]`.

### Канонический формат

Канонический формат хранения payload — **Versioned JSON Envelope**.

Обязательная структура:

```json
{
  "schemaVersion": 1,
  "kind": "<LogicalPayloadKind>",
  "payload": { ... }
}
```

Где:

- `schemaVersion` — версия схемы хранимого payload
- `kind` — логический discriminator, независимый от CLR type name
- `payload` — сам сериализованный бизнес-объект

### Требования к формату

Нужно обеспечивать:

- minified JSON без форматирования
- читаемый Unicode в БД без принудительного `\uXXXX` escaping для кириллицы и другого non-ASCII текста
- стабильные ключи `schemaVersion`, `kind`, `payload`
- отсутствие double-encoding
- отсутствие зависимости от полного CLR type name
- готовность к повышению `schemaVersion`

### Централизованная JSON-сериализация

Storage-контур использует единые `StorageJsonOptions`.

Фактические правила текущей реализации:

- envelope сериализуется через `JsonSerializer.Serialize(..., StorageJsonOptions.Default)`
- raw payload сериализуется через `JsonSerializer.Serialize(..., StorageJsonOptions.Default)`
- envelope десериализуется через `JsonSerializer.Deserialize(..., StorageJsonOptions.Default)`
- payload десериализуется через `JsonSerializer.Deserialize(..., StorageJsonOptions.Default)`

`StorageJsonOptions.Default` задает:

- `Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)`
- `WriteIndented = false`
- `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`
- `DefaultIgnoreCondition = JsonIgnoreCondition.Never`

Это обязательная часть текущего storage-формата, потому что JSON в БД должен оставаться одновременно:

- minified
- валидным
- читаемым глазами
- независимым от `byte[]`-pipeline

### Как трактуется `kind`

`kind` — это не полное имя .NET-типа.

Правильно:

- `Document`
- `FolderData`
- `WikiPage`

Неправильно:

- `My.Namespace.Document, MyAssembly`

---

## 8. Как сейчас выполняется запись payload

### Общий принцип

Новый канонический pipeline:

```text
runtime object -> raw JSON -> versioned JSON envelope string -> DB
```

Payload сохраняется через `DataProviderMiniApp`.

### Порядок записи

1. `BusinessEntityHelper` вызывает `IDataProviderConnector.UpdateDataAsync(id, data)`
2. `DataProviderConnector` сериализует runtime-данные через `JsonSerializer.Serialize(...)`
3. `DataProviderMessageHandler` передает raw JSON в `DataProviderService`
4. `DataProviderService` читает `BusinessEntityDto`, определяет логический `kind`
5. `DataProviderService` заворачивает raw JSON в versioned envelope
6. строка envelope сохраняется в `BusinessEntityDataDto.Data`

Во всех шагах JSON-serialization используется `StorageJsonOptions.Default`.

### Специальный случай документа

Сейчас документ выше по стеку по-прежнему приходит как `string`.

Поэтому внутри `DataProviderMiniApp` при записи документа raw JSON-строка преобразуется в object-payload вида:

```json
{"text":"Документ","tag":"Document"}
```

После этого payload заворачивается в envelope:

```json
{"schemaVersion":1,"kind":"Document","payload":{"text":"Документ","tag":"Document"}}
```

---

## 9. Как сейчас выполняется чтение payload

Чтение идет в две стадии:

1. из `BusinessEntityDataDto.Data` читается JSON-envelope строка
2. envelope десериализуется в raw-model
3. проверяется `schemaVersion`
4. проверяется наличие `kind`
5. из поля `payload` выполняется десериализация в нужный runtime-тип

### Специальный случай документа

Для вызова `GetDataAsync<string>(id)` у документа storage-слой извлекает `payload.text`.

То есть runtime-код выше по стеку продолжает получать текст документа как `string`, хотя в БД уже лежит object-payload внутри envelope.

Чтение envelope и payload также использует те же `StorageJsonOptions.Default`, что и запись.

---

## 10. Как сейчас создается документ

Документ в текущей системе создается как три независимые storage-записи.

Алгоритм:

1. создается `BusinessEntity` типа `Document`
2. создается `BusinessEntityRelation` типа `Contains` между родителем и документом
3. сохраняется payload документа в `BusinessEntityDataDto`

### Runtime-порядок вызовов

Упрощенная цепочка:

`SampleDataService -> BusinessEntityHelper -> DataProviderConnector -> DataProviderMessageHandler -> DataProviderService -> EF/Postgres repository -> Postgres`

### Что записывается в БД

Для документа появляются:

1. запись в `BusinessEntities`
2. запись в `BusinessEntityRelations`
3. запись в `BusinessEntityDataItems`

Для папки:

1. запись в `BusinessEntities`
2. запись в `BusinessEntityRelations`

Для пространства:

1. запись только в `BusinessEntities`

---

## 11. Где живет storage-логика

Вся storage-логика живет внутри `DataProviderMiniApp`.

Это включает:

- `DataProviderConnector`
- `DataProviderMessageHandler`
- `DataProviderService`
- `DataPayloadEnvelopeSerializer`
- `DataProviderMapper`
- репозитории DTO

Слои разделены так:

- `BusinessEntityHelper` — бизнес-операции
- `DataProviderConnector` — bus request/response facade
- `DataProviderMessageHandler` — подписка на storage messages
- `DataProviderService` — прикладной CRUD DTO-слоя
- `IAsyncRepository<T>` — абстракция репозитория
- `EfPostgresAsyncRepositoryBase<T>` — текущая Postgres-реализация

---

## 12. Текущая реализация репозиториев

На текущий момент `DataProviderMiniApp` использует Postgres-репозитории.

В DI зарегистрированы:

- `BusinessEntityDtoEfPostgresRepository`
- `BusinessEntityDataDtoEfPostgresRepository`
- `BusinessEntityDataChunkDtoEfPostgresRepository`
- `BusinessEntityPropertyDtoEfPostgresRepository`
- `BusinessEntityDataPropertyDtoEfPostgresRepository`
- `BusinessEntityDataChunkPropertyDtoEfPostgresRepository`
- `BusinessEntityRelationDtoEfPostgresRepository`

Для CRUD-операций используется свежий `KmsBusinessEntityDbContext` на каждую операцию.

Это сделано для того, чтобы:

- не накапливать tracked entities между вызовами
- избегать конфликтов EF tracking при сидировании и runtime-операциях

---

## 13. Инициализация схемы

При старте `BusinessEntity` приложение явно гарантирует наличие таблиц:

- `BusinessEntities`
- `BusinessEntityRelations`
- `BusinessEntityDataItems`
- `BusinessEntityDataChunks`
- `BusinessEntityProperties`
- `BusinessEntityDataProperties`
- `BusinessEntityDataChunkProperties`

Для строковых payload/property-полей используется тип `text`:

- `BusinessEntityDataItems.Data`
- `BusinessEntityDataChunks.Data`
- `BusinessEntityDataChunks.PlainText`
- `BusinessEntityDataChunks.HtmlCache`
- `BusinessEntityDataChunks.Checksum`
- `BusinessEntityProperties.Data`
- `BusinessEntityProperties.Metadata`
- `BusinessEntityDataProperties.Data`
- `BusinessEntityDataProperties.Metadata`
- `BusinessEntityDataChunkProperties.Data`
- `BusinessEntityDataChunkProperties.Metadata`

Причина:

- payload хранится как JSON string
- property data и metadata хранятся как string-представление технических данных
- схема должна соответствовать string-based envelope storage

Для property-таблиц обязательны индексы по `ParentEntityId` и по паре `ParentEntityId + PropertyType`, чтобы быстро получать свойства конкретной storage-строки и свойства конкретного технического типа.

---

## 14. Текущая семантика связей

В дереве используется базовый тип связи:

- `Contains`

Тип `VisuallyContains` выведен из системы.

Следствие:

- дерево документов и папок строится на `Contains`
- удаление поддерева идет по `Contains`
- поиск детей идет по `Contains`
- смена родителя идет по `Contains`

---

## 15. Что считается правильным storage-повторением объекта

### Простая сущность

Пример:

- `Space`

Хранение:

- только `BusinessEntityDto`

### Контейнерная сущность

Пример:

- `Folder`

Хранение:

- `BusinessEntityDto`
- `BusinessEntityRelationDto`

### Data-backed сущность

Пример:

- `Document`

Хранение:

- `BusinessEntityDto`
- `BusinessEntityRelationDto`
- `BusinessEntityDataDto`
- опционально `BusinessEntityPropertyDto`
- опционально `BusinessEntityDataPropertyDto`
- опционально `BusinessEntityDataChunkDto`
- опционально `BusinessEntityDataChunkPropertyDto`

---

## 16. Ограничения и правила

Запрещено:

- хранить payload внутри `BusinessEntityDto`
- хранить payload как `byte[]` как канонический формат
- сериализовать payload в UTF-8 bytes как основной storage pipeline
- смешивать таблицы логгера и бизнес-объектов в одной базе данных
- использовать `BusinessEntityData` как независимую identity
- хранить связи внутри payload вместо `BusinessEntityRelation`
- использовать property DTO как самостоятельные бизнес-объекты графа
- хранить graph-связи в property DTO вместо `BusinessEntityRelationDto`
- хранить основной payload объекта в property DTO вместо `BusinessEntityDataDto`

Допустимо:

- иметь `BusinessEntity` без `BusinessEntityData`
- хранить payload как minified JSON string
- расширять payload новыми typed-наследниками `BusinessEntityData`
- менять формат только вместе с пустым storage текущей схемы; старые данные не интерпретируются
- хранить технические свойства storage-строк в property DTO
- хранить крупный или структурированный технический data-body в `BusinessEntityDataChunkDto`

---

## 17. Транзакционное хранение этапа 2

EntitySaveBatch — единственный путь изменений графа. Пакет связанных карточек,
payload, точных references чанков, связей, свойств, комментариев и файлов проходит
через одну шину, один DbContext и одну SQL-транзакцию. DataProviderOperations
содержит хэш и результат того же коммита; повтор OperationId не дублирует версию.
Пакеты принимаются последовательно. Репозиторные CRUD API удалены.

DbContext, EF DTO, SQL и файловое хранилище находятся в DataProvider.Internal.
UserMiniApp владеет своими отдельными служебными таблицами. Внешние потребители
получают только runtime-модели и read-контракты через connector.

Публикация хранится в BusinessEntities.PublishedVersion. Payload ID равен
EntityId; Version >= 1. BusinessEntityChunkReferences содержит EntityId,
DocumentVersion, Position, ChunkId и конкретную ChunkRowVersion. Выбор последней
строки чанка «не новее версии» не используется. Полные правила:
[история](business-entity-versioning-policy.md), [файлы](file-object-storage-policy.md).

Схема DataProvider версии 2 и UserMiniApp версии 1 создаётся только с нуля;
несовместимая существующая схема даёт явный отказ. Догоняющих DDL и backfill нет.

## 18. Практический итог

На текущий момент система хранения устроена так:

- runtime-модель разделена на `BusinessEntity`, `BusinessEntityData`, `BusinessEntityRelation`
- storage-модель разделена на базовые `BusinessEntityDto`, `BusinessEntityDataDto`, `BusinessEntityRelationDto` и технические DTO для chunk/property-хранения
- основное приложение хранит данные в базе `business_entity`
- веб-логгер хранит данные в базе `web_logger`
- payload `BusinessEntityData` хранится как minified JSON string
- технические свойства storage-строк хранятся в `BusinessEntityPropertyDto`, `BusinessEntityDataPropertyDto`, `BusinessEntityDataChunkPropertyDto`
- канонический формат payload — versioned JSON envelope
- дерево и граф документов строятся через relation типа `Contains`
- запись и чтение идут через `DataProviderMiniApp`

Это и есть текущий канонический контур хранения бизнес-объектов в проекте.
