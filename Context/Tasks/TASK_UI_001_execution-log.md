# TASK_UI_001 — журнал реализации

11.11.2026. База main/ffd9f00. Пользователь поручил реализацию, CMMTP 2 и деплой
на 55.15 с внешним Authentik. Версия выпуска 0.18.0.

Э0: прочитаны контекст, ТЗ, регистрации, сохранение и образец Geo4Web.
Э1: удалены служебные страницы/иконки, профиль оставлен в аватаре, шестерёнка.
Э2: RightPanelMiniApp и AuxiliaryTab, один стартовый Messages, queue UI — в Multimedia.
Э3: перевод результатов/ошибок в адресный журнал, early collector и защита credentials.
Э4: сохранённые формы настроек, декларативные маршруты владельцев, ApplyOnCenter,
координация drafts и штатный navigation guard; операционные User/API/Identity отдельно.
Э5: полный локальный theme catalog, validation/schema, preview, palette, styles,
import/export, ранний namespace bootstrap и standard user layout.

Финальный SQL-прогон: 219 .NET passed, 0 skipped на отдельном PostgreSQL;
6 Node contract tests passed; 7 Python installer tests passed. Контейнерные app/logger
собраны. Native Edge на настоящем Blazor и отдельной PostgreSQL/OIDC fixture:
вход, навигация, одно Messages, draft между разделами, запись system settings,
копия/save/preview/apply/reload темы, standard layout, узкий viewport и удалённые URL
пройдены. Расширенный прогон профиля/Cancel/операционного Apply пройден.
Финальный native Edge: 13 сценариев PASS, включая фактическую запись layout property,
профиль, отмену preview, отсутствие побочного API grant и два реальных окна.
Исправлены найденные в браузере относительный URL в navigation guard, ранняя
доставка уведомления и refresh формы после внешнего Apply.

Найденные сценарии BotFactory и старый чат установки подтверждают INSPECT,
INSTALL, STATUS. INSTALL — только первая установка, новый source SHA возвращает
EXPLICIT_UPDATE_REQUIRED. Пользователю задан вопрос о подготовке сценария обновления;
ответ ожидается. Рабочий сервер не изменялся.


Артефакты: `BusinessEntity.Tests/TestResults/ui001-full.trx`,
`artifacts/ui001-full-tests.log`, `artifacts/ui001-browser/result.json`,
`artifacts/ui001-browser/settings.png`, `artifacts/ui001-docker-build.log`.
Фикстуры используют собственные PostgreSQL и подписанный локальный OIDC;
производственный пароль и регистрации провайдера не используются.
Финальные native-прогоны завершены без необработанных ошибок circuit.
Полная матрица A01–A18 на production до поставки не объявляется пройденной.
