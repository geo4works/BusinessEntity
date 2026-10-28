using BusinessEntity.Core.Classes;
namespace BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
// Владелец объявляет неизменяемый numeric ID и ключ собственного типа.
public static class MediaVideoType
{
    public const BusinessEntityTypeEnum Id = (BusinessEntityTypeEnum)500;
    public const string Key = "MediaVideo";
}
