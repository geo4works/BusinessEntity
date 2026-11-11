namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

// DTO строки локального пользователя для административного UI user mini-app.
public sealed class UserAdministrationRecord
{
    public Guid Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string DisplayedName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime DateLastModified { get; set; }
}
