# Пользовательская область

Норма TASK_AUTH_001. UserMiniApp владеет постоянным локальным UserId, внешними
привязками, локальными ролями/группами/назначениями, пользовательскими properties,
профилями провайдеров, процессом переключения и сессиями безопасности.
Infrastructure/Identity реализует стандартный OIDC протокол и интеграцию cookie;
Authentik является одной из необязательных реализаций управления каталогом.

## Проверка identity

Интерактивный вход: Authorization Code + PKCE, state, nonce, подпись, точные issuer
и audience, срок и разрешённый алгоритм. Предметный JIT запускается в TicketReceived
после полной протокольной проверки. Разбор JWT без валидации не даёт прав.
Raw claims не разбираются компонентами других владельцев: используется публичный
User контракт через единственную шину. UI своего миниаппа вызывает свои сервисы.

Связь определяется проверенной парой Issuer/Subject. Логин/email используются для
поиска конфликта, а не доказательства принадлежности. Management PK/UUID и sub
сопоставляет конкретный адаптер с проверкой настроенного subject mode. Имена
admin/akadmin не имеют специального смысла. Типы LocalUser: Human, Service, LocalSystem.

## Хранение и доступ

User schema 2 хранит Users, UserProperties, локальные роли/группы/назначения,
IdentityProviders, immutable IdentityProviderRevisions, ExternalIdentities,
IdentityInstallation, IdentitySwitchPlans и security records ApiAccessGrants/ApiSessions.
Наличие зарезервированных таблиц API не означает готовность HTTP API.

Cookie содержит локальный UserId, ProviderId, AuthEpoch и подтверждённую связь.
Действительность пользователя/привязки/эпохи проверяется на HTTP и при действии
живого circuit. Права читаются из актуальных локальных назначений. Старый circuit
после смены эпохи не начинает новую запись даже без перезагрузки страницы.
Черновики других окон не сохраняются и не переносятся автоматически.

UserOwnStorageWriter применяет служебные изменения User в SQL-транзакции
DataProvider вместе с outcome. Протокольный JIT и одноразовый bootstrap изменяют
только собственные служебные таблицы User. Канонический граф/файлы записывает
исключительно DataProvider. Внешние изменения у IdP не откатываются SQL rollback.

## Секреты и эксплуатация

Профили хранят только SecretRef из объявленного identity secret store. UI не
возвращает значения client/management secrets. Пароли, password hashes, JWT,
refresh tokens и credentials не попадают в DB/outcomes/events/logs.
Временная выдача живёт только в памяти инициатора. Потерянный временный пароль
можно явно перевыпустить только для созданной процессом ещё не активированной учётки.

Первый администратор привязывается однократно по pinned issuer/subject оператора.
Рестарт не проверяет и не сбрасывает фиксированные пароли. Сохранённый выбор БД
приоритетнее env bootstrap. Собственный Authentik запускается профилем identity-local;
существующий внешний OIDC не требует этих контейнеров. Рабочая среда требует HTTPS;
HTTP разрешён только Development.

[Администраторы](admin-users-policy.md), [ТЗ](../../Tasks/TASK_AUTH_001_pluggable-identity-providers.md),
[восстановление](../../Identity-recovery.md).
