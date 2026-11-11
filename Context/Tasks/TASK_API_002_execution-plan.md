# Раскатка API_002 после проверенного AUTH_001/API_001

Статус 30.10.2026: этапы 0–6 выполнены и проверены.
Фактические прогоны/размеры/ограничения — [execution log](TASK_AUTH_API_execution-log.md).

## Этап 0: сверка и контракты

Фактический каркас: 16 логических миниаппов, одна шина, User владеет identity/grants/
sessions, DataProvider — canonical и техническим storage. Все три решения §11
API_002 уже подтверждены. Новых предметных типов/режимов истории нет.

| Этап | Владелец и общий сценарий | Права и атомарность |
|---|---|---|
| 1: Space | SpaceHelper, SpacePropertiesWorkingSet | GlobalAdmin create; AdminSpace update/delete/settings; один пакет |
| 1: Folder/tree | TreeMiniAppService + общий graph helper | AdminItems на источниках/цели; explicit SpaceId; cross-space move409 |
| 1: Document | Собственный headless сценарий + общие правила DocumentsWorkingSet | AdminItems create; content policy read/edit/delete/history; полная база автора |
| 2: RichText | RichTextEdit/reader/search/outline/file services | Draft/published/history раздельно; новая версия по содержимому/названию |
| 3: uploads | Общий DataProvider staging + Infrastructure transport | subject/client/purpose/space; opaque IDs, части/digest, отсутствие canonical файлов до Save |
| 3: media/images | Собственные file/media сценарии | ACL каждого download/Range; включение подготовленного файла в атомарный Save |
| 4: jobs/import | Runtime владельца + DP technical records | Один heavy worker; актуальная сессия/epoch/права до финального commit |
| 5: settings | Spaces/SysParameters/User | Явный allowlist; server paths/secrets/identity administration исключены |
| 6 | Регрессия, каталог, документация | Build/tests/RSTT/без-cookie CRUD/провайдеры/крупные входы |

HTTP bindings используют только известные Request/Response. Canonical write возвращает
сохранённый ApiMutationOutcome. Technical/deferred операции получают отдельный
проверяемый typed путь регистрации, без фиктивного «успеха» Pending.
Ключ/operation ID и generated IDs устойчивы. DataProvider final admission повторно
проверяет security session/допуск и объявленные владельцем permission assertions
после занятия единственного writer, до применения пакета.

Списки: opaque защищённый cursor, фиксированный порядок, фильтрация прав до страницы.
Удаление: короткоживущий авторизованный технический план; fingerprint состава/
зависимостей перепроверяет сам writer. Это не версия/dirty пространства.

Внешняя статья: schema v1 блоков, HTML sanitization и проверенные attachment/media
references. Курсор содержимого привязан к конкретной версии. Импорт UTF text/Markdown/
HTML выполняется ограниченными блоками; доверенный staging manifest позволяет
один финальный Save без нескольких полных копий книги. Технические записи используют
уже предусмотренную таблицу schema3; повторный сброс/миграция БД не требуются.

При начальном аудите часть inline CRUD находилась в helper/working set.
Выделены общие owner prepare/rules; ExternalApi не содержит предметных алгоритмов.
Потоковый импорт внедрён на этапе4 вместо полного MemoryStream/ToArray. Старые публичные media/image URL
остаются отдельной SECURITY_001; новые API bytes не перенаправляют на них.

Этапы с кодом принимаются последовательно только после фактических проверок;
журнал результатов — TASK_AUTH_API_execution-log.md. Коммит/push не поручены.
