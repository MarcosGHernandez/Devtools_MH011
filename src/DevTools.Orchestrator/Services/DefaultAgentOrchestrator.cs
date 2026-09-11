using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using DevTools.Core.Interfaces;
using DevTools.Core.Models;

namespace DevTools.Orchestrator.Services;

public sealed class DefaultAgentOrchestrator : IAgentOrchestrator
{
    private readonly IToolkitLoader _toolkitLoader;
    private readonly string _toolkitDirectory;
    private readonly IChatClient? _chatClient;
    private readonly Dictionary<string, ISkillExecutor> _skillExecutors;

    public DefaultAgentOrchestrator(
        IToolkitLoader toolkitLoader,
        string toolkitDirectory,
        IChatClient? chatClient = null,
        IEnumerable<ISkillExecutor>? skillExecutors = null)
    {
        _toolkitLoader = toolkitLoader;
        _toolkitDirectory = toolkitDirectory;
        _chatClient = chatClient;
        _skillExecutors = (skillExecutors ?? []).ToDictionary(s => s.SupportedSkillId, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AgentResponse> ExecuteAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var appliedSkills = new List<string>();

        try
        {
            // 1. Load the requested prompt definition from the decoupled toolkit
            var prompt = await _toolkitLoader.LoadPromptAsync(_toolkitDirectory, request.PromptId, cancellationToken);

            // 2. Execute any requested skills to enrich the context
            var contextAdditions = new List<string>();
            foreach (var skillId in request.EnabledSkillIds)
            {
                if (_skillExecutors.TryGetValue(skillId, out var executor))
                {
                    var skillResult = await executor.ExecuteAsync(new SkillExecutionRequest
                    {
                        SkillId = skillId,
                        Parameters = new Dictionary<string, object?> { ["repositoryPath"] = Directory.GetCurrentDirectory() }
                    }, cancellationToken);

                    if (skillResult.Success && !string.IsNullOrWhiteSpace(skillResult.OutputJson))
                    {
                        appliedSkills.Add(skillId);
                        contextAdditions.Add($"[Skill Data from '{skillId}']:\n{skillResult.OutputJson}");
                    }
                }
            }

            // 3. Render the prompt template
            var renderedPrompt = PromptRenderer.Render(prompt, request);
            if (contextAdditions.Count > 0)
            {
                renderedPrompt += "\n\n## Context Gathered from Skills:\n" + string.Join("\n\n", contextAdditions);
            }

            // 4. Generate LLM response or deterministic offline evaluation
            string rawOutput;
            if (_chatClient is not null)
            {
                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, renderedPrompt),
                    new(ChatRole.User, request.CodeContent ?? $"Execute agent '{request.AgentId}' for prompt '{request.PromptId}'.")
                };

                var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
                rawOutput = response.Text ?? string.Empty;
            }
            else
            {
                rawOutput = GenerateOfflineEvaluation(request, prompt);
            }

            // 5. Try parse structured CodeReviewReport if applicable
            CodeReviewReport? report = null;
            if (request.PromptId.Contains("review", StringComparison.OrdinalIgnoreCase))
            {
                report = TryParseReviewReport(rawOutput, request);
            }

            stopwatch.Stop();

            return new AgentResponse
            {
                AgentId = request.AgentId,
                Success = true,
                RawOutput = rawOutput,
                ReviewReport = report,
                AppliedSkills = appliedSkills,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new AgentResponse
            {
                AgentId = request.AgentId,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static CodeReviewReport? TryParseReviewReport(string output, AgentRequest request)
    {
        try
        {
            var jsonStart = output.IndexOf('{');
            var jsonEnd = output.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = output.Substring(jsonStart, jsonEnd - jsonStart + 1);
                return JsonSerializer.Deserialize<CodeReviewReport>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
        }
        catch
        {
            // If model output had markdown wrapper or non-strict JSON
        }

        return null;
    }

    private static string GenerateOfflineEvaluation(AgentRequest request, PromptDefinition prompt)
    {
        // High-value static heuristic when running locally without active LLM key
        var issues = new List<ReviewIssue>();
        var code = request.CodeContent ?? string.Empty;
        var targetFile = request.TargetFilePath ?? "Source.cs";

        if (code.Contains(".Result") || code.Contains(".Wait()"))
        {
            issues.Add(new ReviewIssue
            {
                RuleId = "ASYNC001",
                Severity = ReviewSeverity.HIGH,
                File = targetFile,
                Line = 1,
                Message = "Potential deadlock / thread starvation: Synchronous wait on asynchronous task (.Result or .Wait()).",
                Recommendation = "Use async/await with Task/ValueTask instead of blocking the calling thread.",
                SuggestedFix = new SuggestedFix
                {
                    OriginalCode = "var res = task.Result;",
                    ReplacementCode = "var res = await task;"
                }
            });
        }

        if ((code.Contains("password", StringComparison.OrdinalIgnoreCase) ||
             code.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
             code.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
             code.Contains("token", StringComparison.OrdinalIgnoreCase)) &&
            code.Contains("=") && code.Contains("\""))
        {
            issues.Add(new ReviewIssue
            {
                RuleId = "SEC001",
                Severity = ReviewSeverity.CRITICAL,
                File = targetFile,
                Line = 1,
                Message = "Possible hardcoded credentials detected in source code.",
                Recommendation = "Store secrets in environment variables or Azure Key Vault / User Secrets.",
                SuggestedFix = null
            });
        }

        var critical = issues.Count(i => i.Severity == ReviewSeverity.CRITICAL);
        var high = issues.Count(i => i.Severity == ReviewSeverity.HIGH);
        var score = Math.Max(0, 100 - (critical * 30 + high * 15));
        var verdict = critical > 0 ? "REQUEST_CHANGES" : (score > 85 ? "APPROVE" : "COMMENT");

        var report = new CodeReviewReport
        {
            Summary = $"Analyzed code with prompt '{prompt.Name}'. Found {issues.Count} actionable issue(s).",
            Score = score,
            Verdict = verdict,
            Metrics = new ReviewMetrics
            {
                CriticalCount = critical,
                HighCount = high,
                MediumCount = 0,
                LowCount = 0
            },
            Issues = issues
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}
