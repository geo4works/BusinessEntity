using BusinessEntity.Infrastructure.Bus;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts;

public sealed record ApiPrintPageSettings(int FontScalePercent=100,int MarginTopMm=15,int MarginBottomMm=15,int MarginRightMm=15,int MarginLeftMm=15);
public sealed record ApiPrintPreset(string Name,ApiPrintPageSettings Settings);
public sealed record ApiPrintingPreferences(string SelectedPresetName,ApiPrintPreset[] Presets,int SchemaVersion=1);
public sealed record ApiDocumentDisplayPreference(int DisplayLevelCount);
public sealed record ReadApiPrintingPreferencesRequest(Guid RequestId):BusRequest(RequestId);
public sealed record ReadApiPrintingPreferencesResponse(Guid RequestId,ApiPrintingPreferences? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record SaveApiPrintingPreferencesRequest(Guid RequestId,ApiPrintingPreferences Input):BusRequest(RequestId);
public sealed record ReadApiDocumentPreferenceRequest(Guid RequestId,Guid DocumentId):BusRequest(RequestId);
public sealed record ReadApiDocumentPreferenceResponse(Guid RequestId,ApiDocumentDisplayPreference? Result,BusError? Error=null):BusResponse(RequestId,Error);
public sealed record SaveApiDocumentPreferenceRequest(Guid RequestId,Guid DocumentId,ApiDocumentDisplayPreference Input):BusRequest(RequestId);
public sealed record SaveApiPreferencesResponse(Guid RequestId,BusError? Error=null):BusResponse(RequestId,Error);
