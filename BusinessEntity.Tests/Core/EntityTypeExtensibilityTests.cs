using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Services;
using BusinessEntity.Core.Types;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using Xunit;

namespace BusinessEntity.Tests.Core;

// Расширение движка требует только декларации и codec от нового владельца.
public sealed class EntityTypeExtensibilityTests
{
    // Тип, которого нет ни в enum, ни в switch движка, создаётся, копируется и читается storage codec.
    [Fact]
    public void UnknownDomainCanRegisterWithoutChangingCore()
    {
        const BusinessEntityTypeEnum id = (BusinessEntityTypeEnum)8001;
        var descriptor = new EntityTypeDescriptor<ExtensionData>(id, "ExtensionMiniApp", "Extension", true, true, "NewExtension",
            (entityId, name, text) => new(new ExtensionData { Id = entityId, Name = name, Value = text ?? string.Empty }));
        var registry = new EntityTypeRegistry([descriptor]); var factory = new BusinessEntityFactory(registry);
        var card = factory.Create(id, "Новый тип");
        var data = (ExtensionData)descriptor.CreateInitial(card.Id, card.Name, "исходный текст").Data!;
        var copy = (ExtensionData)descriptor.Clone(data); data.Value = "новая правка";
        var codec = new EntityDataStorageCodec(new EntityDataStorageConverterFactory([new ExtensionConverter()]));
        Assert.Equal("Extension", codec.GetStorageKind(id));
        Assert.True(card.IsPublic);
        Assert.Equal("исходный текст", codec.DeserializePayloadBody<ExtensionData>(id, codec.SerializePayload(copy)).Value);
        Assert.Equal("ExtensionMiniApp", new BusinessEntity.Core.Saving.EntitySaveBatchFactory(registry).Create(id).MiniAppId);
    }
    // Отсутствующий описатель не заменяется базовой пустой моделью.
    [Fact]
    public void MissingDescriptorFailsExplicitly() => Assert.Throws<NotSupportedException>(() => new EntityTypeRegistry([]).Get((BusinessEntityTypeEnum)8002));
    // Модель тестового владельца находится вне Core приложения.
    public sealed class ExtensionData : BusinessEntityData
    {
        public override BusinessEntityTypeEnum EntityType { get; set; } = (BusinessEntityTypeEnum)8001;
        public string Value { get; set; } = string.Empty;
    }
    // Собственный codec нового типа не добавляет ветвление в DataProvider или Core.
    private sealed class ExtensionConverter : EntityDataStorageConverterBase<ExtensionData>
    {
        public override BusinessEntityTypeEnum SupportedType => (BusinessEntityTypeEnum)8001;
        public override string StorageKind => "Extension";
        // Сериализует поле своего владельца.
        public override string SerializePayload(ExtensionData data) => SerializeBody(data.Value);
        // Восстанавливает поле своего владельца.
        public override ExtensionData DeserializePayload(string json) => new() { Value = DeserializeBody<string>(json) };
    }
}
