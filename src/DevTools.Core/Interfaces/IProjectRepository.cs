using DevTools.Core.Entities;

namespace DevTools.Core.Interfaces;

public interface IProjectRepository
{
    Task<ProjectEntity> RegisterProjectAsync(string name, string rootPath, string? gitRepo = null, CancellationToken cancellationToken = default);
    Task<ProjectEntity?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectEntity?> GetProjectByPathAsync(string rootPath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectEntity>> ListProjectsAsync(CancellationToken cancellationToken = default);
    Task RecordAuditAsync(Guid projectId, string targetPath, int score, string verdict, int critical, int high, int medium, int low, string reportJson, CancellationToken cancellationToken = default);

    Task<PlanningMessageEntity> AddMessageAsync(Guid projectId, string role, string content, string? suggestedQuestionsJson = null, string? blueprintJson = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanningMessageEntity>> GetMessagesAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task UpdateProjectMetadataAsync(Guid projectId, string? description, string? architecturalStyle, string? frontendStack, string? databaseType, string? latestBlueprintJson, CancellationToken cancellationToken = default);
    Task<bool> DeleteProjectAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectEntity?> UpdateProjectDetailsAsync(Guid id, string name, string? rootPath, string? description, CancellationToken cancellationToken = default);
    Task<bool> ClearProjectMessagesAsync(Guid projectId, CancellationToken cancellationToken = default);
}
