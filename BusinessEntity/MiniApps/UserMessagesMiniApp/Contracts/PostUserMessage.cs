namespace BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;

// Однократное сообщение адресату своего окна с корреляцией операции.
public sealed record PostUserMessage(Guid UserId, Guid LogicalWindowId, Guid CorrelationId,
    string Text, UserMessageLevel Level = UserMessageLevel.Info, string? Title = null);
