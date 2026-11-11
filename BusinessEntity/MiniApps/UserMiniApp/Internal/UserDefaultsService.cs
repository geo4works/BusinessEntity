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

using static BusinessEntity.MiniApps.UserMiniApp.Internal.UserValueRules;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// UserDefaultsService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserDefaultsService
{
        private readonly UserIdentityService _identityService;
        private readonly UserRoleCatalogService _roleCatalogService;
        // Получает зависимости выделенного сценария.
        public UserDefaultsService(UserIdentityService identityService, UserRoleCatalogService roleCatalogService)
        { _identityService = identityService; _roleCatalogService = roleCatalogService; }
        // Выполняет сценарий EnsureSystemDefaultsAsync внутри владельца User.
        public async Task EnsureSystemDefaultsAsync(CancellationToken cancellationToken = default)
        {
            await _roleCatalogService.EnsureSystemRolesAsync(cancellationToken);
            await _identityService.EnsureSystemUsersAsync(cancellationToken);
        }
}
