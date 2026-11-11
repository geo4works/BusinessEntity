# External API v1: клиент и эксплуатация

Реализован пакет AUTH_001 / API_001 / API_002. Источник точных DTO и обязательных
полей — `/swagger/api-v1/swagger.json`; [58 capabilities](Registry/api-capabilities.md).
Приложение — страницы, без вкладочной модели и глобального Save.

## Доступ

Администратор в `/administration/identity-providers` настраивает стандартный OIDC
профиль: UI code+PKCE отдельно от API resource с точными issuer/discovery/audience,
allowed clients/client claim/scope. Authentik — один management adapter; профиль
может указывать на собственный stack или на существующую внешнюю установку.
Переключение выполняется планом, provisioning journal и доказанным входом admin.
Локальные user IDs/авторство/роли сохраняются; username/email сами по себе не
связывают аккаунты. Пароли пользователей не копируются между провайдерами.

Для интеграции нужна доказанная Service identity, локальная роль/права на данные
и отдельный grant в `/administration/api-integrations`: client, capabilities,
AllSpaces либо явные SpaceIds. OAuth scope не заменяет grant или локальные права.
В тестовом Authentik client_credentials использует username служебного аккаунта
и его app password на `/application/o/token/`, client_id и scope `be.api`.
Это отдельный service credential; человеческий пароль не отправляется BusinessEntity.
Provisioning Authentik выдаёт отдельный неистекающий app password; access token
при этом имеет короткий срок IdP. Credential можно явно перевыпустить/отозвать.
Management account должен иметь право создавать token именно целевого пользователя;
адаптер проверяет возвращённого владельца и intent, а не полагается на HTTP 201.
Другой IdP выдаёт access token по своему настроенному стандартному OAuth flow.

UI cookie, ID token интерфейса и refresh token не являются API access credential.
Проверяются точные issuer/audience/client/scope/подпись/срок и локальная привязка.

```http
POST /api/v1/sessions
Authorization: Bearer <access-token>
Content-Type: application/json

{}
```

Ответ содержит `id` security session. Допустимо сузить исходный grant:
`{"capabilities":["documents.read"],"spaces":{"allSpaces":false,"spaceIds":["<id>"]}}`.
Каждый последующий запрос требует одновременно Bearer и `X-BE-Session-Id`.
SessionId не является самостоятельным credential и не хранит UI draft/SpaceId.
Срок по умолчанию60мин абсолютный. Grant revision/отключение/local user/IdP epoch
проверяются заново. Новый access token того же user/client может обслуживать ту
же ещё действующую session; смена epoch требует новой session.

`GET /me`, `/sessions/current`, `/capabilities`; `DELETE /sessions/current` закрывает
свою сессию, повтор закрытия идемпотентен. Принятый commit этим не откатывается.

## Запись и повторы

Для каждого изменяющего owner command передавайте новый для нового намерения `Idempotency-Key`
(ASCII `A-Z a-z 0-9 _ . : -`,1–128). При потере ответа повторяйте тот же known
request с тем же ключом. Пространство ключа: LocalUser/client/capability, не HTTP
request/session ID. Технический Context и bearer не входят в semantic hash.
Гарантированное хранение receipt/outcome —7суток.

Canonical ответ — `operationId,entityId,version,createdVersion,changedEntities,
deletedEntities`. Он сохранён той же SQL transaction, что граф/файлы/история.
Тот же ключ с другим содержимым —409. `503 operation.unknown` содержит operationId
и statusUrl: прочитать `GET /operations/{id}`, а не считать это rollback.
Чужой subject/client не получает outcome; нужны также исходный capability и space.
Bodyless DELETE поддерживается; изменяющий owner DELETE тоже требует ключ.
Открытие/закрытие framework session не требуют operation key.

## Space, дерево, обычный документ

- `POST /spaces`: `{"name":"Knowledge","doBackup":false}`; GlobalAdmin/AllSpaces.
- `GET /spaces`, `/spaces/{id}`; `PATCH /spaces/{id}` меняет name/isPublic.
- `GET /spaces/{id}/tree` и folder endpoints: точные URI в каталоге.
- `POST /spaces/{id}/documents`: `{"title":"Note","content":"Text","parentId":null}`.
  Заметка — этот же Document. `PUT /documents/{id}` требует baseVersion/title/content.
