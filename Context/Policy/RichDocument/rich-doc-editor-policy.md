# Политика редактирования Rich Document

## 1. Назначение

Этот документ фиксирует актуальную политику редактирования rich-text документов в системе `BusinessEntity`.

Политика описывает поведение edit-mode и его связь с chunked storage:

- документ не загружается целиком в DOM и редактор;
- редактирование выполняется оконно, по чанкам;
- dirty-состояние чанков хранится отдельно от БД до явного сохранения;
- переходы по содержанию и скролл работают по тем же принципам, что и read viewport;
- название документа редактируется вместе с содержимым;
- Tiptap / ProseMirror используется как UI/editor engine, но не как canonical storage format.

## 2. Базовый принцип

Rich document в режиме редактирования открывается не как один большой редактор.

Основная модель:

```text
Документ редактируется оконно.
В DOM и Tiptap одновременно находятся только чанки текущего edit window.
```

Все чанки, которые находятся в editor viewport, являются редактируемыми.

Если chunk выгружен из editor viewport:

- он больше не отображается;
- его Tiptap instance уничтожается;
- если chunk был изменен, его HTML draft остается во внутреннем dirty cache;
- если chunk не был изменен, он не остается в edit cache и при необходимости читается заново.

Readonly preview чанков в edit mode не используется.

## 3. Режимы документа

Rich document имеет два основных UI-режима:

1. `Read`
2. `Edit`

### Read mode

В режиме чтения:

- используется `RichTextDocumentViewport`;
- работает оконная подгрузка чанков;
- содержание читается из сохраненных chunk properties выбранной версии;
- есть кнопка `Edit`;
- при нажатии `Edit` фиксируется текущий видимый chunk `SortOrder`.

Edit доступен только для последней версии документа.
Все версии кроме последней открываются только для просмотра.

### Edit mode

В режиме редактирования:

- используется `RichTextDocumentEditorViewport`;
- есть кнопка `Save`;
- есть кнопка с иконкой открытой книги для возврата в режим чтения;
- название документа отображается как поле ввода;
- toolbar применяется к активному Tiptap editor;
- отображается индикатор несохраненных изменений, если есть dirty chunks.

Переход из `Read` в `Edit` должен открывать редактор не обязательно в начале документа, а в текущей позиции чтения.

Правило:

```text
Если пользователь нажал Edit из read viewport, edit viewport открывает окно вокруг текущего видимого чанка.
```

Точность текущей реализации: на уровне чанка. Точное восстановление позиции внутри чанка можно добавить отдельным этапом через block id, heading id или intra-chunk offset.

## 4. Открытие edit mode

При открытии edit mode:

1. shell документа и содержание уже обслуживаются страницей документа;
2. read viewport сообщает текущий видимый `SortOrder`, если переход выполнен из read mode;
3. editor viewport получает `InitialTargetSortOrder`;
4. если target chunk не входит в уже переданное начальное окно, editor viewport читает окно вокруг target chunk;
5. после отрисовки editor viewport прокручивается так, чтобы target chunk был видим;
6. если `InitialTargetSortOrder` не задан, используется начальное окно от первого чанка.

Настройка количества чанков при открытии:

```text
RichTextEditChunksOnOpen
```

Если пользователь входит в edit mode из read mode, приоритет имеет текущий видимый chunk, а не начало документа.

Edit mode всегда работает с `ViewedVersion == LatestVersion`.
Если пользователь переключился на старую версию, UI должен выйти из edit mode или запретить вход в edit mode.

## 5. Editor viewport

Editor viewport повторяет общую модель read viewport:

- `LoadedChunks`;
- `TotalChunkCount`;
- top spacer;
- bottom spacer;
- estimated chunk height;
- загрузка окон чанков;
- переходы по `SortOrder`;
- измерение фактической высоты чанков;
- пересчет spacer-ов.

Дополнительно editor viewport содержит edit-state:

- dirty draft cache;
- множество dirty sort orders;
- JS registry активных Tiptap editors;
- активный editor для toolbar-команд;
- pending anchor / pending visible chunk для scroll restore;
- защиту от конкурентных загрузок окна через load version.

Editor viewport не должен грузить весь документ.

## 6. Размер edit window

Размер окна редактирования задается системными параметрами, доступными в админке:

```text
RichTextEditChunksBeforeFocused
RichTextEditChunksAfterFocused
RichTextEditChunksOnOpen
```

Расчет окна вокруг целевого чанка:

```text
start = targetSortOrder - RichTextEditChunksBeforeFocused
take  = RichTextEditChunksBeforeFocused + 1 + RichTextEditChunksAfterFocused
```

Границы нормализуются по диапазону документа.

Целевой режим для больших документов:

```text
В editor viewport обычно находятся 2-3 чанка.
```

Это не жесткий лимит, а управляемая настройками политика.

