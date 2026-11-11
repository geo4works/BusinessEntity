namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Строка плана описывает доказательство привязки и состояние внешнего эффекта, но не пароль.
internal sealed record IdentitySwitchEntry(Guid UserId,string Login,string Kind,string Status,
    Guid ProvisioningOperationId,string? AccountId=null,string? Subject=null,string? Error=null);
// Одноразовый результат только собственного UI; не является bus contract или durable payload.
internal sealed record IdentityProvisioningCredential(string Login,string Secret,string Kind="Human");
