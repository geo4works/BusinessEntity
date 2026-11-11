using System.Reactive.Linq;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using ReactiveUI;

namespace BusinessEntity.Infrastructure.Api;

// Storage вызывает технический admission после занятия writer, не зная предметных правил User.
public interface IApiCommitAuthorization
{
    Task ValidateAsync(ApiOperationIdentity identity,ApiOperationCommit commit,BusRequestContext actor,CancellationToken ct);
}

// Singleton relay не захватывает scoped User: его endpoint уже поднят при HTTP session validation.
public sealed class ApiBusCommitAuthorization(IMessageBus bus):IApiCommitAuthorization
{
    // Корреляция сохраняет исходный серверный scope, не создавая System authority или вторую шину.
    public async Task ValidateAsync(ApiOperationIdentity identity,ApiOperationCommit commit,BusRequestContext actor,CancellationToken ct)
    {
        var request=new AuthorizeApiCommitRequest(Guid.NewGuid(),identity.CapabilityId,commit.SpaceId,commit.PermissionChecks??[]) {Context=actor};
        var completion=new TaskCompletionSource<AuthorizeApiCommitResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription=bus.Listen<AuthorizeApiCommitResponse>().Where(r=>r.RequestId==request.RequestId&&r.Context==actor).Take(1).Subscribe(r=>completion.TrySetResult(r),e=>completion.TrySetException(e));
        bus.SendMessage(request);var response=await completion.Task.WaitAsync(BusRequestClient.DefaultTimeout,ct);
        if(response.Error!=null)throw new BusRequestFailedException(response.Error);
    }
}
