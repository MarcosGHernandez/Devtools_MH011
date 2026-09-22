using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public sealed record ArchitectureSuggestion
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("category")]
    public string Category { get; init; } = "Architecture"; // Architecture, Reliability, Security, Performance, Maintainability

    [JsonPropertyName("impact")]
    public string Impact { get; init; } = "Medium"; // Low, Medium, High, Critical

    [JsonPropertyName("rationale")]
    public required string Rationale { get; init; }

    [JsonPropertyName("implementationGuidance")]
    public required string ImplementationGuidance { get; init; }
}

public sealed record DocumentationSuiteExport
{
    [JsonPropertyName("projectName")]
    public required string ProjectName { get; init; }

    [JsonPropertyName("prdMarkdown")]
    public string? PrdMarkdown { get; init; }

    [JsonPropertyName("specKit")]
    public SpecKitSpec? SpecKit { get; init; }

    [JsonPropertyName("agentsMarkdown")]
    public string? AgentsMarkdown { get; init; }

    [JsonPropertyName("architectureMarkdown")]
    public string? ArchitectureMarkdown { get; init; }

    [JsonPropertyName("suggestionsMarkdown")]
    public string? SuggestionsMarkdown { get; init; }

    [JsonPropertyName("adrs")]
    public Dictionary<string, string> Adrs { get; init; } = [];
}
