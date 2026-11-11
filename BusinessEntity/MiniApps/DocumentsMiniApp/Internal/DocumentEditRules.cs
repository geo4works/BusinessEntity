using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Internal;

// Точно один validation/freeze полного снимка используется UI working set и headless Save.
internal static class DocumentEditRules
{
    public static Document Freeze(Document source,EntityTypeRegistry types)
    {
        if(string.IsNullOrWhiteSpace(source.Name))throw new ArgumentException("Название документа обязательно.");
        if(source.Text==null)throw new ArgumentException("Content обязателен; пустой текст допустим.");
        var copy=(Document)types.Get(source.EntityType).Clone(source);copy.Name=copy.Name.Trim();return copy;
    }
}
