using System.Collections.Immutable;

namespace BusinessEntity.Infrastructure.Selection;

// Значения выбранного объекта не раскрывают изменяемую entity или ViewModel владельца.
public sealed record SelectedObjectSnapshot(Guid EntityId, string Title, string TypeKey,
    DateTime? CreatedDate, DateTime? ModifiedDate);
// Адрес активного master меняется независимо от его локальной ревизии выделения.
public sealed record SelectionSourceAddress(Guid OriginModuleId, long SourceRevision);
// Событие содержит только неизменяемые значения и полный адрес источника.
public sealed record ObjectInContextSelected(Guid LogicalWindowId, Guid OriginModuleId,
    long SelectionRevision, long SourceRevision, ImmutableArray<SelectedObjectSnapshot> Objects);
// Изменение активного источника очищает старые данные helper до следующего снимка.
public sealed record SelectionSourceChanged(Guid LogicalWindowId, SelectionSourceAddress? Source);
// Поздний helper запрашивает текущий снимок через общую шину.
public sealed record GiveMeCurrentSelection(Guid RequestId, Guid LogicalWindowId, Guid RequesterModuleId);
// Корреляция ответа не позволяет запоздалому запросу подменить текущее выделение.
public sealed record HereIsCurrentSelection(Guid RequestId, ObjectInContextSelected Snapshot);
