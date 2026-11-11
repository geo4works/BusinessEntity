namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

// DTO команды создания или обновления локального пользователя из административного UI.
public sealed class UserAdministrationSaveRequest
{
    public string Login { get; set; } = string.Empty;
    public string DisplayedName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
