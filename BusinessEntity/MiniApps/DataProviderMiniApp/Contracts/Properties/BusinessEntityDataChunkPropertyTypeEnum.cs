namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Properties;

/// <summary>
/// Типы технических свойств, привязанных к BusinessEntityDataChunkDto.
/// </summary>
public enum BusinessEntityDataChunkPropertyTypeEnum
{
    // Неопределенный тип свойства.
    Undefined = 0,

    // Содержание текстового куска, сформированного при построении RichTextDocument.
    RichDocTableOfContents = 100
}
