using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Types;

namespace BusinessEntity.Core.Services;

// Общий поставщик Contains правил, объявленных владельцами дочерних типов.
public sealed class PossibleEntityRelationTypesProvider : IPossibleEntityRelationTypesProvider
{
    private readonly EntityTypeRegistry _types;
    // Не содержит списка предметных типов.
    public PossibleEntityRelationTypesProvider(EntityTypeRegistry types) => _types = types;
    // Новое расширение получает вложение через свой описатель без правки Core.
    public IEnumerable<MacroRelationType> GetPossibleRelations() => _types.All.SelectMany(child =>
        child.AllowedParentTypes.Select(parent => new MacroRelationType($"contains:{_types.Get(parent).TypeKey}:{child.TypeKey}",
            parent, child.EntityType, BusinessEntityRelationTypeEnum.Contains)));
}
