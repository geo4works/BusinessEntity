# Политика чтения Rich Document

## 1. Назначение

Этот документ фиксирует read-side политику для rich-text документов, хранящихся в `BusinessEntity`.

Документ описывает концепции и правила реализации для:

- чанкового хранения rich-text документов
- открытия документа
- загрузки содержания
- чтения чанков через viewport
- поведения скролла
- кеширования чанков
- диагностики и логирования
- границ ответственности между storage-инфраструктурой и rich-document доменной логикой

Цель политики: дать возможность читать большие rich-text документы без загрузки всего документа в browser DOM целиком.

Для версионируемых rich-text документов все read-side операции выполняются в контексте выбранной версии документа.
Если версия явно не выбрана, используется последняя версия `BusinessEntityDataDto`.

---

## 2. Базовая модель хранения

Rich-text документ представлен обычным `BusinessEntity` и typed payload в `BusinessEntityData`.

Тело документа не хранится одним большим текстовым полем. Оно режется на упорядоченные чанки, которые хранятся как `BusinessEntityDataChunkDto`.

Базовая структура хранения:

- `BusinessEntityDto` - идентичность документа и объект дерева
- `BusinessEntityDataDto` - manifest документа и метаданные
- `BusinessEntityDataChunkDto` - упорядоченные чанки содержимого
- `BusinessEntityDataChunkPropertyDto` - технические свойства чанков, например данные содержания

`BusinessEntityDataChunkDto` является технической storage-строкой. Он не является бизнес-объектом и не должен трактоваться как узел графа.

Порядок чанков задается полем `SortOrder`. Вся read-side навигация и оценка позиции viewport опирается на `SortOrder` как на стабильный ключ порядка.

---

## 3. Manifest rich-документа

Payload уровня документа хранит manifest, а не полный контент.

Manifest описывает формат и политику хранения документа, например:

- режим хранения содержимого
- формат редактора
- политику чанков
- режим хранения embedded-файлов
- флаги поддержки изображений

Manifest намеренно остается маленьким. Его безопасно читать при открытии документа, и это единственные данные документа, которые должны блокировать первичный render shell страницы.

---

## 4. Содержимое чанка

Каждый rich-text chunk хранит данные, необходимые для render соответствующего диапазона документа.

Важные поля чанка:

- `BusinessEntityId`
- `SortOrder`
- `Data`
- `PlainText`
- `HtmlCache`
- `BlockCount`
- `CharCount`
- `DataSizeBytes`
- `Version`
- `Checksum`

`HtmlCache` является основным read-side полем для browser viewport. Viewport рендерит уже подготовленный HTML и не пересобирает документ из raw blocks при обычном чтении.

Если paragraph/heading HTML содержит inline image marker `span.rich-text-inline-image`, `HtmlCache` должен содержать уже отрендеренный `span > img`, где `src` построен сервером из `documentId + imageId + variant`. Browser viewport не должен доверять сохраненному `src` из editor HTML.

Чанки режутся по настроенному размеру. Read-side логика не должна зависеть от того, что семантические границы идеально совпадают с границами чанков.

---

## 5. Свойства чанков

Свойства чанков хранятся в `BusinessEntityDataChunkPropertyDto`.

Содержание хранится как chunk property с типом:

```text
BusinessEntityDataChunkPropertyTypeEnum.RichDocTableOfContents = 100
```

Property принадлежит чанку через `ParentEntityId`.

Данные property содержат heading entries, найденные внутри этого чанка. Каждая запись должна содержать достаточно информации, чтобы вернуться к точному месту render:

- заголовок
- уровень заголовка
- anchor заголовка
- chunk id
- chunk sort order
- block id или block index, если доступен

Read-side собирает полное outline документа чтением этих chunk properties из storage. Он не должен парсить весь HTML документа в браузере, чтобы построить outline.

---

## 6. Flow открытия документа

Открытие rich-text документа выполняется поэтапно.

