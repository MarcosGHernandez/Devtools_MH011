namespace DevTools.Core.Entities;

public sealed class DocumentationEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ProjectId { get; set; }
    public ProjectEntity? Project { get; set; }

    public required string Title { get; set; }
    public required string DocType { get; set; } // ADR, C4Model, ApiSpec, Guide
    public required string MarkdownContent { get; set; }
    public string? Version { get; set; } = "1.0.0";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
