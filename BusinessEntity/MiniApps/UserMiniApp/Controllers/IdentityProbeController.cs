using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessEntity.MiniApps.UserMiniApp.Controllers;

// HTTP-адаптер владельца разрешает только пробный вход конкретного проверенного административного плана.
[Authorize]
[Route("auth/probe")]
public sealed class IdentityProbeController : Controller
{
    // Собственный внутренний сервис вызывается напрямую, не через self bus connector.
    private readonly IIdentityProbeAuthorizer _preparation;
    private readonly IIdentityProviderRegistry _registry;
    // Публичный контракт своего владельца не раскрывает storage DTO и внутренние сервисы.
    public IdentityProbeController(IIdentityProbeAuthorizer preparation,IIdentityProviderRegistry registry)
    {_preparation=preparation;_registry=registry;}
    // URL не принимает произвольный issuer или адрес callback от клиента.
    [HttpGet("{planId:guid}")]
    public async Task<IActionResult> Probe(Guid planId,CancellationToken ct)
    {
        var plan=await _preparation.AuthorizeProbeAsync(planId,ct);
        var profile=_registry.Get(plan.ProviderId,plan.Revision);
        var properties=new AuthenticationProperties {RedirectUri="/administration/identity-providers"};
        properties.Items["be.epoch"]=plan.Epoch.ToString();properties.Items["be.probe"]=planId.ToString();
        return Challenge(properties,profile.Scheme);
    }
}
