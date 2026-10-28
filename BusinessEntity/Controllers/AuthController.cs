using BusinessEntity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;


namespace BusinessEntity.Controllers
{
    /// <summary>
    /// Контроллер точек входа аутентификации.
    /// Login/Logout работают через единый AuthentikSessionManager.
    /// Callback завершает code-flow и создаёт локальную cookie-сессию.
    /// </summary>
    [Route("auth")]
    public class AuthController : Controller
    {
        private readonly ILogger<AuthController> _logger;
        private readonly AuthentikSessionManager _authService;
        private readonly IUserConnector _userConnector;
        private readonly IUserMessageFlashStore _messages;

        public AuthController(
            ILogger<AuthController> logger,
            AuthentikSessionManager authService,
            IUserConnector userConnector,
            IUserMessageFlashStore messages)
        {
            _logger = logger;
            _authService = authService;
            _userConnector = userConnector;
            _messages = messages;
        }

        /// <summary>
        /// Перенаправляет браузер на Authentik authorize endpoint.
        /// </summary>
        [HttpGet("login")]
        public async Task<IActionResult> Login(string? returnUrl = null)
        {
            _logger.LogInformation("[AuthController.Login] Processing login request for returnUrl: {ReturnUrl}", returnUrl);
            
            // Если пользователь уже авторизован, перенаправляем
            if (User.Identity?.IsAuthenticated == true)
            {
                await _userConnector.EnsureCurrentUserAsync(HttpContext.RequestAborted);
                _logger.LogInformation("[AuthController.Login] User already authenticated, redirecting to: {ReturnUrl}", returnUrl ?? "/");
                return LocalRedirect(returnUrl ?? "/");
            }

            var loginUrl = _authService.GetLoginUrl(returnUrl);
            _logger.LogInformation("[AuthController.Login] Redirecting to Authentik login URL: {LoginUrl}", loginUrl);
            return Redirect(loginUrl);
        }

        /// <summary>
        /// Принимает локальную форму логин/пароль и выполняет server-side login flow в Authentik без browser redirect.
        /// </summary>
        [HttpPost("password-login")]
        public async Task<IActionResult> PasswordLogin(
            [FromForm] string? username,
            [FromForm] string? password,
            [FromForm] string? returnUrl = null)
        {
            var safeReturnUrl = NormalizeReturnUrl(returnUrl);

            if (User.Identity?.IsAuthenticated == true)
            {
                await _userConnector.EnsureCurrentUserAsync(HttpContext.RequestAborted);
                return LocalRedirect(safeReturnUrl);
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return LocalRedirect(BuildLoginErrorReturnUrl(safeReturnUrl));
            }

            try
            {
                var redirectUrl = await _authService.CompletePasswordLoginAsync(
                    username.Trim(),
                    password,
                    safeReturnUrl,
                    HttpContext.RequestAborted);
                var localUser = await _userConnector.EnsureCurrentUserAsync(HttpContext.RequestAborted);
                _logger.LogInformation(
                    "[AuthController.PasswordLogin] Local user ensured. localUserId={LocalUserId} name={LocalUserName}",
                    localUser?.Id,
                    localUser?.ExternalId);
                return LocalRedirect(AddLoginNotification(redirectUrl, localUser?.Id, username.Trim()));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AuthController.PasswordLogin] Password login failed for username: {Username}", username);
                return LocalRedirect(BuildLoginErrorReturnUrl(safeReturnUrl));
            }
        }

        /// <summary>
        /// Callback для Authentik authorization-code flow.
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback(string? code, string? state, string? error)
        {
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogError("[AuthController.Callback] Authentication error from Authentik: {Error}", error);
                return Redirect($"/auth/error?message={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                _logger.LogError("[AuthController.Callback] No authorization code received");
                return Redirect("/auth/error?message=Authorization failed");
            }

            try
            {
                var returnUrl = await _authService.CompleteLoginAsync(code, state, HttpContext.RequestAborted);
                var localUser = await _userConnector.EnsureCurrentUserAsync(HttpContext.RequestAborted);
                _logger.LogInformation(
                    "[AuthController.Callback] Local user ensured. localUserId={LocalUserId} name={LocalUserName}",
                    localUser?.Id,
                    localUser?.ExternalId);
                return LocalRedirect(AddLoginNotification(returnUrl, localUser?.Id, User.Identity?.Name));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthController.Callback] Unexpected exception during callback processing");
                return Redirect("/auth/error?message=Authentication processing failed");
            }
        }

        /// <summary>
        /// Завершает локальную сессию и инициирует logout в Authentik.
        /// </summary>
        [HttpGet("logout")]
        [Authorize]
        public async Task<IActionResult> Logout(string? returnUrl = null)
        {
            _logger.LogInformation("[AuthController.Logout] Initiating sign-out. User: {User}", User.Identity?.Name);
            var redirectUrl = await _authService.SignOutAsync(HttpContext.RequestAborted);
            return Redirect(string.IsNullOrWhiteSpace(redirectUrl) ? (returnUrl ?? "/auth/logged-out") : redirectUrl);
        }

        private static string BuildLoginErrorReturnUrl(string returnUrl)
        {
            return QueryHelpers.AddQueryString(
                "/login",
                new Dictionary<string, string?>
                {
                    ["returnUrl"] = returnUrl,
                    ["loginError"] = "1"
                });
        }

        // Передаёт уведомление одному окну после login; соседние окна не очищаются.
        private string AddLoginNotification(string url, Guid? userId, string? userName)
        {
            if (!userId.HasValue) throw new InvalidOperationException("Login не создал локального пользователя.");
            var text = string.IsNullOrWhiteSpace(userName) ? "Вход выполнен успешно."
                : $"Вход выполнен успешно. Пользователь {userName.Trim()} авторизован.";
            var key = _messages.Create(userId.Value, text, "Логин успешен");
            return QueryHelpers.AddQueryString(url, "notification", key.ToString());
        }

        private static string NormalizeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return "/";
            }

            if (!returnUrl.StartsWith("/", StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal))
            {
                return "/";
            }

            return returnUrl.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) ? "/" : returnUrl;
        }
    }
}
