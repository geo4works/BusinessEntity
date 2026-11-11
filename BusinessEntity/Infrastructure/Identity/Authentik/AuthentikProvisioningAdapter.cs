using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BusinessEntity.Infrastructure.Identity.Authentik;

// Единственное место знания Authentik Management API; наружу отдаёт нейтральные проверенные аккаунты.
public sealed partial class AuthentikProvisioningAdapter(IIdentitySecretResolver secrets, IHttpClientFactory clients) : IIdentityProvisioningAdapter,IIdentityServiceProvisioningAdapter
{
    public string Kind => "Authentik";
    // Оба OAuth resources должны фактически использовать user_uuid; совпадение имени не участвует в связывании.
    public async Task<ProvisionedIdentity?> ResolveAccessSubjectAsync(IdentityProviderProfile profile,ApiTokenProfile api,string subject,CancellationToken ct)
    {
        await ValidateAsync(profile,ct);
        using var resource=await SendAsync(profile,HttpMethod.Get,"providers/oauth2/?client_id="+Uri.EscapeDataString(api.Audience),null,ct);
        var actual=resource!.RootElement.GetProperty("results").EnumerateArray().Single(x=>x.GetProperty("client_id").GetString()==api.Audience);
        if(actual.GetProperty("sub_mode").GetString()!="user_uuid"||!Guid.TryParse(subject,out var uuid))throw new InvalidDataException("Не доказано соответствие API subject mode.");
        using var users=await SendAsync(profile,HttpMethod.Get,"core/users/?uuid="+uuid.ToString(),null,ct);
        var user=users!.RootElement.GetProperty("results").EnumerateArray().SingleOrDefault(x=>x.GetProperty("uuid").GetString()==uuid.ToString());
        return user.ValueKind==JsonValueKind.Undefined?null:Map(profile,user);
    }
    // Проверяет фактический subject mode и заявленную настройку обязательной смены пароля.
    public async Task ValidateAsync(IdentityProviderProfile profile, CancellationToken ct)
    {
        if (profile.SubjectMode != "user_uuid" || !profile.PasswordResetFlowVerified)
            throw new NotSupportedException("identity.capability_unavailable: требуется user_uuid и проверенный flow смены пароля.");
        var provider = await GetProviderAsync(profile, ct);
        if (provider.GetProperty("sub_mode").GetString() != "user_uuid")
            throw new InvalidDataException("Authentik provider не использует заявленный user_uuid.");
    }
    // Точное имя используется только для обнаружения конфликта, не для доказательства принадлежности.
    public async Task<ProvisionedIdentity?> FindAsync(IdentityProviderProfile profile, string login, CancellationToken ct)
    {
        using var result = await SendAsync(profile, HttpMethod.Get, "core/users/?username=" + Uri.EscapeDataString(login), null, ct);
        var matches = result!.RootElement.GetProperty("results").EnumerateArray()
            .Where(x => string.Equals(x.GetProperty("username").GetString(), login, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException("Неоднозначный логин целевого каталога.");
        return matches.Length == 0 ? null : Map(profile, matches[0]);
    }
    // Повтор может использовать аккаунт только при совпадении собственного маркера операции в каталоге.
    public async Task<ProvisionedIdentity> CreateAsync(IdentityProviderProfile profile, string login, string password, Guid operationId, CancellationToken ct)
    {
        await ValidateAsync(profile, ct);
        using var lookup = await SendAsync(profile, HttpMethod.Get, "core/users/?username=" + Uri.EscapeDataString(login), null, ct);
        var existing = lookup!.RootElement.GetProperty("results").EnumerateArray()
            .SingleOrDefault(x => string.Equals(x.GetProperty("username").GetString(), login, StringComparison.OrdinalIgnoreCase));
        ProvisionedIdentity user;
        if (existing.ValueKind != JsonValueKind.Undefined)
        {
            if (!existing.GetProperty("attributes").TryGetProperty("be_provisioning_operation", out var marker)
                || marker.GetString() != operationId.ToString())
                throw new InvalidOperationException("identity.username_conflict: имя уже занято.");
            user = Map(profile, existing);
        }
        else
        {
            var attributes = new Dictionary<string,object> { ["be_provisioning_operation"] = operationId.ToString(), [profile.PasswordResetAttribute] = true };
            using var created = await SendAsync(profile, HttpMethod.Post, "core/users/", new { username = login,
                name = login, is_active = true, type = "internal", path = "businessentity", attributes }, ct);
            user = Map(profile, created!.RootElement);
        }
        if (!user.RequiresPasswordChange) throw new InvalidOperationException("Аккаунт уже активирован; повторная установка начального пароля запрещена.");
        await SetTemporaryPasswordAsync(profile, user.AccountId, password, ct);
        return await ReadAsync(profile, user.AccountId, ct);
    }
    // UUID проверяется в фактической записи, а не выводится из имени пользователя.
    public async Task<ProvisionedIdentity> ReadAsync(IdentityProviderProfile profile, string accountId, CancellationToken ct)
    {
        if (!int.TryParse(accountId, out var id) || id <= 0) throw new ArgumentException("Некорректный внешний account ID.");
        using var result = await SendAsync(profile, HttpMethod.Get, $"core/users/{id}/", null, ct);
        return Map(profile, result!.RootElement);
    }
    // Выставляет временный пароль и сохраняет остальные внешние атрибуты аккаунта.
    public async Task SetTemporaryPasswordAsync(IdentityProviderProfile profile, string accountId, string password, CancellationToken ct)
    {
        if (password.Length < 16) throw new ArgumentException("Временный пароль должен содержать не менее 16 символов.");
        using var user = await SendAsync(profile, HttpMethod.Get, $"core/users/{accountId}/", null, ct);
        var attributes = JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(user!.RootElement.GetProperty("attributes").GetRawText())!;
        attributes[profile.PasswordResetAttribute] = JsonSerializer.SerializeToElement(true);
        using var patched = await SendAsync(profile, HttpMethod.Patch, $"core/users/{accountId}/", new { attributes }, ct);
        using var set = await SendAsync(profile, HttpMethod.Post, $"core/users/{accountId}/set_password/", new { password }, ct);
    }
    // Административная проверка никогда не переключает sub_mode существующего OAuth provider.
    private async Task<JsonElement> GetProviderAsync(IdentityProviderProfile profile, CancellationToken ct)
    {
        using var response = await SendAsync(profile, HttpMethod.Get, "providers/oauth2/?client_id=" + Uri.EscapeDataString(profile.ClientId), null, ct);
        return response!.RootElement.GetProperty("results").EnumerateArray().Single(x => x.GetProperty("client_id").GetString() == profile.ClientId).Clone();
    }
    // Management account PK и OIDC subject имеют разные поля и не смешиваются.
    private static ProvisionedIdentity Map(IdentityProviderProfile profile, JsonElement value)
    {
        var uuid = value.GetProperty("uuid").GetString();
        if (!Guid.TryParse(uuid, out var parsed)) throw new InvalidDataException("Authentik не вернул устойчивый UUID.");
        var reset = value.GetProperty("attributes").TryGetProperty(profile.PasswordResetAttribute, out var flag) && flag.ValueKind == JsonValueKind.True;
        return new(value.GetProperty("pk").GetInt32().ToString(), profile.Issuer, parsed.ToString(),
            value.GetProperty("username").GetString()!, value.GetProperty("name").GetString()!, value.GetProperty("is_active").GetBoolean(), reset,
            value.TryGetProperty("type",out var type)&&type.GetString() is "service_account" or "internal_service_account"?"Service":"Human");
    }
    // Ошибки внешнего сервера не раскрывают response body с возможными credentials в пользовательском сообщении.
    private async Task<JsonDocument?> SendAsync(IdentityProviderProfile profile, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Uri.TryCreate(profile.ManagementEndpoint, UriKind.Absolute, out var address)) throw new InvalidOperationException("Management endpoint не настроен.");
        using var http = clients.CreateClient("IdentityManagement");
        http.BaseAddress = new Uri(address.ToString().TrimEnd('/') + "/"); http.Timeout = TimeSpan.FromSeconds(25);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secrets.Resolve(profile.ManagementSecretRef));
        request.Headers.Host = new Uri(profile.Issuer).Authority;
        // Небольшие management DTO буферизуются: proxy получает Content-Length, а не chunked JSON.
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"identity.provider_error: {method} {path.Split('?')[0]} HTTP {(int)response.StatusCode}");
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }
}
