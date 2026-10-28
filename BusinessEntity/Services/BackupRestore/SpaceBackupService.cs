using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;

namespace BusinessEntity.Services.BackupRestore;

// Фоновый владелец расписания backup; получает весь canonical graph только через шину.
public sealed class SpaceBackupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SpaceArchivePaths _paths;
    private readonly GraphArchiveDirectory _directory = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<SpaceBackupService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, DateTime> _next = new();
    // Подключает системные scopes, архивные пути и журнал фонового backup.
    public SpaceBackupService(IServiceScopeFactory scopes, SpaceArchivePaths paths, IConfiguration configuration, ILogger<SpaceBackupService> logger)
    { _scopes = scopes; _paths = paths; _configuration = configuration; _logger = logger; }

    public sealed class SpaceBackupRunResult
    {
        public Guid SpaceId { get; init; }
        public string SpaceName { get; init; } = string.Empty;
        public int ChangedEntityCount { get; init; }
        public bool RelationsChanged { get; init; }
        public bool ManifestUpdated { get; init; }
        public DateTime FinishedUtc { get; init; }
        public TimeSpan NextInterval { get; init; }
    }
    public sealed class SpaceBackupClearResult
    {
        public Guid SpaceId { get; init; }
        public string BackupFolder { get; init; } = string.Empty;
        public string DisplayBackupFolder { get; init; } = string.Empty;
        public bool Deleted { get; init; }
    }
    // Запускает due-проверки с отменой и диагностикой отказов.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_configuration.GetValue("SpaceBackup:Enabled", true)) await RunCycleAsync(true, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_configuration.GetValue("SpaceBackup:PollIntervalSeconds", 30)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Space backup cycle failed.");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
    // Возвращает фактический каталог backup выбранного пространства.
    public string GetEffectiveSpaceBackupFolder(Guid id, GenericSpaceProperties settings) => _paths.ForSpace(id, settings);
    // Возвращает отображаемый пользователю путь backup.
    public string GetDisplaySpaceBackupFolder(Guid id, GenericSpaceProperties settings) => _paths.Display(_paths.ForSpace(id, settings));
    // Проверяет все включённые пространства по явной команде.
    public Task<IReadOnlyList<SpaceBackupRunResult>> RunBackupCycleAsync(CancellationToken ct = default) => RunCycleAsync(false, ct);
    // Выбирает пространства и их настройки в отдельном системном scope.
    private async Task<IReadOnlyList<SpaceBackupRunResult>> RunCycleAsync(bool dueOnly, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BusRequestScope>().MarkSystem();
        var data = scope.ServiceProvider.GetRequiredService<IDataProviderConnector>();
        var helper = scope.ServiceProvider.GetRequiredService<SpaceHelper>();
        var spaces = (await data.GetAllAsync(ct)).Where(x => x.EntityType == BusinessEntityTypeEnum.Space).ToArray();
        var results = new List<SpaceBackupRunResult>();
        foreach (var space in spaces)
        {
            var settings = await helper.GetGenericSpacePropertiesAsync(space.Id, ct);
            if (!settings.DoBackup) continue;
            lock (_next) { if (dueOnly && _next.TryGetValue(space.Id, out var next) && next > DateTime.UtcNow) continue; }
            try { results.Add(await RunManualBackupAsync(space.Id, ct)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { _logger.LogError(exception, "Backup failed for {SpaceId}", space.Id); }
        }
        return results;
    }
    // Экспортирует согласованный подграф и публикует полный manifest.
    public async Task<SpaceBackupRunResult> RunManualBackupAsync(Guid spaceId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BusRequestScope>().MarkSystem();
            var data = scope.ServiceProvider.GetRequiredService<IDataProviderConnector>();
            var space = await data.GetByIdAsync(spaceId, ct);
            if (space?.EntityType != BusinessEntityTypeEnum.Space) throw new KeyNotFoundException("Пространство не найдено.");
            var settings = await scope.ServiceProvider.GetRequiredService<SpaceHelper>().GetGenericSpacePropertiesAsync(spaceId, ct);
            var export = await data.ExportSubgraphAsync(new(spaceId, ["Contains", "RelatesTo"], [BusinessEntityTypeEnum.Space]), ct);
            try
            {
                var result = await _directory.WriteAsync(_paths.ForSpace(spaceId, settings), export, ct);
                var finished = DateTime.UtcNow; var interval = TimeSpan.FromMinutes(settings.BackupIntervalMinutes);
                lock (_next) _next[spaceId] = finished + interval;
                return new() { SpaceId = spaceId, SpaceName = space.Name, ChangedEntityCount = result.ChangedEntities,
                    RelationsChanged = result.RelationsChanged, ManifestUpdated = result.ManifestUpdated, FinishedUtc = finished, NextInterval = interval };
            }
            finally { await data.ReleaseExportAsync(export.ExportId, CancellationToken.None); }
        }
        finally { _gate.Release(); }
    }
    // Очищает только проверенный архив своего пространства внутри storage.
    public async Task<SpaceBackupClearResult> ClearSpaceBackupAsync(Guid spaceId, GenericSpaceProperties settings, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var path = _paths.ForSpace(spaceId, settings);
            if (!SpaceArchivePaths.IsInside(path, _paths.StorageRoot) || SpaceArchivePaths.IsInside(Path.Combine(_paths.StorageRoot, "business-entities"), path))
                throw new InvalidOperationException("Очистка разрешена только для каталога backup внутри storage.");
            if (Directory.Exists(path))
            {
                var archive = await _directory.ReadAsync(path, ct);
                if (archive.RootEntityId != spaceId) throw new InvalidDataException("Каталог принадлежит другому пространству.");
                Directory.Delete(path, true);
                return new() { SpaceId = spaceId, BackupFolder = path, DisplayBackupFolder = _paths.Display(path), Deleted = true };
            }
            return new() { SpaceId = spaceId, BackupFolder = path, DisplayBackupFolder = _paths.Display(path) };
        }
        finally { _gate.Release(); }
    }
}
