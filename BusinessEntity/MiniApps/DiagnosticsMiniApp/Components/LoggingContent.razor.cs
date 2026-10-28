using Microsoft.AspNetCore.Components;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.Services;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace BusinessEntity.MiniApps.DiagnosticsMiniApp.Components
{
    // Предоставляет административные команды веб-журнала.
    public partial class LoggingContent
    {
        [Inject] public AuthentikSessionManager AuthService { get; set; } = default!;
        [Inject] public IUserConnector UserConnector { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public ILogger<LoggingContent> Logger { get; set; } = default!;
        [Inject] public IWebLoggerService WebLogger { get; set; } = default!;
        [CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

        private string LogMessage { get; set; } = string.Empty;
        private string StatusMessage { get; set; } = string.Empty;
        private bool IsLoading { get; set; } = false;
        private bool IsError { get; set; } = false;

        // Computed properties for UI
        private string AlertClass => IsError ? "alert-danger" : "alert-success";
        private string StatusLabel => IsError ? "Ошибка:" : "Успех:";

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var authState = await AuthenticationStateTask;
                var user = authState.User;

                if (user.Identity == null || !user.Identity.IsAuthenticated)
                {
                    Logger.LogInformation("Anonymous user accessed LoggingContent page - redirecting to login");
                    RedirectToLogin();
                    return; // Прекращаем дальнейшее выполнение, т.к. произойдёт редирект
                }

                var userName = user.Identity.Name ?? "Unknown";
                Logger.LogInformation("User {UserName} accessed LoggingContent page", userName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during initialization of LoggingContent page");
                SetStatusMessage("Ошибка при инициализации страницы", true);
            }
        }

        private async Task SendLogMessage()
        {
            if (string.IsNullOrWhiteSpace(LogMessage))
            {
                SetStatusMessage("Введите сообщение для логгирования", true);
                return;
            }

            IsLoading = true;
            StateHasChanged();

            try
            {
                var currentUser = await UserConnector.GetCurrentUserAsync();
                var userName = currentUser?.UserName ?? "Unknown";
                var fullMessage = $"[{userName}] {LogMessage}";

                // Отправляем сообщение в веб-логгер
                await WebLogger.Information(fullMessage);

                // Логгируем в консольный логгер
                Logger.LogInformation("Message sent to web logger: {Message}", fullMessage);

                SetStatusMessage($"Сообщение успешно отправлено в логгер: \"{LogMessage}\"", false);

                // Очищаем поле после успешной отправки
                LogMessage = string.Empty;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error sending message to web logger: {Message}", LogMessage);
                SetStatusMessage($"Ошибка при отправке сообщения: {ex.Message}", true);
            }
            finally
            {
                IsLoading = false;
                StateHasChanged();
            }
        }

        private void ClearMessage()
        {
            LogMessage = string.Empty;
            StatusMessage = string.Empty;
            IsError = false;
            StateHasChanged();
        }

        private void RedirectToLogin()
        {
            try
            {
                var loginUrl = AuthService.GetLoginUrl("/logging");
                Logger.LogInformation("Redirecting user to Authentik login url={LoginUrl}", loginUrl);
                Navigation.NavigateTo(loginUrl, forceLoad: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error redirecting to login from LoggingContent page");
                SetStatusMessage("Ошибка при перенаправлении на страницу входа", true);
            }
        }

        private void SetStatusMessage(string message, bool isError)
        {
            StatusMessage = message;
            IsError = isError;
            StateHasChanged();
        }
    }
}
