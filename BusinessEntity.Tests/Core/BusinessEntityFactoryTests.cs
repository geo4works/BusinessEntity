using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.Core.Services;
using Xunit;

namespace BusinessEntity.Tests.Core;

// Проверяет общие гарантии создания сущности и связанного payload.
public sealed class BusinessEntityFactoryTests
{
    private readonly BusinessEntityFactory _factory = CreateFactory();

    // Подключает реальные описатели владельцев без запуска их runtime.
    private static BusinessEntityFactory CreateFactory()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        BusinessEntity.MiniApps.DocumentsMiniApp.Registration.DocumentsMiniAppRegistration.AddDocumentsMiniApp(services);
        BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration.RichTextDocumentMiniAppRegistration.AddRichTextDocumentMiniApp(services);
        BusinessEntity.MiniApps.SpacesMiniApp.Registration.SpacesMiniAppRegistration.AddSpacesMiniApp(services);
        BusinessEntity.MiniApps.TreeMiniApp.Registration.TreeMiniAppRegistration.AddTreeMiniApp(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        return new BusinessEntityFactory(new BusinessEntity.Core.Types.EntityTypeRegistry(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<BusinessEntity.Core.Types.IEntityTypeDescriptor>(provider)));
    }

    // Новая карточка получает собственный идентификатор и время UTC.
    [Fact]
    public void CreatesIndependentEntityIdentities()
    {
        var before = DateTime.UtcNow;
        var first = _factory.Create(global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, "Папка");
        var second = _factory.Create(global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, "Папка");

        Assert.NotEqual(Guid.Empty, first.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("Папка", first.Name);
        Assert.Equal(global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, first.EntityType);
        Assert.InRange(first.CreatedDate, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, first.CreatedDate.Kind);
    }

    // Присоединение сохраняет содержимое и связывает идентичность с карточкой.
    [Fact]
    public void AttachesPayloadWithoutLosingItsContent()
    {
        var payload = new Document { Name = "Документ", Text = "Содержимое" };

        var entity = _factory.Create(global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, payload);

        Assert.Same(payload, entity.Data);
        Assert.Equal(entity.Id, payload.Id);
        Assert.Equal(entity.Name, payload.Name);
        Assert.Equal(entity.LastModifiedDate, payload.LastModifiedDate);
        Assert.Equal("Содержимое", payload.Text);
    }

    // Контентная карточка получает установленный движком признак общей видимости.
    [Theory]
    [InlineData(global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, true)]
    [InlineData(global::BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichTextDocumentType.Id, true)]
    [InlineData(global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, false)]
    [InlineData(global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, false)]
    public void AppliesInitialContentVisibility(BusinessEntityTypeEnum type, bool expected)
    {
        Assert.Equal(expected, _factory.Create(type, "Объект").IsPublic);
    }

    // Создание по runtime-типу возвращает typed-сущность с готовым payload.
    [Fact]
    public void CreatesTypedEntityFromRuntimePayloadType()
    {
        var entity = _factory.CreateWithData(typeof(Document), "Статья");

        var typed = Assert.IsType<BusinessEntity<Document>>(entity);
        Assert.Equal("Статья", typed.Data.Name);
        Assert.Equal(typed.Id, typed.Data.Id);
    }

    // Некорректный runtime-тип не превращается в пустой payload.
    [Fact]
    public void RejectsTypeOutsidePayloadContract()
    {
        Assert.Throws<ArgumentException>(() => _factory.CreateWithData(typeof(string)));
    }

    // Отсутствующий payload отклоняется до создания карточки.
    [Fact]
    public void RejectsMissingPayload()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _factory.Create<Document>(global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, (Document)null!));
    }
}
