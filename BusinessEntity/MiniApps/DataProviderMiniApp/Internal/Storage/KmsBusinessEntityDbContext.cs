using Microsoft.EntityFrameworkCore;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Transactions;
using BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Models;

namespace BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage;

/// <summary>
/// DbContext, используемый репозиториями BusinessEntityData. Конфигурация таблиц настраивается в вызывающем коде.
/// </summary>
internal sealed class KmsBusinessEntityDbContext : DbContext
{
    // Подключает единственный контекст storage к явно переданным SQL-настройкам.
    public KmsBusinessEntityDbContext(DbContextOptions<KmsBusinessEntityDbContext> options) : base(options)
    {
    }

    public DbSet<BusinessEntityDto> BusinessEntities => Set<BusinessEntityDto>();
    public DbSet<BusinessEntityRelationDto> BusinessEntityRelations => Set<BusinessEntityRelationDto>();
    public DbSet<BusinessEntityDataDto> BusinessEntityDataItems => Set<BusinessEntityDataDto>();
    public DbSet<BusinessEntityDataChunkDto> BusinessEntityDataChunks => Set<BusinessEntityDataChunkDto>();
    public DbSet<BusinessEntityPropertyDto> BusinessEntityProperties => Set<BusinessEntityPropertyDto>();
    public DbSet<BusinessEntityDataPropertyDto> BusinessEntityDataProperties => Set<BusinessEntityDataPropertyDto>();
    public DbSet<BusinessEntityDataChunkPropertyDto> BusinessEntityDataChunkProperties => Set<BusinessEntityDataChunkPropertyDto>();
    public DbSet<BusinessEntityCommentDto> BusinessEntityComments => Set<BusinessEntityCommentDto>();

    internal DbSet<OperationOutcomeRow> OperationOutcomes => Set<OperationOutcomeRow>();
    internal DbSet<ChunkReferenceRow> ChunkReferences => Set<ChunkReferenceRow>();
    internal DbSet<EntityFileRow> EntityFiles => Set<EntityFileRow>();
    internal DbSet<VersionFileReferenceRow> VersionFileReferences => Set<VersionFileReferenceRow>();
    internal DbSet<StorageSchemaRow> StorageSchema => Set<StorageSchemaRow>();

    // Определяет текущую схему, ограничения версий и ссылки целостности.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessEntityDto>().ToTable("BusinessEntities");
        modelBuilder.Entity<BusinessEntityRelationDto>().ToTable("BusinessEntityRelations");
        modelBuilder.Entity<BusinessEntityDataDto>().ToTable("BusinessEntityDataItems");
        modelBuilder.Entity<BusinessEntityDataDto>().HasKey(x => new { x.Id, x.Version });
        modelBuilder.Entity<BusinessEntityDataDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataDto>().Property(x => x.VersionDescription).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataDto>().HasIndex(x => new { x.BusinessEntityId, x.Version });
        modelBuilder.Entity<BusinessEntityDataChunkDto>().ToTable("BusinessEntityDataChunks");
        modelBuilder.Entity<BusinessEntityDataChunkDto>().HasKey(x => new { x.Id, x.Version });
        modelBuilder.Entity<BusinessEntityDataChunkDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataChunkDto>().Property(x => x.PlainText).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataChunkDto>().Property(x => x.HtmlCache).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataChunkDto>().Property(x => x.Checksum).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataChunkDto>().HasIndex(x => new { x.BusinessEntityId, x.SortOrder, x.Version });
        modelBuilder.Entity<BusinessEntityPropertyDto>().ToTable("BusinessEntityProperties");
        modelBuilder.Entity<BusinessEntityPropertyDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityPropertyDto>().Property(x => x.Metadata).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityPropertyDto>().HasIndex(x => x.ParentEntityId);
        modelBuilder.Entity<BusinessEntityPropertyDto>().HasIndex(x => new { x.ParentEntityId, x.PropertyType });
        modelBuilder.Entity<BusinessEntityDataPropertyDto>().ToTable("BusinessEntityDataProperties");
        modelBuilder.Entity<BusinessEntityDataPropertyDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataPropertyDto>().Property(x => x.Metadata).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataPropertyDto>().HasIndex(x => x.ParentEntityId);
        modelBuilder.Entity<BusinessEntityDataPropertyDto>().HasIndex(x => new { x.ParentEntityId, x.PropertyType });
        modelBuilder.Entity<BusinessEntityDataChunkPropertyDto>().ToTable("BusinessEntityDataChunkProperties");
        modelBuilder.Entity<BusinessEntityDataChunkPropertyDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataChunkPropertyDto>().Property(x => x.Metadata).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityDataChunkPropertyDto>().HasIndex(x => x.ParentEntityId);
        modelBuilder.Entity<BusinessEntityDataChunkPropertyDto>().HasIndex(x => new { x.ParentEntityId, x.PropertyType });
        modelBuilder.Entity<BusinessEntityCommentDto>().ToTable("BusinessEntityComments");
        modelBuilder.Entity<BusinessEntityCommentDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<BusinessEntityCommentDto>().HasIndex(x => new { x.BusinessEntityId, x.CreatedDate });
        modelBuilder.Entity<BusinessEntityCommentDto>().HasIndex(x => x.ParentId);

