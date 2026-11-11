namespace BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;

// Постоянная локальная учётная запись; внешние identities находятся в отдельных привязках.
public sealed class UserDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Login { get; set; } = string.Empty;
    public string NormalizedLogin { get; set; } = string.Empty;
    public string Kind { get; set; } = "Human";
    public bool Enabled { get; set; } = true;
    public string Payload { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime DateLastModified { get; set; } = DateTime.UtcNow;
}
