using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BusinessEntity.Infrastructure.Identity;

// Текущий HTTP principal или аутентифицированный principal живого circuit, без vendor API.
public sealed class CurrentPrincipalSource(IHttpContextAccessor http, AuthenticationStateProvider? authentication = null) : ICurrentPrincipalSource,IBackgroundPrincipalSink
{
    // API scope фиксирует сам HttpContext до bus dispatch: ambient AsyncLocal не является адресацией шины.
    private readonly HttpContext? _apiRequest=http.HttpContext?.Request.Path.StartsWithSegments("/api/v1")==true?http.HttpContext:null;
    private ClaimsPrincipal? _background;
    public void Install(ClaimsPrincipal principal){if(http.HttpContext!=null)throw new InvalidOperationException("Background principal не устанавливается в HTTP scope.");_background=principal;}
    // Background имеет отдельный явный execution context; не выбирает случайное окно пользователя.
    public async Task<ClaimsPrincipal> GetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if(_background!=null)return _background;
        if(_apiRequest!=null)return _apiRequest.User;
        if (http.HttpContext != null) return http.HttpContext.User;
        if (authentication == null) return new ClaimsPrincipal(new ClaimsIdentity());
        try { return (await authentication.GetAuthenticationStateAsync()).User; }
        catch (InvalidOperationException) { return new ClaimsPrincipal(new ClaimsIdentity()); }
    }
}
