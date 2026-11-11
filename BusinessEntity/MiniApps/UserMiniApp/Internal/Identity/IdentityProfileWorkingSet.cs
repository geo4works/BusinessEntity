using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.Infrastructure.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Storage;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Поля страницы являются черновиком владельца User; секретов в форме нет, только ссылки на них.
internal sealed class IdentityProfileForm
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public int Revision { get; set; }=1;
    public bool Enabled { get; set; }=true;
    public bool ManagedLocal { get; set; }
    public string[] Scopes { get; set; }=["openid","profile","email"];
    public string SubjectMode { get; set; }="user_uuid";
    public string PasswordResetAttribute { get; set; }="be_password_reset";
    public string LoginClaim { get; set; }="preferred_username";
    public string NameClaim { get; set; }="name";
    public string EmailClaim { get; set; }="email";
    public string Name { get; set; }="Новый провайдер";
    public string Issuer { get; set; }=string.Empty;
    public string MetadataAddress { get; set; }=string.Empty;
    public string ClientId { get; set; }=string.Empty;
    public string ClientSecretRef { get; set; }=string.Empty;
    public string Backchannel { get; set; }=string.Empty;
    public string ProvisioningKind { get; set; }="None";
    public string ManagementEndpoint { get; set; }=string.Empty;
    public string ManagementSecretRef { get; set; }=string.Empty;
    public bool PasswordResetFlowVerified { get; set; }
    public string ApiProfilesJson { get; set; }="[]";
    // Фиксирует независимый профиль до await; JSON применяется только к заранее известной модели настроек.
    public IdentityProviderProfile Snapshot() => new()
    {
        Id=Id,Revision=Revision,Enabled=Enabled,ManagedLocal=ManagedLocal,Scopes=Scopes.ToArray(),SubjectMode=SubjectMode,
        PasswordResetAttribute=PasswordResetAttribute,Name=Name.Trim(),Issuer=Issuer.Trim(),MetadataAddress=MetadataAddress.Trim(),ClientId=ClientId.Trim(),
        Claims=new(LoginClaim.Trim(),NameClaim.Trim(),EmailClaim.Trim()),
        ClientSecretRef=ClientSecretRef.Trim(),BackchannelBaseAddress=string.IsNullOrWhiteSpace(Backchannel)?null:Backchannel.Trim(),
        ProvisioningKind=ProvisioningKind,ManagementEndpoint=ManagementEndpoint.Trim(),ManagementSecretRef=ManagementSecretRef.Trim(),
        PasswordResetFlowVerified=PasswordResetFlowVerified,
        ApiTokenProfiles=JsonSerializer.Deserialize<ApiTokenProfile[]>(ApiProfilesJson,IdentityStateStore.Json)??throw new ArgumentException("Нет схемы API token profiles.")
    };
    // Перенос в форму не разделяет массивы/дочерние mutable значения сохранённой конфигурации.
    public static IdentityProfileForm From(IdentityProviderProfile profile) => new()
    {
        Id=profile.Id,Revision=profile.Revision,Enabled=profile.Enabled,ManagedLocal=profile.ManagedLocal,Scopes=profile.Scopes.ToArray(),
        SubjectMode=profile.SubjectMode,PasswordResetAttribute=profile.PasswordResetAttribute,
        LoginClaim=profile.Claims.Login,NameClaim=profile.Claims.DisplayName,EmailClaim=profile.Claims.Email,
        Name=profile.Name,Issuer=profile.Issuer,MetadataAddress=profile.MetadataAddress,
        ClientId=profile.ClientId,ClientSecretRef=profile.ClientSecretRef,Backchannel=profile.BackchannelBaseAddress??string.Empty,
        ProvisioningKind=profile.ProvisioningKind,ManagementEndpoint=profile.ManagementEndpoint,ManagementSecretRef=profile.ManagementSecretRef,
        PasswordResetFlowVerified=profile.PasswordResetFlowVerified,ApiProfilesJson=JsonSerializer.Serialize(profile.ApiTokenProfiles,IdentityStateStore.Json)
    };
}

// Сохраняет профиль и его immutable revision одной стандартной транзакцией всего рабочего экземпляра.
internal sealed class IdentityProfileWorkingSet(MiniAppSaveContext context,IdentityProviderAdministration administration) : IMiniAppSaveParticipant
{
    public IdentityProfileForm Form { get; private set; }=new();
    private IdentityProviderProfile _saved=new();
    private IdentityProviderProfile? _captured;
    private IdentityProviderProfile? _prepared;
    // Выбор другого профиля не уничтожает текущий dirty без Save/Discard.
    public void Load(IdentityProviderProfile profile)
    {if(context.IsDirty)throw new InvalidOperationException("Сначала сохраните или отмените правки.");_saved=profile;Form=IdentityProfileForm.From(profile);}
    // Синхронное изменение формы регистрируется до сетевых проверок и сохранения.
    public void Changed()=>context.MarkChanged();
    // Снимок не содержит паролей, client secrets или внешних access tokens.
    public EntitySaveBatch Capture(Guid operationId,Guid editSessionId,long revision)
    {_captured=Form.Snapshot();return new(operationId,"UserMiniApp",editSessionId);}
    // Подготавливает только строки; собственного commit внутри Prepare нет.
    public async Task<EntitySaveBatch> PrepareAsync(EntitySaveBatch captured,CancellationToken ct)
    {
        var prepared=await administration.PrepareAsync(_captured!,ct);_prepared=prepared.Profile;
        var changes=prepared.Rows.Select(row=>new UserTableMutation(row.GetType().Name,
            (Guid)row.GetType().GetProperty("Id")!.GetValue(row)!,JsonSerializer.Serialize(row,row.GetType(),GraphArchiveJson.CreateOptions()))).ToArray();
        return captured with {OwnedServiceChanges=[new("UserMiniApp","UserIdentityV1",JsonSerializer.Serialize(changes,GraphArchiveJson.CreateOptions()))]};
    }
    // Только после commit новый профиль появляется в runtime-каталоге; поздние правки формы сохраняются dirty.
    public async Task AcknowledgeAsync(EntitySaveBatch sent,SaveBatchResult result,long revision,CancellationToken ct)
    {
        await administration.PublishAsync(_prepared!);_saved=_prepared!;
        if(context.Revision==revision)Form=IdentityProfileForm.From(_prepared!);
        else Form.Revision=_prepared!.Revision;
    }
    // Отменяются только локальные поля формы, а не активный провайдер или чужие изменения.
    public Task DiscardAsync(CancellationToken ct)
    {Form=IdentityProfileForm.From(_saved);return Task.CompletedTask;}
}

// Техническая подготовка возвращает только owned User rows, не предоставляет вызывающему DB callback.
internal sealed record PreparedIdentityProfile(IdentityProviderProfile Profile,IReadOnlyList<object> Rows);
