using BusinessEntity.Core.Services;
using BusinessEntity.WebLogger.Services;
using Xunit;

namespace BusinessEntity.Tests.Core;

// Закрепляет правила допустимых пользовательских имён.
public sealed class EntityNameValidatorTests
{
    private readonly EntityNameValidator _validator = new(new SilentWebLogger());

    // Имена поддерживают русский/латинский текст и разделители, но требуют букву.
    [Theory]
    [InlineData("Документ", true)]
    [InlineData("Ёжик 2026", true)]
    [InlineData("Release_9-0", true)]
    [InlineData("  Моя папка  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("123", false)]
    [InlineData("___---", false)]
    [InlineData("../Папка", false)]
    [InlineData("<script>", false)]
    [InlineData("Документ/два", false)]
    [InlineData("Имя:документа", false)]
    public void ValidatesNames(string name, bool expected)
    {
        Assert.Equal(expected, _validator.IsValidEntityName(name));
    }

    // Заглушка исключает сетевые запросы веб-логгеру из проверки чистого правила.
    private sealed class SilentWebLogger : IWebLoggerService
    {
        // Принимает информационную запись без внешнего эффекта.
        public Task Information(string text) => Task.CompletedTask;
        // Принимает предупреждение без внешнего эффекта.
        public Task Warning(string text) => Task.CompletedTask;
        // Принимает текст ошибки без внешнего эффекта.
        public Task Error(string text) => Task.CompletedTask;
        // Принимает исключение без внешнего эффекта.
        public Task Error(Exception ex) => Task.CompletedTask;
        // Принимает диагностическую запись без внешнего эффекта.
        public Task Debug(string text) => Task.CompletedTask;
        // Настройка сетевого отправителя для заглушки не требуется.
        public void SetActiveStatus(bool newStatus) { }
        // Принимает объект без сериализации или отправки.
        public Task SendObject(object data) => Task.CompletedTask;
    }
}
