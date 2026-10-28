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
internal sealed class UserAccountsServiceMessageHandler : ScopedBusEndpoint
{
    private readonly UserAccountsService _service;
    // Подписки принадлежат только текущему server scope.
    public UserAccountsServiceMessageHandler(UserAccountsService service, IMessageBus bus, BusRequestScope scope, ILogger<UserAccountsServiceMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<DeleteCurrentUserRequest, DeleteCurrentUserResponse>(async request =>
        {
            var result = await _service.DeleteCurrentUserAsync();
            return new DeleteCurrentUserResponse(request.RequestId, result);
        }, static (id, error) => new DeleteCurrentUserResponse(id, default, error));
        Subscribe<GetAdministrationUsersRequest, GetAdministrationUsersResponse>(async request =>
        {
            var result = await _service.GetAdministrationUsersAsync();
            return new GetAdministrationUsersResponse(request.RequestId, result);
        }, static (id, error) => new GetAdministrationUsersResponse(id, default, error));
        Subscribe<ReadAdministrationUsersFromAuthentikRequest, ReadAdministrationUsersFromAuthentikResponse>(async request =>
        {
            var result = await _service.ReadAdministrationUsersFromAuthentikAsync();
            return new ReadAdministrationUsersFromAuthentikResponse(request.RequestId, result);
        }, static (id, error) => new ReadAdministrationUsersFromAuthentikResponse(id, default, error));
        Subscribe<CreateAdministrationUserRequest, CreateAdministrationUserResponse>(async request =>
        {
            var result = await _service.CreateAdministrationUserAsync();
            return new CreateAdministrationUserResponse(request.RequestId, result);
        }, static (id, error) => new CreateAdministrationUserResponse(id, default, error));
        Subscribe<UpdateAdministrationUserRequest, UpdateAdministrationUserResponse>(async request =>
        {
            var result = await _service.UpdateAdministrationUserAsync(request.UserId, request.Request);
            return new UpdateAdministrationUserResponse(request.RequestId, result);
        }, static (id, error) => new UpdateAdministrationUserResponse(id, default, error));
        Subscribe<DeleteAdministrationUserRequest, DeleteAdministrationUserResponse>(async request =>
        {
            var result = await _service.DeleteAdministrationUserAsync(request.UserId);
            return new DeleteAdministrationUserResponse(request.RequestId, result);
        }, static (id, error) => new DeleteAdministrationUserResponse(id, default, error));
        Subscribe<GetProfileRequest, GetProfileResponse>(async request =>
        {
            var result = await _service.GetProfileAsync();
            return new GetProfileResponse(request.RequestId, result);
        }, static (id, error) => new GetProfileResponse(id, default, error));
        Subscribe<UpdateProfileRequest, UpdateProfileResponse>(async request =>
        {
            var result = await _service.UpdateProfileAsync(request.Request);
            return new UpdateProfileResponse(request.RequestId, result);
        }, static (id, error) => new UpdateProfileResponse(id, default, error));
    }
}