Страница должна синхронно читать только shell документа:

1. `BusinessEntity`
2. rich-document manifest
3. список версий / latest version

После получения shell страница может сразу render document view.

Следующие операции должны выполняться асинхронно и независимо:

- загрузка начального окна чанков выбранной версии
- загрузка содержания выбранной версии

Это не дает большому содержанию блокировать первый видимый экран документа.

UI использует отдельные loading-состояния:

- `IsInitialContentLoading`
- `IsOutlineLoading`

`InitialChunkWindow == null` не должно означать "документ пуст", пока `IsInitialContentLoading` равно true.

---

## 7. Начальное окно чанков

При открытии документа viewport загружает только настроенное начальное окно чанков.

Размер начального окна управляется системными настройками.

Поведение по умолчанию: быстро показать первые чанки документа, не ожидая загрузки всего содержания.

Начальное окно передается в `RichTextDocumentViewport` как `InitialWindow`. Viewport применяет его к `LoadedChunks`.

Начальное окно всегда читается по текущей просматриваемой версии:

```text
documentVersion = ViewedVersion
startSortOrder = 0
take = RichTextInitialChunkCount
```

Нельзя читать все chunks документа при открытии. Документ считается потенциально бесконечным.

---

## 8. Загрузка содержания

Outline загружается из сохраненных chunk properties.

Read-side outline является деревом `RichTextDocumentOutlineNode`.

Сейчас в table-of-contents properties включаются только уровни H1-H3. UI может показывать настроенное подмножество этих уровней, сейчас от 1 до 3.

Загрузка outline должна быть независимой от загрузки начальных чанков.

В отличие от тела документа, outline должен быть догружен по всей выбранной версии документа, потому что от него зависят:

- навигация по содержанию;
- semantic jump при отпускании scrollbar;
- переходы в read/edit viewport.

Загрузка outline выполняется итеративно, асинхронными батчами. Она не должна блокировать первичный render текста.

Если outline еще не загружен, тело документа все равно должно быть доступно для чтения.

При смене версии документа outline перечитывается заново для выбранной версии.

---

## 9. Read model viewport

Rich-text viewport является виртуализированным окном чанков.

Он рендерит:

- top spacer
- текущие загруженные чанки
- bottom spacer

Spacers представляют незагруженные диапазоны документа. Это позволяет browser scrollbar приблизительно отражать полную длину документа, пока DOM содержит только ограниченный набор реальных чанков.

Состояние viewport хранится в:

```text
LoadedChunks
TotalChunkCount
TopSpacerPx
BottomSpacerPx
EstimatedChunkHeight
```

`LoadedChunks` является source of truth для того, что сейчас отображается как реальный HTML. Любое изменение `LoadedChunks` прокидывается на страницу через обычный Blazor render.

---

## 10. Политика LoadedChunks

`LoadedChunks` содержит реальные чанки, которые сейчас отрендерены во viewport.

При adjacent scroll loading новые чанки merge-ятся в уже загруженный набор. Это не дает уже видимому тексту исчезать, когда пользователь пересекает границу чанка.

При навигации по содержанию или дальнем jump viewport заменяет загруженный набор новым окном вокруг target chunk. Это предотвращает неограниченный рост DOM при прямой навигации.

Политика merge:

- merge, если requested window соседствует с текущим loaded window
- replace, если requested window является дальним jump

Дубликаты чанков разрешаются по `SortOrder`; последняя загруженная версия побеждает.

При versioned-read storage сначала выбирает chunk rows с `Version <= ViewedVersion`, затем оставляет последнюю запись по logical chunk `Id`, и только после этого упорядочивает результат по `SortOrder`.

---

## 11. Read cache чанков

Перед чтением из storage viewport проверяет, есть ли запрошенные чанки уже в `LoadedChunks`.

Если все запрошенные чанки уже загружены, чтение из БД не нужно.

Если отсутствует только часть requested range, нужно читать только отсутствующие contiguous ranges.

