using BusinessEntity.Core.Saving;
using BusinessEntity.Core.RichText;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;

// Читает комментарии конкретного владельца, а не открытый generic-репозиторий.
public sealed record GetStoredCommentsRequest(Guid RequestId, Guid EntityId) : BusRequest(RequestId);
// Возвращает реальные данные activity; повреждение не маскируется пустым списком.
public sealed record GetStoredCommentsResponse(Guid RequestId, IReadOnlyList<StoredEntityComment>? Records, BusError? Error = null) : BusResponse(RequestId, Error);
// Читает свойства заданного уровня с необязательным ограничением владельца.
public sealed record GetStoredPropertiesRequest(Guid RequestId, EntityPropertyOwner Owner, Guid? ParentEntityId) : BusRequest(RequestId);
// Возвращает типизированную read-проекцию без EF DTO.
public sealed record GetStoredPropertiesResponse(Guid RequestId, IReadOnlyList<StoredEntityProperty>? Records, BusError? Error = null) : BusResponse(RequestId, Error);
// Окно или выбранные чанки читаются по конкретному списку версии, без ближайшей строки снизу.
public sealed record GetVersionChunkWindowRequest(Guid RequestId, Guid EntityId, int Version, long Start, int Count,
    IReadOnlyList<Guid>? ChunkIds = null, string? SearchText = null) : BusRequest(RequestId);
// Содержит порядок конкретной версии и общий размер её состава.
public sealed record GetVersionChunkWindowResponse(Guid RequestId, RichTextDocumentChunkWindow? Window, BusError? Error = null) : BusResponse(RequestId, Error);
// Находит неизменяемый файл по логическому адресу; отсутствие файла отделено от повреждения метаданных.
public sealed record GetStoredFileRequest(Guid RequestId, Guid EntityId, string FileId, string Variant) : BusRequest(RequestId);
// Серверный результат предназначен для HTTP FileResult, а не сериализации клиенту браузера.
public sealed record GetStoredFileResponse(Guid RequestId, StoredEntityFile? File, BusError? Error = null) : BusResponse(RequestId, Error);
// Читает только порядок и ссылки выбранной версии для импорта и формирования пакета.
public sealed record GetVersionChunkMembershipRequest(Guid RequestId, Guid EntityId, int Version) : BusRequest(RequestId);
// Состав отделён от тяжёлого тела документа.
public sealed record GetVersionChunkMembershipResponse(Guid RequestId, IReadOnlyList<VersionChunkMembership>? Records, BusError? Error = null) : BusResponse(RequestId, Error);
