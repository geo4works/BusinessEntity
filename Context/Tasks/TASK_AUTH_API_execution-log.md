# Раскатка AUTH_001 → API_001 → API_002

Начало: 2026-10-29. Пользователь поручил реализовать весь подготовленный комплект.
Исходная точка: main, cda95ab / 0.17.0. Три файла ТЗ находятся вне индекса;
других пользовательских правок при начале нет. Команда на новый коммит/push не дана.


## Итог, 30.10.2026

**AUTH_001, API_001 и все этапы 0–6 API_002 завершены и проверены.**
Финальный Release build: 0 ошибок. Реальный PostgreSQL: **202 passed, 0 failed,
0 skipped** (`BusinessEntity.Tests/TestResults/api-service-final.trx`). RSTT
app/logger и сквозные API/UI/provider smoke выполнены; подробности этапа6 ниже.
Версия/HEAD не менялись: main, cda95ab / 0.17.0. Коммит/push не поручены.

## Зафиксированные решения

- Временные пароли индивидуальные, обязательная смена при первом входе.
- Первая установка на отдельной пустой БД и отдельном storage, исходные сохраняются.
- После первой установки ID/авторство/права сохраняются при смене провайдера.
- Заметка — существующий Document (ответ пользователя при старте раскатки).
- Импорт: TXT/Markdown/HTML, одна большая статья. Предел книги 100 MiB, видео 1 GiB; одна тяжёлая обработка одновременно (подтверждено пользователем).

## AUTH, этап 0 — аудит и проектирование

Инвентарь ТЗ соответствует cda95ab. Дополнительно найдено: UserValueRules.IsAccessAdmin
даёт полномочия по одному имени admin; это также удаляется при переходе на локальные
назначения. Существующая UsersAdministration содержит vendor-specific поля и тексты;
нейтрализация нужна не только в протокольном слое. Текущий импорт целиком читает bytes
в память; полноценная потоковая обработка относится к API_002.

Docker работает: текущие приложение/logger, Authentik и обе БД здоровы; исходные
контейнеры/данные не сбрасывались. Полный исходный тестовый прогон на отдельном
be-arch-test-db: 126 passed, 0 failed, 0 skipped; exit 0.

Проектируем целевые User identity/grants/sessions и общие технические записи
DataProvider до первого ввода новой БД. Отдельный транспорт не создаётся.

## Фактическое выполнение

AUTH_001 и API_001 реализованы/проверены; API_002 выполняется. Журнал хранит историю,
поэтому промежуточные незавершённые статусы ниже заменяются последующими отчётами;
наличие исходников/незавершённой правки не означает выполненный этап.


### AUTH: реализация в работе

Добавлены Infrastructure/Identity (контракты, строгий OIDC, именованные схемы,
secret references, admission/drain), отдельные User identity/profile revision/link/
plan/grant/session models. Старый AuthentikSessionManager/password-flow и повторный
bootstrap фиксированных паролей удалены. Локальные права больше не выдаются по
имени admin/akadmin. Добавлены страница профилей, подготовка/provisioning/probe/
активация и стандартный SaveContext формы. Это ещё не принятый этап.

Промежуточная сборка прошла; 77 проверок без SQL прошли. Первый полный прогон
нового кода: 128 passed, 1 failed, 0 skipped — выявлена гонка первого входа между
чтением link и проверкой занятого login. Добавлено повторное чтение строго по
проверенным iss/sub; повторный прогон начат, результат ещё не засчитан.

Docker Desktop оказался выключен. После запуска восстановлены только собственные
postgres-production-db, be-arch-test-db, logger и прежний app image. Новая сборка
ещё не развёрнута, новая рабочая БД/storage ещё не созданы. Текущие данные не очищались.

API_001/API_002 ещё не реализованы. Версия остаётся 0.17.0; коммиты/push не выполнялись.


### Промежуточная проверка AUTH

Полный повторный прогон: 129 passed, 0 failed, 0 skipped (включая новые SQL identity
и admission/provisioning проверки). Созданы только отдельные be-identity fixture
flow/providers в Authentik A и новая пустая business_entity_identity; старые OAuth
registrations, пользователи, исходная БД и canonical storage не перезаписывались.
Новые credentials записаны в игнорируемую .env; её копия до правки сохранена в
пользовательском TEMP. Подготовлен отдельный состав Authentik B с собственными
БД/ключами; новая app сборка пока ещё не развёрнута.

