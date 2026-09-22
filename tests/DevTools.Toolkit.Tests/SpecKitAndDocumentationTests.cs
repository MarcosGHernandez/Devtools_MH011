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

public class SpecKitAndDocumentationTests
{
    private static ProjectPlanBlueprint CreateSampleBlueprint(string name = "FinTechCore")
    {
        return new ProjectPlanBlueprint
        {
            ProjectName = name,
            ExecutiveSummary = "Plataforma core bancaria de microservicios para conciliación y pagos en tiempo real.",
            ArchitecturalStyle = "Clean Architecture / Hexagonal",
            ArchitecturalRationale = "Aislamiento estricto de reglas de dominio financiero de pasarelas de pago y bases de datos.",
            TechStack = new Dictionary<string, string>
            {
                ["Backend"] = ".NET 9 / C# 13 Minimal APIs",
                ["Database"] = "PostgreSQL + EF Core 9",
                ["Cache"] = "Redis 7",
                ["Messaging"] = "RabbitMQ / Kafka",
                ["Frontend"] = "React 19 + TypeScript + Minimalist Slate"
            },
            DirectoryStructure = "src/\n  FinTechCore.Domain/\n  FinTechCore.Application/\n  FinTechCore.Infrastructure/\n  FinTechCore.Api/\ntests/\n  FinTechCore.UnitTests/",
            C4DiagramMermaid = "graph TD;\n  Client-->Api;\n  Api-->Database;\n  Api-->Redis;\n  Api-->RabbitMQ;",
            InitialAdrTitle = "ADR-001: Seleccion de Clean Architecture y Minimal APIs",
            InitialAdrContent = "Se selecciona Clean Architecture para garantizar testabilidad y desacoplamiento en transacciones financieras.",
            KeyConventions = ["Inmutabilidad en entidades de dominio", "Result Pattern en lugar de excepciones", "CQRS liviano"],
            FrontendDesignSpec = new FrontendDesignSpec
            {
                ThemeName = "Classic Minimalist Slate",
                TokensCss = ":root { --accent-blue: #3b82f6; --surface: #14171d; }"
            }
        };
    }

    [Fact]
    public void GenerateSpecKit_ShouldProduceAllFourCanonicalFiles()
    {
        // Arrange
        var bp = CreateSampleBlueprint("FinTechCore");

        // Act
        var specKit = SpecKitDocumentationGenerator.GenerateSpecKit(bp);

        // Assert
        specKit.Should().NotBeNull();
        specKit.ConstitutionMarkdown.Should().NotBeNullOrWhiteSpace();
        specKit.SpecMarkdown.Should().NotBeNullOrWhiteSpace();
        specKit.PlanMarkdown.Should().NotBeNullOrWhiteSpace();
        specKit.TasksMarkdown.Should().NotBeNullOrWhiteSpace();

        // Check Constitution contents
        specKit.ConstitutionMarkdown.Should().Contain("Constitución del Proyecto: FinTechCore");
        specKit.ConstitutionMarkdown.Should().Contain("GitHub Spec Kit");
        specKit.ConstitutionMarkdown.Should().Contain("Declaración de Principios Inmutables");
        specKit.ConstitutionMarkdown.Should().Contain("Compuertas de Decisión (Decision Gates)");

        // Check Spec contents
        specKit.SpecMarkdown.Should().Contain("Especificación Técnica Formal: FinTechCore");
        specKit.SpecMarkdown.Should().Contain("Requerimientos Funcionales");
        specKit.SpecMarkdown.Should().Contain("Requerimientos No Funcionales");
        specKit.SpecMarkdown.Should().Contain("Invariantes del Dominio");

        // Check Plan contents
        specKit.PlanMarkdown.Should().Contain("Plan Técnico de Arquitectura: FinTechCore");
        specKit.PlanMarkdown.Should().Contain("Mapa de Capas y Dependencias");
        specKit.PlanMarkdown.Should().Contain("Diagrama de Componentes C4");

        // Check Tasks contents
        specKit.TasksMarkdown.Should().Contain("Plan de Tareas de Implementación: FinTechCore");
        specKit.TasksMarkdown.Should().Contain("Fase 1: Fundamentos y Capa de Dominio");
        specKit.TasksMarkdown.Should().Contain("Fase 2: Capa de Aplicación");
        specKit.TasksMarkdown.Should().Contain("Fase 3: Capa de Infraestructura");
        specKit.TasksMarkdown.Should().Contain("Fase 4: Capa de Presentación y Web API");
        specKit.TasksMarkdown.Should().Contain("Fase 5: Aseguramiento de Calidad y Pruebas");
    }

