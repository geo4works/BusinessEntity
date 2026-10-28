using BusinessEntity.Core.Saving;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.DocumentsMiniApp.Registration;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration;
using BusinessEntity.MiniApps.SpacesMiniApp.Registration;
using BusinessEntity.MiniApps.SysParametersMiniApp.Registration;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Conversion;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Registration;
using BusinessEntity.MiniApps.DocumentsMiniApp.Internal;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BusinessEntity.Tests.Storage;

// Каждый тест получает собственную новую БД, которую создаёт и удаляет только сам.
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnection;
    private readonly string _databaseName;
    private readonly ServiceProvider _services;
    public DbContextOptions<KmsBusinessEntityDbContext> Options { get; }
    public string StorageRoot { get; }
    public ImmutableEntityFileStore Files { get; }
    private readonly IConfiguration _configuration;
    public TestStorageFaults Faults { get; } = new();
    public DataProviderTransactionService Writer { get; }
    public GraphArchiveRemapper Remapper => _services.GetRequiredService<GraphArchiveRemapper>();
    public ExportFileLeases Leases => _services.GetRequiredService<ExportFileLeases>();
    public ReactiveUI.IMessageBus Bus => _services.GetRequiredService<ReactiveUI.IMessageBus>();

    // Собирает storage-сервисы без UI, Authentik и рабочей БД.
    private PostgresTestDatabase(string adminConnection, string databaseName, string connection)
    {
        _adminConnection = adminConnection; _databaseName = databaseName;
        StorageRoot = Path.Combine(Path.GetTempPath(), "be-arch-tests", databaseName);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Storage:RootPath"] = StorageRoot }).Build();
        Files = new ImmutableEntityFileStore(_configuration, Faults, NullLogger<ImmutableEntityFileStore>.Instance);
        Options = new DbContextOptionsBuilder<KmsBusinessEntityDbContext>().UseNpgsql(connection).Options;
        var services = new ServiceCollection(); services.AddLogging(); services.AddBusinessEntityBus(); services.AddDataProviderMiniApp();
        services.AddDocumentsMiniApp(); services.AddRichTextDocumentMiniApp(); services.AddSpacesMiniApp(); services.AddSysParametersMiniApp();
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.Folder, false));
        services.AddSingleton<IEntityArchivePolicy>(new NoStoredReferencesArchivePolicy(BusinessEntityTypeEnum.MediaVideo, true));
        services.AddSingleton(new EntityUrlPattern("/media-server-files/videos/"));
        _services = services.BuildServiceProvider();
        Writer = CreateWriter();
    }

    // Имитирует новый процесс с теми же SQL-данными и пустой памятью учёта операций.
    public DataProviderTransactionService CreateWriter() => new(Options,
        _services.GetRequiredService<EntityDataStorageCodec>(),
        [new DocumentVersionPolicy(), new RichTextDocumentVersionPolicy()], Faults, _configuration, Files, _services.GetRequiredService<DataProviderCommitPublisher>(), _services.GetServices<IEntityArchivePolicy>());

    // Создаёт изолированную БД с безопасным сгенерированным именем и актуальной схемой.
    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var connection = Environment.GetEnvironmentVariable("BE_TEST_POSTGRES")
            ?? throw new InvalidOperationException("BE_TEST_POSTGRES не задан.");
        var builder = new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" };
        var adminConnection = builder.ConnectionString;
        var name = "be_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await command.ExecuteNonQueryAsync();
        builder.Database = name;
        var result = new PostgresTestDatabase(adminConnection, name, builder.ConnectionString);
        try { await new DataProviderSchema(result.Options).InitializeAsync(); return result; }
        catch { await result.DisposeAsync(); throw; }
    }

    // Удаляет только БД, созданную этим экземпляром; рабочие базы и volumes не затрагиваются.
    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, true);
    }
}

// Управляемый отказ внутри писателя для проверки отката и потерянного ответа после коммита.
internal sealed class TestStorageFaults : IStorageFaults
{
    public StorageFaultPoint? ThrowAt { get; set; }
    // Создаёт сбой только в выбранной точке реальной SQL-транзакции.
    public void Hit(StorageFaultPoint point)
    {
        if (ThrowAt == point) throw new IOException("Искусственный сбой storage-теста.");
    }
}
