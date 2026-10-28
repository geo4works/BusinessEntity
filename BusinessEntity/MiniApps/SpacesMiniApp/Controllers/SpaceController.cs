using BusinessEntity.Services;
using BusinessEntity.MiniApps.SpacesMiniApp.Internal;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Controllers
{
    /// <summary>
    /// Серверные действия выбора пространства.
    /// </summary>
    [Authorize]
    [Route("api/space")]
    public class SpaceController : Controller
    {
        private readonly SpaceHelper _spaceHelper;
        private readonly IUserContextService _userContextService;
        private readonly IUserConnector _userConnector;
        private readonly ILogger<SpaceController> _logger;
        private readonly IWebLoggerService? _webLogger;

        public SpaceController(
            SpaceHelper spaceHelper,
            IUserContextService userContextService,
            IUserConnector userConnector,
            ILogger<SpaceController> logger,
            IWebLoggerService? webLogger = null)
        {
            _spaceHelper = spaceHelper;
            _userContextService = userContextService;
            _userConnector = userConnector;
            _logger = logger;
            _webLogger = webLogger;
        }

        /// <summary>
        /// Выбирает текущее пространство пользователя и редиректит на главную.
        /// </summary>
        [HttpGet("select/{spaceId:guid}")]
        public async Task<IActionResult> Select(Guid spaceId)
        {
            await LogInfoAsync(
                $"[space-selection] [controller:select-enter] requestPath={Request.Path} requestedSpaceId={spaceId} hasSelectedSpace={_userContextService.HasSelectedSpace} currentSpaceId={_userContextService.CurrentSpaceId?.ToString() ?? "null"} user='{User?.Identity?.Name ?? "anonymous"}'");

            var space = await _spaceHelper.GetSpaceByIdAsync(spaceId);
            if (space == null)
            {
                _logger.LogWarning("Failed to select space {SpaceId}: not found.", spaceId);
                await LogInfoAsync(
                    $"[space-selection] [controller:select-missing] requestedSpaceId={spaceId} action=redirect-space-selection");
                return Redirect("/space-selection");
            }

            _userContextService.SetSpace(space.Id, space.Name);
            _logger.LogInformation("Selected space {SpaceId} '{SpaceName}'.", space.Id, space.Name);
            await LogInfoAsync(
                $"[space-selection] [controller:select-success] selectedSpaceId={space.Id} selectedSpaceName='{space.Name}' action=redirect-home");
            return LocalRedirect("/");
        }

        /// <summary>
        /// Выбирает пространство для anonymous-режима после проверки anonymous-политики.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("select-anonymous/{spaceId:guid}")]
        public async Task<IActionResult> SelectAnonymous(Guid spaceId)
        {
            await LogInfoAsync(
                $"[space-selection] [controller:select-anonymous-enter] requestedSpaceId={spaceId} user='{User?.Identity?.Name ?? "anonymous"}'");

            var space = await _spaceHelper.GetSpaceByIdAsync(spaceId);
            if (space == null)
            {
                _logger.LogWarning("Failed to select anonymous space {SpaceId}: not found.", spaceId);
                return Redirect("/");
            }

            var anonymousSpaces = await _userConnector.GetAnonymousAccessibleSpacesAsync(HttpContext.RequestAborted);
            if (anonymousSpaces.All(anonymousSpace => anonymousSpace.Id != spaceId))
            {
                _logger.LogWarning("Failed to select anonymous space {SpaceId}: no anonymous access.", spaceId);
                await LogInfoAsync(
                    $"[space-selection] [controller:select-anonymous-denied] requestedSpaceId={spaceId}");
                return Redirect("/");
            }

            _userContextService.SetSpace(space.Id, space.Name);
            _logger.LogInformation("Selected anonymous space {SpaceId} '{SpaceName}'.", space.Id, space.Name);
            await LogInfoAsync(
                $"[space-selection] [controller:select-anonymous-success] selectedSpaceId={space.Id} selectedSpaceName='{space.Name}'");

            var documents = await _userConnector.GetAnonymousAccessibleDocumentsAsync(space.Id, HttpContext.RequestAborted);
            var firstDocument = documents.FirstOrDefault();
            if (firstDocument != null)
            {
                var documentRoute = GetDocumentRoute(firstDocument);
                await LogInfoAsync(
                    $"[space-selection] [controller:select-anonymous-open-document] selectedSpaceId={space.Id} documentId={firstDocument.Id} documentType={firstDocument.EntityType} route={documentRoute}");
                return LocalRedirect(documentRoute);
            }

            return LocalRedirect("/");
        }

        private static string GetDocumentRoute(UserAccessibleDocumentRecord document)
        {
            return document.EntityType switch
            {
                global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id => $"/rich-document/{document.Id}",
                global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id => $"/document/{document.Id}",
                _ => "/"
            };
        }

        private async Task LogInfoAsync(string message)
        {
            if (_webLogger != null)
            {
                await _webLogger.Information(message);
            }
        }
    }
}
