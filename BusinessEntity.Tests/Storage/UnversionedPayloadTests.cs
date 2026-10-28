using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Entity = BusinessEntity.Core.Classes.BusinessEntity;

namespace BusinessEntity.Tests.Storage;

// Тип без истории сравнивает всё тело payload, а не только имя карточки.
public sealed class UnversionedPayloadTests
{
    // Смена настроек сохраняется в единственной строке v1 и доступна после обновления кэша.
    [PostgresFact]
    public async Task SystemSettingsChangePersistsWithoutNewVersion()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor();
        var entity = new Entity { Id = Guid.NewGuid(), Name = "SysParameters", EntityType = SysParametersTpType.Id };
        await fixture.Writer.SaveAsync(new(Guid.NewGuid(), "SysParametersMiniApp", null)
            { Entities = [entity], Payloads = [new(entity.Id, 0, new SysParameters { Name = entity.Name, CompanyName = "before" })] }, actor);
        var data = ((SysParameters)(await fixture.Reader.GetPayloadAsync(entity.Id, null))!)!; data.CompanyName = "after"; data.RichTextChunkCharLimit = 14000;
        var result = await fixture.Writer.SaveAsync(new(Guid.NewGuid(), "SysParametersMiniApp", Guid.NewGuid()) { Payloads = [new(entity.Id, 1, data)] }, actor);
        Assert.Equal(1, result.Versions.Single().Version); Assert.False(result.Versions.Single().CreatedVersion);
        var stored = ((SysParameters)(await fixture.Reader.GetPayloadAsync(entity.Id, null))!)!;
        Assert.Equal("after", stored.CompanyName); Assert.Equal(14000, stored.RichTextChunkCharLimit);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options); Assert.Equal(1, await db.BusinessEntityDataItems.CountAsync(x => x.BusinessEntityId == entity.Id));
    }
    // Комментарий видео меняется без переименования; неизменяемый оригинал остаётся тем же.
    [PostgresFact]
    public async Task MediaCommentOnlyChangePersists()
    {
        await using var fixture = await PostgresTestDatabase.CreateAsync(); var actor = Actor();
        var entity = new Entity { Id = Guid.NewGuid(), Name = "video", EntityType = MediaVideoType.Id };
        var initial = new MediaVideo { Name = entity.Name, DisplayName = entity.Name, FileName = "video.mp4", ContentType = "video/mp4", OriginalSizeBytes = 4, UploadedDate = DateTime.UtcNow };
        await fixture.Writer.SaveAsync(new(Guid.NewGuid(), "MediaServerMiniApp", null)
        { Entities = [entity], Payloads = [new(entity.Id, 0, initial)], Files = [new(entity.Id, "video", "original", "video.mp4", "video/mp4", [1, 2, 3, 4])] }, actor);
        var data = ((MediaVideo)(await fixture.Reader.GetPayloadAsync(entity.Id, null))!)!; data.Comment = "only comment changed";
        var result = await fixture.Writer.SaveAsync(new(Guid.NewGuid(), "MediaServerMiniApp", Guid.NewGuid()) { Payloads = [new(entity.Id, 1, data)] }, actor);
        Assert.False(result.Versions.Single().CreatedVersion); Assert.Equal(1, result.Versions.Single().Version);
        Assert.Equal("only comment changed", ((MediaVideo)(await fixture.Reader.GetPayloadAsync(entity.Id, null))!)!.Comment);
        await using var db = new KmsBusinessEntityDbContext(fixture.Options); Assert.Equal(1, await db.BusinessEntityDataItems.CountAsync(x => x.BusinessEntityId == entity.Id));
    }
    // Системная identity теста не обращается к внешнему Authentik.
    private static BusRequestContext Actor() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), IsSystem: true);
}
