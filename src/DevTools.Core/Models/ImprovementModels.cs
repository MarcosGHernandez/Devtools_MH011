using System.Text.Json.Serialization;

namespace DevTools.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuditCategory
{
    Architecture,
    CodeQuality,
    Performance,
    Security,
    Reliability,
    Maintainability,
    DeveloperExperience
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalImpact
{
    Low,
    Medium,
    High,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProposalStatus
{
    Pending,
    InProgress,
    Applied,
    Dismissed
}

public sealed record CodebaseMetrics
{
    [JsonPropertyName("totalProjects")]
    public int TotalProjects { get; init; }

    [JsonPropertyName("totalCSharpFiles")]
    public int TotalCSharpFiles { get; init; }

    [JsonPropertyName("totalLinesOfCode")]
    public int TotalLinesOfCode { get; init; }

    [JsonPropertyName("totalTestCases")]
    public int TotalTestCases { get; init; }

    [JsonPropertyName("architectureLayers")]
    public List<string> ArchitectureLayers { get; init; } = [];

    [JsonPropertyName("cleanArchitectureCompliant")]
    public bool CleanArchitectureCompliant { get; init; }

    [JsonPropertyName("scanDurationMs")]
    public long ScanDurationMs { get; init; }
}

public sealed record IsoQualityScores
{
    [JsonPropertyName("maintainabilityScore")]
    public int MaintainabilityScore { get; init; } = 85;

    [JsonPropertyName("reliabilityScore")]
    public int ReliabilityScore { get; init; } = 85;

    [JsonPropertyName("performanceScore")]
    public int PerformanceScore { get; init; } = 90;

    [JsonPropertyName("securityScore")]
    public int SecurityScore { get; init; } = 90;

    [JsonPropertyName("overallQualityScore")]
    public int OverallQualityScore { get; init; } = 88;
}

public sealed record ImprovementProposal
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("category")]
    public AuditCategory Category { get; init; } = AuditCategory.Architecture;

    [JsonPropertyName("impact")]
    public ProposalImpact Impact { get; init; } = ProposalImpact.Medium;

    [JsonPropertyName("targetFile")]
    public string TargetFile { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("antigravityActionPlan")]
    public string AntigravityActionPlan { get; init; } = string.Empty;

    [JsonPropertyName("verificationCriteria")]
    public string VerificationCriteria { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public sealed record ProjectAuditReport
{
    [JsonPropertyName("auditId")]
    public string AuditId { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("projectName")]
    public string ProjectName { get; init; } = "DevTools";

    [JsonPropertyName("modelUsed")]
    public string ModelUsed { get; init; } = "hermes3:8b";

    [JsonPropertyName("thoughtScratchpad")]
    public string ThoughtScratchpad { get; init; } = string.Empty;

    [JsonPropertyName("executiveSummary")]
    public string ExecutiveSummary { get; init; } = string.Empty;

    [JsonPropertyName("metrics")]
    public CodebaseMetrics Metrics { get; init; } = new();

    [JsonPropertyName("qualityScores")]
    public IsoQualityScores QualityScores { get; init; } = new();

    [JsonPropertyName("proposals")]
    public List<ImprovementProposal> Proposals { get; init; } = [];
}
