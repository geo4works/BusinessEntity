using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using BusinessEntity.Core.Classes;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;

// Текущий JSON-контракт графа требует даже nullable metadata: отсутствие отличается от явного null.
public static class GraphArchiveJson
{
    // Создаёт строгие настройки текущего JSON-контракта без восстановления отсутствующих полей.
    public static JsonSerializerOptions CreateOptions(bool indented = false)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(Core.Classes.BusinessEntity) || info.Type == typeof(BusinessEntityRelation))
                foreach (var property in info.Properties) property.IsRequired = true;
        });
        return new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            TypeInfoResolver = resolver
        };
    }
}
