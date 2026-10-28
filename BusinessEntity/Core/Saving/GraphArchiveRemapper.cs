using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.Core.Classes;

namespace BusinessEntity.Core.Saving;

// Общая перепривязка идентичностей; предметное содержимое переписывают только политики владельцев.
public sealed class GraphArchiveRemapper
{
    private readonly IReadOnlyDictionary<BusinessEntityTypeEnum, IEntityArchivePolicy> _policies;
    // Не содержит ветвлений по конкретным доменам.
    public GraphArchiveRemapper(IEnumerable<IEntityArchivePolicy> policies) => _policies = policies.ToDictionary(x => x.EntityType);
    // Создаёт полный независимый подграф до отправки единственного пакета восстановления.
    public GraphRestorePlan Remap(GraphArchive source, string rootName)
    {
        if (source.FormatVersion != GraphArchiveFormat.CurrentVersion) throw new NotSupportedException("Формат архива не поддерживается.");
        if (string.IsNullOrWhiteSpace(rootName)) throw new ArgumentException("Имя нового корня обязательно.", nameof(rootName));
        if (!source.Entities.Any(x => x.Card.Id == source.RootEntityId)) throw new InvalidDataException("В архиве нет корня.");
        var entities = CreateMap(source.Entities.Select(x => x.Card.Id));
        var chunks = CreateMap(source.Entities.SelectMany(x => x.Chunks).Select(x => x.Id).Distinct());
        var properties = CreateMap(source.Entities.SelectMany(x => x.Properties).Select(x => x.Id));
        var comments = CreateMap(source.Entities.SelectMany(x => x.Comments).Select(x => x.Id));
        var relations = CreateMap(source.Relations.Select(x => x.Id));
        var identities = new GraphRestoreIdentities(entities, chunks, properties, comments, relations);
        var result = source.Entities.Select(entity =>
        {
            var policy = _policies.TryGetValue(entity.Card.EntityType, out var value) ? value
                : throw new NotSupportedException($"Нет правила восстановления типа {entity.Card.EntityType}.");
            if (policy.RequiresPayload && entity.Payloads.Count == 0) throw new InvalidDataException("Отсутствует обязательный payload архива.");
            var card = JsonSerializer.Deserialize<Classes.BusinessEntity>(JsonSerializer.Serialize(entity.Card))
                ?? throw new InvalidDataException("Повреждена карточка архива.");
            var isRoot = card.Id == source.RootEntityId;
            card.Id = entities[card.Id];
            if (isRoot) card.Name = rootName;
            var payloads = entity.Payloads.Select(x => x with { Name = isRoot ? rootName : x.Name,
                EnvelopeJson = policy.RemapPayload(x.EnvelopeJson, identities) }).ToArray();
            var body = entity.Chunks.Select(x =>
            {
                if (Digest(x.EnvelopeJson) != x.Checksum) throw new InvalidDataException("Повреждён checksum чанка архива.");
                var json = policy.RemapChunk(x.EnvelopeJson, identities);
                return x with { Id = chunks[x.Id], EnvelopeJson = json, Checksum = Digest(json) };
            }).ToArray();
            return new ArchivedEntity(card, payloads, body,
                entity.ChunkReferences.Select(x => x with { ChunkId = Get(chunks, x.ChunkId) }).ToArray(),
                entity.Properties.Select(x => x with { Id = properties[x.Id], ParentEntityId = x.Owner == EntityPropertyOwner.Chunk
                    ? Get(chunks, x.ParentEntityId) : Get(entities, x.ParentEntityId) }).ToArray(),
                entity.Comments.Select(x => x with { Id = comments[x.Id], EntityId = Get(entities, x.EntityId),
                    ParentId = x.ParentId.HasValue ? Get(comments, x.ParentId.Value) : null }).ToArray(), entity.Files);
        }).ToArray();
        var mappedRelations = source.Relations.Select(x => new BusinessEntityRelation { Id = relations[x.Id],
            ObjectAId = Get(entities, x.ObjectAId), ObjectBId = Get(entities, x.ObjectBId), RelationType = x.RelationType,
            RelationParams = x.RelationParams, CreatedDate = x.CreatedDate, LastModifiedDate = x.LastModifiedDate }).ToArray();
        return new(new(GraphArchiveFormat.CurrentVersion, entities[source.RootEntityId], result, mappedRelations), identities);
    }
    // Каждая исходная идентичность однозначна; дублирование не превращается в выбор первой записи.
    private static Dictionary<Guid, Guid> CreateMap(IEnumerable<Guid> ids)
    {
        var result = new Dictionary<Guid, Guid>();
        foreach (var id in ids)
            if (id == Guid.Empty || !result.TryAdd(id, Guid.NewGuid())) throw new InvalidDataException("Повторный или пустой ID архива.");
        return result;
    }
    // Отсутствующая ссылка является ошибкой целостности архива.
    private static Guid Get(IReadOnlyDictionary<Guid, Guid> ids, Guid source) => ids.TryGetValue(source, out var id)
        ? id : throw new InvalidDataException("Архив ссылается на отсутствующий объект.");
    // Проверяет исходные bytes сериализованного чанка до перепривязки.
    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
// Подготовленный результат не меняет исходный архив и сохраняет явные таблицы перепривязки.
public sealed record GraphRestorePlan(GraphArchive Archive, GraphRestoreIdentities Identities);
