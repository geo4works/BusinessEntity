using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using BusinessEntity.Services;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.Components;
using BusinessEntity.Core.BaseClasses.Relations;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Components
{    // Главная страница и публичный вход в пространства.
    public partial class SpacesHome
    {
        [Inject] public IUserConnector UserConnector { get; set; } = default!;
        [Inject] public IUserContextService UserContext { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public ILogger<SpacesHome> Logger { get; set; } = default!;
        [Inject] public ITreeSelectionService TreeSelectionService { get; set; } = default!;

        private string? CurrentUserName { get; set; }
        private string? CurrentUserId { get; set; }
        private bool IsAuthenticated { get; set; }
        private IReadOnlyList<UserSpaceRecord> AnonymousAccessibleSpaces { get; set; } = Array.Empty<UserSpaceRecord>();
        private bool IsAnonymousSpacesLoading { get; set; }
        private UserSpaceRecord? CurrentAnonymousSpace => UserContext.CurrentSpaceId.HasValue
            ? AnonymousAccessibleSpaces.FirstOrDefault(space => space.Id == UserContext.CurrentSpaceId.Value)
            : null;

        // Состояние выбранных узлов дерева через сервис
        private bool IsMultiSelectActive => TreeSelectionService.IsMultiSelectActive;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var currentUser = await UserConnector.GetCurrentUserAsync();
                IsAuthenticated = currentUser?.IsAuthenticated == true;

                if (IsAuthenticated)
                {
                    CurrentUserName = currentUser!.UserName;
                    CurrentUserId = currentUser.UserId;

                    Logger.LogInformation("User {UserName} accessed main page", CurrentUserName);
                }
                else
                {
                    await LoadAnonymousEntryAsync();
                    Logger.LogInformation("Anonymous user accessed main page");
                }

                // Подписываемся на изменения выбора в дереве
                TreeSelectionService.SelectionChanged += OnTreeSelectionChanged;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading user information on main page");
            }
        }

        private void OnTreeSelectionChanged(List<TreeSelectedItem> selectedNodes)
        {
            try
            {
                Logger.LogInformation("Tree selection changed: {Count} nodes selected", selectedNodes.Count);
                StateHasChanged(); // Обновляем UI при изменении выбора
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling tree selection change");
            }
        }

        // Открывает локальную форму входа и возвращает пользователя на главную после авторизации.
        private void OpenLoginForm()
        {
            try
            {
                var loginUrl = $"/login?returnUrl={Uri.EscapeDataString("/")}";
                Logger.LogInformation("Redirecting anonymous user to local login form url={LoginUrl}", loginUrl);
                Navigation.NavigateTo(loginUrl);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error redirecting to login");
            }
        }

        // Загружает anonymous-доступные пространства и автоматически выбирает единственное пространство.
        private async Task LoadAnonymousEntryAsync()
        {
            IsAnonymousSpacesLoading = true;
            try
            {
                AnonymousAccessibleSpaces = await UserConnector.GetAnonymousAccessibleSpacesAsync();
                if (AnonymousAccessibleSpaces.Count == 0)
                {
                    Navigation.NavigateTo($"/login?returnUrl={Uri.EscapeDataString("/")}", replace: true);
                    return;
                }

                if (UserContext.CurrentSpaceId.HasValue &&
                    AnonymousAccessibleSpaces.Any(space => space.Id == UserContext.CurrentSpaceId.Value))
                {
                    await OpenFirstAnonymousDocumentAsync(UserContext.CurrentSpaceId.Value);
                    return;
                }

                if (AnonymousAccessibleSpaces.Count == 1 &&
                    (!UserContext.CurrentSpaceId.HasValue || UserContext.CurrentSpaceId.Value != AnonymousAccessibleSpaces[0].Id))
                {
                    SelectAnonymousSpace(AnonymousAccessibleSpaces[0].Id);
                }
            }
            finally
            {
                IsAnonymousSpacesLoading = false;
            }
        }

        // Переводит anonymous-посетителя в выбранное публичное пространство через серверный endpoint.
        private void SelectAnonymousSpace(Guid spaceId)
        {
            Navigation.NavigateTo($"/api/space/select-anonymous/{spaceId}", forceLoad: true);
        }

        // Открывает первый anonymous-доступный документ текущего пространства.
        private async Task OpenFirstAnonymousDocumentAsync(Guid spaceId)
        {
            var documents = await UserConnector.GetAnonymousAccessibleDocumentsAsync(spaceId);
            var firstDocument = documents.FirstOrDefault();
            if (firstDocument == null)
            {
                Logger.LogInformation("Anonymous space {SpaceId} has no openable documents", spaceId);
                return;
            }

            var route = firstDocument.EntityType switch
            {
                global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id => $"/rich-document/{firstDocument.Id}",
                global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id => $"/document/{firstDocument.Id}",
                _ => "/"
            };

            if (route == "/")
            {
                return;
            }

            Logger.LogInformation(
                "Opening first anonymous document {DocumentId} of type {EntityType} in space {SpaceId}",
                firstDocument.Id,
                firstDocument.EntityType,
                spaceId);
            Navigation.NavigateTo(route);
        }

        // Вспомогательные методы для работы с выбранными узлами
        private string GetSelectedNodesInfo()
        {
            return TreeSelectionService.GetSelectedNodesInfo();
        }
    }
}
