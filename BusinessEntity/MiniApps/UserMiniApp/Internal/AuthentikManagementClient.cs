using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Тонкий клиент Authentik Admin API для user mini-app.
internal sealed class AuthentikManagementClient
{
    private const string HttpClientName = "AuthentikAuth";
    private const string DefaultApplicationUsersGroupName = "GeoUsers";
    private const string DefaultGeneralAdminsGroupName = "BusinessEntityAdmins";
    private const string DefaultManagedUsernamePrefix = "user-";
    private const int DefaultGeneratedUsernameCodeLength = 5;
    private const int PageSize = 100;

    private static readonly char[] GeneratedCodeAlphabet = "abcdefghijklmnopqrstuvwxyz".ToCharArray();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiToken;
    private readonly string _applicationUsersGroupName;
    private readonly string _generalAdminsGroupName;
    private readonly string _managedUsernamePrefix;
    private readonly int _generatedUsernameCodeLength;

    public AuthentikManagementClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;

        var section = configuration.GetSection("AuthentikAuth");
        _apiToken = Environment.GetEnvironmentVariable("AUTHENTIK_API_TOKEN")
                    ?? section["ApiToken"]
                    ?? section["ManagementApiToken"]
                    ?? string.Empty;
        _applicationUsersGroupName = Environment.GetEnvironmentVariable("AUTHENTIK_APPLICATION_USERS_GROUP")
                                     ?? section["ApplicationUsersGroupName"]
                                     ?? DefaultApplicationUsersGroupName;
        _generalAdminsGroupName = Environment.GetEnvironmentVariable("AUTHENTIK_GENERAL_ADMINS_GROUP")
                                   ?? section["GeneralAdminsGroupName"]
                                   ?? DefaultGeneralAdminsGroupName;
        _managedUsernamePrefix = section["ManagedUsernamePrefix"] ?? DefaultManagedUsernamePrefix;
        _generatedUsernameCodeLength = Math.Clamp(
            ReadConfiguredInt(section["GeneratedUsernameCodeLength"], DefaultGeneratedUsernameCodeLength),
            1,
            16);
    }

    // Возвращает true, если для Authentik Admin API настроен bearer token.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiToken);

    // Возвращает configured group пользователей приложения.
    public string ApplicationUsersGroupName => _applicationUsersGroupName;

    // Возвращает configured group общего административного доступа.
    public string GeneralAdminsGroupName => _generalAdminsGroupName;

    // Возвращает пользователей Authentik, допущенных к приложению через configured group.
    public async Task<IReadOnlyList<AuthentikUserRecord>> GetApplicationUsersAsync(CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string?>
        {
            ["page_size"] = PageSize.ToString(),
            ["type"] = "internal"
        };

        if (!string.IsNullOrWhiteSpace(_applicationUsersGroupName))
        {
            query["groups_by_name"] = _applicationUsersGroupName;
        }

        return await ReadPagedUsersAsync("/api/v3/core/users/" + QueryHelpers.AddQueryString(string.Empty, query), cancellationToken);
    }

    // Создает или чинит внутреннего пользователя Authentik с нужными группами.
    public async Task<AuthentikUserRecord> EnsureInternalUserAsync(
        string username,
        string password,
        IEnumerable<string> groupNames,
        CancellationToken cancellationToken)
    {
        username = NormalizeRequiredText(username, nameof(username));
        var normalizedPassword = NormalizeRequiredText(password, nameof(password));
        var groups = new List<AuthentikGroupRecord>();
        foreach (var groupName in groupNames
                     .Where(groupName => !string.IsNullOrWhiteSpace(groupName))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            groups.Add(await EnsureGroupAsync(groupName, cancellationToken));
        }

        var user = await FindInternalUserByUsernameAsync(username, cancellationToken);
        if (user == null)
        {
            user = await CreateInternalUserAsync(username, groups, cancellationToken);
            await SetPasswordAsync(user.Pk, normalizedPassword, cancellationToken);
            return await GetUserAsync(user.Pk, cancellationToken);
        }

        if (!user.IsActive || !string.Equals(user.Name, username, StringComparison.Ordinal))
        {
            user = await UpdateUserBasicsAsync(user, username, cancellationToken);
        }

        foreach (var group in groups)
        {
            await AddUserToGroupAsync(group.Pk, user.Pk, cancellationToken);
        }

        if (!HasPasswordConfigured(user))
        {
            await SetPasswordAsync(user.Pk, normalizedPassword, cancellationToken);
            user = await GetUserAsync(user.Pk, cancellationToken);
        }

        return user;
    }

    // Создает Authentik-пользователя приложения с системным username user-[5 букв].
    public async Task<AuthentikUserRecord> CreateApplicationUserAsync(
        IEnumerable<string> reservedUsernames,
        CancellationToken cancellationToken)
    {
        var reservedNames = reservedUsernames
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = new List<string>();

        var applicationGroupPk = await TryGetApplicationGroupPkAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(applicationGroupPk))
        {
            groups.Add(applicationGroupPk);
        }

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var username = _managedUsernamePrefix + GenerateCode(_generatedUsernameCodeLength);
            if (reservedNames.Contains(username))
            {
                continue;
            }

            var payload = new
            {
                username,
                name = username,
                is_active = true,
                groups,
                email = string.Empty,
                path = "users",
                type = "internal",
                attributes = new { }
            };

            using var request = CreateJsonRequest(HttpMethod.Post, "/api/v3/core/users/", payload);
            using var response = await SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode && IsUsernameConflict(response, body))
            {
                reservedNames.Add(username);
                continue;
            }

            EnsureSuccess(response, body, "создать пользователя в Authentik");

            using var document = JsonDocument.Parse(body);
            return ReadUser(document.RootElement);
        }

        throw new InvalidOperationException("Не удалось подобрать свободный логин Authentik для нового пользователя.");
    }

    // Ищет внутреннего пользователя Authentik по точному username.
    public async Task<AuthentikUserRecord?> FindInternalUserByUsernameAsync(
        string username,
        CancellationToken cancellationToken)
    {
        username = NormalizeRequiredText(username, nameof(username));
        var query = new Dictionary<string, string?>
        {
            ["page_size"] = PageSize.ToString(),
            ["type"] = "internal",
            ["username"] = username
        };

        var users = await ReadPagedUsersAsync("/api/v3/core/users/" + QueryHelpers.AddQueryString(string.Empty, query), cancellationToken);
        return users.FirstOrDefault(user => string.Equals(user.Username, username, StringComparison.Ordinal));
    }

    // Возвращает пользователя Authentik по числовому pk.
    public async Task<AuthentikUserRecord> GetUserAsync(int authentikUserPk, CancellationToken cancellationToken)
    {
        if (authentikUserPk <= 0)
        {
            throw new InvalidOperationException("У пользователя нет идентификатора Authentik.");
        }

        using var request = CreateRequest(HttpMethod.Get, $"/api/v3/core/users/{authentikUserPk}/");
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "получить пользователя из Authentik");

        using var document = JsonDocument.Parse(body);
        return ReadUser(document.RootElement);
    }

    // Создает или возвращает существующую группу Authentik по имени.
    public async Task<AuthentikGroupRecord> EnsureGroupAsync(
        string groupName,
        CancellationToken cancellationToken)
    {
        groupName = NormalizeRequiredText(groupName, nameof(groupName));
        var existingGroup = await FindGroupByNameAsync(groupName, cancellationToken);
        if (existingGroup != null)
        {
            return existingGroup;
        }

        var payload = new
        {
            name = groupName,
            is_superuser = false,
            parents = Array.Empty<string>(),
            users = Array.Empty<int>(),
            attributes = new { },
            roles = Array.Empty<string>()
        };

        using var request = CreateJsonRequest(HttpMethod.Post, "/api/v3/core/groups/", payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "создать группу в Authentik");

        using var document = JsonDocument.Parse(body);
        return ReadGroup(document.RootElement);
    }

    // Проверяет, состоит ли внутренний пользователь Authentik в группе с заданным именем.
    public async Task<bool> IsInternalUserInGroupAsync(
        string username,
        string groupName,
        CancellationToken cancellationToken)
    {
        username = NormalizeRequiredText(username, nameof(username));
        groupName = NormalizeRequiredText(groupName, nameof(groupName));
        var query = new Dictionary<string, string?>
        {
            ["page_size"] = PageSize.ToString(),
            ["type"] = "internal",
            ["username"] = username,
            ["groups_by_name"] = groupName
        };

        var users = await ReadPagedUsersAsync("/api/v3/core/users/" + QueryHelpers.AddQueryString(string.Empty, query), cancellationToken);
        return users.Any(user => string.Equals(user.Username, username, StringComparison.Ordinal));
    }

    // Добавляет пользователя в группу Authentik идемпотентно.
    public async Task EnsureUserInGroupAsync(
        string groupPk,
        int userPk,
        CancellationToken cancellationToken)
    {
        await AddUserToGroupAsync(groupPk, userPk, cancellationToken);
    }

    // Меняет username пользователя в Authentik и возвращает обновленную запись.
    public async Task<AuthentikUserRecord> UpdateUsernameAsync(
        int authentikUserPk,
        string username,
        CancellationToken cancellationToken)
    {
        if (authentikUserPk <= 0)
        {
            throw new InvalidOperationException("У пользователя нет идентификатора Authentik.");
        }

        var payload = new { username };
        using var request = CreateJsonRequest(new HttpMethod("PATCH"), $"/api/v3/core/users/{authentikUserPk}/", payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "изменить логин пользователя в Authentik");

        using var document = JsonDocument.Parse(body);
        return ReadUser(document.RootElement);
    }

    // Устанавливает новый пароль внутреннего Authentik-пользователя.
    public async Task SetPasswordAsync(
        int authentikUserPk,
        string password,
        CancellationToken cancellationToken)
    {
        if (authentikUserPk <= 0)
        {
            throw new InvalidOperationException("У пользователя нет идентификатора Authentik.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Пароль не может быть пустым.", nameof(password));
        }

        var payload = new { password };
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/v3/core/users/{authentikUserPk}/set_password/",
            payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "изменить пароль пользователя в Authentik");
    }

    // Удаляет пользователя из Authentik.
    public async Task DeleteUserAsync(int authentikUserPk, CancellationToken cancellationToken)
    {
        if (authentikUserPk <= 0)
        {
            throw new InvalidOperationException("У пользователя нет идентификатора Authentik.");
        }

        using var request = CreateRequest(HttpMethod.Delete, $"/api/v3/core/users/{authentikUserPk}/");
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // Повтор удаления уже отсутствующего внешнего пользователя завершает локальную очистку.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return;
        EnsureSuccess(response, body, "удалить пользователя из Authentik");
    }

    // Создает внутреннего Authentik-пользователя без пароля, пароль ставится отдельным endpoint.
    private async Task<AuthentikUserRecord> CreateInternalUserAsync(
        string username,
        IReadOnlyList<AuthentikGroupRecord> groups,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            username,
            name = username,
            is_active = true,
            groups = groups.Select(group => group.Pk).ToList(),
            email = string.Empty,
            path = "users",
            type = "internal",
            attributes = new { }
        };

        using var request = CreateJsonRequest(HttpMethod.Post, "/api/v3/core/users/", payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, $"создать пользователя '{username}' в Authentik");

        using var document = JsonDocument.Parse(body);
        return ReadUser(document.RootElement);
    }

    // Чинит базовые поля пользователя, не меняя username и пароль.
    private async Task<AuthentikUserRecord> UpdateUserBasicsAsync(
        AuthentikUserRecord user,
        string displayName,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            name = displayName,
            is_active = true
        };

        using var request = CreateJsonRequest(new HttpMethod("PATCH"), $"/api/v3/core/users/{user.Pk}/", payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "обновить базовые поля пользователя в Authentik");

        using var document = JsonDocument.Parse(body);
        return ReadUser(document.RootElement);
    }

    // Ищет группу Authentik по точному имени.
    private async Task<AuthentikGroupRecord?> FindGroupByNameAsync(
        string groupName,
        CancellationToken cancellationToken)
    {
        var query = QueryHelpers.AddQueryString(
            "/api/v3/core/groups/",
            new Dictionary<string, string?>
            {
                ["name"] = groupName,
                ["page_size"] = PageSize.ToString()
            });

        using var request = CreateRequest(HttpMethod.Get, query);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "получить группы из Authentik");

        using var document = JsonDocument.Parse(body);
        foreach (var group in document.RootElement.GetProperty("results").EnumerateArray())
        {
            var record = ReadGroup(group);
            if (string.Equals(record.Name, groupName, StringComparison.Ordinal))
            {
                return record;
            }
        }

        return null;
    }

    // Добавляет пользователя в группу Authentik идемпотентно.
    private async Task AddUserToGroupAsync(
        string groupPk,
        int userPk,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupPk) || userPk <= 0)
        {
            return;
        }

        var payload = new { pk = userPk };
        using var request = CreateJsonRequest(HttpMethod.Post, $"/api/v3/core/groups/{groupPk}/add_user/", payload);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode || IsAlreadyInGroup(response, body))
        {
            return;
        }

        EnsureSuccess(response, body, "добавить пользователя в группу Authentik");
    }

    private async Task<string?> TryGetApplicationGroupPkAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_applicationUsersGroupName))
        {
            return null;
        }

        var query = QueryHelpers.AddQueryString(
            "/api/v3/core/groups/",
            new Dictionary<string, string?>
            {
                ["search"] = _applicationUsersGroupName,
                ["page_size"] = PageSize.ToString()
            });

        using var request = CreateRequest(HttpMethod.Get, query);
        using var response = await SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, body, "получить группу пользователей приложения из Authentik");

        using var document = JsonDocument.Parse(body);
        foreach (var group in document.RootElement.GetProperty("results").EnumerateArray())
        {
            var name = ReadString(group, "name");
            if (string.Equals(name, _applicationUsersGroupName, StringComparison.Ordinal))
            {
                return ReadString(group, "pk");
            }
        }

        throw new InvalidOperationException($"Группа пользователей приложения '{_applicationUsersGroupName}' не найдена в Authentik.");
    }

    private async Task<IReadOnlyList<AuthentikUserRecord>> ReadPagedUsersAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        var users = new List<AuthentikUserRecord>();
        var nextUri = requestUri;

        while (!string.IsNullOrWhiteSpace(nextUri))
        {
            using var request = CreateRequest(HttpMethod.Get, nextUri);
            using var response = await SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, body, "получить пользователей из Authentik");

            using var document = JsonDocument.Parse(body);
            foreach (var user in document.RootElement.GetProperty("results").EnumerateArray())
            {
                var record = ReadUser(user);
                if (string.Equals(record.Type, "internal", StringComparison.OrdinalIgnoreCase))
                {
                    users.Add(record);
                }
            }

            var nextPage = document.RootElement.GetProperty("pagination").TryGetProperty("next", out var nextElement)
                ? nextElement.GetInt32()
                : 0;
            nextUri = nextPage > 0 ? ReplacePage(nextUri, nextPage) : string.Empty;
        }

        return users;
    }

    // Определяет ошибку уникальности username, чтобы подобрать другой случайный код без перечитывания пользователей.
    private static bool IsUsernameConflict(HttpResponseMessage response, string body)
    {
        return (int)response.StatusCode == 400 &&
               body.Contains("username", StringComparison.OrdinalIgnoreCase);
    }

    // Определяет, что Authentik отказал в добавлении пользователя, потому что связь уже есть.
    private static bool IsAlreadyInGroup(HttpResponseMessage response, string body)
    {
        return (int)response.StatusCode == 400 &&
               (body.Contains("already", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("exists", StringComparison.OrdinalIgnoreCase));
    }

    // Проверяет, что у пользователя уже есть зафиксированная дата изменения пароля.
    private static bool HasPasswordConfigured(AuthentikUserRecord user)
    {
        return !string.IsNullOrWhiteSpace(user.PasswordChangeDate);
    }

    private HttpRequestMessage CreateJsonRequest(HttpMethod method, string requestUri, object payload)
    {
        var request = CreateRequest(method, requestUri);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, UserMiniAppJsonOptions.Default),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string requestUri)
    {
        EnsureConfigured();

        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_apiToken))
        {
            throw new InvalidOperationException("Не настроен AUTHENTIK_API_TOKEN или AuthentikAuth:ApiToken.");
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Не удалось {operation}. Authentik вернул {(int)response.StatusCode}: {body}");
    }

    private static AuthentikUserRecord ReadUser(JsonElement user)
    {
        return new AuthentikUserRecord(
            ReadInt(user, "pk"),
            ReadString(user, "username"),
            ReadString(user, "name"),
            ReadString(user, "uid"),
            ReadString(user, "uuid"),
            ReadBool(user, "is_active"),
            ReadString(user, "email"),
            ReadString(user, "type"),
            ReadString(user, "password_change_date"));
    }

    // Читает минимальную Authentik group DTO из API JSON.
    private static AuthentikGroupRecord ReadGroup(JsonElement group)
    {
        return new AuthentikGroupRecord(
            ReadString(group, "pk"),
            ReadString(group, "name"),
            ReadBool(group, "is_superuser"));
    }

    private static string ReplacePage(string uri, int page)
    {
        var separator = uri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return uri.Contains("page=", StringComparison.Ordinal)
            ? System.Text.RegularExpressions.Regex.Replace(uri, @"([?&]page=)\d+", "${1}" + page)
            : uri + separator + "page=" + page;
    }

    private static string GenerateCode(int length)
    {
        Span<char> chars = stackalloc char[length];
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = GeneratedCodeAlphabet[RandomNumberGenerator.GetInt32(GeneratedCodeAlphabet.Length)];
        }

        return new string(chars);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : 0;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;
    }

    private static int ReadConfiguredInt(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    // Нормализует обязательный текстовый параметр Authentik API.
    private static string NormalizeRequiredText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Значение не может быть пустым.", parameterName);
        }

        return value.Trim();
    }
}

internal sealed record AuthentikUserRecord(
    int Pk,
    string Username,
    string Name,
    string Uid,
    string Uuid,
    bool IsActive,
    string Email,
    string Type,
    string PasswordChangeDate);

internal sealed record AuthentikGroupRecord(
    string Pk,
    string Name,
    bool IsSuperuser);
