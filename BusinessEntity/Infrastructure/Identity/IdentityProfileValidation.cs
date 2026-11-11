using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace BusinessEntity.Infrastructure.Identity;

// Проверяет реальный discovery/JWKS и параметры без активации профиля.
public sealed class IdentityProfileValidation(IIdentitySecretResolver secrets, IWebHostEnvironment environment)
{
    // Результат относится к точной ревизии; доступность management проверяется отдельным адаптером.
    public async Task ValidateAsync(IdentityProviderProfile profile, CancellationToken ct)
    {
        if (profile.Id == Guid.Empty || profile.Revision < 1 || string.IsNullOrWhiteSpace(profile.Name)
            || string.IsNullOrWhiteSpace(profile.ClientId) || !profile.Scopes.Contains("openid"))
            throw new ArgumentException("Не заполнены обязательные параметры OIDC.");
        CheckAddress(profile.Issuer); CheckAddress(profile.MetadataAddress);
        if (profile.Claims == null || string.IsNullOrWhiteSpace(profile.Claims.Login)
            || string.IsNullOrWhiteSpace(profile.Claims.DisplayName) || string.IsNullOrWhiteSpace(profile.Claims.Email))
            throw new ArgumentException("Нужно сопоставление профильных claims.");
        _ = secrets.Resolve(profile.ClientSecretRef);
        using var handler = string.IsNullOrWhiteSpace(profile.BackchannelBaseAddress)
            ? (HttpMessageHandler)new HttpClientHandler()
            : new IdentityBackchannelHandler(profile.Issuer, profile.BackchannelBaseAddress);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var document = await OpenIdConnectConfigurationRetriever.GetAsync(profile.MetadataAddress,
            new HttpDocumentRetriever(http) { RequireHttps = !environment.IsDevelopment() }, ct);
        if (!string.Equals(document.Issuer, profile.Issuer, StringComparison.Ordinal)
            || document.SigningKeys.Count == 0 || string.IsNullOrWhiteSpace(document.AuthorizationEndpoint)
            || string.IsNullOrWhiteSpace(document.TokenEndpoint))
            throw new InvalidDataException("Discovery issuer/keys/endpoints не соответствуют профилю.");
        foreach (var api in profile.ApiTokenProfiles)
        {
            CheckAddress(api.Issuer); CheckAddress(api.MetadataAddress);
            if (string.IsNullOrWhiteSpace(api.Audience) || api.AllowedClientIds.Length == 0 || string.IsNullOrWhiteSpace(api.RequiredScope))
                throw new ArgumentException("API token profile не ограничен audience/client/scope.");
            if(api.Audience==profile.ClientId)throw new ArgumentException("Для API нужна отдельная audience, отличная от UI ID token.");
            using var apiHandler=string.IsNullOrWhiteSpace(profile.BackchannelBaseAddress)?(HttpMessageHandler)new HttpClientHandler():new IdentityBackchannelHandler(api.Issuer,profile.BackchannelBaseAddress);
            using var apiHttp=new HttpClient(apiHandler){Timeout=TimeSpan.FromSeconds(20)};
            var apiDocument=await OpenIdConnectConfigurationRetriever.GetAsync(api.MetadataAddress,new HttpDocumentRetriever(apiHttp){RequireHttps=!environment.IsDevelopment()},ct);
            if(apiDocument.Issuer!=api.Issuer||apiDocument.SigningKeys.Count==0)throw new InvalidDataException("API discovery не соответствует issuer/keys.");
        }
    }
    // Разрешение HTTP ограничено dev-режимом установки, а не значением из редактируемого профиля.
    private void CheckAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment) || (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http")))
            throw new ArgumentException("Требуется допустимый HTTPS-адрес identity; HTTP разрешён только в Development.");
    }
}
