using FluentAssertions;
using DevTools.Core.Configuration;
using DevTools.Core.Models;
using DevTools.Orchestrator.Factories;
using DevTools.Orchestrator.Services;
using DevTools.Toolkit.Engine.Loaders;
using DevTools.Toolkit.Engine.Validators;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class OrchestratorAndValidatorTests
{
    private static string ResolveToolkitPath()
    {
        var dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "toolkit");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "toolkit.manifest.json")))
            {
                return candidate;
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "toolkit");
    }

    [Fact]
    public async Task ToolkitValidator_ShouldFindZeroErrorsInEntireToolkit()
    {
        // Arrange
        var toolkitDir = ResolveToolkitPath();
        var loader = new FileToolkitLoader();
        var manifest = await loader.LoadManifestAsync(toolkitDir);

        // Act
        var issues = ToolkitValidator.Validate(toolkitDir, manifest);

        // Assert
        issues.Should().BeEmpty("the decoupled toolkit must be strictly valid and self-contained with 0 errors");
    }

    [Fact]
    public async Task AgentOrchestrator_ShouldExecuteReviewAndDetectSecurityIssues()
    {
        // Arrange
        var toolkitDir = ResolveToolkitPath();
        var loader = new FileToolkitLoader();
        var orchestrator = new DefaultAgentOrchestrator(loader, toolkitDir);

        var suspiciousCode = """
        public class PaymentService
        {
            private string apiKey = "sk-live-999888777666555444";

            public PaymentResult Charge(decimal amount)
            {
                var result = ProcessChargeAsync(amount).Result; // Sync-over-async
                return result;
            }

            private async Task<PaymentResult> ProcessChargeAsync(decimal amt) => new PaymentResult();
        }
        """;

        // Act
        var response = await orchestrator.ExecuteAsync(new AgentRequest
        {
            AgentId = "test-auditor",
            PromptId = "code-review.system",
            TargetFilePath = "PaymentService.cs",
            CodeContent = suspiciousCode
        });

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ReviewReport.Should().NotBeNull();
        response.ReviewReport!.Issues.Should().NotBeEmpty();
        response.ReviewReport.Issues.Should().Contain(i => i.Message.Contains("hardcoded", StringComparison.OrdinalIgnoreCase));
        response.ReviewReport.Issues.Should().Contain(i => i.Message.Contains("Synchronous wait", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChatClientFactory_ShouldReturnNullWhenConfiguredAsOffline()
    {
        // Arrange
        var config = new DevToolsConfig
        {
            Ai = new AiConfig { DefaultProvider = "offline" }
        };

        // Act
        var client = ChatClientFactory.CreateClient(config);

        // Assert
        client.Should().BeNull("offline mode uses deterministic heuristics without external HTTP clients");
        ChatClientFactory.CurrentResolvedModel.Should().Be("deterministic-heuristics");
    }

    [Fact]
    public async Task ChatClientFactory_ShouldDiscoverOllamaStatusGracefully()
    {
        // Act
        var status = await ChatClientFactory.GetOllamaStatusAsync("http://localhost:11434");

        // Assert - If Ollama is running locally, status should be online and discover models without throwing
        status.Should().NotBeNull();
        status.PreferredModel.Should().Be("qwen2.5-coder:7b");
        if (status.IsOnline)
        {
            status.InstalledModels.Should().NotBeEmpty();
            status.ActiveModel.Should().NotBeNullOrEmpty();
        }
    }
}
