using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;

public sealed record ApiPreparedChunkIndex(Guid Id,int Position,string Checksum);
public sealed record ApiPreparedChunkSet(Guid Id,Guid EntityId,Guid UploadId,string Sha256,IReadOnlyList<ApiPreparedChunkIndex> Index,long Length);
public sealed record OpenApiPreparedChunksRequest(Guid RequestId,Guid SetId,Guid EntityId,Guid UploadId):BusRequest(RequestId);
public sealed record OpenApiPreparedChunksResponse(Guid RequestId,Guid? WriteLeaseId,ApiPreparedChunkSet? Existing,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record SealApiPreparedChunksRequest(Guid RequestId,Guid LeaseId):BusRequest(RequestId);
public sealed record SealApiPreparedChunksResponse(Guid RequestId,ApiPreparedChunkSet? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record AbandonApiPreparedChunksRequest(Guid RequestId,Guid LeaseId):BusRequest(RequestId);
public sealed record AbandonApiPreparedChunksResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);

public sealed record ApiStagedChunk(Guid Id,IReadOnlyList<BusinessEntity.Core.Chunks.BusinessEntityDataBlock> Blocks);
