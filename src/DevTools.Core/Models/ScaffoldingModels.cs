using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public sealed record ScaffoldedFile
{
    [JsonPropertyName("relativePath")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

public sealed record ProjectScaffoldingRequest
{
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; init; }

    [JsonPropertyName("targetPath")]
    public required string TargetPath { get; init; }

    [JsonPropertyName("includeDockerCompose")]
    public bool IncludeDockerCompose { get; init; } = true;

    [JsonPropertyName("includeGitIgnore")]
    public bool IncludeGitIgnore { get; init; } = true;

    [JsonPropertyName("includeTests")]
    public bool IncludeTests { get; init; } = true;
}

public sealed record ProjectScaffoldingResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("outputPath")]
    public required string OutputPath { get; init; }

    [JsonPropertyName("totalFilesCreated")]
    public int TotalFilesCreated { get; init; }

    [JsonPropertyName("generatedFiles")]
    public List<string> GeneratedFiles { get; init; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = [];
}

public sealed record StreamingPlanningChunk
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "token", "action", "done", "error"

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("actionResult")]
    public AgentActionResult? ActionResult { get; init; }

    [JsonPropertyName("suggestions")]
    public List<string>? Suggestions { get; init; }

    [JsonPropertyName("blueprint")]
    public ProjectPlanBlueprint? Blueprint { get; init; }

    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; init; }
}
