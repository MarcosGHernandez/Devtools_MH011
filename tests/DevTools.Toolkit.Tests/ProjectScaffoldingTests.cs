using System.IO.Compression;
using DevTools.Core.Entities;
using DevTools.Core.Interfaces;
using DevTools.Core.Models;
using DevTools.Data.Repositories;
using DevTools.Orchestrator.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class ProjectScaffoldingTests
{
    private static ProjectPlanBlueprint CreateTestBlueprint(string name = "LogisticsCore")
    {
        return new ProjectPlanBlueprint
        {
            ProjectName = name,
            ExecutiveSummary = "Sistema de trazabilidad logística y telemetría IoT distribuida.",
            ArchitecturalRationale = "Clean Architecture con PostgreSQL, Redis y Kafka.",
            TechStack = new Dictionary<string, string>
            {
                ["Database"] = "PostgreSQL + EF Core 9",
                ["Cache"] = "Redis 7",
                ["Messaging"] = "Apache Kafka",
                ["Frontend"] = "React + Minimalist Slate"
            },
            DirectoryStructure = "src/\n  LogisticsCore.Domain/\n  LogisticsCore.Application/\n  LogisticsCore.Infrastructure/\n  LogisticsCore.Api/\ntests/\n  LogisticsCore.UnitTests/",
            C4DiagramMermaid = "graph TD;\n  Client-->Api;\n  Api-->Database;\n  Api-->Redis;\n  Api-->Kafka;",
            InitialAdrTitle = "ADR-001: Adopcion de Clean Architecture",
            InitialAdrContent = "Se adopta Clean Architecture para aislar reglas de negocio de la infraestructura.",
            KeyConventions = ["Git Flow estricto", "CQRS con MediatR", "Validacion con FluentValidation"],
            FrontendDesignSpec = new FrontendDesignSpec
            {
                ThemeName = "Classic Minimalist Slate",
                ColorPalette = new Dictionary<string, string>
                {
                    ["Accent"] = "#3b82f6",
                    ["Surface"] = "#14171d"
                }
            }
        };
    }

    [Fact]
    public void BuildProjectFiles_ShouldGenerate_CompleteCleanArchitectureFiles()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateTestBlueprint("LogisticsCore");

        // Act
        var files = service.BuildProjectFiles(bp);

        // Assert
        files.Should().NotBeEmpty();
        files.Should().Contain(f => f.RelativePath == "LogisticsCore.sln");
        files.Should().Contain(f => f.RelativePath == "src/LogisticsCore.Domain/LogisticsCore.Domain.csproj");
        files.Should().Contain(f => f.RelativePath == "src/LogisticsCore.Application/LogisticsCore.Application.csproj");
        files.Should().Contain(f => f.RelativePath == "src/LogisticsCore.Infrastructure/LogisticsCore.Infrastructure.csproj");
        files.Should().Contain(f => f.RelativePath == "src/LogisticsCore.Api/LogisticsCore.Api.csproj");
        files.Should().Contain(f => f.RelativePath == "src/LogisticsCore.Api/Program.cs");
        files.Should().Contain(f => f.RelativePath == "tests/LogisticsCore.UnitTests/LogisticsCore.UnitTests.csproj");
        files.Should().Contain(f => f.RelativePath == "docker-compose.yml");
        files.Should().Contain(f => f.RelativePath == "Dockerfile");
        files.Should().Contain(f => f.RelativePath == "README.md");
        files.Should().Contain(f => f.RelativePath == "AGENTS.md");
        files.Should().Contain(f => f.RelativePath == ".agents/AGENTS.md");
        files.Should().Contain(f => f.RelativePath == "docs/ARCHITECTURE.md");
        files.Should().Contain(f => f.RelativePath == "docs/adrs/ADR-001-Initial-Architecture.md");

        // Check docker-compose contains Postgres, Redis, and Kafka
        var dockerCompose = files.First(f => f.RelativePath == "docker-compose.yml").Content;
        dockerCompose.Should().Contain("postgres");
        dockerCompose.Should().Contain("redis");
        dockerCompose.Should().Contain("kafka");

        // Check AGENTS.md contains Antigravity directives
        var agentsMd = files.First(f => f.RelativePath == "AGENTS.md").Content;
        agentsMd.Should().Contain("Google Antigravity");
        agentsMd.Should().Contain("Clean Architecture");
        agentsMd.Should().Contain("C# 13");
        agentsMd.Should().Contain("dotnet build");
        agentsMd.Should().Contain("dotnet test --nologo");
    }

    [Fact]
    public async Task GenerateOnDiskAsync_ShouldCreate_PhysicalFiles_InTargetDirectory()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateTestBlueprint("TestDiskApp");
        var tempDir = Path.Combine(Path.GetTempPath(), "DevTools_Scaffold_Test_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            // Act
            var result = await service.GenerateOnDiskAsync(bp, tempDir);

            // Assert
            result.Success.Should().BeTrue();
            result.TotalFilesCreated.Should().BeGreaterThan(10);
            Directory.Exists(tempDir).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "TestDiskApp.sln")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "docker-compose.yml")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "README.md")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "src", "TestDiskApp.Api", "Program.cs")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateZipArchiveAsync_ShouldCreate_ValidZipWithEntries()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateTestBlueprint("ZipTestApp");

        // Act
        var zipBytes = await service.GenerateZipArchiveAsync(bp);

        // Assert
        zipBytes.Should().NotBeNullOrEmpty();
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Should().HaveCountGreaterThan(10);
        archive.Entries.Should().Contain(e => e.FullName == "ZipTestApp.sln");
        archive.Entries.Should().Contain(e => e.FullName == "README.md");
        archive.Entries.Should().Contain(e => e.FullName == "docker-compose.yml");
    }

    private static DevTools.Data.DevToolsDbContext CreateTestDbContext()
    {
        var dbName = "test_scaffold_" + Guid.NewGuid().ToString("N") + ".db";
        var dbPath = Path.Combine(Path.GetTempPath(), dbName);

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<DevTools.Data.DevToolsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new DevTools.Data.DevToolsDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_ShouldRecognize_ScaffoldingCommand()
    {
        // Arrange
        using var db = CreateTestDbContext();
        var projectRepo = new SqliteProjectRepository(db);
        var docRepo = new SqliteDocumentationRepository(db);
        var knowledgeRepo = new SqliteKnowledgeRepository(db);
        var scaffoldingService = new ProjectScaffoldingService();
        var service = new ProjectPlanningService(projectRepo, docRepo, knowledgeRepo, chatClient: null, scaffoldingService);

        var tempTarget = Path.Combine(Path.GetTempPath(), "DevTools_AgentScaffold_" + Guid.NewGuid().ToString("N")[..8]);
        var project = await projectRepo.RegisterProjectAsync("AgentScaffoldApp", tempTarget);

        try
        {
            // Act
            var response = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                ProjectId = project.Id.ToString(),
                UserMessage = $"Genera la solución en disco en {tempTarget}"
            });

            // Assert
            response.ExecutedAction.Should().NotBeNull();
            response.ExecutedAction!.ActionType.Should().Be(AgentActionType.ScaffoldProject);
            response.ExecutedAction.Success.Should().BeTrue();
            response.ExecutedAction.FilesCreatedCount.Should().BeGreaterThan(10);
            Directory.Exists(tempTarget).Should().BeTrue();
            File.Exists(Path.Combine(tempTarget, "AgentScaffoldApp.sln")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempTarget))
            {
                Directory.Delete(tempTarget, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_ShouldRecognize_ExportZipCommand()
    {
        // Arrange
        using var db = CreateTestDbContext();
        var projectRepo = new SqliteProjectRepository(db);
        var docRepo = new SqliteDocumentationRepository(db);
        var knowledgeRepo = new SqliteKnowledgeRepository(db);
        var service = new ProjectPlanningService(projectRepo, docRepo, knowledgeRepo, chatClient: null);

        var project = await projectRepo.RegisterProjectAsync("ExportZipTest", "C:\\Dummy");

        // Act
        var response = await service.ProcessPlanningChatAsync(new PlanningChatRequest
        {
            ProjectId = project.Id.ToString(),
            UserMessage = "Exporta el proyecto como zip"
        });

        // Assert
        response.ExecutedAction.Should().NotBeNull();
        response.ExecutedAction!.ActionType.Should().Be(AgentActionType.ExportProjectZip);
        response.ExecutedAction.Success.Should().BeTrue();
        response.ExecutedAction.DownloadUrl.Should().Contain($"/api/planning/projects/{project.Id}/export-zip");
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_ShouldRecognize_ExportDocsCommand()
    {
        // Arrange
        using var db = CreateTestDbContext();
        var projectRepo = new SqliteProjectRepository(db);
        var docRepo = new SqliteDocumentationRepository(db);
        var knowledgeRepo = new SqliteKnowledgeRepository(db);
        var service = new ProjectPlanningService(projectRepo, docRepo, knowledgeRepo, chatClient: null);

        var project = await projectRepo.RegisterProjectAsync("DocsExportTest", "C:\\Dummy");

        // Act
        var response = await service.ProcessPlanningChatAsync(new PlanningChatRequest
        {
            ProjectId = project.Id.ToString(),
            UserMessage = "Exporta la documentación en markdown"
        });

        // Assert
        response.ExecutedAction.Should().NotBeNull();
        response.ExecutedAction!.ActionType.Should().Be(AgentActionType.ExportDocumentation);
        response.ExecutedAction.Success.Should().BeTrue();
        response.ExecutedAction.DownloadUrl.Should().Contain($"/api/planning/projects/{project.Id}/export-docs");
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_ShouldRecognize_ExportAgentsCommand()
    {
        // Arrange
        using var db = CreateTestDbContext();
        var projectRepo = new SqliteProjectRepository(db);
        var docRepo = new SqliteDocumentationRepository(db);
        var knowledgeRepo = new SqliteKnowledgeRepository(db);
        var service = new ProjectPlanningService(projectRepo, docRepo, knowledgeRepo, chatClient: null);

        var project = await projectRepo.RegisterProjectAsync("AntigravityProject", "C:\\Dummy");

        // Act
        var response = await service.ProcessPlanningChatAsync(new PlanningChatRequest
        {
            ProjectId = project.Id.ToString(),
            UserMessage = "Exporta el archivo agents.md para antigravity"
        });

        // Assert
        response.ExecutedAction.Should().NotBeNull();
        response.ExecutedAction!.ActionType.Should().Be(AgentActionType.ExportAgentsMarkdown);
        response.ExecutedAction.Success.Should().BeTrue();
        response.ExecutedAction.DownloadUrl.Should().Contain($"/api/planning/projects/{project.Id}/export-agents");
        response.AssistantReply.Should().Contain("AGENTS.md");
    }

    [Fact]
    public async Task GenerateAgentsMarkdownAsync_ShouldGenerate_AntigravityDirectives()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateTestBlueprint("EnterpriseSystem");

        // Act
        var agentsMd = await service.GenerateAgentsMarkdownAsync(bp);

        // Assert
        agentsMd.Should().NotBeNullOrWhiteSpace();
        agentsMd.Should().Contain("EnterpriseSystem");
        agentsMd.Should().Contain("Google Antigravity");
        agentsMd.Should().Contain("Clean Architecture");
        agentsMd.Should().Contain("src/EnterpriseSystem.Domain");
        agentsMd.Should().Contain("src/EnterpriseSystem.Application");
        agentsMd.Should().Contain("src/EnterpriseSystem.Infrastructure");
        agentsMd.Should().Contain("src/EnterpriseSystem.Api");
        agentsMd.Should().Contain("dotnet build");
        agentsMd.Should().Contain("dotnet test --nologo");
        agentsMd.Should().Contain("ISO/IEC 25010");
    }

    [Fact]
    public async Task ScaffoldProject_Should_CompileWithDotnetBuild()
    {
        // Arrange
        var blueprint = CreateTestBlueprint("CompileTestApp");
        var service = new ProjectScaffoldingService();
        var tempTarget = Path.Combine(Path.GetTempPath(), "DevTools_BuildVerify_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            // Act - 1. Scaffold files
            var result = await service.GenerateOnDiskAsync(blueprint, tempTarget);
            result.Success.Should().BeTrue();

            var slnPath = Path.Combine(tempTarget, "CompileTestApp.sln");
            File.Exists(slnPath).Should().BeTrue();

            // Act - 2. Execute dotnet build
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{slnPath}\" --nologo -c Release",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            proc.Start();
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // Assert
            proc.ExitCode.Should().Be(0, because: $"dotnet build output: {stdout}\nErrors: {stderr}");
        }
        finally
        {
            if (Directory.Exists(tempTarget))
            {
                try { Directory.Delete(tempTarget, recursive: true); } catch { }
            }
        }
    }
}
