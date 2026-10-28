using BusinessEntity.Infrastructure.Bus;
namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
// Страница просит дерево показать доступный узел в своём окне.
public sealed record RevealTreeEntityRequest(Guid RequestId, Guid? EntityId) : BusRequest(RequestId);
// Ответ подтверждает принятие намерения, не раскрывая модель узла.
public sealed record RevealTreeEntityResponse(Guid RequestId, BusError? Error = null) : BusResponse(RequestId, Error);
