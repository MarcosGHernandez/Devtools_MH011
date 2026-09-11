using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public sealed record ToolkitManifest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("compatibility")]
    public ToolkitCompatibility? Compatibility { get; init; }

    [JsonPropertyName("prompts")]
    public List<PromptMetadata> Prompts { get; init; } = [];

    [JsonPropertyName("skills")]
    public List<SkillMetadata> Skills { get; init; } = [];
}

public sealed record ToolkitCompatibility
{
    [JsonPropertyName("engineVersion")]
    public string? EngineVersion { get; init; }

    [JsonPropertyName("dotnetVersion")]
    public string? DotnetVersion { get; init; }
}

public sealed record PromptMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("rulesPath")]
    public string? RulesPath { get; init; }

    [JsonPropertyName("outputSchema")]
    public string? OutputSchema { get; init; }
}

public sealed record SkillMetadata
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }
}

public sealed record PromptDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string FilePath { get; init; }
    public required string RawContent { get; init; }
    public required string Body { get; init; }
    public Dictionary<string, string> Frontmatter { get; init; } = [];
    public string? RulesContent { get; init; }
    public string? OutputSchemaContent { get; init; }
}

public sealed record SkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string FilePath { get; init; }
    public required string RawJson { get; init; }
}