### AUTH: RSTT и текущая проверка протокола

Последующие изменения: offline `identity-recover <ProviderId> <Revision>` с новым
AuthEpoch и аудитом; исключительная session advisory lock БД не допускает её
параллельно с сервером. Новый реальный SQL тест обнаружил удержание advisory lock
через connection pool; выделенное соединение теперь без pooling. Также тест
зависимостей выявил 7 зависимостей recovery; проверка целевого администратора
выделена в отдельный сервис, предел 6 сохранён.

Повторная Release сборка: 0 ошибок. Полный `auth-recovery2.trx`: 132 passed,
0 failed, 0 skipped. Первый RSTT обоих контейнеров завершён; image IDs совпали
с собранными. Новая сборка запущена на business_entity_identity и отдельном
BusinessEntityIdentityStorage; старые БД/storage не очищались.
Реальный браузерный OIDC вход Authentik A → callback → /authinfo успешен:
локальный пользователь материализован, одноразовый pinned bootstrap дал GlobalAdmin.

Начат A→B→A smoke. Первый прогон остановился до создания B-профиля: HTTP middleware
требовал выбранное пространство для глобальной страницы identity. Добавлен bypass
этой страницы; также перед активацией добавлена повторная live-проверка цели.
Эти последние изменения требуют повторных проверок; этап AUTH ещё не принят.
Ни каркас API, ни предметный CRUD ещё не реализованы. Коммиты/push не выполнялись.

### Проверка на момент запроса статуса

Последняя Release сборка — 0 ошибок; `auth-activation.trx` — 132 passed,
0 failed, 0 skipped. Второй RSTT обоих контейнеров завершён. Профиль B сохранён
через UI без активации, проверен discovery и management, подготовлен план.
Источник A остаётся активным, epoch 1. Полная приёмка AUTH не пройдена.

A→B→A smoke остановился при provisioning: реальный POST core/users/ адаптера
вернул HTTP 400, поэтому план находится в NeedsReconciliation, status Creating.
Нужно проверить передачу JSON body .NET HttpClient/JsonContent через back-channel
(в частности Content-Length/chunked); это гипотеза, причина ещё не подтверждена.
При диагностике тот же create-запрос из Node с маркером исходной операции
успешно создал только новую B-учётку admin, без установки пароля. Исходный маркер
сохранён в attributes, поэтому повтор исходного плана должен сверить его и
продолжить, а не строить новый конфликтующий план. Чужие A-пользователи/пароли
не изменялись. Временные пароли в файл/журнал не сохранялись.

B ProviderId: 76e4b460-92f5-47af-b196-dc57ae9a04a2. Актуальный fixture ID находится
также в игнорируемой .env и TEMP metadata. Сценарий Smoke/identity-provider-smoke.cjs
пока требует доработки для продолжения уже созданного NeedsReconciliation плана.
Дополнительно проверить probe callback: стандартный OIDC request handler исполняется
до обычного cookie middleware; при probe может понадобиться явная проверка исходной
cookie через AuthenticateAsync перед обращением к UserSecurityAuthority.

Не завершены: provisioning/probe/активация A→B→A, generic OIDC fixture, проверка
операторского recovery, обновление всей deployment/user документации. API_001 и
API_002 ещё не реализованы. Версия 0.17.0, HEAD cda95ab, новых коммитов/push нет.

### AUTH: завершение основных сценариев, 2026-10-29

Реальный management provisioning с буферизованным JSON продолжил исходный план;
новая B-учётка получила индивидуальный временный пароль. Первый probe потребовал
смены этого пароля до получения результата. A→B сохранил локальный UserId/права.
Возврат B→A выполнен через новую проверку/активацию. Полный повторный браузерный
сценарий завершился exit 0: login, сохранение без активации, использование проверенной
B-привязки, probe, активация, отказ записи старого circuit без его аварии, отказ старой
cookie, возврат A. Неиспользованный дублированный QA-профиль удалён через собственный UI.

Новый generic OIDC fixture на реальных HTTP endpoints проверяет code+PKCE/JWKS/RS256.
6 вариантов прошли: valid, неверные audience/issuer/signature/expiry/nonce.
Nonce-тест обнаружил вызов JIT в TokenValidated до полной protocol validation;
JIT/probe перенесены в TicketReceived. Исправлена явная проверка source cookie на probe.
Полный auth-circuit.trx: 138 passed, 0 failed, 0 skipped. Release сборка — 0 ошибок.
RSTT обоих контейнеров выполнен, браузерный A→B→A smoke прошёл на этой сборке.

