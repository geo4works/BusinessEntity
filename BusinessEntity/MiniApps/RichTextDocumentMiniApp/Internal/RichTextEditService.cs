using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;

using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;
using BusinessEntity.WebLogger.Services;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// RichTextEditService: выделенный сценарий владельца статьи без общего смешанного helper.
public sealed class RichTextEditService
{
        private readonly IDataProviderConnector _dataProviderConnector;
        private readonly HtmlToRichTextBlocksConverter _htmlToRichTextBlocksConverter;
        private readonly RichTextDocumentReader _reader;
        private readonly IUserConnector _users;
        // Подключает только зависимости своего предметного сценария.
        public RichTextEditService(IDataProviderConnector dataProviderConnector, HtmlToRichTextBlocksConverter htmlToRichTextBlocksConverter, RichTextDocumentReader reader, IUserConnector users)
        { _dataProviderConnector = dataProviderConnector; _htmlToRichTextBlocksConverter = htmlToRichTextBlocksConverter; _reader = reader; _users = users; }
        // Готовит и сохраняет полный авторский набор правок.
        public async Task<EntitySaveBatch> PrepareEditorBatchAsync(Guid operationId, Guid editSessionId, Guid entityId, int baseVersion,
            string title, string description, IReadOnlyList<RichTextDocumentChunkEditDraft> drafts, CancellationToken ct = default)
        {
            if (!(await _users.GetEntityContentAccessAsync(entityId, ct)).CanEditDraft) throw new UnauthorizedAccessException("Нет прав на редактирование статьи.");
            if (baseVersion < 1) throw new ArgumentOutOfRangeException(nameof(baseVersion), "Не задана база редактора.");
            var manifest = await _dataProviderConnector.GetDataVersionAsync<RichTextDocument>(entityId, baseVersion, ct)
                ?? throw new KeyNotFoundException("База редактора не найдена.");
            var normalizedTitle = RichTextTitleRules.NormalizeRichTextDocumentTitle(title);
            var titleChanged = manifest.Name != normalizedTitle;
            manifest.Name = normalizedTitle;
            var changed = new List<BusinessEntityDataChunk>();
            var files = new List<EntityFileChange>();
            foreach (var draft in drafts)
            {
                var window = await _dataProviderConnector.GetVersionChunkWindowAsync(entityId, baseVersion, 0, 1,
                    [draft.ChunkId], cancellationToken: ct);
                var original = window.Chunks.SingleOrDefault() ?? throw new KeyNotFoundException("Чанк отсутствует в базе автора.");
                var converted = await _htmlToRichTextBlocksConverter.ConvertHtmlAsync(draft.Html, ct);
                var blocks = converted.Blocks ?? throw new InvalidDataException("Конвертер не вернул содержимое чанка.");
                if (JsonSerializer.Serialize(blocks) == JsonSerializer.Serialize(original.Blocks)) continue;
                changed.Add(new BusinessEntityDataChunk { Id = original.Id, BusinessEntityId = entityId,
                    SortOrder = original.SortOrder, Blocks = blocks.ToList() });
                files.AddRange(converted.Files.Select(file => RichTextFileChanges.FileChange(entityId, file)));
            }
            return new EntitySaveBatch(operationId, "RichTextDocumentMiniApp", editSessionId)
                { Payloads = [new(entityId, baseVersion, manifest, changed, VersionDescription: description)], Files = files };

        }

        // Готовит и сохраняет полный авторский набор правок.
        public async Task SaveRichTextDocumentAsync(BusinessEntity.Core.Classes.BusinessEntity entity,
            RichTextDocument manifest, IReadOnlyList<BusinessEntityDataChunk> chunks,
            IReadOnlyList<RichTextEmbeddedFile>? files, CancellationToken ct = default)
        {
            manifest.Id = entity.Id; manifest.Name = entity.Name;
            var existing = await _dataProviderConnector.GetDataAsync<RichTextDocument>(entity.Id, ct);
            var snapshot = chunks.OrderBy(x => x.SortOrder).Select((chunk, index) => CloneChunkForSave(chunk, entity.Id, index)).ToArray();
            var saved = await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
            {
                Entities = [entity], Payloads = [new(entity.Id, existing == null ? 0 : manifest.Version, manifest,
                    snapshot, snapshot.Select(x => x.Id).ToArray())],
                Files = (files ?? []).Select(file => RichTextFileChanges.FileChange(entity.Id, file)).ToArray()
            }, ct);
            manifest.Version = saved.Versions.Single().Version;
        }

        // Готовит и сохраняет полный авторский набор правок.
        public async Task AppendImportedContentAsync(BusinessEntity.Core.Classes.BusinessEntity entity,
            RichTextDocument manifest, IReadOnlyList<BusinessEntityDataChunk> appendedChunks,
            IReadOnlyList<RichTextEmbeddedFile>? appendedFiles, CancellationToken ct = default)
        {
            var membership = await _dataProviderConnector.GetVersionChunkMembershipAsync(entity.Id, manifest.Version, ct);
            var order = membership.Select(x => x.ChunkId).ToList();
            if (order.Count == 1 && IsOnlyInitialEmptyChunk((await _reader.GetChunkWindowAsync(entity.Id, 0, 1, manifest.Version, ct)).Chunks))
                order.Clear();
            var inserted = appendedChunks.OrderBy(x => x.SortOrder)
                .Select((chunk, index) => CloneChunkForSave(chunk, entity.Id, order.Count + index)).ToArray();
            order.AddRange(inserted.Select(x => x.Id));
            manifest.Name = entity.Name;
            var result = await _dataProviderConnector.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "RichTextDocumentMiniApp", null)
            {
                Payloads = [new(entity.Id, manifest.Version, manifest, inserted, order)],
                Files = (appendedFiles ?? []).Select(file => RichTextFileChanges.FileChange(entity.Id, file)).ToArray()
            }, ct);
            manifest.Version = result.Versions.Single().Version;
        }

        // Готовит и сохраняет полный авторский набор правок.
        private static BusinessEntityDataChunk CloneChunkForSave(BusinessEntityDataChunk source, Guid businessEntityId, long sortOrder)
        {
            return new BusinessEntityDataChunk
            {
                Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
                BusinessEntityId = businessEntityId,
                SortOrder = sortOrder,
                Blocks = source.Blocks?.Select(CloneBlock).ToList() ?? new List<BusinessEntityDataBlock>(),
                Version = source.Version
            };
        }

        // Готовит и сохраняет полный авторский набор правок.
        private static BusinessEntityDataBlock CloneBlock(BusinessEntityDataBlock source) =>
            JsonSerializer.Deserialize<BusinessEntityDataBlock>(JsonSerializer.Serialize(source))
                ?? throw new InvalidDataException("Не удалось скопировать блок.");

        // Готовит и сохраняет полный авторский набор правок.
        private static bool IsOnlyInitialEmptyChunk(IReadOnlyList<BusinessEntityDataChunk> chunks)
        {
            if (chunks == null || chunks.Count != 1)
            {
                return false;
            }

            var chunk = chunks[0];
            return chunk.Blocks == null || chunk.Blocks.Count == 0;
        }
}
