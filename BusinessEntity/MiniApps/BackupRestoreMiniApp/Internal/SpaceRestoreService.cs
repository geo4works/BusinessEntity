using BusinessEntity.MiniApps.BackupRestoreMiniApp.Contracts;
using System.Text.Json;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Properties;

namespace BusinessEntity.MiniApps.BackupRestoreMiniApp.Internal;

// Восстанавливает новое пространство одним пакетом; частичного бизнес-состояния для cleanup не существует.
public sealed class SpaceRestoreService
{
    private readonly IDataProviderConnector _data;
    private readonly GraphArchiveRemapper _remapper;
    private readonly EntitySaveBatchFactory _batches;
    private readonly SpaceArchivePaths _paths;
    private readonly ILogger<SpaceRestoreService> _logger;
    // Подключает транзакционный connector и правила владельцев для полного restore.
    public SpaceRestoreService(IDataProviderConnector data, GraphArchiveRemapper remapper, EntitySaveBatchFactory batches,
        SpaceArchivePaths paths, ILogger<SpaceRestoreService> logger)
    { _data = data; _remapper = remapper; _batches = batches; _paths = paths; _logger = logger; }


    // Проверяет архив и создаёт новое пространство одной подтверждаемой операцией.
    public Task<SpaceRestoreResult> RestoreSpaceAsync(string backupRootPath, string? targetSpaceName = null, CancellationToken ct = default) =>
        RestoreSpaceAsync(new() { BackupRootPath = backupRootPath, TargetSpaceName = targetSpaceName }, ct);
    // Проверяет архив и создаёт новое пространство одной подтверждаемой операцией.
    public async Task<SpaceRestoreResult> RestoreSpaceAsync(SpaceRestoreRequest request, CancellationToken ct = default)
    {
        var root = _paths.ResolveInput(request.BackupRootPath);
        var source = await new GraphArchiveDirectory().ReadAsync(root, ct);
        var sourceRoot = source.Entities.Single(x => x.Card.Id == source.RootEntityId);
        if (sourceRoot.Card.EntityType != global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id) throw new InvalidDataException("Корень backup не является пространством.");
        var name = string.IsNullOrWhiteSpace(request.TargetSpaceName) ? sourceRoot.Card.Name + "_restored" : request.TargetSpaceName.Trim();
        var plan = _remapper.Remap(source, name);
        var entities = plan.Archive.Entities.Select(entity =>
        {
            if (entity.Card.Id != plan.Archive.RootEntityId) return entity;
            var properties = entity.Properties.ToList();
            var old = properties.SingleOrDefault(p => p.Owner == EntityPropertyOwner.Entity && p.PropertyType == (int)BusinessEntityPropertyTypeEnum.GenericSpaceProperties);
            var settings = old == null ? new GenericSpaceProperties() : JsonSerializer.Deserialize<GenericSpaceProperties>(old.Data, GraphArchiveDirectory.Json)
                ?? throw new InvalidDataException("Повреждены настройки пространства backup.");
            if (settings.SchemaVersion != 1 || settings.Kind != nameof(GenericSpaceProperties) || settings.BackupIntervalMinutes < 1)
                throw new InvalidDataException("Неизвестные настройки пространства backup.");
            settings.DoBackup = !request.DisableBackupForRestoredSpace; settings.BackupFolder = string.Empty;
            if (old != null) properties.Remove(old);
            properties.Add(new(old?.Id ?? Guid.NewGuid(), entity.Card.Id, EntityPropertyOwner.Entity,
                (int)BusinessEntityPropertyTypeEnum.GenericSpaceProperties, JsonSerializer.Serialize(settings, GraphArchiveDirectory.Json),
                nameof(GenericSpaceProperties), old?.CreatedDate ?? DateTime.UtcNow, DateTime.UtcNow));
            return entity with { Properties = properties };
        }).ToArray();
        var archive = plan.Archive with { Entities = entities };
        var files = archive.Entities.SelectMany(entity => entity.Files.Select(file => new EntityFileChange(entity.Card.Id, file.FileId,
            file.Variant, file.FileName, file.ContentType, [], GraphArchiveDirectory.Resolve(root, file.ArchiveEntry)))).ToArray();
        var batch = _batches.Create(global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id) with { ImportSnapshot = archive, Files = files };
        await _data.SaveAsync(batch, ct);
        // Диагностический отчёт записывается после commit; его ошибка не объявляет восстановленный граф откатанным.
        var report = string.Empty; var warnings = new List<string>();
        try
        {
            var reports = Path.Combine(_paths.StorageRoot, "restore-reports"); Directory.CreateDirectory(reports);
            report = Path.Combine(reports, batch.OperationId.ToString("D") + ".json");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { OperationId = batch.OperationId, SourceSpaceId = source.RootEntityId,
                TargetSpaceId = archive.RootEntityId, plan.Identities }, GraphArchiveDirectory.Json), CancellationToken.None);
        }
        catch (Exception exception)
        {
            report = string.Empty; warnings.Add("Пространство восстановлено; диагностический отчёт записать не удалось.");
            _logger.LogError(exception, "Restore committed, report write failed for {OperationId}", batch.OperationId);
        }
        return new() { RestoreSessionId = batch.OperationId, SourceBackupRoot = root, SourceSpaceId = source.RootEntityId,
            TargetSpaceId = archive.RootEntityId, TargetSpaceName = name, RestoredEntityCount = archive.Entities.Count,
            RestoredRelationCount = archive.Relations.Count, RestoreReportPath = report, Warnings = warnings };
    }
}
