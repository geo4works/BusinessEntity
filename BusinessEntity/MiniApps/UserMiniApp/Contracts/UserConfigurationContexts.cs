namespace BusinessEntity.MiniApps.UserMiniApp.Contracts;

// Разные агрегаты настроек User имеют собственные draft/save contexts в пределах окна.
public static class UserConfigurationContexts
{
    public static readonly Guid IdentityProviders=Guid.Parse("d7229901-aee4-48a1-9928-3620d0210001");
    public static readonly Guid ApiIntegrations=Guid.Parse("d7229901-aee4-48a1-9928-3620d0210002");
}
