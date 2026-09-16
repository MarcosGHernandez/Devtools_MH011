using System.Text.Json.Serialization;

namespace DevTools.Core.Configuration;

public sealed record DevToolsConfig
{
    [JsonPropertyName("toolkit")]
    public ToolkitConfig Toolkit { get; init; } = new();

    [JsonPropertyName("ai")]
    public AiConfig Ai { get; init; } = new();

    [JsonPropertyName("ui")]
    public UiConfig Ui { get; init; } = new();

    public DevToolsConfig Normalize(string? baseDirectory = null)
    {
        var resolvedToolkitPath = Common.SolutionPathResolver.NormalizePath(Toolkit.Path, baseDirectory);
        return this with
        {
            Toolkit = Toolkit with
            {
                Path = resolvedToolkitPath
            }
        };
    }
}

public sealed record ToolkitConfig
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = "./toolkit";

    [JsonPropertyName("autoReload")]
    public bool AutoReload { get; init; } = true;
}

public sealed record AiConfig
{
    [JsonPropertyName("defaultProvider")]
    public string DefaultProvider { get; init; } = "offline"; // "local" (Ollama), "openai", "offline"

    [JsonPropertyName("providers")]
    public Dictionary<string, AiProviderSettings> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new AiProviderSettings
        {
            ApiKeyEnvironmentVariable = "OPENAI_API_KEY",
            ModelId = "gpt-4o"
        },
        ["ollama"] = new AiProviderSettings
        {
            Endpoint = "http://localhost:11434/v1",
            ModelId = "hermes3:8b"
        }
    };
}

public sealed record AiProviderSettings
{
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    [JsonPropertyName("apiKeyEnvironmentVariable")]
    public string? ApiKeyEnvironmentVariable { get; init; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }

    [JsonPropertyName("modelId")]
    public string ModelId { get; init; } = "default";
}

public sealed record UiConfig
{
    [JsonPropertyName("theme")]
    public string Theme { get; init; } = "classic-minimalist";

    [JsonPropertyName("tableBorder")]
    public string TableBorder { get; init; } = "rounded";

    [JsonPropertyName("compact")]
    public bool Compact { get; init; } = true;
}
