using System.Security.Claims;
using System.Text.Json;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
using BusinessEntity.Tests.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Реальный SQL подтверждает, что SessionId не является самостоятельным credential и не кеширует права.
public sealed class ApiSessionTests
{
    // Закрытие повторяется своим автором, но больше не допускает выполнение.
    [PostgresFact]
    public async Task CloseIsIdempotentAndPreventsFurtherUse()
    {
        await using var test=await Setup.CreateAsync();var session=await test.Sessions.OpenAsync(new(),default);
        await test.Sessions.CloseAsync(session.Id,default);await test.Sessions.CloseAsync(session.Id,default);
        var error=await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.ValidateAsync(session.Id,null,default));Assert.Equal(401,error.Status);
    }
    // Субъект/client и исходные ограничения проверяются даже при известном ID чужой сессии.
    [PostgresFact]
    public async Task SessionCannotBeUsedByAnotherClientOrWidened()
    {
        await using var test=await Setup.CreateAsync();var session=await test.Sessions.OpenAsync(new(),default);
        await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.OpenAsync(new(["spaces.delete"]),default));
        await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.OpenAsync(new(Spaces:new(true)),default));
        test.Principal.Client="another-client";
        var error=await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.ValidateAsync(session.Id,null,default));Assert.Equal(401,error.Status);
    }
    // Изменение допуска действует немедленно, без ожидания истечения подписанного токена.
    [PostgresFact]
    public async Task GrantRevisionRevokesExistingSession()
    {
        await using var test=await Setup.CreateAsync();var session=await test.Sessions.OpenAsync(new(),default);
        await using(var db=new UserMiniAppDbContext(test.Options))await db.ApiAccessGrants.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revision,2));
        var error=await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.ValidateAsync(session.Id,null,default));Assert.Equal("api.session_grant_revoked",error.Code);
    }
    // Новое выполнение сервиса после restart читает прежний срок, не продлевая сессию.
    [PostgresFact]
    public async Task SessionSurvivesServiceRestartWithoutExtendingExpiry()
    {
        await using var test=await Setup.CreateAsync();var first=await test.Sessions.OpenAsync(new(),default);
        var second=await test.NewSessions().ValidateAsync(first.Id,"user.self.read",default);Assert.Equal(first.ExpiresAt,second.ExpiresAt);
        var expired=DateTime.UtcNow.AddMinutes(-2);
        await using(var db=new UserMiniAppDbContext(test.Options))await db.ApiSessions.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ExpiresAt,expired));
        await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.ValidateAsync(first.Id,null,default));
    }
    // Возврат к прежнему провайдеру не оживляет старую session даже с новым проверенным токеном.
    [PostgresFact]
    public async Task ProviderEpochChangePermanentlyInvalidatesOldSession()
    {
        await using var test=await Setup.CreateAsync();var session=await test.Sessions.OpenAsync(new(),default);
        test.Runtime.Activate(test.Profile.Id,1,2);test.Principal.Epoch=2;
        await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.ValidateAsync(session.Id,null,default));
        test.Runtime.Activate(test.Profile.Id,1,3);test.Principal.Epoch=3;
        await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.ValidateAsync(session.Id,null,default));
    }
    // Валидная локальная identity без отдельного grant не получает API session.
    [PostgresFact]
    public async Task ValidIdentityHasNoImplicitApiGrant()
    {
        await using var test=await Setup.CreateAsync();await using(var db=new UserMiniAppDbContext(test.Options))await db.ApiAccessGrants.ExecuteDeleteAsync();
        var error=await Assert.ThrowsAsync<ApiFault>(()=>test.Sessions.OpenAsync(new(),default));Assert.Equal(403,error.Status);
    }
    // Подготовленная ранее запись проверяет новый revision допуска именно перед writer admission.
    [PostgresFact]
    public async Task FinalCommitAuthorizationRejectsGrantRevokedAfterPreparation()
    {
        await using var test=await Setup.CreateAsync();var session=await test.Sessions.OpenAsync(new(),default);await test.Sessions.ValidateAsync(session.Id,null,default);
        var request=new AuthorizeApiCommitRequest(Guid.NewGuid(),"user.self.read",null,[new("Self")]){Context=test.Scope.Snapshot()};
        var authorizer=new UserApiCommitAuthorizer(test.Sessions,test.Authority,null!);await authorizer.AuthorizeAsync(request,default);
        await using(var db=new UserMiniAppDbContext(test.Options))await db.ApiAccessGrants.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revision,2));
        var error=await Assert.ThrowsAsync<ApiFault>(()=>authorizer.AuthorizeAsync(request,default));Assert.Equal("api.session_grant_revoked",error.Code);
    }
    // Server-derived login admin без локальной роли не даёт GlobalAdmin даже в final admission.
    [PostgresFact]
    public async Task FinalCommitAuthorizationDoesNotGrantAuthorityByLogin()
    {
        await using var test=await Setup.CreateAsync();await using(var db=new UserMiniAppDbContext(test.Options))await db.Users.Where(x=>x.Kind=="Service").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Login,"admin"));
        var session=await test.Sessions.OpenAsync(new(),default);await test.Sessions.ValidateAsync(session.Id,null,default);
        var request=new AuthorizeApiCommitRequest(Guid.NewGuid(),"user.self.read",null,[new("GlobalAdmin")]){Context=test.Scope.Snapshot()};
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new UserApiCommitAuthorizer(test.Sessions,test.Authority,null!).AuthorizeAsync(request,default));
    }
    // Fixture использует отдельную be_test БД и только метаданные заранее проверенного principal.
    private sealed class Setup : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;
        public DbContextOptions<UserMiniAppDbContext> Options {get;}
        public IdentityRuntime Runtime {get;}=new(new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())));
        public IdentityProviderProfile Profile {get;}=new(){Name="Fixture",Issuer="https://issuer.test"};
        public Source Principal {get;}
        private readonly ServiceProvider _services=new ServiceCollection().BuildServiceProvider();
        private readonly BusRequestScope _scope=new();
        private readonly IdentityAdmissionGate _gate=new();
        private readonly IdentityStateStore _store;
        public BusRequestScope Scope=>_scope;
        public UserSecurityAuthority Authority=>new(Options,Principal,Runtime,_scope);
        public UserApiSessions Sessions=>NewSessions();
        // Все instances используют одну служебную БД, а не singleton mutable пользовательский state.
        private Setup(PostgresTestDatabase database,DbContextOptions<UserMiniAppDbContext> options,Guid userId)
        {_database=database;Options=options;Principal=new(userId,Profile.Id);_scope.MarkApiRequest();_store=new(options,new BusRequestClient(new MessageBus(),_scope,_services,[]),_scope,new());}
        // Новый scoped service не удерживает старый DbContext или grant cache.
        public UserApiSessions NewSessions()=>new(new(Options,Principal,Runtime,_scope),_store,Runtime,_scope,_gate,
            new(new ConfigurationBuilder().AddInMemoryCollection().Build()));
        // Тестовые строки имитируют только уже доказанную external identity и явный допуск.
        public static async Task<Setup> CreateAsync()
        {
            var database=await PostgresTestDatabase.CreateAsync();var options=await UserOwnedTransactionTests.InitializeUserAsync(database);
            var id=Guid.NewGuid();var result=new Setup(database,options,id);await result.Runtime.AddAsync(result.Profile);result.Runtime.Activate(result.Profile.Id,1,1);
            await using var db=new UserMiniAppDbContext(options);db.IdentityProfiles.Add(new(){Id=result.Profile.Id,Revision=1,Json="{}"});
            db.Users.Add(new(){Id=id,Login="service-fixture",NormalizedLogin="SERVICE-FIXTURE",Kind="Service",Enabled=true,Payload="{}"});
            db.ExternalIdentities.Add(new(){Id=Guid.NewGuid(),ProviderId=result.Profile.Id,UserId=id,Issuer=result.Profile.Issuer,Subject="verified-subject",Enabled=true});
            db.ApiAccessGrants.Add(new(){Id=Guid.NewGuid(),UserId=id,ClientId="client",Enabled=true,Revision=1,CapabilitiesJson="[\"user.self.read\"]",SpacesJson="{}"});
            await db.SaveChangesAsync();return result;
        }
        // Удаляется исключительно собственная fixture БД.
        public async ValueTask DisposeAsync(){await _services.DisposeAsync();await _database.DisposeAsync();}
    }
    // Источник теста представляет результат аутентификации; подписи проверяются отдельными protocol tests.
    private sealed class Source(Guid userId,Guid providerId):ICurrentPrincipalSource
    {
        public string Client {get;set;}="client";
        public long Epoch {get;set;}=1;
        public Task<ClaimsPrincipal> GetAsync(CancellationToken ct=default)=>Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaimNames.UserId,userId.ToString()),new(IdentityClaimNames.ProviderId,providerId.ToString()),new(IdentityClaimNames.Epoch,Epoch.ToString()),
            new(IdentityClaimNames.Issuer,"https://issuer.test"),new(IdentityClaimNames.Subject,"verified-subject"),new(IdentityClaimNames.ClientId,Client)],"ExternalBearer")));
    }
}