- `POST /spaces/{id}/rich-documents`: `{"title":"Article","blocks":[{"kind":"paragraph","html":"Text"}]}`.

Внешний API не выбирает browser space. Parent/entity должны принадлежать явному
разрешённому space; перенос между пространствами даёт409. Списки/cursors/counts
фильтруются владельцем по актуальным правам. Общий limit1–100, cursors защищены,
привязаны к user/client/query; chunk cursor дополнительно закреплён за версией.

Удаление Space/поддерева предваряется `POST /.../deletion-plan`. Полученный planId
передаётся в query при DELETE Space; для дерева используется body команды
`POST /tree/delete`. План5мин, до10000entities; состав/зависимости повторно
проверяются writer. Изменённый план или защищённая ссылка —409, не SQL bypass.
Удаление target закрывает его ещё не использованные uploads/jobs; staging GC
не удаляет корень storage или Docker volume.

## Полная версия статьи

`GET /rich-documents/{id}` — metadata; `/content?version=...&limit=...` — chunk window.
Published-only субъект видит выбранный опубликованный snapshot/его заголовок,
не draft/history/private data. `/versions`, `/versions/{version}` требуют history.

`POST /rich-documents/{id}/revisions`:

```json
{"baseVersion":1,"title":"Article","chunkOrder":["<existing-chunk-id>","new:second"],"changes":[{"id":"new:second","blocks":[{"kind":"paragraph","html":"New text"}]}]}
```

ChunkOrder — весь порядок автора, Changes — только его изменённые/new chunks.
Existing ID должен входить в BaseVersion, `new:key` даёт stable server ID.
Другой latest не примешивается к полной версии автора. Первый Save создаётv1;
контент/название дают новую версию, точный no-op её не создаёт. Restore через
`POST /restore-version {"version":N}` создаёт новый полный snapshot с текущим автором.
`PUT /publication {"version":N}` только выбирает существующую версию;
`PATCH /versions/N/description {"description":"..."}` меняет её metadata.

Block schema1: paragraph/heading/list/code/table/image/video. HTML bounded и
санитизируется; scripts/SVG/remote img/data URI не превращаются в серверный fetch.
Ссылка image принадлежит статье, video — разрешённому MediaVideo. Internal EF,
viewport, авторы, номера новых версий и storage paths не принимаются от клиента.

## Возобновляемая загрузка

`POST /uploads`:

```json
{"purpose":"rich.book","spaceId":"<space>","targetId":"<parent-or-article>","length":12345,"fileName":"book.md","contentType":"text/markdown","sha256":"<optional-whole-sha256-hex>"}
```

Назначения: `rich.image` (существующая статья), `rich.book` (Space/Folder/статья),
`media.video` (Space). Их owner объявляет типы/формат/право и RequiredCapability.
Статус содержит upload ID, partSize4MiB, принятые номера/digests, expiry/state.
PUT `/uploads/{id}/parts/{number}`: numbering0-based, body application/octet-stream,
обязательные Content-Length и X-Part-SHA256. Полная часть4MiB, последняя — остаток.
Ложный digest/другая повторная часть не продвигает прогресс; повторяет key/bytes.
Части доступны для записи до sealing Ready. GET status позволяет продолжить
после обрыва/перезапуска, не загружая уже принятые parts повторно.

`POST /uploads/{id}/complete` проверяет полный digest/длину и делает Ready.
Большое assembly может дать503unknown при HTTP timeout: смотреть operation/status,
не создавать новую загрузку автоматически. `DELETE /uploads/{id}` отменяет свой
неиспользованный input; при job reservation сначала отменить job. Отмена собственного
input не требует сохранения прежнего права редактировать уже удалённый target.
Части не создают статью/версию. DataProvider владеет техническими файлами; Stream,
массива размера файла или клиентского server path в bus message нет.

## Изображение, книга, видео

Картинка: Ready `rich.image`, затем `POST /rich-documents/{id}/images`:
`{"readyUploadId":"...","baseVersion":1,"altText":"..."}`. Owner вставляет chunk
и immutable file одной версией/transaction; итоговый attachmentId — в content.
Удаление блока новой revision сохраняет файл для старых версий.

