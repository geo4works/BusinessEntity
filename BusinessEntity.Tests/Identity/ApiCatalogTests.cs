using BusinessEntity.Infrastructure.Api;
using Microsoft.AspNetCore.Routing;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Ошибки деклараций обнаруживаются до публикации HTTP endpoints.
public sealed class ApiCatalogTests
{
    // Тестовые декларации/маршруты не регистрируются в production container.
    [Theory]
    [InlineData("duplicate-capability")] [InlineData("route-collision")] [InlineData("schema-collision")]
    [InlineData("foreign-owner")] [InlineData("missing-binding")] [InlineData("extra-binding")]
    [InlineData("reserved-route")]
    public async Task InvalidCatalogCannotStart(string variant)
    {
        var bus=new MessageBus();var first=Definition("one","/one/{id:guid}");var second=Definition("two","/two/{id:guid}");
        if(variant=="duplicate-capability")second=second with {Id=first.Id};
        if(variant=="route-collision")second=second with {Route="/one/{another:guid}"};
        if(variant=="schema-collision")second=second with {InputSchema=first.InputSchema};
        if(variant=="foreign-owner")second=second with {OwnerMiniAppId="ForeignOwner"};
        if(variant=="reserved-route")second=second with {Route="/operations/{id}"};
        using var owner=new ApiToolsetHost(bus,"FixtureOwner",[first,second]);
        IApiRouteBinding[] bindings=variant=="missing-binding"?[new Binding(first)]:variant=="extra-binding"?[new Binding(first),new Binding(second),new Binding(Definition("extra","/extra"))]:[new Binding(first),new Binding(second)];
        var catalog=new ApiCapabilityCatalog(bus,[owner],bindings);await Assert.ThrowsAsync<InvalidOperationException>(()=>catalog.InitializeAsync(default));
    }
    // Успешный снимок нельзя менять cast к List/array через публичный getter.
    [Fact]
    public async Task ValidCatalogPublishesAnImmutableSnapshot()
    {var bus=new MessageBus();var definition=Definition("one","/fixture");using var owner=new ApiToolsetHost(bus,"FixtureOwner",[definition]);var catalog=new ApiCapabilityCatalog(bus,[owner],[new Binding(definition)]);await catalog.InitializeAsync(default);Assert.Single(catalog.Definitions);Assert.Throws<NotSupportedException>(()=>((IList<ApiCapability>)catalog.Definitions)[0]=definition with{Id="changed"});}
    // Только metadata contract, без настоящих HTTP endpoint в рабочем приложении.
    private static ApiCapability Definition(string id,string route)=>new("fixture",1,id,1,"FixtureOwner","Fixture","GET",route,id+".in.v1",id+".out.v1");
    // Runtime mapping не запускается в проверке discovery.
    private sealed record Binding(ApiCapability Definition):IApiRouteBinding{public void Map(IEndpointRouteBuilder endpoints)=>throw new NotSupportedException();}
}
