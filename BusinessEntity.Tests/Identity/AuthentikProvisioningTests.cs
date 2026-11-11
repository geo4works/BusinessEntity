using System.Net;
using System.Text;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.Infrastructure.Identity.Authentik;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Адаптер не присваивает существующую внешнюю учётку и не скрывает отказ management API.
public sealed class AuthentikProvisioningTests
{
    // Совпадение имени без своего provisioning marker всегда является конфликтом.
    [Fact]
    public async Task ExistingUnrelatedLoginCannotBeClaimed()
    {
        var factory = new Factory(HttpStatusCode.OK);
        var adapter = new AuthentikProvisioningAdapter(new Secrets(),factory);
        var profile = new IdentityProviderProfile { Issuer="https://idp.test",ClientId="client",ManagementEndpoint="https://idp.test/api/v3/",
            ManagementSecretRef="management",SubjectMode="user_uuid",PasswordResetFlowVerified=true };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.CreateAsync(profile,"alice","aA1!aVeryLongTemporaryPassword",Guid.NewGuid(),default));
        Assert.Contains("identity.username_conflict",error.Message); Assert.Equal(0,factory.Mutations);
    }
    // Тело ошибки провайдера, даже содержащее секрет, не возвращается пользователю.
    [Fact]
    public async Task ProviderFailureDoesNotExposeBody()
    {
        var adapter = new AuthentikProvisioningAdapter(new Secrets(),new Factory(HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.FindAsync(new IdentityProviderProfile
            { Issuer="https://idp.test",ManagementEndpoint="https://idp.test/api/v3/",ManagementSecretRef="management" },"alice",default));
        Assert.DoesNotContain("DO_NOT_EXPOSE",error.Message); Assert.Contains("401",error.Message);
    }
    // Значения разрешённых secret references остаются внутри адаптера.
    private sealed class Secrets : IIdentitySecretResolver { public string Resolve(string reference) => "test-only-management-token"; }
    // Общий handler позволяет видеть попытки внешних изменений, не обращаясь к рабочему IdP.
    private sealed class Factory(HttpStatusCode status) : HttpMessageHandler, IHttpClientFactory
    {
        public int Mutations { get; private set; }
        public HttpClient CreateClient(string name) => new(this,false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            if(request.Method!=HttpMethod.Get) Mutations++;
            var body=request.RequestUri!.AbsolutePath.Contains("providers")
                ? "{\"results\":[{\"client_id\":\"client\",\"sub_mode\":\"user_uuid\"}]}"
                : "{\"results\":[{\"pk\":1,\"uuid\":\"b6fb3012-4bcc-4a7d-a3e0-4039712149cc\",\"username\":\"alice\",\"name\":\"Alice\",\"is_active\":true,\"attributes\":{}}]}";
            if(status!=HttpStatusCode.OK) body="DO_NOT_EXPOSE";
            return Task.FromResult(new HttpResponseMessage(status) { Content=new StringContent(body,Encoding.UTF8,"application/json") });
        }
    }
}
