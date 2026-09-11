using DevTools.Core.Entities;

namespace DevTools.Core.Interfaces;

public interface IKnowledgeRepository
{
    Task<KnowledgeItemEntity> AddKnowledgeAsync(string title, string domain, string content, string tags = "", string source = "Manual", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeItemEntity>> ListKnowledgeAsync(string? domain = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeItemEntity>> SearchKnowledgeAsync(string query, CancellationToken cancellationToken = default);
    Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken = default);
}
