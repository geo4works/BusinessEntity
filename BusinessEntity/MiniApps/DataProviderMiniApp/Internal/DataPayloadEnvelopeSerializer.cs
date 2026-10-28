using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal;

// Формирует и читает канонический versioned JSON envelope для payload BusinessEntityData.
internal static class DataPayloadEnvelopeSerializer
{
    private const int CurrentSchemaVersion = 1;

    // Заворачивает raw payload JSON в storage-envelope с версией схемы и логическим kind.
    public static string CreateEnvelopeJson(BusinessEntityDto entity, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var kind = GetStorageKind(entity.EntityType);
        return CreateEnvelopeJson(
            kind,
            payloadJson,
            entity.CreatedByUserId,
            entity.LastModifiedByUserId);
    }

    // Заворачивает raw payload JSON в технический storage-envelope по явному kind.
    public static string CreateEnvelopeJson(
        string kind,
        string payloadJson,
        Guid? createdByUserId = null,
        Guid? lastModifiedByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new InvalidOperationException("Storage kind cannot be empty.");
        }

        var payload = CreateEnvelopePayload(payloadJson);
        var envelope = new DataPayloadEnvelopeRaw
        {
            SchemaVersion = CurrentSchemaVersion,
            Kind = kind,
            CreatedByUserId = createdByUserId,
            LastModifiedByUserId = lastModifiedByUserId,
            Payload = payload
        };

        return JsonSerializer.Serialize(envelope, StorageJsonOptions.Default);
    }

    // Читает envelope и возвращает его kind вместе с raw JSON тела payload.
    public static ParsedEnvelope ReadEnvelope(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
        {
            throw new InvalidOperationException("Stored payload envelope is empty.");
        }

        var envelope = JsonSerializer.Deserialize<DataPayloadEnvelopeRaw>(envelopeJson, StorageJsonOptions.Default)
            ?? throw new InvalidOperationException("Stored payload envelope is invalid.");

        if (envelope.SchemaVersion != CurrentSchemaVersion)
        {
            throw new NotSupportedException($"Unsupported payload schemaVersion '{envelope.SchemaVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Kind))
        {
            throw new InvalidOperationException("Stored payload envelope does not contain kind.");
        }

        if (envelope.Payload.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Payload должен быть объектом.");

        return new ParsedEnvelope(
            envelope.Kind,
            envelope.Payload.GetRawText(),
            envelope.CreatedByUserId,
            envelope.LastModifiedByUserId);
    }

    // Возвращает стабильный storage-kind, независимый от CLR type name.
    public static string GetStorageKind(BusinessEntityTypeEnum entityType)
    {
        return entityType == BusinessEntityTypeEnum.Undefined ? "Undefined" : entityType.ToString();
    }

    // Возвращает компактную длину JSON-строки для логов и диагностики.
    public static int GetJsonLength(string? json)
    {
        return string.IsNullOrEmpty(json) ? 0 : json.Length;
    }

    // Нормализует raw payload JSON и превращает его в detached JsonElement для envelope.
    private static JsonElement CreateEnvelopePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) throw new InvalidDataException("Пустое тело payload.");
        var normalizedPayloadJson = payloadJson;

        using var payloadDocument = JsonDocument.Parse(normalizedPayloadJson);
        if (payloadDocument.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Payload должен быть объектом.");
        return payloadDocument.RootElement.Clone();
    }

    // Структурированное представление уже провалидированного storage-envelope.
    internal sealed record ParsedEnvelope(
        string Kind,
        string PayloadJson,
        Guid? CreatedByUserId,
        Guid? LastModifiedByUserId);

    // Raw-envelope storage contract с фиксированными именами JSON-ключей.
    private sealed class DataPayloadEnvelopeRaw
    {
        [JsonPropertyName("schemaVersion")]
        [JsonRequired]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("kind")]
        [JsonRequired]
        public string Kind { get; set; } = string.Empty;

        [JsonPropertyName("createdByUserId")]
        [JsonRequired]
        public Guid? CreatedByUserId { get; set; }

        [JsonPropertyName("lastModifiedByUserId")]
        [JsonRequired]
        public Guid? LastModifiedByUserId { get; set; }

        [JsonPropertyName("payload")]
        [JsonRequired]
        public JsonElement Payload { get; set; }
    }
}
