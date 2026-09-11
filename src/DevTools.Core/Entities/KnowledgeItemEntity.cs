namespace DevTools.Core.Entities;

public sealed class KnowledgeItemEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Domain { get; set; } // Database, Architecture, API, UI, Security, Pipelines
    public required string Content { get; set; }
    public string Tags { get; set; } = string.Empty; // Comma-separated tags
    public string Source { get; set; } = "Manual"; // Manual, ADR, WebExtraction, CodeReview
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
