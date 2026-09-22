using DevTools.Core.Models;

namespace DevTools.Core.Interfaces;

public interface IProjectScaffoldingService
{
    IReadOnlyList<ScaffoldedFile> BuildProjectFiles(ProjectPlanBlueprint blueprint);

    Task<ProjectScaffoldingResult> GenerateOnDiskAsync(
        ProjectPlanBlueprint blueprint,
        string targetPath,
        CancellationToken ct = default);

    Task<byte[]> GenerateZipArchiveAsync(
        ProjectPlanBlueprint blueprint,
        CancellationToken ct = default);

    Task<string> GenerateArchitectureMarkdownAsync(
        ProjectPlanBlueprint blueprint,
        CancellationToken ct = default);

    Task<string> GenerateAgentsMarkdownAsync(
        ProjectPlanBlueprint blueprint,
        CancellationToken ct = default);

    Task<string> GeneratePrdMarkdownAsync(
        ProjectPlanBlueprint blueprint,
        CancellationToken ct = default);

    Task<byte[]> GenerateSpecKitZipAsync(
        ProjectPlanBlueprint blueprint,
        CancellationToken ct = default);
}
