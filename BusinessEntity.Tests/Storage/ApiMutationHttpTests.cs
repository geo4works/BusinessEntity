using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Xunit;

namespace BusinessEntity.Tests.Storage;

// Тестовый владелец/HTTP host существуют только в тестах; canonical writer и PostgreSQL настоящие.
public sealed class ApiMutationHttpTests
{
    // HTTP -> typed binding -> owner bus -> DataProvider bus -> SQL: retry/restart/concurrency без дублей.
    [PostgresFact]
    public async Task LostReplyAndNewProcessPreserveTheHttpMutationResult()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var state=new FixtureState();Guid entityId;
        await using(var app=await HostAsync(db,db.Writer,state))
        {
            using var http=Client(app,state);db.Faults.ThrowAt=StorageFaultPoint.AfterCommit;
            using var first=await Send(http,"one",new Input("first"));Assert.Equal(200,(int)first.StatusCode);var result=await first.Content.ReadFromJsonAsync<ApiMutationOutcome>();entityId=result!.EntityId!.Value;Assert.Equal(1,result.Version);
            db.Faults.ThrowAt=null;using var conflict=await Send(http,"one",new Input("different"));Assert.Equal(409,(int)conflict.StatusCode);
            Assert.Equal(1,state.Prepared);
        }
        state.Session=state.Session with {Id=Guid.NewGuid()};
        await using(var restarted=await HostAsync(db,db.CreateWriter(),state))
        {
            using var http=Client(restarted,state);var retries=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Send(http,"one",new Input("first"))));
            foreach(var response in retries){using(response){Assert.Equal(200,(int)response.StatusCode);Assert.Equal(entityId,(await response.Content.ReadFromJsonAsync<ApiMutationOutcome>())!.EntityId);}}
            Assert.Equal(1,state.Prepared);
        }
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.Equal(1,await sql.BusinessEntities.CountAsync());Assert.Equal(1,await sql.BusinessEntityDataItems.CountAsync());Assert.Equal(1,await sql.OperationOutcomes.CountAsync());
    }
    // Ошибочное тело/ключ/размер отсекаются до регистрации и canonical commit.
    [PostgresFact]
    public async Task InvalidTransportCannotReachTheOwnerOrCanonicalWriter()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var state=new FixtureState();await using var app=await HostAsync(db,db.Writer,state);using var http=Client(app,state);
        using var missing=await http.PostAsJsonAsync("/api/v1/fixture",new Input("no key"));Assert.Equal(400,(int)missing.StatusCode);
        using var forbidden=await Send(http,"forbidden",new{Name="x",UserId=Guid.NewGuid(),IsSystem=true});Assert.Equal(400,(int)forbidden.StatusCode);
        using var absentField=await Send(http,"absent-field",new{});Assert.Equal(400,(int)absentField.StatusCode);using var nullField=await Send(http,"null-field",new{Name=(string?)null});Assert.Equal(400,(int)nullField.StatusCode);
        using var large=await Send(http,"large",new Input(new string('x',20000)));Assert.Equal(413,(int)large.StatusCode);
        using var wrongTypeRequest=new HttpRequestMessage(HttpMethod.Post,"/api/v1/fixture"){Content=new StringContent("unsupported")};wrongTypeRequest.Headers.Add("Idempotency-Key","wrong-type");using var wrongType=await http.SendAsync(wrongTypeRequest);Assert.Equal(415,(int)wrongType.StatusCode);Assert.Contains("payload.unsupported",await wrongType.Content.ReadAsStringAsync());
        Assert.Equal(0,state.Prepared);await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.False(await sql.TechnicalStates.AnyAsync());Assert.False(await sql.BusinessEntities.AnyAsync());
    }
    // Bodyless DELETE не отбрасывается routing consumes policy до выполнения known binding.
    [PostgresFact]
    public async Task DeleteWithoutContentTypeUsesTheRegisteredTypedEndpoint()
    {
        await using var db=await PostgresTestDatabase.CreateAsync();var state=new FixtureState();await using var app=await HostAsync(db,db.Writer,state);using var http=Client(app,state);
        using var create=await Send(http,"created",new Input("delete fixture"));Assert.Equal(200,(int)create.StatusCode);var id=(await create.Content.ReadFromJsonAsync<ApiMutationOutcome>())!.EntityId!.Value;
        using var request=new HttpRequestMessage(HttpMethod.Delete,"/api/v1/fixture/"+id);request.Headers.Add("Idempotency-Key","delete");using var response=await http.SendAsync(request);Assert.Equal(200,(int)response.StatusCode);Assert.Contains(id,(await response.Content.ReadFromJsonAsync<ApiMutationOutcome>())!.DeletedEntities);
        await using var sql=new KmsBusinessEntityDbContext(db.Options);Assert.False(await sql.BusinessEntities.AnyAsync());
    }
    // Отдельный host создаёт новый bus/gate/request scopes и фиксированный typed fake owner.
    private static async Task<WebApplication> HostAsync(PostgresTestDatabase database,DataProviderTransactionService writer,FixtureState state)
    {
        var b=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development"});b.Configuration.Sources.Clear();b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{{"Api:MaxJsonBytes","16384"}});b.Logging.ClearProviders();b.WebHost.UseUrls("http://127.0.0.1:0");
        b.Services.AddBusinessEntityBus();b.Services.AddSingleton(writer);b.Services.AddSingleton(state);b.Services.AddSingleton<StorageEndpoint>();b.Services.AddScoped<OwnerEndpoint>();
        b.Services.AddBusEndpoint<StorageEndpoint>(typeof(BeginApiOperationRequest),typeof(ReadApiOperationRequest),typeof(SaveEntityBatchRequest));b.Services.AddBusEndpoint<OwnerEndpoint>(typeof(CreateFixture),typeof(DeleteFixture));
        b.Services.AddScoped<IUserApiConnector,Security>();b.Services.AddScoped<ApiRequestDispatcher>();b.Services.AddScoped<ApiOperationExecutor>();b.Services.AddScoped<ApiWriteContext>();b.Services.AddSingleton<ApiOperationGate>();b.Services.AddSingleton<ApiCapabilityCatalog>();b.Services.AddSingleton<ApiTransportLimits>();b.Services.AddSingleton<ApiConcurrencyGate>();
        b.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions,Authentication>("Fixture",_=>{});b.Services.AddAuthorization(o=>o.AddPolicy("ExternalApi",p=>p.AddAuthenticationSchemes("Fixture").RequireAuthenticatedUser()));
        b.Services.AddRateLimiter(o=>o.AddFixedWindowLimiter("external-api",l=>{l.PermitLimit=1000;l.Window=TimeSpan.FromMinutes(1);l.QueueLimit=0;}));
        b.Services.AddApiBinding<Input,CreateFixture,CreatedFixture,ApiMutationOutcome>(new("fixture",1,"fixture.create",1,"FixtureOwner","Fixture","POST","/fixture","fixture.in.v1","fixture.out.v1",true),static(i,_)=>new(Guid.NewGuid(),i.Name),static _=>throw new NotSupportedException());b.Services.AddApiBinding<ApiEmptyInput,DeleteFixture,CreatedFixture,ApiMutationOutcome>(new("fixture",1,"fixture.delete",1,"FixtureOwner","Fixture delete","DELETE","/fixture/{id:guid}","fixture.delete.in.v1","fixture.delete.out.v1",true),static(_,h)=>new(Guid.NewGuid(),ApiHttpInput.Id(h)),static _=>throw new NotSupportedException());b.Services.AddApiToolset("FixtureOwner");
        var app=b.Build();app.UseMiddleware<ApiErrorMiddleware>();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();var catalog=app.Services.GetRequiredService<ApiCapabilityCatalog>();await catalog.InitializeAsync(default);catalog.Map(app);await app.StartAsync();return app;
    }
    // Header authentication здесь — только seam теста transport; настоящие JWT проверяют ApiBearerProtocolTests.
    private static HttpClient Client(WebApplication app,FixtureState state){var http=new HttpClient{BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};http.DefaultRequestHeaders.Authorization=new("Bearer","fixture");http.DefaultRequestHeaders.Add("X-BE-Session-Id",state.Session.Id.ToString());return http;}
    // Внешний путь не содержит произвольного CLR имени/объектного command envelope.
    private static Task<HttpResponseMessage> Send(HttpClient http,string key,object body){var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/fixture"){Content=JsonContent.Create(body)};request.Headers.Add("Idempotency-Key",key);return http.SendAsync(request);}
    public sealed record Input(string Name);
    public sealed record CreateFixture(Guid RequestId,string Name):BusRequest(RequestId);
    public sealed record CreatedFixture(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
    public sealed record DeleteFixture(Guid RequestId,Guid EntityId):BusRequest(RequestId);
    private sealed class FixtureState{public int Prepared;public ApiSessionInfo Session=new(Guid.NewGuid(),Guid.NewGuid(),"fixture-client",Guid.NewGuid(),1,DateTime.UtcNow.AddHours(1),"Active",["fixture.create","fixture.delete"],new());}
    private sealed class Security(BusRequestScope scope,FixtureState state):IUserApiConnector
    {
        public Task<ApiSessionInfo> OpenAsync(ApiSessionOptions options,CancellationToken ct)=>throw new NotSupportedException();
        public Task CloseAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ApiSessionInfo> ValidateAsync(Guid id,string? capability,CancellationToken ct){Assert.Equal(state.Session.Id,id);scope.SetUser(state.Session.UserId);scope.SetAuthenticationEpoch(1);scope.SetApiContext(id,state.Session.ClientId);return Task.FromResult(state.Session);}
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory log,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,log,encoder)
    {protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(Request.Headers.Authorization=="Bearer fixture"?AuthenticateResult.Success(new AuthenticationTicket(new(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub","fixture")],"Fixture")),"Fixture")):AuthenticateResult.NoResult());}
    // Исключительно тестовый typed владелец готовит настоящий Document и один пакет.
    private sealed class OwnerEndpoint(IMessageBus bus,BusRequestScope scope,BusRequestClient client,ApiWriteContext write,FixtureState state,ILogger<OwnerEndpoint> logger):ScopedBusEndpoint(bus,scope,logger)
    {
        protected override void SubscribeRequests(){Subscribe<DeleteFixture,CreatedFixture>(async r=>{await client.SendAsync<SaveEntityBatchRequest,SaveEntityBatchResponse>(new(Guid.NewGuid(),write.Attach(new(write.OperationId,"FixtureOwner",null){DeleteEntities=[r.EntityId]},r.EntityId)));return new(r.RequestId);},(id,error)=>new(id,error));Subscribe<CreateFixture,CreatedFixture>(async r=>{Interlocked.Increment(ref state.Prepared);var id=write.Id("document");var batch=write.Attach(new(write.OperationId,"FixtureOwner",null){Entities=[new(){Id=id,Name=r.Name,EntityType=global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id}],Payloads=[new(id,0,new Document{Name=r.Name,Text="fixture"})]},id);await client.SendAsync<SaveEntityBatchRequest,SaveEntityBatchResponse>(new(Guid.NewGuid(),batch));return new(r.RequestId);},(id,error)=>new(id,error));}
    }
    // Не подменяет storage: только подключает настоящий writer к общей шине тестового HTTP host.
    private sealed class StorageEndpoint(IMessageBus bus,DataProviderTransactionService writer,ILogger<StorageEndpoint> logger):BusEndpoint(bus,logger)
    {
        protected override void SubscribeRequests()
        {Subscribe<BeginApiOperationRequest,BeginApiOperationResponse>(async r=>new(r.RequestId,await writer.BeginApiOperationAsync(r.Operation,r.Context)),(id,e)=>new(id,null,e));Subscribe<ReadApiOperationRequest,ReadApiOperationResponse>(async r=>new(r.RequestId,await writer.ReadApiOperationAsync(r.OperationId,r.Context)),(id,e)=>new(id,null,e));Subscribe<SaveEntityBatchRequest,SaveEntityBatchResponse>(async r=>new(r.RequestId,await writer.SaveAsync(r.Batch,r.Context)),(id,e)=>new(id,null,e));}
    }
}