Recovery при работающем сервере отклонён host lease (ожидаемый nonzero).
При остановленном сервере реальная CLI команда завершилась exit 0: выбран A revision1,
новый epoch8, OperatorRecovery и outcome сохранены. Сервер запущен снова.
Старые БД/storage/volumes не удалялись. Новые secrets не выводились в журнал.

Добавлены claims mapping, наличие secret references без выдачи значений,
удаление неиспользованного/отключение использованного профиля, first-bootstrap validation.
Обновлены User политики, deployment metadata/secret templates и installer: fixed
паролей/startup external-user reset больше нет. External compose config проверен:
только postgres-production-db, business-entity, web_logger; зависимости на local IdP нет.
PowerShell syntax проверен до последующих мелких правок; итоговую проверку повторить.

API_001/API_002 ещё не реализованы; дальнейшая работа — Bearer/service identity,
локальные допуски, API session и типизированные toolset bindings. Коммит/push не выполнялись.

### API_001: реализация security/session/toolsets в работе

Добавлены Dynamic JwtBearer API resource schemes (точные issuer/audience/client/scope,
без cookie fallback), User API identity linking по доказанному sub_mode user_uuid,
ApiAccessGrant UI, own security sessions и отдельный Api execution scope.
ExternalApiMiniApp публикует session/me/catalog; me исполняется typed Request/Response
UserMiniApp на существующей шине. GiveMeApiToolsets/HereAreApiToolsets фиксируют каталог
на старте. Добавлены JSON problems, OpenAPI API-v1, лимиты запросов/параллелизма/JSON.

Реальный M2M app-password token Authentik A получен для отдельного be-api-smoke;
issuer/audience/client/scope соответствуют API profile. Credential сохранён только
в игнорируемой .env; никакие Human пароли не менялись. В новую app сборку этот API
пока не развёрнут; права API не выдавались через SQL.

Первый полный прогон: 143 passed, 1 failed, 0 skipped. Проверка абсолютного срока
сессии использовала SQL now(), в то время как срок выдаёт app clock; фикстура
переведена на явный app timestamp. Повтор и итоговая приёмка ещё не выполнены.
Не завершены: HTTP/auth/session smoke, technical receipts/idempotency, операции,
service provisioning при смене IdP, документация API. API_002 ещё не реализован.


### AUTH_001 / API_001: завершение проверенного каркаса

Release сборка — 0 ошибок. Последний полный реальный PostgreSQL прогон:
`api-http.trx`: **169 passed, 0 failed, 0 skipped**. Включены 6 generic OIDC
code+PKCE случаев, 11 настоящих signed JWT/HTTP discovery/JWKS случаев API
(valid/issuer/audience/signature/expiry/scope/client/missing-client/UI ID token/
unsigned/HMAC), 8 startup catalog/immutability случаев, SQL session/receipt tests.
Сквозной тестовый HTTP owner → bus → настоящий DataProvider/SQL проверил потерю
ответа после commit, новое приложение/новую session, 8 параллельных retry,
конфликт иного тела, запрет неизвестных authority полей/отсутствующего ключа/
слишком большого тела. Тестовых endpoints в production нет.

RSTT business-entity + web_logger выполнен на финальном production исходнике
каркаса. `api-framework-smoke.cjs` exit0: реальный Authentik A M2M, owner-proof
service linking, локальный grant через UI, session/me/catalog, 8 запросов,
Bearer/session binding, JSON отказ без redirect, forged authority400, OpenAPI,
отзыв допуска и идемпотентный close. Начальный отдельный прогон также проверил
валидный JWT без локального grant403.

`api-provider-switch-smoke.cjs` дважды exit0. Первый прогон создал недостающий
B service account/app password штатным планом, не через SQL/совпадение имени.
A→B→A сохранил local Service UserId; нужны явные grants разных client IDs A/B.
Старые token/session отвергаются, старые epoch не оживают при возврате. При B
остановлены только собственные локальные A server/worker, приложение перезапущено:
B identity и ранее открытая durable API session работали. A контейнеры возвращены,
активен A. Старые БД/storage не удалялись. Credentials остались только в памяти
и игнорируемой QA .env, не в журнале/SQL/bus.

