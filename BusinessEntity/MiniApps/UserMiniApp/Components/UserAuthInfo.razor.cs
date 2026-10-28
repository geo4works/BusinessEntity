using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.Services;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using Microsoft.AspNetCore.Components;

namespace BusinessEntity.MiniApps.UserMiniApp.Components
{
    // Показывает сведения текущей identity-сессии.
    public partial class UserAuthInfo
    {
        [Inject] private UserIdentityService UserIdentityService { get; set; } = default!;
        [Inject] public AuthentikSessionManager AuthService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public ILogger<UserAuthInfo> Logger { get; set; } = default!;

        private BusinessEntityUser? CurrentUserModel { get; set; }
        private string? CurrentUserName { get; set; }
        private string? CurrentUserEmail { get; set; }
        private string? CurrentUserId { get; set; }
        private string? IdentityToken { get; set; }
        private List<BusinessEntityClaim> AllClaims { get; set; } = new();
        private Dictionary<string, List<BusinessEntityClaim>> ClaimsByType { get; set; } = new();
        private List<string> AuthentikGroups { get; set; } = new();

        private string AuthenticationStatusText => CurrentUserModel?.IsAuthenticated == true ? "Да" : "Нет";

        protected override async Task OnInitializedAsync()
        {
            try
            {
                CurrentUserModel = await UserIdentityService.GetCurrentUserAsync();

                if (CurrentUserModel?.IsAuthenticated == true)
                {
                    CurrentUserName = CurrentUserModel.UserName;
                    CurrentUserEmail = CurrentUserModel.Email;
                    CurrentUserId = CurrentUserModel.UserId;
                    IdentityToken = await AuthService.GetIdentityTokenAsync();

                    AllClaims = CurrentUserModel.Claims.ToList();
                    ClaimsByType = AllClaims
                        .GroupBy(claim => claim.Type)
                        .ToDictionary(group => group.Key, group => group.ToList());
                    AuthentikGroups = CurrentUserModel.Groups.ToList();

                    Logger.LogInformation("User {UserName} accessed UserAuthInfo page with {ClaimsCount} claims", CurrentUserName, AllClaims.Count);
                }
                else
                {
                    Logger.LogInformation("Anonymous user accessed UserAuthInfo page - redirecting to login");
                    RedirectToLogin();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading user information on UserAuthInfo page");
            }
        }

        private void RedirectToLogin()
        {
            try
            {
                var loginUrl = AuthService.GetLoginUrl("/authinfo");
                Logger.LogInformation("Redirecting user to Authentik login url={LoginUrl}", loginUrl);
                Navigation.NavigateTo(loginUrl, forceLoad: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error redirecting to login from UserAuthInfo");
            }
        }

        private Task SignOut()
        {
            try
            {
                Logger.LogInformation("User {UserName} is requesting sign out from UserAuthInfo page", CurrentUserName);

                CurrentUserModel = null;
                CurrentUserName = null;
                CurrentUserEmail = null;
                CurrentUserId = null;
                AuthentikGroups.Clear();
                AllClaims.Clear();
                ClaimsByType.Clear();

                StateHasChanged();
                Logger.LogInformation("Redirecting to sign out page");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during sign out preparation from UserAuthInfo");
            }
            finally
            {
                Navigation.NavigateTo("/auth/logout", forceLoad: true);
            }

            return Task.CompletedTask;
        }

        private string GetFriendlyClaimTypeName(string claimType)
        {
            return claimType switch
            {
                System.Security.Claims.ClaimTypes.NameIdentifier => "ID пользователя",
                System.Security.Claims.ClaimTypes.Name => "Имя пользователя",
                System.Security.Claims.ClaimTypes.Email => "Email",
                System.Security.Claims.ClaimTypes.GivenName => "Имя",
                System.Security.Claims.ClaimTypes.Surname => "Фамилия",
                System.Security.Claims.ClaimTypes.Role => "Роль",
                System.Security.Claims.ClaimTypes.AuthenticationMethod => "Метод аутентификации",
                "sub" => "Субъект (Subject)",
                "aud" => "Аудитория (Audience)",
                "iss" => "Издатель (Issuer)",
                "iat" => "Выдан в (Issued At)",
                "exp" => "Истекает (Expires)",
                "nbf" => "Не действителен до (Not Before)",
                "jti" => "JWT ID",
                "groups" => "Группы пользователя",
                "preferred_username" => "Предпочитаемое имя пользователя",
                "given_name" => "Имя",
                "family_name" => "Фамилия",
                _ => claimType
            };
        }
    }
}
