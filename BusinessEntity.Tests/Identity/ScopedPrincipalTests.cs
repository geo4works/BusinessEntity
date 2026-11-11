using System.Security.Claims;
using BusinessEntity.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BusinessEntity.Tests.Identity;

public sealed class ScopedPrincipalTests
{
    [Fact]
    public async Task ApiPrincipalRemainsBoundToRequestWhenConcurrentBusCallbackHasNoAmbientHttp()
    {
        var accessor=new HttpContextAccessor();var request=new DefaultHttpContext();request.Request.Path="/api/v1/jobs/fixture";request.User=new(new ClaimsIdentity([new Claim(ClaimTypes.Name,"request-user")],"Bearer"));accessor.HttpContext=request;var source=new CurrentPrincipalSource(accessor);accessor.HttpContext=null;Assert.Same(request.User,await source.GetAsync());
    }
    [Fact]
    public async Task BackgroundPrincipalIsExplicitAndCannotBeInstalledIntoHttpScope()
    {
        var accessor=new HttpContextAccessor();var source=new CurrentPrincipalSource(accessor);var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"job-user")],"Bearer"));source.Install(principal);Assert.Same(principal,await source.GetAsync());accessor.HttpContext=new DefaultHttpContext();Assert.Throws<InvalidOperationException>(()=>new CurrentPrincipalSource(accessor).Install(principal));accessor.HttpContext=null;
    }
}
