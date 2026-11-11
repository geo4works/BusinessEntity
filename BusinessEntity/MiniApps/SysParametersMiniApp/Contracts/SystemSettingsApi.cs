using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.SysParametersMiniApp.Contracts;

public sealed record SystemSettingsValues(string CompanyName,int RichTextChunkCharLimit,int RichTextInitialChunkCount,int RichTextTableOfContentsBeforeBuffer,int RichTextTableOfContentsAfterBuffer,int RichTextScrollPreviousChunkCount,bool RichTextHideTableOfContentsScrollbar,int RichTextEditChunksBeforeFocused,int RichTextEditChunksAfterFocused,int RichTextEditChunksOnOpen);
public sealed record ReadSystemSettingsApiRequest(Guid RequestId):BusRequest(RequestId);
public sealed record ReadSystemSettingsApiResponse(Guid RequestId,SystemSettingsValues? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record SaveSystemSettingsApiRequest(Guid RequestId,SystemSettingsValues Input):BusRequest(RequestId);
public sealed record SaveSystemSettingsApiResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
