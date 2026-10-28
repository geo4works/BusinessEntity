using BusinessEntity.Core.Classes;
namespace BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
// Владелец объявляет неизменяемый numeric ID и ключ собственного типа.
public static class SpaceType
{
    public const BusinessEntityTypeEnum Id = (BusinessEntityTypeEnum)100;
    public const string Key = "Space";
}
