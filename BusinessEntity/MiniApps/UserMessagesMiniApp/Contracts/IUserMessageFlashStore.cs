namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;

// Передаёт одно уведомление из HTTP login в новое окно circuit без рассылки по UserId.
public interface IUserMessageFlashStore
{
    // Выдаёт одноразовый непрозрачный ключ с коротким сроком жизни.
    Guid Create(Guid userId, string text, string title);
    // Забирает сообщение только для подтверждённого сервером пользователя.
    PostUserMessage? Take(Guid key, Guid userId, Guid logicalWindowId);
}
