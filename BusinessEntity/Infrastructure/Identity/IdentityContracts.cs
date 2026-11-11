using System.Security.Claims;

namespace BusinessEntity.Infrastructure.Identity;

// Проверенный протоколом субъект внешнего провайдера; не локальная роль приложения.
public sealed record VerifiedExternalIdentity(Guid ProviderId, string Issuer, string Subject,
    string Login, string DisplayName, string? Email, string AccountKind = "Human");

// Явный контракт ресурса API, не доверяющий всем токенам одного хоста.
public sealed record ApiTokenProfile(string Issuer, string MetadataAddress, string Audience,
    string[] AllowedClientIds, string ClientClaim = "azp", string RequiredScope = "be.api");

// Имена профильных claims задаются конкретной регистрацией OIDC, а не vendor DTO.
public sealed record IdentityClaimsMapping(string Login = "preferred_username", string DisplayName = "name", string Email = "email");

// Версионированный профиль OIDC без значений секретов; изменения активируются явно.
public sealed record IdentityProviderProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public int Revision { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public string Issuer { get; init; } = string.Empty;
    public string MetadataAddress { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecretRef { get; init; } = string.Empty;
    public string[] Scopes { get; init; } = ["openid", "profile", "email"];
    public IdentityClaimsMapping Claims { get; init; } = new();
    public string? BackchannelBaseAddress { get; init; }
    public string ProvisioningKind { get; init; } = "None";
    public string ManagementEndpoint { get; init; } = string.Empty;
    public string ManagementSecretRef { get; init; } = string.Empty;
    public string SubjectMode { get; init; } = "user_uuid";
    public string PasswordResetAttribute { get; init; } = "be_password_reset";
    public bool PasswordResetFlowVerified { get; init; }
    public bool ManagedLocal { get; init; }
    public ApiTokenProfile[] ApiTokenProfiles { get; init; } = [];
    public string Scheme => $"oidc-{Id:N}-r{Revision}";
    public string CallbackPath => $"/auth/callback/{Id:N}/{Revision}";
}

// Коммитнутый выбор профиля и монотонная эпоха отзыва сессий.
public sealed record ActiveIdentityConfiguration(IdentityProviderProfile Profile, long Epoch);

// Внешний протокол использует проверенную runtime-копию настроек владельца User.
public interface IIdentityProviderRegistry
{
    ActiveIdentityConfiguration Current { get; }
    IdentityProviderProfile Get(Guid id, int revision);
    IReadOnlyList<IdentityProviderProfile> Profiles { get; }
}

// Разрешает только заранее объявленные оператором secret references.
public interface IIdentitySecretResolver
{
    // Значение используется только протокольным адаптером, не выдаётся в metadata/UI.
    string Resolve(string reference);
}

// UserMiniApp завершает проверенный OIDC flow, сохраняя постоянную локальную identity.
public interface IIdentitySignInHandler
{
    // Probe проверяет доступ к плану, но не создаёт рабочую cookie целевого профиля.
    Task<ClaimsPrincipal> CompleteAsync(VerifiedExternalIdentity identity, long epoch,
        Guid? probePlanId, CancellationToken ct);
}

// Получает уже аутентифицированный principal HTTP/circuit в одном месте инфраструктуры.
public interface ICurrentPrincipalSource
{
    Task<ClaimsPrincipal> GetAsync(CancellationToken ct = default);
}

// Только User проверяет сохранённый контекст работы, прежде чем установить principal отдельного background scope.
public interface IBackgroundPrincipalSink{void Install(ClaimsPrincipal principal);}

// Устойчивые имена claims не раскрывают внутренние claims конкретного IdP остальному приложению.
public static class IdentityClaimNames
{
    public const string UserId = "be.user";
    public const string ProviderId = "be.provider";
    public const string Epoch = "be.epoch";
    public const string Subject = "be.subject";
    public const string Issuer = "be.issuer";
    public const string Kind = "be.kind";
    public const string ClientId = "be.client";
    public const string AccessExpiresAt="be.api.access-expires";
}

// Нейтральный внешний аккаунт с доказуемым соответствием настроенному subject mode.
public sealed record ProvisionedIdentity(string AccountId, string Issuer, string Subject,
    string Login, string DisplayName, bool Enabled, bool RequiresPasswordChange,string AccountKind="Human");

// Необязательное управление каталогом, отделённое от OIDC-входа.
public interface IIdentityProvisioningAdapter
{
    string Kind { get; }
    Task ValidateAsync(IdentityProviderProfile profile, CancellationToken ct);
    Task<ProvisionedIdentity?> FindAsync(IdentityProviderProfile profile, string login, CancellationToken ct);
    Task<ProvisionedIdentity> CreateAsync(IdentityProviderProfile profile, string login,
        string temporaryPassword, Guid operationId, CancellationToken ct);
    Task<ProvisionedIdentity> ReadAsync(IdentityProviderProfile profile, string accountId, CancellationToken ct);
    Task SetTemporaryPasswordAsync(IdentityProviderProfile profile, string accountId, string password, CancellationToken ct);
    // Доказательство соответствия API subject и UI-каталога принадлежит конкретному адаптеру.
    Task<ProvisionedIdentity?> ResolveAccessSubjectAsync(IdentityProviderProfile profile,ApiTokenProfile api,string subject,CancellationToken ct);
}

// Протокол cookie спрашивает владельца о действительности локальной сессии, не читая его таблицы.
public interface IIdentitySessionValidator
{
    Task<bool> ValidateAsync(ClaimsPrincipal principal,CancellationToken ct);
}

// Необязательная возможность service provisioning отделена от человеческих временных паролей.
public interface IIdentityServiceProvisioningAdapter
{
    Task<ProvisionedServiceIdentity> CreateServiceAsync(IdentityProviderProfile profile,string login,Guid operationId,CancellationToken ct);
    Task<string> RotateServiceCredentialAsync(IdentityProviderProfile profile,string accountId,Guid operationId,CancellationToken ct);
}
// Credential существует только в памяти инициатора; не является bus message или хранимым outcome.
public sealed record ProvisionedServiceIdentity(ProvisionedIdentity Account,string Credential);
