using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace BusinessEntity.Services;

// Донастраивает свежий Authentik под OIDC-интеграцию приложения.
public sealed class AuthentikBootstrapService
{
    private const string HttpClientName = "AuthentikAuth";
    private const string DefaultApplicationName = "BusinessEntity";
    private const string DefaultProviderSlug = "be-oidc";
    private const string DefaultApplicationUsersGroupName = "GeoUsers";
    private const string DefaultAuthorizationFlowSlug = "default-provider-authorization-implicit-consent";
    private const string DefaultInvalidationFlowSlug = "default-provider-invalidation-flow";
    private const string DefaultAuthenticationFlowSlug = "default-authentication-flow";
    private const string GroupsScopeMappingName = "BusinessEntity OAuth Mapping: groups";
    private const int MaxBootstrapAttempts = 36;
    private static readonly TimeSpan BootstrapAttemptDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthentikBootstrapService> _logger;

    // Получает HTTP-клиент, настройки установки и логгер bootstrap.
    public AuthentikBootstrapService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AuthentikBootstrapService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    // Запускает bootstrap только при явном EnsureAuthentikOnStartup=true.
    public async Task EnsureAsync(CancellationToken stoppingToken = default)
    {
        if (!IsBootstrapEnabled())
        {
            _logger.LogInformation("Authentik bootstrap is disabled.");
            return;
        }

        var apiToken = ReadSetting("AUTHENTIK_API_TOKEN", "AuthentikAuth:ApiToken", "AuthentikAuth:ManagementApiToken");
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            _logger.LogCritical("Authentik bootstrap is enabled, but AUTHENTIK_API_TOKEN or AuthentikAuth:ApiToken is empty.");
            throw new InvalidOperationException(
                "Authentik bootstrap is enabled, but AUTHENTIK_API_TOKEN or AuthentikAuth:ApiToken is empty.");
        }

        try
        {
            await ExecuteBootstrapWithRetryAsync(apiToken, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Authentik bootstrap failed. BusinessEntity installation cannot continue.");
            throw;
        }
    }

    // Ожидает bootstrap token и стандартные blueprints Authentik перед настройкой OIDC.
    private async Task ExecuteBootstrapWithRetryAsync(string apiToken, CancellationToken cancellationToken)
    {
        using var client = CreateApiClient(apiToken);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxBootstrapAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await WaitForLiveEndpointAsync(client, cancellationToken);
                await ExecuteBootstrapAsync(client, cancellationToken);
                _logger.LogInformation("Authentik bootstrap completed.");
                return;
            }
            catch (Exception ex) when (attempt < MaxBootstrapAttempts && !cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                _logger.LogWarning(
                    ex,
                    "Authentik bootstrap attempt {Attempt}/{MaxAttempts} failed; retrying.",
                    attempt,
                    MaxBootstrapAttempts);
                await Task.Delay(BootstrapAttemptDelay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Authentik bootstrap retry limit reached.", lastError);
    }

    // Создает application group, provider и application. Пользователей и админские группы ведет UserMiniApp.
    private async Task ExecuteBootstrapAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var applicationName = ReadSetting("AUTHENTIK_APPLICATION_NAME", "AuthentikAuth:ApplicationName") ?? DefaultApplicationName;
        var providerSlug = ReadSetting("AUTHENTIK_PROVIDER_SLUG", "AuthentikAuth:ProviderSlug") ?? DefaultProviderSlug;
        var clientId = ReadRequiredSetting("AUTHENTIK_CLIENT_ID", "AuthentikAuth:ClientId");
        var clientSecret = ReadRequiredSetting("AUTHENTIK_CLIENT_SECRET", "AuthentikAuth:ClientSecret");
        var redirectUris = ReadRedirectUris();
        var applicationGroupName =
            ReadSetting("AUTHENTIK_APPLICATION_USERS_GROUP", "AuthentikAuth:ApplicationUsersGroupName")
            ?? DefaultApplicationUsersGroupName;
        var authenticationFlowSlug =
            ReadSetting("AUTHENTIK_AUTHENTICATION_FLOW_SLUG", "AuthentikAuth:AuthenticationFlowSlug")
            ?? DefaultAuthenticationFlowSlug;
        var authorizationFlowSlug =
            ReadSetting("AUTHENTIK_AUTHORIZATION_FLOW_SLUG", "AuthentikAuth:AuthorizationFlowSlug")
            ?? DefaultAuthorizationFlowSlug;
        var invalidationFlowSlug =
            ReadSetting("AUTHENTIK_INVALIDATION_FLOW_SLUG", "AuthentikAuth:InvalidationFlowSlug")
            ?? DefaultInvalidationFlowSlug;

        await EnsureGroupAsync(client, applicationGroupName, cancellationToken);

        var authenticationFlowPk = await ReadFlowPkAsync(client, authenticationFlowSlug, cancellationToken);
        var authorizationFlowPk = await ReadFlowPkAsync(client, authorizationFlowSlug, cancellationToken);
        var invalidationFlowPk = await ReadFlowPkAsync(client, invalidationFlowSlug, cancellationToken);
        var scopeMappings = await EnsureScopeMappingsAsync(client, cancellationToken);
        var providerPk = await EnsureOAuthProviderAsync(
            client,
            applicationName,
            providerSlug,
            clientId,
            clientSecret,
            redirectUris,
            authenticationFlowPk,
            authorizationFlowPk,
            invalidationFlowPk,
            scopeMappings,
            cancellationToken);

        await EnsureApplicationAsync(
            client,
            applicationName,
            providerSlug,
            providerPk,
            BuildLaunchUrl(redirectUris),
            cancellationToken);
    }

