using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using BusinessEntity.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Тестовый HTTP issuer реально публикует discovery/JWKS; production JwtBearer проверяет подписанные токены.
public sealed class ApiBearerProtocolTests
{
    // Неверные claims, подпись, срок и UI ID token отсекаются до вызова User-владельца.
    [Theory]
    [InlineData("valid",true)] [InlineData("issuer",false)] [InlineData("audience",false)]
    [InlineData("signature",false)] [InlineData("expired",false)] [InlineData("scope",false)]
    [InlineData("client",false)] [InlineData("missing-client",false)] [InlineData("id-token",false)]
    [InlineData("unsigned",false)] [InlineData("symmetric",false)]
    public async Task SignedAccessTokenMustMeetTheExactApiProfile(string variant,bool valid)
    {
        using var rsa=RSA.Create(2048);using var other=RSA.Create(2048);var key=new RsaSecurityKey(rsa){KeyId="api-fixture"};
        await using var provider=NewHost();string issuer=string.Empty;
        provider.MapGet("/.well-known/openid-configuration",()=>Results.Json(new{issuer,jwks_uri=issuer+"/keys",authorization_endpoint=issuer+"/authorize",token_endpoint=issuer+"/token"}));
        provider.MapGet("/keys",()=>{var p=rsa.ExportParameters(false);return Results.Json(new{keys=new[]{new{kty="RSA",use="sig",kid=key.KeyId,alg="RS256",n=Base64UrlEncoder.Encode(p.Modulus!),e=Base64UrlEncoder.Encode(p.Exponent!)}}});});
        await provider.StartAsync();issuer=Address(provider);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        var keys=Path.Combine(Path.GetTempPath(),"be-api-protocol-tests",Guid.NewGuid().ToString("N"));builder.Configuration["Identity:KeyDirectory"]=keys;
        builder.Services.AddPluggableIdentity(builder.Configuration);builder.Services.AddApiBearer();var owner=new Owner();builder.Services.AddSingleton<IApiIdentityHandler>(owner);
        await using var app=builder.Build();app.UseAuthentication();app.UseAuthorization();
        var profile=new IdentityProviderProfile{Name="Generic API issuer",Issuer=issuer+"/ui",MetadataAddress=issuer+"/.well-known/openid-configuration",ClientId="ui-client",ClientSecretRef="fixture",
            ApiTokenProfiles=[new(issuer,issuer+"/.well-known/openid-configuration","api-resource",["integration"],ClientClaim:"azp",RequiredScope:"be.api")]};
        var runtime=app.Services.GetRequiredService<IdentityRuntime>();await runtime.AddAsync(profile);runtime.Activate(profile.Id,1,1);
        app.MapGet("/api-proof",()=>Results.Ok()).RequireAuthorization("ExternalApi");await app.StartAsync();
        try
        {
            var claims=new Dictionary<string,object>{{"sub","external-subject"},{"azp",variant=="client"?"other":"integration"},{"scope",variant=="scope"?"openid":"be.api"}};
            if(variant=="missing-client")claims.Remove("azp");if(variant=="id-token")claims["scope"]="openid";
            var now=DateTime.UtcNow;var descriptor=new SecurityTokenDescriptor{Issuer=variant=="issuer"?issuer+"/other":issuer,Audience=variant=="audience"?"wrong":variant=="id-token"?"ui-client":"api-resource",IssuedAt=now.AddMinutes(-5),NotBefore=now.AddMinutes(-5),Expires=variant=="expired"?now.AddMinutes(-2):now.AddMinutes(5),Claims=claims,
                SigningCredentials=variant=="unsigned"?null:variant=="symmetric"?new(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)),SecurityAlgorithms.HmacSha256):new(variant=="signature"?new RsaSecurityKey(other){KeyId=key.KeyId}:key,SecurityAlgorithms.RsaSha256)};
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false});http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new JsonWebTokenHandler().CreateToken(descriptor));
            using var response=await http.GetAsync(Address(app)+"/api-proof");Assert.Equal(valid?200:401,(int)response.StatusCode);Assert.Equal(valid?1:0,owner.Completed);Assert.Null(response.Headers.Location);
        }
        finally{await app.StopAsync();await provider.StopAsync();if(Directory.Exists(keys))Directory.Delete(keys,true);}
    }
    // Хост не читает рабочую конфигурацию/секреты приложения.
    private static WebApplication NewHost(){var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});b.Configuration.Sources.Clear();b.Configuration.AddInMemoryCollection();b.Logging.ClearProviders();b.WebHost.UseUrls("http://127.0.0.1:0");return b.Build();}
    // Ephemeral порт разрешён операционной системой.
    private static string Address(WebApplication app)=>app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
    // Этот владелец существует только в тестах; проверка токена остаётся настоящей.
    private sealed class Owner:IApiIdentityHandler
    {
        public int Completed{get;private set;}
        public Task<ClaimsPrincipal> CompleteAsync(VerifiedAccessIdentity identity,CancellationToken ct){Assert.Equal("external-subject",identity.Subject);Assert.Equal("integration",identity.ClientId);Completed++;return Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"fixture")],"Fixture")));}
    }
}
