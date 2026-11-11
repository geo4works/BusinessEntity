using System.Collections.Concurrent;
using System.Security.Cryptography;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

// Только DataProvider открывает физические staging paths; вызывающий получает одноразовый opaque lease.
internal sealed partial class ApiStagingStore(IConfiguration configuration,ApiHeavyWorkGate heavy):IApiStagingDataPlane
{
    private readonly string _root=Path.Combine(Path.GetFullPath(configuration["Storage:RootPath"]??throw new InvalidOperationException()),".api-uploads");
    private readonly ConcurrentDictionary<Guid,PartLease> _parts=new();
    private readonly ConcurrentDictionary<Guid,ReadLease> _reads=new();
    private readonly ApiOperationGate _assemblers=new();
    public string ReadyPath(Guid id)=>Path.Combine(_root,id.ToString("N"),"payload.ready");
    private string PartPath(Guid id,int number)=>Path.Combine(_root,id.ToString("N"),"parts",number.ToString("D8")+".blob");
    // Метаданные actor являются серверными; сам lease никогда не принимается как HTTP authority.
    public Guid BeginPart(Guid upload,int number,long length,string hash,BusRequestContext actor)
    {
        Expire();var id=Guid.NewGuid();var directory=Path.Combine(_root,upload.ToString("N"),"temporary");Directory.CreateDirectory(directory);
        _parts[id]=new(upload,number,length,hash,actor.UserId!.Value,actor.ApiClientId!,Path.Combine(directory,id.ToString("N")+".tmp"),DateTime.UtcNow.AddMinutes(5));return id;
    }
    public async Task CopyPartAsync(Guid leaseId,Stream input,CancellationToken ct)
    {
        var lease=Part(leaseId);if(Interlocked.CompareExchange(ref lease.Started,1,0)!=0)throw new PublicOperationException("upload.part_conflict","Lease уже использован.");
        try
        {
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long total=0;var buffer=new byte[65536];
            await using var output=new FileStream(lease.Path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true);
            while(true){var read=await input.ReadAsync(buffer,ct);if(read==0)break;total=checked(total+read);if(total>lease.Length)throw new ArgumentException("Часть превышает заявленную длину.");hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),ct);}
            await output.FlushAsync(ct);if(total!=lease.Length||!string.Equals(Convert.ToHexString(hash.GetHashAndReset()),lease.Hash,StringComparison.OrdinalIgnoreCase))throw new PublicOperationException("upload.digest_conflict","Длина/digest части не совпали.");
            lease.Complete=true;
        }
        catch{TryDelete(lease.Path);throw;}
    }
    // Rename выполняется только после полноценного проверенного потока; одинаковый retry не меняет старый файл.
    public PartLease CommitPart(Guid id,BusRequestContext actor)
    {
        var lease=Part(id);if(!lease.Complete||lease.UserId!=actor.UserId||lease.ClientId!=actor.ApiClientId)throw new UnauthorizedAccessException();
        var destination=PartPath(lease.UploadId,lease.Number);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if(File.Exists(destination)){using var stream=File.OpenRead(destination);if(!string.Equals(Convert.ToHexString(SHA256.HashData(stream)),lease.Hash,StringComparison.OrdinalIgnoreCase))throw new PublicOperationException("upload.part_conflict","Принята другая часть.");TryDelete(lease.Path);}else File.Move(lease.Path,destination);
        _parts.TryRemove(id,out _);return lease;
    }
    public PartLease Part(Guid id)=>_parts.TryGetValue(id,out var lease)&&lease.ExpiresAt>DateTime.UtcNow?lease:throw new KeyNotFoundException("Lease истёк.");
    // Полная загрузка собирается bounded buffer под общим heavy gate; HTTP parts не создают версии статьи.
    public async Task<string> AssembleAsync(Guid upload,int count,long expectedLength,string? expectedHash,CancellationToken ct)
    {
        using var assembled=await _assemblers.EnterAsync(upload,ct);
        try
        {
            using var heavyLease=await heavy.EnterAsync(ct);var ready=ReadyPath(upload);var temporary=ready+".assembling";Directory.CreateDirectory(Path.GetDirectoryName(ready)!);TryDelete(temporary);
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long length=0;var buffer=new byte[65536];
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
            {for(var number=0;number<count;number++){await using var input=File.OpenRead(PartPath(upload,number));while(true){var read=await input.ReadAsync(buffer,ct);if(read==0)break;length+=read;digest.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),ct);}}await output.FlushAsync(ct);}
            var hash=Convert.ToHexString(digest.GetHashAndReset());if(length!=expectedLength||(expectedHash!=null&&!string.Equals(hash,expectedHash,StringComparison.OrdinalIgnoreCase))){TryDelete(temporary);throw new PublicOperationException("upload.digest_conflict","Полный digest/длина не совпали.");}
            File.Move(temporary,ready,true);return hash;
        }
        finally{}
    }
    public Guid BeginRead(Guid upload)=>BeginStoredRead(ReadyPath(upload));
    public Guid BeginStoredRead(string path){Expire();var id=Guid.NewGuid();_reads[id]=new(path,DateTime.UtcNow.AddMinutes(5));return id;}
    public Stream OpenRead(Guid leaseId)=>_reads.TryRemove(leaseId,out var lease)&&lease.ExpiresAt>DateTime.UtcNow?new FileStream(lease.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true):throw new KeyNotFoundException();
    // Удаляется только собственная staging папка конкретной загрузки, не root storage/volume.
    public void Cancel(Guid upload){var directory=Path.Combine(_root,upload.ToString("N"));if(Directory.Exists(directory))Directory.Delete(directory,true);}
    private void Expire(){foreach(var pair in _prepared.Where(x=>x.Value.ExpiresAt<=DateTime.UtcNow))if(_prepared.TryRemove(pair.Key,out var expired))TryDelete(expired.Path);foreach(var pair in _parts.Where(x=>x.Value.ExpiresAt<=DateTime.UtcNow))if(_parts.TryRemove(pair.Key,out var lease))TryDelete(lease.Path);foreach(var pair in _reads.Where(x=>x.Value.ExpiresAt<=DateTime.UtcNow))_reads.TryRemove(pair.Key,out _);}
    private static void TryDelete(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    internal sealed record ReadLease(string Path,DateTime ExpiresAt);
    internal sealed record PartLease(Guid UploadId,int Number,long Length,string Hash,Guid UserId,string ClientId,string Path,DateTime ExpiresAt){public int Started;public bool Complete;}
}
