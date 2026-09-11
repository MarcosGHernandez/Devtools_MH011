using System.IO.Compression;
using System.Text;
using DevTools.Core.Interfaces;
using DevTools.Core.Models;

namespace DevTools.Orchestrator.Services;

public class ProjectScaffoldingService : IProjectScaffoldingService
{
    public IReadOnlyList<ScaffoldedFile> BuildProjectFiles(ProjectPlanBlueprint blueprint)
    {
        var rawName = string.IsNullOrWhiteSpace(blueprint.ProjectName) ? "MyApp" : blueprint.ProjectName;
        var safeName = SanitizeIdentifier(rawName);
        var files = new List<ScaffoldedFile>();

        var dbType = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL + EF Core 9");
        var broker = blueprint.TechStack.GetValueOrDefault("Messaging", "None");
        var cache = blueprint.TechStack.GetValueOrDefault("Cache", "None");

        // 1. Solution file (.sln)
        files.Add(new ScaffoldedFile
        {
            RelativePath = $"{safeName}.sln",
            Content = GenerateSolutionFile(safeName),
            Description = "Visual Studio / .NET 9 Solution File"
        });

        // 2. Domain Layer
        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Domain/{safeName}.Domain.csproj",
            Content = GenerateProjectFile("Microsoft.NET.Sdk", "net9.0", []),
            Description = "Domain Layer C# Project"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Domain/Common/BaseEntity.cs",
            Content = GenerateBaseEntityCode(safeName),
            Description = "Base Entity Abstraction"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Domain/Entities/SampleAggregate.cs",
            Content = GenerateSampleAggregateCode(safeName),
            Description = "Core Domain Aggregate Entity"
        });

        // 3. Application Layer
        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Application/{safeName}.Application.csproj",
            Content = GenerateProjectFile(
                "Microsoft.NET.Sdk",
                "net9.0",
                [$"..\\{safeName}.Domain\\{safeName}.Domain.csproj"],
                ["Microsoft.EntityFrameworkCore"]),
            Description = "Application Layer C# Project"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Application/Common/Interfaces/IApplicationDbContext.cs",
            Content = GenerateDbContextInterfaceCode(safeName),
            Description = "DbContext Interface Abstraction"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Application/Features/SampleAggregateDto.cs",
            Content = GenerateDtoCode(safeName),
            Description = "Sample Data Transfer Object"
        });

        // 4. Infrastructure Layer
        var infraPackages = new List<string> { "Microsoft.EntityFrameworkCore" };
        if (dbType.Contains("Postgres", StringComparison.OrdinalIgnoreCase) || dbType.Contains("Cockroach", StringComparison.OrdinalIgnoreCase))
        {
            infraPackages.Add("Npgsql.EntityFrameworkCore.PostgreSQL");
        }
        else if (dbType.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            infraPackages.Add("Microsoft.EntityFrameworkCore.Sqlite");
        }
        else
        {
            infraPackages.Add("Microsoft.EntityFrameworkCore.InMemory");
        }

        if (cache.Contains("Redis", StringComparison.OrdinalIgnoreCase) || blueprint.TechStack.Values.Any(v => v.Contains("Redis", StringComparison.OrdinalIgnoreCase)))
        {
            infraPackages.Add("StackExchange.Redis");
        }

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Infrastructure/{safeName}.Infrastructure.csproj",
            Content = GenerateProjectFile(
                "Microsoft.NET.Sdk",
                "net9.0",
                [$"..\\{safeName}.Domain\\{safeName}.Domain.csproj", $"..\\{safeName}.Application\\{safeName}.Application.csproj"],
                infraPackages),
            Description = "Infrastructure Layer C# Project"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Infrastructure/Persistence/ApplicationDbContext.cs",
            Content = GenerateDbContextCode(safeName),
            Description = "EF Core ApplicationDbContext Implementation"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Infrastructure/DependencyInjection.cs",
            Content = GenerateInfrastructureDiCode(safeName, dbType),
            Description = "Infrastructure Dependency Injection Registration"
        });

        // 5. Api Layer
        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Api/{safeName}.Api.csproj",
            Content = GenerateProjectFile(
                "Microsoft.NET.Sdk.Web",
                "net9.0",
                [$"..\\{safeName}.Application\\{safeName}.Application.csproj", $"..\\{safeName}.Infrastructure\\{safeName}.Infrastructure.csproj"],
                ["Microsoft.AspNetCore.OpenApi"]),
            Description = "Web API Host C# Project"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Api/Program.cs",
            Content = GenerateApiProgramCode(safeName, blueprint),
            Description = "Minimal API Entry Point with OpenAPI"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"src/{safeName}.Api/appsettings.json",
            Content = GenerateAppSettingsJson(safeName, dbType),
            Description = "Application Configuration Settings"
        });

        // 6. Unit Tests Layer
        files.Add(new ScaffoldedFile
        {
            RelativePath = $"tests/{safeName}.UnitTests/{safeName}.UnitTests.csproj",
            Content = GenerateProjectFile(
                "Microsoft.NET.Sdk",
                "net9.0",
                [$"..\\..\\src\\{safeName}.Domain\\{safeName}.Domain.csproj", $"..\\..\\src\\{safeName}.Application\\{safeName}.Application.csproj"],
                ["xunit", "xunit.runner.visualstudio", "FluentAssertions", "Microsoft.NET.Test.Sdk"]),
            Description = "Unit Tests Project"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = $"tests/{safeName}.UnitTests/DomainEntityTests.cs",
            Content = GenerateSampleUnitTestCode(safeName),
            Description = "Sample Domain Unit Tests"
        });

        // 7. DevOps & Docker
        files.Add(new ScaffoldedFile
        {
            RelativePath = "docker-compose.yml",
            Content = GenerateDockerCompose(safeName, blueprint),
            Description = "Multi-Container Docker Compose Specification"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = "Dockerfile",
            Content = GenerateDockerfile(safeName),
            Description = "Multi-Stage Dockerfile for .NET 9 API"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = ".gitignore",
            Content = GenerateGitIgnore(),
            Description = "Comprehensive .NET / Node .gitignore"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = ".editorconfig",
            Content = GenerateEditorConfig(),
            Description = "Clean Code .editorconfig Rules"
        });

        // 8. Documentation & Agent Guidelines
        var agentsContent = GenerateAgentsMarkdown(safeName, blueprint);

        files.Add(new ScaffoldedFile
        {
            RelativePath = "AGENTS.md",
            Content = agentsContent,
            Description = "Antigravity & AI Agent Coding Rules, Layer Isolation and Standards"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = ".agents/AGENTS.md",
            Content = agentsContent,
            Description = "Workspace Customizations Agent Rules for Antigravity"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = "README.md",
            Content = GenerateReadmeMarkdown(safeName, blueprint),
            Description = "Project Readme and Quickstart Guide"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = "docs/ARCHITECTURE.md",
            Content = GenerateArchitectureMarkdownInternal(safeName, blueprint),
            Description = "Architecture Documentation with C4 Diagram"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = "docs/adrs/ADR-001-Initial-Architecture.md",
            Content = GenerateAdrMarkdown(safeName, blueprint),
            Description = "ADR-001 Architecture Decision Record"
        });

        files.Add(new ScaffoldedFile
        {
            RelativePath = "docs/adr/0001-architecture-selection.md",
            Content = GenerateAdrMarkdown(safeName, blueprint),
            Description = "Initial Architecture Selection ADR"
        });

        var tokensContent = !string.IsNullOrWhiteSpace(blueprint.FrontendDesignSpec?.TokensCss)
            ? blueprint.FrontendDesignSpec.TokensCss
            : GenerateDefaultTokensCss();

        files.Add(new ScaffoldedFile
        {
            RelativePath = "frontend/tokens/tokens.css",
            Content = tokensContent,
            Description = "Frontend CSS Design Tokens"
        });

        return files;
    }

    public async Task<ProjectScaffoldingResult> GenerateOnDiskAsync(
        ProjectPlanBlueprint blueprint,
        string targetPath,
        CancellationToken ct = default)
    {
        var rawName = string.IsNullOrWhiteSpace(blueprint.ProjectName) ? "MyApp" : blueprint.ProjectName;
        var safeName = SanitizeIdentifier(rawName);

        var resolvedPath = Path.GetFullPath(targetPath.Trim());
        var warnings = new List<string>();

        try
        {
            if (!Directory.Exists(resolvedPath))
            {
                Directory.CreateDirectory(resolvedPath);
            }
            else
            {
                var existingCount = Directory.GetFileSystemEntries(resolvedPath).Length;
                if (existingCount > 0)
                {
                    warnings.Add($"Target directory '{resolvedPath}' already contains {existingCount} file(s)/folder(s). Existing files may be overwritten.");
                }
            }

            var files = BuildProjectFiles(blueprint);
            var generatedList = new List<string>();

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var fullFilePath = Path.Combine(resolvedPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var fileDir = Path.GetDirectoryName(fullFilePath);
                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir))
                {
                    Directory.CreateDirectory(fileDir);
                }

                await File.WriteAllTextAsync(fullFilePath, file.Content, Encoding.UTF8, ct);
                generatedList.Add(file.RelativePath);
            }

            return new ProjectScaffoldingResult
            {
                Success = true,
                Message = $"Scaffolding for '{safeName}' generated successfully in {resolvedPath}.",
                OutputPath = resolvedPath,
                TotalFilesCreated = generatedList.Count,
                GeneratedFiles = generatedList,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new ProjectScaffoldingResult
            {
                Success = false,
                Message = $"Failed to scaffold project on disk: {ex.Message}",
                OutputPath = resolvedPath,
                TotalFilesCreated = 0,
                GeneratedFiles = [],
                Warnings = [ex.ToString()]
            };
        }
    }

    public async Task<byte[]> GenerateZipArchiveAsync(ProjectPlanBlueprint blueprint, CancellationToken ct = default)
    {
        var files = BuildProjectFiles(blueprint);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true, Encoding.UTF8))
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                await writer.WriteAsync(file.Content);
            }
        }

        return memoryStream.ToArray();
    }

    public Task<string> GenerateArchitectureMarkdownAsync(ProjectPlanBlueprint blueprint, CancellationToken ct = default)
    {
        var safeName = SanitizeIdentifier(blueprint.ProjectName);
        return Task.FromResult(GenerateArchitectureMarkdownInternal(safeName, blueprint));
    }

    public Task<string> GenerateAgentsMarkdownAsync(ProjectPlanBlueprint blueprint, CancellationToken ct = default)
    {
        var safeName = SanitizeIdentifier(blueprint.ProjectName);
        return Task.FromResult(GenerateAgentsMarkdown(safeName, blueprint));
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }
        var result = sb.ToString();
        return string.IsNullOrEmpty(result) ? "AppCore" : result;
    }

    private static string GenerateSolutionFile(string safeName)
    {
        var domainGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var appGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var infraGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var apiGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var testsGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var csharpTypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";

        return $"""
        Microsoft Visual Studio Solution File, Format Version 12.00
        # Visual Studio Version 17
        VisualStudioVersion = 17.0.31903.59
        MinimumVisualStudioVersion = 10.0.40219.1
        Project("{csharpTypeGuid}") = "{safeName}.Domain", "src\{safeName}.Domain\{safeName}.Domain.csproj", "{domainGuid}"
        EndProject
        Project("{csharpTypeGuid}") = "{safeName}.Application", "src\{safeName}.Application\{safeName}.Application.csproj", "{appGuid}"
        EndProject
        Project("{csharpTypeGuid}") = "{safeName}.Infrastructure", "src\{safeName}.Infrastructure\{safeName}.Infrastructure.csproj", "{infraGuid}"
        EndProject
        Project("{csharpTypeGuid}") = "{safeName}.Api", "src\{safeName}.Api\{safeName}.Api.csproj", "{apiGuid}"
        EndProject
        Project("{csharpTypeGuid}") = "{safeName}.UnitTests", "tests\{safeName}.UnitTests\{safeName}.UnitTests.csproj", "{testsGuid}"
        EndProject
        Global
        	GlobalSection(SolutionConfigurationPlatforms) = preSolution
        		Debug|Any CPU = Debug|Any CPU
        		Release|Any CPU = Release|Any CPU
        	EndGlobalSection
        	GlobalSection(ProjectConfigurationPlatforms) = postSolution
        		{domainGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{domainGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{domainGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
        		{domainGuid}.Release|Any CPU.Build.0 = Release|Any CPU
        		{appGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{appGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{appGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
        		{appGuid}.Release|Any CPU.Build.0 = Release|Any CPU
        		{infraGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{infraGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{infraGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
        		{infraGuid}.Release|Any CPU.Build.0 = Release|Any CPU
        		{apiGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{apiGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{apiGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
        		{apiGuid}.Release|Any CPU.Build.0 = Release|Any CPU
        		{testsGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        		{testsGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
        		{testsGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
        		{testsGuid}.Release|Any CPU.Build.0 = Release|Any CPU
        	EndGlobalSection
        	GlobalSection(SolutionProperties) = preSolution
        		HideSolutionNode = FALSE
        	EndGlobalSection
        EndGlobal
        """;
    }

    private static string GenerateProjectFile(
        string sdk,
        string targetFramework,
        IReadOnlyList<string> projectReferences,
        IReadOnlyList<string>? packageReferences = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""<Project Sdk="{sdk}">""");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
        sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine("    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>");
        sb.AppendLine("  </PropertyGroup>");

        if (projectReferences.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var pref in projectReferences)
            {
                sb.AppendLine($"""    <ProjectReference Include="{pref}" />""");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        if (packageReferences is { Count: > 0 })
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var pkg in packageReferences)
            {
                var ver = ResolvePackageVersion(pkg);
                sb.AppendLine($"""    <PackageReference Include="{pkg}" Version="{ver}" />""");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");
        return sb.ToString();
    }

    private static string ResolvePackageVersion(string packageName)
    {
        return packageName switch
        {
            "Microsoft.EntityFrameworkCore" => "9.0.2",
            "Microsoft.EntityFrameworkCore.Sqlite" => "9.0.2",
            "Microsoft.EntityFrameworkCore.InMemory" => "9.0.2",
            "Microsoft.EntityFrameworkCore.Design" => "9.0.2",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "9.0.3",
            "StackExchange.Redis" => "2.8.24",
            "xunit" => "2.9.3",
            "xunit.runner.visualstudio" => "3.0.1",
            "FluentAssertions" => "7.0.0",
            "Microsoft.NET.Test.Sdk" => "17.12.0",
            "Microsoft.AspNetCore.OpenApi" => "9.0.2",
            "Swashbuckle.AspNetCore" => "6.6.2",
            _ => "9.*"
        };
    }

    private static string GenerateBaseEntityCode(string safeName)
    {
        return $$"""
        namespace {{safeName}}.Domain.Common;

        public abstract class BaseEntity
        {
            public Guid Id { get; protected set; } = Guid.NewGuid();
            public DateTime CreatedAtUtc { get; protected set; } = DateTime.UtcNow;
            public DateTime? UpdatedAtUtc { get; protected set; }

            public void MarkUpdated() => UpdatedAtUtc = DateTime.UtcNow;
        }
        """;
    }

    private static string GenerateSampleAggregateCode(string safeName)
    {
        return $$"""
        using {{safeName}}.Domain.Common;

        namespace {{safeName}}.Domain.Entities;

        public sealed class SampleAggregate : BaseEntity
        {
            public string Title { get; private set; }
            public string Status { get; private set; }

            public SampleAggregate(string title)
            {
                if (string.IsNullOrWhiteSpace(title))
                    throw new ArgumentException("Title cannot be empty.", nameof(title));

                Title = title.Trim();
                Status = "Active";
            }

            public void UpdateTitle(string newTitle)
            {
                if (string.IsNullOrWhiteSpace(newTitle))
                    throw new ArgumentException("Title cannot be empty.", nameof(newTitle));

                Title = newTitle.Trim();
                MarkUpdated();
            }

            public void Complete()
            {
                Status = "Completed";
                MarkUpdated();
            }
        }
        """;
    }

    private static string GenerateDbContextInterfaceCode(string safeName)
    {
        return $$"""
        using {{safeName}}.Domain.Entities;
        using Microsoft.EntityFrameworkCore;

        namespace {{safeName}}.Application.Common.Interfaces;

        public interface IApplicationDbContext
        {
            DbSet<SampleAggregate> SampleAggregates { get; }
            Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
        }
        """;
    }

    private static string GenerateDtoCode(string safeName)
    {
        return $$"""
        namespace {{safeName}}.Application.Features;

        public sealed record SampleAggregateDto(
            Guid Id,
            string Title,
            string Status,
            DateTime CreatedAtUtc,
            DateTime? UpdatedAtUtc
        );
        """;
    }

    private static string GenerateDbContextCode(string safeName)
    {
        return $$"""
        using {{safeName}}.Application.Common.Interfaces;
        using {{safeName}}.Domain.Entities;
        using Microsoft.EntityFrameworkCore;

        namespace {{safeName}}.Infrastructure.Persistence;

        public sealed class ApplicationDbContext : DbContext, IApplicationDbContext
        {
            public DbSet<SampleAggregate> SampleAggregates => Set<SampleAggregate>();

            public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);

                modelBuilder.Entity<SampleAggregate>(entity =>
                {
                    entity.HasKey(e => e.Id);
                    entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                    entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                    entity.Property(e => e.CreatedAtUtc).IsRequired();
                });
            }
        }
        """;
    }

    private static string GenerateInfrastructureDiCode(string safeName, string dbType)
    {
        var isPostgres = dbType.Contains("Postgres", StringComparison.OrdinalIgnoreCase) || dbType.Contains("Cockroach", StringComparison.OrdinalIgnoreCase);
        var isSqlite = dbType.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        var dbConfigSnippet = isPostgres
            ? "options.UseNpgsql(configuration.GetConnectionString(\"DefaultConnection\"));"
            : (isSqlite
                ? "options.UseSqlite(configuration.GetConnectionString(\"DefaultConnection\"));"
                : "options.UseInMemoryDatabase(\"AppDb\");");

        return $$"""
        using {{safeName}}.Application.Common.Interfaces;
        using {{safeName}}.Infrastructure.Persistence;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;

        namespace {{safeName}}.Infrastructure;

        public static class DependencyInjection
        {
            public static IServiceCollection AddInfrastructureServices(
                this IServiceCollection services,
                IConfiguration configuration)
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                {
                    {{dbConfigSnippet}}
                });

                services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

                return services;
            }
        }
        """;
    }

    private static string GenerateApiProgramCode(string safeName, ProjectPlanBlueprint blueprint)
    {
        return $$"""
        using {{safeName}}.Application.Common.Interfaces;
        using {{safeName}}.Domain.Entities;
        using {{safeName}}.Infrastructure;
        using Microsoft.EntityFrameworkCore;

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container
        builder.Services.AddOpenApi();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        var app = builder.Build();

        // Configure HTTP request pipeline
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        // System Health & Info
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "Healthy",
            project = "{{safeName}}",
            timestamp = DateTime.UtcNow
        })).WithName("HealthCheck");

        // Sample Aggregate Endpoints
        var sampleGroup = app.MapGroup("/api/sample");

        sampleGroup.MapGet("/", async (IApplicationDbContext db, CancellationToken ct) =>
        {
            var items = await db.SampleAggregates
                .AsNoTracking()
                .ToListAsync(ct);
            return Results.Ok(items);
        }).WithName("GetSampleItems");

        sampleGroup.MapPost("/", async (string title, IApplicationDbContext db, CancellationToken ct) =>
        {
            var item = new SampleAggregate(title);
            db.SampleAggregates.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sample/{item.Id}", item);
        }).WithName("CreateSampleItem");

        app.Run();
        """;
    }

    private static string GenerateAppSettingsJson(string safeName, string dbType)
    {
        var isPostgres = dbType.Contains("Postgres", StringComparison.OrdinalIgnoreCase) || dbType.Contains("Cockroach", StringComparison.OrdinalIgnoreCase);
        var isSqlite = dbType.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        var connStr = isPostgres
            ? "Host=localhost;Port=5432;Database=" + safeName.ToLowerInvariant() + "_db;Username=postgres;Password=postgres;"
            : (isSqlite ? $"Data Source={safeName.ToLowerInvariant()}.db;" : "Data Source=:memory:;");

        return $$"""
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.AspNetCore": "Warning"
            }
          },
          "ConnectionStrings": {
            "DefaultConnection": "{{connStr}}"
          },
          "AllowedHosts": "*"
        }
        """;
    }

    private static string GenerateSampleUnitTestCode(string safeName)
    {
        return $$"""
        using {{safeName}}.Domain.Entities;
        using FluentAssertions;
        using Xunit;

        namespace {{safeName}}.UnitTests;

        public class DomainEntityTests
        {
            [Fact]
            public void Should_Create_SampleAggregate_With_Active_Status()
            {
                // Arrange & Act
                var aggregate = new SampleAggregate("Test Title");

                // Assert
                aggregate.Title.Should().Be("Test Title");
                aggregate.Status.Should().Be("Active");
                aggregate.Id.Should().NotBeEmpty();
                aggregate.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
            }

            [Fact]
            public void Should_Throw_When_Title_Is_Empty()
            {
                // Act
                var act = () => new SampleAggregate("");

                // Assert
                act.Should().Throw<ArgumentException>();
            }

            [Fact]
            public void Should_Complete_Aggregate()
            {
                // Arrange
                var aggregate = new SampleAggregate("Valid Title");

                // Act
                aggregate.Complete();

                // Assert
                aggregate.Status.Should().Be("Completed");
                aggregate.UpdatedAtUtc.Should().NotBeNull();
            }
        }
        """;
    }

    private static string GenerateDockerCompose(string safeName, ProjectPlanBlueprint blueprint)
    {
        var dbType = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL");
        var isPostgres = dbType.Contains("Postgres", StringComparison.OrdinalIgnoreCase);
        var isCockroach = dbType.Contains("Cockroach", StringComparison.OrdinalIgnoreCase);
        var hasRedis = blueprint.TechStack.Values.Any(v => v.Contains("Redis", StringComparison.OrdinalIgnoreCase));
        var hasKafka = blueprint.TechStack.Values.Any(v => v.Contains("Kafka", StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        sb.AppendLine("services:");

        if (isCockroach)
        {
            sb.AppendLine($"""
              cockroachdb:
                image: cockroachdb/cockroach:v23.2.0
                container_name: {safeName.ToLowerInvariant()}-cockroachdb
                command: start-single-node --insecure
                ports:
                  - "26257:26257"
                  - "8080:8080"
                volumes:
                  - cockroach_data:/cockroach/cockroach-data
            """);
        }
        else if (isPostgres || !dbType.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"""
              postgres:
                image: postgres:16-alpine
                container_name: {safeName.ToLowerInvariant()}-postgres
                environment:
                  POSTGRES_DB: {safeName.ToLowerInvariant()}_db
                  POSTGRES_USER: postgres
                  POSTGRES_PASSWORD: postgres
                ports:
                  - "5432:5432"
                volumes:
                  - postgres_data:/var/lib/postgresql/data
                healthcheck:
                  test: ["CMD-SHELL", "pg_isready -U postgres"]
                  interval: 5s
                  timeout: 5s
                  retries: 5
            """);
        }

        if (hasRedis)
        {
            sb.AppendLine($"""
              redis:
                image: redis:7-alpine
                container_name: {safeName.ToLowerInvariant()}-redis
                ports:
                  - "6379:6379"
                volumes:
                  - redis_data:/data
            """);
        }

        if (hasKafka)
        {
            sb.AppendLine($"""
              kafka:
                image: confluentinc/cp-kafka:7.5.0
                container_name: {safeName.ToLowerInvariant()}-kafka
                ports:
                  - "9092:9092"
                environment:
                  KAFKA_NODE_ID: 1
                  KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: 'CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT'
                  KAFKA_ADVERTISED_LISTENERS: 'PLAINTEXT://localhost:9092'
                  KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
                  KAFKA_PROCESS_ROLES: 'broker,controller'
                  KAFKA_CONTROLLER_QUORUM_VOTERS: '1@localhost:9093'
                  KAFKA_LISTENERS: 'PLAINTEXT://0.0.0.0:9092,CONTROLLER://0.0.0.0:9093'
                  KAFKA_CONTROLLER_LISTENER_NAMES: 'CONTROLLER'
                  CLUSTER_ID: '4L622nShTWWkrFuMmISBLg'
            """);
        }

        sb.AppendLine($"""
          api:
            build:
              context: .
              dockerfile: Dockerfile
            container_name: {safeName.ToLowerInvariant()}-api
            ports:
              - "5000:8080"
            environment:
              - ASPNETCORE_ENVIRONMENT=Development
        """);

        sb.AppendLine();
        sb.AppendLine("volumes:");
        if (isCockroach) sb.AppendLine("  cockroach_data:");
        else if (isPostgres || !dbType.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) sb.AppendLine("  postgres_data:");
        if (hasRedis) sb.AppendLine("  redis_data:");

        return sb.ToString();
    }

    private static string GenerateDockerfile(string safeName)
    {
        return $"""
        FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
        WORKDIR /src

        COPY ["src/{safeName}.Domain/{safeName}.Domain.csproj", "src/{safeName}.Domain/"]
        COPY ["src/{safeName}.Application/{safeName}.Application.csproj", "src/{safeName}.Application/"]
        COPY ["src/{safeName}.Infrastructure/{safeName}.Infrastructure.csproj", "src/{safeName}.Infrastructure/"]
        COPY ["src/{safeName}.Api/{safeName}.Api.csproj", "src/{safeName}.Api/"]

        RUN dotnet restore "src/{safeName}.Api/{safeName}.Api.csproj"

        COPY . .
        WORKDIR "/src/src/{safeName}.Api"
        RUN dotnet publish "{safeName}.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

        FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
        WORKDIR /app
        EXPOSE 8080
        COPY --from=build /app/publish .
        ENTRYPOINT ["dotnet", "{safeName}.Api.dll"]
        """;
    }

    private static string GenerateGitIgnore()
    {
        return """
        ## Build & Artifacts
        [Bb]in/
        [Oo]bj/
        out/
        dist/
        .vs/
        .vscode/
        .idea/
        *.user
        *.suo
        *.userosscache
        *.sln.docstates

        ## Packages
        *.nupkg
        *.snupkg
        packages/

        ## OS & Temp
        .DS_Store
        Thumbs.db
        *.log
        *.tmp

        ## Environment & Secrets
        appsettings.Development.json
        appsettings.Local.json
        .env
        """;
    }

    private static string GenerateEditorConfig()
    {
        return """
        root = true

        [*]
        indent_style = space
        indent_size = 4
        end_of_line = lf
        charset = utf-8
        trim_trailing_whitespace = true
        insert_final_newline = true

        [*.cs]
        csharp_style_var_for_built_in_types = true:suggestion
        csharp_style_var_when_type_is_apparent = true:suggestion
        dotnet_style_qualification_for_field = false:suggestion
        dotnet_style_qualification_for_property = false:suggestion
        dotnet_style_require_accessibility_modifiers = for_non_interface_members:error
        csharp_prefer_braces = true:suggestion
        csharp_style_namespace_declarations = file_scoped:warning

        [*.{json,yaml,yml,xml}]
        indent_size = 2
        """;
    }

    private static string GenerateReadmeMarkdown(string safeName, ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {safeName}");
        sb.AppendLine();
        sb.AppendLine($"> {blueprint.ExecutiveSummary}");
        sb.AppendLine();
        sb.AppendLine("## Stack Tecnológico");
        sb.AppendLine();
        foreach (var (k, v) in blueprint.TechStack)
        {
            sb.AppendLine($"- **{k}**: {v}");
        }
        sb.AppendLine();
        sb.AppendLine("## Prerrequisitos");
        sb.AppendLine("- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)");
        sb.AppendLine("- [Docker Desktop](https://www.docker.com/products/docker-desktop/)");
        sb.AppendLine();
        sb.AppendLine("## Inicio Rápido");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("# 1. Levantar servicios de infraestructura en Docker");
        sb.AppendLine("docker compose up -d");
        sb.AppendLine();
        sb.AppendLine("# 2. Restaurar dependencias y compilar solución");
        sb.AppendLine($"dotnet restore {safeName}.sln");
        sb.AppendLine($"dotnet build {safeName}.sln");
        sb.AppendLine();
        sb.AppendLine("# 3. Ejecutar pruebas unitarias");
        sb.AppendLine("dotnet test --nologo");
        sb.AppendLine();
        sb.AppendLine("# 4. Iniciar la API Web");
        sb.AppendLine($"dotnet run --project src/{safeName}.Api/{safeName}.Api.csproj");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Arquitectura");
        sb.AppendLine($"Consulta la documentación técnica detallada en [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) y los ADRs en [`docs/adrs/`](docs/adrs/).");

        return sb.ToString();
    }

    private static string GenerateArchitectureMarkdownInternal(string safeName, ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Arquitectura de Sistema: {safeName}");
        sb.AppendLine();
        sb.AppendLine("## Resumen Ejecutivo");
        sb.AppendLine(blueprint.ExecutiveSummary);
        sb.AppendLine();
        sb.AppendLine("## Justificación Arquitectónica");
        sb.AppendLine(blueprint.ArchitecturalRationale);
        sb.AppendLine();
        sb.AppendLine("## Diagrama C4 (Nivel Contenedor)");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine(blueprint.C4DiagramMermaid);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Convenciones y Estándares Clave");
        foreach (var conv in blueprint.KeyConventions)
        {
            sb.AppendLine($"- {conv}");
        }

        return sb.ToString();
    }

    private static string GenerateAdrMarkdown(string safeName, ProjectPlanBlueprint blueprint)
    {
        return $"""
        # ADR-001: {blueprint.InitialAdrTitle}

        ## Estado
        Aceptado (Accepted)

        ## Fecha
        {DateTime.UtcNow:yyyy-MM-dd}

        ## Contexto
        {blueprint.ExecutiveSummary}

        ## Decisión
        {blueprint.InitialAdrContent}

        ## Consecuencias
        - **Positivas**: Estructura modular desacoplada, alta testabilidad unitaria y de integración, portabilidad de base de datos con EF Core 9.
        - **Trade-offs**: Requiere disciplina de mapeo entre capas (Domain Entities vs DTOs) y configuración explícita de inyección de dependencias.
        """;
    }

    private static string GenerateDefaultTokensCss()
    {
        return """
        :root {
          /* Surface & Background */
          --color-bg: #0d0f12;
          --color-surface: #14171d;
          --color-surface-hover: #1c212a;
          --color-border: #242b35;

          /* Typography Colors */
          --color-text-primary: #f0f3f6;
          --color-text-secondary: #8c97a8;
          --color-text-muted: #576171;

          /* Semantics */
          --color-accent: #3b82f6;
          --color-success: #10b981;
          --color-warning: #f59e0b;
          --color-danger: #ef4444;

          /* Fonts & Spacing */
          --font-sans: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
          --font-mono: 'JetBrains Mono', monospace;
          --space-unit: 8px;
          --radius-base: 6px;
        }
        """;
    }

    private static string GenerateAgentsMarkdown(string safeName, ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Guía de Agentes y Reglas de Desarrollo: {safeName}");
        sb.AppendLine();
        sb.AppendLine("> Documento de directrices para Google Antigravity, agentes de codificación e ingenieros de software.");
        sb.AppendLine();
        sb.AppendLine("## Misión del Sistema");
        sb.AppendLine(blueprint.ExecutiveSummary);
        sb.AppendLine();
        sb.AppendLine("## Aislamiento de Capas y Reglas Arquitectónicas");
        sb.AppendLine("Este proyecto implementa Clean Architecture sobre .NET 9 con un flujo de dependencias unidireccional estricto:");
        sb.AppendLine();
        sb.AppendLine($"1. **`src/{safeName}.Domain` (Capa de Dominio)**:");
        sb.AppendLine("   - Pureza absoluta: Cero dependencias externas hacia frameworks, Entity Framework Core o ASP.NET.");
        sb.AppendLine("   - Contiene: Entidades, Value Objects, Aggregate Roots, Excepciones de Dominio y Eventos de Dominio.");
        sb.AppendLine("   - No usar decoradores de persistencia ni anotaciones de base de datos en las entidades.");
        sb.AppendLine();
        sb.AppendLine($"2. **`src/{safeName}.Application` (Capa de Aplicación)**:");
        sb.AppendLine("   - Depende únicamente de `Domain`.");
        sb.AppendLine("   - Contiene: Casos de uso (Commands/Queries), DTOs, Mapeos, Interfaces de Abstracción (`IApplicationDbContext`, servicios de notificación, repositorios) y validaciones con FluentValidation.");
        sb.AppendLine();
        sb.AppendLine($"3. **`src/{safeName}.Infrastructure` (Capa de Infraestructura)**:");
        sb.AppendLine("   - Depende de `Application` y `Domain`.");
        sb.AppendLine("   - Implementa los accesos a datos (EF Core DbContext, Fluent API Configurations, Migraciones), clientes Redis, colas de mensajería y servicios externos.");
        sb.AppendLine();
        sb.AppendLine($"4. **`src/{safeName}.Api` (Capa de Presentación / Entrada)**:");
        sb.AppendLine("   - Punto de entrada de la aplicación. Configura Inyección de Dependencias, Middlewares, Endpoints (Minimal APIs o Controladores) y Swagger/OpenAPI.");
        sb.AppendLine();
        sb.AppendLine($"5. **`tests/{safeName}.UnitTests` (Pruebas Unitarias)**:");
        sb.AppendLine("   - Pruebas rápidas y aisladas con xUnit y FluentAssertions sobre lógica de Dominio y Aplicación.");
        sb.AppendLine();
        sb.AppendLine("## Estándares de Codificación (C# 13 / .NET 9)");
        sb.AppendLine("- **C# 13 Idiomático**: Usar primary constructors, collection expressions (`[...]`), file-scoped namespaces y tipos nullable (`#nullable enable`).");
        sb.AppendLine("- **Manejo de Errores**: Preferir Result pattern (`Result<T>`) para flujos de negocio previstos en lugar de lanzar excepciones.");
        sb.AppendLine("- **Asincronía Total**: Todas las operaciones de I/O deben ser asíncronas con sufijo `Async` y recibir `CancellationToken ct = default`.");
        sb.AppendLine("- **Cero Emojis**: Prohibido el uso de emojis en código fuente, logs de consola, mensajes de commit o documentación técnica.");
        sb.AppendLine();
        sb.AppendLine("## Comandos de Verificación para Agentes CLI");
        sb.AppendLine("```bash");
        sb.AppendLine($"# Compilar la solución completa");
        sb.AppendLine($"dotnet build {safeName}.sln");
        sb.AppendLine();
        sb.AppendLine($"# Ejecutar suite de pruebas unitarias");
        sb.AppendLine($"dotnet test --nologo");
        sb.AppendLine();
        sb.AppendLine($"# Levantar servicios de base de datos y dependencias en Docker");
        sb.AppendLine($"docker compose up -d");
        sb.AppendLine();
        sb.AppendLine($"# Ejecutar la API en modo desarrollo");
        sb.AppendLine($"dotnet run --project src/{safeName}.Api/{safeName}.Api.csproj");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Stack Tecnológico Aprobado");
        foreach (var (k, v) in blueprint.TechStack)
        {
            sb.AppendLine($"- **{k}**: {v}");
        }
        sb.AppendLine();
        sb.AppendLine("## Convenciones y Criterios de Calidad (ISO/IEC 25010)");
        foreach (var conv in blueprint.KeyConventions)
        {
            sb.AppendLine($"- {conv}");
        }
        sb.AppendLine();
        sb.AppendLine("## Control de Cambios y ADRs");
        sb.AppendLine("- Cualquier cambio estructural en el esquema de base de datos o arquitectura debe documentarse en `docs/adrs/`.");
        sb.AppendLine("- Los contratos de API deben ser retrocompatibles o versionados bajo `/api/v1/`.");

        return sb.ToString();
    }
}
