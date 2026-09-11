using Microsoft.EntityFrameworkCore;
using DevTools.Core.Entities;
using DevTools.Core.Interfaces;

namespace DevTools.Data.Repositories;

public sealed class SqliteDocumentationRepository : IDocumentationRepository
{
    private readonly DevToolsDbContext _dbContext;

    public SqliteDocumentationRepository(DevToolsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DocumentationEntity> SaveDocumentAsync(
        string title,
        string docType,
        string markdownContent,
        Guid? projectId = null,
        string version = "1.0.0",
        CancellationToken cancellationToken = default)
    {
        var doc = new DocumentationEntity
        {
            Title = title,
            DocType = docType,
            MarkdownContent = markdownContent,
            ProjectId = projectId,
            Version = version,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Documents.Add(doc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return doc;
    }

    public async Task<IReadOnlyList<DocumentationEntity>> ListDocumentsAsync(
        Guid? projectId = null,
        string? docType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Documents.AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(d => d.ProjectId == projectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(docType))
        {
            query = query.Where(d => d.DocType.ToLower() == docType.ToLower());
        }

        return await query.OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<DocumentationEntity?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Documents
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var doc = await _dbContext.Documents.FindAsync([id], cancellationToken);
        if (doc is null) return false;

        _dbContext.Documents.Remove(doc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DocumentationEntity?> UpdateDocumentAsync(Guid id, string title, string markdownContent, string version = "1.0.0", CancellationToken cancellationToken = default)
    {
        var doc = await _dbContext.Documents.FindAsync([id], cancellationToken);
        if (doc is null) return null;

        if (!string.IsNullOrWhiteSpace(title)) doc.Title = title.Trim();
        if (markdownContent is not null) doc.MarkdownContent = markdownContent;
        if (!string.IsNullOrWhiteSpace(version)) doc.Version = version.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return doc;
    }
}
