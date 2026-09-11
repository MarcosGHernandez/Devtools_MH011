namespace DevTools.Core.Entities;

public sealed class AuditRecordEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public ProjectEntity? Project { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string TargetPath { get; set; }
    public int Score { get; set; }
    public required string Verdict { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public required string ReportJson { get; set; }
}
