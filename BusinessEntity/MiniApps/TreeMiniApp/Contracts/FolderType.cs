using BusinessEntity.Core.Classes;
namespace BusinessEntity.MiniApps.TreeMiniApp.Contracts;
// Владелец объявляет неизменяемый numeric ID и ключ собственного типа.
public static class FolderType
{
    public const BusinessEntityTypeEnum Id = (BusinessEntityTypeEnum)200;
    public const string Key = "Folder";
}
