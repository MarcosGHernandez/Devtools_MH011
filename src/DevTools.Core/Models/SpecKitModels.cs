using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public sealed record SpecKitSpec
{
    [JsonPropertyName("constitutionMarkdown")]
    public string ConstitutionMarkdown { get; init; } = string.Empty;

    [JsonPropertyName("specMarkdown")]
    public string SpecMarkdown { get; init; } = string.Empty;

    [JsonPropertyName("planMarkdown")]
    public string PlanMarkdown { get; init; } = string.Empty;

    [JsonPropertyName("tasksMarkdown")]
    public string TasksMarkdown { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record SpecKitFile
{
    [JsonPropertyName("relativePath")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}
