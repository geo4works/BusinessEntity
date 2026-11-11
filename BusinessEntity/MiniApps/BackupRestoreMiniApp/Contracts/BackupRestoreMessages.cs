using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;

// Сообщение команды RunManualBackupAsync.
public sealed record BackupRestoreRunManualBackupRequest(Guid RequestId, Guid SpaceId) : BusRequest(RequestId);
// Результат команды RunManualBackupAsync.
public sealed record BackupRestoreRunManualBackupResponse(Guid RequestId, SpaceBackupRunResult Value, BusError? Error = null) : BusResponse(RequestId, Error);
// Сообщение команды RunBackupCycleAsync.
public sealed record BackupRestoreRunBackupCycleRequest(Guid RequestId) : BusRequest(RequestId);
// Результат команды RunBackupCycleAsync.
public sealed record BackupRestoreRunBackupCycleResponse(Guid RequestId, IReadOnlyList<SpaceBackupRunResult> Value, BusError? Error = null) : BusResponse(RequestId, Error);
// Сообщение команды ClearSpaceBackupAsync.
public sealed record BackupRestoreClearSpaceBackupRequest(Guid RequestId, Guid SpaceId, GenericSpaceProperties Settings) : BusRequest(RequestId);
// Результат команды ClearSpaceBackupAsync.
public sealed record BackupRestoreClearSpaceBackupResponse(Guid RequestId, SpaceBackupClearResult Value, BusError? Error = null) : BusResponse(RequestId, Error);
// Сообщение команды GetDisplaySpaceBackupFolderAsync.
public sealed record BackupRestoreGetDisplaySpaceBackupFolderRequest(Guid RequestId, Guid SpaceId, GenericSpaceProperties Settings) : BusRequest(RequestId);
// Результат команды GetDisplaySpaceBackupFolderAsync.
public sealed record BackupRestoreGetDisplaySpaceBackupFolderResponse(Guid RequestId, string Value, BusError? Error = null) : BusResponse(RequestId, Error);
// Сообщение команды RestoreSpaceAsync.
public sealed record BackupRestoreRestoreSpaceRequest(Guid RequestId, SpaceRestoreRequest Request) : BusRequest(RequestId);
// Результат команды RestoreSpaceAsync.
public sealed record BackupRestoreRestoreSpaceResponse(Guid RequestId, SpaceRestoreResult Value, BusError? Error = null) : BusResponse(RequestId, Error);
