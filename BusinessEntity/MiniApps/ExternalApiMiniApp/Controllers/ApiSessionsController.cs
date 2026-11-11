using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

namespace BusinessEntity.MiniApps.ExternalApiMiniApp.Controllers;

// Внешняя поверхность security session использует публичный User connector, не собственную копию SQL.
[ApiController,Route("api/v1"),Authorize(Policy="ExternalApi"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("external-api")]
public sealed class ApiSessionsController(IUserApiConnector security,ApiRequestDispatcher dispatcher,ApiCapabilityCatalog catalog,ApiOperationExecutor operations):ControllerBase
{
    // Ограничения запроса сужаются владельцем относительно текущего локального grant.
    [HttpPost("sessions")]
    public Task<ApiSessionInfo> Open([FromBody]ApiSessionOptions options,CancellationToken ct)=>security.OpenAsync(options,ct);
    // Возвращается только своя session данного Bearer subject/client.
    [HttpGet("sessions/current")]
    public Task<ApiSessionInfo> Current(CancellationToken ct)=>dispatcher.SessionAsync(HttpContext,null,ct);
    // Повтор DELETE своей отозванной session безопасен; чужая не обнаруживается по GUID.
    [HttpDelete("sessions/current"),ProducesResponseType(204)]
    public async Task<IActionResult> Close(CancellationToken ct)
    {if(!Guid.TryParse(Request.Headers["X-BE-Session-Id"],out var id))throw new ApiFault(401,"api.session_required","Требуется X-BE-Session-Id.");await security.CloseAsync(id,ct);return NoContent();}
    // Каталог фильтруется локальным допуском; права конкретного ресурса проверит владелец при выполнении.
    [HttpGet("capabilities")]
    public async Task<IReadOnlyList<ApiCapability>> Capabilities(CancellationToken ct)
    {var session=await dispatcher.SessionAsync(HttpContext,null,ct);return catalog.Definitions.Where(x=>session.Capabilities.Contains(x.Id,StringComparer.Ordinal)).ToArray();}
    // Новый session автора/client может выяснить исход без сохранения прежнего SessionId.
    [HttpGet("operations/{id:guid}")]
    public async Task<ApiOperationStatus> Operation(Guid id,CancellationToken ct)
    {var session=await dispatcher.SessionAsync(HttpContext,null,ct);var receipt=await operations.ReadAsync(id,session,ct);return new(id,receipt.State,receipt.Result,receipt.ExpiresAt,receipt.ReferenceId);}
}
