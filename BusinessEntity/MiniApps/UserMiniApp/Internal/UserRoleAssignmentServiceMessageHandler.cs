using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using System.Text.Json;

using ReactiveUI;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Messages;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Адресный endpoint выделенного сценария User.
internal sealed class UserRoleAssignmentServiceMessageHandler : ScopedBusEndpoint
{
    private readonly UserRoleAssignmentService _service;
    // Подписки принадлежат только текущему server scope.
    public UserRoleAssignmentServiceMessageHandler(UserRoleAssignmentService service, IMessageBus bus, BusRequestScope scope, ILogger<UserRoleAssignmentServiceMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<GetRoleAssignmentsRequest, GetRoleAssignmentsResponse>(async request =>
        {
            var result = await _service.GetRoleAssignmentsAsync(request.SpaceId);
            return new GetRoleAssignmentsResponse(request.RequestId, result);
        }, static (id, error) => new GetRoleAssignmentsResponse(id, default, error));
        Subscribe<CreateRoleAssignmentRequest, CreateRoleAssignmentResponse>(async request =>
        {
            var result = await _service.CreateRoleAssignmentAsync(request.SpaceId, request.Request);
            return new CreateRoleAssignmentResponse(request.RequestId, result);
        }, static (id, error) => new CreateRoleAssignmentResponse(id, default, error));
        Subscribe<DeleteRoleAssignmentRequest, DeleteRoleAssignmentResponse>(async request =>
        {
            var result = await _service.DeleteRoleAssignmentAsync(request.AssignmentId);
            return new DeleteRoleAssignmentResponse(request.RequestId, result);
        }, static (id, error) => new DeleteRoleAssignmentResponse(id, default, error));
    }
}
