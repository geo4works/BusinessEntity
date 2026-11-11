namespace BusinessEntity.MiniApps.UserMiniApp.Contracts;

// Безопасный результат проверки собственного административного плана, без внутренних storage DTO.
public sealed record IdentityProbe(Guid ProviderId,int Revision,long Epoch);
// Собственный HTTP-адаптер User получает разрешение на probe напрямую от владельца.
public interface IIdentityProbeAuthorizer
{
    Task<IdentityProbe> AuthorizeProbeAsync(Guid planId,CancellationToken ct);
}
