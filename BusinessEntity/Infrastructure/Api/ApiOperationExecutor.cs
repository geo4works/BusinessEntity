using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.Infrastructure.Api;

// Scoped executor не пишет SQL и не вызывает предметный сервис в обход общей шины.
public sealed class ApiOperationExecutor(BusRequestClient client,BusRequestScope scope,ApiOperationGate gate)
{
    // Hash вычисляется из известного типа запроса без корреляции/сессии, а ID включает субъект/client/capability/key.
    public async Task<ApiMutationOutcome> ExecuteAsync<TRequest,TResponse>(HttpContext http,ApiCapability definition,TRequest request,ApiSessionInfo session,CancellationToken ct)
        where TRequest:BusRequest where TResponse:BusResponse
    {
        var specification=Identity(http,definition,request,session);var id=specification.OperationId;var hash=specification.RequestHash;
        using var accepted=await gate.EnterAsync(id,ct);
        var receipt=await BeginAsync(specification,ct);
        if(receipt.State=="Committed")return Permitted(receipt,session);
        scope.SetApiOperation(id,hash);
        try{await client.SendAsync<TRequest,TResponse>(request,ct);}
        catch(Exception ex)when(ex is not OperationCanceledException)
        {
            // Ошибка ответа после commit всё равно возвращает достоверный SQL результат.
            ApiOperationReceipt? committed=null;
            try{committed=await TryReadAsync(id,CancellationToken.None);}catch{ /* Недоступное хранилище не доказывает rollback. */ }
            if(committed?.State=="Committed")return Permitted(committed,session);
            throw Failure(ex,id);
        }
        var result=await TryReadAsync(id,ct);
        return result?.State=="Committed"?Permitted(result,session):throw new ApiFault(503,"operation.unknown","Подтверждённый исход пока отсутствует.",id);
    }
    // Technical/deferred owner возвращает свой известный DTO лишь после durable регистрации reference.
    public async Task<TResponse> ExecuteRegisteredAsync<TRequest,TResponse>(HttpContext http,ApiCapability definition,TRequest request,ApiSessionInfo session,CancellationToken ct)
        where TRequest:BusRequest where TResponse:BusResponse
    {
        var identity=Identity(http,definition,request,session);using var held=await gate.EnterAsync(identity.OperationId,ct);
        await BeginAsync(identity,ct);scope.SetApiOperation(identity.OperationId,identity.RequestHash);
        try
        {
            var response=await client.SendAsync<TRequest,TResponse>(request,ct);var receipt=await TryReadAsync(identity.OperationId,ct);
            if(receipt?.ReferenceId==null||receipt.State=="Pending")throw new ApiFault(503,"operation.unknown","Регистрация результата не подтверждена.",identity.OperationId);
            CheckScope(receipt,session);return response;
        }
        catch(Exception ex)when(ex is not OperationCanceledException){throw Failure(ex,identity.OperationId);}
    }
    // Semantic identity не зависит от нового RequestId, SQL timestamp или SessionId.
    private static ApiOperationIdentity Identity<TRequest>(HttpContext http,ApiCapability definition,TRequest request,ApiSessionInfo session)where TRequest:BusRequest
    {
        var key=http.Request.Headers["Idempotency-Key"].ToString();if(!System.Text.RegularExpressions.Regex.IsMatch(key,"^[A-Za-z0-9_.:-]{1,128}$"))throw new ApiFault(400,"validation.failed","Нужен Idempotency-Key длиной 1–128 допустимых ASCII символов.");
        var normalized=JsonSerializer.SerializeToNode(request)!.AsObject();normalized.Remove("RequestId");normalized.Remove("Context");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {definition.Id,definition.Version,definition.WriteMode,Request=normalized}))));
        return new(StableId($"be.api.v1:{session.UserId:N}:{session.ClientId}:{definition.Id}:{key}"),definition.OwnerMiniAppId,definition.Id,definition.Version,hash,definition.WriteMode);
    }
    // Registration не объявляет commit; недоступный ответ сохраняет корреляцию Unknown.
    private async Task<ApiOperationReceipt> BeginAsync(ApiOperationIdentity identity,CancellationToken ct)
    {try{return(await client.SendAsync<BeginApiOperationRequest,BeginApiOperationResponse>(new(Guid.NewGuid(),identity),ct)).Result??throw new InvalidDataException();}catch(Exception ex)when(ex is not OperationCanceledException){throw Failure(ex,identity.OperationId);}}
    // Исход принадлежит автору/client и должен оставаться в пределах нового допуска и пространства.
    public async Task<ApiOperationReceipt> ReadAsync(Guid id,ApiSessionInfo session,CancellationToken ct)
    {var receipt=await TryReadAsync(id,ct)??throw new ApiFault(404,"api.operation_not_found","Операция не найдена.");CheckScope(receipt,session);return receipt;}
    // Отсутствие row не объявляется rollback; caller решает, как отображать Unknown/Pending.
    private async Task<ApiOperationReceipt?> TryReadAsync(Guid id,CancellationToken ct)=>(await client.SendAsync<ReadApiOperationRequest,ReadApiOperationResponse>(new(Guid.NewGuid(),id),ct)).Result;
    // Timeout/ошибка транспорта или SQL не означают, что ещё работающий writer откатился.
    private static ApiFault Failure(Exception ex,Guid id)
    {var error=BusError.FromException(ex);return ApiProblems.Status(error.Code)>=500?new(503,"operation.unknown","Подтверждённый исход пока недоступен. Повторите тот же запрос или проверьте statusUrl.",id):new(ApiProblems.Status(error.Code),error.Code,error.Message,id);}
    // Нельзя расширить доступ к результатам новой менее ограниченной сессией другого client.
    private static void CheckScope(ApiOperationReceipt receipt,ApiSessionInfo session)
    {if(!session.Capabilities.Contains(receipt.Identity.CapabilityId,StringComparer.Ordinal)||(receipt.SpaceId.HasValue&&!session.Spaces.Allows(receipt.SpaceId.Value)))throw new ApiFault(403,"api.capability_denied","Исход не входит в текущий допуск.");}
    // Public mutation response берётся из атомарного receipt, включая сохранённые generated IDs.
    private static ApiMutationOutcome Permitted(ApiOperationReceipt receipt,ApiSessionInfo session)
    {CheckScope(receipt,session);return receipt.Result??throw new InvalidDataException();}
    // Одинаковая техническая identity одинакова на Windows/Linux и после рестарта приложения.
    public static Guid StableId(string text)=>new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0,16));
}

