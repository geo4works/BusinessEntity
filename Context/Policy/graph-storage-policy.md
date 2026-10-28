# Хранение графа и транзакции

Фактическая реализация TASK_ARCH_001, версия приложения 0.17.0.
Норма: [ТЗ v2.2](../Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md).

## Владельцы

BusinessEntity/Data/Chunk/Block/Relation — общие runtime-модели Core.
Предметные payload и правила объявляют владельцы типов через описатели.
BusinessEntityHelper — единственный общий движковый сервис, без EF/репозиториев.

DataProviderMiniApp единолично владеет каноническим Postgres-графом, EF DTO,
DbContext, codec, кэшем и постоянными entity-файлами. Межминиаппный доступ —
Contracts/DataProviderConnector/шина; другие владельцы не получают его внутренности.

UserMiniApp имеет собственную схему служебных таблиц пользователей, свойств,
ролей, групп, memberships и assignments в той же БД. Это исключение не разрешает
ему прямо менять таблицы графа. UserOwnedStorageWriter исполняет связанные
изменения форм в общей открытой транзакции DataProvider. Самостоятельные операции
своих служебных таблиц остаются у User. Authentik — внешний API вне SQL-транзакции.
БД web-логгера отдельная и не является частью графа.

## Текущая схема

DataProviderSchema version 2 создаётся только в пустой БД. User schema version 1
инициализируется своим владельцем. Старая/неизвестная схема отклоняется явно.
Миграций, backfill и догоняющих ALTER TABLE нет.

| Таблицы | Назначение |
|---|---|
| BusinessEntities | карточки, тип, название, служебные даты, указатель публикации |
| BusinessEntityRelations | направленные связи |
| BusinessEntityDataItems | полные typed payload-версии, ключ (Id, Version) |
| BusinessEntityDataChunks | версии строк чанков, ключ (Id, Version) |
| BusinessEntityChunkReferences | точный упорядоченный состав версии статьи |
| BusinessEntityProperties / DataProperties / DataChunkProperties | свойства соответствующего уровня |
| BusinessEntityComments | комментарии и ответы |
| BusinessEntityFiles / BusinessEntityVersionFiles | manifest файлов и исторические зависимости |
| DataProviderOperations | durable исход OperationId, hash, автор, время, результат |
| DataProviderSchema | версия установленной схемы |

Фактические ограничения и индексы задаёт KmsBusinessEntityDbContext внутри
DataProvider. Составные ссылки чанков включают конкретный ChunkId/ChunkVersion,
а не запрос «последний чанк». Canonical payload имеет единственный текущий JSON
envelope, владелец регистрирует converter по явному StorageKind. Неизвестные
ключи, типы и повреждённые обязательные данные дают BusError.

## Запись

Владелец формирует EntitySaveBatch: OperationId, MiniAppId, EditSessionId,
изменения карточек/payload/чанков/связей/свойств/комментариев/файлов и при необходимости
пакет своих служебных таблиц. Payload фиксируется независимо от живого UI.

DataProvider:

1. Проверяет адрес/автора, формат пакета, типы, базовые версии и ограничения графа.
2. Под writer gate готовит независимый пакет, неизменяемые файлы и их digest.
3. Открывает одну SQL-транзакцию, применяет связанные изменения и writer-ы
   служебных владельцев; ограничения/FK проверяют целостность.
4. Назначает новые номера версий последовательно. Состояние версии строится от
   базы автора, без слияния с чужими параллельными правками.
5. Записывает DataProviderOperations и результат в той же транзакции. Кандидат
   immutable кэша строится до commit из этой же транзакции.
6. После успешного commit публикует кэш, инвалидирует тяжёлые payload и отправляет
   EntityBatchCommitted/события изменений. До commit события успеха не публикуются.
7. Выполняет уборку файлов по достоверному manifest; её отказ не меняет уже
   зафиксированный SQL outcome и повторяется recovery.

БД применяет весь пакет или откатывает весь пакет. SQL rollback не отменяет
внешний Authentik API и не является общей транзакцией с файловой системой.

## Повтор и неопределённый исход

RequestId коррелирует конкретный запрос; OperationId обозначает принятый пакет.
Таймаут/отмена ожидания не означает rollback. Клиент проверяет durable outcome
и при Unknown повторяет тот же frozen пакет, сохраняя новые правки отдельно.
Одинаковый OperationId/hash возвращает прежний результат; другой пакет с тем же
ID отвергается. Учёт исходов по умолчанию хранится 7 дней, настройка
DataProvider:OperationRetentionDays. Это технический учёт, не история пространства.

## Кэш и чтение

DataProviderGraphCache публикует целый immutable snapshot структурных метаданных.
Чтение прогретых проекций карточек, связей и навигационных свойств не обращается
к SQL. Наружу выдаются значения/копии, которыми нельзя изменить общий кэш.
Права не встраиваются в snapshot: User рассчитывает их в контексте потребителя.

Кэш содержит metadata актуальных/опубликованных версий, без всех тяжёлых тел.
BoundedPayloadCache — ограниченный LRU по entity/version; значения десериализуются
в свежие модели. По умолчанию 32 MiB и 128 записей, настройки
PayloadCacheMaxBytes/PayloadCacheMaxEntries секции DataProvider. История и окна
чанков читаются нужным объёмом; массовой загрузки всех тел в память нет.

Предметное окно, поиск, оглавление и редактирование статьи находятся в
RichTextDocumentMiniApp: Reader/Search/Outline/Edit/File/Import services.
DataProvider предоставляет generic чтение точного состава/окон и конвертацию,
не определяет UX статьи.

## Связанные нормы и проверки

[История](business-entity-versioning-policy.md), [файлы](file-object-storage-policy.md),
[Save/Discard](MiniApps/miniapp-saving-policy.md), [backup](BackupRestore/README.md).

Реальные Postgres-тесты проверяют rollback всего пакета, граф/Contains,
конкурентные полные снимки, durable retry после нового writer, no-op/публикацию,
точные исторические чанки, файлы/recovery, архив/restore и ноль SQL на прогретой
структуре. Без отдельного BE_TEST_POSTGRES эти проверки явно skipped.
