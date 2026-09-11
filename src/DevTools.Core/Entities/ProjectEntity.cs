namespace DevTools.Core.Entities;

public sealed class ProjectEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string RootPath { get; set; }
    public string? Description { get; set; }
    public string? ArchitecturalStyle { get; set; }
    public string? FrontendStack { get; set; }
    public string? DatabaseType { get; set; }
    public string? LatestBlueprintJson { get; set; }

    public string? GitRepository { get; set; }
    public string? DefaultBranch { get; set; } = "main";
    public int LastQualityScore { get; set; } = 100;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAuditedAt { get; set; }

    public List<AuditRecordEntity> Audits { get; set; } = [];
    public List<DocumentationEntity> Documents { get; set; } = [];
    public List<PlanningMessageEntity> Messages { get; set; } = [];
}