Книга: `POST /rich-documents/imports/{targetId}`:
`{"readyUploadId":"...","mode":"create","title":"Book","isPublic":true}`.
Create target — Space/Folder. Replace/append target — статья, обязательна baseVersion;
isPublic при этих режимах меняется отдельным metadata endpoint. UTF8/UTF16 BOM,
TXT/Markdown/HTML в одну статью. Большие блоки нормализуются bounded fragments;
HTML tag до8192символов/depth64. Assets загружаются отдельно, импорт не скачивает
remote/inline-data картинки. Размер технических chunks использует глобальный target.

Видео: `POST /spaces/{id}/media-videos` с readyUploadId/displayName/comment.
Хранится исходный файл: никакого фиктивного transcoding/processing. Duration null,
если она не извлекалась. GET/list/PATCH metadata/DELETE — у Media owner.
Space association не означает автоматическое уничтожение общей медиатеки при
удалении Space; требуемые MediaVideo удаляются явным owner DELETE. Сохранённые
article-version references защищают используемое видео от удаления.

Оба создания возвращают202 только после durable регистрации:
`jobId,operationId,statusUrl`. `GET /jobs/{id}`: Queued/Running/Succeeded/Failed/
Cancelled/NeedsReauthorization. Прогресс — фактически прочитанный input/high-water
mark; Running не обещает готовую статью или процент transcoding.
Одновременно одна heavy операция, включая upload assembly.

`DELETE /jobs/{id}`: queued cancel либо запрос остановки running; если commit уже
принят, ответ Succeeded с outcome. Смена IdP/epoch, закрытая/истёкшая session,
отзыв допуска не дают job System authority. `POST /jobs/{id}/reauthorize` под
действующим Bearer/session того же LocalUser/client явно разрешает продолжение;
input/base/operation/generated IDs сохраняются. Исходный access expiry также
проверяется перед непринятым commit. Source upload живёт24ч; после её expiry
требуется новый input/job. При смене IdP client ownership не передаётся другой
интеграции автоматически, даже если LocalUser совпадает.

## Скачивание и настройки

GET `/rich-documents/{id}/images/{fileId}/original?version=N` проверяет Read и
членство файла в разрешённой версии. GET `/media-videos/{id}/file` проверяет
права/space; оба отдают bytes/Range206 напрямую с Bearer/session, без redirect
на старый public UI route. Старая группа UI URL рассматривается отдельно
в TASK_SECURITY_001; новый API не меняет её статус.

System settings GET/PUT `/settings/system`: явные CompanyName, chunk target,
read/editor windows, TOC buffers/scrollbar. GlobalAdmin и AllSpaces. Ни secret,
JWT key, connection string, storage/server path, роли, users, IdP-management сюда
не входят. Space: GET/PATCH `/spaces/{id}/settings` — backup flag/interval.
Self: GET/PUT `/me/preferences/printing` (named print presets) и
`/me/preferences/rich-documents/{id}` (displayLevelCount1–3). Нет поля UserId;
настройка чтения не меняет историю статьи, document preference требует Read/space.

## Пределы и ошибки

JSON2MiB, part4MiB, image20MiB (PNG/JPEG/GIF/WebP), book100MiB, video1GiB.
Четыре неиспользованных inputs/2GiB на subject/client; normalized staging также
bounded. API по умолчанию120calls/min и16одновременных;429 содержит Retry-After.
Эти transport limits не расширяют domain permissions. Missing/unknown/null
обязательных DTO fields —400, unauthenticated401, forbidden403, unknown404,
conflict409, oversize413, unsupported415;503unknown требует reconciliation.
Ошибки содержат code/requestId и при известной операции operationId/statusUrl,
не SQL/stack/credentials. User/API/worker имеют разные scopes на одной шине.

[Журнал проверок и измерений](Tasks/TASK_AUTH_API_execution-log.md): реальный
100MiB text и1GiB original transport, restart/reauthorization, PostgreSQL rollback/
after-commit, UI/API сценарии. Коммит/push этого пакета без отдельной команды нет.