## 7. Источник данных для чанка

Когда editor viewport должен показать chunk, источник выбирается так:

```text
1. Dirty cache
2. Уже загруженные LoadedChunks
3. Database
```

Если для sort order есть dirty draft, именно он показывается в редакторе.

Версия из БД не должна затирать dirty draft до явного `Save` или будущего явного discard-сценария.

## 8. Dirty cache

В edit mode существует внутренний cache несохраненных изменений.

Cache хранит только измененные чанки.

Принципиальное правило:

```text
Неизмененные просмотренные чанки не кешируются как edit draft.
```

Причины:

- документ может быть большим;
- хранить все просмотренные чанки дорого;
- неизмененный chunk можно прочитать из БД повторно;
- измененный chunk нельзя потерять до сохранения.

Текущая draft-модель:

```text
EditorChunkDraft
    ChunkId
    SortOrder
    OriginalHtml
    CurrentHtml
```

Dirty cache keyed by `SortOrder`.

## 9. Когда chunk становится dirty

Chunk становится dirty в момент фактического изменения в Tiptap editor:

- ввод текста;
- удаление символов;
- вставка;
- вставка inline-картинки как atom внутри текста;
- команда форматирования;
- изменение heading / paragraph mode.

Dirty-состояние не должно ждать dispose чанка.

Текущий flow:

1. Tiptap `onUpdate` срабатывает при изменении editor state;
2. JS помечает editor state как dirty;
3. JS отправляет в Blazor snapshot текущего HTML;
4. Blazor сразу кладет chunk в dirty cache;
5. UI помечает документ как имеющий несохраненные изменения.

Dispose чанка остается страховочным механизмом:

- при смене окна;
- при уничтожении viewport;
- при сохранении;
- при пересинхронизации JS editors.

Но основное попадание в dirty cache происходит в момент правки.

## 10. Выгрузка чанка из editor viewport

Когда chunk выходит из editor viewport:

1. JS сообщает о dispose editor instance;
2. если editor был dirty, его текущее состояние уже должно быть в dirty cache;
3. `CaptureCurrentEditorsAsync` дополнительно забирает dirty snapshots перед сменой окна;
4. неизмененные chunks удаляются из dirty markers;
5. Tiptap instance уничтожается.

Выгрузка чанка не пишет данные в БД.

БД обновляется только по `Save`.

## 11. Переход по содержанию в edit mode

Клик по пункту содержания работает аналогично read viewport.

Алгоритм:

1. outline node дает `HeadingId` и `ChunkSortOrder`;
2. если target chunk уже загружен, выполняется scroll к heading;
3. если heading не найден в DOM, выполняется fallback scroll к chunk;
4. если target chunk не загружен, editor viewport читает окно вокруг target chunk;
5. после отрисовки выполняется scroll к heading или chunk;
6. dirty drafts подставляются вместо данных из БД.

Переход по содержанию не должен терять несохраненные изменения в других чанках.

## 12. Скролл в edit mode

Скролл в edit mode работает по оконной модели.

При приближении к нижней границе текущего окна:

- вычисляется целевой sort order;
- читается новое окно вокруг целевого чанка;
- старые editor instances выгружаются;
- dirty drafts сохраняются;
- после отрисовки viewport удерживается около целевого чанка.

При приближении к верхней границе текущего окна:

- используется тот же принцип, но target window строится назад;
- количество чанков перед/после target берется из тех же edit settings.

Для перетаскивания scrollbar применяется отдельное правило:

```text
Во время drag scrollbar документ не читается постоянно.
После release вычисляется примерная позиция в документе и загружается ближайший пункт содержания / chunk.
```

Это снижает риск зацикливания, повторных чтений и мигания editor viewport.

## 13. PgUp / PgDn

PgUp и PgDn должны работать как обычный скролл editor viewport:

- не загружать весь документ;
- не перечитывать уже загруженные dirty chunks из БД;
- при достижении границы окна подгружать соседнее окно;
- после загрузки удерживать видимую позицию на целевом chunk.

## 14. Синхронизация Tiptap instances

JS-модуль `richTextEditor` владеет runtime instances Tiptap для текущего viewport.

Основные операции:

- `syncEditors` создает editors для текущих visible chunks;
- `collectEditors` возвращает dirty snapshots;
- `destroyEditors` уничтожает все editors viewport;
- `markClean` помечает сохраненные editors чистыми;
- `runCommand` применяет toolbar command к активному editor.

Если DOM host editor-а был пересоздан Blazor-ом, `syncEditors` должен пересоздать Tiptap instance и не оставить белый пустой editor.

## 15. Toolbar

Toolbar работает с активным Tiptap editor.

Активный editor определяется по последнему focused chunk.

Минимальный набор команд:

- Bold;
- Italic;
- Underline;
- Paragraph;
- H1;
- H2;
- H3.

