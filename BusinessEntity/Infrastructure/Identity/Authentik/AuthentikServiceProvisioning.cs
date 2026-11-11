using System.Security.Cryptography;

namespace BusinessEntity.Infrastructure.Identity.Authentik;

// Service account получает собственный app-password token и никогда не проходит human password reset.
public sealed partial class AuthentikProvisioningAdapter
{
    // Retry подтверждает владение operation marker, затем продолжает выдачу отдельного service credential.
    public async Task<ProvisionedServiceIdentity> CreateServiceAsync(IdentityProviderProfile profile,string login,Guid operationId,CancellationToken ct)
    {
        await ValidateAsync(profile,ct);
        using var found=await SendAsync(profile,HttpMethod.Get,"core/users/?username="+Uri.EscapeDataString(login),null,ct);
        var existing=found!.RootElement.GetProperty("results").EnumerateArray().SingleOrDefault(x=>string.Equals(x.GetProperty("username").GetString(),login,StringComparison.OrdinalIgnoreCase));
        ProvisionedIdentity account;
        if(existing.ValueKind!=System.Text.Json.JsonValueKind.Undefined)
        {
            if(!existing.GetProperty("attributes").TryGetProperty("be_provisioning_operation",out var marker)||marker.GetString()!=operationId.ToString())
                throw new InvalidOperationException("identity.username_conflict: служебный логин занят другой identity.");
            account=Map(profile,existing);
        }
        else
        {
            using var created=await SendAsync(profile,HttpMethod.Post,"core/users/",new {username=login,name=login,type="service_account",is_active=true,path="businessentity",
                attributes=new {be_provisioning_operation=operationId.ToString()}},ct);
            account=Map(profile,created!.RootElement);
        }
        if(account.AccountKind!="Service"||!account.Enabled||account.RequiresPasswordChange)throw new InvalidOperationException("Некорректный целевой service account.");
        var identifier="be-service-"+operationId.ToString("N");
        using var tokens=await SendAsync(profile,HttpMethod.Get,"core/tokens/?identifier="+identifier,null,ct);
        var token=tokens!.RootElement.GetProperty("results").EnumerateArray().SingleOrDefault(x=>x.GetProperty("identifier").GetString()==identifier);
        if(token.ValueKind==System.Text.Json.JsonValueKind.Undefined)
        {
            using var created=await SendAsync(profile,HttpMethod.Post,"core/tokens/",new {identifier,intent="app_password",user=int.Parse(account.AccountId),expiring=false,description="BusinessEntity service provisioning"},ct);
            token=created!.RootElement.Clone();
        }
        if(token.GetProperty("user").GetInt32().ToString()!=account.AccountId||token.GetProperty("intent").GetString()!="app_password")
            throw new InvalidOperationException("Служебный credential не принадлежит операции.");
        if(token.TryGetProperty("expiring",out var expires)&&expires.GetBoolean())
            await PreserveServiceTokenAsync(profile,identifier,account.AccountId,ct);
        using var key=await SendAsync(profile,HttpMethod.Get,"core/tokens/"+identifier+"/view_key/",null,ct);
        return new(account,key!.RootElement.GetProperty("key").GetString()!);
    }
    // Явная повторная выдача меняет только созданный этим планом app-password token, не все ключи аккаунта.
    public async Task<string> RotateServiceCredentialAsync(IdentityProviderProfile profile,string accountId,Guid operationId,CancellationToken ct)
    {
        using var user=await SendAsync(profile,HttpMethod.Get,"core/users/"+accountId+"/",null,ct);
        if(Map(profile,user!.RootElement).AccountKind!="Service"||!user.RootElement.GetProperty("attributes").TryGetProperty("be_provisioning_operation",out var marker)||marker.GetString()!=operationId.ToString())
            throw new InvalidOperationException("Служебный аккаунт не принадлежит плану.");
        var identifier="be-service-"+operationId.ToString("N");
        using var token=await SendAsync(profile,HttpMethod.Get,"core/tokens/"+identifier+"/",null,ct);
        if(token!.RootElement.GetProperty("user").GetInt32().ToString()!=accountId||token.RootElement.GetProperty("intent").GetString()!="app_password")throw new InvalidDataException();
        await PreserveServiceTokenAsync(profile,identifier,accountId,ct);
        var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var rotated=await SendAsync(profile,HttpMethod.Post,"core/tokens/"+identifier+"/set_key/",new {key=password},ct);return password;
    }
    // Authentik defaults в PATCH меняют владельца и intent, если не передать их явно.
    private async Task PreserveServiceTokenAsync(IdentityProviderProfile profile,string identifier,string accountId,CancellationToken ct)
    {
        using var updated=await SendAsync(profile,HttpMethod.Patch,"core/tokens/"+identifier+"/",new{identifier,user=int.Parse(accountId),intent="app_password",expiring=false},ct);
        var token=updated!.RootElement;
        if(token.GetProperty("user").GetInt32().ToString()!=accountId||token.GetProperty("intent").GetString()!="app_password"||token.GetProperty("expiring").GetBoolean())
            throw new InvalidOperationException("Провайдер не подтвердил владельца и параметры служебного credential.");
    }
}