`identity-provider-smoke.cjs` повторно exit0 на финальном каркасе: обычный UI вход,
профили/probe/A→B→A, неизменный local admin ID, stale circuit write/cookie denied.
Изначальный первый B provisioning действительно проверял обязательную смену
индивидуального временного human пароля; повтор использует уже доказанную link.
Убраны последние admin-name bypass в Tree и SampleData, повторён полный тестовый прогон.

Deployment и изменённые Initialize-AuthentikEnv/Build-ReleaseBundle PowerShell
синтаксически проверены: 0 ошибок. Полный поиск также нашёл ошибки в неизменённом
старом operator menu `Powershell/СontainerManagement.ps1` (6 при UTF8 ParseInput,
27 при native Windows PowerShell ParseFile); production installer его не использует.
Это не засчитывается как успешная проверка всех PowerShell файлов.

Добавлены инструкция API клиента, каталог capabilities, policy/реестры/индекс,
публичный типизированный статус операции. Pending/Unknown не означают rollback;
public result и operation outcome находятся в одной SQL транзакции, retention7дней.
На момент приёмки API_001 production toolset только `user.self.read`, остальные
routes — sessions/catalog/operations. Переходим к API_002, без сброса новой БД.
Версия/HEAD прежние (0.17.0/cda95ab); коммит/push не выполнялись.


### API_002, этапы 0–1: spaces/tree/Document приняты

Матрица владельцев/прав/этапов: TASK_API_002_execution-plan.md. Реализованы 8 Space,
7 tree/folders и 7 Document capabilities через owner typed handlers. API
не содержит собственных CRUD алгоритмов. Общий Core prepare теперь используется
существующими UI helpers и API; DocumentsWorkingSet/API делят DocumentEditRules.
Фиксированный защищённый pagination cursor привязан к user/client/query.

DataProvider финально перепроверяет security session/grant и permission assertions
в User через ту же шину после занятия writer. Новые API writes без этого admission
не получают разрешения. Технические планы удаления/receipt-reference атомарны;
writer проверяет TTL, owner/client, fingerprint структуры/версий/зависимостей.
Нет глобального Save/версии пространства, нет нового Note типа, SQL schema не сбрасывалась.

Release build0errors. Полный PostgreSQL прогон `api-stage1-delete-regression.trx`:
**175 passed, 0 failed, 0 skipped**. Добавлены SQL tests на stale/foreign/nonempty
планы и technical receipt restart, final grant revocation/запрет GlobalAdmin по
login, HTTP regression bodylessDELETE и unsupported content415.

RSTT app/logger выполнен. `api-crud-smoke.cjs` exit0, настоящие A M2M token/session:
явные grant/местная роль заданы через owner UI; бизнес-операции только Node fetch
без cookie. Space create/read,8parallelretry, conflict иное тело, app restart+
повтор того же ID/session; empty Folder read/update/tree; Documentv1→v2 A→v3 B
от прежнейv1 без merge, no-op без версии, история/авторство/restorev4; move/отказ
cross-space; grant SpaceScope ограничивает даже localGlobalAdmin; delete/retry,
stale plan409, свежий план удаляет поддерево, Space delete и durable lookup.

Smoke нашёл два исправления: глобальная администрация не должна требовать выбранного
пространства на новой пустой установке; generic OpenAPI Accepts metadata отбрасывал
bodyless DELETE в catchall. Административный bypass теперь общий /administration,
серверные permission guards сохранены. JSON schema metadata отделён от runtime
consumes matching, реальные type/content checks делают binding; Path params описаны.
UI QA helper ждёт загрузки конкретной grant form перед вводом; это исправление теста.

QA Service be-api-smoke включён в отдельную собственную QA группу с местной ролью
Админ для проверок создания Space. Это явное действие owner UI, не выдача роли
через token/provider или SQL. Обычный новый сервис роли автоматически не получает.
Текущие исходники и образ согласованы; коммит/push/версия не изменялись.
Следующий этап — RichText CRUD/полная версия/публикация; uploads/jobs ещё не реализованы.


### API_002, этап 2: RichText принят