Это предотвращает лишние чтения из БД, когда пользователь скроллит обратно в диапазон, который все еще отображается.

---

## 12. Скролл вниз

При обычном скролле вниз viewport оценивает target chunk по scroll offset и estimated chunk height.

Если estimated chunk уже загружен, чтение не выполняется.

Если estimated chunk не загружен, viewport грузит настроенное scroll window вокруг target.

При обычном adjacent scrolling новое окно merge-ится в `LoadedChunks`.

---

## 13. Скролл вверх

При скролле вверх viewport проверяет, приближается ли scroll position к верхней границе первого загруженного чанка.

Если пользователь достигает этой границы и предыдущие чанки существуют, viewport загружает предыдущее окно и merge-ит его в `LoadedChunks`.

Количество предыдущих чанков, которые удерживаются или подгружаются при скролле, управляется rich-document settings.

Это поведение должно работать и после прямой навигации по содержанию, и после обычного скролла.

---

## 14. PageUp и PageDown

`PageUp` и `PageDown` трактуются как обычный viewport scrolling.

Они должны запускать те же проверки границ и загрузку chunk-window, что mouse wheel или trackpad scrolling.

Они не должны использовать table-of-contents jump semantics.

---

## 15. Перетаскивание scrollbar

Перетаскивание rich-document scrollbar имеет специальную семантику.

Пока пользователь удерживает и тащит scrollbar thumb, viewport не должен читать чанки.

При отпускании мыши viewport:

1. читает финальную позицию scrollbar
2. оценивает приблизительную позицию в документе
3. мапит эту позицию на примерный chunk `SortOrder`
4. находит ближайший table-of-contents node
5. загружает этот node так, как если бы пользователь кликнул по outline item

Это предотвращает повторяющиеся чтения во время быстрого движения scrollbar и дает scrollbar семантику навигации по всему документу.

Если содержание недоступно, viewport может загрузить окно вокруг estimated chunk.

---

## 16. Навигация по содержанию

Клик по outline item переводит viewport к стабильному heading anchor.

Если target chunk уже загружен, viewport скроллит к anchor в текущем DOM.

Если target chunk не загружен, viewport загружает настроенное окно вокруг target chunk, затем после render скроллит к heading anchor.

Настроенное table-of-contents window включает:

- чанки перед target
- target chunk
- чанки после target

Before/after count задаются системными настройками.

---

## 17. Настройки

Read behavior rich-документа управляется системными параметрами.

Текущие read-side настройки включают:

- размер rich-text chunk
- количество начальных чанков при открытии документа
- table-of-contents before buffer
- table-of-contents after buffer
- scroll previous chunk count
- видимость scrollbar содержания

Настройки читаются через `RichTextDocumentSettingsService`.

Storage provider не должен содержать rich-document domain decisions. Доменное поведение чтения принадлежит rich-document services и components.

---

## 18. Логирование

Чтение чанков логируется в web logger со специальным tag:

```text
[rich-doc-chunk-read]
```

Состояние loaded chunks может логироваться с tag:

```text
[rich-doc-loaded-chunks]
```

Diagnostic logs должны использовать стабильные tags, чтобы их можно было фильтровать в web logger.

Diagnostic tags не должны содержать динамические chunk values как отдельные logger tags. Динамические значения должны быть частью message text.

---

## 19. Пересоздание содержания

Содержание обновляется явно или как часть операций записи chunks.

Оно должно быть актуально после:

- после import
- после сохранения измененных chunks
- после нажатия кнопки обновления/перечитывания table-of-contents

Открытие документа не должно пересоздавать содержание.

Открытие документа только читает сохраненные table-of-contents properties.

Для больших документов запрещено пересоздавать содержание путем чтения всего документа в память. Допустима только итеративная обработка chunk windows / chunk batches выбранной версии.

---

## 20. Import policy

Import считается правкой rich-text документа.

Import append-ит chunks и создает новую версию документа:

