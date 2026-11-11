using System.Net;
using System.Text;
using System.Text.Json;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.Infrastructure.Identity.Authentik;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Реальные defaults TokenSerializer не должны менять владельца при выдаче и PATCH.
public sealed class AuthentikServiceProvisioningTests
{
    private static readonly Guid Operation=Guid.Parse("4e95b80f-0138-4a26-9d5c-0d014e144979");
    private static IdentityProviderProfile Profile()=>new(){Issuer="https://idp.test",ClientId="client",ManagementEndpoint="https://idp.test/api/v3/",ManagementSecretRef="management",SubjectMode="user_uuid",PasswordResetFlowVerified=true};
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceCredentialCreateAndRetryKeepExplicitOwnerIntentAndLifetime(bool existing)
    {
        var factory=new Factory(existing);var adapter=new AuthentikProvisioningAdapter(new Secrets(),factory);
        var result=await adapter.CreateServiceAsync(Profile(),"service",Operation,default);
        Assert.Equal("7",result.Account.AccountId);Assert.Equal("test-only-app-password",result.Credential);
        Assert.False(factory.TokenExpiring);Assert.Equal(7,factory.TokenUser);Assert.Equal("app_password",factory.TokenIntent);
        Assert.Equal(existing?1:0,factory.Patches);Assert.Equal(existing?0:1,factory.Creates);
    }
    [Fact]
    public async Task ExplicitRotationPreservesServiceOwnerAndDoesNotExpireCredential()
    {
        var factory=new Factory(true);var adapter=new AuthentikProvisioningAdapter(new Secrets(),factory);
        var result=await adapter.RotateServiceCredentialAsync(Profile(),"7",Operation,default);
        Assert.Equal(64,result.Length);Assert.Equal(result,factory.RotatedKey);Assert.Equal(1,factory.Patches);
        Assert.Equal(7,factory.TokenUser);Assert.Equal("app_password",factory.TokenIntent);Assert.False(factory.TokenExpiring);
    }
    [Fact]
    public async Task ProviderThatCreatesTokenForManagementUserCannotReturnItsSecretAsServiceCredential()
    {
        var factory=new Factory(false){ForceManagementOwner=true};var adapter=new AuthentikProvisioningAdapter(new Secrets(),factory);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>adapter.CreateServiceAsync(Profile(),"service",Operation,default));
        Assert.Equal(0,factory.KeyReads);Assert.Equal(0,factory.Patches);
    }
    private sealed class Secrets:IIdentitySecretResolver{public string Resolve(string reference)=>"test-only-management-token";}
    private sealed class Factory(bool existing):HttpMessageHandler,IHttpClientFactory
    {
        public bool ForceManagementOwner;public int Creates,Patches,KeyReads;public int TokenUser=7;public string TokenIntent="app_password",RotatedKey="";public bool TokenExpiring=true;
        public HttpClient CreateClient(string name)=>new(this,false);
        private object Token()=>new{identifier="be-service-"+Operation.ToString("N"),user=TokenUser,intent=TokenIntent,expiring=TokenExpiring};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;object body;
            var user=new{pk=7,uuid="eb8c3d2a-6ab9-47b6-a698-9d50f4b5e2df",username="service",name="Service",type="service_account",is_active=true,attributes=new{be_provisioning_operation=Operation.ToString()}};
            if(path.Contains("providers"))body=new{results=new[]{new{client_id="client",sub_mode="user_uuid"}}};
            else if(path.Contains("users"))body=path.EndsWith("/7/")?user:new{results=new[]{user}};
            else if(request.Method==HttpMethod.Post&&path.EndsWith("/tokens/"))
            {
                Creates++;using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var root=json.RootElement;
                TokenUser=ForceManagementOwner?4:root.GetProperty("user").GetInt32();TokenIntent=root.GetProperty("intent").GetString()!;TokenExpiring=root.GetProperty("expiring").GetBoolean();body=Token();
            }
            else if(request.Method==HttpMethod.Patch)
            {
                Patches++;using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var root=json.RootElement;
                // Поведение Authentik: отсутствующие user/intent в PATCH получают defaults request.user/api.
                TokenUser=root.TryGetProperty("user",out var owner)?owner.GetInt32():4;TokenIntent=root.TryGetProperty("intent",out var intent)?intent.GetString()!:"api";TokenExpiring=root.GetProperty("expiring").GetBoolean();body=Token();
            }
            else if(path.EndsWith("/view_key/")){KeyReads++;body=new{key="test-only-app-password"};}
            else if(path.EndsWith("/set_key/")){using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));RotatedKey=json.RootElement.GetProperty("key").GetString()!;return new(HttpStatusCode.NoContent);}
            else if(request.RequestUri.Query.Length>0)body=new{results=existing?new[]{Token()}:Array.Empty<object>()};
            else body=Token();
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
        }
    }
}
