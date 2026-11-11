namespace BusinessEntity.MiniApps.UserMiniApp.Storage;

// Физическая запись конфигурации без credentials; Json имеет актуальную строгую схему.
internal sealed class IdentityProfileRow
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public string Json { get; set; } = string.Empty;
    public int? ValidatedRevision { get; set; }
    public DateTime? ValidatedAt { get; set; }
}

// Неизменяемая сохранённая ревизия нужна для callback, restart и безопасной отложенной активации.
internal sealed class IdentityProfileRevisionRow
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public int Revision { get; set; }
    public string Json { get; set; } = string.Empty;
}

// Один коммитнутый выбор провайдера на установку; epoch никогда не уменьшается.
internal sealed class IdentityInstallationRow
{
    public Guid Id { get; set; }
    public Guid ActiveProviderId { get; set; }
    public int ActiveRevision { get; set; } = 1;
    public long Epoch { get; set; } = 1;
    public bool BootstrapCompleted { get; set; }
}

// Связь локального пользователя с проверенными iss/sub, без паролей и токенов.
internal sealed class ExternalIdentityLinkRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProviderId { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string ProviderLogin { get; set; } = string.Empty;
    public Guid? ProvisioningOperationId { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public bool Enabled { get; set; } = true;
}

// Устойчивый план и журнал внешних эффектов переключения, никогда не содержащий секретов.
internal sealed class IdentitySwitchPlanRow
{
    public Guid Id { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid SourceProviderId { get; set; }
    public Guid TargetProviderId { get; set; }
    public int TargetRevision { get; set; }
    public long SourceEpoch { get; set; }
    public string State { get; set; } = "Draft";
    public string UsersFingerprint { get; set; } = string.Empty;
    public string Json { get; set; } = string.Empty;
    public Guid? VerifiedAdminId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Разрешение программе ограничено локальным субъектом и проверенным OAuth client.
internal sealed class ApiAccessGrantRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public long Revision { get; set; } = 1;
    public string CapabilitiesJson { get; set; } = "[]";
    public string SpacesJson { get; set; } = "{}";
}

// Сессия интеграции переживает процесс, но не хранит bearer/refresh credentials.
internal sealed class ApiSessionRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProviderId { get; set; }
    public long Epoch { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public Guid GrantId { get; set; }
    public long GrantRevision { get; set; }
    public string State { get; set; } = "Active";
    public string CapabilitiesJson { get; set; } = "[]";
    public string SpacesJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
