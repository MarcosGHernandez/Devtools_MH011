using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using DevTools.Core.Models;
using DevTools.Data;
using DevTools.Data.Repositories;
using DevTools.Orchestrator.Services;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class ProjectPlanningServiceTests
{
    private static (DevToolsDbContext, ProjectPlanningService) CreateTestService()
    {
        var dbName = "plan_test_" + Guid.NewGuid().ToString("N") + ".db";
        var dbPath = Path.Combine(Path.GetTempPath(), dbName);

        var options = new DbContextOptionsBuilder<DevToolsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new DevToolsDbContext(options);
        context.Database.EnsureCreated();

        var projRepo = new SqliteProjectRepository(context);
        var docRepo = new SqliteDocumentationRepository(context);
        var knowRepo = new SqliteKnowledgeRepository(context);

        var service = new ProjectPlanningService(projRepo, docRepo, knowRepo);
        return (context, service);
    }

    [Fact]
    public async Task SynthesizePlanAsync_ShouldCreateCompleteBlueprintAndPersist()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var answers = new ProjectInterviewAnswers
            {
                ProjectName = "OrderStream Platform",
                Description = "High-throughput order processing system with event-driven architecture.",
                TargetUsers = "Logistics Operators",
                ArchitecturalStyle = "Clean Architecture / Vertical Slices",
                FrontendStack = "React + Minimalist Design System",
                DatabaseType = "PostgreSQL + EF Core 9",
                ExpectedLoad = "High (>50,000 orders/day)"
            };

            // Act
            var blueprint = await service.SynthesizePlanAsync(answers, persistToDatabase: true);

            // Assert
            blueprint.Should().NotBeNull();
            blueprint.ProjectName.Should().Be("OrderStream Platform");
            blueprint.TechStack.Should().ContainKey("Backend Runtime");
            blueprint.C4DiagramMermaid.Should().Contain("OrderStream Platform");
            blueprint.InitialAdrContent.Should().Contain("Status: Accepted");

            // Verify database records
            var projects = await context.Projects.ToListAsync();
            projects.Should().ContainSingle(p => p.Name == "OrderStream Platform");

            var docs = await context.Documents.ToListAsync();
            docs.Should().ContainSingle(d => d.DocType == "ADR");

            var knowledge = await context.KnowledgeItems.ToListAsync();
            knowledge.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task ScaffoldProjectDirectoryAsync_ShouldGenerateFilesOnDisk()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "scaffold_test_" + Guid.NewGuid().ToString("N"));
            var answers = new ProjectInterviewAnswers
            {
                ProjectName = "ScaffoldApp",
                Description = "Test application for scaffolding."
            };

            var blueprint = await service.SynthesizePlanAsync(answers, persistToDatabase: false);

            // Act
            await service.ScaffoldProjectDirectoryAsync(tempDir, blueprint);

            // Assert
            File.Exists(Path.Combine(tempDir, "README.md")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, ".editorconfig")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "docs", "adr", "0001-architecture-selection.md")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "frontend", "tokens", "tokens.css")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "docker-compose.yml")).Should().BeTrue();
            Directory.Exists(Path.Combine(tempDir, "src")).Should().BeTrue();

            // Cleanup
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_ShouldRespondWithRecommendationsAndBlueprint()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var request = new PlanningChatRequest
            {
                UserMessage = "Quiero crear un SaaS de facturación electrónica con Clean Architecture, React y PostgreSQL."
            };

            // Act
            var response = await service.ProcessPlanningChatAsync(request);

            // Assert
            response.Should().NotBeNull();
            response.AssistantReply.Should().Contain("Recomendaciones");
            response.UpdatedAnswers.ArchitecturalStyle.Should().Contain("Clean Architecture");
            response.UpdatedAnswers.DatabaseType.Should().Contain("PostgreSQL");
            response.GeneratedBlueprint.Should().NotBeNull();
            response.GeneratedBlueprint!.FrontendDesignSpec.Should().NotBeNull();
            response.GeneratedBlueprint.FrontendDesignSpec!.TokensCss.Should().Contain("--color-bg");
            response.ReadyToScaffold.Should().BeTrue();
            response.SuggestedQuestions.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_ShouldPersistConversationHistoryPerProject()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            // First turn (creates project and first 2 messages: user + assistant)
            var req1 = new PlanningChatRequest
            {
                UserMessage = "Quiero crear una plataforma de logistica y envios"
            };
            var res1 = await service.ProcessPlanningChatAsync(req1);

            res1.ProjectId.Should().NotBeNullOrEmpty();
            Guid.TryParse(res1.ProjectId, out var projectId).Should().BeTrue();

            // Second turn (references same project)
            var req2 = new PlanningChatRequest
            {
                ProjectId = res1.ProjectId,
                UserMessage = "Usaremos microservicios y Kafka con Blazor en el frontend"
            };
            var res2 = await service.ProcessPlanningChatAsync(req2);

            res2.ProjectId.Should().Be(res1.ProjectId);

            // Fetch conversation history from repository
            var messages = await service.GetProjectMessagesAsync(projectId);
            messages.Should().HaveCount(4, "there should be 2 user messages and 2 assistant responses for this project");
            messages[0].Role.Should().Be("user");
            messages[0].Content.Should().Contain("logistica");
            messages[1].Role.Should().Be("assistant");
            messages[2].Role.Should().Be("user");
            messages[2].Content.Should().Contain("Kafka");
            messages[3].Role.Should().Be("assistant");

            // Verify project summary
            var summaries = await service.GetProjectsSummaryAsync();
            var projSummary = summaries.FirstOrDefault(s => s.Id == res1.ProjectId);
            projSummary.Should().NotBeNull();
            projSummary!.MessageCount.Should().Be(4);
            projSummary.ArchitecturalStyle.Should().Contain("Microservices");
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_WhenUserOrdersProjectRename_ShouldUpdateNameAndBlueprint()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var initial = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                UserMessage = "SaaS de Facturación para Pequeñas Empresas"
            });

            // Act - rename
            var renameReq = new PlanningChatRequest
            {
                ProjectId = initial.ProjectId,
                UserMessage = "Cambia el nombre del proyecto a FacturaFast Pro"
            };
            var renameRes = await service.ProcessPlanningChatAsync(renameReq);

            // Assert
            renameRes.ExecutedAction.Should().NotBeNull();
            renameRes.ExecutedAction!.ActionType.Should().Be(AgentActionType.UpdateProject);
            renameRes.ExecutedAction.AffectedEntityName.Should().Be("FacturaFast Pro");
            renameRes.GeneratedBlueprint!.ProjectName.Should().Be("FacturaFast Pro");

            var summaries = await service.GetProjectsSummaryAsync();
            summaries.First(s => s.Id == initial.ProjectId).Name.Should().Be("FacturaFast Pro");
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_WhenUserOrdersStackModification_ShouldUpdateBlueprintTechStack()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var initial = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                UserMessage = "API de Comercio Electrónico"
            });

            // Act - update tech stack
            var updateReq = new PlanningChatRequest
            {
                ProjectId = initial.ProjectId,
                UserMessage = "Agrega Redis al stack y cambia la base de datos a CockroachDB"
            };
            var updateRes = await service.ProcessPlanningChatAsync(updateReq);

            // Assert
            updateRes.ExecutedAction.Should().NotBeNull();
            updateRes.ExecutedAction!.ActionType.Should().Be(AgentActionType.UpdateTechStack);
            updateRes.GeneratedBlueprint!.TechStack.Should().ContainKey("Cache / Sesiones");
            updateRes.GeneratedBlueprint.TechStack["Cache / Sesiones"].Should().Contain("Redis");
            updateRes.GeneratedBlueprint.TechStack["Base de Datos"].Should().Contain("CockroachDB");
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_WhenUserOrdersClearChat_ShouldClearMessages()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var initial = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                UserMessage = "Plataforma de Reservas"
            });

            // Add another message
            await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                ProjectId = initial.ProjectId,
                UserMessage = "Con soporte para pasarelas de pago Stripe"
            });

            var msgsBefore = await service.GetProjectMessagesAsync(Guid.Parse(initial.ProjectId));
            msgsBefore.Should().HaveCount(4);

            // Act - clear chat
            var clearRes = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                ProjectId = initial.ProjectId,
                UserMessage = "Limpia el chat por favor"
            });

            // Assert
            clearRes.ExecutedAction.Should().NotBeNull();
            clearRes.ExecutedAction!.ActionType.Should().Be(AgentActionType.ClearChat);

            var msgsAfter = await service.GetProjectMessagesAsync(Guid.Parse(initial.ProjectId));
            msgsAfter.Should().HaveCount(1);
            msgsAfter[0].Content.Should().Contain("reiniciado");
        }
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_WhenUserOrdersProjectDeletion_ShouldDeleteProjectAndCascade()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var initial = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                UserMessage = "Sistema Temporal a Descartar"
            });

            var pGuid = Guid.Parse(initial.ProjectId);

            // Act - delete project
            var deleteRes = await service.ProcessPlanningChatAsync(new PlanningChatRequest
            {
                ProjectId = initial.ProjectId,
                UserMessage = "Elimina este proyecto"
            });

            // Assert
            deleteRes.ExecutedAction.Should().NotBeNull();
            deleteRes.ExecutedAction!.ActionType.Should().Be(AgentActionType.DeleteProject);
            deleteRes.ExecutedAction.IsProjectDeleted.Should().BeTrue();

            var summaries = await service.GetProjectsSummaryAsync();
            summaries.Should().NotContain(s => s.Id == initial.ProjectId);

            var msgs = await service.GetProjectMessagesAsync(pGuid);
            msgs.Should().BeEmpty();
        }
    }
}
