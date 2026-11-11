using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

// Техническая идентичность canonical операции; никаких credentials или произвольных runtime-команд.
public sealed record ApiOperationIdentity(Guid OperationId,string OwnerMiniAppId,string CapabilityId,int Version,string RequestHash,ApiWriteMode WriteMode=ApiWriteMode.Canonical);
// Deferred означает зарегистрированный durable job, а не произвольный успешный ответ HTTP adapter.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ApiWriteMode{Canonical,Technical,Deferred}
// Владелец сообщает основной объект/пространство своего пакета после проверки предметных прав.
public sealed record ApiOperationCommit(Guid OperationId,string RequestHash,Guid? EntityId,Guid? SpaceId=null,
    IReadOnlyList<ApiPermissionCheck>? PermissionChecks=null,Guid? DeletionPlanId=null,int? ResponseVersion=null,IReadOnlyList<Guid>? ConsumeUploads=null);
// Это серверные assertions владельца; HTTP input не может подставить их вместо собственных прав.
public sealed record ApiPermissionCheck(string Permission,Guid? EntityId=null,Guid? SpaceId=null,bool RequireAllSpaces=false);
// Общий результат записи строится из того же сохранённого пакета, а не из повторной генерации ID в HTTP.
public sealed record ApiMutationOutcome(Guid OperationId,Guid? EntityId,int? Version,bool CreatedVersion,IReadOnlyList<Guid> ChangedEntities,IReadOnlyList<Guid> DeletedEntities);
// Pending означает только регистрацию; до SQL commit исход не объявляется успешным/откаченным.
public sealed record ApiOperationReceipt(ApiOperationIdentity Identity,string State,ApiMutationOutcome? Result,Guid? SpaceId,DateTime ExpiresAt,Guid? ReferenceId=null);
// HTTP projection не раскрывает внутренний hash запроса и технического владельца строки.
public sealed record ApiOperationStatus(Guid OperationId,string State,ApiMutationOutcome? Result,DateTime ExpiresAt,Guid? ReferenceId=null);
public sealed record BeginApiOperationRequest(Guid RequestId,ApiOperationIdentity Operation):BusRequest(RequestId);
public sealed record BeginApiOperationResponse(Guid RequestId,ApiOperationReceipt? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record ReadApiOperationRequest(Guid RequestId,Guid OperationId):BusRequest(RequestId);
public sealed record ReadApiOperationResponse(Guid RequestId,ApiOperationReceipt? Result,BusError? Error=null):BusResponse(RequestId,Error);
