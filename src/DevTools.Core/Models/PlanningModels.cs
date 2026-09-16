using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public sealed record ProjectInterviewAnswers
{
    [JsonPropertyName("projectName")]
    public required string ProjectName { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("targetUsers")]
    public string TargetUsers { get; init; } = "General Developers / Users";

    [JsonPropertyName("architecturalStyle")]
    public string ArchitecturalStyle { get; init; } = "Clean Architecture"; // Modular Monolith, Clean Architecture, Vertical Slices, Event-Driven

    [JsonPropertyName("frontendStack")]
    public string FrontendStack { get; init; } = "React + Minimalist Design System"; // React, Blazor, Next.js, Svelte, Vue, CLI

    [JsonPropertyName("databaseType")]
    public string DatabaseType { get; init; } = "PostgreSQL + EF Core 9"; // PostgreSQL, SQLite, SQL Server, MongoDB

    [JsonPropertyName("expectedLoad")]
    public string ExpectedLoad { get; init; } = "Medium (<50,000 daily users)";

    [JsonPropertyName("specialConstraints")]
    public string SpecialConstraints { get; init; } = "Classic minimalist UI, high maintainability, strict automated testing.";
}

public sealed record FrontendDesignSpec
{
    [JsonPropertyName("themeName")]
    public string ThemeName { get; init; } = "Classic Minimalist Slate";

    [JsonPropertyName("primarySansFont")]
    public string PrimarySansFont { get; init; } = "'Inter', -apple-system, BlinkMacSystemFont, sans-serif";

    [JsonPropertyName("primaryMonoFont")]
    public string PrimaryMonoFont { get; init; } = "'JetBrains Mono', monospace";

    [JsonPropertyName("colorPalette")]
    public Dictionary<string, string> ColorPalette { get; init; } = [];

    [JsonPropertyName("componentRules")]
    public List<string> ComponentRules { get; init; } = [];

    [JsonPropertyName("tokensCss")]
    public string TokensCss { get; init; } = string.Empty;
}

public sealed record ProjectPlanBlueprint
{
    [JsonPropertyName("projectName")]
    public required string ProjectName { get; init; }

    [JsonPropertyName("executiveSummary")]
    public required string ExecutiveSummary { get; init; }

    [JsonPropertyName("architecturalRationale")]
    public required string ArchitecturalRationale { get; init; }

    [JsonPropertyName("techStack")]
    public Dictionary<string, string> TechStack { get; init; } = [];

    [JsonPropertyName("directoryStructure")]
    public required string DirectoryStructure { get; init; }

    [JsonPropertyName("c4DiagramMermaid")]
    public required string C4DiagramMermaid { get; init; }

    [JsonPropertyName("initialAdrTitle")]
    public required string InitialAdrTitle { get; init; }

    [JsonPropertyName("initialAdrContent")]
    public required string InitialAdrContent { get; init; }

    [JsonPropertyName("keyConventions")]
    public List<string> KeyConventions { get; init; } = [];

    [JsonPropertyName("frontendDesignSpec")]
    public FrontendDesignSpec? FrontendDesignSpec { get; init; }
}

public sealed record PlanningChatMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; } // "user", "assistant", "system"

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("suggestedActions")]
    public List<string> SuggestedActions { get; init; } = [];

    [JsonPropertyName("blueprintJson")]
    public string? BlueprintJson { get; init; }
}

public sealed record AttachedDocumentModel
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("fileType")]
    public string? FileType { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

public sealed record PlanningChatRequest
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("projectName")]
    public string? ProjectName { get; init; }

    [JsonPropertyName("userMessage")]
    public required string UserMessage { get; init; }

    [JsonPropertyName("history")]
    public List<PlanningChatMessage>? History { get; init; }

    [JsonPropertyName("currentAnswers")]
    public ProjectInterviewAnswers? CurrentAnswers { get; init; }

    [JsonPropertyName("attachedDocuments")]
    public List<AttachedDocumentModel>? AttachedDocuments { get; init; }
}

public sealed record PlanningChatResponse
{
    [JsonPropertyName("sessionId")]
    public required string SessionId { get; init; }

    [JsonPropertyName("projectId")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("assistantReply")]
    public required string AssistantReply { get; init; }

    [JsonPropertyName("suggestedQuestions")]
    public List<string> SuggestedQuestions { get; init; } = [];

    [JsonPropertyName("updatedAnswers")]
    public required ProjectInterviewAnswers UpdatedAnswers { get; init; }

    [JsonPropertyName("generatedBlueprint")]
    public ProjectPlanBlueprint? GeneratedBlueprint { get; init; }

    [JsonPropertyName("readyToScaffold")]
    public bool ReadyToScaffold { get; init; }

    [JsonPropertyName("executedAction")]
    public AgentActionResult? ExecutedAction { get; init; }
}

public sealed record ProjectSummaryDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("rootPath")]
    public required string RootPath { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("architecturalStyle")]
    public string? ArchitecturalStyle { get; init; }

    [JsonPropertyName("frontendStack")]
    public string? FrontendStack { get; init; }

    [JsonPropertyName("databaseType")]
    public string? DatabaseType { get; init; }

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; init; }

    [JsonPropertyName("latestBlueprint")]
    public ProjectPlanBlueprint? LatestBlueprint { get; init; }
}
