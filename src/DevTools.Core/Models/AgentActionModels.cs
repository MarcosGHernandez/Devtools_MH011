using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public enum AgentActionType
{
    None = 0,
    UpdateProject,
    DeleteProject,
    ClearChat,
    UpdateTechStack,
    AddConvention,
    RemoveConvention,
    UpdateC4Diagram,
    UpdateDirectoryStructure,
    CreateAdr,
    UpdateAdr,
    DeleteAdr,
    AddKnowledgeRule,
    DeleteKnowledgeRule,
    ScaffoldProject,
    ExportProjectZip,
    ExportDocumentation,
    ExportAgentsMarkdown,
    CreatePrd,
    UpdatePrd,
    CreateSpecKit,
    UpdateSpecKit,
    CreateDocument,
    UpdateDocument,
    DeleteDocument,
    ReadDocument
}

public sealed record AgentActionResult
{
    [JsonPropertyName("actionType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentActionType ActionType { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("affectedEntityId")]
    public string? AffectedEntityId { get; init; }

    [JsonPropertyName("affectedEntityName")]
    public string? AffectedEntityName { get; init; }

    [JsonPropertyName("isProjectDeleted")]
    public bool IsProjectDeleted { get; init; }

    [JsonPropertyName("targetDirectory")]
    public string? TargetDirectory { get; init; }

    [JsonPropertyName("filesCreatedCount")]
    public int FilesCreatedCount { get; init; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonPropertyName("updatedBlueprint")]
    public ProjectPlanBlueprint? UpdatedBlueprint { get; init; }
}
