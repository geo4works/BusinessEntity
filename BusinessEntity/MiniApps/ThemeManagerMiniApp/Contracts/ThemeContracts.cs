using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace BusinessEntity.MiniApps.ThemeManagerMiniApp.Contracts;
// Один статический контракт редактора, C# validator и раннего JS применения.
public static class ThemeSchema
{
    public static readonly string Json = Read();
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static readonly ThemeFields Fields = JsonSerializer.Deserialize<ThemeFields>(Json, Options)!;
    public static readonly Guid BuiltInId = Guid.Parse("ae160cf4-56fa-4d12-921b-c40ab5e1e28e");
    // Читает встроенную схему из того же ресурса, который получает браузер.
    private static string Read()
    {
        using var reader = new StreamReader(typeof(ThemeSchema).Assembly.GetManifestResourceStream("BusinessEntity.ThemeSchema.json")!);
        return reader.ReadToEnd();
    }
    // Создаёт независимую неизменяемую основу пользовательского каталога.
    public static BeTheme BuiltIn() => new() { Id = BuiltInId, Name = "BusinessEntity Light", Colors = Fields.Colors.ToDictionary(x => x.Key, x => x.Value), Numbers = Fields.Numbers.ToDictionary(x => x.Key, x => x.Value), MessageDesign = Fields.Designs[0], Tables = [new() { Id = Guid.Parse("94c4ca40-88fa-433e-ae11-df0dcf9b6721"), Name = "Стандартный", Header = "#E9ECEF", Background = "#FFFFFF", Alternate = "#F8F9FA", Selection = "#DCEEFF", Text = "#252525", Border = "#D6D5D5" }], ActiveTableStyleId = Guid.Parse("94c4ca40-88fa-433e-ae11-df0dcf9b6721") };
    // Строгая проверка полного snapshot не допускает частичную тему или CSS-подстановку.
    public static void Validate(BeTheme t)
    {
        if (t == null)
            throw new InvalidDataException("Пустая тема.");
        if (t.Id == Guid.Empty || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 100 || t.Name.Any(char.IsControl))
            throw new InvalidDataException("Некорректное имя или ID темы.");
        if (t.Colors == null || t.Numbers == null || !t.Colors.Keys.Order().SequenceEqual(Fields.Colors.Select(x => x.Key).Order()) || !t.Numbers.Keys.Order().SequenceEqual(Fields.Numbers.Select(x => x.Key).Order()))
            throw new InvalidDataException("Неизвестные или отсутствующие параметры темы.");
        foreach (var c in t.Colors.Values)
            Color(c);
        foreach (var f in Fields.Numbers)
            if (!double.IsFinite(t.Numbers[f.Key]) || t.Numbers[f.Key] < f.Min || t.Numbers[f.Key] > f.Max || (f.Max > 1 && t.Numbers[f.Key] % 1 != 0))
                throw new InvalidDataException("Параметр вне диапазона: " + f.Label);
        if (!Fields.Designs.Contains(t.MessageDesign))
            throw new InvalidDataException("Неизвестный дизайн сообщений.");
        if (t.Tables == null || t.Tables.Count is < 1 or > 50 || t.Tables.Select(x => x.Id).Distinct().Count() != t.Tables.Count || !t.Tables.Any(x => x.Id == t.ActiveTableStyleId))
            throw new InvalidDataException("Некорректный каталог стилей таблиц.");
        foreach (var style in t.Tables)
        {
            if (style.Id == Guid.Empty || string.IsNullOrWhiteSpace(style.Name) || style.Name.Length > 100)
                throw new InvalidDataException("Некорректный стиль таблицы.");
            foreach (var c in new[] { style.Header, style.Background, style.Alternate, style.Selection, style.Text, style.Border })
                Color(c);
        }
    }
    // Принимает только шестизначный HEX, без URL, CSS и прозрачных строковых выражений.
    public static void Color(string? color)
    {
        if (color == null || !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidDataException("Ожидается цвет #RRGGBB.");
    }
    // JSON copy не сохраняет ссылок на живой draft.
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;
    // Имя копии получает минимальный свободный суффикс.
    public static string FreeName(string name, IEnumerable<string> names)
    {
        var used = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = Regex.Replace(name, "_[0-9]+$", "");
        if (!used.Contains(stem))
            return stem;
        for (var i = 2; ; i++)
            if (!used.Contains(stem + "_" + i))
                return stem + "_" + i;
    }
    // Проверяет весь namespace каталога до единственной атомарной записи.
    public static void ValidateCatalog(ThemeCatalog c)
    {
        if (c.SchemaVersion != 1 || c.Themes == null || c.Palette == null || c.Palette.Length != 16 || c.Themes.Count > 100 || c.Themes.Any(x => x == null || x.Id == BuiltInId || string.Equals(x.Name, "BusinessEntity Light", StringComparison.OrdinalIgnoreCase)) || c.Themes.Select(x => x.Id).Distinct().Count() != c.Themes.Count || c.Themes.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != c.Themes.Count)
            throw new InvalidDataException("Каталог тем повреждён или его версия не поддерживается.");
        foreach (var t in c.Themes)
            Validate(t);
        foreach (var color in c.Palette)
            if (color != null)
                Color(color);
        if (c.ActiveThemeId != BuiltInId && !c.Themes.Any(x => x.Id == c.ActiveThemeId))
            throw new InvalidDataException("Активная тема не найдена.");
    }
    // Import имеет собственный marker и версию, Geo4 payload не принимается как BE.
    public static BeTheme Import(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 262144)
            throw new InvalidDataException("Тема больше 256 KiB.");
        var envelope = JsonSerializer.Deserialize<ThemeExchange>(json, Options) ?? throw new InvalidDataException("Пустой файл темы.");
        if (envelope.Marker != "BusinessEntityTheme" || envelope.SchemaVersion != 1)
            throw new InvalidDataException("Неподдерживаемый формат темы.");
        Validate(envelope.Theme);
        return envelope.Theme;
    }
}
// Поля общие для модели, редактора и браузерного allowlist.
public sealed record ThemeColorField(string Key, string Section, string Label, string Value);
public sealed record ThemeNumberField(string Key, string Section, string Label, double Min, double Max, double Value);
public sealed record ThemeFields(ThemeColorField[] Colors, ThemeNumberField[] Numbers, string[] Designs);
// Полный переносимый snapshot без полей identity и секретов приложения.
public sealed class BeTheme
{
    public required Guid Id
    {
        get; set;
    }
    public required string Name
    {
        get; set;
    }
    public required Dictionary<string, string> Colors
    {
        get; set;
    }
    public required Dictionary<string, double> Numbers
    {
        get; set;
    }
    public required string MessageDesign
    {
        get; set;
    }
    public required List<BeTableStyle> Tables
    {
        get; set;
    }
    public required Guid ActiveTableStyleId
    {
        get; set;
    }
}
// Стиль административных таблиц не применяется к содержимому пользовательских статей.
public sealed class BeTableStyle
{
    public required Guid Id
    {
        get; set;
    }
    public required string Name
    {
        get; set;
    }
    public required string Header
    {
        get; set;
    }
    public required string Background
    {
        get; set;
    }
    public required string Alternate
    {
        get; set;
    }
    public required string Selection
    {
        get; set;
    }
    public required string Text
    {
        get; set;
    }
    public required string Border
    {
        get; set;
    }
}
// Один atomic localStorage snapshot на пользователя и origin.
public sealed class ThemeCatalog
{
    public required int SchemaVersion
    {
        get; set;
    }
    public required Guid ActiveThemeId
    {
        get; set;
    }
    public required List<BeTheme> Themes
    {
        get; set;
    }
    public required string?[] Palette
    {
        get; set;
    }
    // Отсутствие каталога имеет явное начальное состояние.
    public static ThemeCatalog Empty() => new() { SchemaVersion = 1, ActiveThemeId = ThemeSchema.BuiltInId, Themes = [], Palette = new string?[16] };
}
// Версионный файл обмена BE не утверждает совместимость с Geo4 themes.
public sealed record ThemeExchange(string Marker, int SchemaVersion, BeTheme Theme);

// Адрес выбранного слота пользовательской палитры.
public sealed record PaletteChange(int Slot, string Color);
