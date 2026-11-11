using System.Security.Claims;
using System.Text.Encodings.Web;
using BusinessEntity.Infrastructure.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BusinessEntity.Infrastructure.Identity;

// Только результат полной проверки API token, без токена или клиентского локального UserId.
public sealed record VerifiedAccessIdentity(Guid ProviderId,long Epoch,string Issuer,string Subject,string ClientId,
    string Login,string DisplayName,string? Email,DateTime AccessExpiresAt=default);

// Локальные привязки и полномочия остаются у владельца User.
public interface IApiIdentityHandler
{
    Task<ClaimsPrincipal> CompleteAsync(VerifiedAccessIdentity identity,CancellationToken ct);
}

// API policy выбирает только immutable token profiles активной ревизии установки.
public sealed class ActiveBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,
    UrlEncoder encoder,IIdentityProviderRegistry registry) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    public const string SchemeName="ExternalBearer";
    // Невалидированный iss не используется даже для расширения набора доверенных metadata endpoints.
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if(!Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))return AuthenticateResult.NoResult();
        var active=registry.Current;
        for(var i=0;i<active.Profile.ApiTokenProfiles.Length;i++)
        {
            var result=await Context.AuthenticateAsync(IdentityApiOptions.Scheme(active.Profile,i));
            if(result.Succeeded)return AuthenticateResult.Success(new AuthenticationTicket(result.Principal!,SchemeName));
        }
        return AuthenticateResult.Fail("API token не прошёл проверку.");
    }
    // Внешний клиент всегда получает JSON, без cookie login redirects.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)=>ApiProblems.WriteAsync(Context,401,"api.identity_invalid","Требуется действующий API access token.");
    // Отказ identity policy также использует единый problem contract.
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)=>ApiProblems.WriteAsync(Context,403,"api.grant_denied","Доступ к API не разрешён.");
}

// Стандартный JwtBearerHandler выполняет подпись/issuer/audience/expiry/algorithm до owner callback.
public sealed class IdentityApiOptions(IIdentityProviderRegistry registry,IWebHostEnvironment environment) : IConfigureNamedOptions<JwtBearerOptions>
{
    // Именование закрепляет токен за сохранённой ревизией и явным API resource.
    public static string Scheme(IdentityProviderProfile profile,int index)=>$"api-{profile.Id:N}-r{profile.Revision}-p{index}";
    // Ни UI issuer, ни все clients хоста не становятся доверенными неявно.
    public void Configure(string? name,JwtBearerOptions options)
    {
        var parts=name?.Split('-');
        if(parts?.Length!=4||parts[0]!="api"||!Guid.TryParseExact(parts[1],"N",out var id)
            ||!int.TryParse(parts[2][1..],out var revision)||!int.TryParse(parts[3][1..],out var index))throw new InvalidOperationException("Неизвестная API scheme.");
        var profile=registry.Get(id,revision);
        if(index<0||index>=profile.ApiTokenProfiles.Length)throw new InvalidOperationException("Неизвестный API token profile.");
        var api=profile.ApiTokenProfiles[index];
        options.Authority=api.Issuer;options.MetadataAddress=api.MetadataAddress;options.RequireHttpsMetadata=!environment.IsDevelopment();
        options.MapInboundClaims=false;options.SaveToken=false;options.IncludeErrorDetails=false;
        options.TokenValidationParameters=new() {ValidateIssuer=true,ValidIssuer=api.Issuer,ValidateAudience=true,ValidAudience=api.Audience,
            ValidateLifetime=true,RequireExpirationTime=true,RequireSignedTokens=true,ValidateIssuerSigningKey=true,ClockSkew=TimeSpan.FromSeconds(30),
            ValidAlgorithms=[SecurityAlgorithms.RsaSha256,SecurityAlgorithms.RsaSha384,SecurityAlgorithms.RsaSha512,SecurityAlgorithms.EcdsaSha256,SecurityAlgorithms.EcdsaSha384,SecurityAlgorithms.EcdsaSha512]};
        if(!string.IsNullOrWhiteSpace(profile.BackchannelBaseAddress))options.BackchannelHttpHandler=new IdentityBackchannelHandler(api.Issuer,profile.BackchannelBaseAddress);
        options.Events=new JwtBearerEvents {OnTokenValidated=async context=>
        {
            var current=registry.Current;
            if(current.Profile.Id!=profile.Id||current.Profile.Revision!=profile.Revision){context.Fail("Устаревшая security revision.");return;}
            var principal=context.Principal!;var client=principal.FindFirstValue(api.ClientClaim);var subject=principal.FindFirstValue("sub");
            var scopes=principal.FindAll("scope").SelectMany(c=>c.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
            if(string.IsNullOrWhiteSpace(subject)||client==null||!api.AllowedClientIds.Contains(client,StringComparer.Ordinal)||!scopes.Contains(api.RequiredScope))
            {context.Fail("Не разрешены subject/client/scope.");return;}
            var login=principal.FindFirstValue(profile.Claims.Login)??subject;
            context.Principal=await context.HttpContext.RequestServices.GetRequiredService<IApiIdentityHandler>().CompleteAsync(
                new(profile.Id,current.Epoch,api.Issuer,subject,client,login,principal.FindFirstValue(profile.Claims.DisplayName)??login,principal.FindFirstValue(profile.Claims.Email),context.SecurityToken.ValidTo.ToUniversalTime()),context.HttpContext.RequestAborted);
        }};
    }
    // API требует точного имени; общий default token profile не существует.
    public void Configure(JwtBearerOptions options)=>throw new InvalidOperationException("Нужна именованная API scheme.");
}

// Регистрирует только транспорт; конкретный IApiIdentityHandler предоставляет UserMiniApp.
public static class IdentityApiRegistration
{
    // Схемы конкретных ресурсов объявит IdentityRuntime при загрузке сохранённых профилей.
    public static IServiceCollection AddApiBearer(this IServiceCollection services)
    {
        services.AddOptions<JwtBearerOptions>();services.AddTransient<JwtBearerHandler>();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>,IdentityApiOptions>();
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>,JwtBearerPostConfigureOptions>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions,ActiveBearerHandler>(ActiveBearerHandler.SchemeName,_=>{});
        services.AddAuthorization(o=>o.AddPolicy("ExternalApi",p=>p.AddAuthenticationSchemes(ActiveBearerHandler.SchemeName).RequireAuthenticatedUser()));
        return services;
    }
}
