namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

internal sealed partial class ApiStagingStore
{
    // До старта background runtimes нет живых leases. Accepted parts/Ready/sealed input сохраняются.
    internal void RecoverTemporary()
    {
        if(!Directory.Exists(_root))return;
        foreach(var directory in Directory.EnumerateDirectories(_root))
        {
            if(!Guid.TryParseExact(Path.GetFileName(directory),"N",out _))continue;
            var temporary=Path.Combine(directory,"temporary");if(Directory.Exists(temporary))foreach(var file in Directory.EnumerateFiles(temporary,"*.tmp"))File.Delete(file);
            File.Delete(Path.Combine(directory,"payload.ready.assembling"));
        }
        var prepared=Path.Combine(_root,"prepared");if(Directory.Exists(prepared))foreach(var directory in Directory.EnumerateDirectories(prepared))
            if(Guid.TryParseExact(Path.GetFileName(directory),"N",out _))foreach(var file in Directory.EnumerateFiles(directory,"*.tmp"))File.Delete(file);
    }
}
