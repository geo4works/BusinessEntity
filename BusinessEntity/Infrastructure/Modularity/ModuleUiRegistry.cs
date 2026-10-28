using Microsoft.AspNetCore.Components;

namespace BusinessEntity.Infrastructure.Modularity;

// Запись UI создаётся только локально в Registration владельца.
public sealed record ModuleComponentRegistration(string Key, Type ComponentType);
// Разрешает локальный ключ компонента и явно отклоняет неизвестный обязательный ключ.
public interface IModuleUiFactory
{
    // Получает тип для DynamicComponent без передачи Type по шине.
    Type Resolve(string key);
}
// Единый проверенный реестр, не зависящий от конкретных доменов.
public sealed class ModuleUiFactory : IModuleUiFactory
{
    private readonly IReadOnlyDictionary<string, Type> _components;
    // Дублирование ключей считается ошибкой композиции, а не выбором первой записи.
    public ModuleUiFactory(IEnumerable<ModuleComponentRegistration> registrations)
    {
        _components = registrations.ToDictionary(x => x.Key, x => x.ComponentType, StringComparer.Ordinal);
        if (_components.Any(x => !typeof(IComponent).IsAssignableFrom(x.Value)))
            throw new InvalidOperationException("В реестре UI указан тип, не являющийся компонентом.");
    }
    // Неизвестный ключ не подменяется пустым UI.
    public Type Resolve(string key) => _components.TryGetValue(key, out var component) ? component
        : throw new KeyNotFoundException($"Компонент модуля '{key}' не зарегистрирован.");
}
