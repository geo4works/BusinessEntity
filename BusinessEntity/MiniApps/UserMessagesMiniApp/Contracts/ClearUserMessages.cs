namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;

// Очищает сообщения только указанного окна и пользователя.
public sealed record ClearUserMessages(Guid UserId, Guid LogicalWindowId);