12 owner capabilities: create/card/content/revisions/metadata/history/version/
publication/description/restore/delete. API block schema1 не содержит EF/viewport;
полный ChunkOrder автора и Changes от BaseVersion, stable new:key IDs.
RichTextEditService UI/API используют общий assembler. Chunk cursor защищён и
закреплён за явным snapshot. Внешний HTML проходит bounded whitelist sanitizer,
не вызывает UI downloader/HTTP fetch. References изображений/видео валидируются.
Mutation receipt для metadata/publication возвращает проверенную версию, не
создавая payload history.

Build0errors; полный реальный PostgreSQL `api-rich-depth.trx`: **181 passed,
0 failed, 0 skipped**. Шесть HTML tests включают encoded javascript, SVG/scripts,
remote img, table attributes, размер и глубокую вложенность. Глубина выявила
специальный HAP parser exception; он переведён в validation400, тест прошёл.
RSTT app/logger выполнен на этой сборке.

`api-rich-smoke.cjs` exit0: независимый be-api-reader создан/доказан адаптером,
явно назначены reader grant и локальная роль Ридерс через UI. Бизнес — только
Node fetch/M2M: initialv1/sanitization; v2 два чанка; v3 A/v4 B от общейv2
без примеси A; старый cursor после изменений прочитал точныйv2; restorev3→v5
и retry безv6; descriptionv2/publicationv2 без роста; title-only→v6; no-op
остаётсяv6. Published-only другой субъект видит толькоv2/старый заголовок,
не получаетv6/history/private/чужой receipt. База999 и чужой ChunkId отвергнуты,
удаление выполнено owner/планом Space. Нового storage reset не было.

Общие UI QA helpers исправлены: ждут загрузки выбранной grant form и результата
создания новой группы до ввода. Остатки собственных ApiQA_* пространств от
неудачного этапа1 удалены через публичный API (только creator==QA service).
Загрузки/Media/книги/jobs/остальные settings ещё не выполнены. Коммит/push нет.


### API_002, этап 3: staging/images/Media принято

DataProvider публикует uploads toolset: durable own-user/client/purpose/space/target
upload, части 4 MiB с обязательными длиной и SHA-256, полный digest, Ready/cancel,
TTL сутки, квота 4/2 GiB незавершённых на subject/client. Форматы/лимиты/целевой
тип/право объявляют описатели владельцев. Физические bytes идут только через
общую техническую data-plane по одноразовому opaque lease; ни Stream/bytes,
ни клиентский path не попадают в bus request. Staging пишет/убирает только DP;
maintenance удаляет собственные expired/cancelled/consumed temporary inputs.

Картинка и авторский snapshot статьи фиксируются одной SQL транзакцией; UploadId
потребляется той же транзакцией, rollback сохраняет возможность повторить.
PNG/JPEG/GIF/WebP до20MiB; SVG/remote-fetch нет. Внешнее скачивание проверяет
Read и конкретное членство файла в разрешённой версии (published-only не видит
файл нового draft). File/Range отдаётся напрямую после ACL по opaque read lease.
Старые публичные UI URL остаются отдельной SECURITY_001, redirect на них нет.

Media owner объявляет CRUD/list/download с явным space и тем же prepare metadata/
entity/file/association, что UI. Оригинал хранится без выдуманной обработки;
Duration остаётся null, transcoding не объявлен. User owner учитывает публичную
space-association Media при расчёте содержащего пространства. Delete видео
атомарно удаляет собственную association; остальные входящие связи и сохранённые
file references защищают его от удаления. Core не перечисляет Media тип/правила.

Build0errors. Полный реальный PostgreSQL api-upload-final.trx: **186 passed,
0 failed, 0 skipped**. Новые проверки: resume/new writer, foreign subject/client,
part/full digest, cancel, rollback consumption/retry, собственная association и
сохранение защиты других зависимостей. RSTT app/logger выполнен. Реальный
api-upload-smoke.cjs exit0: двухчастный PNG, app restart между частями,
wrong digest/part conflict/incomplete409, retry и ровно articlev2, reader404
на файл отсутствующий в publishedv1 и reader403 на draftv2, publicationv2 и
Range206; Media CRUD/explicitspace/Range обоим субъектам; video reference в статье
блокирует delete409; cancel запретил resume. Smoke удалил свои сущности;
cleanup дополнительно удалил одно подтверждённо собственное ApiQA_* пространство
неудачного запуска вместе с его Media. Другие данные/тома не удалялись.
Остались этапы4–6: крупные импорты/durable jobs, settings, финальная регрессия.
Коммит/push не выполнялись.


