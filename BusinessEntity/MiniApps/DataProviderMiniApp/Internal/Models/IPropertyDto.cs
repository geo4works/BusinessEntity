

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

/// <summary>
/// Общий контракт storage-property, привязанной к строке родительской DTO-таблицы.
/// </summary>
internal interface IPropertyDto : IBaseEntity
{
    Guid ParentEntityId { get; set; }
    int PropertyType { get; set; }
    string Data { get; set; }
    string Metadata { get; set; }
}