    [Fact]
    public void GeneratePrd_ShouldProduceComprehensiveProductRequirements()
    {
        // Arrange
        var bp = CreateSampleBlueprint("PaymentGateway");

        // Act
        var prd = SpecKitDocumentationGenerator.GeneratePrd(bp);

        // Assert
        prd.Should().NotBeNullOrWhiteSpace();
        prd.Should().Contain("Documento de Requerimientos de Producto (PRD)");
        prd.Should().Contain("PaymentGateway");
        prd.Should().Contain("Declaración del Problema y Visión");
        prd.Should().Contain("Personas y Usuarios Clave");
        prd.Should().Contain("Requerimientos Funcionales");
        prd.Should().Contain("Requerimientos No Funcionales y SLAs");
        prd.Should().Contain("Criterios de Aceptación");
        prd.Should().Contain("Métricas de Éxito (KPIs)");
    }

    [Fact]
    public void GenerateSuggestionsAndRoadmap_ShouldContainISO25010Recommendations()
    {
        // Arrange
        var bp = CreateSampleBlueprint("OrderManagement");

        // Act
        var suggestions = SpecKitDocumentationGenerator.GenerateSuggestionsAndRoadmap(bp);

        // Assert
        suggestions.Should().NotBeNullOrWhiteSpace();
        suggestions.Should().Contain("ISO/IEC 25010");
        suggestions.Should().Contain("Outbox Transaccional");
        suggestions.Should().Contain("Caché Multinivel");
        suggestions.Should().Contain("OpenTelemetry");
        suggestions.Should().Contain("Idempotencia");
        suggestions.Should().Contain("Hoja de Ruta de Desarrollo (Roadmap por Fases)");
        suggestions.Should().Contain("Fase 1");
        suggestions.Should().Contain("Fase 2");
    }

    [Fact]
    public void BuildProjectFiles_ShouldIncludeSpecKitAndDocumentationSuite()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateSampleBlueprint("EnterpriseService");

        // Act
        var files = service.BuildProjectFiles(bp);

        // Assert
        files.Should().NotBeEmpty();

        // Spec Kit canonical files
        files.Should().Contain(f => f.RelativePath == ".spec-kit/constitution.md");
        files.Should().Contain(f => f.RelativePath == ".spec-kit/spec.md");
        files.Should().Contain(f => f.RelativePath == ".spec-kit/plan.md");
        files.Should().Contain(f => f.RelativePath == ".spec-kit/tasks.md");

        // PRD and Architectural documentation suite
        files.Should().Contain(f => f.RelativePath == "PRD.md");
        files.Should().Contain(f => f.RelativePath == "docs/PRD.md");
        files.Should().Contain(f => f.RelativePath == "docs/SUGGESTIONS.md");
        files.Should().Contain(f => f.RelativePath == "docs/ROADMAP.md");
        files.Should().Contain(f => f.RelativePath == "AGENTS.md");
        files.Should().Contain(f => f.RelativePath == ".agents/AGENTS.md");

        // Verify content integrity
        var constitution = files.First(f => f.RelativePath == ".spec-kit/constitution.md").Content;
        constitution.Should().Contain("EnterpriseService");

