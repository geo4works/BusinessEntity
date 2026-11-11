using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace BusinessEntity.Infrastructure.Identity;

// Не инициализирует все неактивные OIDC-профили при каждом HTTP-запросе.
public sealed class IdentityAuthenticationSchemes(IOptions<AuthenticationOptions> options,IHttpContextAccessor http)
    : AuthenticationSchemeProvider(options)
{
    // Remote handler выбирается по точному известному callback-пути; state/nonce затем проверяет сам OIDC handler.
    public override async Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync()
    {
        var path=http.HttpContext?.Request.Path.Value??string.Empty;
        if(!path.StartsWith("/auth/callback/",StringComparison.Ordinal))return [];
        var parts=path[15..].Split('/');
        if(parts.Length<2||!Guid.TryParseExact(parts[0],"N",out var id)||!int.TryParse(parts[1],out var revision))return [];
        var name=$"oidc-{id:N}-r{revision}";
        return (await base.GetRequestHandlerSchemesAsync()).Where(x=>x.Name==name).ToArray();
    }
}
