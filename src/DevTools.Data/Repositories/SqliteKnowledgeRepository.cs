using Microsoft.EntityFrameworkCore;
using DevTools.Core.Entities;
using DevTools.Core.Interfaces;

namespace DevTools.Data.Repositories;

public sealed class SqliteKnowledgeRepository : IKnowledgeRepository
{
    private readonly DevToolsDbContext _dbContext;

    public SqliteKnowledgeRepository(DevToolsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<KnowledgeItemEntity> AddKnowledgeAsync(
        string title,
        string domain,
        string content,
        string tags = "",
        string source = "Manual",
        CancellationToken cancellationToken = default)
    {
        var item = new KnowledgeItemEntity
        {
            Title = title,
            Domain = domain,
            Content = content,
            Tags = tags,
            Source = source,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.KnowledgeItems.Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyList<KnowledgeItemEntity>> ListKnowledgeAsync(string? domain = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.KnowledgeItems.AsQueryable();
        if (!string.IsNullOrWhiteSpace(domain))
        {
            query = query.Where(k => k.Domain.ToLower() == domain.ToLower());
        }

        return await query.OrderByDescending(k => k.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeItemEntity>> SearchKnowledgeAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await ListKnowledgeAsync(null, cancellationToken);
        }

        var term = query.ToLower();
        return await _dbContext.KnowledgeItems
            .Where(k => k.Title.ToLower().Contains(term) ||
                        k.Content.ToLower().Contains(term) ||
                        k.Tags.ToLower().Contains(term) ||
                        k.Domain.ToLower().Contains(term))
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _dbContext.KnowledgeItems.FindAsync([id], cancellationToken);
        if (item is null) return false;

        _dbContext.KnowledgeItems.Remove(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
