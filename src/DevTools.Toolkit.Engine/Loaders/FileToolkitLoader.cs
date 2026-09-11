using System.Text.Json;
using DevTools.Core.Interfaces;
using DevTools.Core.Models;
using DevTools.Toolkit.Engine.Parsers;

namespace DevTools.Toolkit.Engine.Loaders;

public sealed class FileToolkitLoader : IToolkitLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ToolkitManifest> LoadManifestAsync(string toolkitDirectory, CancellationToken cancellationToken = default)
    {
        var manifestPath = Path.Combine(toolkitDirectory, "toolkit.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Toolkit manifest not found at: '{manifestPath}'");
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ToolkitManifest>(json, JsonOptions);

        return manifest ?? throw new InvalidOperationException($"Failed to deserialize manifest at '{manifestPath}'");
    }

    public async Task<PromptDefinition> LoadPromptAsync(string toolkitDirectory, string promptId, CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(toolkitDirectory, cancellationToken);
        var meta = manifest.Prompts.FirstOrDefault(p => string.Equals(p.Id, promptId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Prompt with ID '{promptId}' was not found in manifest.");

        return await LoadPromptInternalAsync(toolkitDirectory, meta, cancellationToken);
    }

    public async Task<IReadOnlyList<PromptDefinition>> LoadAllPromptsAsync(string toolkitDirectory, CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(toolkitDirectory, cancellationToken);
        var list = new List<PromptDefinition>(manifest.Prompts.Count);

        foreach (var meta in manifest.Prompts)
        {
            var prompt = await LoadPromptInternalAsync(toolkitDirectory, meta, cancellationToken);
            list.Add(prompt);
        }

        return list;
    }

    public async Task<SkillDefinition> LoadSkillAsync(string toolkitDirectory, string skillId, CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(toolkitDirectory, cancellationToken);
        var meta = manifest.Skills.FirstOrDefault(s => string.Equals(s.Id, skillId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Skill with ID '{skillId}' was not found in manifest.");

        return await LoadSkillInternalAsync(toolkitDirectory, meta, cancellationToken);
    }

    public async Task<IReadOnlyList<SkillDefinition>> LoadAllSkillsAsync(string toolkitDirectory, CancellationToken cancellationToken = default)
    {
        var manifest = await LoadManifestAsync(toolkitDirectory, cancellationToken);
        var list = new List<SkillDefinition>(manifest.Skills.Count);

        foreach (var meta in manifest.Skills)
        {
            var skill = await LoadSkillInternalAsync(toolkitDirectory, meta, cancellationToken);
            list.Add(skill);
        }

        return list;
    }

    private static async Task<PromptDefinition> LoadPromptInternalAsync(string root, PromptMetadata meta, CancellationToken ct)
    {
        var fullPath = Path.Combine(root, meta.Path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Prompt file '{meta.Path}' not found at '{fullPath}'");
        }

        var raw = await File.ReadAllTextAsync(fullPath, ct);
        var (frontmatter, body) = FrontmatterParser.Parse(raw);

        string? rules = null;
        if (!string.IsNullOrWhiteSpace(meta.RulesPath))
        {
            var rulesFull = Path.Combine(root, meta.RulesPath);
            if (File.Exists(rulesFull))
            {
                rules = await File.ReadAllTextAsync(rulesFull, ct);
            }
        }

        string? schema = null;
        if (!string.IsNullOrWhiteSpace(meta.OutputSchema))
        {
            var schemaFull = Path.Combine(root, meta.OutputSchema);
            if (File.Exists(schemaFull))
            {
                schema = await File.ReadAllTextAsync(schemaFull, ct);
            }
        }

        return new PromptDefinition
        {
            Id = meta.Id,
            Name = meta.Name,
            Description = meta.Description,
            FilePath = fullPath,
            RawContent = raw,
            Body = body,
            Frontmatter = frontmatter,
            RulesContent = rules,
            OutputSchemaContent = schema
        };
    }

    private static async Task<SkillDefinition> LoadSkillInternalAsync(string root, SkillMetadata meta, CancellationToken ct)
    {
        var fullPath = Path.Combine(root, meta.Path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Skill specification file '{meta.Path}' not found at '{fullPath}'");
        }

        var raw = await File.ReadAllTextAsync(fullPath, ct);

        return new SkillDefinition
        {
            Id = meta.Id,
            Name = meta.Name,
            Description = meta.Description,
            FilePath = fullPath,
            RawJson = raw
        };
    }
}
