using Microsoft.EntityFrameworkCore;
using DevTools.Core.Entities;

namespace DevTools.Data;

public sealed class DevToolsDbContext : DbContext
{
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<KnowledgeItemEntity> KnowledgeItems => Set<KnowledgeItemEntity>();
    public DbSet<DocumentationEntity> Documents => Set<DocumentationEntity>();
    public DbSet<AuditRecordEntity> AuditRecords => Set<AuditRecordEntity>();
    public DbSet<PlanningMessageEntity> PlanningMessages => Set<PlanningMessageEntity>();

    public DevToolsDbContext(DbContextOptions<DevToolsDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Projects
        modelBuilder.Entity<ProjectEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RootPath).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.RootPath).IsRequired().HasMaxLength(1024);

            entity.HasMany(e => e.Audits)
                  .WithOne(e => e.Project)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Documents)
                  .WithOne(e => e.Project)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Messages)
                  .WithOne(e => e.Project)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Planning Messages
        modelBuilder.Entity<PlanningMessageEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ProjectId);
            entity.Property(e => e.Role).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Content).IsRequired();
        });

        // Knowledge Items
        modelBuilder.Entity<KnowledgeItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Domain);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Domain).IsRequired().HasMaxLength(128);
            entity.Property(e => e.Content).IsRequired();
        });

        // Documentation
        modelBuilder.Entity<DocumentationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DocType);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(256);
            entity.Property(e => e.DocType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.MarkdownContent).IsRequired();
        });

        // Audit Records
        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.TargetPath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.Verdict).IsRequired().HasMaxLength(64);
        });
    }
}