```text
targetVersion = currentLatestVersion + 1
```

Все chunks, полученные импортом, сохраняются с `Version = targetVersion`.
Manifest документа сохраняется как новая `BusinessEntityDataDto` версия с тем же `targetVersion`.

Для нового пустого rich-document стартовый пустой chunk может быть удален, чтобы импорт не создавал пустую строку сверху, но сам импорт все равно остается новой версией.

Import создает table-of-contents properties для импортированных chunks.

Нарезка чанков управляется настройками размера.

Read-side предполагает, что imported chunks уже имеют:

- стабильный `SortOrder`
- отрендеренный `HtmlCache`
- table-of-contents properties, если внутри есть headings

Если в chunk нет H1-H3 headings, table-of-contents property не требуется.

После успешного импорта UI должен перечитать документ тем же flow, что и при открытии:

1. сбросить просматриваемую версию на latest;
2. прочитать shell и версии;
3. загрузить начальное окно chunks новой версии;
4. итеративно догрузить полный outline новой версии.

---

## 21. Границы ответственности

Rich-document logic принадлежит rich-document layer.

Data provider может предоставлять generic storage operations и converters, но он не должен владеть rich-document read policy.

Правильное разделение ответственности:

- data provider: хранит и читает DTO
- converters: переводят persisted DTO payloads
- rich-document helper: rich-document domain operations
- rich-document viewport: UI windowing и navigation
- rich-document outline: table-of-contents UI

Это сохраняет storage layer переиспользуемым и не дает domain-specific behavior протекать в generic infrastructure.

---

## 22. Политика ошибок и cancellation

Открытие документа может запускать несколько асинхронных чтений.

Если пользователь перешел к другому документу до завершения этих чтений, старые чтения не должны перетирать состояние новой страницы.

Страница использует:

- cancellation tokens
- load version

Каждый asynchronous result должен проверять, что он все еще относится к активному документу, перед применением UI state.

---

## 23. Текущие tradeoffs

Текущая модель ограничивает initial DOM size и избегает full-document reads при обычном открытии.

Однако adjacent scrolling пока может увеличивать DOM со временем, потому что merged chunks остаются видимыми. Для текущей фазы реализации это допустимо.

В будущем более строгая virtualization model может вытеснять дальние чанки и держать только visible range plus buffer.

---

## 24. Non-goals

Текущая read policy не требует:

- загружать весь rich document в DOM
- читать все chunks документа при открытии
- пересоздавать содержание при каждом открытии
- парсить browser DOM для поиска headings
- трактовать chunks как business entities
- помещать rich-document navigation rules в data provider

Этих подходов нужно избегать, если policy не пересматривается намеренно.

---

## 25. Принципиальные подходы к решению задач

Этот раздел фиксирует предпочтительные инженерные подходы для повторяющихся задач чтения rich-документа.

### 25.1. Открытие большого документа

Проблема:

- пользователь должен быстро увидеть документ
- документ может содержать много чанков
- содержание может быть большим

Принципиальный подход:

- синхронно читать только shell
- render page frame сразу после получения shell
- загружать initial chunk window асинхронно
- загружать table of contents асинхронно
- загружать table of contents по всей выбранной версии итеративными батчами
- держать отдельные loading states для body и outline

Избегать:

- ожидания всех чанков перед render
- ожидания содержания перед показом первого текста
- ограничения outline только начальными chunks
- одного global loading flag для всех этапов чтения

### 25.2. Чтение тела документа

Проблема:

- тело документа может быть слишком большим для одного DOM render
- пользователю обычно нужен только локальный диапазон

Принципиальный подход:

- читать chunks ordered windows
- рендерить только loaded chunks как real DOM
- представлять unloaded ranges через spacers
- оценивать total scroll height по chunk count и measured chunk heights

Избегать:

- склейки всех chunk HTML в одну огромную строку
- загрузки всех chunks при открытии
- scrollbar, который отражает только текущее loaded window

