using System.Text.Json;
using BusinessEntity.Core.Saving;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Archives;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

internal sealed class UserApiPreferences(UserPreferencesService preferences,UserApiPreferenceContext context,ApiWriteContext write,IDataProviderConnector data)
{
    public async Task<ApiPrintingPreferences> PrintingAsync(CancellationToken ct)
    {await context.UserAsync(ct);var current=await preferences.GetDocPrintPresetsAsync(ct);return new(current.SelectedPresetName,current.Presets.Select(p=>new ApiPrintPreset(p.Name,new(p.Settings.FontScalePercent,p.Settings.MarginTopMm,p.Settings.MarginBottomMm,p.Settings.MarginRightMm,p.Settings.MarginLeftMm))).ToArray());}
    public async Task SavePrintingAsync(ApiPrintingPreferences input,CancellationToken ct)
    {
        var user=await context.UserAsync(ct);if(input.SchemaVersion!=1||input.Presets==null||input.Presets.Length>64||input.SelectedPresetName==null||input.SelectedPresetName.Length>100||input.Presets.Any(p=>p==null||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>100||p.Settings==null))throw new ArgumentException("Неверные именованные настройки печати.");
        var payload=new DocPrintSettingsPresetCollection{SelectedPresetName=input.SelectedPresetName,Presets=input.Presets.Select(p=>new DocPrintSettingsPreset{Name=p.Name,Settings=new(){FontScalePercent=p.Settings.FontScalePercent,MarginTopMm=p.Settings.MarginTopMm,MarginBottomMm=p.Settings.MarginBottomMm,MarginRightMm=p.Settings.MarginRightMm,MarginLeftMm=p.Settings.MarginLeftMm}}).ToList()};
        UserTableMutation[] changes;using(var capture=new UserMutationCapture()){await preferences.UpsertDocPrintPresetsPayloadAsync(user,payload,ct);changes=capture.Changes.ToArray();}await SaveAsync(user,null,changes,[new("Self",user)],ct);
    }
    public async Task<ApiDocumentDisplayPreference> DocumentAsync(Guid id,CancellationToken ct)
    {await context.DocumentAsync(id,ct);return new(await preferences.GetRichDocDisplayedLevelAsync(id,ct));}
    public async Task SaveDocumentAsync(Guid id,ApiDocumentDisplayPreference input,CancellationToken ct)
    {
        var user=await context.UserAsync(ct);var space=await context.DocumentAsync(id,ct);UserTableMutation[] changes;using(var capture=new UserMutationCapture()){await preferences.SaveRichDocDisplayedLevelAsync(id,input.DisplayLevelCount,ct);changes=capture.Changes.ToArray();}await SaveAsync(user,space,changes,[new("Self",user),new("Read",id,space)],ct);
    }
    private Task SaveAsync(Guid user,Guid? space,UserTableMutation[] changes,IReadOnlyList<BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions.ApiPermissionCheck> checks,CancellationToken ct)=>data.SaveAsync(write.Attach(new(write.OperationId,"UserMiniApp",null){OwnedServiceChanges=[new("UserMiniApp","UserFormsV1",JsonSerializer.Serialize(changes,GraphArchiveJson.CreateOptions()))]},user,space,checks),ct);
}
