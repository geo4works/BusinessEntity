using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Строит настройки представления статьи из обязательных валидированных параметров системы.
public sealed class RichTextDocumentSettingsService(BusinessEntityHelper entities)
{
    // Отказ чтения не заменяется конфигурационным fallback или созданием записи из UI.
    public async Task<RichTextDocumentSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var entity = await entities.GetRequiredSingletonEntityAsync<SysParameters>(SysParametersTpType.Id, "SysParameters", cancellationToken);
        var data = entity.Data;
        return new()
        {
            InitialChunkCount = data.RichTextInitialChunkCount,
            TableOfContentsBeforeBuffer = data.RichTextTableOfContentsBeforeBuffer,
            TableOfContentsAfterBuffer = data.RichTextTableOfContentsAfterBuffer,
            ScrollPreviousChunkCount = data.RichTextScrollPreviousChunkCount,
            HideTableOfContentsScrollbar = data.RichTextHideTableOfContentsScrollbar,
            EditChunksBeforeFocused = data.RichTextEditChunksBeforeFocused,
            EditChunksAfterFocused = data.RichTextEditChunksAfterFocused,
            EditChunksOnOpen = data.RichTextEditChunksOnOpen
        };
    }
}
