using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// Публикация/описание находятся вне истории; restore использует общий immutable snapshot механизм.
internal sealed class RichApiHistoryCommands(IDataProviderConnector data,RichApiAccess access,ApiWriteContext write)
{
    public async Task MetadataAsync(Guid id,bool isPublic,CancellationToken ct)
    {var(entity,space,decision)=await access.ContextAsync(id,ct);if(!decision.CanChangeCommonFlag)throw new UnauthorizedAccessException();entity.IsPublic=isPublic;await data.SaveAsync(write.Attach(new(write.OperationId,"RichTextDocumentMiniApp",null){Entities=[entity]},id,space,[new("ChangeCommon",id,space)]),ct);}
    public async Task PublishAsync(Guid id,int version,CancellationToken ct)
    {var(_,space,decision)=await access.ContextAsync(id,ct);if(!decision.CanPublishDraft)throw new UnauthorizedAccessException();if(version<1)throw new ArgumentException("Публикация выбирает существующую версию.");var batch=write.Attach(new(write.OperationId,"RichTextDocumentMiniApp",null){Publications=[new(id,version)]},id,space,[new("PublishDraft",id,space)]);await data.SaveAsync(batch with{ApiOperation=batch.ApiOperation! with{ResponseVersion=version}},ct);}
    public async Task DescribeAsync(Guid id,int version,string description,CancellationToken ct)
    {var(_,space,decision)=await access.ContextAsync(id,ct);if(!decision.CanEditDraft||!decision.CanViewDraft)throw new UnauthorizedAccessException();if(version<1||description==null||description.Length>8000)throw new ArgumentException("Неверная версия/описание.");var batch=write.Attach(new(write.OperationId,"RichTextDocumentMiniApp",null){VersionDescriptions=[new(id,version,description)]},id,space,[new("EditDraft",id,space),new("History",id,space)]);await data.SaveAsync(batch with{ApiOperation=batch.ApiOperation! with{ResponseVersion=version}},ct);}
    public async Task RestoreAsync(Guid id,int version,CancellationToken ct)
    {var(_,space,decision)=await access.ContextAsync(id,ct);if(!decision.CanEditDraft||!decision.CanViewDraft)throw new UnauthorizedAccessException();if(version<1)throw new ArgumentException("Неверная версия.");await data.SaveAsync(write.Attach(new(write.OperationId,"RichTextDocumentMiniApp",null){Restores=[new(id,version)]},id,space,[new("EditDraft",id,space),new("History",id,space)]),ct);}
    public async Task DeleteAsync(Guid id,Guid? planId,CancellationToken ct)
    {var space=await access.DeleteAsync(id,ct);var batch=write.Attach(new(write.OperationId,"RichTextDocumentMiniApp",null){DeleteEntities=[id]},id,space,[new("Delete",id,space)]);await data.SaveAsync(batch with{ApiOperation=batch.ApiOperation! with{DeletionPlanId=planId}},ct);}
}
