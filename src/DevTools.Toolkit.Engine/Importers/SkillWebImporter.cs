using System.Text.Json;
using DevTools.Core.Models;

namespace DevTools.Toolkit.Engine.Importers;

public sealed record SkillImportResult
{
    public required bool Success { get; init; }
    public string? SkillId { get; init; }
    public string? InstalledPath { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class SkillWebImporter
{
    private readonly HttpClient _httpClient;

    public SkillWebImporter(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DevTools-Agent/1.0");
    }

    public async Task<SkillImportResult> ImportFromUrlAsync(
        string toolkitDirectory,
        Uri remoteUrl,
        string? targetCategory = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestPath = Path.Combine(toolkitDirectory, "toolkit.manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new SkillImportResult
                {
                    Success = false,
                    ErrorMessage = $"Manifest not found at '{manifestPath}'."
                };
            }

            // 1. Fetch remote content
            var jsonContent = await _httpClient.GetStringAsync(remoteUrl, cancellationToken);

            // 2. Validate JSON structure
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id", out var idProp) || string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                return new SkillImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid skill definition: missing required 'id' field."
                };
            }

            var skillId = idProp.GetString()!;
            var name = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? skillId : skillId;
            var description = root.TryGetProperty("description", out var dProp) ? dProp.GetString() : null;
            var category = targetCategory ?? (root.TryGetProperty("category", out var cProp) ? cProp.GetString() : "community") ?? "community";

            // 3. Determine installation path
            var categoryDir = Path.Combine(toolkitDirectory, "skills", category, skillId);
            Directory.CreateDirectory(categoryDir);
            var destinationFile = Path.Combine(categoryDir, "skill.json");

            await File.WriteAllTextAsync(destinationFile, jsonContent, cancellationToken);

            // 4. Update toolkit.manifest.json
            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<ToolkitManifest>(manifestJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to read existing manifest.");

            var relativePath = Path.Combine("skills", category, skillId, "skill.json").Replace('\\', '/');

            // Remove existing registration if upgrading/re-importing
            manifest.Skills.RemoveAll(s => string.Equals(s.Id, skillId, StringComparison.OrdinalIgnoreCase));

            manifest.Skills.Add(new SkillMetadata
            {
                Id = skillId,
                Name = name,
                Description = description,
                Path = relativePath
            });

            var updatedManifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(manifestPath, updatedManifestJson, cancellationToken);

            return new SkillImportResult
            {
                Success = true,
                SkillId = skillId,
                InstalledPath = destinationFile
            };
        }
        catch (Exception ex)
        {
            return new SkillImportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
