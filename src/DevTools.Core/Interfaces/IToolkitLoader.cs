using DevTools.Core.Models;

namespace DevTools.Core.Interfaces;

public interface IToolkitLoader
{
    Task<ToolkitManifest> LoadManifestAsync(string toolkitDirectory, CancellationToken cancellationToken = default);
    Task<PromptDefinition> LoadPromptAsync(string toolkitDirectory, string promptId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromptDefinition>> LoadAllPromptsAsync(string toolkitDirectory, CancellationToken cancellationToken = default);
    Task<SkillDefinition> LoadSkillAsync(string toolkitDirectory, string skillId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SkillDefinition>> LoadAllSkillsAsync(string toolkitDirectory, CancellationToken cancellationToken = default);
}