        var prd = files.First(f => f.RelativePath == "PRD.md").Content;
        prd.Should().Contain("Requerimientos de Producto");
    }

    [Fact]
    public async Task GenerateSpecKitZipAsync_ShouldProduceValidZipWithSpecKitFiles()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateSampleBlueprint("SpecKitZipProject");

        // Act
        var zipBytes = await service.GenerateSpecKitZipAsync(bp);

        // Assert
        zipBytes.Should().NotBeNull();
        zipBytes.Length.Should().BeGreaterThan(0);

        using var memoryStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

        archive.Entries.Should().Contain(e => e.FullName == ".spec-kit/constitution.md");
        archive.Entries.Should().Contain(e => e.FullName == ".spec-kit/spec.md");
        archive.Entries.Should().Contain(e => e.FullName == ".spec-kit/plan.md");
        archive.Entries.Should().Contain(e => e.FullName == ".spec-kit/tasks.md");
        archive.Entries.Should().Contain(e => e.FullName == "README.md");
    }

    [Fact]
    public async Task GeneratePrdMarkdownAsync_ShouldProduceNonEmptyMarkdown()
    {
        // Arrange
        var service = new ProjectScaffoldingService();
        var bp = CreateSampleBlueprint("PrdGenProject");

        // Act
        var prdMd = await service.GeneratePrdMarkdownAsync(bp);

        // Assert
        prdMd.Should().NotBeNullOrWhiteSpace();
        prdMd.Should().Contain("PrdGenProject");
        prdMd.Should().Contain("Documento de Requerimientos de Producto (PRD)");
    }

    [Fact]
    public async Task AgentAction_CRUD_PrdAndSpecKit_ShouldPersistAndManageInDatabase()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_speckit_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<Data.DevToolsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        using var db = new Data.DevToolsDbContext(options);
        db.Database.EnsureCreated();

        var projectRepo = new SqliteProjectRepository(db);
        var docRepo = new SqliteDocumentationRepository(db);
        var knowledgeRepo = new SqliteKnowledgeRepository(db);
        var scaffoldingService = new ProjectScaffoldingService();

        var planner = new ProjectPlanningService(projectRepo, docRepo, knowledgeRepo, null, scaffoldingService);

        var project = await projectRepo.RegisterProjectAsync("SaaSPlatform", @"C:\Projects\SaaSPlatform");
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = "SaaSPlatform",
            Description = "Plataforma multi-tenant B2B.",
            ArchitecturalStyle = "Clean Architecture",
            FrontendStack = "React + Slate",
            DatabaseType = "PostgreSQL"
        };

        // Act: Synthesize Plan (which automatically generates and persists PRD, SpecKit, ADR, Agents, Suggestions)
        var bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: true);

        // Assert: Blueprint has all properties populated
        bp.SpecKit.Should().NotBeNull();
        bp.PrdMarkdown.Should().NotBeNullOrWhiteSpace();
        bp.SuggestionsMarkdown.Should().NotBeNullOrWhiteSpace();
        bp.AgentsMarkdown.Should().NotBeNullOrWhiteSpace();

        // Assert: Verify documents are stored in documentation repository
        var docs = await docRepo.ListDocumentsAsync();
        docs.Should().Contain(d => d.DocType == "PRD" && d.Title.Contains("SaaSPlatform"));
        docs.Should().Contain(d => d.DocType == "SpecKit" && d.Title.Contains("SaaSPlatform"));
        docs.Should().Contain(d => d.DocType == "Suggestions" && d.Title.Contains("SaaSPlatform"));
        docs.Should().Contain(d => d.DocType == "AgentsMd" && d.Title.Contains("SaaSPlatform"));
        docs.Should().Contain(d => d.DocType == "ADR");

        // Act: Agent CRUD - Read Document
        var readReq = new PlanningChatRequest
        {
            ProjectId = project.Id.ToString(),
            UserMessage = "lee el prd del proyecto"
        };
        var readResp = await planner.ProcessPlanningChatAsync(readReq);
        readResp.AssistantReply.Should().Contain("Requerimientos de Producto");

        // Act: Agent CRUD - Delete Document
        var prdDoc = docs.First(d => d.DocType == "PRD");
        var deleteResult = await planner.DeleteDocumentAsync(prdDoc.Id);
        deleteResult.Should().BeTrue();

        var remainingDocs = await docRepo.ListDocumentsAsync();
        remainingDocs.Should().NotContain(d => d.Id == prdDoc.Id);
    }
}