// Сериализует конкурентные HTTP повторы; SQL receipt остаётся единственным источником истины после crash.
public sealed class ApiOperationGate
{
    private readonly object _gate=new();
    private readonly Dictionary<Guid,Entry> _entries=[];
    // Refcount учитывает и ожидающих: новый запрос не создаёт второй lock пока старый ещё используется.
    public async Task<IDisposable> EnterAsync(Guid id,CancellationToken ct)
    {
        Entry entry;lock(_gate){if(!_entries.TryGetValue(id,out entry!))_entries.Add(id,entry=new());entry.References++;}
        try{await entry.Semaphore.WaitAsync(ct);}
        catch{Release(id,entry,false);throw;}
        return new Lease(()=>Release(id,entry,true));
    }
    // Последний caller освобождает только собственную запись, без неограниченного кэша ключей.
    private void Release(Guid id,Entry entry,bool held)
    {if(held)entry.Semaphore.Release();lock(_gate){if(--entry.References==0){_entries.Remove(id);entry.Semaphore.Dispose();}}}
    // Ключ не хранит тело запроса, токен или секретный Idempotency-Key.
    private sealed class Entry{public readonly SemaphoreSlim Semaphore=new(1,1);public int References;}
    // Повтор Dispose не меняет refcount.
    private sealed class Lease(Action release):IDisposable{private Action? _release=release;public void Dispose()=>Interlocked.Exchange(ref _release,null)?.Invoke();}
}

// Владельцы используют эти технические IDs при подготовке своего обычного EntitySaveBatch.
public sealed class ApiWriteContext(BusRequestScope scope)
{
    public Guid OperationId=>scope.ApiOperationId??throw new InvalidOperationException("Нет принятой API операции.");
    // Generated EntityId/ChunkId/RelationId не меняется при повторе принятого запроса.
    public Guid Id(string purpose)=>StableOperationIdentity.Create(OperationId,purpose);
    // Primary entity/space предоставлены владельцем после его предметных проверок.
    public EntitySaveBatch Attach(EntitySaveBatch batch,Guid? entityId,Guid? spaceId=null,IReadOnlyList<ApiPermissionCheck>? checks=null)
        =>batch with {ApiOperation=new(OperationId,scope.ApiRequestHash??throw new InvalidDataException(),entityId,spaceId,checks)};
}
