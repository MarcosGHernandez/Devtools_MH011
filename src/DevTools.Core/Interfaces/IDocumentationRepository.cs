using DevTools.Core.Entities;

namespace DevTools.Core.Interfaces;

public interface IDocumentationRepository
{
    Task<DocumentationEntity> SaveDocumentAsync(string title, string docType, string markdownContent, Guid? projectId = null, string version = "1.0.0", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentationEntity>> ListDocumentsAsync(Guid? projectId = null, string? docType = null, CancellationToken cancellationToken = default);
    Task<DocumentationEntity?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DocumentationEntity?> UpdateDocumentAsync(Guid id, string title, string markdownContent, string version = "1.0.0", CancellationToken cancellationToken = default);
}