### API_002, этап 4: крупные импорты и durable jobs приняты (30.10.2026 МСК)

RichText owner: create/replace/append TXT/Markdown/HTML принимает ReadyUploadId,
BaseVersion для существующей статьи, фиксированный target/title и регистрирует
job+Deferred receipt одной транзакцией до202. Media create теперь тоже durable
job оригинального файла (без имитации transcoding). Одновременно выполняется
одна тяжёлая работа; assembly uploads использует тот же gate. Очередь/прогресс/
результат/отмена/повторное разрешение хранятся у DataProvider. Upload зарезервирован
за исходной операцией, cancellation/failure освобождает резерв; accepted input
потребляется вместе с canonical commit. Уже committed всегда возвращает Succeeded.

User bootstrap читает собственный сохранённый security context по JobId. Он не
содержит bearer/refresh/password/HttpContext. Проверяются LocalUser/client/session/
grant revision/provider epoch/link и исходный access expiry; отдельный background
scope получает проверенный API principal, никогда System для canonical Save.
NeedsReauthorization требует явной новой проверки. Reauthorization сохраняет
input/base/operation/generated IDs. HTTP scope не удерживается до конца job.

Streaming fragments ограничены (8192 input chars, HTML depth64/tag8192), UTF8 и
UTF16 BOM. API sanitizer не скачивает remote/data-URI изображения; прежний owner
HTML→blocks converter переиспользован после sanitizer. UI file importer тоже
читает fragments, больше нет MemoryStream/ToArray целого файла; clipboard
converters остаются отдельным существующим сценарием. Большие input блоки могут
разбиваться; это нормализация, не многодокументный импорт. Исправлен HAP numeric
entity decode для Unicode вне BMP. API jobs пишут normalized chunks напрямую в
DP-owned opaque staging, sealed index содержит только IDs/checksums. Save читает
ограниченные окна, сохраняет точные immutable row references, все SQL flush в
одной транзакции. История/полный порядок автора и file references сохранены.

Build0errors; **195 passed,0 failed,0 skipped**, реальный PostgreSQL
api-job-fix.trx. Новые проверки: durable registration/restart claim, sealed chunks,
SQL rollback и потерянный after-commit reply, cancellation/reservation, неизменный
input/op при новом разрешении, Unicode/HTML depth/remote fetch/fences. Real smoke
выявил ambient HttpContext race при одновременном bus callback: API scoped principal
теперь связан с конкретным request HttpContext, background имеет отдельный sink;
два regression tests. Helper initial empty chunk исключается при sealed source.
RSTT app/logger выполнен на этой сборке.

api-job-smoke.cjs exit0: actual worker/session bootstrap, три формата, emoji,
append/replace от старой базы без примеси, durable operation/job status, отмена
после commit вернула Succeeded, видео202→реальный canonical original+Range.
api-job-large-smoke.cjs exit0: **104857600 bytes TXT → одна статья/v1,3201 chunk**;
один Running/другие Queued, queued cancel, закрытая session остановила queued job,
явная reauthorization продолжила; приложение перезапущено при Running, итог один
accepted outcome/одна версия. **1073741824 bytes video**: части+полный SHA-256,
job copy, size и точный хвост Range206. Это original-byte transport fixture,
playback/transcoding/извлечение duration не объявлены. RSS из /proc/1/status:
book baseline327.74MiB/peak364.41MiB; video321.24MiB/peak330.32MiB.
Smoke удалил свои canonical сущности через owner API. Стартовая/оригинальная БД,
тома и корень файлового storage не очищались. Коммит/push не выполнялись.
Остались settings и финальная регрессия/документация.


### API_002, этап 5: allowlisted settings приняты

SysParameters owner публикует GET/PUT /settings/system (10 существующих прикладных
полей), GlobalAdmin + AllSpaces. UI/API используют общий min-normalization,
API ограничивает размер company/window. Новые book imports читают тот же
глобальный target chunk size, что существующий UI import. Space settings уже
реализованы owner на этапе1 (GET/PATCH, backup flag/interval, без пути).
User публикует own printing preferences и rich-doc displayed-level. Поля UserId,
roles/config/secret/connection/storage path отсутствуют. Existing User preference
services/normalizers вызываются через UserMutationCapture; SQL-изменения и receipt
коммитит DP вместе. Сам SQL writer разрешает неадмину только свои property типов
DocPrintPresets/DisplayedLevel; смена владельца/типа запрещена. Smoke выявил и помог
добавить этот explicit Self policy, проверен отдельным реальным SQL test.