    // Создает API-клиент с bootstrap token без вывода секрета в лог.
    private HttpClient CreateApiClient(string apiToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // Проверяет готовность HTTP API Authentik.
    private async Task WaitForLiveEndpointAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/-/health/live/", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Создает группу пользователей приложения, если ее еще нет.
    private async Task<AuthentikGroup> EnsureGroupAsync(
        HttpClient client,
        string groupName,
        CancellationToken cancellationToken)
    {
        var existing = await FindGroupAsync(client, groupName, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        using var document = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v3/core/groups/",
            new Dictionary<string, object?>
            {
                ["name"] = groupName,
                ["is_superuser"] = false,
                ["attributes"] = new Dictionary<string, object?>()
            },
            cancellationToken);

        return ReadGroup(document.RootElement);
    }

    // Находит группу по точному имени.
    private async Task<AuthentikGroup?> FindGroupAsync(
        HttpClient client,
        string groupName,
        CancellationToken cancellationToken)
    {
        var uri = QueryHelpers.AddQueryString(
            "/api/v3/core/groups/",
            new Dictionary<string, string?>
            {
                ["search"] = groupName,
                ["page_size"] = "100"
            });

        using var document = await SendAsync(client, HttpMethod.Get, uri, cancellationToken);
        foreach (var group in document.RootElement.GetProperty("results").EnumerateArray())
        {
            if (string.Equals(ReadString(group, "name"), groupName, StringComparison.Ordinal))
            {
                return ReadGroup(group);
            }
        }

        return null;
    }

    // Получает обязательный flow после загрузки стандартных blueprints.
    private async Task<string> ReadFlowPkAsync(
        HttpClient client,
        string flowSlug,
        CancellationToken cancellationToken)
    {
        return await TryReadFlowPkAsync(client, flowSlug, cancellationToken)
               ?? throw new InvalidOperationException($"Authentik flow '{flowSlug}' was not found.");
    }

    // Читает flow по slug; отсутствие позволяет повторить bootstrap после загрузки blueprints.
    private async Task<string?> TryReadFlowPkAsync(
        HttpClient client,
        string flowSlug,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/v3/flows/instances/{Uri.EscapeDataString(flowSlug)}/",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        using var document = await ReadSuccessfulDocumentAsync(response, cancellationToken);
        return ReadString(document.RootElement, "pk");
    }

    // Подключает стандартные OIDC claims и mapping групп пользователя.
    private async Task<IReadOnlyList<string>> EnsureScopeMappingsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var mappings = await ReadScopeMappingsAsync(client, cancellationToken);
        var requiredScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "openid",
            "profile",
            "email",
            "groups"
        };

