using BusinessEntity.Core.Classes;
namespace BusinessEntity.MiniApps.DocumentsMiniApp.Contracts;
// Владелец объявляет неизменяемый numeric ID и ключ собственного типа.
public static class DocumentType
{
    public const BusinessEntityTypeEnum Id = (BusinessEntityTypeEnum)300;
    public const string Key = "Document";
}
