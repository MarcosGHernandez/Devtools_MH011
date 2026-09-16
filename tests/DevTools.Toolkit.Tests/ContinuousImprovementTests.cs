using DevTools.Core.Models;
using DevTools.Orchestrator.Services;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class ContinuousImprovementTests
{
    [Fact]
    public async Task GatherMetricsAsync_ShouldScanCurrentSolutionSuccessfully()
    {
        var service = new HermesContinuousImprovementService();
        var rootDir = Directory.GetCurrentDirectory();

        var metrics = await service.GatherMetricsAsync(rootDir);

        Assert.NotNull(metrics);
        Assert.True(metrics.TotalProjects >= 1, "Debe detectar al menos un proyecto .NET");
        Assert.True(metrics.TotalCSharpFiles >= 1, "Debe detectar archivos de codigo C#");
        Assert.True(metrics.TotalLinesOfCode > 0, "Debe calcular lineas de codigo");
        Assert.True(metrics.CleanArchitectureCompliant, "DevTools debe cumplir Clean Architecture");
        Assert.True(metrics.ScanDurationMs >= 0, "La duracion debe medirse");
    }

    [Fact]
    public void ExtractThought_ShouldExtractHermesScratchpadContent()
    {
        var rawResponse = """
            <thought>
            Evaluating Clean Architecture invariants: Core has 0 external references.
            Checking ISO/IEC 25010 maintainability and reliability metrics.
            </thought>
            Executive Summary: All layers are decoupled.
            ```json
            [
              {
                "id": "IMP-001",
                "title": "Decouple Logging",
                "category": "Architecture",
                "impact": "Low",
                "targetFile": "Program.cs",
                "description": "Improve logging interfaces",
                "antigravityActionPlan": "Add ILogger abstraction",
                "verificationCriteria": "Run tests"
              }
            ]
            ```
            """;

        var thought = HermesContinuousImprovementService.ExtractThought(rawResponse);

        Assert.Contains("Evaluating Clean Architecture invariants", thought);
        Assert.Contains("ISO/IEC 25010", thought);
        Assert.DoesNotContain("<thought>", thought);
        Assert.DoesNotContain("</thought>", thought);
    }

    [Fact]
    public void ExtractProposals_ShouldParseStructuredJsonProposals()
    {
        var rawResponse = """
            <thought>Reasoning on improvements</thought>
            Resumen de auditoria.
            ```json
            [
              {
                "id": "IMP-010",
                "title": "Defensive Null Checks",
                "category": "CodeQuality",
                "impact": "High",
                "targetFile": "Services/Planner.cs",
                "description": "Prevent NRE when options are omitted",
                "antigravityActionPlan": "Inject ArgumentNullException.ThrowIfNull",
                "verificationCriteria": "dotnet test --filter Planner"
              }
            ]
            ```
            """;

        var proposals = HermesContinuousImprovementService.ExtractProposals(rawResponse);

        Assert.Single(proposals);
        var p = proposals[0];
        Assert.Equal("IMP-010", p.Id);
        Assert.Equal("Defensive Null Checks", p.Title);
        Assert.Equal(AuditCategory.CodeQuality, p.Category);
        Assert.Equal(ProposalImpact.High, p.Impact);
        Assert.Equal("Services/Planner.cs", p.TargetFile);
        Assert.Equal(ProposalStatus.Pending, p.Status);
    }

    [Fact]
    public async Task AuditProjectAsync_HeuristicFallback_ShouldGenerateCompleteReportWithScores()
    {
        var service = new HermesContinuousImprovementService();
        var report = await service.AuditProjectAsync(Directory.GetCurrentDirectory());

        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report.AuditId));
        Assert.NotNull(report.Metrics);
        Assert.NotNull(report.QualityScores);
        Assert.True(report.QualityScores.OverallQualityScore >= 60 && report.QualityScores.OverallQualityScore <= 100);
        Assert.NotEmpty(report.Proposals);
        Assert.False(string.IsNullOrWhiteSpace(report.ThoughtScratchpad));
    }

    [Fact]
    public async Task UpdateProposalStatusAsync_ShouldUpdateStateCorrectly()
    {
        var service = new HermesContinuousImprovementService();
        var report = await service.AuditProjectAsync(Directory.GetCurrentDirectory());
        var firstId = report.Proposals[0].Id;

        var updated = await service.UpdateProposalStatusAsync(firstId, ProposalStatus.Applied);
        var latest = await service.GetLatestReportAsync();

        Assert.True(updated);
        Assert.NotNull(latest);
        var target = latest.Proposals.First(p => p.Id == firstId);
        Assert.Equal(ProposalStatus.Applied, target.Status);
    }
}