Команда toolbar считается правкой, если она меняет состояние документа, и должна привести chunk в dirty state через Tiptap `onUpdate`.

## 16. Название документа

В edit mode название rich document открывается в поле ввода.

Название сохраняется вместе с содержимым по `Save` и при переходе из edit mode в read mode через кнопку открытой книги.

Фильтрация и нормализация названия находятся в `RichTextTitleRules` владельца RichTextDocumentMiniApp.

Текущие правила:

- максимальная длина: `120` символов;
- управляющие символы запрещены;
- запрещены символы: `< > : " / \ | ? *`;
- повторяющиеся whitespace схлопываются в один пробел;
- начальные whitespace удаляются;
- хвостовые пробелы и точки удаляются;
- пустое название запрещено.

При изменении названия сохраняется shell entity и обновляется дерево через entity update message.

## 17. Save

Кнопка `Save` сохраняет:

- dirty chunks;
- название документа.

Save flow:

1. editor viewport вызывает `CaptureCurrentEditorsAsync`;
2. собираются только dirty drafts;
3. dirty drafts фиксируются в `RichTextWorkingSet.Capture`, затем `RichTextEditService.PrepareEditorBatchAsync` готовит пакет;
4. helper сохраняет только измененные chunks как новые versioned rows;
5. название проходит нормализацию и сохраняется через `SaveRichTextDocumentTitleAsync`;
6. успешно сохраненные editor instances помечаются clean через JS `markClean`;
7. dirty cache очищается;
8. содержание перечитывается асинхронно из БД;
9. UI показывает статус сохранения.

Если dirty chunks нет, но изменено название, сохраняется только название.

Save не должен полностью пересоздавать chunk storage документа.

Если сохранение dirty chunks создает новую версию документа, UI должен перейти на latest version и перечитать зависимые read-side данные этой версии.

## 18. Возврат в read mode

В edit mode есть кнопка с иконкой открытой книги.

Правило:

```text
Переход из edit mode в read mode сначала выполняет Save.
```

Если сохранение прошло успешно:

- edit mode выключается;
- editor viewport уничтожается;
- dirty cache должен быть очищен;
- документ открывается в режиме чтения.

Если сохранение не прошло из-за ошибки валидации названия или другой ошибки, пользователь остается в edit mode.

## 19. Conflict policy

В текущем MVP conflict detection не реализуется.

Правило:

```text
Последний Save выигрывает.
```

Следствия:

- `OriginalVersion` и `OriginalChecksum` не обязательны в draft;
- сервер не отклоняет save из-за версии чанка;
- conflict UI отсутствует.

Future extension:

- добавить `OriginalVersion` и/или `OriginalChecksum`;
- проверять актуальность чанка на сервере;
- отклонять сохранение конкретного чанка при конфликте;
- показывать пользователю conflict resolution UI.

## 20. Tiptap / ProseMirror integration

Tiptap / ProseMirror используется как editor engine для текущих visible chunks.

Tiptap не является storage format системы.

Системные роли:

```text
BusinessEntityDataChunkDto.Data = canonical chunk storage
RichTextBlock[] = canonical chunk domain model
Tiptap editor state = temporary UI/editor model
HtmlCache = render cache and current MVP edit transport
PlainText = search/read helper cache
```

Текущий MVP редактирует HTML чанка через Tiptap и сохраняет его обратно через helper, который конвертирует HTML в rich-text blocks и обновляет chunk metadata.

Долгосрочно нужен стабильный adapter:

```text
RichTextDocumentChunk <-> Tiptap JSON
```

Adapter должен сохранять:

- block identity;
- block kind;
- heading level;
- inline formatting;
- inline image markers в `span.rich-text-inline-image`;
- links;
- image references;
- table structure, когда таблицы будут поддержаны.

## 21. Block identity

Для долгосрочного редактирования каждому блоку нужен стабильный `blockId`.

Текущая модель с `blockIndex` годится как fallback, но недостаточна для сложного редактирования, потому что при вставке/удалении блоков индексы меняются.

Policy:

- добавить стабильный `Id` в `RichTextBlock`;
- существующие блоки без `Id` получают его при нормализации;
- новые блоки получают новые ids;
- heading anchors должны опираться на `blockId`, а не только на `blockIndex`;
- `blockIndex` можно оставить как fallback для старых данных.

## 22. Table of Contents после редактирования

Содержание строится не из DOM, а из сохраненных chunk properties.

После сохранения измененных chunks:

- ToC properties для измененных chunks должны быть актуализированы;
- страница перечитывает содержание из БД асинхронно по текущей latest version;
- UI обновляет outline без полной перезагрузки документа.

В read и edit mode используется одно и то же persisted outline.

Outline не должен ограничиваться только начальными chunks. Для корректной навигации и scrollbar-jump он должен догружаться итеративно по всей просматриваемой версии документа.

