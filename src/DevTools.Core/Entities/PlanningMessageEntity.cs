namespace DevTools.Core.Entities;

public sealed class PlanningMessageEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public ProjectEntity? Project { get; set; }

    public string Role { get; set; } = "user"; // "user", "assistant", "system"
    public required string Content { get; set; }
    public string? SuggestedQuestionsJson { get; set; }
    public string? BlueprintJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
