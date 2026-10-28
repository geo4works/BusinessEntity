using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Internal;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Registration;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Bus;

// Проверяет реальную доставку уведомлений через singleton bus в scoped-окна.
public sealed class UserMessagesTests
{
    // Одна ошибка остаётся в своём окне даже при повторной публикации корреляции.
    [Fact]
    public void DeliversOnceOnlyToAddressedWindow()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(); collection.AddBusinessEntityBus(); collection.AddUserMessagesMiniApp();
        using var services = collection.BuildServiceProvider(validateScopes: true);
        using var first = services.CreateScope(); using var second = services.CreateScope();
        var user = Guid.NewGuid();
        var firstAddress = first.ServiceProvider.GetRequiredService<BusRequestScope>();
        firstAddress.SetUser(user);
        second.ServiceProvider.GetRequiredService<BusRequestScope>().SetUser(user);
        var firstMessages = first.ServiceProvider.GetRequiredService<UserMessagesService>();
        var secondMessages = second.ServiceProvider.GetRequiredService<UserMessagesService>();
        var bus = services.GetRequiredService<IMessageBus>();
        var message = new PostUserMessage(user, firstAddress.LogicalWindowId, Guid.NewGuid(), "Ошибка БД", UserMessageLevel.Error);
        bus.SendMessage(message); bus.SendMessage(message);
        Assert.Single(firstMessages.GetMessages(user));
        Assert.Empty(secondMessages.GetMessages(user));
        bus.SendMessage(message with { UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid() });
        Assert.Single(firstMessages.GetMessages(user));
    }

    // Одноразовый ключ login принадлежит подтверждённому пользователю и новому circuit.
    [Fact]
    public void LoginNotificationCannotBeConsumedByAnotherUserOrTwice()
    {
        var collection = new ServiceCollection(); collection.AddLogging();
        collection.AddBusinessEntityBus(); collection.AddUserMessagesMiniApp();
        using var services = collection.BuildServiceProvider();
        var flash = services.GetRequiredService<IUserMessageFlashStore>();
        var user = Guid.NewGuid(); var window = Guid.NewGuid();
        var key = flash.Create(user, "Вход выполнен", "Логин");
        Assert.Null(flash.Take(key, Guid.NewGuid(), window));
        var message = flash.Take(key, user, window);
        Assert.NotNull(message);
        Assert.Equal(window, message.LogicalWindowId);
        Assert.Null(flash.Take(key, user, window));
    }

    // Обёртка EF не превращает недоступность БД в неопределённую техническую ошибку.
    [Fact]
    public void MapsWrappedDatabaseFailure()
    {
        var error = BusError.FromException(new InvalidOperationException("EF wrapper", new Npgsql.NpgsqlException("private")));
        Assert.Equal("storage.unavailable", error.Code);
    }

    // Неправильный JSON и отказ драйвера не раскрывают технические подробности пользователю.
    [Theory]
    [InlineData(true, "payload.invalid")]
    [InlineData(false, "storage.unavailable")]
    public void MapsStorageFailuresToTypedPublicError(bool invalidJson, string code)
    {
        Exception failure = invalidJson ? new System.Text.Json.JsonException("secret path")
            : new Npgsql.NpgsqlException("secret connection string");
        var error = BusError.FromException(failure);
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("secret", error.Message);
    }
}