## 23. Import в rich document

Импорт считается правкой документа.

Правила импорта:

1. определить текущую последнюю версию документа;
2. вычислить `targetVersion = currentLatestVersion + 1`;
3. импортированные chunks сохранить с `Version = targetVersion`;
4. manifest сохранить как новую `BusinessEntityDataDto` версию с тем же `targetVersion`;
5. создать / обновить table-of-contents properties для импортированных chunks;
6. сбросить просматриваемую версию на latest;
7. перечитать документ тем же flow, что и при открытии.

После импорта UI не должен локально достраивать состояние поверх старой версии. Он должен заново загрузить shell, начальное окно chunks и полное outline новой версии.

## 24. Логирование

Для отладки edit viewport используются специальные web-logger tags:

```text
[rich-doc-edit-window-request]
[rich-doc-edit-window-loaded]
[rich-doc-edit-chunk-cache-put]
[rich-doc-edit-chunk-dispose]
[rich-doc-edit-capture]
```

Правила логов:

- для cache/dispose сообщений `chunkId` должен быть в начале полезной части сообщения;
- `documentId` в этих сообщениях не нужен, если пользователь отлаживает конкретный открытый документ;
- логи должны помогать видеть, когда chunk попал в dirty cache и когда был уничтожен editor instance.

Логирование является диагностическим инструментом и не должно управлять бизнес-логикой.

## 25. Что не делать

Не делать:

- не загружать весь документ в editor;
- не создавать Tiptap editor для каждого чанка большого документа;
- не писать в БД при каждом нажатии клавиши;
- не писать в БД при blur/focus lost;
- не ждать dispose чанка, чтобы пометить его dirty;
- не терять dirty draft при скролле;
- не затирать dirty draft версией из БД;
- не использовать `ReplaceRichTextChunksAsync` для сохранения edit changes;
- не перестраивать все chunk storage документа при сохранении одного чанка;
- не считать Tiptap JSON canonical storage;
- не считать `HtmlCache` долгосрочным canonical editable format;
- не показывать readonly preview chunks внутри edit viewport;
- не использовать временный hardcode для размеров edit window;
- не читать чанки непрерывно во время drag scrollbar;
- не разрешать редактирование старых версий документа;
- не считать import in-place операцией без версии.

## 26. Текущий MVP scope

Текущий MVP edit mode:

1. Read mode имеет кнопку `Edit`.
2. Edit mode имеет кнопку `Save`.
3. Edit mode имеет кнопку возврата в read mode с сохранением.
4. При входе в edit mode из read mode открывается текущий видимый chunk.
5. При входе без стартовой позиции открывается начальное окно документа.
6. Клик по содержанию открывает target chunk в editor viewport.
7. Скролл вверх/вниз подгружает соседние chunks.
8. Drag scrollbar читает документ после release, а не во время движения.
9. В editor viewport находятся только chunks текущего окна, и все они редактируемые.
10. Каждый отображаемый chunk редактируется через Tiptap.
11. Dirty chunk попадает в cache в момент фактической правки.
12. Dispose/capture используются как страховка для dirty-состояния.
13. Возврат к dirty chunk показывает cached draft.
14. В БД изменения пишутся только по `Save`.
15. Save обновляет только dirty chunks.
16. Save сохраняет название документа.
17. Содержание перечитывается после Save асинхронно из БД.
18. Conflict detection и Discard не входят в текущий MVP.
19. Размеры edit window задаются через sys parameters в админке.
20. Старые версии доступны только на чтение.
21. Import создает новую версию и затем перечитывает документ как при открытии.
22. Картинки вставляются как inline atom внутри paragraph/heading HTML; между картинками можно вводить текст.

## 27. Зафиксированные решения

Зафиксированные решения на текущий момент:

1. Cancel/Discard пока не реализуется.
2. Все, что находится в editor viewport, является редактируемым.
3. Preview chunks в edit mode не используются.
4. Если chunk выгружен из editor viewport, его в редакторе нет.
5. Dirty cache хранит только измененные chunks.
6. Dirty state выставляется в момент правки, а не только при dispose.
7. Первый MVP поддерживает paragraphs, headings H1-H3, inline formatting и inline-картинки.
8. Таблицы, галереи/сетки изображений и сложные embedded-элементы добавляются отдельным этапом.
9. Conflict tracking в первом MVP не выполняется.
10. Save сохраняет dirty chunks поверх текущего состояния БД.
11. Название документа сохраняется вместе с edit save.
12. Возврат в read mode сохраняет изменения перед переключением.
13. Старт edit mode из read mode сохраняет позицию на уровне текущего видимого chunk.
14. Размер editor viewport задается через sys parameters.
15. Редактируется только latest version.
16. Import является versioned edit operation.
