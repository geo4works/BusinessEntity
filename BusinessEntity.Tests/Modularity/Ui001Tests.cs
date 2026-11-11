using System.Text.Json;
using BusinessEntity.Infrastructure.Settings;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Contracts;
using Xunit;
namespace BusinessEntity.Tests.Modularity;
// Проверяет недоверенные theme payload и координацию нескольких независимых участников.
public sealed class Ui001Tests
{
    // Полный export/import сохраняет все параметры и использует собственный формат BE.
    [Fact]
    public void ThemeExchangeRoundTrips()
    {
        var theme = ThemeSchema.BuiltIn();
        var json = JsonSerializer.Serialize(new ThemeExchange("BusinessEntityTheme", 1, theme), ThemeSchema.Options);
        var restored = ThemeSchema.Import(json);
        Assert.Equal(JsonSerializer.Serialize(theme, ThemeSchema.Options), JsonSerializer.Serialize(restored, ThemeSchema.Options));
    }
    // Неизвестные поля не превращаются в незаметно потерянные настройки.
    [Fact]
    public void UnknownExchangeFieldRejected()
    {
        var json = JsonSerializer.Serialize(new ThemeExchange("BusinessEntityTheme", 1, ThemeSchema.BuiltIn()), ThemeSchema.Options);
        Assert.Throws<JsonException>(() => ThemeSchema.Import(json[..^1] + ",\"unknown\":true}"));
    }
    // Неполный payload нельзя принять за корректную тему со значениями по умолчанию.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"marker\":\"Geo4Theme\",\"schemaVersion\":19,\"theme\":null}")]
    public void MissingAndForeignPayloadRejected(string json)
    {
        Assert.ThrowsAny<Exception>(() => ThemeSchema.Import(json));
    }
    // CSS/URL и некорректные числовые параметры не попадают в runtime mapper.
    [Fact]
    public void UnsafeColorAndRangeRejected()
    {
        var t = ThemeSchema.BuiltIn();
        t.Colors["primary"] = "url(https://invalid/)";
        Assert.Throws<InvalidDataException>(() => ThemeSchema.Validate(t));
        t = ThemeSchema.BuiltIn();
        t.Numbers["message-accent-width"] = 65;
        Assert.Throws<InvalidDataException>(() => ThemeSchema.Validate(t));
    }
    // Дополнительный словарный ключ также является ошибкой схемы.
    [Fact]
    public void ExtraThemeRoleRejected()
    {
        var t = ThemeSchema.BuiltIn();
        t.Colors["custom"] = "#FFFFFF";
        Assert.Throws<InvalidDataException>(() => ThemeSchema.Validate(t));
    }
    // Активный стиль обязан принадлежать целому каталогу.
    [Fact]
    public void MissingStyleRejected()
    {
        var t = ThemeSchema.BuiltIn();
        t.ActiveTableStyleId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => ThemeSchema.Validate(t));
    }
    // Нельзя перезаписать встроенную identity или сослаться на отсутствующую тему.
    [Fact]
    public void InvalidCatalogCannotReplaceBuiltIn()
    {
        var c = ThemeCatalog.Empty();
        c.Themes.Add(ThemeSchema.BuiltIn());
        Assert.Throws<InvalidDataException>(() => ThemeSchema.ValidateCatalog(c));
        c = ThemeCatalog.Empty();
        c.ActiveThemeId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => ThemeSchema.ValidateCatalog(c));
    }
    // Палитра имеет ровно 16 слотов, а локальные имена уникальны без учёта регистра.
    [Fact]
    public void PaletteAndNamesAreStrict()
    {
        var c = ThemeCatalog.Empty();
        c.Palette = new string?[15];
        Assert.Throws<InvalidDataException>(() => ThemeSchema.ValidateCatalog(c));
        Assert.Equal("Example_3", ThemeSchema.FreeName("Example_9", ["Example", "Example_2"]));
    }
    // Отказ второго участника оставляет его draft и не повторяет уже подтверждённый первый.
    [Fact]
    public async Task PartialApplyKeepsRemainingDraft()
    {
        var s = new SettingsDraftSession([]);
        var a = new Draft();
        var b = new Draft { Failure = true };
        s.Register(a);
        s.Register(b);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.ApplyAsync());
        Assert.False(a.IsDirty);
        Assert.True(b.IsDirty);
        b.Failure = false;
        await s.ApplyAsync();
        Assert.Equal(1, a.Applies);
        Assert.Equal(2, b.Applies);
        Assert.False(s.IsDirty);
    }
    // Отмена после успешного Apply не вызывает повторной записи.
    [Fact]
    public async Task CancelAndUnregisterAreLocal()
    {
        var s = new SettingsDraftSession([]);
        var a = new Draft();
        s.Register(a);
        s.Register(a);
        await s.CancelAsync();
        Assert.False(s.IsDirty);
        Assert.Equal(1, a.Cancels);
        s.Unregister(a);
        Assert.False(s.IsDirty);
    }
    // Исключение внутренней навигации ограничено явно объявленными маршрутами.
    [Fact]
    public void SettingsRouteBoundaryIsExplicit()
    {
        var s = new SettingsDraftSession([new("/administration/users", "Users", "User:Page", 0, "GlobalAdmin", AcceptSubroutes: true)]);
        Assert.True(s.ContainsRoute("/administration/users/roles"));
        Assert.False(s.ContainsRoute("/administration/unknown"));
        Assert.False(s.ContainsRoute("/administration/users-other"));
    }
    // Relative NavigateTo и внешний URL проходят разные ветви guard без UriFormatException.
    [Fact]
    public void RelativeNavigationAndForeignOrigin()
    {
        var s = new SettingsDraftSession([new("/administration/system", "System", "Sys:Page", 0, "GlobalAdmin")]);
        var current = new Uri("https://example.invalid/administration/system");
        Assert.True(s.CanKeepDraftsForNavigation(current, new Uri(current, "/administration")));
        Assert.False(s.CanKeepDraftsForNavigation(current, new Uri(current, "/")));
        Assert.False(s.CanKeepDraftsForNavigation(current, new Uri("https://other.invalid/administration/system")));
    }
    // Независимый участник эмулирует подтверждение и ошибку внешнего хранилища.
    private sealed class Draft : ISettingsDraft
    {
        public bool IsDirty { get; private set; } = true; public bool Failure; public int Applies, Cancels;
        // Подтверждает тестовый draft либо эмулирует отказ.
        public Task ApplyAsync(CancellationToken ct = default)
        {
            Applies++;
            if (Failure)
                throw new InvalidOperationException("storage failure");
            IsDirty = false;
            return Task.CompletedTask;
        }
        // Отбрасывает тестовый draft без записи.
        public Task CancelAsync(CancellationToken ct = default)
        {
            Cancels++;
            IsDirty = false;
            return Task.CompletedTask;
        }
    }
}
