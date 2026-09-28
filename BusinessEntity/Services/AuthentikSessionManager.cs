using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BusinessEntity.Services
{
    public sealed class AuthentikSessionManager
    {
        private const string HttpClientName = "AuthentikAuth";
        private const string StatePurpose = "BusinessEntityData.Authentik.State";
        private const string AccessTokenName = "access_token";
        private const string RefreshTokenName = "refresh_token";
        private const string IdTokenName = "id_token";
        private const string AccessTokenExpiresAtName = "access_token_expires_at";
        private const string SessionModeProperty = "authentik_session_mode";
        private const string PasswordFlowSessionMode = "password_flow";
        private const string IdentificationStageComponent = "ak-stage-identification";
        private const string PasswordStageComponent = "ak-stage-password";
        private const string FlowErrorComponent = "ak-stage-flow-error";
        private const string FlowRedirectComponent = "xak-flow-redirect";
        private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AuthentikSessionManager> _logger;
        private readonly IDataProtector _stateProtector;
        private readonly string _serverBaseUrl;
        private readonly string _browserBaseUrl;
        private readonly string _hostHeader;
        private readonly string _providerSlug;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;
        private readonly string _scope;
        private readonly string _authenticationFlowSlug;
        private readonly TimeSpan _sessionLifetime;
        private readonly TimeSpan _refreshLeadTime;

        public AuthentikSessionManager(
            IHttpContextAccessor httpContextAccessor,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<AuthentikSessionManager> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _stateProtector = dataProtectionProvider.CreateProtector(StatePurpose);

            var section = configuration.GetSection("AuthentikAuth");

            _serverBaseUrl = (
                Environment.GetEnvironmentVariable("AUTHENTIK_BASE_URL")
                ?? section["BaseUrl"]
                ?? "http://localhost:9000").TrimEnd('/');

            _browserBaseUrl = (
                Environment.GetEnvironmentVariable("AUTHENTIK_BASE_URL_FOR_BROWSER")
                ?? section["BaseUrlForBrowser"]
                ?? section["BaseUrl"]
                ?? "http://localhost:9000").TrimEnd('/');
            _hostHeader = new Uri(_browserBaseUrl).Authority;

            _providerSlug = Environment.GetEnvironmentVariable("AUTHENTIK_PROVIDER_SLUG") ?? section["ProviderSlug"] ?? "be-oidc";
            _clientId = Environment.GetEnvironmentVariable("AUTHENTIK_CLIENT_ID") ?? section["ClientId"] ?? throw new InvalidOperationException("AuthentikAuth:ClientId is required.");
            _clientSecret = Environment.GetEnvironmentVariable("AUTHENTIK_CLIENT_SECRET") ?? section["ClientSecret"] ?? throw new InvalidOperationException("AuthentikAuth:ClientSecret is required.");
            // Первый разрешенный callback одинаков для startup-bootstrap и обмена authorization code.
            _redirectUri = Environment.GetEnvironmentVariable("AUTHENTIK_REDIRECT_URIS")?
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).FirstOrDefault(value => value.Length > 0)
                ?? section["RedirectUri"] ?? throw new InvalidOperationException("AuthentikAuth:RedirectUri is required.");
            _scope = section["Scope"] ?? "openid profile email";
            _authenticationFlowSlug = Environment.GetEnvironmentVariable("AUTHENTIK_AUTHENTICATION_FLOW_SLUG") ?? section["AuthenticationFlowSlug"] ?? "default-authentication-flow";
            _sessionLifetime = TimeSpan.FromHours(ReadDouble(section["SessionLifetimeHours"], 8));
            _refreshLeadTime = TimeSpan.FromMinutes(ReadDouble(section["RefreshLeadTimeMinutes"], 5));
        }

        public Task<bool> IsUserAuthenticatedAsync()
            => Task.FromResult(_httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false);

        public Task<ClaimsPrincipal?> GetCurrentUserAsync()
            => Task.FromResult(_httpContextAccessor.HttpContext?.User);

        public Task<string?> GetUserNameAsync()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return Task.FromResult(
                user?.Identity?.Name
                ?? user?.FindFirst("preferred_username")?.Value
                ?? user?.FindFirst(ClaimTypes.Name)?.Value);
        }

        public Task<string?> GetUserEmailAsync()
        {
            var email = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Email)?.Value
                        ?? _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value;
            return Task.FromResult(email);
        }

        public async Task<string?> GetIdentityTokenAsync()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null)
            {
                return null;
            }

            var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return authResult.Properties?.GetTokenValue(IdTokenName);
        }

        public string GetLoginUrl(string? returnUrl = null)
        {
            var state = _stateProtector.Protect(JsonSerializer.Serialize(new AuthState(NormalizeReturnUrl(returnUrl), DateTimeOffset.UtcNow)));

            var query = new Dictionary<string, string?>
            {
                ["client_id"] = _clientId,
                ["redirect_uri"] = _redirectUri,
                ["response_type"] = "code",
                ["scope"] = _scope,
                ["state"] = state
            };

            return _browserBaseUrl + QueryHelpers.AddQueryString("/application/o/authorize/", query);
        }

        public async Task<string> CompleteLoginAsync(string code, string? state, CancellationToken cancellationToken = default)
        {
            var tokens = await ExchangeAuthorizationCodeAsync(code, cancellationToken);
            if (string.IsNullOrWhiteSpace(tokens.IdToken))
            {
                throw new InvalidOperationException("Authentik did not return an id_token.");
            }

            var principal = CreatePrincipal(tokens.IdToken);
            var properties = CreateAuthenticationProperties(tokens, DateTimeOffset.UtcNow, null, null, null);

            var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HTTP context is not available.");
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            context.User = principal;

            return ReadReturnUrl(state);
        }

        public async Task<string> CompletePasswordLoginAsync(
            string username,
            string password,
            string? returnUrl,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("Username is required.", nameof(username));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Password is required.", nameof(password));
            }

            var authentikUser = await AuthenticateWithPasswordFlowAsync(username, password, cancellationToken);
            var principal = CreatePrincipal(authentikUser);
            var properties = CreatePasswordFlowAuthenticationProperties(DateTimeOffset.UtcNow);

            var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HTTP context is not available.");
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            context.User = principal;

            return NormalizeReturnUrl(returnUrl);
        }

        // Проверяет логин и пароль через Authentik flow без изменения текущей локальной сессии.
        public async Task<bool> ValidatePasswordAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            try
            {
                await AuthenticateWithPasswordFlowAsync(username, password, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Authentik password validation failed for {Username}.", username);
                return false;
            }
        }

        public async Task<string> SignOutAsync(CancellationToken cancellationToken = default)
        {
            var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("HTTP context is not available.");
            var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var idToken = authResult.Properties?.GetTokenValue(IdTokenName);

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            if (string.IsNullOrWhiteSpace(idToken))
            {
                return "/auth/logged-out";
            }

            return BuildFrontChannelLogoutUrl(idToken);
        }

        public async Task RefreshSessionAsync(CookieValidatePrincipalContext context)
        {
            if (context.Principal?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var sessionExpiresUtc = context.Properties.ExpiresUtc;
            if (sessionExpiresUtc == null || sessionExpiresUtc <= now)
            {
                _logger.LogInformation("Authentication cookie expired; signing user out.");
                await RejectSessionAsync(context);
                return;
            }

            var refreshToken = context.Properties.GetTokenValue(RefreshTokenName);
            var currentIdToken = context.Properties.GetTokenValue(IdTokenName);
            var accessTokenExpiresAt = ParseTokenExpiration(context.Properties.GetTokenValue(AccessTokenExpiresAtName));

            if (accessTokenExpiresAt == null)
            {
                if (IsPasswordFlowSession(context.Properties))
                {
                    return;
                }

                _logger.LogWarning("Authentication cookie does not contain access token expiry; signing user out.");
                await RejectSessionAsync(context);
                return;
            }

            if (accessTokenExpiresAt > now.Add(_refreshLeadTime))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                _logger.LogInformation("Refresh token is missing; signing user out.");
                await RejectSessionAsync(context);
                return;
            }

            try
            {
                var refreshedTokens = await RefreshTokensAsync(refreshToken, context.HttpContext.RequestAborted);
                var idToken = string.IsNullOrWhiteSpace(refreshedTokens.IdToken) ? currentIdToken : refreshedTokens.IdToken;
                if (string.IsNullOrWhiteSpace(idToken))
                {
                    throw new InvalidOperationException("No id_token is available after refresh.");
                }

                var nextRefreshToken = string.IsNullOrWhiteSpace(refreshedTokens.RefreshToken) ? refreshToken : refreshedTokens.RefreshToken;
                var principal = CreatePrincipal(idToken);
                var refreshedProperties = CreateAuthenticationProperties(
                    refreshedTokens with { IdToken = idToken, RefreshToken = nextRefreshToken },
                    now,
                    sessionExpiresUtc,
                    nextRefreshToken,
                    idToken);

                context.ReplacePrincipal(principal);
                context.Properties.IsPersistent = refreshedProperties.IsPersistent;
                context.Properties.AllowRefresh = refreshedProperties.AllowRefresh;
                context.Properties.ExpiresUtc = refreshedProperties.ExpiresUtc;
                context.Properties.IssuedUtc = refreshedProperties.IssuedUtc;
                context.Properties.RedirectUri = refreshedProperties.RedirectUri;
                context.Properties.StoreTokens(refreshedProperties.GetTokens());
                context.ShouldRenew = true;

                _logger.LogDebug("Authentication session refreshed for {User}.", principal.Identity?.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh Authentik session.");
                await RejectSessionAsync(context);
            }
        }

        private async Task<TokenSet> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken)
        {
            using var request = CreateTokenRequest(
                new Dictionary<string, string?>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = _redirectUri,
                    ["client_id"] = _clientId
                });

            return await SendTokenRequestAsync(request, cancellationToken);
        }

        private async Task<AuthentikFlowUser> AuthenticateWithPasswordFlowAsync(
            string username,
            string password,
            CancellationToken cancellationToken)
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                CookieContainer = new CookieContainer(),
                UseCookies = true
            };
            using var client = CreateFlowClient(handler);

            var flowUri = $"/api/v3/flows/executor/{Uri.EscapeDataString(_authenticationFlowSlug)}/";

            using (var initialRequest = new HttpRequestMessage(HttpMethod.Get, flowUri))
            {
                using var initialResponse = await client.SendAsync(initialRequest, cancellationToken);
                using var initialDocument = await ReadFlowResponseAsync(initialResponse, "получить форму логина Authentik", cancellationToken);
            }

            using var identificationChallenge = await PostFlowChallengeAsync(
                client,
                handler.CookieContainer,
                flowUri,
                new
                {
                    component = IdentificationStageComponent,
                    uid_field = username
                },
                "передать логин в Authentik",
                cancellationToken);
            EnsureExpectedFlowComponent(identificationChallenge, PasswordStageComponent, "Authentik did not accept the username.");

            using var passwordChallenge = await PostFlowChallengeAsync(
                client,
                handler.CookieContainer,
                flowUri,
                new
                {
                    component = PasswordStageComponent,
                    password
                },
                "передать пароль в Authentik",
                cancellationToken);
            EnsureExpectedFlowComponent(passwordChallenge, FlowRedirectComponent, "Authentik rejected the supplied credentials.");

            return await ReadCurrentFlowUserAsync(client, cancellationToken);
        }

        private async Task<TokenSet> RefreshTokensAsync(string refreshToken, CancellationToken cancellationToken)
        {
            using var request = CreateTokenRequest(
                new Dictionary<string, string?>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken,
                    ["client_id"] = _clientId
                });

            return await SendTokenRequestAsync(request, cancellationToken);
        }

        private async Task<TokenSet> SendTokenRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Authentik token endpoint returned {(int)response.StatusCode}: {responseBody}");
            }

            using var json = JsonDocument.Parse(responseBody);
            var root = json.RootElement;

            return new TokenSet(
                root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Token response does not contain access_token."),
                root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null,
                root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : null,
                root.GetProperty("expires_in").GetInt32());
        }

        private HttpRequestMessage CreateTokenRequest(IDictionary<string, string?> form)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/application/o/token/");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
            request.Content = new FormUrlEncodedContent(form!);
            return request;
        }

        private HttpClient CreateFlowClient(HttpClientHandler handler)
        {
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(_serverBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Host = _hostHeader;
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private async Task<JsonDocument> PostFlowChallengeAsync(
            HttpClient client,
            CookieContainer cookieContainer,
            string flowUri,
            object payload,
            string operation,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, flowUri);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var csrfToken = ReadCsrfToken(cookieContainer, client.BaseAddress);
            if (!string.IsNullOrWhiteSpace(csrfToken))
            {
                request.Headers.Add("X-CSRFToken", csrfToken);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            return await ReadFlowResponseAsync(response, operation, cancellationToken);
        }

        private async Task<JsonDocument> ReadFlowResponseAsync(
            HttpResponseMessage response,
            string operation,
            CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Не удалось {operation}. Authentik вернул {(int)response.StatusCode}: {body}");
            }

            return JsonDocument.Parse(body);
        }

        private async Task<AuthentikFlowUser> ReadCurrentFlowUserAsync(
            HttpClient client,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v3/core/users/me/");
            using var response = await client.SendAsync(request, cancellationToken);
            using var document = await ReadFlowResponseAsync(response, "получить текущего пользователя Authentik", cancellationToken);

            var user = document.RootElement.TryGetProperty("user", out var userElement)
                ? userElement
                : throw new InvalidOperationException("Authentik did not return current user data.");

            var groups = new List<string>();
            if (user.TryGetProperty("groups", out var groupsElement) && groupsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in groupsElement.EnumerateArray())
                {
                    var groupName = ReadJsonString(group, "name");
                    if (!string.IsNullOrWhiteSpace(groupName))
                    {
                        groups.Add(groupName);
                    }
                }
            }

            return new AuthentikFlowUser(
                ReadJsonInt(user, "pk"),
                ReadJsonString(user, "username"),
                ReadJsonString(user, "name"),
                ReadJsonString(user, "uid"),
                ReadJsonString(user, "email"),
                ReadJsonString(user, "type"),
                groups
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        private ClaimsPrincipal CreatePrincipal(string idToken)
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
            var subject = jwt.Subject ?? Guid.NewGuid().ToString("N");
            var userName =
                jwt.Claims.FirstOrDefault(claim => claim.Type == "preferred_username")?.Value
                ?? jwt.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name)?.Value
                ?? subject;
            var email =
                jwt.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value
                ?? jwt.Claims.FirstOrDefault(claim => claim.Type == "email")?.Value;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, subject),
                new(ClaimTypes.Name, userName)
            };

            if (!string.IsNullOrWhiteSpace(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
            }

            foreach (var claim in jwt.Claims)
            {
                if (claims.Any(existing => existing.Type == claim.Type && existing.Value == claim.Value))
                {
                    continue;
                }

                claims.Add(claim);
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private ClaimsPrincipal CreatePrincipal(AuthentikFlowUser user)
        {
            if (string.IsNullOrWhiteSpace(user.Uid))
            {
                throw new InvalidOperationException("Authentik user does not contain uid.");
            }

            var userName = string.IsNullOrWhiteSpace(user.Username) ? user.Uid : user.Username;
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Uid),
                new("sub", user.Uid),
                new(ClaimTypes.Name, userName),
                new("preferred_username", userName),
                new("authentik_user_pk", user.Pk.ToString(CultureInfo.InvariantCulture)),
                new("authentik_user_type", user.Type)
            };

            if (!string.IsNullOrWhiteSpace(user.Name))
            {
                claims.Add(new Claim("name", user.Name));
            }

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, user.Email));
                claims.Add(new Claim("email", user.Email));
            }

            if (user.Groups.Count > 0)
            {
                claims.Add(new Claim("groups", JsonSerializer.Serialize(user.Groups)));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private AuthenticationProperties CreateAuthenticationProperties(
            TokenSet tokens,
            DateTimeOffset now,
            DateTimeOffset? sessionExpiresUtc,
            string? fallbackRefreshToken,
            string? fallbackIdToken)
        {
            var idToken = string.IsNullOrWhiteSpace(tokens.IdToken) ? fallbackIdToken : tokens.IdToken;
            var refreshToken = string.IsNullOrWhiteSpace(tokens.RefreshToken) ? fallbackRefreshToken : tokens.RefreshToken;

            var storedTokens = new List<AuthenticationToken>
            {
                new() { Name = AccessTokenName, Value = tokens.AccessToken },
                new() { Name = AccessTokenExpiresAtName, Value = now.AddSeconds(tokens.ExpiresIn).ToString("O", CultureInfo.InvariantCulture) }
            };

            if (!string.IsNullOrWhiteSpace(idToken))
            {
                storedTokens.Add(new AuthenticationToken { Name = IdTokenName, Value = idToken });
            }

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                storedTokens.Add(new AuthenticationToken { Name = RefreshTokenName, Value = refreshToken });
            }

            var properties = new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                ExpiresUtc = sessionExpiresUtc ?? now.Add(_sessionLifetime)
            };

            properties.StoreTokens(storedTokens);
            return properties;
        }

        private AuthenticationProperties CreatePasswordFlowAuthenticationProperties(DateTimeOffset now)
        {
            var properties = new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                IssuedUtc = now,
                ExpiresUtc = now.Add(_sessionLifetime)
            };

            properties.Items[SessionModeProperty] = PasswordFlowSessionMode;
            return properties;
        }

        private string BuildFrontChannelLogoutUrl(string idToken)
        {
            var postLogoutRedirectUri = new Uri(new Uri(_redirectUri), "/auth/logged-out").ToString();

            var query = new Dictionary<string, string?>
            {
                ["client_id"] = _clientId,
                ["post_logout_redirect_uri"] = postLogoutRedirectUri,
                ["id_token_hint"] = idToken
            };

            return _browserBaseUrl + QueryHelpers.AddQueryString($"/application/o/{_providerSlug}/end-session/", query);
        }

        private string ReadReturnUrl(string? state)
        {
            if (string.IsNullOrWhiteSpace(state))
            {
                return "/";
            }

            try
            {
                var payload = JsonSerializer.Deserialize<AuthState>(_stateProtector.Unprotect(state));
                if (payload == null)
                {
                    return "/";
                }

                if (DateTimeOffset.UtcNow - payload.IssuedUtc > StateLifetime)
                {
                    _logger.LogInformation("Protected auth state expired; returning to root.");
                    return "/";
                }

                return NormalizeReturnUrl(payload.ReturnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read auth state; returning to root.");
                return "/";
            }
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

            return returnUrl;
        }

        private static void EnsureExpectedFlowComponent(
            JsonDocument document,
            string expectedComponent,
            string errorMessage)
        {
            var component = document.RootElement.TryGetProperty("component", out var componentElement)
                ? componentElement.GetString()
                : null;

            if (string.Equals(component, expectedComponent, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(component, FlowErrorComponent, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(errorMessage);
            }

            throw new InvalidOperationException(errorMessage);
        }

        private static string? ReadCsrfToken(CookieContainer cookieContainer, Uri? baseAddress)
        {
            if (baseAddress == null)
            {
                return null;
            }

            foreach (Cookie cookie in cookieContainer.GetCookies(baseAddress))
            {
                if (cookie.Name.Contains("csrf", StringComparison.OrdinalIgnoreCase))
                {
                    return cookie.Value;
                }
            }

            return null;
        }

        private static bool IsPasswordFlowSession(AuthenticationProperties properties)
        {
            return properties.Items.TryGetValue(SessionModeProperty, out var sessionMode)
                   && string.Equals(sessionMode, PasswordFlowSessionMode, StringComparison.Ordinal);
        }

        private static string ReadJsonString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static int ReadJsonInt(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : 0;
        }

        private static DateTimeOffset? ParseTokenExpiration(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
                ? result
                : null;
        }

        private static double ReadDouble(string? value, double defaultValue)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static async Task RejectSessionAsync(CookieValidatePrincipalContext context)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        private sealed record AuthState(string ReturnUrl, DateTimeOffset IssuedUtc);

        private sealed record TokenSet(string AccessToken, string? IdToken, string? RefreshToken, int ExpiresIn);

        private sealed record AuthentikFlowUser(
            int Pk,
            string Username,
            string Name,
            string Uid,
            string Email,
            string Type,
            IReadOnlyList<string> Groups);
    }
}
