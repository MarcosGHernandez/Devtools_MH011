using DevTools.Core.Models;

namespace DevTools.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default);
}
