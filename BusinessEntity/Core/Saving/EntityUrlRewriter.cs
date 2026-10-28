using System.Text.RegularExpressions;

namespace BusinessEntity.Core.Saving;

// Публичный шаблон ссылки объявляет владелец маршрута, а не общий движок.
public sealed record EntityUrlPattern(string Prefix);
// Перепривязывает только зарегистрированные ссылки на сущности без знания конкретных доменов.
public sealed class EntityUrlRewriter
{
    private readonly Regex _links;
    // Объединяет объявления владельцев; совпадение обычного GUID в тексте не является ссылкой.
    public EntityUrlRewriter(IEnumerable<EntityUrlPattern> patterns)
    {
        var prefixes = patterns.Select(x => x.Prefix).Distinct().OrderByDescending(x => x.Length).Select(Regex.Escape).ToArray();
        _links = new Regex(prefixes.Length == 0 ? "(?!)" : "(?<![a-zA-Z0-9/:])(?<prefix>" + string.Join("|", prefixes) + ")(?<id>[0-9a-fA-F-]{36})(?![a-zA-Z0-9-])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
    // Внешняя ссылка вне переносимого подграфа сохраняется.
    public string Rewrite(string text, IReadOnlyDictionary<Guid, Guid> entities) => _links.Replace(text, match =>
        Guid.TryParse(match.Groups["id"].Value, out var id) && entities.TryGetValue(id, out var replacement)
            ? match.Groups["prefix"].Value + replacement.ToString("D") : match.Value);
}
