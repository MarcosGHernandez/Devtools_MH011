using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using DevTools.Data;
using DevTools.Data.Repositories;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class DataPersistenceTests
{
    private static DevToolsDbContext CreateTestDbContext()
    {
        var dbName = "test_" + Guid.NewGuid().ToString("N") + ".db";
        var dbPath = Path.Combine(Path.GetTempPath(), dbName);

        var options = new DbContextOptionsBuilder<DevToolsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new DevToolsDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task ProjectRepository_ShouldRegisterAndAuditProject()
    {
        // Arrange
        using var context = CreateTestDbContext();
        var repo = new SqliteProjectRepository(context);

        // Act
        var project = await repo.RegisterProjectAsync("DevTools Core App", "c:/repos/DevTools", "https://github.com/org/devtools");

        // Assert
        project.Should().NotBeNull();
        project.Name.Should().Be("DevTools Core App");

        // Record an audit
        await repo.RecordAuditAsync(project.Id, "src/DevTools.Core", 95, "APPROVE", 0, 0, 1, 2, "{\"summary\":\"Good\"}");

        var updated = await repo.GetProjectByIdAsync(project.Id);
        updated!.LastQualityScore.Should().Be(95);
        updated.Audits.Should().HaveCount(1);
        updated.Audits[0].Score.Should().Be(95);
    }

    [Fact]
    public async Task KnowledgeRepository_ShouldStoreAndSearchKnowledge()
    {
        // Arrange
        using var context = CreateTestDbContext();
        var repo = new SqliteKnowledgeRepository(context);

        // Act
        await repo.AddKnowledgeAsync(
            title: "Async Await Best Practices",
            domain: "Architecture",
            content: "Never block on async code with .Result or .Wait()",
            tags: "async,csharp,threading"
        );

        await repo.AddKnowledgeAsync(
            title: "Postgres Indexing",
            domain: "Database",
            content: "Use partial indexes on soft-deleted tables with WHERE is_deleted = false",
            tags: "postgres,indexing,performance"
        );

        // Assert
        var results = await repo.SearchKnowledgeAsync("async");
        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Async Await Best Practices");

        var dbItems = await repo.ListKnowledgeAsync("Database");
        dbItems.Should().HaveCount(1);
        dbItems[0].Title.Should().Be("Postgres Indexing");
    }

    [Fact]
    public async Task DocumentationRepository_ShouldSaveAndRetrieveAdr()
    {
        // Arrange
        using var context = CreateTestDbContext();
        var repo = new SqliteDocumentationRepository(context);

        // Act
        var adr = await repo.SaveDocumentAsync(
            title: "ADR 001: SQLite Embedded Storage",
            docType: "ADR",
            markdownContent: "# ADR 001\nWe use SQLite for local zero-config database storage."
        );

        // Assert
        adr.Should().NotBeNull();
        var docs = await repo.ListDocumentsAsync(docType: "ADR");
        docs.Should().HaveCount(1);
        docs[0].Title.Should().Be("ADR 001: SQLite Embedded Storage");

        // Update ADR
        var updated = await repo.UpdateDocumentAsync(adr.Id, "ADR 001: SQLite Enterprise Storage", "# ADR 001 Updated");
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("ADR 001: SQLite Enterprise Storage");

        // Delete ADR
        var deleted = await repo.DeleteDocumentAsync(adr.Id);
        deleted.Should().BeTrue();
        var emptyDocs = await repo.ListDocumentsAsync(docType: "ADR");
        emptyDocs.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectRepository_ShouldDeleteProjectAndCascade()
    {
        // Arrange
        using var context = CreateTestDbContext();
        var repo = new SqliteProjectRepository(context);

        var project = await repo.RegisterProjectAsync("Project To Delete", "c:/repos/delete-me");
        await repo.AddMessageAsync(project.Id, "user", "Message 1");
        await repo.AddMessageAsync(project.Id, "assistant", "Response 1");
        await repo.RecordAuditAsync(project.Id, "target", 90, "PASS", 0, 0, 0, 0, "{}");

        var msgsBefore = await repo.GetMessagesAsync(project.Id);
        msgsBefore.Should().HaveCount(2);

        // Act
        var deleted = await repo.DeleteProjectAsync(project.Id);

        // Assert
        deleted.Should().BeTrue();
        var p = await repo.GetProjectByIdAsync(project.Id);
        p.Should().BeNull();

        var msgsAfter = await repo.GetMessagesAsync(project.Id);
        msgsAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task ProjectRepository_ShouldUpdateDetailsAndClearMessages()
    {
        // Arrange
        using var context = CreateTestDbContext();
        var repo = new SqliteProjectRepository(context);

        var project = await repo.RegisterProjectAsync("Old Name", "c:/repos/old-path");
        await repo.AddMessageAsync(project.Id, "user", "Hello");

        // Act - Update details
        var updated = await repo.UpdateProjectDetailsAsync(project.Id, "New Name", "c:/repos/new-path", "New Desc");
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("New Name");
        updated.Description.Should().Be("New Desc");

        // Act - Clear messages
        var cleared = await repo.ClearProjectMessagesAsync(project.Id);
        cleared.Should().BeTrue();

        var msgs = await repo.GetMessagesAsync(project.Id);
        msgs.Should().BeEmpty();
    }
}
