using System.Security.Cryptography;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Прямое создание пользователя собственным UI; конкретный каталог скрыт адаптером.
internal sealed class UserAccountProvisioning(IdentityDirectory directory, IdentityStateStore store,
    IIdentityProviderRegistry registry, IdentityProvisioners provisioners)
{
    private string? _createdPassword;
    // Пароль случайный и одноразово выдаётся только в памяти инициировавшего UI scope.
    public async Task<UserDto> CreateAsync(string login, CancellationToken ct)
    {
        var profile = registry.Current.Profile; var adapter = provisioners.Get(profile);
        if (await adapter.FindAsync(profile, login, ct) != null) throw new InvalidOperationException("identity.username_conflict");
        var password = GeneratePassword();
        var external = await adapter.CreateAsync(profile, login, password, Guid.NewGuid(), ct);
        var user = await directory.ResolveAsync(new(profile.Id, external.Issuer, external.Subject, external.Login, external.DisplayName, null), ct);
        await store.ReadAsync(async db =>
        {
            var link = await db.ExternalIdentities.SingleAsync(x => x.Issuer == external.Issuer && x.Subject == external.Subject, ct);
            link.AccountId = external.AccountId; link.RequiresPasswordChange = true; await db.SaveChangesAsync(ct); return true;
        });
        _createdPassword = password; return user;
    }
    // Отдельный direct-call собственного UI не публикует пароль в общем response/event.
    public string? ConsumeCreatedPassword() => Interlocked.Exchange(ref _createdPassword, null);
    // Обновление проверяет только идентичности уже существующих привязок.
    public async Task RefreshLinkedAsync(CancellationToken ct)
    {
        var profile = registry.Current.Profile; var adapter = provisioners.Get(profile);
        var links = await store.ReadAsync(db => db.ExternalIdentities.Where(x => x.ProviderId == profile.Id && x.Enabled && x.AccountId != "").ToArrayAsync(ct));
        foreach (var link in links)
        {
            var account = await adapter.ReadAsync(profile, link.AccountId, ct);
            if (account.Issuer != link.Issuer || account.Subject != link.Subject || !account.Enabled)
                throw new InvalidOperationException("Внешняя привязка недоступна или изменилась.");
        }
    }
    // Явная административная смена начального пароля не изменяет чужой аккаунт по имени.
    public async Task SetTemporaryPasswordAsync(Guid userId, string password, CancellationToken ct)
    {
        var profile = registry.Current.Profile;
        var link = await store.ReadAsync(db => db.ExternalIdentities.SingleAsync(x => x.UserId == userId && x.ProviderId == profile.Id && x.Enabled, ct));
        await provisioners.Get(profile).SetTemporaryPasswordAsync(profile, link.AccountId, password, ct);
        await store.ReadAsync(async db => { var actual = await db.ExternalIdentities.SingleAsync(x => x.Id == link.Id, ct);
            actual.RequiresPasswordChange = true; await db.SaveChangesAsync(ct); return true; });
    }
    // Содержит разные классы символов и не зависит от имени/дефолтного пароля установки.
    public static string GeneratePassword() => "aA1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
}
