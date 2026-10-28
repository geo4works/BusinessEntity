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
internal sealed class UserPreferencesServiceMessageHandler : ScopedBusEndpoint
{
    private readonly UserPreferencesService _service;
    // Подписки принадлежат только текущему server scope.
    public UserPreferencesServiceMessageHandler(UserPreferencesService service, IMessageBus bus, BusRequestScope scope, ILogger<UserPreferencesServiceMessageHandler> logger) : base(bus, scope, logger) => _service = service;
    // Один адресный обработчик на объявленные типы запросов.
    protected override void SubscribeRequests()
    {
        Subscribe<GetRichDocBookmarksRequest, GetRichDocBookmarksResponse>(async request =>
        {
            var result = await _service.GetRichDocBookmarksAsync(request.DocumentId);
            return new GetRichDocBookmarksResponse(request.RequestId, result);
        }, static (id, error) => new GetRichDocBookmarksResponse(id, default, error));
        Subscribe<AddRichDocBookmarkRequest, AddRichDocBookmarkResponse>(async request =>
        {
            var result = await _service.AddRichDocBookmarkAsync(request.DocumentId, request.Selection);
            return new AddRichDocBookmarkResponse(request.RequestId, result);
        }, static (id, error) => new AddRichDocBookmarkResponse(id, default, error));
        Subscribe<DeleteRichDocBookmarkRequest, DeleteRichDocBookmarkResponse>(async request =>
        {
            var result = await _service.DeleteRichDocBookmarkAsync(request.BookmarkId);
            return new DeleteRichDocBookmarkResponse(request.RequestId, result);
        }, static (id, error) => new DeleteRichDocBookmarkResponse(id, default, error));
        Subscribe<GetRichDocDisplayedLevelRequest, GetRichDocDisplayedLevelResponse>(async request =>
        {
            var result = await _service.GetRichDocDisplayedLevelAsync(request.DocumentId);
            return new GetRichDocDisplayedLevelResponse(request.RequestId, result);
        }, static (id, error) => new GetRichDocDisplayedLevelResponse(id, default, error));
        Subscribe<SaveRichDocDisplayedLevelRequest, SaveRichDocDisplayedLevelResponse>(async request =>
        {
            await _service.SaveRichDocDisplayedLevelAsync(request.DocumentId, request.DisplayLevelCount);
            return new SaveRichDocDisplayedLevelResponse(request.RequestId, true);
        }, static (id, error) => new SaveRichDocDisplayedLevelResponse(id, default, error));
        Subscribe<GetCurrentUserPropertiesRequest, GetCurrentUserPropertiesResponse>(async request =>
        {
            var result = await _service.GetCurrentUserPropertiesAsync(request.PropertyType);
            return new GetCurrentUserPropertiesResponse(request.RequestId, result);
        }, static (id, error) => new GetCurrentUserPropertiesResponse(id, default, error));
        Subscribe<UpsertCurrentUserPropertyRequest, UpsertCurrentUserPropertyResponse>(async request =>
        {
            var result = await _service.UpsertCurrentUserPropertyAsync(request.PropertyId, request.PropertyType, request.Data, request.Metadata);
            return new UpsertCurrentUserPropertyResponse(request.RequestId, result);
        }, static (id, error) => new UpsertCurrentUserPropertyResponse(id, default, error));
        Subscribe<DeleteCurrentUserPropertyRequest, DeleteCurrentUserPropertyResponse>(async request =>
        {
            var result = await _service.DeleteCurrentUserPropertyAsync(request.PropertyId);
            return new DeleteCurrentUserPropertyResponse(request.RequestId, result);
        }, static (id, error) => new DeleteCurrentUserPropertyResponse(id, default, error));
        Subscribe<GetDocPrintPresetsRequest, GetDocPrintPresetsResponse>(async request =>
        {
            var result = await _service.GetDocPrintPresetsAsync();
            return new GetDocPrintPresetsResponse(request.RequestId, result);
        }, static (id, error) => new GetDocPrintPresetsResponse(id, default, error));
        Subscribe<SaveDocPrintPresetRequest, SaveDocPrintPresetResponse>(async request =>
        {
            var result = await _service.SaveDocPrintPresetAsync(request.Preset);
            return new SaveDocPrintPresetResponse(request.RequestId, result);
        }, static (id, error) => new SaveDocPrintPresetResponse(id, default, error));
        Subscribe<DeleteDocPrintPresetRequest, DeleteDocPrintPresetResponse>(async request =>
        {
            var result = await _service.DeleteDocPrintPresetAsync(request.PresetName);
            return new DeleteDocPrintPresetResponse(request.RequestId, result);
        }, static (id, error) => new DeleteDocPrintPresetResponse(id, default, error));
    }
}