### 25.3. Навигация по содержанию

Проблема:

- пользователь кликает по смысловому heading, а не по raw chunk number
- target chunk может быть не загружен

Принципиальный подход:

- хранить stable heading anchors в chunk properties
- резолвить clicked outline node в `ChunkSortOrder` и anchor
- если chunk загружен, скроллить к anchor
- если chunk не загружен, загрузить configured window вокруг него, затем scroll after render

Избегать:

- reparsing всего document HTML для поиска headings
- загрузки всего документа ради anchor navigation
- visual text matching вместо stable anchors

### 25.4. Скролл вниз и вверх

Проблема:

- пользователь постепенно пересекает chunk boundaries
- контент не должен исчезать на границе

Принципиальный подход:

- определять приближение к границе loaded range
- читать только missing adjacent window
- merge adjacent windows в `LoadedChunks`
- переиспользовать chunks, которые уже есть в `LoadedChunks`

Избегать:

- замены всего loaded range при adjacent scrolling
- повторного чтения chunks, которые уже загружены
- отдельной навигационной модели для скролла вверх

### 25.5. Перетаскивание scrollbar thumb

Проблема:

- scrollbar dragging может генерировать много scroll events
- чтение во время drag вызывает повторные чтения БД и flicker

Принципиальный подход:

- подавлять chunk reads, пока мышь удерживает scrollbar thumb
- ждать mouse release
- оценивать final document position
- мапить позицию на ближайший outline node, если возможно
- грузить эту точку через table-of-contents jump semantics

Избегать:

- чтения chunks непрерывно во время thumb drag
- трактовки каждого промежуточного scroll event как real navigation intent
- merge каждого drag-produced window в текущий DOM

### 25.6. Пересоздание содержания

Проблема:

- rebuild сканирует chunks и пишет technical properties
- это тяжелее, чем чтение persisted outline data

Принципиальный подход:

- rebuild только по explicit events
- после import создавать/обновлять table-of-contents properties для импортированных chunks
- после import перечитывать outline по всей новой версии
- выполнять rebuild по кнопке пользователя
- при открытии документа только читать persisted properties

Избегать:

- rebuild при каждом open
- rebuild как side effect чтения
- скрытия rebuild cost внутри generic data-provider reads
- full-document read ради rebuild или outline load

### 25.7. Выбор merge или replace

Проблема:

- adjacent scroll должен ощущаться непрерывным
- far jumps не должны без необходимости раздувать DOM

Принципиальный подход:

- merge, когда новое window соприкасается или пересекается с existing loaded window
- replace, когда новое window является far jump
- использовать replace для table-of-contents jumps и scrollbar-release jumps

Избегать:

- always replace, потому что это вызывает видимое исчезновение текста около границ
- always merge, потому что после многих jumps DOM может сильно вырасти

### 25.8. Размещение domain behavior

Проблема:

- chunk storage является generic infrastructure
- rich-document reading является domain-specific behavior

Принципиальный подход:

- держать DTO persistence в data provider
- держать rich-document decisions в `RichTextDocumentReader`, Search/Outline/Edit services владельца и его UI components
- ограничить converters переводом storage payload

Избегать:

- table-of-contents navigation policy в data provider
- storage APIs, которые понимают UI scroll behavior
- coupling generic repository code с rich-document semantics

### 25.9. Диагностика

Проблема:

- chunked reading сложно понимать без наблюдаемости
- logs могут стать шумными и тяжелыми для фильтрации

Принципиальный подход:

- логировать реальные chunk reads со stable tag
- логировать loaded-window state отдельным stable tag
- держать dynamic values в message text, а не в logger tags
- уменьшать или удалять diagnostic logging, когда оно начинает мешать normal analysis

Избегать:

- dynamic tags для chunk ids или chunk sizes
- логирования каждой внутренней UI estimate как отдельного tag
- смешивания read diagnostics с unrelated application logs
