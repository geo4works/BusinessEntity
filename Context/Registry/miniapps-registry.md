# Реестр миниаппов

Фактическое состояние этапа 0 TASK_ARCH_001, 2026-10-28.
Приложение содержит семь миниаппов. Host-service, декларативные модули
и общий контекст сохранения добавляются в этапах 4–6, пока их нет.

| Миниапп | Назначение | Текущее время жизни | Доступ и состояние |
|---|---|---|---|
| DataProviderMiniApp | Граф, payload, чанки, properties, файлы | Service/handler/facade singleton; connector scoped | Bus Request/Response; кодек и фасад в connector исправляются на этапе 1; обходы репозиториев — на этапе 2 |
| UserMiniApp | Identity, локальные пользователи, роли, группы, права | Scoped | Connector прямо вызывает facade; State и handler scoped; разделение областей и адресация — этап 1 |
| TreeMiniApp | Дерево пространства и действия над узлами | Scoped | Bus connector с зависимостью от facade; handler без адреса окна — этап 1 |
| ActivityMiniApp | Комментарии | Scoped | Connector вызывает facade; сервис пишет репозиторий комментариев напрямую — этапы 1–2 |
| MediaServerMiniApp | Видео и фоновые загрузки | Service/facade scoped; registry/manager singleton, manager hosted | Прямой API; постоянные файлы переносит этап 2; UI/контроллер — этапы 5–6 |
| SampleDataMiniApp | Начальные данные | Scoped | Facade и сервис; ручной startup пока в Program; перенос запуска — этап 4 |
| UserMessagesMiniApp | Адресованные пользователю сообщения | State/handler/facade singleton | PostUserMessage/ClearUserMessages; адрес окна и доставка ошибки — этап 1 |

Регистрации: [MiniApps](../../BusinessEntity/MiniApps).
Фактические контракты: [каталог сообщений](bus-messages-catalog.md).

## Владельцы, которые появятся в этапах 5–6

Navigation, Spaces, Documents, RichTextDocument, BackupRestore, SysParameters,
Diagnostics и Administration оформляются отдельными миниаппами по инвентарю ТЗ.
Это планируемые владельцы, а не уже существующий код.
WidgetsMiniApp для удаляемых часов не создаётся.

## Общий слой

Core содержит модели, фабрики и BusinessEntityHelper. Миниаппами они не являются.
До этапа 6 helper ещё знает конкретные типы; К-1 заменяет эти зависимости
описателями от владельцев. Infrastructure предоставляет единый транспорт,
жизненный цикл и механизм сохранения. Внутренние вкладочные хосты не вводятся.

## Обновление

Новый миниапп, module definition, handler, время жизни или публичный контракт
фиксируется здесь вместе с кодом. После этапа 4 реестр содержит фактические
host-service, размещения и владельцев контекстов сохранения.
Источник: [ТЗ v2.2](../Tasks/TASK_ARCH_001_miniapp-bus-architecture-migration.md).
