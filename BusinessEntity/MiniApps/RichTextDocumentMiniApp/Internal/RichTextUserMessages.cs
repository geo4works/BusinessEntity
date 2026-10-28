using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Предметный производитель сообщений статьи в общей панели своего окна.
public sealed class RichTextUserMessages
{
    private readonly IMessageBus _bus;
    private readonly BusRequestScope _scope;
    // Получает только публичный транспорт и текущий серверный адрес.
    public RichTextUserMessages(IMessageBus bus, BusRequestScope scope) { _bus = bus; _scope = scope; }
    // Не очищает сообщения других предметных операций при переходе между статьями.
    public void Add(Guid entityId, string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var userId = _scope.UserId ?? throw new InvalidOperationException("Не установлен адресат сообщения статьи.");
        _bus.SendMessage(new PostUserMessage(userId, _scope.LogicalWindowId, Guid.NewGuid(), message.Trim(), Title: "Статья"));
    }
}
