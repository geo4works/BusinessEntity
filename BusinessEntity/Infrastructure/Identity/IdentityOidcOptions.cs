using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BusinessEntity.Infrastructure.Identity;

// Каждый handler получает отдельные immutable options профиля, а не переключаемый общий Authority.
public sealed class IdentityOidcOptions(IIdentityProviderRegistry registry, IIdentitySecretResolver secrets,
    IWebHostEnvironment environment) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    // Именованная схема выводится только из серверного реестра, не из произвольного URL браузера.
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        var profile = registry.Profiles.FirstOrDefault(p => p.Scheme == name);
        if (profile == null && name?.StartsWith("oidc-", StringComparison.Ordinal) == true)
        {
            var split = name.Split("-r");
            if (split.Length == 2 && Guid.TryParseExact(split[0][5..], "N", out var id) && int.TryParse(split[1], out var revision))
                profile = registry.Get(id, revision);
        }
        if (profile == null) throw new InvalidOperationException("OIDC scheme не зарегистрирована.");
        options.Authority = profile.Issuer;
        options.MetadataAddress = profile.MetadataAddress;
        options.RequireHttpsMetadata = !environment.IsDevelopment();
        options.ClientId = profile.ClientId;
        options.ClientSecret = secrets.Resolve(profile.ClientSecretRef);
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CallbackPath = profile.CallbackPath;
        options.SignedOutCallbackPath = profile.CallbackPath + "/signed-out";
        options.ResponseType = "code";
        options.ResponseMode = "query";
        options.UsePkce = true;
        // Code/query возвращается top-level GET, поэтому Lax сохраняет nonce/correlation и работает в локальном HTTP dev.
        options.CorrelationCookie.SameSite=SameSiteMode.Lax;options.NonceCookie.SameSite=SameSiteMode.Lax;
        if(environment.IsDevelopment())
        {options.CorrelationCookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;options.NonceCookie.SecurePolicy=CookieSecurePolicy.SameAsRequest;}
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.Scope.Clear(); foreach (var scope in profile.Scopes) options.Scope.Add(scope);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = profile.Issuer,
            ValidateAudience = true, ValidAudience = profile.ClientId,
            ValidateLifetime = true, RequireSignedTokens = true, ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
                SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512],
            ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = "preferred_username"
        };
        if (!string.IsNullOrWhiteSpace(profile.BackchannelBaseAddress))
            options.BackchannelHttpHandler = new IdentityBackchannelHandler(profile.Issuer, profile.BackchannelBaseAddress);
        options.Events = new OpenIdConnectEvents
        {
            // TicketReceived вызывается после nonce/state/protocol validation; TokenValidated для JIT ещё слишком рано.
            OnTicketReceived = async context =>
            {
                var properties=context.Properties??throw new InvalidDataException("Отсутствует state входа.");
                var principal = context.Principal ?? throw new InvalidDataException("Отсутствует проверенная identity.");
                var subject = principal.FindFirstValue("sub") ?? throw new InvalidDataException("Отсутствует subject.");
                var login = principal.FindFirstValue(profile.Claims.Login) ?? subject;
                if (!long.TryParse((properties.Items.TryGetValue("be.epoch", out var epochText) ? epochText : null), out var epoch))
                    throw new InvalidDataException("Отсутствует эпоха входа.");
                Guid? plan = Guid.TryParse((properties.Items.TryGetValue("be.probe", out var probeText) ? probeText : null), out var planId) ? planId : null;
                if (plan.HasValue)
                {
                    // Remote callback исполняется раньше обычного cookie middleware; инициатор проверяется явно.
                    var source = await context.HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    if (!source.Succeeded || source.Principal == null) throw new UnauthorizedAccessException("Исходная сессия пробного входа завершена.");
                    context.HttpContext.User = source.Principal;
                }
                var identity = new VerifiedExternalIdentity(profile.Id, profile.Issuer, subject, login,
                    principal.FindFirstValue(profile.Claims.DisplayName) ?? login, principal.FindFirstValue(profile.Claims.Email));
                context.Principal = await context.HttpContext.RequestServices.GetRequiredService<IIdentitySignInHandler>()
                    .CompleteAsync(identity, epoch, plan, context.HttpContext.RequestAborted);
                if (plan.HasValue)
                {
                    context.HandleResponse(); context.Response.Redirect("/administration/identity-providers?planId=" + properties.Items["be.probe"]);
                }
            },
            OnRemoteFailure = context =>
            {
                context.HandleResponse(); context.Response.Redirect("/auth/error?code=identity.login_failed");
                return Task.CompletedTask;
            }
        };
    }
    // Неименованные options никогда не используются для произвольного активного профиля.
    public void Configure(OpenIdConnectOptions options) => throw new InvalidOperationException("Нужна именованная OIDC scheme.");
}

// Доверенный back-channel меняет маршрут сервера, сохраняя публичный issuer и Host.
internal sealed class IdentityBackchannelHandler(string issuer, string backchannel) : DelegatingHandler(new HttpClientHandler())
{
    // Только запросы к настроенному authority переводятся во внутреннюю сеть; внешний redirect не переписывается.
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var publicUri = new Uri(issuer); var original = request.RequestUri!;
        if (original.Authority == publicUri.Authority)
        {
            var internalUri = new Uri(backchannel);
            request.Headers.Host = original.Authority;
            request.RequestUri = new UriBuilder(original) { Scheme = internalUri.Scheme, Host = internalUri.Host, Port = internalUri.Port }.Uri;
        }
        return base.SendAsync(request, ct);
    }
}
