namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

// DTO команды обновления текущего пользовательского профиля.
public sealed class UserProfileUpdateRequest
{
    public string DisplayedName { get; set; } = string.Empty;
}
