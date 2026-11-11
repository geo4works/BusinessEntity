namespace BusinessEntity.Infrastructure.Identity;

// Реестр capabilities внешнего каталога не выдаёт универсальный доступ к чужому SDK.
public sealed class IdentityProvisioners(IEnumerable<IIdentityProvisioningAdapter> adapters)
{
    private readonly IReadOnlyDictionary<string,IIdentityProvisioningAdapter> _adapters = adapters.ToDictionary(x => x.Kind, StringComparer.Ordinal);
    // Неподдержанный provisioning явно отличается от работающего OIDC-входа.
    public IIdentityProvisioningAdapter Get(IdentityProviderProfile profile) => _adapters.TryGetValue(profile.ProvisioningKind, out var adapter)
        ? adapter : throw new NotSupportedException("identity.capability_unavailable: каталог этого провайдера недоступен.");
}
