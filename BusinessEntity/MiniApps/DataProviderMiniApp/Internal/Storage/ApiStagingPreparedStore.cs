using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

internal sealed partial class ApiStagingStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid,PreparedLease> _prepared=new();
    public string PreparedPath(Guid id)=>Path.Combine(_root,"prepared",id.ToString("N"),"chunks.jsonl");
    public Guid BeginPrepared(Guid setId,Guid entityId,Guid uploadId,BusRequestContext actor)
    {
        Expire();var id=Guid.NewGuid();var path=Path.Combine(_root,"prepared",setId.ToString("N"),id.ToString("N")+".tmp");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _prepared[id]=new(setId,entityId,uploadId,actor.UserId!.Value,actor.ApiClientId!,actor.ApiOperationId!.Value,path,DateTime.UtcNow.AddHours(2));return id;
    }
    public Stream OpenPreparedWrite(Guid id)
    {var lease=Prepared(id);if(Interlocked.CompareExchange(ref lease.Started,1,0)!=0)throw new InvalidOperationException("Lease уже открыт.");return new LimitedOutput(new FileStream(lease.Path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true),1024L*1024*1024);}
    public PreparedLease Prepared(Guid id)=>_prepared.TryGetValue(id,out var value)&&value.ExpiresAt>DateTime.UtcNow?value:throw new KeyNotFoundException();
    public void PublishPrepared(Guid leaseId)
    {var lease=Prepared(leaseId);File.Move(lease.Path,PreparedPath(lease.SetId),false);_prepared.TryRemove(leaseId,out _);}
    public void CancelPrepared(Guid id){var path=Path.GetDirectoryName(PreparedPath(id))!;if(Directory.Exists(path))Directory.Delete(path,true);}
    public void AbandonPrepared(Guid id,BusRequestContext actor)
    {if(!_prepared.TryGetValue(id,out var lease))return;if(lease.UserId!=actor.UserId||lease.ClientId!=actor.ApiClientId||lease.OperationId!=actor.ApiOperationId)throw new UnauthorizedAccessException();if(_prepared.TryRemove(id,out _))File.Delete(lease.Path);}
    internal sealed record PreparedLease(Guid SetId,Guid EntityId,Guid UploadId,Guid UserId,string ClientId,Guid OperationId,string Path,DateTime ExpiresAt){public int Started;}
    private sealed class LimitedOutput(Stream inner,long maximum):Stream
    {
        private long _written;public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>_written;public override long Position{get=>_written;set=>throw new NotSupportedException();}
        private void Reserve(int count){if(_written+count>maximum)throw new InvalidDataException("Промежуточный текст превышает 1 GiB.");_written+=count;}
        public override void Write(byte[] buffer,int offset,int count){Reserve(count);inner.Write(buffer,offset,count);}
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken ct=default){Reserve(buffer.Length);await inner.WriteAsync(buffer,ct);}
        public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
        protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}public override async ValueTask DisposeAsync(){await inner.DisposeAsync();GC.SuppressFinalize(this);}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
