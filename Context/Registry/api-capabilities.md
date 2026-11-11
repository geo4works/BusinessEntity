# Каталог API v1

Фактический пакет AUTH_001 / API_001 / API_002, 30.10.2026. Ниже production bindings,
сверенные с `/swagger/api-v1/swagger.json`. Все calls требуют Bearer, после открытия
также `X-BE-Session-Id`; изменяющие owner commands требуют `Idempotency-Key`.
Framework open/close session не требуют operation key.

| Framework route | Владелец | Назначение |
|---|---|---|
| POST /api/v1/sessions | ExternalApi → User | открыть security session в пределах локального grant |
| GET /api/v1/sessions/current | ExternalApi → User | своя сессия |
| DELETE /api/v1/sessions/current | ExternalApi → User | закрыть свою сессию, без отката принятых операций |
| GET /api/v1/capabilities | ExternalApi catalog | только разрешённые capabilities |
| GET /api/v1/operations/{id} | ExternalApi → DataProvider | свой outcome с проверкой исходного capability/space |

Canonical возвращает ApiMutationOutcome из той же SQL transaction. Technical
возвращает известный DTO только после durable reference; Deferred возвращает202
после регистрации job. Read не имеет внешнего Idempotency-Key.

| Владелец | Capability v1 | Метод и route | Режим |
|---|---|---|---|
| DataProviderMiniApp | `jobs.cancel` | `DELETE /api/v1/jobs/{id}` | Technical |
| DataProviderMiniApp | `jobs.read` | `GET /api/v1/jobs/{id}` | Read |
| DataProviderMiniApp | `jobs.reauthorize` | `POST /api/v1/jobs/{id}/reauthorize` | Technical |
| DataProviderMiniApp | `uploads.cancel` | `DELETE /api/v1/uploads/{id}` | Technical |
| DataProviderMiniApp | `uploads.complete` | `POST /api/v1/uploads/{id}/complete` | Technical |
| DataProviderMiniApp | `uploads.create` | `POST /api/v1/uploads` | Technical |
| DataProviderMiniApp | `uploads.part.put` | `PUT /api/v1/uploads/{id}/parts/{partNumber}` | Technical |
| DataProviderMiniApp | `uploads.read` | `GET /api/v1/uploads/{id}` | Read |
| DocumentsMiniApp | `documents.create` | `POST /api/v1/spaces/{spaceId}/documents` | Canonical |
| DocumentsMiniApp | `documents.delete` | `DELETE /api/v1/documents/{id}` | Canonical |
| DocumentsMiniApp | `documents.read` | `GET /api/v1/documents/{id}` | Read |
| DocumentsMiniApp | `documents.restore-version` | `POST /api/v1/documents/{id}/restore-version` | Canonical |
| DocumentsMiniApp | `documents.update` | `PUT /api/v1/documents/{id}` | Canonical |
| DocumentsMiniApp | `documents.version.read` | `GET /api/v1/documents/{id}/versions/{version}` | Read |
| DocumentsMiniApp | `documents.versions` | `GET /api/v1/documents/{id}/versions` | Read |
| MediaServerMiniApp | `media-videos.create` | `POST /api/v1/spaces/{spaceId}/media-videos` | Deferred |
| MediaServerMiniApp | `media-videos.delete` | `DELETE /api/v1/media-videos/{id}` | Canonical |
| MediaServerMiniApp | `media-videos.download` | `GET /api/v1/media-videos/{id}/file` | Read |
| MediaServerMiniApp | `media-videos.list` | `GET /api/v1/spaces/{spaceId}/media-videos` | Read |
| MediaServerMiniApp | `media-videos.read` | `GET /api/v1/media-videos/{id}` | Read |
| MediaServerMiniApp | `media-videos.update` | `PATCH /api/v1/media-videos/{id}` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.content` | `GET /api/v1/rich-documents/{id}/content` | Read |
| RichTextDocumentMiniApp | `rich-documents.create` | `POST /api/v1/spaces/{spaceId}/rich-documents` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.delete` | `DELETE /api/v1/rich-documents/{id}` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.images.attach` | `POST /api/v1/rich-documents/{id}/images` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.images.download` | `GET /api/v1/rich-documents/{id}/images/{fileId}/{variant}` | Read |
| RichTextDocumentMiniApp | `rich-documents.import` | `POST /api/v1/rich-documents/imports/{targetId}` | Deferred |
| RichTextDocumentMiniApp | `rich-documents.metadata.update` | `PATCH /api/v1/rich-documents/{id}` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.publication.read` | `GET /api/v1/rich-documents/{id}/publication` | Read |
| RichTextDocumentMiniApp | `rich-documents.publication.update` | `PUT /api/v1/rich-documents/{id}/publication` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.read` | `GET /api/v1/rich-documents/{id}` | Read |
| RichTextDocumentMiniApp | `rich-documents.restore-version` | `POST /api/v1/rich-documents/{id}/restore-version` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.revisions.save` | `POST /api/v1/rich-documents/{id}/revisions` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.version.describe` | `PATCH /api/v1/rich-documents/{id}/versions/{version}/description` | Canonical |
| RichTextDocumentMiniApp | `rich-documents.version.read` | `GET /api/v1/rich-documents/{id}/versions/{version}` | Read |
| RichTextDocumentMiniApp | `rich-documents.versions` | `GET /api/v1/rich-documents/{id}/versions` | Read |
| SpacesMiniApp | `spaces.create` | `POST /api/v1/spaces` | Canonical |
| SpacesMiniApp | `spaces.delete` | `DELETE /api/v1/spaces/{id}` | Canonical |
| SpacesMiniApp | `spaces.deletion-plan` | `POST /api/v1/spaces/{id}/deletion-plan` | Canonical |
| SpacesMiniApp | `spaces.list` | `GET /api/v1/spaces` | Read |
| SpacesMiniApp | `spaces.read` | `GET /api/v1/spaces/{id}` | Read |
| SpacesMiniApp | `spaces.settings.read` | `GET /api/v1/spaces/{id}/settings` | Read |
| SpacesMiniApp | `spaces.settings.update` | `PATCH /api/v1/spaces/{id}/settings` | Canonical |
| SpacesMiniApp | `spaces.update` | `PATCH /api/v1/spaces/{id}` | Canonical |
| SysParametersMiniApp | `settings.system.read` | `GET /api/v1/settings/system` | Read |
| SysParametersMiniApp | `settings.system.update` | `PUT /api/v1/settings/system` | Canonical |
| TreeMiniApp | `folders.create` | `POST /api/v1/spaces/{spaceId}/folders` | Canonical |
| TreeMiniApp | `folders.read` | `GET /api/v1/folders/{id}` | Read |
| TreeMiniApp | `folders.update` | `PATCH /api/v1/folders/{id}` | Canonical |
| TreeMiniApp | `tree.children` | `GET /api/v1/spaces/{spaceId}/tree` | Read |
| TreeMiniApp | `tree.delete` | `POST /api/v1/tree/delete` | Canonical |
| TreeMiniApp | `tree.deletion-plan` | `POST /api/v1/tree/deletion-plan` | Canonical |
| TreeMiniApp | `tree.move` | `POST /api/v1/tree/move` | Canonical |
| UserMiniApp | `settings.personal.display.read` | `GET /api/v1/me/preferences/rich-documents/{id}` | Read |
| UserMiniApp | `settings.personal.display.update` | `PUT /api/v1/me/preferences/rich-documents/{id}` | Canonical |
| UserMiniApp | `settings.personal.printing.read` | `GET /api/v1/me/preferences/printing` | Read |
| UserMiniApp | `settings.personal.printing.update` | `PUT /api/v1/me/preferences/printing` | Canonical |
| UserMiniApp | `user.self.read` | `GET /api/v1/me` | Read |

Всего 58 owner capabilities. Input/output schema IDs каждого binding:
`<capability>.input.v1` / `<capability>.output.v1`; точные DTO обязательные поля
и binary bodies описаны OpenAPI. Metadata получают GiveMeApiToolsets /
HereAreApiToolsets на одной шине; duplicate/missing/foreign binding — startup error.

Предметные решения остаются у владельцев. ExternalApi не выбирает CLR method,
не знает EF/SQL и не содержит копий CRUD. Bytes идут по технической data-plane
DataProvider через opaque lease, не в Stream/byte[] bus messages.

[Клиентские примеры и лимиты](../Api-v1-client.md),
[журнал фактических проверок](../Tasks/TASK_AUTH_API_execution-log.md).
