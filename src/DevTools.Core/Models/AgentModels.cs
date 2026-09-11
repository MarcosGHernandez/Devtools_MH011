namespace DevTools.Core.Models;

public sealed record AgentRequest
{
    public required string AgentId { get; init; }
    public required string PromptId { get; init; }
    public string? TargetFilePath { get; init; }
    public string? CodeContent { get; init; }
    public Dictionary<string, string> Arguments { get; init; } = [];
    public List<string> EnabledSkillIds { get; init; } = [];
}

public sealed record AgentResponse
{
    public required string AgentId { get; init; }
    public required bool Success { get; init; }
    public string? RawOutput { get; init; }
    public CodeReviewReport? ReviewReport { get; init; }
    public List<string> AppliedSkills { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}
