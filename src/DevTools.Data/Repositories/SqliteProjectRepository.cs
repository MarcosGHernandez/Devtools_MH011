using Microsoft.EntityFrameworkCore;
using DevTools.Core.Entities;
using DevTools.Core.Interfaces;

namespace DevTools.Data.Repositories;

public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly DevToolsDbContext _dbContext;

    public SqliteProjectRepository(DevToolsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ProjectEntity> RegisterProjectAsync(string name, string rootPath, string? gitRepo = null, CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(rootPath);
        var existing = await _dbContext.Projects.FirstOrDefaultAsync(p => p.RootPath == normalizedPath, cancellationToken);
        if (existing is not null)
        {
            existing.Name = name;
            existing.GitRepository = gitRepo ?? existing.GitRepository;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var project = new ProjectEntity
        {
            Name = name,
            RootPath = normalizedPath,
            GitRepository = gitRepo,
            CreatedAt = DateTime.UtcNow,
            LastAuditedAt = DateTime.UtcNow
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<ProjectEntity?> GetProjectByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Projects
            .Include(p => p.Audits)
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<ProjectEntity?> GetProjectByPathAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFullPath(rootPath);
        return await _dbContext.Projects
            .Include(p => p.Audits)
            .Include(p => p.Documents)
            .FirstOrDefaultAsync(p => p.RootPath == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectEntity>> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Projects
            .OrderByDescending(p => p.LastAuditedAt ?? p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAuditAsync(
        Guid projectId,
        string targetPath,
        int score,
        string verdict,
        int critical,
        int high,
        int medium,
        int low,
        string reportJson,
        CancellationToken cancellationToken = default)
    {
        var project = await _dbContext.Projects.FindAsync([projectId], cancellationToken);
        if (project is not null)
        {
            project.LastQualityScore = score;
            project.LastAuditedAt = DateTime.UtcNow;
        }

        var audit = new AuditRecordEntity
        {
            ProjectId = projectId,
            TargetPath = targetPath,
            Score = score,
            Verdict = verdict,
            CriticalCount = critical,
            HighCount = high,
            MediumCount = medium,
            LowCount = low,
            ReportJson = reportJson
        };

        _dbContext.AuditRecords.Add(audit);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PlanningMessageEntity> AddMessageAsync(
        Guid projectId,
        string role,
        string content,
        string? suggestedQuestionsJson = null,
        string? blueprintJson = null,
        CancellationToken cancellationToken = default)
    {
        var msg = new PlanningMessageEntity
        {
            ProjectId = projectId,
            Role = role,
            Content = content,
            SuggestedQuestionsJson = suggestedQuestionsJson,
            BlueprintJson = blueprintJson,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PlanningMessages.Add(msg);

        var project = await _dbContext.Projects.FindAsync([projectId], cancellationToken);
        if (project is not null)
        {
            project.LastAuditedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return msg;
    }

    public async Task<IReadOnlyList<PlanningMessageEntity>> GetMessagesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.PlanningMessages
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateProjectMetadataAsync(
        Guid projectId,
        string? description,
        string? architecturalStyle,
        string? frontendStack,
        string? databaseType,
        string? latestBlueprintJson,
        CancellationToken cancellationToken = default)
    {
        var project = await _dbContext.Projects.FindAsync([projectId], cancellationToken);
        if (project is not null)
        {
            if (description is not null) project.Description = description;
            if (architecturalStyle is not null) project.ArchitecturalStyle = architecturalStyle;
            if (frontendStack is not null) project.FrontendStack = frontendStack;
            if (databaseType is not null) project.DatabaseType = databaseType;
            if (latestBlueprintJson is not null) project.LatestBlueprintJson = latestBlueprintJson;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> DeleteProjectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _dbContext.Projects.FindAsync([id], cancellationToken);
        if (project is null) return false;

        var messages = await _dbContext.PlanningMessages.Where(m => m.ProjectId == id).ToListAsync(cancellationToken);
        if (messages.Count > 0) _dbContext.PlanningMessages.RemoveRange(messages);

        var audits = await _dbContext.AuditRecords.Where(a => a.ProjectId == id).ToListAsync(cancellationToken);
        if (audits.Count > 0) _dbContext.AuditRecords.RemoveRange(audits);

        var docs = await _dbContext.Documents.Where(d => d.ProjectId == id).ToListAsync(cancellationToken);
        if (docs.Count > 0) _dbContext.Documents.RemoveRange(docs);

        _dbContext.Projects.Remove(project);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ProjectEntity?> UpdateProjectDetailsAsync(Guid id, string name, string? rootPath, string? description, CancellationToken cancellationToken = default)
    {
        var project = await _dbContext.Projects.FindAsync([id], cancellationToken);
        if (project is null) return null;

        if (!string.IsNullOrWhiteSpace(name)) project.Name = name.Trim();
        if (!string.IsNullOrWhiteSpace(rootPath)) project.RootPath = Path.GetFullPath(rootPath.Trim());
        if (description is not null) project.Description = description;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<bool> ClearProjectMessagesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var messages = await _dbContext.PlanningMessages.Where(m => m.ProjectId == projectId).ToListAsync(cancellationToken);
        if (messages.Count > 0)
        {
            _dbContext.PlanningMessages.RemoveRange(messages);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return true;
    }
}
