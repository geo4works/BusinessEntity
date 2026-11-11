using BusinessEntity.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessEntity.Controllers;

// Общие точки входа используют активный OIDC-профиль, не собственный password-flow провайдера.
[AllowAnonymous]
[Route("auth")]
public sealed class AuthController(IIdentityProviderRegistry registry) : Controller
{
    // Корреляцию/state/nonce и PKCE обрабатывает стандартный handler точной ревизии профиля.
    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        var active = registry.Current;
        var properties = new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" };
        properties.Items["be.epoch"] = active.Epoch.ToString();
        return Challenge(properties, active.Profile.Scheme);
    }
    // Завершает локальную и провайдерскую сессии через выбранный стандартный handler.
    [HttpGet("logout")]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = "/auth/logged-out" },
        CookieAuthenticationDefaults.AuthenticationScheme, registry.Current.Profile.Scheme);
    // Доступ без полномочий отделяется от ошибки аутентификации.
    [HttpGet("access-denied")]
    public IActionResult AccessDenied() => StatusCode(403, "Недостаточно прав.");
}
