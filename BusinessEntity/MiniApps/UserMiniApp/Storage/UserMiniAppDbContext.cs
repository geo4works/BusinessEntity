using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BusinessEntity.MiniApps.UserMiniApp.Storage;

// Собственный DbContext user mini-app. Использует ту же физическую БД, что и остальные mini-app.
public sealed class UserMiniAppDbContext : DbContext
{
    public UserMiniAppDbContext(DbContextOptions<UserMiniAppDbContext> options) : base(options)
    {
    }

    public DbSet<UserDto> Users => Set<UserDto>();
    public DbSet<UserPropertyDto> UserProperties => Set<UserPropertyDto>();
    public DbSet<UserRoleDto> UserRoles => Set<UserRoleDto>();
    public DbSet<UserGroupDto> UserGroups => Set<UserGroupDto>();
    public DbSet<UserGroupMemberDto> UserGroupMembers => Set<UserGroupMemberDto>();
    public DbSet<UserRoleAssignmentDto> UserRoleAssignments => Set<UserRoleAssignmentDto>();

    internal DbSet<UserStorageVersionRow> SchemaMetadata => Set<UserStorageVersionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserStorageVersionRow>().ToTable("UserMiniAppSchema");
        modelBuilder.Entity<UserStorageVersionRow>().HasKey(x => x.Id);
        modelBuilder.Entity<UserDto>().ToTable("Users");
        modelBuilder.Entity<UserDto>().HasKey(x => x.Id);
        modelBuilder.Entity<UserDto>().Property(x => x.ExternalId).HasColumnType("text");
        modelBuilder.Entity<UserDto>().Property(x => x.Payload).HasColumnType("text");
        modelBuilder.Entity<UserDto>().HasIndex(x => x.ExternalId).IsUnique();

        modelBuilder.Entity<UserPropertyDto>().ToTable("UserProperties");
        modelBuilder.Entity<UserPropertyDto>().HasKey(x => x.Id);
        modelBuilder.Entity<UserPropertyDto>().Property(x => x.Data).HasColumnType("text");
        modelBuilder.Entity<UserPropertyDto>().Property(x => x.Metadata).HasColumnType("text");
        modelBuilder.Entity<UserPropertyDto>().HasIndex(x => x.ParentEntityId);
        modelBuilder.Entity<UserPropertyDto>().HasIndex(x => new { x.ParentEntityId, x.PropertyType });

        modelBuilder.Entity<UserRoleDto>().ToTable("UserRoles");
        modelBuilder.Entity<UserRoleDto>().HasKey(x => x.Id);
        modelBuilder.Entity<UserRoleDto>().Property(x => x.Name).HasColumnType("text");
        modelBuilder.Entity<UserRoleDto>().Property(x => x.Permissions).HasColumnType("text");
        modelBuilder.Entity<UserRoleDto>().HasIndex(x => x.Name).IsUnique();

        modelBuilder.Entity<UserGroupDto>().ToTable("UserGroups");
        modelBuilder.Entity<UserGroupDto>().HasKey(x => x.Id);
        modelBuilder.Entity<UserGroupDto>().Property(x => x.Name).HasColumnType("text");
        modelBuilder.Entity<UserGroupDto>().HasIndex(x => x.Name).IsUnique();

        modelBuilder.Entity<UserGroupMemberDto>().ToTable("UserGroupMembers");
        modelBuilder.Entity<UserGroupMemberDto>().HasKey(x => x.Id);
        modelBuilder.Entity<UserGroupMemberDto>().HasIndex(x => x.GroupId);
        modelBuilder.Entity<UserGroupMemberDto>().HasIndex(x => x.UserId);
        modelBuilder.Entity<UserGroupMemberDto>().HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();

        modelBuilder.Entity<UserRoleAssignmentDto>().ToTable("UserRoleAssignments");
        modelBuilder.Entity<UserRoleAssignmentDto>().HasKey(x => x.Id);
        modelBuilder.Entity<UserRoleAssignmentDto>().Property(x => x.Subject).HasColumnType("text");
        modelBuilder.Entity<UserRoleAssignmentDto>().Property(x => x.AssignmentType).HasColumnType("text");
        modelBuilder.Entity<UserRoleAssignmentDto>().HasIndex(x => x.SpaceId);
        modelBuilder.Entity<UserRoleAssignmentDto>().HasIndex(x => x.Subject);
        modelBuilder.Entity<UserRoleAssignmentDto>().HasIndex(x => x.SubjectId);
        modelBuilder.Entity<UserRoleAssignmentDto>().HasIndex(x => x.RoleId);
        modelBuilder.Entity<UserRoleAssignmentDto>()
            .HasIndex(x => new { x.SpaceId, x.Subject, x.SubjectId, x.AssignmentType, x.RoleId })
            .IsUnique();
    }
}
