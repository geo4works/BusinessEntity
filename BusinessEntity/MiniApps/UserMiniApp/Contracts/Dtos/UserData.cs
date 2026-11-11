namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

// Локальный профиль пользователя не содержит идентификаторов конкретного провайдера.
public sealed class UserData
{
    public string DisplayedName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
