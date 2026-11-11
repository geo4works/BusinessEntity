using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Internal;

internal static class SystemSettingsRules
{
    public static SystemSettingsValues View(SysParameters v)=>new(v.CompanyName,v.RichTextChunkCharLimit,v.RichTextInitialChunkCount,v.RichTextTableOfContentsBeforeBuffer,v.RichTextTableOfContentsAfterBuffer,v.RichTextScrollPreviousChunkCount,v.RichTextHideTableOfContentsScrollbar,v.RichTextEditChunksBeforeFocused,v.RichTextEditChunksAfterFocused,v.RichTextEditChunksOnOpen);
    // То же min-normalization UI/API, без новых выдуманных ключей config.
    public static void Apply(SysParameters data,SystemSettingsValues v)
    {
        data.Name="SysParameters";data.Tag=SysParametersTpType.Key;data.CompanyName=v.CompanyName?.Trim()??"";data.RichTextChunkCharLimit=Math.Max(1000,v.RichTextChunkCharLimit);data.RichTextInitialChunkCount=Math.Max(1,v.RichTextInitialChunkCount);data.RichTextTableOfContentsBeforeBuffer=Math.Max(0,v.RichTextTableOfContentsBeforeBuffer);data.RichTextTableOfContentsAfterBuffer=Math.Max(0,v.RichTextTableOfContentsAfterBuffer);data.RichTextScrollPreviousChunkCount=Math.Max(0,v.RichTextScrollPreviousChunkCount);data.RichTextHideTableOfContentsScrollbar=v.RichTextHideTableOfContentsScrollbar;data.RichTextEditChunksBeforeFocused=Math.Max(0,v.RichTextEditChunksBeforeFocused);data.RichTextEditChunksAfterFocused=Math.Max(0,v.RichTextEditChunksAfterFocused);data.RichTextEditChunksOnOpen=Math.Max(1,v.RichTextEditChunksOnOpen);
    }
    public static void ValidateWire(SystemSettingsValues v)
    {if(v.CompanyName==null||v.CompanyName.Length>1000||v.RichTextChunkCharLimit>65536||new[]{v.RichTextInitialChunkCount,v.RichTextTableOfContentsBeforeBuffer,v.RichTextTableOfContentsAfterBuffer,v.RichTextScrollPreviousChunkCount,v.RichTextEditChunksBeforeFocused,v.RichTextEditChunksAfterFocused,v.RichTextEditChunksOnOpen}.Any(x=>x>256))throw new ArgumentException("Превышен предел размера имени/окна чанков.");}
}
