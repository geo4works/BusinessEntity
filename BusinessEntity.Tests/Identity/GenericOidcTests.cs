using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BusinessEntity.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
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

// Настоящие HTTP discovery/JWKS/code+PKCE и подписанный ID token не требуют Authentik DTO.
public sealed class GenericOidcTests
{
    // Владелец получает identity только после полной проверки стандартным OIDC handler.
    [Theory]
    [InlineData("valid", true)]
    [InlineData("audience", false)]
    [InlineData("issuer", false)]
    [InlineData("signature", false)]
    [InlineData("expired", false)]
    [InlineData("nonce", false)]
    public async Task GenericProviderUsesRealCodeFlowAndRejectsInvalidTokens(string variant, bool expected)
    {
        using var rsa = RSA.Create(2048); using var other = RSA.Create(2048);
        var signing = new RsaSecurityKey(rsa) { KeyId = "fixture-key" };
        var codes = new ConcurrentDictionary<string, (string Nonce, string Challenge)>();
        var keys = Path.Combine(Path.GetTempPath(), "be-oidc-tests", Guid.NewGuid().ToString("N"));
        await using var provider = NewHost();
        string issuer = string.Empty; string allowedCallback = string.Empty; var verifiedPkce = false;
        provider.MapGet("/.well-known/openid-configuration", () => Results.Json(new {
            issuer, authorization_endpoint = issuer + "/authorize", token_endpoint = issuer + "/token", jwks_uri = issuer + "/keys",
            response_types_supported = new[] { "code" }, subject_types_supported = new[] { "public" }, id_token_signing_alg_values_supported = new[] { "RS256" } }));
        provider.MapGet("/keys", () => { var p = rsa.ExportParameters(false); return Results.Json(new { keys = new[] { new {
            kty = "RSA", use = "sig", kid = signing.KeyId, alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus!), e = Base64UrlEncoder.Encode(p.Exponent!) } } }); });
        provider.MapGet("/authorize", (HttpContext context) => {
            var q = context.Request.Query;
            if(q["client_id"]!="generic-client" || q["response_type"]!="code" || q["redirect_uri"]!=allowedCallback || q["code_challenge_method"]!="S256")return Results.BadRequest();
            var code = Guid.NewGuid().ToString("N"); codes[code] = (q["nonce"].ToString(), q["code_challenge"].ToString());
            return Results.Redirect(allowedCallback + "?code=" + code + "&state=" + Uri.EscapeDataString(q["state"].ToString()));
        });
        provider.MapPost("/token", async (HttpContext context) => {
            var form = await context.Request.ReadFormAsync();
            if(form["client_id"]!="generic-client" || form["client_secret"]!="fixture-secret" || form["redirect_uri"]!=allowedCallback
                ||!codes.TryRemove(form["code"].ToString(),out var grant))return Results.BadRequest();
            verifiedPkce=Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())))==grant.Challenge;
            if(!verifiedPkce)return Results.BadRequest();
            var now=DateTime.UtcNow;
            var descriptor=new SecurityTokenDescriptor { Issuer=variant=="issuer"?"https://wrong.test":issuer, Audience=variant=="audience"?"other-client":"generic-client",
                IssuedAt=now.AddMinutes(-5),NotBefore=now.AddMinutes(-5),Expires=variant=="expired"?now.AddMinutes(-2):now.AddMinutes(5),
                Claims=new Dictionary<string,object>{{"sub","generic-subject"},{"preferred_username","reader"},{"nonce",variant=="nonce"?"wrong":grant.Nonce}},
                SigningCredentials=new(variant=="signature"?new RsaSecurityKey(other){KeyId=signing.KeyId}:signing,SecurityAlgorithms.RsaSha256) };
            return Results.Json(new { access_token="unused-fixture-access",token_type="Bearer",expires_in=300,id_token=new JsonWebTokenHandler().CreateToken(descriptor) });
        });
        await provider.StartAsync(); issuer = Address(provider);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();
        builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Identity:KeyDirectory",keys},{"Identity:Secrets:client","fixture-secret"}});
        builder.Services.AddPluggableIdentity(builder.Configuration);
        var owner=new Owner(); builder.Services.AddSingleton<IIdentitySignInHandler>(owner); builder.Services.AddSingleton<IIdentitySessionValidator>(owner);
        await using var application=builder.Build(); application.UseAuthentication();
        var profile=new IdentityProviderProfile { Name="Generic OIDC",Issuer=issuer,MetadataAddress=issuer+"/.well-known/openid-configuration",ClientId="generic-client",ClientSecretRef="client" };
        var runtime=application.Services.GetRequiredService<IdentityRuntime>();await runtime.AddAsync(profile);runtime.Activate(profile.Id,1,1);
        application.MapGet("/login",async context=>{var state=new AuthenticationProperties { RedirectUri="/complete" };state.Items["be.epoch"]="1";await context.ChallengeAsync(profile.Scheme,state);});
        application.MapGet("/complete",(HttpContext context)=>context.User.Identity?.IsAuthenticated==true?Results.Ok():Results.Unauthorized());
        await application.StartAsync(); allowedCallback=Address(application)+profile.CallbackPath;
        try
        {
            await application.Services.GetRequiredService<IdentityProfileValidation>().ValidateAsync(profile,default);
            using var http=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false,UseCookies=true });
            using var challenge=await http.GetAsync(Address(application)+"/login"); Assert.Equal(302,(int)challenge.StatusCode);
            using var authorize=await http.GetAsync(challenge.Headers.Location); Assert.Equal(302,(int)authorize.StatusCode);
            using var callback=await http.GetAsync(authorize.Headers.Location);Assert.Equal(302,(int)callback.StatusCode);
            Assert.True(verifiedPkce);
            Assert.Equal(expected?"/complete":"/auth/error",callback.Headers.Location!.IsAbsoluteUri?callback.Headers.Location.AbsolutePath:callback.Headers.Location.OriginalString.Split('?')[0]);
            Assert.Equal(expected?1:0,owner.Completed);
            if(expected){using var completed=await http.GetAsync(Address(application)+"/complete");Assert.Equal(200,(int)completed.StatusCode);}
        }
        finally { await application.StopAsync();await provider.StopAsync();if(Directory.Exists(keys))Directory.Delete(keys,true); }
    }
    // Отдельный ephemeral host не использует application connection strings, Docker или рабочие credentials.
    private static WebApplication NewHost()
    {var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Development" });builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");return builder.Build();}
    // Считывает назначенный Kestrel порт после StartAsync.
    private static string Address(WebApplication host)=>host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
    // Тестовый владелец доказывает границу протокола; не подменяет реальную проверку подписанного токена.
    private sealed class Owner : IIdentitySignInHandler,IIdentitySessionValidator
    {
        public int Completed {get;private set;}
        public Task<ClaimsPrincipal> CompleteAsync(VerifiedExternalIdentity identity,long epoch,Guid? plan,CancellationToken ct)
        {Assert.Equal("generic-subject",identity.Subject);Assert.Equal("reader",identity.Login);Completed++;return Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"reader")],"Cookies")));}
        public Task<bool> ValidateAsync(ClaimsPrincipal principal,CancellationToken ct)=>Task.FromResult(principal.Identity?.IsAuthenticated==true);
    }
}
