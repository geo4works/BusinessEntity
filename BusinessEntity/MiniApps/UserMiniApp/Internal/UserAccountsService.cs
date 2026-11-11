using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Общие пользовательские сценарии используют локальные записи и выбранный provisioning adapter.
internal sealed class UserAccountsService(IUserMiniAppRepository<UserDto> users, UserIdentityService identity,
    UserSecurityAuthority authority, UserAccountProvisioning provisioning, UserAccountRemoval removal)
{
    // Локальная запись текущего пользователя не определяется повторно по внешнему username.
    public async Task<bool> DeleteCurrentUserAsync(CancellationToken ct = default)
    {
        var current = await identity.EnsureCurrentUserAsync(ct); if (current == null) return false;
        await removal.DeleteAsync(current.Id, ct); return true;
    }
    // Административный список не требует внешнего API при обычном открытии.
    public async Task<IReadOnlyList<UserAdministrationRecord>> GetAdministrationUsersAsync(CancellationToken ct = default)
    {
        await authority.RequireGlobalAdminAsync(ct);
        return UserValueRules.SortAdministrationRecords((await users.GetAllAsync(null, ct)).Select(UserValueRules.MapLocalAdministrationRecord));
    }
    // Проверяет только уже связанные аккаунты, не присваивая локальным пользователям совпавшие имена каталога.
    public async Task<IReadOnlyList<UserAdministrationRecord>> RefreshAdministrationUsersAsync(CancellationToken ct = default)
    { await authority.RequireGlobalAdminAsync(ct); await provisioning.RefreshLinkedAsync(ct); return await GetAdministrationUsersAsync(ct); }
    // Начальный пароль доступен только собственному UI однократным Consume, не публичному DTO/шине.
    public async Task<UserAdministrationRecord> CreateAdministrationUserAsync(CancellationToken ct = default)
    {
        await authority.RequireGlobalAdminAsync(ct);
        var result = await provisioning.CreateAsync("user-" + Guid.NewGuid().ToString("N")[..8], ct);
        return UserValueRules.MapLocalAdministrationRecord(result);
    }
    // Собственный UI получает секрет отдельным прямым вызовом, после которого значение удаляется.
    public string? ConsumeCreatedPassword() => provisioning.ConsumeCreatedPassword();
    // Не меняет логин/identity владельца через редактирование локального профиля.
    public async Task<UserAdministrationRecord> UpdateAdministrationUserAsync(Guid id, UserAdministrationSaveRequest request, CancellationToken ct = default)
    {
        await authority.RequireGlobalAdminAsync(ct);
        var user = await users.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException();
        if (UserValueRules.IsSystemUser(user)) throw new InvalidOperationException("Системный пользователь не редактируется.");
        if (request.Login != user.Login) throw new NotSupportedException("Логин существующей учётной записи неизменяем в этой версии.");
        if (!string.IsNullOrWhiteSpace(request.Password)) await provisioning.SetTemporaryPasswordAsync(id, request.Password, ct);
        var data = UserValueRules.ReadUserData(user); data.DisplayedName = UserValueRules.NormalizeRequiredText(request.DisplayedName, "Отображаемое имя");
        user.Payload = UserValueRules.SerializeUserData(data); user.DateLastModified = DateTime.UtcNow;
        await users.UpdateAsync(user, ct); return UserValueRules.MapLocalAdministrationRecord(user);
    }
    // Удаление локальной учётки не удаляет автоматически аккаунт чужого провайдера.
    public async Task<bool> DeleteAdministrationUserAsync(Guid id, CancellationToken ct = default)
    {
        var current = await authority.RequireGlobalAdminAsync(ct);
        if (current.Id == id) throw new InvalidOperationException("Нельзя удалить собственную административную учётную запись.");
        var user = await users.GetByIdAsync(id, ct); if (user == null) return false;
        if (UserValueRules.IsSystemUser(user)) throw new InvalidOperationException("Системный пользователь не удаляется.");
        await removal.DeleteAsync(id, ct); return true;
    }
    // Профиль приложения имеет собственное отображаемое имя и не хранит пароль провайдера.
    public async Task<UserProfileDto?> GetProfileAsync(CancellationToken ct = default)
    {
        var user = await identity.EnsureCurrentUserAsync(ct);
        return user == null ? null : new() { UserId = user.Id, Login = user.Login, DisplayedName = UserValueRules.ReadUserData(user).DisplayedName };
    }
    // Свои профильные сведения меняются без обращения к vendor-specific API.
    public async Task<UserProfileDto> UpdateProfileAsync(UserProfileUpdateRequest request, CancellationToken ct = default)
    {
        var user = await identity.EnsureCurrentUserAsync(ct) ?? throw new UnauthorizedAccessException();
        var data = UserValueRules.ReadUserData(user); data.DisplayedName = UserValueRules.NormalizeRequiredText(request.DisplayedName, "Отображаемое имя");
        user.Payload = UserValueRules.SerializeUserData(data); user.DateLastModified = DateTime.UtcNow;
        await users.UpdateAsync(user, ct);
        return new() { UserId = user.Id, Login = user.Login, DisplayedName = data.DisplayedName };
    }
}
