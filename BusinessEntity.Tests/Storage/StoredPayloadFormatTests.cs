using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BusinessEntity.Tests.Storage;

// Повреждённые и старые структуры payload не подменяются пустыми данными.
public sealed class StoredPayloadFormatTests
{
    // Все обязательные поля канонического тела должны присутствовать и иметь допустимые значения.
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"text\":null,\"tag\":\"\"}")]
    [InlineData("{\"tag\":\"\"}")]
    [InlineData("{\"text\":\"text\",\"tag\":\"\",\"publishedVersion\":1}")]
    public void RejectsMalformedAndOldDocumentBodies(string body)
    {
        using var services = CreateServices();
        var codec = services.GetRequiredService<EntityDataStorageCodec>();
        var stored = DataPayloadEnvelopeSerializer.CreateEnvelopeJson("Document", body);
        Assert.Throws<System.Text.Json.JsonException>(() => codec.DeserializeEnvelope<IBusinessEntityData>(stored));
    }

    // Пустой текст — действительное содержимое, в отличие от отсутствующего поля или null.
    [Fact]
    public void AcceptsExplicitEmptyDocumentContent()
    {
        using var services = CreateServices();
        var stored = DataPayloadEnvelopeSerializer.CreateEnvelopeJson("Document", "{\"text\":\"\",\"tag\":\"\"}");
        var data = services.GetRequiredService<EntityDataStorageCodec>().DeserializeEnvelope<Document>(stored);
        Assert.Equal(string.Empty, data.Text);
    }

    // Неизвестный kind не выбирает похожий конвертер.
    [Fact]
    public void RejectsUnknownKind()
    {
        using var services = CreateServices();
        var stored = DataPayloadEnvelopeSerializer.CreateEnvelopeJson("Unknown", "{}");
        Assert.Throws<NotSupportedException>(() => services.GetRequiredService<EntityDataStorageCodec>()
            .DeserializeEnvelope<IBusinessEntityData>(stored));
    }

    // Некорректные настройки не сохраняются, чтобы последующее чтение не исправляло их молча.
    [Fact]
    public void RejectsInvalidSettingsBeforeStorage()
    {
        using var services = CreateServices();
        Assert.Throws<InvalidDataException>(() => services.GetRequiredService<EntityDataStorageCodec>()
            .SerializePayload(new SysParameters { RichTextChunkCharLimit = -1 }));
    }

    // Разрешает только собственную фабрику конвертеров; БД этому тесту не нужна.
    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection(); services.AddDataProviderMiniApp();
        BusinessEntity.MiniApps.DocumentsMiniApp.Registration.DocumentsMiniAppRegistration.AddDocumentsMiniApp(services);
        BusinessEntity.MiniApps.SysParametersMiniApp.Registration.SysParametersMiniAppRegistration.AddSysParametersMiniApp(services); return services.BuildServiceProvider();
    }
}
