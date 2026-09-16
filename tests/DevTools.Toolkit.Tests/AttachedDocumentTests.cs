using System.Text.Json;
using DevTools.Core.Models;
using DevTools.Data;
using DevTools.Data.Repositories;
using DevTools.Orchestrator.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class AttachedDocumentTests
{
    private static (DevToolsDbContext Context, ProjectPlanningService Service) CreateTestService()
    {
        var dbName = "attach_test_" + Guid.NewGuid().ToString("N") + ".db";
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
    public void BuildEffectiveUserPrompt_WithNoDocuments_ReturnsOriginalMessage()
    {
        // Arrange
        var userMsg = "Crear un sistema de facturacion en .NET 9";

        // Act
        var resultNull = ProjectPlanningService.BuildEffectiveUserPrompt(userMsg, null);
        var resultEmpty = ProjectPlanningService.BuildEffectiveUserPrompt(userMsg, []);

        // Assert
        resultNull.Should().Be(userMsg);
        resultEmpty.Should().Be(userMsg);
    }

    [Fact]
    public void BuildEffectiveUserPrompt_WithSingleDocument_IncludesDocumentHeadersAndContent()
    {
        // Arrange
        var userMsg = "Analiza este modelo de dominio para el nuevo proyecto";
        var docs = new List<AttachedDocumentModel>
        {
            new()
            {
                FileName = "InvoiceEntity.cs",
                FileType = "text/plain",
                SizeBytes = 245,
                Content = "public class Invoice { public Guid Id { get; set; } public decimal Total { get; set; } }"
            }
        };

        // Act
        var result = ProjectPlanningService.BuildEffectiveUserPrompt(userMsg, docs);

        // Assert
        result.Should().Contain("### DOCUMENTOS ADJUNTOS / CONTEXTO TÉCNICO PROPORCIONADO POR EL USUARIO:");
        result.Should().Contain("--- INICIO DOCUMENTO: InvoiceEntity.cs (245 bytes, tipo: text/plain) ---");
        result.Should().Contain("public class Invoice");
        result.Should().Contain("--- FIN DOCUMENTO: InvoiceEntity.cs ---");
        result.Should().Contain("### REQUERIMIENTO / MENSAJE DEL USUARIO:");
        result.Should().Contain(userMsg);
    }

    [Fact]
    public void BuildEffectiveUserPrompt_WithMultipleDocuments_IncludesAllDocumentsSequentially()
    {
        // Arrange
        var userMsg = "Revisa estos dos archivos";
        var docs = new List<AttachedDocumentModel>
        {
            new()
            {
                FileName = "schema.sql",
                FileType = "application/sql",
                SizeBytes = 120,
                Content = "CREATE TABLE Users (Id UUID PRIMARY KEY, Email VARCHAR(255));"
            },
            new()
            {
                FileName = "architecture.md",
                FileType = "text/markdown",
                SizeBytes = 80,
                Content = "# Arquitectura Hexagonal con .NET 9"
            }
        };

        // Act
        var result = ProjectPlanningService.BuildEffectiveUserPrompt(userMsg, docs);

        // Assert
        result.Should().Contain("schema.sql");
        result.Should().Contain("CREATE TABLE Users");
        result.Should().Contain("architecture.md");
        result.Should().Contain("# Arquitectura Hexagonal con .NET 9");
        result.Should().Contain(userMsg);
    }

    [Fact]
    public async Task ProcessPlanningChatAsync_WithAttachedDocuments_PersistsEffectiveMessageToDatabase()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var request = new PlanningChatRequest
            {
                UserMessage = "Diseña la base de datos a partir de esta entidad",
                AttachedDocuments =
                [
                    new AttachedDocumentModel
                    {
                        FileName = "ProductAggregate.cs",
                        FileType = "csharp",
                        SizeBytes = 150,
                        Content = "public sealed class Product { public Guid Id { get; private set; } public string Sku { get; private set; } = string.Empty; }"
                    }
                ]
            };

            // Act
            var response = await service.ProcessPlanningChatAsync(request);

            // Assert
            response.Should().NotBeNull();
            response.AssistantReply.Should().NotBeNullOrWhiteSpace();

            // Verify stored messages in repository
            Guid.TryParse(response.ProjectId, out var pId).Should().BeTrue();
            var messages = await service.GetProjectMessagesAsync(pId);
            messages.Count.Should().BeGreaterThanOrEqualTo(2); // user + assistant

            var userTurn = messages.First(m => m.Role == "user");
            userTurn.Content.Should().Contain("ProductAggregate.cs");
            userTurn.Content.Should().Contain("public sealed class Product");
            userTurn.Content.Should().Contain("Diseña la base de datos a partir de esta entidad");
        }
    }

    [Fact]
    public async Task StreamPlanningChatAsync_WithAttachedDocuments_StreamsChunksAndStoresDocumentContext()
    {
        // Arrange
        var (context, service) = CreateTestService();
        using (context)
        {
            var request = new PlanningChatRequest
            {
                UserMessage = "Crea un proyecto para GestionPedidos basado en este documento de especificaciones",
                AttachedDocuments =
                [
                    new AttachedDocumentModel
                    {
                        FileName = "specs.json",
                        FileType = "application/json",
                        SizeBytes = 95,
                        Content = "{\"system\": \"GestionPedidos\", \"slaMs\": 150, \"database\": \"PostgreSQL\"}"
                    }
                ]
            };

            // Act
            var chunks = new List<StreamingPlanningChunk>();
            await foreach (var chunk in service.StreamPlanningChatAsync(request))
            {
                chunks.Add(chunk);
            }

            // Assert
            chunks.Should().NotBeEmpty();
            var doneChunk = chunks.LastOrDefault(c => c.Type == "done");
            doneChunk.Should().NotBeNull();

            // Verify database history
            Guid.TryParse(doneChunk!.ProjectId, out var pId).Should().BeTrue();
            var messages = await service.GetProjectMessagesAsync(pId);
            var userTurn = messages.First(m => m.Role == "user");
            userTurn.Content.Should().Contain("specs.json");
            userTurn.Content.Should().Contain("GestionPedidos");
        }
    }

    [Fact]
    public void AttachedDocumentModel_JsonSerialization_PreservesAllFields()
    {
        // Arrange
        var request = new PlanningChatRequest
        {
            UserMessage = "Verifica la serializacion",
            AttachedDocuments =
            [
                new AttachedDocumentModel
                {
                    FileName = "appsettings.json",
                    FileType = "application/json",
                    SizeBytes = 512,
                    Content = "{\"Logging\": {\"LogLevel\": {\"Default\": \"Information\"}}}"
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<PlanningChatRequest>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserMessage.Should().Be("Verifica la serializacion");
        deserialized.AttachedDocuments.Should().HaveCount(1);
        deserialized.AttachedDocuments![0].FileName.Should().Be("appsettings.json");
        deserialized.AttachedDocuments[0].SizeBytes.Should().Be(512);
        deserialized.AttachedDocuments[0].Content.Should().Contain("Information");
    }
}