        modelBuilder.Entity<OperationOutcomeRow>().ToTable("DataProviderOperations");
        modelBuilder.Entity<OperationOutcomeRow>().HasKey(x => x.OperationId);
        modelBuilder.Entity<OperationOutcomeRow>().HasIndex(x => x.CommittedAt);
        modelBuilder.Entity<ChunkReferenceRow>().ToTable("BusinessEntityChunkReferences", table =>
        {
            table.HasCheckConstraint("CK_ChunkReference_Version", "\"Version\" >= 1 AND \"ChunkRowVersion\" >= 1");
            table.HasCheckConstraint("CK_ChunkReference_Position", "\"Position\" >= 0");
        });
        modelBuilder.Entity<ChunkReferenceRow>().HasKey(x => new { x.BusinessEntityId, x.Version, x.ChunkId });
        modelBuilder.Entity<ChunkReferenceRow>().HasIndex(x => new { x.BusinessEntityId, x.Version, x.Position }).IsUnique();
        modelBuilder.Entity<ChunkReferenceRow>().HasOne<BusinessEntityDataDto>().WithMany()
            .HasForeignKey(x => new { x.BusinessEntityId, x.Version }).HasPrincipalKey(x => new { x.Id, x.Version })
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ChunkReferenceRow>().HasOne<BusinessEntityDataChunkDto>().WithMany()
            .HasForeignKey(x => new { x.ChunkId, x.ChunkRowVersion }).HasPrincipalKey(x => new { x.Id, x.Version })
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<EntityFileRow>().ToTable("BusinessEntityFiles");
        modelBuilder.Entity<EntityFileRow>().HasKey(x => new { x.EntityId, x.FileId, x.Variant });
        modelBuilder.Entity<EntityFileRow>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.EntityId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<VersionFileReferenceRow>().ToTable("BusinessEntityVersionFiles");
        modelBuilder.Entity<VersionFileReferenceRow>().HasKey(x => new { x.SourceEntityId, x.Version, x.FileEntityId, x.FileId, x.Variant });
        modelBuilder.Entity<VersionFileReferenceRow>().HasOne<BusinessEntityDataDto>().WithMany()
            .HasForeignKey(x => new { x.SourceEntityId, x.Version }).HasPrincipalKey(x => new { x.Id, x.Version })
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<VersionFileReferenceRow>().HasOne<EntityFileRow>().WithMany()
            .HasForeignKey(x => new { x.FileEntityId, x.FileId, x.Variant }).HasPrincipalKey(x => new { x.EntityId, x.FileId, x.Variant })
            .OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<StorageSchemaRow>().ToTable("DataProviderSchema");
        modelBuilder.Entity<StorageSchemaRow>().HasKey(x => x.Id);
        modelBuilder.Entity<BusinessEntityDataDto>().ToTable("BusinessEntityDataItems", table =>
        {
            table.HasCheckConstraint("CK_Payload_Version", "\"Version\" >= 1");
            table.HasCheckConstraint("CK_Payload_Identity", "\"Id\" = \"BusinessEntityId\"");
        });
        modelBuilder.Entity<BusinessEntityDataDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.BusinessEntityId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BusinessEntityDataChunkDto>().ToTable("BusinessEntityDataChunks", table =>
            table.HasCheckConstraint("CK_Chunk_Version", "\"Version\" >= 1"));
        modelBuilder.Entity<BusinessEntityDataChunkDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.BusinessEntityId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BusinessEntityRelationDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.ObjectAId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BusinessEntityRelationDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.ObjectBId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BusinessEntityCommentDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.BusinessEntityId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BusinessEntityPropertyDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.ParentEntityId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BusinessEntityDataPropertyDto>().HasOne<BusinessEntityDto>().WithMany()
            .HasForeignKey(x => x.ParentEntityId).OnDelete(DeleteBehavior.Cascade);

        // Явно фиксируем имена таблиц, чтобы shared Postgres-база не зависела от EF-конвенций.
    }
}
