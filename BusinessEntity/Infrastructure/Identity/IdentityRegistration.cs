using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BusinessEntity.Infrastructure.Identity;

// Общая регистрация протокола не знает классов и служебных таблиц конкретного миниаппа.
public static class IdentityRegistration
{
    // Профили и полномочия предоставляет владелец через интерфейсы, схемы OIDC добавляются по декларациям.
    public static IServiceCollection AddPluggableIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor(); services.AddHttpClient(); services.AddDataProtection().SetApplicationName("BusinessEntity.Identity.v2");
        var keyPath=configuration["Identity:KeyDirectory"]??throw new InvalidOperationException("Identity:KeyDirectory обязателен.");
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        services.AddSingleton<IdentityRuntime>(); services.AddSingleton<IIdentityProviderRegistry>(p => p.GetRequiredService<IdentityRuntime>());
        services.AddSingleton<IIdentitySecretResolver, IdentitySecretResolver>();
        services.AddSingleton<BusinessEntity.Infrastructure.Bus.ITransactionAdmission, IdentityTransactionAdmission>();
        services.AddSingleton<IdentityProfileValidation>(); services.AddSingleton<IdentityAdmissionGate>();
        services.AddScoped<CurrentPrincipalSource>();services.AddScoped<ICurrentPrincipalSource>(p=>p.GetRequiredService<CurrentPrincipalSource>());services.AddScoped<IBackgroundPrincipalSink>(p=>p.GetRequiredService<CurrentPrincipalSource>());
        services.AddOptions<OpenIdConnectOptions>(); services.AddTransient<OpenIdConnectHandler>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, IdentityOidcOptions>();
        services.AddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>();
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddCookie(options =>
        {
            options.Cookie.Name = "be.identity.v2"; options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath = "/auth/login"; options.AccessDeniedPath = "/auth/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = false;
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async context =>
                {
                    var validator=context.HttpContext.RequestServices.GetRequiredService<IIdentitySessionValidator>();
                    if(context.Principal==null||!await validator.ValidateAsync(context.Principal,context.HttpContext.RequestAborted))context.RejectPrincipal();
                },
                OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api/v1")) context.Response.StatusCode = 401;
                    else context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api/v1")) context.Response.StatusCode = 403;
                    else context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                }
            };
        });
        services.Replace(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider,IdentityAuthenticationSchemes>());
        services.AddAuthorization(); return services;
    }
}