JSON input теперь отвергает также отсутствующие обязательные поля и null в
non-nullable constructor arguments; HTTP negative tests подтверждают, что owner/
receipt не затрагиваются. Oversize declared book/video возвращает413 до IO.
Build0errors; api-settings-self.trx **196 passed,0 failed,0 skipped**. RSTT
app/logger выполнен. api-settings-smoke.cjs exit0: common UI company update,
min-normalization, idempotent retry без версии; reader c теми же capabilities
получил403 system; unknown config/foreign UserId/missing/null400; print clamps
и независимый другой user; уровни3/2 двух user не создали версию статьи;
restricted space session403 global setting, own permitted preference доступна;
Space PATCH setting и размеры100MiB+1/1GiB+1→413. System/printing восстановлены,
canonical fixture удалён через planned Space delete. Предыдущий failed smoke
также восстановил системные настройки. Коммит/push не выполнялись.
Остался этап6: совокупная регрессия, финальный аудит/документация/cleanup QA.


### API_002, этап 6: итоговая регрессия и аудит приняты

58 owner capabilities сверены с фактическим Swagger; каталог, клиентская
инструкция, registry miniapps/routes/messages/save contexts, index и Smoke README
обновлены. ExternalApi остаётся headless transport: нет чужих Internal/DbContext/
предметного CRUD. Typed owner requests используют одну шину, общие engine DTO
остаются нейтральными. DataProvider — единственный canonical/file writer.

Исправлено завершающим аудитом:

- Удаление target в той же SQL tx закрывает неиспользованные uploads и их jobs,
  освобождает reservations. Maintenance распознаёт уже осиротевшие staging inputs;
  cleanup затрагивает только собственные inputs, не storage root/volumes.
  Это проверено двумя реальными SQL tests.
- При повторном A→B M2M обнаружен истёкший служебный app-password. Provisioning
  теперь явно выдаёт отдельный `expiring=false` app-password. Create/retry/rotation
  проверяют возвращённых owner/intent; управление чужим token не допускается.
  Authentik TokenSerializer в PATCH подставляет request.user/api при пропуске
  полей: передаются явно identifier/user/intent/expiring. Четыре protocol cases
  проверяют create/retry, reissue и ошибочного management owner без выдачи ключа.
  Исправлены только явно известные QA credentials B, не человеческие пароли.
- Smoke helper waits приведены к реальной готовности grant form после reload;
  начальный Idle не обязан отображать SaveStateIndicator. Старые ожидания legacy
  login и profile notification заменены реальным OIDC и изменением/сохранением/
  восстановлением DisplayedName. Никаких ослаблений guard/dirty runtime не сделано.

Финальные проверки после исправлений:

- `dotnet build BusinessEntity.sln -c Release`: exit0, 0 ошибок. Известные прежние
  Razor/logger warnings и AutoMapper13 NU1903 остаются; не объявлены исправленными.
- `dotnet test ... --no-build` с отдельным реальным be-arch-test-db: **202/0/0**,
  api-service-final.trx. Тестовые БД только be_test_GUID; текущая БД не reset.
- RSTT: `docker compose build business-entity web_logger`, затем `up -d --no-build
  --no-deps --force-recreate business-entity web_logger`: оба exit0.
- `api-provider-switch-smoke --jobs`: exit0, **9 markers**. A→B→A; B M2M сохраняет
  LocalUserId; локальные A server/worker реально остановлены, B/session работают
  после restart приложения. Старые bearer/session/epoch не оживают. Большая
  работа и очередная job требуют явного reauthorize исходного user/client.
  После возврата A книга **104857600 bytes**, **12800 chunks**, одна версия v1
  текущего автора; queued маленькая книга также Succeeded. Это новый прогон с
  общим UI/API chunk target12000; более ранние RSS измерения этапа4 были при32768.
