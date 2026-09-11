using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

public enum ReviewSeverity
{
    CRITICAL,
    HIGH,
    MEDIUM,
    LOW
}

public sealed record ReviewIssue
{
    [JsonPropertyName("ruleId")]
    public required string RuleId { get; init; }

    [JsonPropertyName("severity")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReviewSeverity Severity { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("line")]
    public int? Line { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("recommendation")]
    public required string Recommendation { get; init; }

    [JsonPropertyName("suggestedFix")]
    public SuggestedFix? SuggestedFix { get; init; }
}

public sealed record SuggestedFix
{
    [JsonPropertyName("originalCode")]
    public string? OriginalCode { get; init; }

    [JsonPropertyName("replacementCode")]
    public string? ReplacementCode { get; init; }
}

public sealed record ReviewMetrics
{
    [JsonPropertyName("criticalCount")]
    public int CriticalCount { get; init; }

    [JsonPropertyName("highCount")]
    public int HighCount { get; init; }

    [JsonPropertyName("mediumCount")]
    public int MediumCount { get; init; }

    [JsonPropertyName("lowCount")]
    public int LowCount { get; init; }
}

public sealed record CodeReviewReport
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("metrics")]
    public required ReviewMetrics Metrics { get; init; }

    [JsonPropertyName("issues")]
    public List<ReviewIssue> Issues { get; init; } = [];
}