        // Стандартные scope mappings появляются асинхронно вместе с default blueprints.
        if (new[] { "openid", "profile", "email" }.Any(scope =>
                !mappings.Any(mapping => string.Equals(mapping.ScopeName, scope, StringComparison.Ordinal))))
        {
            throw new InvalidOperationException("Authentik default OIDC scope mappings are not ready.");
        }

        var selected = mappings
            .Where(mapping => requiredScopes.Contains(mapping.ScopeName))
            .Select(mapping => mapping.Pk)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!mappings.Any(mapping => string.Equals(mapping.ScopeName, "groups", StringComparison.OrdinalIgnoreCase)))
        {
            using var createdGroupsMapping = await SendJsonAsync(
                client,
                HttpMethod.Post,
                "/api/v3/propertymappings/provider/scope/",
                new Dictionary<string, object?>
                {
                    ["name"] = GroupsScopeMappingName,
                    ["scope_name"] = "groups",
                    ["description"] = "Adds Authentik group names to BusinessEntity OIDC tokens.",
                    ["expression"] = "return {\"groups\": [group.name for group in request.user.groups.all()]}"
                },
                cancellationToken);
            selected.Add(ReadString(createdGroupsMapping.RootElement, "pk"));
        }

        return selected;
    }

    // Читает все страницы доступных scope mappings.
    private async Task<IReadOnlyList<ScopeMapping>> ReadScopeMappingsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var mappings = new List<ScopeMapping>();
        var nextUri = "/api/v3/propertymappings/provider/scope/?page_size=100";

        while (!string.IsNullOrWhiteSpace(nextUri))
        {
            using var document = await SendAsync(client, HttpMethod.Get, nextUri, cancellationToken);
            foreach (var mapping in document.RootElement.GetProperty("results").EnumerateArray())
            {
                mappings.Add(new ScopeMapping(
                    ReadString(mapping, "pk"),
                    ReadString(mapping, "name"),
                    ReadString(mapping, "scope_name")));
            }

            nextUri = ReadNextPageUri(document.RootElement, nextUri);
        }

        return mappings;
    }

    // Создает или обновляет OIDC-провайдер без удаления его идентификатора.
    private async Task<int> EnsureOAuthProviderAsync(
        HttpClient client,
        string applicationName,
        string providerSlug,
        string clientId,
        string clientSecret,
        IReadOnlyList<string> redirectUris,
        string? authenticationFlowPk,
        string authorizationFlowPk,
        string invalidationFlowPk,
        IReadOnlyList<string> scopeMappings,
        CancellationToken cancellationToken)
    {
        var existingProviderPk = await TryFindProviderPkAsync(client, providerSlug, clientId, cancellationToken);
        var payload = BuildProviderPayload(
            applicationName,
            clientId,
            clientSecret,
            redirectUris,
            authenticationFlowPk,
            authorizationFlowPk,
            invalidationFlowPk,
            scopeMappings);

        if (existingProviderPk > 0)
        {
            using var updated = await SendJsonAsync(
                client,
                new HttpMethod("PATCH"),
                $"/api/v3/providers/oauth2/{existingProviderPk}/",
                payload,
                cancellationToken);
            return ReadInt(updated.RootElement, "pk");
        }

        using var created = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v3/providers/oauth2/",
            payload,
            cancellationToken);
        return ReadInt(created.RootElement, "pk");
    }

    // Находит провайдер приложения либо провайдер с заданным client_id.
    private async Task<int> TryFindProviderPkAsync(
        HttpClient client,
        string providerSlug,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var application = await TryReadApplicationAsync(client, providerSlug, cancellationToken);
        if (application != null && application.RootElement.TryGetProperty("provider", out var providerElement))
        {
            var providerPk = ReadInt(providerElement);
            if (providerPk > 0)
            {
                return providerPk;
            }
        }

        var nextUri = QueryHelpers.AddQueryString(
            "/api/v3/providers/oauth2/",
            new Dictionary<string, string?>
            {
                ["page_size"] = "100",
                ["search"] = clientId
            });

        while (!string.IsNullOrWhiteSpace(nextUri))
        {
            using var document = await SendAsync(client, HttpMethod.Get, nextUri, cancellationToken);
            foreach (var provider in document.RootElement.GetProperty("results").EnumerateArray())
            {
                if (string.Equals(ReadString(provider, "client_id"), clientId, StringComparison.Ordinal))
                {
                    return ReadInt(provider, "pk");
                }
            }

            nextUri = ReadNextPageUri(document.RootElement, nextUri);
        }

        return 0;
    }

    // Собирает параметры confidential OIDC-провайдера и разрешенных callback URL.
    private static Dictionary<string, object?> BuildProviderPayload(
        string applicationName,
        string clientId,
        string clientSecret,
        IReadOnlyList<string> redirectUris,
        string? authenticationFlowPk,
        string authorizationFlowPk,
        string invalidationFlowPk,
        IReadOnlyList<string> scopeMappings)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = applicationName + " OIDC",
            ["authorization_flow"] = authorizationFlowPk,
            ["invalidation_flow"] = invalidationFlowPk,
            ["client_type"] = "confidential",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uris"] = redirectUris
                .Select(uri => new Dictionary<string, string>
                {
                    ["matching_mode"] = "strict",
                    ["url"] = uri,
                    ["redirect_uri_type"] = "authorization"
                })
                .ToList(),
            ["sub_mode"] = "hashed_user_id",
            ["issuer_mode"] = "per_provider",
            ["include_claims_in_id_token"] = true,
            ["property_mappings"] = scopeMappings
        };

        if (!string.IsNullOrWhiteSpace(authenticationFlowPk))
        {
            payload["authentication_flow"] = authenticationFlowPk;
        }

        return payload;
    }

    // Создает или обновляет приложение и его группу в каталоге Authentik.
    private async Task EnsureApplicationAsync(
        HttpClient client,
        string applicationName,
        string providerSlug,
        int providerPk,
        string launchUrl,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["name"] = applicationName,
            ["slug"] = providerSlug,
            ["provider"] = providerPk,
            ["group"] = applicationName,
            ["open_in_new_tab"] = false,
            ["meta_launch_url"] = launchUrl,
            ["meta_description"] = applicationName,
            ["meta_publisher"] = applicationName,
            ["policy_engine_mode"] = "all"
        };

        var existing = await TryReadApplicationAsync(client, providerSlug, cancellationToken);
        if (existing != null)
        {
            existing.Dispose();
            await SendJsonExpectSuccessAsync(
                client,
                new HttpMethod("PATCH"),
                $"/api/v3/core/applications/{Uri.EscapeDataString(providerSlug)}/",
                payload,
                cancellationToken);
            return;
        }

        await SendJsonExpectSuccessAsync(
            client,
            HttpMethod.Post,
            "/api/v3/core/applications/",
            payload,
            cancellationToken);
    }

    // Ищет приложение по постоянному slug.
    private async Task<JsonDocument?> TryReadApplicationAsync(
        HttpClient client,
        string providerSlug,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/api/v3/core/applications/{Uri.EscapeDataString(providerSlug)}/",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadSuccessfulDocumentAsync(response, cancellationToken);
    }

    // Выполняет запрос к API и читает JSON-ответ.
    private async Task<JsonDocument> SendAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadSuccessfulDocumentAsync(response, cancellationToken);
    }

    // Отправляет JSON и возвращает успешный JSON-ответ API.
    private async Task<JsonDocument> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        return await ReadSuccessfulDocumentAsync(response, cancellationToken);
    }

    // Отправляет JSON без требования непустого ответа.
    private async Task SendJsonExpectSuccessAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // Проверяет HTTP-результат и читает JSON без логирования секретов ответа.
    private static async Task<JsonDocument> ReadSuccessfulDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Authentik API {response.RequestMessage?.RequestUri?.AbsolutePath} returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        return string.IsNullOrWhiteSpace(body)
            ? JsonDocument.Parse("{}")
            : JsonDocument.Parse(body);
    }

    // Проверяет успешность изменения в Authentik.
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Authentik API {response.RequestMessage?.RequestUri?.AbsolutePath} returned {(int)response.StatusCode}.", null, response.StatusCode);
    }

    // Проверяет флаг автоматической настройки Authentik.
    private bool IsBootstrapEnabled()
    {
        var rawValue = ReadSetting("EnsureAuthentikOnStartup", "EnsureAuthentikOnStartup");
        return bool.TryParse(rawValue, out var parsed) && parsed;
    }

    // Читает обязательную настройку из окружения или конфигурации приложения.
    private string ReadRequiredSetting(string environmentName, string configurationName)
    {
        return ReadSetting(environmentName, configurationName)
               ?? throw new InvalidOperationException($"{environmentName} or {configurationName} is required.");
    }

    // Читает настройку с приоритетом переменных окружения.
    private string? ReadSetting(string environmentName, params string[] configurationNames)
    {
        var environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue;
        }

        return configurationNames
            .Select(name => _configuration[name])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    // Использует явно заданные callback URL или fallback из appsettings.
    private IReadOnlyList<string> ReadRedirectUris()
    {
        var values = new List<string>();
        var envRedirectUris = Environment.GetEnvironmentVariable("AUTHENTIK_REDIRECT_URIS");
        if (!string.IsNullOrWhiteSpace(envRedirectUris))
        {
            values.AddRange(envRedirectUris.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        var configuredRedirectUri = _configuration["AuthentikAuth:RedirectUri"];
        if (values.Count == 0 && !string.IsNullOrWhiteSpace(configuredRedirectUri))
        {
            values.Add(configuredRedirectUri);
        }

        var redirectUris = values.Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (redirectUris.Count == 0 || redirectUris.Any(value =>
                !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("Authentik requires at least one absolute HTTP(S) redirect URI.");
        }

        return redirectUris;
    }

    // Получает стартовую страницу приложения из первого callback URL.
    private static string BuildLaunchUrl(IReadOnlyList<string> redirectUris)
    {
        var firstRedirectUri = redirectUris.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstRedirectUri) ||
            !Uri.TryCreate(firstRedirectUri, UriKind.Absolute, out var uri))
        {
            return "/";
        }

        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }

    // Читает ссылку или номер следующей страницы Authentik API.
    private static string ReadNextPageUri(JsonElement root, string currentUri)
    {
        if (!root.TryGetProperty("pagination", out var pagination) ||
            !pagination.TryGetProperty("next", out var nextElement))
        {
            return string.Empty;
        }

        if (nextElement.ValueKind == JsonValueKind.Number)
        {
            var page = nextElement.GetInt32();
            return page > 0 ? ReplaceOrAddPage(currentUri, page) : string.Empty;
        }

        return nextElement.ValueKind == JsonValueKind.String
            ? nextElement.GetString() ?? string.Empty
            : string.Empty;
    }

    // Добавляет либо заменяет номер страницы в query string.
    private static string ReplaceOrAddPage(string uri, int page)
    {
        var pageText = page.ToString();
        if (uri.Contains("page=", StringComparison.Ordinal))
        {
            return System.Text.RegularExpressions.Regex.Replace(uri, @"([?&]page=)\d+", "${1}" + pageText);
        }

        return QueryHelpers.AddQueryString(uri, "page", pageText);
    }

    // Читает идентификатор и имя группы из ответа Authentik.
    private static AuthentikGroup ReadGroup(JsonElement group)
    {
        return new AuthentikGroup(ReadString(group, "pk"), ReadString(group, "name"));
    }

    // Читает строковое свойство JSON с пустым fallback.
    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    // Читает числовое свойство объекта Authentik.
    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? ReadInt(property)
            : 0;
    }

    // Принимает числовой идентификатор как число или строку.
    private static int ReadInt(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt32(),
            JsonValueKind.String => int.TryParse(element.GetString(), out var parsed) ? parsed : 0,
            _ => 0
        };
    }

    // Хранит идентификатор и имя группы Authentik.
    private sealed record AuthentikGroup(string Pk, string Name);

    // Хранит идентификатор и назначение scope mapping.
    private sealed record ScopeMapping(string Pk, string Name, string ScopeName);
}