- `api-framework-smoke`: exit0, **11 markers**, реальный M2M/no cookie/parallel
  request scopes/grant revoke/close, filtered catalog и invalid client authority.
  Отдельный ApiBearerProtocolTests поднимает настоящие HTTP discovery/JWKS/API
  hosts, подписывает RSA JWT и отвергает issuer/audience/signature/expired/scope/
  client/missing client/UI ID token/unsigned/symmetric варианты.
- `api-crud-smoke`: exit0, **9 markers**, Space/Folder/Document/права/plan/receipt/
  retry через restart, независимость explicit SpaceId от UI.
- `api-rich-smoke`: exit0, **7 markers**, full snapshots/exact old refs/order/
  pinned cursor/restore/publication/no-op/published reader/foreign outcome deny.
- `api-upload-smoke`: exit0, **6 markers**, parts+app restart/digests/Ready,
  image+version атомарно, published file membership/Range, Media create202 через
  durable job, metadata CRUD и защита сохранённой чужой video reference.
- `api-job-smoke`: exit0, **5 markers**, TXT/Markdown/HTML/emoji, append/replace
  старой базы, late cancel сообщает Succeeded, video copy идёт настоящей job.
- `api-settings-smoke`: exit0, **5 markers**, system/space/self, общие UI/API
  normalizers, другие user, запрет unknown/missing/null/config/security input,
  размеры book100MiB+1/video1GiB+1→413. Настройки восстановлены.
- `api-ui-regression-smoke`: exit0. OIDC, tree, создание/сохранение/reload обоих
  редакторов, comment, реальное profile Save и восстановление имени, изоляция
  состояния двух окон, users admin, logger200. Navigation: **8 markers** —
  Stay/Discard/Save/validation error, два независимых draft, beforeunload reload/
  close и forceLoad Space без второго предупреждения. Все fixtures в собственном
  QA Space, удалены через owner API.
- `api-cleanup-owned-qa`: exit0, удалены оставшиеся **2** точно принадлежащих QA
  Space от промежуточного failed harness run. Ранее cleanup перед финальным
  provider proof удалил4 своих Space. Это canonical owner delete, без SQL bypass.
- `git diff --check`: exit0. .env не tracked. Исходный BusinessEntityStorage и
  новый BusinessEntityIdentityStorage существуют. A/B/Postgres/app/logger подняты;
  текущий active A подтверждён новым OIDC и A API после обратного переключения.

Матрица обязательных CRUD инвариантов:

| Критерии | Фактическая проверка |
|---|---|
| CRUD-01–05 | API CRUD/framework/rich: no cookie, typed permissions/space, retry/conflict, cursor/filter/published deny |
| CRUD-06 | Rich smoke + SQL ParallelAuthorsKeepFullOwnStateAndRestoreCreatesNewSnapshot и ConcurrentSavesAreOrderedWithoutMergingAuthors; разные actor IDs, immutable versions |
| CRUD-07–09 | Upload/job smoke, ApiUploadTests/ApiJobTests, FileTransactionTests/TransactionTests: staging без версии, rollback/after-commit/file manifest, exact refs/старые файлы |
| CRUD-10–11 | Deletion-plan conflict/video-reference deny, concurrent API requests, scoped principal tests и независимые окна |
| CRUD-12–13 | Upload ownership/digests/cancel и real app restart; durable session/job/outcome и reclaim |
| CRUD-14 | Этап4: фактические100MiB/1GiB и RSS; этап6:100MiB/12800chunks/shared target; реальные bytes/Range, без заявления playback |
| CRUD-15 | Закрытие session, queued cancellation, restart/IdP A→B→A, NeedsReauthorization/explicit reauth; atomic committed result побеждает поздний cancel |
| CRUD-16 | Architecture tests в202: запрет foreign Internal/DbContext в ExternalApi, owner bindings/neutral Core; shared prepare/rules/import/metadata/settings |
| CRUD-17 | Актуальные OIDC browser/navigation smoke, оба редактора и independent windows |
| CRUD-18 | Settings smoke + UserOwnedTransaction SQL policy, scope/self/allowlist и unknown input deny |

Практический итог: весь согласованный объём AUTH/API завершён. Исторический
публичный доступ через старые UI image/video URL остаётся ровно отдельной
TASK_SECURITY_001, как согласовано; новые API streams закрыты owner ACL.
Оригинальная БД/storage/volumes не удалялись, новые миграции/reset не выполнялись.
Коммит/push и повышение версии/CHANGELOG не выполнялись без отдельной команды.
