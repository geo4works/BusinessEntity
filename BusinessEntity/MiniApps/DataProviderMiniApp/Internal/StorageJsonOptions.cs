using System.Text.Json;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal;

// Сохранение и архив используют одинаковый строгий контракт общих карточек и metadata.
internal static class StorageJsonOptions
{
    public static readonly JsonSerializerOptions Default = GraphArchiveJson.CreateOptions();
}
