using DevTools.Core.Models;

namespace DevTools.Core.Interfaces;

public interface IContinuousImprovementService
{
    Task<CodebaseMetrics> GatherMetricsAsync(string solutionOrProjectPath, CancellationToken ct = default);

    Task<ProjectAuditReport> AuditProjectAsync(string solutionOrProjectPath, string? preferredModel = null, CancellationToken ct = default);

    Task<ProjectAuditReport?> GetLatestReportAsync(CancellationToken ct = default);

    Task<bool> UpdateProposalStatusAsync(string proposalId, ProposalStatus newStatus, CancellationToken ct = default);
}
