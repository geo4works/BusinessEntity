using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Contracts.Messages;

// Запрос операции GetComments.
public sealed record GetCommentsRequest(Guid RequestId, Guid BusinessEntityId) : BusRequest(RequestId);
// Результат операции GetComments или явный отказ.
public sealed record GetCommentsResponse(Guid RequestId, IReadOnlyList<BusinessEntityCommentRecord>? Result, BusError? Error = null) : BusResponse(RequestId, Error);

// Запрос операции CreateComment.
public sealed record CreateCommentRequest(Guid RequestId, BusinessEntityCommentCreateRequest Request) : BusRequest(RequestId);
// Результат операции CreateComment или явный отказ.
public sealed record CreateCommentResponse(Guid RequestId, BusinessEntityCommentRecord? Result, BusError? Error = null) : BusResponse(RequestId, Error);
