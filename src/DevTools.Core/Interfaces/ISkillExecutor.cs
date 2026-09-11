using DevTools.Core.Models;

namespace DevTools.Core.Interfaces;

public sealed record SkillExecutionRequest
{
    public required string SkillId { get; init; }
    public Dictionary<string, object?> Parameters { get; init; } = [];
}

public sealed record SkillExecutionResult
{
    public required string SkillId { get; init; }
    public required bool Success { get; init; }
    public string? OutputJson { get; init; }
    public string? Error { get; init; }
}

public interface ISkillExecutor
{
    string SupportedSkillId { get; }
    Task<SkillExecutionResult> ExecuteAsync(SkillExecutionRequest request, CancellationToken cancellationToken = default);
}
