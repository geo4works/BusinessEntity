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
internal sealed class UserRoleAssignmentDirectoryMessageHandler : ScopedBusEndpoint
{
    private readonly UserRoleAssignmentDirectory _service;
    // Подписки принадлежат только текущему server scope.
    public UserRoleAssignmentDirectoryMessageHandler(UserRoleAssignmentDirectory service, IMessageBus bus, BusRequestScope scope, ILogger<UserRoleAssignmentDirectoryMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<GetRoleAssignmentSpacesRequest, GetRoleAssignmentSpacesResponse>(async request =>
        {
            var result = await _service.GetRoleAssignmentSpacesAsync();
            return new GetRoleAssignmentSpacesResponse(request.RequestId, result);
        }, static (id, error) => new GetRoleAssignmentSpacesResponse(id, default, error));
    }
}
