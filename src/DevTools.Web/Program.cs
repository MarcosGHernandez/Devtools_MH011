using DevTools.Core.Interfaces;
using DevTools.Core.Models;
using DevTools.Data;
using DevTools.Data.Repositories;
using DevTools.Orchestrator.Services;
using DevTools.Orchestrator.Skills;
using DevTools.Toolkit.Engine.Loaders;

var builder = WebApplication.CreateBuilder(args);

var toolkitDir = ResolveToolkitDir();
var loader = new FileToolkitLoader();
var config = LoadConfiguration();
string? activeProviderOverride = null;
string? activeModelOverride = null;

builder.Services.AddSingleton<IToolkitLoader>(loader);
builder.Services.AddSingleton<ISkillExecutor, LocalGitSkillExecutor>();
builder.Services.AddScoped<IAgentOrchestrator>(sp =>
    new DefaultAgentOrchestrator(
        loader,
        toolkitDir,
        chatClient: DevTools.Orchestrator.Factories.ChatClientFactory.CreateClient(config, activeProviderOverride, activeModelOverride),
        skillExecutors: sp.GetServices<ISkillExecutor>()
    ));

builder.Services.AddHttpClient();
builder.Services.AddScoped(_ => DbInitializer.CreateDbContext());
builder.Services.AddScoped<IProjectRepository, SqliteProjectRepository>();
builder.Services.AddScoped<IKnowledgeRepository, SqliteKnowledgeRepository>();
builder.Services.AddScoped<IDocumentationRepository, SqliteDocumentationRepository>();
builder.Services.AddSingleton<IProjectScaffoldingService, ProjectScaffoldingService>();
builder.Services.AddScoped(sp =>
    new ProjectPlanningService(
        sp.GetRequiredService<IProjectRepository>(),
        sp.GetRequiredService<IDocumentationRepository>(),
        sp.GetRequiredService<IKnowledgeRepository>(),
        DevTools.Orchestrator.Factories.ChatClientFactory.CreateClient(config, activeProviderOverride, activeModelOverride),
        sp.GetRequiredService<IProjectScaffoldingService>()
    ));

builder.Services.AddSingleton<IContinuousImprovementService>(_ =>
    new HermesContinuousImprovementService(
        chatClientAccessor: () => DevTools.Orchestrator.Factories.ChatClientFactory.CreateClient(config, activeProviderOverride, activeModelOverride),
        config: config
    ));

var app = builder.Build();

// Ensure DB schema on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DevToolsDbContext>();
    await DbInitializer.InitializeAsync(db);
}

// 1. Dashboard View
app.MapGet("/", () => Results.Content(GetClassicMinimalistHtmlDashboard(), "text/html"));

// 2. Toolkit Endpoints
app.MapGet("/api/toolkit/manifest", async (IToolkitLoader tl) =>
{
    var manifest = await tl.LoadManifestAsync(toolkitDir);
    return Results.Ok(manifest);
});

app.MapGet("/api/toolkit/prompts", async (IToolkitLoader tl) =>
{
    var prompts = await tl.LoadAllPromptsAsync(toolkitDir);
    return Results.Ok(prompts.Select(p => new
    {
        p.Id,
        p.Name,
        p.Description,
        HasRules = !string.IsNullOrEmpty(p.RulesContent),
        HasSchema = !string.IsNullOrEmpty(p.OutputSchemaContent)
    }));
});

app.MapGet("/api/toolkit/skills", async (IToolkitLoader tl) =>
{
    var skills = await tl.LoadAllSkillsAsync(toolkitDir);
    return Results.Ok(skills.Select(s => new
    {
        s.Id,
        s.Name,
        s.Description
    }));
});

// 3. Project & Knowledge Endpoints
app.MapGet("/api/projects", async (IProjectRepository repo) =>
{
    var projects = await repo.ListProjectsAsync();
    return Results.Ok(projects);
});

app.MapPost("/api/projects", async (IProjectRepository repo, RegisterProjectPayload payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.Name)) return Results.BadRequest("Project name is required.");
    var path = payload.RootPath ?? Path.Combine(Directory.GetCurrentDirectory(), payload.Name.ToLowerInvariant().Replace(" ", "-"));
    var project = await repo.RegisterProjectAsync(payload.Name, path);
    return Results.Ok(project);
});

app.MapGet("/api/knowledge", async (IKnowledgeRepository repo) =>
{
    var items = await repo.ListKnowledgeAsync();
    return Results.Ok(items);
});

app.MapPost("/api/knowledge", async (IKnowledgeRepository repo, AddKnowledgePayload payload) =>
{
    var item = await repo.AddKnowledgeAsync(payload.Title, payload.Domain, payload.Content, payload.Tags, payload.Source);
    return Results.Ok(item);
});

app.MapGet("/api/documents", async (IDocumentationRepository repo) =>
{
    var docs = await repo.ListDocumentsAsync();
    return Results.Ok(docs);
});

// 4. Planning & Project-Separated Conversation Endpoints
app.MapGet("/api/planning/projects", async (ProjectPlanningService planner) =>
{
    var summaries = await planner.GetProjectsSummaryAsync();
    return Results.Ok(summaries);
});

app.MapGet("/api/planning/projects/{id}/messages", async (ProjectPlanningService planner, string id) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var messages = await planner.GetProjectMessagesAsync(guid);
    return Results.Ok(messages);
});

app.MapDelete("/api/planning/projects/{id}", async (ProjectPlanningService planner, string id) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var deleted = await planner.DeleteProjectAsync(guid);
    return Results.Ok(new { success = deleted });
});

app.MapPut("/api/planning/projects/{id}", async (ProjectPlanningService planner, string id, UpdateProjectPayload payload) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var updated = await planner.UpdateProjectDetailsAsync(guid, payload.Name, payload.RootPath, payload.Description);
    return updated is not null ? Results.Ok(updated) : Results.NotFound();
});

app.MapDelete("/api/planning/projects/{id}/messages", async (ProjectPlanningService planner, string id) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var cleared = await planner.ClearProjectChatAsync(guid);
    return Results.Ok(new { success = cleared });
});

app.MapDelete("/api/documents/{id}", async (ProjectPlanningService planner, string id) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid document ID.");
    var deleted = await planner.DeleteDocumentAsync(guid);
    return Results.Ok(new { success = deleted });
});

app.MapPut("/api/documents/{id}", async (ProjectPlanningService planner, string id, UpdateDocumentPayload payload) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid document ID.");
    var updated = await planner.UpdateDocumentAsync(guid, payload.Title, payload.MarkdownContent, payload.Version ?? "1.0.0");
    return updated is not null ? Results.Ok(updated) : Results.NotFound();
});

app.MapDelete("/api/knowledge/{id}", async (ProjectPlanningService planner, string id) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid knowledge ID.");
    var deleted = await planner.DeleteKnowledgeAsync(guid);
    return Results.Ok(new { success = deleted });
});

app.MapPost("/api/planning/blueprint", async (ProjectPlanningService planner, ProjectInterviewAnswers answers) =>
{
    var blueprint = await planner.SynthesizePlanAsync(answers, persistToDatabase: true);
    return Results.Ok(blueprint);
});

app.MapPost("/api/planning/chat", async (ProjectPlanningService planner, PlanningChatRequest request) =>
{
    var response = await planner.ProcessPlanningChatAsync(request);
    return Results.Ok(response);
});

app.MapPost("/api/planning/chat/stream", async (ProjectPlanningService planner, PlanningChatRequest request, HttpContext context, CancellationToken ct) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    try
    {
        await foreach (var chunk in planner.StreamPlanningChatAsync(request, ct))
        {
            if (ct.IsCancellationRequested) break;
            var json = System.Text.Json.JsonSerializer.Serialize(chunk);
            await context.Response.WriteAsync($"data: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Conexión cerrada por el cliente sin lanzar error de servidor
    }
});

app.MapPost("/api/planning/projects/{id}/scaffold", async (ProjectPlanningService planner, IProjectRepository repo, string id, ProjectScaffoldingRequest req, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "Scaffolding generation",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var targetPath = string.IsNullOrWhiteSpace(req.TargetPath)
        ? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "scaffolding", project.Name.Replace(" ", "-").ToLowerInvariant())
        : req.TargetPath;

    var result = await planner.ScaffoldingService.GenerateOnDiskAsync(bp, targetPath, ct);
    return Results.Ok(result);
});

app.MapGet("/api/planning/projects/{id}/export-zip", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "ZIP export",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var zipBytes = await planner.ScaffoldingService.GenerateZipArchiveAsync(bp, ct);
    var safeName = project.Name.Replace(" ", "-").ToLowerInvariant();
    return Results.File(zipBytes, "application/zip", $"{safeName}-solution.zip");
});

app.MapGet("/api/planning/projects/{id}/export-docs", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "Documentation export",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var markdown = await planner.ScaffoldingService.GenerateArchitectureMarkdownAsync(bp, ct);
    var safeName = project.Name.Replace(" ", "-").ToLowerInvariant();
    return Results.File(System.Text.Encoding.UTF8.GetBytes(markdown), "text/markdown", $"{safeName}-ARCHITECTURE.md");
});

app.MapGet("/api/planning/projects/{id}/export-agents", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "AGENTS.md export",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var markdown = await planner.ScaffoldingService.GenerateAgentsMarkdownAsync(bp, ct);
    var safeName = project.Name.Replace(" ", "-").ToLowerInvariant();
    return Results.File(System.Text.Encoding.UTF8.GetBytes(markdown), "text/markdown", $"{safeName}-AGENTS.md");
});

app.MapGet("/api/planning/projects/{id}/speckit", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "Spec Kit generation",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    bp.SpecKit ??= SpecKitDocumentationGenerator.GenerateSpecKit(bp);
    return Results.Ok(bp.SpecKit);
});

app.MapGet("/api/planning/projects/{id}/prd", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "PRD generation",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var prd = bp.PrdMarkdown ?? SpecKitDocumentationGenerator.GeneratePrd(bp);
    return Results.Ok(new { prdMarkdown = prd });
});

app.MapGet("/api/planning/projects/{id}/suggestions", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "Suggestions generation",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var suggestions = bp.SuggestionsMarkdown ?? SpecKitDocumentationGenerator.GenerateSuggestionsAndRoadmap(bp);
    return Results.Ok(new { suggestionsMarkdown = suggestions });
});

app.MapGet("/api/planning/projects/{id}/export-speckit", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "Spec Kit ZIP export",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var zipBytes = await planner.ScaffoldingService.GenerateSpecKitZipAsync(bp, ct);
    var safeName = project.Name.Replace(" ", "-").ToLowerInvariant();
    return Results.File(zipBytes, "application/zip", $"{safeName}-spec-kit.zip");
});

app.MapGet("/api/planning/projects/{id}/export-prd", async (ProjectPlanningService planner, IProjectRepository repo, string id, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var guid)) return Results.BadRequest("Invalid project ID.");
    var project = await repo.GetProjectByIdAsync(guid, ct);
    if (project is null) return Results.NotFound("Project not found.");

    var bp = ProjectPlanningService.TryGetBlueprint(project.LatestBlueprintJson);
    if (bp is null)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = project.Name,
            Description = project.Description ?? "PRD Markdown export",
            ArchitecturalStyle = project.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project.DatabaseType ?? "PostgreSQL + EF Core 9"
        };
        bp = await planner.SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: ct);
    }

    var prdMarkdown = await planner.ScaffoldingService.GeneratePrdMarkdownAsync(bp, ct);
    var safeName = project.Name.Replace(" ", "-").ToLowerInvariant();
    return Results.File(System.Text.Encoding.UTF8.GetBytes(prdMarkdown), "text/markdown", $"{safeName}-PRD.md");
});

app.MapPost("/api/planning/scaffold", async (ProjectPlanningService planner, ScaffoldPayload payload) =>
{
    var targetPath = string.IsNullOrWhiteSpace(payload.TargetPath)
        ? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "scaffolding", payload.Blueprint.ProjectName.Replace(" ", "-").ToLowerInvariant())
        : payload.TargetPath;

    var result = await planner.ScaffoldingService.GenerateOnDiskAsync(payload.Blueprint, targetPath);
    return Results.Ok(result);
});

// 5. Code Reviewer
app.MapPost("/api/agents/review", async (IAgentOrchestrator orchestrator, ReviewPayload payload) =>
{
    var response = await orchestrator.ExecuteAsync(new AgentRequest
    {
        AgentId = "web-reviewer",
        PromptId = "code-review.system",
        TargetFilePath = payload.FileName ?? "CodeSnippet.cs",
        CodeContent = payload.Code ?? string.Empty
    });

    return Results.Ok(response);
});

// 6. AI Engine Status & Model Switching
app.MapGet("/api/ai/status", async () =>
{
    var ollamaEndpoint = config.Ai.Providers.TryGetValue("ollama", out var s) ? s.Endpoint : "http://localhost:11434/v1";
    var preferred = activeModelOverride ?? (config.Ai.Providers.TryGetValue("ollama", out var s2) ? s2.ModelId : "qwen2.5-coder:7b");
    var status = await DevTools.Orchestrator.Factories.ChatClientFactory.GetOllamaStatusAsync(ollamaEndpoint, preferred);

    return Results.Ok(new
    {
        provider = activeProviderOverride ?? DevTools.Orchestrator.Factories.ChatClientFactory.CurrentProvider,
        activeModel = activeModelOverride ?? DevTools.Orchestrator.Factories.ChatClientFactory.CurrentResolvedModel ?? status.ActiveModel,
        isOllamaOnline = status.IsOnline,
        installedModels = status.InstalledModels,
        preferredModel = status.PreferredModel
    });
});

app.MapPost("/api/ai/switch-model", (SwitchModelPayload payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.Model)) return Results.BadRequest("Model name required.");

    if (string.Equals(payload.Model, "offline", StringComparison.OrdinalIgnoreCase))
    {
        activeProviderOverride = "offline";
        activeModelOverride = null;
    }
    else
    {
        activeProviderOverride = "ollama";
        activeModelOverride = payload.Model;
    }

    DevTools.Orchestrator.Factories.ChatClientFactory.CreateClient(config, activeProviderOverride, activeModelOverride);

    return Results.Ok(new
    {
        success = true,
        provider = activeProviderOverride ?? "ollama",
        model = activeModelOverride ?? DevTools.Orchestrator.Factories.ChatClientFactory.CurrentResolvedModel
    });
});

app.MapPost("/api/ai/pull-model", async (SwitchModelPayload payload, IHttpClientFactory httpClientFactory) =>
{
    var modelName = string.IsNullOrWhiteSpace(payload.Model) ? "hermes3:8b" : payload.Model.Trim();
    var ollamaEndpoint = (config.Ai?.Providers != null && config.Ai.Providers.TryGetValue("ollama", out var s) && !string.IsNullOrEmpty(s.Endpoint))
        ? s.Endpoint
        : "http://localhost:11434/v1";
    var baseUri = ollamaEndpoint.TrimEnd('/');
    if (baseUri.EndsWith("/v1")) baseUri = baseUri[..^3];

    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromMinutes(15); // Large models take time to download

    try
    {
        var pullPayload = new { model = modelName, stream = false };
        var resp = await client.PostAsJsonAsync($"{baseUri}/api/pull", pullPayload);
        if (resp.IsSuccessStatusCode)
        {
            activeProviderOverride = "ollama";
            activeModelOverride = modelName;
            DevTools.Orchestrator.Factories.ChatClientFactory.CreateClient(config, activeProviderOverride, activeModelOverride);

            return Results.Ok(new
            {
                success = true,
                message = $"Modelo '{modelName}' descargado e inicializado exitosamente en Ollama.",
                model = modelName
            });
        }

        var err = await resp.Content.ReadAsStringAsync();
        return Results.BadRequest(new { success = false, message = $"Error de Ollama al descargar {modelName}: {err}" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Fallo de conexión al descargar {modelName}: {ex.Message}");
    }
});

// 10. Continuous Improvement & Hermes Self-Audit Endpoints
app.MapGet("/api/improvement/metrics", async (IContinuousImprovementService svc) =>
{
    var metrics = await svc.GatherMetricsAsync(ResolveSolutionDir());
    return Results.Ok(metrics);
});

app.MapPost("/api/improvement/audit", async (IContinuousImprovementService svc, HttpContext ctx) =>
{
    string? model = null;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("model", out var modelProp))
            {
                model = modelProp.GetString();
            }
        }
    }
    catch { }

    var report = await svc.AuditProjectAsync(ResolveSolutionDir(), model, ctx.RequestAborted);
    return Results.Ok(report);
});

app.MapGet("/api/improvement/latest", async (IContinuousImprovementService svc) =>
{
    var report = await svc.GetLatestReportAsync();
    return Results.Ok(report);
});

app.MapPost("/api/improvement/proposals/{id}/status", async (string id, IContinuousImprovementService svc, HttpContext ctx) =>
{
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("status", out var stProp) &&
            Enum.TryParse<ProposalStatus>(stProp.GetString(), true, out var status))
        {
            var ok = await svc.UpdateProposalStatusAsync(id, status);
            return Results.Ok(new { success = ok });
        }
    }
    catch { }
    return Results.BadRequest(new { error = "Invalid status payload" });
});

app.Run();

static string ResolveSolutionDir() => DevTools.Core.Common.SolutionPathResolver.FindSolutionRoot();

static string ResolveToolkitDir() => DevTools.Core.Common.SolutionPathResolver.FindToolkitDirectory();

static DevTools.Core.Configuration.DevToolsConfig LoadConfiguration()
{
    var configFile = DevTools.Core.Common.SolutionPathResolver.FindConfigFile();
    if (configFile is not null && File.Exists(configFile))
    {
        try
        {
            var json = File.ReadAllText(configFile);
            var parsed = System.Text.Json.JsonSerializer.Deserialize<DevTools.Core.Configuration.DevToolsConfig>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is not null)
            {
                return parsed.Normalize(Path.GetDirectoryName(configFile));
            }
        }
        catch { }
    }

    return new DevTools.Core.Configuration.DevToolsConfig().Normalize();
}

static string GetClassicMinimalistHtmlDashboard()
{
    return """
    <!DOCTYPE html>
    <html lang="es">
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>AI DevTools // Architecture & Planning Studio</title>
        <link rel="preconnect" href="https://fonts.googleapis.com">
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
        <link href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap" rel="stylesheet">
        <!-- Mermaid.js for C4 diagrams -->
        <script src="https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"></script>
        <!-- PDF.js for client-side PDF document parsing -->
        <script src="https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js"></script>
        <!-- Mammoth.js for client-side Word DOCX document parsing -->
        <script src="https://cdnjs.cloudflare.com/ajax/libs/mammoth/1.6.0/mammoth.browser.min.js"></script>
        <style>
            :root {
                --bg: #0c0e12;
                --surface: #13171f;
                --surface-hover: #191f2a;
                --surface-active: #202735;
                --border: #232a35;
                --border-subtle: #181d26;
                --border-focus: #3d4757;
                --text-primary: #f0f3f6;
                --text-secondary: #8b96a5;
                --text-muted: #556070;
                --accent-blue: #3b82f6;
                --accent-blue-subtle: rgba(59, 130, 246, 0.12);
                --accent-green: #10b981;
                --accent-green-subtle: rgba(16, 185, 129, 0.12);
                --accent-amber: #f59e0b;
                --accent-red: #ef4444;
            }
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
                background: var(--bg);
                color: var(--text-primary);
                font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
                font-size: 13px;
                line-height: 1.5;
                -webkit-font-smoothing: antialiased;
                overflow-x: hidden;
            }
            .app-layout {
                display: flex;
                flex-direction: column;
                min-height: 100vh;
                width: 100%;
                overflow-x: hidden;
            }
            header {
                background: var(--surface);
                border-bottom: 1px solid var(--border);
                padding: 12px 24px;
                display: flex;
                justify-content: space-between;
                align-items: center;
                position: sticky;
                top: 0;
                z-index: 50;
            }
            .brand {
                display: flex;
                align-items: center;
                gap: 12px;
            }
            .brand-badge {
                background: var(--accent-blue-subtle);
                border: 1px solid rgba(59, 130, 246, 0.25);
                color: var(--accent-blue);
                font-family: 'JetBrains Mono', monospace;
                font-size: 11px;
                padding: 2px 7px;
                border-radius: 4px;
                font-weight: 500;
            }
            .nav-tabs {
                display: flex;
                gap: 4px;
                background: var(--bg);
                padding: 3px;
                border-radius: 6px;
                border: 1px solid var(--border);
            }
            .engine-status-badge {
                display: flex;
                align-items: center;
                gap: 8px;
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 4px 10px;
                font-size: 11px;
            }
            .engine-status-dot {
                width: 7px;
                height: 7px;
                border-radius: 50%;
                background: var(--accent-green);
                box-shadow: 0 0 6px var(--accent-green);
                flex-shrink: 0;
            }
            .engine-status-dot.offline {
                background: var(--text-muted);
                box-shadow: none;
            }
            .engine-select {
                background: transparent;
                border: none;
                color: var(--text-primary);
                font-size: 11px;
                font-family: 'JetBrains Mono', monospace;
                outline: none;
                cursor: pointer;
            }
            .engine-select option {
                background: #14171d;
                color: #f0f3f6;
            }
            @keyframes pulse {
                0%, 100% { opacity: 1; }
                50% { opacity: 0.35; }
            }
            .tab-btn {
                background: transparent;
                border: none;
                color: var(--text-secondary);
                padding: 6px 12px;
                font-size: 12px;
                font-weight: 500;
                border-radius: 4px;
                cursor: pointer;
                transition: all 120ms ease;
                display: flex;
                align-items: center;
                gap: 7px;
            }
            .tab-btn:hover {
                color: var(--text-primary);
                background: var(--surface-hover);
            }
            .tab-btn.active {
                background: var(--surface);
                color: var(--text-primary);
                box-shadow: 0 1px 3px rgba(0,0,0,0.3);
            }
            main {
                flex: 1;
                width: 100%;
                max-width: 1600px;
                margin: 0 auto;
                padding: 14px 18px;
                box-sizing: border-box;
            }
            .tab-pane {
                display: none;
            }
            .tab-pane.active {
                display: block;
            }

            /* SVG Icons */
            .icon {
                width: 14px;
                height: 14px;
                display: inline-block;
                vertical-align: middle;
                stroke: currentColor;
                fill: none;
                stroke-width: 1.75;
                stroke-linecap: round;
                stroke-linejoin: round;
            }
            .icon-lg {
                width: 18px;
                height: 18px;
            }

            /* 3-Column Planner Layout */
            .planner-container {
                display: grid;
                grid-template-columns: 260px minmax(320px, 1fr) minmax(380px, 1.3fr);
                gap: 14px;
                align-items: stretch;
                height: calc(100vh - 90px);
                max-height: calc(100vh - 90px);
                width: 100%;
                box-sizing: border-box;
            }
            @media (max-width: 1024px) {
                .planner-container {
                    grid-template-columns: 240px 1fr;
                    height: auto;
                    max-height: none;
                }
                .planner-artifacts {
                    grid-column: 1 / -1;
                }
            }
            @media (max-width: 768px) {
                .planner-container {
                    grid-template-columns: 1fr;
                    height: auto;
                    max-height: none;
                }
            }

            .panel {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 6px;
                overflow: hidden;
                display: flex;
                flex-direction: column;
                min-width: 0;
                min-height: 0;
                height: 100%;
            }
            .panel-header {
                padding: 12px 16px;
                background: #101319;
                border-bottom: 1px solid var(--border);
                display: flex;
                justify-content: space-between;
                align-items: center;
                flex-shrink: 0;
            }
            .panel-title {
                font-size: 11px;
                font-weight: 600;
                text-transform: uppercase;
                letter-spacing: 0.06em;
                color: var(--text-secondary);
                display: flex;
                align-items: center;
                gap: 7px;
            }

            /* Sidebar Projects List */
            .project-list-search {
                padding: 10px 14px;
                border-bottom: 1px solid var(--border);
                background: #0d1015;
            }
            .project-search-input {
                width: 100%;
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 4px;
                color: var(--text-primary);
                font-size: 12px;
                padding: 6px 10px;
                outline: none;
            }
            .project-search-input:focus {
                border-color: var(--border-focus);
            }
            .project-list-items {
                flex: 1;
                overflow-y: auto;
                padding: 8px;
                display: flex;
                flex-direction: column;
                gap: 4px;
                background: #0d1015;
                min-width: 0;
            }
            .project-item {
                padding: 8px 10px;
                border-radius: 5px;
                border: 1px solid transparent;
                cursor: pointer;
                transition: all 120ms ease;
                background: transparent;
                min-width: 0;
                max-width: 100%;
                box-sizing: border-box;
                overflow: hidden;
            }
            .project-item:hover {
                background: var(--surface-hover);
                border-color: var(--border-subtle);
            }
            .project-item.active {
                background: var(--surface-active);
                border-color: var(--accent-blue);
            }
            .project-item-title {
                font-weight: 600;
                font-size: 12px;
                color: var(--text-primary);
                display: flex;
                justify-content: space-between;
                align-items: center;
                gap: 8px;
                margin-bottom: 3px;
                min-width: 0;
                overflow: hidden;
            }
            .project-item-name {
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
                min-width: 0;
                flex: 1;
            }
            .project-item-meta {
                font-size: 11px;
                color: var(--text-muted);
                display: flex;
                justify-content: space-between;
                align-items: center;
                gap: 6px;
                min-width: 0;
                overflow: hidden;
            }
            .project-item-tag {
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
                max-width: 110px;
                min-width: 0;
            }
            .project-item-actions {
                display: none;
                gap: 3px;
                align-items: center;
            }
            .project-item:hover .project-item-actions {
                display: flex;
            }
            .btn-icon {
                background: transparent;
                border: 1px solid var(--border);
                color: var(--text-secondary);
                padding: 2px 4px;
                border-radius: 3px;
                cursor: pointer;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                transition: all 120ms;
            }
            .btn-icon:hover {
                background: var(--surface-hover);
                color: var(--text-primary);
                border-color: var(--border-focus);
            }
            .btn-icon-danger {
                background: transparent;
                border: 1px solid rgba(239, 68, 68, 0.25);
                color: #f87171;
                padding: 2px 4px;
                border-radius: 3px;
                cursor: pointer;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                transition: all 120ms;
            }
            .btn-icon-danger:hover {
                background: rgba(239, 68, 68, 0.15);
                color: #fca5a5;
                border-color: #ef4444;
            }

            /* Chat Area */
            .chat-feed {
                flex: 1;
                overflow-y: auto;
                overflow-x: hidden;
                padding: 16px;
                display: flex;
                flex-direction: column;
                gap: 14px;
                background: #0d1015;
                min-height: 0;
                min-width: 0;
            }
            .message {
                max-width: 88%;
                min-width: 0;
                padding: 12px 16px;
                border-radius: 8px;
                font-size: 12.5px;
                line-height: 1.6;
                word-wrap: break-word;
                overflow-wrap: anywhere;
                word-break: break-word;
                box-sizing: border-box;
            }
            .message.user {
                align-self: flex-end;
                background: var(--accent-blue-subtle);
                border: 1px solid rgba(59, 130, 246, 0.35);
                color: var(--text-primary);
            }
            .message.assistant {
                align-self: flex-start;
                background: var(--surface);
                border: 1px solid var(--border);
                color: var(--text-primary);
            }
            .message-meta {
                display: flex;
                justify-content: space-between;
                align-items: center;
                gap: 12px;
                margin-bottom: 6px;
                font-size: 10.5px;
                letter-spacing: 0.03em;
                user-select: none;
            }
            .message-sender {
                font-weight: 600;
                font-family: 'JetBrains Mono', monospace;
                text-transform: uppercase;
            }
            .message.user .message-sender {
                color: #93c5fd;
            }
            .message.assistant .message-sender {
                color: var(--accent-blue);
            }
            .message-time {
                color: var(--text-muted);
                font-size: 10px;
                font-family: 'JetBrains Mono', monospace;
            }
            .message-body {
                min-width: 0;
                word-break: break-word;
                overflow-wrap: anywhere;
            }
            .message strong { color: var(--text-primary); font-weight: 600; }
            .message code {
                font-family: 'JetBrains Mono', monospace;
                background: rgba(255, 255, 255, 0.08);
                padding: 2px 6px;
                border-radius: 4px;
                font-size: 11.5px;
                border: 1px solid var(--border-subtle);
                word-break: break-word;
                overflow-wrap: anywhere;
                white-space: pre-wrap;
                color: #93c5fd;
            }
            .message .code-box {
                background: #080a0e;
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 12px 14px;
                font-family: 'JetBrains Mono', monospace;
                font-size: 11.5px;
                white-space: pre;
                overflow-x: auto;
                max-width: 100%;
                box-sizing: border-box;
                color: #e2e8f0;
                line-height: 1.5;
                margin: 10px 0;
                word-break: normal;
                overflow-wrap: normal;
            }

            /* Hermes 3 Scratchpad Thought Styles */
            .hermes-thought-card {
                background: rgba(15, 23, 42, 0.6);
                border: 1px solid var(--border);
                border-left: 3px solid var(--accent-blue);
                border-radius: 6px;
                margin: 8px 0 12px 0;
                overflow: hidden;
            }
            .hermes-thought-card[open] {
                background: rgba(15, 23, 42, 0.9);
            }
            .hermes-thought-summary {
                padding: 7px 12px;
                font-size: 11px;
                font-weight: 600;
                color: var(--text-secondary);
                cursor: pointer;
                user-select: none;
                display: flex;
                align-items: center;
                gap: 8px;
                background: rgba(255, 255, 255, 0.02);
            }
            .hermes-thought-summary:hover {
                color: var(--text-primary);
                background: rgba(255, 255, 255, 0.04);
            }

            /* Continuous Improvement / Hermes Self-Audit */
            .imp-grid {
                display: grid;
                grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
                gap: 12px;
                margin-bottom: 20px;
            }
            .imp-metric-card {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 14px 16px;
                display: flex;
                flex-direction: column;
                gap: 4px;
            }
            .imp-metric-card .label {
                font-size: 11px;
                color: var(--text-muted);
                text-transform: uppercase;
                letter-spacing: 0.5px;
            }
            .imp-metric-card .value {
                font-size: 20px;
                font-weight: 700;
                color: var(--text-primary);
                font-family: 'JetBrains Mono', monospace;
            }
            .imp-scores-grid {
                display: grid;
                grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
                gap: 10px;
                margin-bottom: 20px;
            }
            .imp-score-card {
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 12px;
                text-align: center;
            }
            .imp-score-card .score-num {
                font-size: 22px;
                font-weight: 700;
                color: var(--accent-blue);
                font-family: 'JetBrains Mono', monospace;
            }
            .imp-score-card .score-lbl {
                font-size: 11px;
                color: var(--text-secondary);
                margin-top: 4px;
            }
            .imp-proposal-card {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 16px;
                margin-bottom: 14px;
                transition: border-color 150ms;
            }
            .imp-proposal-card:hover {
                border-color: var(--border-focus);
            }
            .imp-proposal-header {
                display: flex;
                justify-content: space-between;
                align-items: flex-start;
                margin-bottom: 10px;
                gap: 12px;
            }
            .imp-proposal-title {
                font-size: 14px;
                font-weight: 600;
                color: var(--text-primary);
            }
            .imp-action-plan-box {
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 5px;
                padding: 10px 12px;
                font-family: 'JetBrains Mono', monospace;
                font-size: 11.5px;
                color: #d1d7e0;
                white-space: pre-wrap;
                margin: 10px 0;
            }
            .hermes-badge {
                font-size: 9.5px;
                font-family: 'JetBrains Mono', monospace;
                text-transform: uppercase;
                letter-spacing: 0.05em;
                background: rgba(59, 130, 246, 0.15);
                color: var(--accent-blue);
                padding: 1px 6px;
                border-radius: 3px;
                border: 1px solid rgba(59, 130, 246, 0.3);
            }
            .hermes-thought-body {
                padding: 10px 14px;
                font-size: 11px;
                font-family: 'JetBrains Mono', monospace;
                color: #cbd5e1;
                line-height: 1.55;
                white-space: pre-wrap;
                border-top: 1px solid var(--border-subtle);
                background: rgba(0, 0, 0, 0.25);
            }

            .chat-list-row {
                display: flex;
                gap: 8px;
                margin: 4px 0;
                align-items: flex-start;
                min-width: 0;
                width: 100%;
            }
            .chat-bullet {
                color: var(--accent-blue);
                flex-shrink: 0;
                font-weight: bold;
                margin-top: 1px;
            }
            .chat-num {
                color: var(--accent-blue);
                flex-shrink: 0;
                font-family: 'JetBrains Mono', monospace;
                font-size: 11px;
                margin-top: 1px;
            }
            .chat-list-body {
                flex: 1;
                min-width: 0;
                word-break: break-word;
                overflow-wrap: anywhere;
            }
            .chat-table-wrapper {
                max-width: 100%;
                overflow-x: auto;
                margin: 10px 0;
                border: 1px solid var(--border);
                border-radius: 6px;
                box-sizing: border-box;
                background: #090b0e;
            }
            .chat-table {
                width: 100%;
                border-collapse: collapse;
                font-size: 11.5px;
                text-align: left;
                min-width: 320px;
            }
            .chat-table th {
                background: #101319;
                padding: 8px 12px;
                color: var(--text-muted);
                font-size: 10.5px;
                text-transform: uppercase;
                letter-spacing: 0.04em;
                border-bottom: 1px solid var(--border);
            }
            .chat-table td {
                padding: 8px 12px;
                border-bottom: 1px solid var(--border-subtle);
                color: var(--text-secondary);
            }
            .chat-table tr:last-child td {
                border-bottom: none;
            }
            .chip-container {
                display: flex;
                flex-wrap: wrap;
                gap: 6px;
                margin-top: 10px;
                min-width: 0;
            }
            .chip {
                background: var(--surface-hover);
                border: 1px solid var(--border);
                color: var(--text-secondary);
                font-size: 11px;
                padding: 6px 10px;
                border-radius: 6px;
                cursor: pointer;
                transition: all 120ms;
                white-space: normal;
                word-break: break-word;
                overflow-wrap: anywhere;
                line-height: 1.4;
                text-align: left;
                max-width: 100%;
                box-sizing: border-box;
            }
            .chip:hover {
                border-color: var(--accent-blue);
                color: var(--text-primary);
                background: var(--surface-active);
            }
            .chat-input-container {
                padding: 10px 12px;
                border-top: 1px solid var(--border);
                background: var(--surface);
                display: flex;
                flex-direction: column;
                gap: 8px;
                position: relative;
                transition: border-color 150ms;
            }
            .chat-input-row {
                display: flex;
                gap: 8px;
                align-items: flex-end;
            }
            .chat-input {
                flex: 1;
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 6px;
                color: var(--text-primary);
                font-size: 12.5px;
                font-family: inherit;
                padding: 10px 12px;
                outline: none;
                transition: border-color 120ms;
                resize: none;
                min-height: 44px;
                max-height: 180px;
                line-height: 1.5;
                box-sizing: border-box;
                overflow-y: auto;
            }
            .chat-input:focus {
                border-color: var(--border-focus);
            }
            .chat-input::placeholder {
                color: var(--text-muted);
            }
            .attached-docs-bar {
                display: flex;
                flex-wrap: wrap;
                gap: 6px;
                padding: 2px 0;
            }
            .attached-doc-chip {
                display: inline-flex;
                align-items: center;
                gap: 6px;
                background: #141a24;
                border: 1px solid rgba(59, 130, 246, 0.4);
                border-radius: 4px;
                padding: 4px 8px;
                font-size: 11px;
                font-family: 'JetBrains Mono', monospace;
                color: #93c5fd;
            }
            .attached-doc-chip .doc-name {
                max-width: 180px;
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }
            .attached-doc-chip .doc-size {
                color: var(--text-muted);
                font-size: 10px;
            }
            .attached-doc-chip .doc-remove {
                cursor: pointer;
                color: var(--text-muted);
                display: inline-flex;
                align-items: center;
                justify-content: center;
                width: 14px;
                height: 14px;
                border-radius: 50%;
                margin-left: 2px;
                font-size: 13px;
                line-height: 1;
                transition: all 120ms;
            }
            .attached-doc-chip .doc-remove:hover {
                color: #ef4444;
                background: rgba(239, 68, 68, 0.15);
            }
            .btn-attach {
                background: #14171d;
                border: 1px solid var(--border);
                color: var(--text-secondary);
                border-radius: 6px;
                width: 44px;
                height: 44px;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                cursor: pointer;
                transition: all 120ms;
                flex-shrink: 0;
                box-sizing: border-box;
            }
            .btn-attach:hover {
                color: var(--accent-blue);
                border-color: var(--accent-blue);
                background: var(--surface-hover);
            }
            .chat-input-actions {
                display: flex;
                gap: 6px;
                align-items: flex-end;
            }
            .chat-input-actions .btn-primary {
                height: 44px;
                padding: 0 16px;
                border-radius: 6px;
            }
            .chat-drop-overlay {
                position: absolute;
                inset: 0;
                background: rgba(12, 16, 25, 0.94);
                border: 2px dashed var(--accent-blue);
                border-radius: 8px;
                z-index: 50;
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                color: var(--accent-blue);
                font-weight: 600;
                font-size: 14px;
                pointer-events: none;
                backdrop-filter: blur(2px);
            }
            .chat-drop-overlay svg {
                width: 42px;
                height: 42px;
                margin-bottom: 10px;
            }
            .attached-doc-loading {
                display: inline-flex;
                align-items: center;
                gap: 6px;
                background: rgba(59, 130, 246, 0.1);
                border: 1px dashed var(--accent-blue);
                border-radius: 4px;
                padding: 4px 10px;
                font-size: 11px;
                color: var(--accent-blue);
            }
            .attached-doc-card {
                background: #0f141d;
                border: 1px solid rgba(59, 130, 246, 0.3);
                border-radius: 6px;
                padding: 8px 12px;
                margin-bottom: 8px;
            }
            .attached-doc-card-header {
                display: flex;
                align-items: center;
                gap: 8px;
                font-size: 11.5px;
                color: #93c5fd;
                font-family: 'JetBrains Mono', monospace;
            }
            .attached-doc-card-meta {
                color: var(--text-muted);
                font-size: 10.5px;
                margin-left: auto;
            }
            .attached-doc-preview-details {
                margin-top: 6px;
                border-top: 1px solid var(--border-subtle);
                padding-top: 4px;
            }
            .attached-doc-preview-details summary {
                font-size: 10.5px;
                color: var(--text-secondary);
                cursor: pointer;
                user-select: none;
            }
            .attached-doc-preview-details summary:hover {
                color: var(--text-primary);
            }
            .attached-doc-preview-text {
                margin-top: 6px;
                padding: 8px;
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 4px;
                font-family: 'JetBrains Mono', monospace;
                font-size: 10.5px;
                color: #cbd5e1;
                max-height: 180px;
                overflow-y: auto;
                white-space: pre-wrap;
                word-break: break-word;
            }

            /* Buttons */
            .btn-primary {
                background: var(--accent-blue);
                color: #ffffff;
                border: none;
                padding: 0 14px;
                border-radius: 5px;
                font-size: 12px;
                font-weight: 500;
                cursor: pointer;
                display: inline-flex;
                align-items: center;
                gap: 6px;
                transition: opacity 120ms;
            }
            .btn-primary:hover { opacity: 0.92; }
            .btn-secondary {
                background: var(--surface-hover);
                color: var(--text-secondary);
                border: 1px solid var(--border);
                padding: 6px 12px;
                border-radius: 5px;
                font-size: 11px;
                cursor: pointer;
                display: inline-flex;
                align-items: center;
                gap: 5px;
                transition: all 120ms;
            }
            .btn-secondary:hover {
                color: var(--text-primary);
                border-color: var(--border-focus);
            }
            .btn-green {
                background: var(--accent-green);
                color: #0c0e12;
                font-weight: 600;
                border: none;
                padding: 8px 14px;
                border-radius: 5px;
                cursor: pointer;
                display: inline-flex;
                align-items: center;
                gap: 6px;
                font-size: 12px;
                transition: opacity 120ms;
            }
            .btn-green:hover { opacity: 0.92; }

            /* Blueprint Subtabs */
            .bp-subtabs {
                display: flex;
                gap: 4px;
                border-bottom: 1px solid var(--border);
                background: #101319;
                padding: 4px 12px 0 12px;
                flex-shrink: 0;
            }
            .bp-subtab {
                background: transparent;
                border: none;
                color: var(--text-muted);
                padding: 8px 12px;
                font-size: 11px;
                cursor: pointer;
                border-bottom: 2px solid transparent;
                transition: all 120ms;
            }
            .bp-subtab:hover { color: var(--text-secondary); }
            .bp-subtab.active {
                color: var(--text-primary);
                border-bottom: 2px solid var(--accent-blue);
                font-weight: 500;
            }
            .bp-content {
                padding: 16px;
                flex: 1;
                overflow-y: auto;
            }
            .mermaid-box {
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 16px;
                overflow-x: auto;
                text-align: center;
            }
            .code-box {
                background: #090b0e;
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 14px;
                font-family: 'JetBrains Mono', monospace;
                font-size: 11px;
                white-space: pre;
                overflow-x: auto;
                color: #d1d7e0;
                line-height: 1.6;
            }

            /* Streaming Cursor & Live Previews */
            .stream-cursor {
                display: inline-block;
                width: 8px;
                height: 14px;
                background: var(--accent-blue);
                vertical-align: middle;
                margin-left: 3px;
                animation: blinkCursor 0.8s infinite;
            }
            @keyframes blinkCursor {
                0%, 100% { opacity: 1; }
                50% { opacity: 0; }
            }
            .token-subview-btn {
                background: #14171d;
                border: 1px solid var(--border);
                color: var(--text-secondary);
                padding: 4px 10px;
                border-radius: 4px;
                font-size: 11px;
                cursor: pointer;
                transition: all 120ms;
            }
            .token-subview-btn.active {
                background: #1c222c;
                color: var(--text-primary);
                border-color: var(--accent-blue);
                font-weight: 500;
            }
            .live-preview-box {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 14px;
                display: flex;
                flex-direction: column;
                gap: 14px;
            }
            .live-kpi-row {
                display: grid;
                grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
                gap: 10px;
            }
            .live-kpi-card {
                background: #0c0f14;
                border: 1px solid var(--border);
                border-radius: 5px;
                padding: 10px 12px;
            }
            .live-kpi-title {
                font-size: 10px;
                text-transform: uppercase;
                color: var(--text-muted);
                margin-bottom: 4px;
            }
            .live-kpi-val {
                font-family: 'JetBrains Mono', monospace;
                font-size: 16px;
                font-weight: 600;
                color: var(--accent-blue);
            }

            /* Tables */
            .table-container {
                background: var(--surface);
                border: 1px solid var(--border);
                border-radius: 6px;
                overflow-x: auto;
                max-width: 100%;
                box-sizing: border-box;
            }
            table {
                width: 100%;
                border-collapse: collapse;
                font-size: 12px;
                text-align: left;
            }
            .cell-ellipsis {
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
            .btn-table-action {
                background: transparent;
                border: 1px solid var(--border);
                color: var(--text-secondary);
                padding: 3px 8px;
                border-radius: 4px;
                font-size: 11px;
                cursor: pointer;
                transition: all 120ms;
                display: inline-flex;
                align-items: center;
                gap: 4px;
            }
            .btn-table-action:hover {
                background: var(--surface-hover);
                color: var(--text-primary);
                border-color: var(--border-focus);
            }
            .btn-table-action.danger {
                border-color: rgba(239, 68, 68, 0.3);
                color: #f87171;
            }
            .btn-table-action.danger:hover {
                background: rgba(239, 68, 68, 0.15);
                color: #fca5a5;
                border-color: #ef4444;
            }
            th {
                background: #0f1217;
                color: var(--text-muted);
                font-weight: 500;
                font-size: 11px;
                text-transform: uppercase;
                letter-spacing: 0.05em;
                padding: 10px 14px;
                border-bottom: 1px solid var(--border);
            }
            td {
                padding: 10px 14px;
                border-bottom: 1px solid var(--border);
                color: var(--text-secondary);
            }
            tr:last-child td { border-bottom: none; }
            tr:hover td { background: var(--surface-hover); color: var(--text-primary); }

            .badge-tag {
                font-family: 'JetBrains Mono', monospace;
                font-size: 10px;
                padding: 2px 6px;
                border-radius: 3px;
                font-weight: 500;
                display: inline-block;
            }
            .badge-blue { background: var(--accent-blue-subtle); color: var(--accent-blue); border: 1px solid rgba(59, 130, 246, 0.3); }
            .badge-green { background: var(--accent-green-subtle); color: var(--accent-green); border: 1px solid rgba(16, 185, 129, 0.3); }
            .badge-muted { background: rgba(85, 96, 112, 0.15); color: var(--text-secondary); border: 1px solid var(--border); }

            footer {
                margin-top: 36px;
                padding: 16px 24px;
                border-top: 1px solid var(--border);
                text-align: center;
                font-size: 11px;
                color: var(--text-muted);
            }
        </style>
    </head>
    <body>
        <div class="app-layout">
            <header>
                <div class="brand">
                    <svg class="icon icon-lg" viewBox="0 0 24 24"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
                    <span style="font-size: 15px; font-weight: 600; letter-spacing: -0.02em;">AI DevTools</span>
                    <span class="brand-badge">.NET 9 + Decoupled Toolkit</span>
                </div>
                <nav class="nav-tabs">
                    <button class="tab-btn active" onclick="switchTab('tab-planner', event)">
                        <svg class="icon" viewBox="0 0 24 24"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path></svg>
                        <span>Project Planner</span>
                    </button>
                    <button class="tab-btn" onclick="switchTab('tab-overview', event)">
                        <svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"></circle><polyline points="12 6 12 12 16 14"></polyline></svg>
                        <span>Overview</span>
                    </button>
                    <button class="tab-btn" onclick="switchTab('tab-projects', event)">
                        <svg class="icon" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path></svg>
                        <span>Projects DB</span>
                    </button>
                    <button class="tab-btn" onclick="switchTab('tab-knowledge', event)">
                        <svg class="icon" viewBox="0 0 24 24"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"></path><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"></path></svg>
                        <span>Knowledge & ADRs</span>
                    </button>
                    <button class="tab-btn" onclick="switchTab('tab-toolkit', event)">
                        <svg class="icon" viewBox="0 0 24 24"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"></path></svg>
                        <span>Toolkit Catalog</span>
                    </button>
                    <button class="tab-btn" onclick="switchTab('tab-reviewer', event)">
                        <svg class="icon" viewBox="0 0 24 24"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"></path></svg>
                        <span>Code Auditor</span>
                    </button>
                    <button class="tab-btn" onclick="switchTab('tab-improvement', event)">
                        <svg class="icon" viewBox="0 0 24 24"><path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"></path></svg>
                        <span>Mejora Continua (Hermes)</span>
                    </button>
                </nav>
                <div class="engine-status-badge">
                    <span class="engine-status-dot" id="engineDot" title="Ollama Conectado"></span>
                    <span style="color: var(--text-muted); font-size: 10px; text-transform: uppercase; letter-spacing: 0.5px;">Motor:</span>
                    <select class="engine-select" id="engineSelect" onchange="switchEngineModel(this.value)">
                        <option value="auto">Detectando Ollama...</option>
                    </select>
                </div>
            </header>

            <main>
                <!-- 1. PLANNER & ORGANIZED PROJECTS (Default) -->
                <div id="tab-planner" class="tab-pane active">
                    <div class="planner-container">
                        <!-- Column 1: Projects & Conversations Sidebar -->
                        <div class="panel">
                            <div class="panel-header">
                                <div class="panel-title">
                                    <svg class="icon" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path></svg>
                                    <span>Proyectos</span>
                                </div>
                                <button class="btn-secondary" style="padding: 3px 8px; font-size: 11px;" onclick="startNewProjectSession()">
                                    <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>
                                    <span>Nuevo</span>
                                </button>
                            </div>
                            <div class="project-list-search">
                                <input type="text" id="projectSearchInput" class="project-search-input" placeholder="Filtrar proyectos..." oninput="filterProjectList()">
                            </div>
                            <div class="project-list-items" id="projectsSidebarList">
                                <div style="color: var(--text-muted); font-size: 11px; text-align: center; padding: 20px;">Cargando proyectos...</div>
                            </div>
                        </div>

                        <!-- Column 2: Interactive Copilot Chat -->
                        <div class="panel planner-chat" id="plannerChatPanel" style="position: relative;">
                            <div class="chat-drop-overlay" id="chatDropOverlay" style="display: none;">
                                <svg class="icon" viewBox="0 0 24 24"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="17 8 12 3 7 8"></polyline><line x1="12" y1="3" x2="12" y2="15"></line></svg>
                                <div>Suelta aquí tus documentos</div>
                                <div style="font-size: 11px; color: var(--text-secondary); margin-top: 4px;">PDF, Word (.docx), Markdown (.md), Código (.cs, .sql, .json, .txt, etc.)</div>
                            </div>
                            <div class="panel-header" style="gap: 8px;">
                                <div class="panel-title" style="min-width: 0; overflow: hidden; display: flex; align-items: center; gap: 7px;">
                                    <svg class="icon" viewBox="0 0 24 24"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path></svg>
                                    <span id="activeProjectChatTitle" style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 140px;">Nuevo Proyecto</span>
                                </div>
                                <div style="display: flex; align-items: center; gap: 5px; flex-shrink: 0;">
                                    <span class="badge-tag badge-green" id="activeProjectChatBadge">Activo</span>
                                    <button class="btn-icon" title="Editar / Renombrar Proyecto" id="btnEditCurrentProject" onclick="renameCurrentProject()" style="display: none;">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"></path></svg>
                                    </button>
                                    <button class="btn-icon" title="Limpiar Chat" id="btnClearChat" onclick="clearCurrentChat()" style="display: none;">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                    </button>
                                    <button class="btn-icon-danger" title="Eliminar Proyecto" id="btnDeleteCurrentProject" onclick="deleteCurrentProject()" style="display: none;">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                    </button>
                                </div>
                            </div>

                            <div style="padding: 8px 14px; background: #101319; border-bottom: 1px solid var(--border);">
                                <span style="font-size: 10px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.05em;">Plantillas Rapidas:</span>
                                <div class="chip-container" style="margin-top: 4px;">
                                    <span class="chip" onclick="applyQuickTemplate('SaaS Multi-tenant de Facturación con Next.js y PostgreSQL')">SaaS Facturación</span>
                                    <span class="chip" onclick="applyQuickTemplate('E-Commerce de alto volumen con Clean Architecture y React Minimalist')">E-Commerce Clean</span>
                                    <span class="chip" onclick="applyQuickTemplate('Microservicios de Procesamiento de Eventos con Kafka y .NET 9')">Event-Driven Kafka</span>
                                    <span class="chip" onclick="applyQuickTemplate('CLI Terminal Tool con Spectre.Console y SQLite embebido')">CLI Terminal</span>
                                </div>
                            </div>

                            <div class="chat-feed" id="chatFeed">
                                <div class="message assistant">
                                    Bienvenido al <strong>Copilot de Planificación y Arquitectura de Software</strong>.
                                    <br><br>
                                    Describe el sistema que deseas construir o selecciona una plantilla rápida. Puedes adjuntar documentos (.pdf, .docx, .md, .txt, .cs, .sql) para que el asistente analice sus requerimientos y modelos de dominio.
                                </div>
                            </div>

                            <div class="chat-input-container" id="chatInputContainer">
                                <div class="attached-docs-bar" id="attachedDocsBar" style="display: none;"></div>
                                <div class="chat-input-row">
                                    <input type="file" id="docFileInput" multiple accept=".pdf,.docx,.doc,.txt,.md,.json,.cs,.sql,.yaml,.yml,.xml,.csv,.py,.ts,.js,.html,.css" style="display: none;" onchange="handleDocAttachment(event)">
                                    <button type="button" class="btn-attach" id="btnAttachDoc" onclick="document.getElementById('docFileInput').click()" title="Adjuntar documento o archivo (.pdf, .docx, .md, .txt, .json, .cs, .sql...)">
                                        <svg class="icon" viewBox="0 0 24 24" style="width: 18px; height: 18px; pointer-events: none;"><path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"></path></svg>
                                    </button>
                                    <textarea id="chatInput" class="chat-input" rows="1" placeholder="Escribe tu requerimiento (Shift+Enter para salto de línea, Enter para enviar)..." onkeydown="handleChatKeyDown(event)" oninput="autoResizeTextarea(this)"></textarea>
                                    <div class="chat-input-actions">
                                        <button type="button" class="btn-primary" onclick="sendChatMessage()" id="btnSendChat">
                                            <svg class="icon" viewBox="0 0 24 24" style="pointer-events: none;"><line x1="22" y1="2" x2="11" y2="13"></line><polygon points="22 2 15 22 11 13 2 9 22 2"></polygon></svg>
                                            <span>Enviar</span>
                                        </button>
                                    </div>
                                </div>
                            </div>
                        </div>

                        <!-- Column 3: Live Blueprint Studio & Artifacts -->
                        <div class="panel planner-artifacts">
                            <div class="panel-header">
                                <div class="panel-title">
                                    <svg class="icon" viewBox="0 0 24 24"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
                                    <span id="bpHeaderName">Plano Arquitectonico</span>
                                </div>
                                <button class="btn-green" id="scaffoldBtn" onclick="scaffoldProjectOnDisk()">
                                    <svg class="icon" viewBox="0 0 24 24"><polyline points="16 16 12 12 8 16"></polyline><line x1="12" y1="12" x2="12" y2="21"></line><path d="M20.39 18.39A5 5 0 0 0 18 9h-1.26A8 8 0 1 0 3 16.3"></path><polyline points="16 16 12 12 8 16"></polyline></svg>
                                    <span>Estructurar en Disco</span>
                                </button>
                            </div>

                            <div class="bp-subtabs">
                                <button class="bp-subtab active" onclick="switchBpSubtab('bp-c4', event)">Diagrama C4 (Mermaid)</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-stack', event)">Stack & Convenciones</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-tokens', event)">Frontend Design Tokens</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-adr', event)">ADR 001 (Decisión)</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-speckit', event)">Spec Kit (SDD)</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-prd', event)">PRD (Requerimientos)</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-suggestions', event)">Sugerencias & Roadmap</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-agents', event)">AGENTS.md</button>
                                <button class="bp-subtab" onclick="switchBpSubtab('bp-tree', event)">Estructura en Disco</button>
                            </div>

                            <div class="bp-content">
                                <!-- C4 Diagram -->
                                <div id="bp-c4" class="bp-pane">
                                    <div class="mermaid-box" id="mermaidContainer">
                                        <div style="color: var(--text-muted); padding: 40px;">
                                            Inicia la conversación o selecciona un proyecto del panel izquierdo para visualizar su diagrama C4.
                                        </div>
                                    </div>
                                </div>

                                <!-- Stack & Conventions -->
                                <div id="bp-stack" class="bp-pane" style="display: none;">
                                    <h4 style="font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-secondary); margin-bottom: 8px;">Matriz Tecnologica</h4>
                                    <div class="table-container" style="margin-bottom: 16px;">
                                        <table id="techStackTable">
                                            <thead><tr><th>Capa / Dimension</th><th>Tecnologia</th></tr></thead>
                                            <tbody>
                                                <tr><td colspan="2" style="text-align: center; color: var(--text-muted);">Sin datos registrados</td></tr>
                                            </tbody>
                                        </table>
                                    </div>
                                    <h4 style="font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-secondary); margin-bottom: 8px;">Convenciones Arquitectonicas</h4>
                                    <ul id="conventionsList" style="padding-left: 18px; color: var(--text-secondary); font-size: 12px; line-height: 1.8;">
                                        <li>Sin convenciones registradas aún.</li>
                                    </ul>
                                </div>

                                <!-- Frontend Tokens -->
                                <div id="bp-tokens" class="bp-pane" style="display: none;">
                                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px;">
                                        <div style="display: flex; gap: 6px;">
                                            <button class="token-subview-btn active" id="btnTokensCssView" onclick="switchTokenSubView('css')">Tokens CSS</button>
                                            <button class="token-subview-btn" id="btnTokensLiveView" onclick="switchTokenSubView('live')">Live Component Preview</button>
                                        </div>
                                        <span class="badge-tag badge-blue" id="tokensThemeName">Classic Minimalist Slate</span>
                                    </div>
                                    <div id="tokensCssContainer">
                                        <div class="code-box" id="tokensCssBox">/* Tokens CSS se generarán con el proyecto */</div>
                                    </div>
                                    <div id="tokensLiveContainer" style="display: none;">
                                        <div class="live-preview-box" id="livePreviewBox">
                                            <div class="live-kpi-row">
                                                <div class="live-kpi-card">
                                                    <div class="live-kpi-title">Usuarios Activos</div>
                                                    <div class="live-kpi-val" id="prevKpiUsers">14,280</div>
                                                </div>
                                                <div class="live-kpi-card">
                                                    <div class="live-kpi-title">Eventos / seg</div>
                                                    <div class="live-kpi-val" id="prevKpiEvents">1,850</div>
                                                </div>
                                                <div class="live-kpi-card">
                                                    <div class="live-kpi-title">Latencia p99</div>
                                                    <div class="live-kpi-val" id="prevKpiLatency">28 ms</div>
                                                </div>
                                            </div>
                                            <div style="background: #090b0e; border: 1px solid var(--border); border-radius: 5px; padding: 12px;">
                                                <div style="font-size: 11px; font-weight: 600; color: var(--text-secondary); text-transform: uppercase; margin-bottom: 8px;">Muestra de Componentes UI</div>
                                                <div style="display: flex; gap: 8px; align-items: center; flex-wrap: wrap;">
                                                    <button class="btn-primary" style="font-size: 11px; padding: 6px 12px;">Boton Primario</button>
                                                    <button class="btn-secondary" style="font-size: 11px; padding: 6px 12px;">Boton Secundario</button>
                                                    <span class="badge-tag badge-green">Activo</span>
                                                    <span class="badge-tag badge-blue">En Proceso</span>
                                                    <span class="badge-tag badge-muted">Archivado</span>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </div>

                                <!-- ADR 001 -->
                                <div id="bp-adr" class="bp-pane" style="display: none;">
                                    <h4 style="font-size: 13px; font-weight: 600; margin-bottom: 8px;" id="adrTitle">ADR 001</h4>
                                    <div class="code-box" id="adrContentBox" style="font-family: inherit; font-size: 12px;">/* El ADR se generará automáticamente con el análisis */</div>
                                </div>

                                <!-- Spec Kit (SDD Canonical Studio) -->
                                <div id="bp-speckit" class="bp-pane" style="display: none;">
                                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px;">
                                        <div style="display: flex; gap: 4px; flex-wrap: wrap;">
                                            <button class="token-subview-btn active" id="btnSpeckitConst" onclick="switchSpecKitFile('constitution')">constitution.md</button>
                                            <button class="token-subview-btn" id="btnSpeckitSpec" onclick="switchSpecKitFile('spec')">spec.md</button>
                                            <button class="token-subview-btn" id="btnSpeckitPlan" onclick="switchSpecKitFile('plan')">plan.md</button>
                                            <button class="token-subview-btn" id="btnSpeckitTasks" onclick="switchSpecKitFile('tasks')">tasks.md</button>
                                        </div>
                                        <div style="display: flex; gap: 6px;">
                                            <button class="btn-secondary" onclick="copyBoxContent('speckitContentBox', this)" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
                                                <span>Copiar</span>
                                            </button>
                                            <button class="btn-secondary" onclick="downloadProjectSpecKitZip()" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>
                                                <span>Descargar Spec Kit (.ZIP)</span>
                                            </button>
                                        </div>
                                    </div>
                                    <div class="code-box" id="speckitContentBox" style="max-height: calc(100vh - 220px); overflow-y: auto;">/* Spec Kit (SDD) se cargará aquí */</div>
                                </div>

                                <!-- PRD Pane -->
                                <div id="bp-prd" class="bp-pane" style="display: none;">
                                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px;">
                                        <h4 style="font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-secondary);">Product Requirements Document (PRD)</h4>
                                        <div style="display: flex; gap: 6px;">
                                            <button class="btn-secondary" onclick="copyBoxContent('prdContentBox', this)" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
                                                <span>Copiar PRD</span>
                                            </button>
                                            <button class="btn-secondary" onclick="downloadProjectPrd()" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line></svg>
                                                <span>Descargar PRD (.md)</span>
                                            </button>
                                        </div>
                                    </div>
                                    <div class="code-box" id="prdContentBox" style="max-height: calc(100vh - 220px); overflow-y: auto;">/* PRD se generará automáticamente con el análisis */</div>
                                </div>

                                <!-- Suggestions & Roadmap Pane -->
                                <div id="bp-suggestions" class="bp-pane" style="display: none;">
                                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px;">
                                        <h4 style="font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-secondary);">Sugerencias Tecnicas (ISO/IEC 25010) & Roadmap</h4>
                                        <button class="btn-secondary" onclick="copyBoxContent('suggestionsContentBox', this)" style="font-size: 11px; padding: 4px 10px;">
                                            <svg class="icon" viewBox="0 0 24 24"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
                                            <span>Copiar Sugerencias</span>
                                        </button>
                                    </div>
                                    <div class="code-box" id="suggestionsContentBox" style="max-height: calc(100vh - 220px); overflow-y: auto;">/* Sugerencias y roadmap se generarán automáticamente */</div>
                                </div>

                                <!-- AGENTS.md Pane -->
                                <div id="bp-agents" class="bp-pane" style="display: none;">
                                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px;">
                                        <h4 style="font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-secondary);">Directivas AGENTS.md (Antigravity, Cursor, Copilot)</h4>
                                        <div style="display: flex; gap: 6px;">
                                            <button class="btn-secondary" onclick="copyBoxContent('agentsContentBox', this)" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>
                                                <span>Copiar AGENTS.md</span>
                                            </button>
                                            <button class="btn-secondary" onclick="downloadProjectAgentsMd()" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 10 10H12V2z"></path><path d="M12 12L2.5 7.5"></path><path d="M12 12v10"></path></svg>
                                                <span>Descargar AGENTS.md</span>
                                            </button>
                                        </div>
                                    </div>
                                    <div class="code-box" id="agentsContentBox" style="max-height: calc(100vh - 220px); overflow-y: auto;">/* AGENTS.md se generará automáticamente */</div>
                                </div>

                                <!-- Directory Tree -->
                                <div id="bp-tree" class="bp-pane" style="display: none;">
                                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; flex-wrap: wrap; gap: 8px;">
                                        <h4 style="font-size: 12px; font-weight: 600; text-transform: uppercase; color: var(--text-secondary);">Estructura Propuesta para Scaffolding</h4>
                                        <div style="display: flex; gap: 6px;">
                                            <button class="btn-secondary" id="btnDownloadZip" onclick="downloadProjectZip()" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>
                                                <span>Descargar ZIP</span>
                                            </button>
                                            <button class="btn-secondary" id="btnExportDocs" onclick="downloadProjectDocs()" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line></svg>
                                                <span>Exportar Docs</span>
                                            </button>
                                            <button class="btn-secondary" id="btnExportAgents" onclick="downloadProjectAgentsMd()" title="Exportar AGENTS.md para Google Antigravity y agentes de IA" style="font-size: 11px; padding: 4px 10px;">
                                                <svg class="icon" viewBox="0 0 24 24"><path d="M12 2a10 10 0 1 0 10 10H12V2z"></path><path d="M12 12L2.5 7.5"></path><path d="M12 12v10"></path></svg>
                                                <span>Exportar AGENTS.md</span>
                                            </button>
                                        </div>
                                    </div>
                                    <div style="display: flex; gap: 6px; margin-bottom: 10px;">
                                        <input type="text" id="txtScaffoldPath" class="chat-input" style="font-size: 11px; padding: 6px 10px;" placeholder="Ruta destino en disco (ej. C:\Proyectos\MiApp)" />
                                        <button class="btn-primary" onclick="scaffoldProjectOnDiskCustom()" style="font-size: 11px; padding: 6px 12px; white-space: nowrap;">
                                            <svg class="icon" viewBox="0 0 24 24"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
                                            <span>Generar en Disco</span>
                                        </button>
                                    </div>
                                    <div class="code-box" id="dirTreeBox">/* Arbol de directorios */</div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 2. OVERVIEW TAB -->
                <div id="tab-overview" class="tab-pane">
                    <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-bottom: 24px;">
                        <div class="panel" style="padding: 16px 20px;">
                            <div style="font-size: 11px; text-transform: uppercase; color: var(--text-muted); margin-bottom: 6px;">Arquitectura de Sistema</div>
                            <div style="font-family: 'JetBrains Mono', monospace; font-size: 20px; font-weight: 600; color: var(--accent-blue);">Híbrida / Modular</div>
                        </div>
                        <div class="panel" style="padding: 16px 20px;">
                            <div style="font-size: 11px; text-transform: uppercase; color: var(--text-muted); margin-bottom: 6px;">Prompts Registrados</div>
                            <div style="font-family: 'JetBrains Mono', monospace; font-size: 24px; font-weight: 600;" id="statPromptsCount">9</div>
                        </div>
                        <div class="panel" style="padding: 16px 20px;">
                            <div style="font-size: 11px; text-transform: uppercase; color: var(--text-muted); margin-bottom: 6px;">Skills en Toolkit</div>
                            <div style="font-family: 'JetBrains Mono', monospace; font-size: 24px; font-weight: 600;" id="statSkillsCount">14</div>
                        </div>
                        <div class="panel" style="padding: 16px 20px;">
                            <div style="font-size: 11px; text-transform: uppercase; color: var(--text-muted); margin-bottom: 6px;">Proyectos en SQLite</div>
                            <div style="font-family: 'JetBrains Mono', monospace; font-size: 24px; font-weight: 600;" id="statProjectsCount">0</div>
                        </div>
                    </div>

                    <div class="panel">
                        <div class="panel-header">
                            <div class="panel-title">Estado del Entorno y Toolkit Desacoplado</div>
                        </div>
                        <div style="padding: 20px;">
                            <p style="color: var(--text-secondary); margin-bottom: 14px;">
                                La aplicación opera como un <strong>Host privado en .NET 9</strong> consumiendo un <strong>Toolkit independiente y portátil</strong> en <code>toolkit/</code>. 
                                Puedes clonar o publicar el toolkit como repositorio abierto o catálogo de equipo.
                            </p>
                            <div style="display: flex; gap: 10px;">
                                <button class="btn-primary" onclick="switchTab('tab-planner')">
                                    <svg class="icon" viewBox="0 0 24 24"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"></path></svg>
                                    <span>Ir al Planificador Interactivo</span>
                                </button>
                                <button class="btn-secondary" onclick="switchTab('tab-toolkit')">
                                    <svg class="icon" viewBox="0 0 24 24"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"></path></svg>
                                    <span>Ver Catálogo de Skills</span>
                                </button>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- 3. PROJECTS DB TAB -->
                <div id="tab-projects" class="tab-pane">
                    <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px;">
                        <h2 style="font-size: 16px; font-weight: 600;">Proyectos Registrados en SQLite</h2>
                        <button class="btn-secondary" onclick="refreshProjects()">
                            <svg class="icon" viewBox="0 0 24 24"><polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 1 14 7 14"></polyline><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path></svg>
                            <span>Actualizar</span>
                        </button>
                    </div>
                    <div class="table-container">
                        <table id="projectsTable">
                            <thead>
                                <tr>
                                    <th style="width: 80px;">ID</th>
                                    <th>Nombre</th>
                                    <th>Ruta en Disco</th>
                                    <th style="width: 90px;">Mensajes</th>
                                    <th>Arquitectura</th>
                                    <th style="width: 110px;">Fecha Registro</th>
                                    <th style="width: 150px;">Acciones</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr><td colspan="7" style="text-align: center; color: var(--text-muted);">Cargando proyectos...</td></tr>
                            </tbody>
                        </table>
                    </div>
                </div>

                <!-- 4. KNOWLEDGE & ADRS TAB -->
                <div id="tab-knowledge" class="tab-pane">
                    <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 16px;">
                        <h2 style="font-size: 16px; font-weight: 600;">Base de Conocimiento & Reglas de Arquitectura</h2>
                        <button class="btn-secondary" onclick="refreshKnowledge()">
                            <svg class="icon" viewBox="0 0 24 24"><polyline points="23 4 23 10 17 10"></polyline><polyline points="1 20 1 14 7 14"></polyline><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path></svg>
                            <span>Actualizar</span>
                        </button>
                    </div>
                    <div class="table-container" style="margin-bottom: 24px;">
                        <table id="knowledgeTable">
                            <thead>
                                <tr>
                                    <th>Titulo</th>
                                    <th style="width: 110px;">Dominio</th>
                                    <th>Contenido / Regla</th>
                                    <th style="width: 120px;">Tags</th>
                                    <th style="width: 90px;">Origen</th>
                                    <th style="width: 90px;">Acciones</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr><td colspan="6" style="text-align: center; color: var(--text-muted);">Cargando conocimiento...</td></tr>
                            </tbody>
                        </table>
                    </div>

                    <h2 style="font-size: 16px; font-weight: 600; margin-bottom: 16px;">Registros de Decision Arquitectonica (ADRs)</h2>
                    <div class="table-container">
                        <table id="documentsTable">
                            <thead>
                                <tr>
                                    <th>Titulo</th>
                                    <th style="width: 100px;">Tipo</th>
                                    <th style="width: 80px;">Version</th>
                                    <th style="width: 100px;">ID Proyecto</th>
                                    <th style="width: 110px;">Fecha Creacion</th>
                                    <th style="width: 90px;">Acciones</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr><td colspan="6" style="text-align: center; color: var(--text-muted);">Cargando documentos...</td></tr>
                            </tbody>
                        </table>
                    </div>
                </div>

                <!-- 5. TOOLKIT CATALOG TAB -->
                <div id="tab-toolkit" class="tab-pane">
                    <h2 style="font-size: 16px; font-weight: 600; margin-bottom: 14px;">Catalogo Desacoplado de Prompts</h2>
                    <div class="table-container" style="margin-bottom: 24px;">
                        <table id="promptsTable">
                            <thead>
                                <tr>
                                    <th>Prompt ID</th>
                                    <th>Nombre</th>
                                    <th>Descripcion</th>
                                    <th>Reglas</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr><td colspan="4" style="text-align: center; color: var(--text-muted);">Cargando prompts...</td></tr>
                            </tbody>
                        </table>
                    </div>

                    <h2 style="font-size: 16px; font-weight: 600; margin-bottom: 14px;">Catalogo Desacoplado de Skills & Herramientas</h2>
                    <div class="table-container">
                        <table id="skillsTable">
                            <thead>
                                <tr>
                                    <th>Skill ID</th>
                                    <th>Nombre</th>
                                    <th>Descripcion</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr><td colspan="3" style="text-align: center; color: var(--text-muted);">Cargando skills...</td></tr>
                            </tbody>
                        </table>
                    </div>
                </div>

                <!-- 6. CODE REVIEWER TAB -->
                <div id="tab-reviewer" class="tab-pane">
                    <h2 style="font-size: 16px; font-weight: 600; margin-bottom: 12px;">Auditor de Codigo & Seguridad OWASP</h2>
                    <div class="panel" style="margin-bottom: 18px;">
                        <div class="panel-header">
                            <div class="panel-title">Codigo C# a Auditar</div>
                            <button class="btn-primary" onclick="runCodeReview()">
                                <svg class="icon" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
                                <span>Ejecutar Auditoria</span>
                            </button>
                        </div>
                        <div style="padding: 12px;">
                            <textarea id="reviewCodeInput" style="width: 100%; height: 180px; background: #090b0e; border: 1px solid var(--border); border-radius: 5px; color: #d1d7e0; font-family: 'JetBrains Mono', monospace; font-size: 12px; padding: 12px; outline: none;"></textarea>
                        </div>
                    </div>
                    <div class="panel" id="reviewResultPanel" style="display: none;">
                        <div class="panel-header">
                            <div class="panel-title">Reporte de Auditoria</div>
                            <span class="badge-tag badge-blue" id="reviewStatusBadge">Audit Complete</span>
                        </div>
                        <div style="padding: 16px;">
                            <div class="code-box" id="reviewResultContent"></div>
                        </div>
                    </div>
                </div>

                <!-- 7. CONTINUOUS IMPROVEMENT (HERMES + ANTIGRAVITY) -->
                <div id="tab-improvement" class="tab-pane">
                    <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 18px; flex-wrap: wrap; gap: 12px;">
                        <div>
                            <h2 style="font-size: 18px; font-weight: 700; color: var(--text-primary); margin-bottom: 4px;">Mejora Continua & Auto-Auditoría (Hermes 3 + Antigravity)</h2>
                            <p style="color: var(--text-secondary); font-size: 12.5px;">Sinergia agéntica: Razonamiento arquitectónico ISO/IEC 25010 (Nous Hermes 3) y refactorización guiada (Antigravity).</p>
                        </div>
                        <div style="display: flex; gap: 10px; align-items: center;">
                            <span class="badge-tag badge-blue" id="impModelBadge">Modelo: hermes3:8b</span>
                            <button class="btn-primary" id="btnRunAudit" onclick="runHermesAudit()" style="padding: 8px 16px; font-size: 12.5px;">
                                <svg class="icon" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
                                <span>Ejecutar Auto-Auditoría</span>
                            </button>
                        </div>
                    </div>

                    <!-- Metric Cards -->
                    <div class="imp-grid">
                        <div class="imp-metric-card">
                            <span class="label">Proyectos .NET</span>
                            <span class="value" id="impMetricProjects">-</span>
                        </div>
                        <div class="imp-metric-card">
                            <span class="label">Archivos C#</span>
                            <span class="value" id="impMetricCsFiles">-</span>
                        </div>
                        <div class="imp-metric-card">
                            <span class="label">Líneas de Código</span>
                            <span class="value" id="impMetricLoc">-</span>
                        </div>
                        <div class="imp-metric-card">
                            <span class="label">Pruebas Unitarias</span>
                            <span class="value" id="impMetricTests">-</span>
                        </div>
                        <div class="imp-metric-card">
                            <span class="label">Clean Architecture</span>
                            <span class="value" id="impMetricCleanArch" style="font-size: 14px; color: var(--accent-green);">Verificando...</span>
                        </div>
                    </div>

                    <!-- ISO/IEC 25010 Quality Scores -->
                    <div class="panel" style="margin-bottom: 18px;">
                        <div class="panel-header">
                            <div class="panel-title">Métricas de Calidad de Software (ISO/IEC 25010)</div>
                            <span class="badge-tag badge-green" id="impOverallScoreBadge">Global: --/100</span>
                        </div>
                        <div style="padding: 16px;">
                            <div class="imp-scores-grid">
                                <div class="imp-score-card">
                                    <div class="score-num" id="scoreMaintainability">--</div>
                                    <div class="score-lbl">Mantenibilidad</div>
                                </div>
                                <div class="imp-score-card">
                                    <div class="score-num" id="scoreReliability">--</div>
                                    <div class="score-lbl">Confiabilidad</div>
                                </div>
                                <div class="imp-score-card">
                                    <div class="score-num" id="scorePerformance">--</div>
                                    <div class="score-lbl">Eficiencia / Rendimiento</div>
                                </div>
                                <div class="imp-score-card">
                                    <div class="score-num" id="scoreSecurity">--</div>
                                    <div class="score-lbl">Seguridad</div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Hermes Cognitive Scratchpad -->
                    <div class="panel" style="margin-bottom: 18px;" id="hermesThoughtPanel">
                        <div class="panel-header">
                            <div class="panel-title" style="display: flex; align-items: center; gap: 8px;">
                                <span class="hermes-badge">Hermes 3</span>
                                <span>Razonamiento Cognitivo de Arquitectura (&lt;thought&gt; Scratchpad)</span>
                            </div>
                            <span class="badge-tag badge-blue">ISO/IEC 25010</span>
                        </div>
                        <div style="padding: 16px;">
                            <div class="code-box" id="hermesThoughtContent" style="white-space: pre-wrap; font-size: 12px; color: #a9b7c6; background: #07090c; border: 1px solid var(--border); padding: 14px; border-radius: 6px; max-height: 240px; overflow-y: auto;">
                                Haz clic en 'Ejecutar Auto-Auditoría' para ver el análisis cognitivo de Hermes 3 en tiempo real.
                            </div>
                        </div>
                    </div>

                    <!-- Proposals List -->
                    <div class="panel">
                        <div class="panel-header">
                            <div class="panel-title">Propuestas de Mejora y Refactorización Priorizadas</div>
                            <span class="badge-tag badge-blue" id="impProposalsCount">0 Propuestas</span>
                        </div>
                        <div style="padding: 16px;" id="impProposalsContainer">
                            <div style="color: var(--text-muted); font-size: 12px; text-align: center; padding: 24px;">
                                Cargando métricas y catálogo de mejoras continuas...
                            </div>
                        </div>
                    </div>
                </div>
            </main>

            <footer>
                AI DevTools // Arquitectura Híbrida &bull; Toolkit Portátil &bull; Host Privado .NET 9 &bull; SQLite Zero-Config
            </footer>
        </div>

        <script>
            // State
            let currentProjectId = null;
            let currentSessionId = null;
            let currentAnswers = null;
            let currentBlueprint = null;
            let projectsCache = [];

            mermaid.initialize({
                startOnLoad: false,
                theme: 'dark',
                themeVariables: {
                    darkMode: true,
                    background: '#090b0e',
                    primaryColor: '#1c2430',
                    primaryTextColor: '#f0f3f6',
                    primaryBorderColor: '#3b82f6',
                    lineColor: '#556070',
                    secondaryColor: '#14171d',
                    tertiaryColor: '#191f2a'
                }
            });

            function switchTab(tabId, ev) {
                document.querySelectorAll('.tab-pane').forEach(el => el.classList.remove('active'));
                document.querySelectorAll('.tab-btn').forEach(el => el.classList.remove('active'));
                document.getElementById(tabId).classList.add('active');
                if (ev && ev.currentTarget) ev.currentTarget.classList.add('active');

                if (tabId === 'tab-projects') refreshProjects();
                if (tabId === 'tab-knowledge') refreshKnowledge();
                if (tabId === 'tab-toolkit') refreshToolkit();
                if (tabId === 'tab-overview') refreshOverview();
                if (tabId === 'tab-improvement') refreshImprovementTab();
            }

            let currentSpecKit = null;
            let currentSpecKitActiveFile = 'constitution';

            function switchBpSubtab(subtabId, ev) {
                document.querySelectorAll('.bp-pane').forEach(el => el.style.display = 'none');
                document.querySelectorAll('.bp-subtab').forEach(el => el.classList.remove('active'));
                document.getElementById(subtabId).style.display = 'block';
                if (ev && ev.currentTarget) ev.currentTarget.classList.add('active');

                if (subtabId === 'bp-c4' && currentBlueprint) {
                    renderMermaid();
                } else if (subtabId === 'bp-speckit') {
                    if (currentBlueprint && currentBlueprint.specKit) {
                        currentSpecKit = currentBlueprint.specKit;
                        switchSpecKitFile(currentSpecKitActiveFile || 'constitution');
                    } else if (currentProjectId && !currentSpecKit) {
                        fetch('/api/planning/projects/' + currentProjectId + '/speckit')
                            .then(r => r.ok ? r.json() : null)
                            .then(sk => {
                                if (sk) {
                                    currentSpecKit = sk;
                                    switchSpecKitFile(currentSpecKitActiveFile || 'constitution');
                                }
                            }).catch(console.error);
                    } else {
                        switchSpecKitFile(currentSpecKitActiveFile || 'constitution');
                    }
                } else if (subtabId === 'bp-prd') {
                    const prdBox = document.getElementById('prdContentBox');
                    if (prdBox && currentProjectId && (!prdBox.textContent || prdBox.textContent.startsWith('/* PRD se generará'))) {
                        fetch('/api/planning/projects/' + currentProjectId + '/prd')
                            .then(r => r.ok ? r.json() : null)
                            .then(data => {
                                if (data && data.prdMarkdown) prdBox.textContent = data.prdMarkdown;
                            }).catch(console.error);
                    }
                } else if (subtabId === 'bp-suggestions') {
                    const sugBox = document.getElementById('suggestionsContentBox');
                    if (sugBox && currentProjectId && (!sugBox.textContent || sugBox.textContent.startsWith('/* Sugerencias y roadmap'))) {
                        fetch('/api/planning/projects/' + currentProjectId + '/suggestions')
                            .then(r => r.ok ? r.json() : null)
                            .then(data => {
                                if (data && data.suggestionsMarkdown) sugBox.textContent = data.suggestionsMarkdown;
                            }).catch(console.error);
                    }
                } else if (subtabId === 'bp-agents') {
                    const agBox = document.getElementById('agentsContentBox');
                    if (agBox && currentProjectId && (!agBox.textContent || agBox.textContent.startsWith('/* AGENTS.md'))) {
                        fetch('/api/planning/projects/' + currentProjectId + '/export-agents')
                            .then(r => r.ok ? r.text() : null)
                            .then(txt => {
                                if (txt) agBox.textContent = txt;
                            }).catch(console.error);
                    }
                }
            }

            let isNewProjectMode = false;

            function formatMessageTime(val) {
                if (!val) return '';
                const d = new Date(val);
                if (isNaN(d.getTime())) return '';
                return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true });
            }

            function formatRelativeTime(val) {
                if (!val) return '';
                const d = new Date(val);
                if (isNaN(d.getTime())) return '';
                const now = new Date();
                const isToday = d.getDate() === now.getDate() && d.getMonth() === now.getMonth() && d.getFullYear() === now.getFullYear();
                const timeStr = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true });
                if (isToday) return `Hoy ${timeStr}`;
                
                const yesterday = new Date(now);
                yesterday.setDate(now.getDate() - 1);
                const isYesterday = d.getDate() === yesterday.getDate() && d.getMonth() === yesterday.getMonth() && d.getFullYear() === yesterday.getFullYear();
                if (isYesterday) return `Ayer ${timeStr}`;
                
                return `${d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}, ${timeStr}`;
            }

            async function loadProjectsSidebar() {
                try {
                    const res = await fetch('/api/planning/projects');
                    if (!res.ok) return;
                    projectsCache = await res.json();
                    renderProjectsSidebar(projectsCache);

                    // Auto select first project ONLY if not in new project mode and none selected
                    if (!isNewProjectMode && !currentProjectId && projectsCache.length > 0) {
                        selectProject(projectsCache[0].id);
                    }
                } catch (e) {
                    console.error('Error loading projects sidebar', e);
                }
            }

            function renderProjectsSidebar(list) {
                const container = document.getElementById('projectsSidebarList');
                container.innerHTML = '';

                if (list.length === 0) {
                    container.innerHTML = '<div style="color: var(--text-muted); font-size: 11px; text-align: center; padding: 20px;">No hay proyectos aun. Haz clic en "Nuevo" para iniciar uno.</div>';
                    return;
                }

                list.forEach(p => {
                    const item = document.createElement('div');
                    item.className = 'project-item' + (p.id === currentProjectId ? ' active' : '');
                    item.onclick = (e) => {
                        if (e.target.closest('.project-item-actions')) return;
                        selectProject(p.id);
                    };

                    const displayDate = formatRelativeTime(p.updatedAt || p.createdAt);
                    const archTag = p.architecturalStyle ? p.architecturalStyle.split('/')[0].trim() : 'Clean Arch';
                    const safeName = escapeHtml(p.name);
                    const safeArch = escapeHtml(archTag);

                    item.innerHTML = `
                        <div class="project-item-title">
                            <span class="project-item-name" title="${safeName}">${safeName}</span>
                            <div style="display: flex; align-items: center; gap: 4px; flex-shrink: 0;">
                                <div class="project-item-actions">
                                    <button class="btn-icon" title="Renombrar Proyecto" onclick="renameProject('${p.id}', '${safeName}', event)">
                                        <svg class="icon" style="width: 10px; height: 10px;" viewBox="0 0 24 24"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"></path></svg>
                                    </button>
                                    <button class="btn-icon-danger" title="Eliminar Proyecto" onclick="deleteProject('${p.id}', '${safeName}', event)">
                                        <svg class="icon" style="width: 10px; height: 10px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                    </button>
                                </div>
                                <span class="badge-tag badge-muted">${p.messageCount} msg</span>
                            </div>
                        </div>
                        <div class="project-item-meta">
                            <span class="project-item-tag" title="${safeArch}">${safeArch}</span>
                            <span title="${p.updatedAt ? 'Actualizado: ' + p.updatedAt : 'Creado: ' + p.createdAt}">${displayDate}</span>
                        </div>
                    `;
                    container.appendChild(item);
                });
            }

            function filterProjectList() {
                const query = document.getElementById('projectSearchInput').value.toLowerCase().trim();
                const filtered = projectsCache.filter(p => p.name.toLowerCase().includes(query) || (p.description && p.description.toLowerCase().includes(query)));
                renderProjectsSidebar(filtered);
            }

            async function selectProject(projectId) {
                isNewProjectMode = false;
                currentProjectId = projectId;
                renderProjectsSidebar(projectsCache);

                const project = projectsCache.find(p => p.id === projectId);
                if (project) {
                    document.getElementById('activeProjectChatTitle').textContent = project.name;
                    document.getElementById('bpHeaderName').textContent = project.name;
                    document.getElementById('btnEditCurrentProject').style.display = 'inline-flex';
                    document.getElementById('btnClearChat').style.display = 'inline-flex';
                    document.getElementById('btnDeleteCurrentProject').style.display = 'inline-flex';
                }

                // Fetch message history
                try {
                    const res = await fetch('/api/planning/projects/' + projectId + '/messages');
                    const messages = await res.json();

                    const feed = document.getElementById('chatFeed');
                    feed.innerHTML = '';

                    if (messages.length === 0) {
                        feed.innerHTML = `
                            <div class="message assistant">
                                <div class="message-meta">
                                    <span class="message-sender">DEVTOOLS AI</span>
                                    <span class="message-time">${formatMessageTime(new Date())}</span>
                                </div>
                                <div class="message-body">
                                    Sesión para <strong>${escapeHtml(project ? project.name : 'este proyecto')}</strong> iniciada.
                                    <br><br>
                                    Escribe los requerimientos específicos o plantea consultas para refinar la arquitectura.
                                </div>
                            </div>
                        `;
                    } else {
                        messages.forEach(m => {
                            appendChatMessage(m.role, m.content, m.suggestedActions, m.timestamp);
                        });
                    }

                    // Restore latest blueprint
                    if (project && project.latestBlueprint) {
                        currentBlueprint = project.latestBlueprint;
                        displayBlueprint(project.latestBlueprint);
                    }
                } catch (e) {
                    console.error('Error fetching project messages', e);
                }
            }

            function startNewProjectSession() {
                isNewProjectMode = true;
                currentProjectId = null;
                currentAnswers = null;
                currentBlueprint = null;
                renderProjectsSidebar(projectsCache);

                document.getElementById('activeProjectChatTitle').textContent = 'Nuevo Proyecto';
                document.getElementById('bpHeaderName').textContent = 'Plano Arquitectonico';
                document.getElementById('btnEditCurrentProject').style.display = 'none';
                document.getElementById('btnClearChat').style.display = 'none';
                document.getElementById('btnDeleteCurrentProject').style.display = 'none';

                const feed = document.getElementById('chatFeed');
                feed.innerHTML = `
                    <div class="message assistant">
                        <div class="message-meta">
                            <span class="message-sender">DEVTOOLS AI</span>
                            <span class="message-time">${formatMessageTime(new Date())}</span>
                        </div>
                        <div class="message-body">
                            Iniciando un <strong>Nuevo Proyecto</strong>.
                            <br><br>
                            Describe el sistema que deseas construir o selecciona una plantilla rápida. El asistente generará el plano arquitectónico y guardará la sesión de forma independiente.
                        </div>
                    </div>
                `;

                // Reset blueprint viewer
                document.getElementById('mermaidContainer').innerHTML = `
                    <div style="color: var(--text-muted); padding: 40px;">
                        Inicia la conversación para generar el diagrama C4 de este nuevo proyecto.
                    </div>
                `;
                document.querySelector('#techStackTable tbody').innerHTML = '<tr><td colspan="2" style="text-align: center; color: var(--text-muted);">Sin datos registrados</td></tr>';
                document.getElementById('conventionsList').innerHTML = '<li>Sin convenciones registradas aún.</li>';
                document.getElementById('tokensCssBox').textContent = '/* Tokens CSS se generarán con el proyecto */';
                document.getElementById('adrContentBox').textContent = '/* El ADR se generará automáticamente */';
                document.getElementById('dirTreeBox').textContent = '/* Arbol de directorios */';
                currentSpecKit = null;
                const spkBox = document.getElementById('speckitContentBox');
                if (spkBox) spkBox.textContent = '/* Inicia la conversación para generar Spec Kit (SDD) */';
                const prdBox = document.getElementById('prdContentBox');
                if (prdBox) prdBox.textContent = '/* Inicia la conversación para generar el PRD */';
                const sugBox = document.getElementById('suggestionsContentBox');
                if (sugBox) sugBox.textContent = '/* Sugerencias y roadmap */';
                const agBox = document.getElementById('agentsContentBox');
                if (agBox) agBox.textContent = '/* AGENTS.md */';
            }

            let pendingAttachedDocuments = [];

            function autoResizeTextarea(el) {
                if (!el) return;
                el.style.height = 'auto';
                el.style.height = Math.min(el.scrollHeight, 180) + 'px';
            }

            function handleChatKeyDown(event) {
                if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    sendChatMessage();
                }
            }

            async function handleDocAttachment(event) {
                const files = event.target.files;
                if (!files || files.length === 0) return;
                await processFiles(Array.from(files));
                event.target.value = '';
            }

            async function extractTextFromPdf(arrayBuffer) {
                if (typeof pdfjsLib === 'undefined') {
                    throw new Error('Biblioteca PDF.js no disponible.');
                }
                pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
                const loadingTask = pdfjsLib.getDocument({ data: arrayBuffer });
                const pdf = await loadingTask.promise;
                let fullText = '';
                for (let i = 1; i <= pdf.numPages; i++) {
                    const page = await pdf.getPage(i);
                    const textContent = await page.getTextContent();
                    const pageText = textContent.items.map(item => item.str).join(' ');
                    fullText += `\n--- Página ${i} ---\n` + pageText;
                }
                return { text: fullText.trim(), pageCount: pdf.numPages };
            }

            async function extractTextFromDocx(arrayBuffer) {
                if (typeof mammoth === 'undefined') {
                    throw new Error('Biblioteca Mammoth.js no disponible.');
                }
                const result = await mammoth.extractRawText({ arrayBuffer: arrayBuffer });
                return result.value || '';
            }

            function readFileAsText(file) {
                return new Promise((resolve, reject) => {
                    const reader = new FileReader();
                    reader.onload = () => resolve(reader.result);
                    reader.onerror = () => reject(reader.error);
                    reader.readAsText(file, 'utf-8');
                });
            }

            function readFileAsArrayBuffer(file) {
                return new Promise((resolve, reject) => {
                    const reader = new FileReader();
                    reader.onload = () => resolve(reader.result);
                    reader.onerror = () => reject(reader.error);
                    reader.readAsArrayBuffer(file);
                });
            }

            function getExtension(name) {
                const idx = name.lastIndexOf('.');
                return idx >= 0 ? name.substring(idx + 1).toLowerCase() : 'txt';
            }

            async function processFiles(fileList) {
                const bar = document.getElementById('attachedDocsBar');
                if (bar) bar.style.display = 'flex';

                for (const file of fileList) {
                    if (file.size > 30 * 1024 * 1024) {
                        alert('El archivo "' + file.name + '" supera el límite de 30 MB.');
                        continue;
                    }

                    const ext = getExtension(file.name);
                    const loadingChip = document.createElement('div');
                    loadingChip.className = 'attached-doc-loading';
                    loadingChip.innerHTML = `<span class="status-spinner"></span> Procesando "${escapeHtml(file.name)}"...`;
                    if (bar) bar.appendChild(loadingChip);

                    try {
                        let content = '';
                        let pageCount = null;

                        if (ext === 'pdf') {
                            const buffer = await readFileAsArrayBuffer(file);
                            const pdfRes = await extractTextFromPdf(buffer);
                            content = pdfRes.text;
                            pageCount = pdfRes.pageCount;
                        } else if (ext === 'docx') {
                            const buffer = await readFileAsArrayBuffer(file);
                            content = await extractTextFromDocx(buffer);
                        } else {
                            content = await readFileAsText(file);
                        }

                        if (!content || content.trim().length === 0) {
                            alert('No se pudo extraer texto del archivo "' + file.name + '". Comprueba que contenga texto legible.');
                            loadingChip.remove();
                            continue;
                        }

                        const wordCount = content.trim().split(/\s+/).length;

                        pendingAttachedDocuments.push({
                            fileName: file.name,
                            fileType: ext,
                            sizeBytes: file.size,
                            pageCount: pageCount,
                            wordCount: wordCount,
                            content: content
                        });
                    } catch (err) {
                        console.error('Error al procesar archivo:', err);
                        alert('Error al leer el archivo "' + file.name + '": ' + err.message);
                    } finally {
                        loadingChip.remove();
                    }
                }
                renderAttachedDocsBar();
            }

            function removeAttachedDoc(index) {
                pendingAttachedDocuments.splice(index, 1);
                renderAttachedDocsBar();
            }

            function renderAttachedDocsBar() {
                const bar = document.getElementById('attachedDocsBar');
                if (!bar) return;
                const loadingChips = Array.from(bar.querySelectorAll('.attached-doc-loading'));
                bar.innerHTML = '';
                loadingChips.forEach(c => bar.appendChild(c));

                if (pendingAttachedDocuments.length === 0 && loadingChips.length === 0) {
                    bar.style.display = 'none';
                    return;
                }
                bar.style.display = 'flex';

                pendingAttachedDocuments.forEach((doc, idx) => {
                    const chip = document.createElement('div');
                    chip.className = 'attached-doc-chip';
                    const formattedSize = doc.sizeBytes < 1024 ? doc.sizeBytes + ' B' : (doc.sizeBytes / 1024).toFixed(1) + ' KB';
                    const pageText = doc.pageCount ? ` - ${doc.pageCount} pág.` : '';
                    const wordText = doc.wordCount ? ` - ${doc.wordCount} pal.` : '';
                    chip.innerHTML = `
                        <svg class="icon" viewBox="0 0 24 24" style="width: 12px; height: 12px; flex-shrink: 0;"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg>
                        <span class="doc-name" title="${escapeHtml(doc.fileName)}">${escapeHtml(doc.fileName)}</span>
                        <span class="doc-size">(${formattedSize}${pageText || wordText})</span>
                        <span class="doc-remove" onclick="removeAttachedDoc(${idx})" title="Eliminar archivo">&times;</span>
                    `;
                    bar.appendChild(chip);
                });
            }

            function setupDragAndDrop() {
                const panel = document.getElementById('plannerChatPanel') || document.querySelector('.panel.planner-chat');
                const overlay = document.getElementById('chatDropOverlay');
                if (!panel || !overlay) return;

                let dragCounter = 0;

                ['dragenter', 'dragover'].forEach(eventName => {
                    window.addEventListener(eventName, (e) => {
                        if (e.dataTransfer && Array.from(e.dataTransfer.types).includes('Files')) {
                            e.preventDefault();
                        }
                    }, false);
                });

                panel.addEventListener('dragenter', (e) => {
                    if (e.dataTransfer && Array.from(e.dataTransfer.types).includes('Files')) {
                        e.preventDefault();
                        dragCounter++;
                        overlay.style.display = 'flex';
                    }
                }, false);

                panel.addEventListener('dragover', (e) => {
                    if (e.dataTransfer && Array.from(e.dataTransfer.types).includes('Files')) {
                        e.preventDefault();
                    }
                }, false);

                panel.addEventListener('dragleave', (e) => {
                    e.preventDefault();
                    dragCounter--;
                    if (dragCounter <= 0) {
                        dragCounter = 0;
                        overlay.style.display = 'none';
                    }
                }, false);

                panel.addEventListener('drop', async (e) => {
                    e.preventDefault();
                    dragCounter = 0;
                    overlay.style.display = 'none';
                    const dt = e.dataTransfer;
                    if (dt && dt.files && dt.files.length > 0) {
                        await processFiles(Array.from(dt.files));
                    }
                }, false);
            }

            async function sendChatMessage(customMsg) {
                const input = document.getElementById('chatInput');
                const message = customMsg || input.value.trim();
                if (!message && pendingAttachedDocuments.length === 0) return;

                const docsToSend = [...pendingAttachedDocuments];
                pendingAttachedDocuments = [];
                renderAttachedDocsBar();

                if (!customMsg) {
                    input.value = '';
                    input.style.height = 'auto';
                }

                appendChatMessage('user', message || '(Documentos adjuntos enviados)', [], new Date().toISOString(), docsToSend);

                const feed = document.getElementById('chatFeed');

                // Create streaming assistant message bubble immediately
                const assistantBubble = document.createElement('div');
                assistantBubble.className = 'message assistant';

                const meta = document.createElement('div');
                meta.className = 'message-meta';
                const sender = document.createElement('span');
                sender.className = 'message-sender';
                sender.textContent = 'DEVTOOLS AI';
                const time = document.createElement('span');
                time.className = 'message-time';
                time.textContent = formatMessageTime(new Date());
                meta.appendChild(sender);
                meta.appendChild(time);
                assistantBubble.appendChild(meta);

                const contentSpan = document.createElement('div');
                contentSpan.className = 'stream-content message-body';
                assistantBubble.appendChild(contentSpan);

                const cursorSpan = document.createElement('span');
                cursorSpan.className = 'stream-cursor';
                assistantBubble.appendChild(cursorSpan);

                feed.appendChild(assistantBubble);
                feed.scrollTop = feed.scrollHeight;

                let accumulatedText = '';
                let streamDone = false;

                try {
                    const res = await fetch('/api/planning/chat/stream', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            sessionId: currentSessionId,
                            projectId: currentProjectId,
                            userMessage: message || 'Procesa y analiza detalladamente los documentos adjuntos.',
                            currentAnswers: currentAnswers,
                            attachedDocuments: docsToSend
                        })
                    });

                    if (!res.ok) throw new Error('Error al contactar con el planificador (' + res.status + ').');

                    const reader = res.body.getReader();
                    const decoder = new TextDecoder('utf-8');
                    let buffer = '';

                    while (true) {
                        const { done, value } = await reader.read();
                        if (done) break;

                        buffer += decoder.decode(value, { stream: true });
                        const lines = buffer.split('\n\n');
                        buffer = lines.pop();

                        for (const block of lines) {
                            if (!block.startsWith('data: ')) continue;
                            const jsonStr = block.substring(6).trim();
                            if (!jsonStr) continue;

                            try {
                                const chunk = JSON.parse(jsonStr);

                                // Check if chunk propagated a new or switched ProjectId
                                if (chunk.projectId && chunk.projectId !== currentProjectId) {
                                    currentProjectId = chunk.projectId;
                                    isNewProjectMode = false;
                                    if (chunk.projectName) {
                                        document.getElementById('activeProjectChatTitle').textContent = chunk.projectName;
                                        document.getElementById('bpHeaderName').textContent = chunk.projectName;
                                    }
                                    document.getElementById('btnEditCurrentProject').style.display = 'inline-flex';
                                    document.getElementById('btnClearChat').style.display = 'inline-flex';
                                    document.getElementById('btnDeleteCurrentProject').style.display = 'inline-flex';
                                }

                                if (chunk.type === 'token') {
                                    accumulatedText += chunk.content || '';
                                    contentSpan.innerHTML = renderMarkdown(accumulatedText);
                                    feed.scrollTop = feed.scrollHeight;
                                } else if (chunk.type === 'action') {
                                    const act = chunk.actionResult;
                                    accumulatedText = chunk.content || '';
                                    contentSpan.innerHTML = renderMarkdown(accumulatedText);

                                    if (act) {
                                        if (act.isProjectDeleted) {
                                            cursorSpan.remove();
                                            startNewProjectSession();
                                            await loadProjectsSidebar();
                                            return;
                                        }
                                        if (act.actionType === 'ClearChat') {
                                            cursorSpan.remove();
                                            document.getElementById('chatFeed').innerHTML = '';
                                            appendChatMessage('assistant', accumulatedText, chunk.suggestions, new Date().toISOString());
                                            return;
                                        }
                                        if (act.actionType === 'UpdateProject' || act.actionType === 'ScaffoldProject' || act.actionType === 'UpdateTechStack') {
                                            await loadProjectsSidebar();
                                            if (act.affectedEntityName) {
                                                document.getElementById('activeProjectChatTitle').textContent = act.affectedEntityName;
                                                document.getElementById('bpHeaderName').textContent = act.affectedEntityName;
                                            }
                                        }
                                    }
                                } else if (chunk.type === 'done') {
                                    streamDone = true;
                                    cursorSpan.remove();
                                    if (chunk.content) {
                                        accumulatedText = chunk.content;
                                        contentSpan.innerHTML = renderMarkdown(accumulatedText);
                                    }
                                    if (chunk.projectId) {
                                        currentProjectId = chunk.projectId;
                                        isNewProjectMode = false;
                                    }
                                    if (chunk.projectName) {
                                        document.getElementById('activeProjectChatTitle').textContent = chunk.projectName;
                                        document.getElementById('bpHeaderName').textContent = chunk.projectName;
                                    }
                                    if (chunk.blueprint) {
                                        currentBlueprint = chunk.blueprint;
                                        displayBlueprint(chunk.blueprint);
                                    }
                                    if (chunk.suggestions && chunk.suggestions.length > 0) {
                                        const chipsDiv = document.createElement('div');
                                        chipsDiv.className = 'chip-container';
                                        chunk.suggestions.forEach(q => {
                                            const chip = document.createElement('span');
                                            chip.className = 'chip';
                                            chip.textContent = q;
                                            chip.onclick = () => sendChatMessage(q);
                                            chipsDiv.appendChild(chip);
                                        });
                                        assistantBubble.appendChild(chipsDiv);
                                    }

                                    await loadProjectsSidebar();
                                    if (currentProjectId) {
                                        const cur = projectsCache.find(p => p.id === currentProjectId);
                                        if (cur) document.getElementById('activeProjectChatTitle').textContent = cur.name;
                                        document.getElementById('btnEditCurrentProject').style.display = 'inline-flex';
                                        document.getElementById('btnClearChat').style.display = 'inline-flex';
                                        document.getElementById('btnDeleteCurrentProject').style.display = 'inline-flex';
                                    }
                                    feed.scrollTop = feed.scrollHeight;
                                }
                            } catch (pe) {
                                console.error('Error parsing SSE chunk:', pe);
                            }
                        }
                    }

                    if (!streamDone) {
                        cursorSpan.remove();
                    }
                } catch (err) {
                    cursorSpan.remove();
                    contentSpan.innerHTML = renderMarkdown('Error al procesar tu mensaje: ' + err.message);
                }
            }

            function switchTokenSubView(mode) {
                const btnCss = document.getElementById('btnTokensCssView');
                const btnLive = document.getElementById('btnTokensLiveView');
                const boxCss = document.getElementById('tokensCssContainer');
                const boxLive = document.getElementById('tokensLiveContainer');

                if (mode === 'css') {
                    btnCss.classList.add('active');
                    btnLive.classList.remove('active');
                    boxCss.style.display = 'block';
                    boxLive.style.display = 'none';
                } else {
                    btnLive.classList.add('active');
                    btnCss.classList.remove('active');
                    boxCss.style.display = 'none';
                    boxLive.style.display = 'block';
                    renderLiveComponentPreview(currentBlueprint);
                }
            }

            function renderLiveComponentPreview(bp) {
                if (!bp) return;
                const spec = bp.frontendDesignSpec;
                if (spec && spec.colorPalette) {
                    const box = document.getElementById('livePreviewBox');
                    const primary = spec.colorPalette['Accent'] || spec.colorPalette['Primary'] || '#3b82f6';
                    const surface = spec.colorPalette['Surface'] || '#14171d';
                    const text = spec.colorPalette['TextPrimary'] || '#f0f3f6';

                    box.style.backgroundColor = surface;
                    box.style.color = text;
                    box.querySelectorAll('.live-kpi-val').forEach(el => el.style.color = primary);
                }
            }

            async function scaffoldProjectOnDiskCustom() {
                if (!currentProjectId) {
                    alert('Por favor selecciona o crea un proyecto antes de generar el scaffolding.');
                    return;
                }

                const pathInput = document.getElementById('txtScaffoldPath');
                const targetPath = pathInput ? pathInput.value.trim() : '';

                try {
                    const res = await fetch('/api/planning/projects/' + currentProjectId + '/scaffold', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ targetPath: targetPath })
                    });
                    const data = await res.json();
                    if (data.success) {
                        alert('¡Scaffolding generado con éxito!\n\nRuta: ' + data.outputPath + '\nArchivos creados: ' + data.totalFilesCreated);
                    } else {
                        alert('Aviso al generar scaffolding: ' + data.message);
                    }
                } catch (e) {
                    alert('Error al generar scaffolding en disco: ' + e.message);
                }
            }

            function scaffoldProjectOnDisk() {
                scaffoldProjectOnDiskCustom();
            }

            function downloadProjectZip() {
                if (!currentProjectId) {
                    alert('Por favor selecciona o crea un proyecto antes de descargar el ZIP.');
                    return;
                }
                window.location.href = '/api/planning/projects/' + currentProjectId + '/export-zip';
            }

            function downloadProjectDocs() {
                if (!currentProjectId) {
                    alert('Por favor selecciona o crea un proyecto antes de descargar los documentos.');
                    return;
                }
                window.location.href = '/api/planning/projects/' + currentProjectId + '/export-docs';
            }

            function downloadProjectAgentsMd() {
                if (!currentProjectId) {
                    alert('Por favor selecciona o crea un proyecto antes de exportar AGENTS.md.');
                    return;
                }
                window.location.href = '/api/planning/projects/' + currentProjectId + '/export-agents';
            }

            function applyQuickTemplate(promptText) {
                sendChatMessage(promptText);
            }

            function escapeHtml(str) {
                if (!str) return '';
                return str
                    .replace(/&/g, '&amp;')
                    .replace(/</g, '&lt;')
                    .replace(/>/g, '&gt;')
                    .replace(/"/g, '&quot;');
            }

            function renderMarkdown(md) {
                if (!md) return '';

                // 1. Extract Hermes Thought Scratchpad blocks
                const thoughtBlocks = [];
                let text = md.replace(/<details class="hermes-thought-card">[\s\S]*?<summary>(.*?)<\/summary>([\s\S]*?)<\/details>/gi, function(match, summary, body) {
                    const idx = thoughtBlocks.length;
                    thoughtBlocks.push(`<details class="hermes-thought-card" open><summary class="hermes-thought-summary"><span class="hermes-badge">Hermes 3</span> ${escapeHtml(summary)}</summary><div class="hermes-thought-body">${escapeHtml(body.trim())}</div></details>`);
                    return `___THOUGHTBLOCK_${idx}___`;
                });
                text = text.replace(/<thought>([\s\S]*?)<\/thought>/gi, function(match, body) {
                    const idx = thoughtBlocks.length;
                    thoughtBlocks.push(`<details class="hermes-thought-card" open><summary class="hermes-thought-summary"><span class="hermes-badge">Hermes 3</span> Razonamiento Arquitectonico</summary><div class="hermes-thought-body">${escapeHtml(body.trim())}</div></details>`);
                    return `___THOUGHTBLOCK_${idx}___`;
                });

                // 1.5 Extract Attached Documents block (from history)
                const attachedCards = [];
                text = text.replace(/### DOCUMENTOS ADJUNTOS \/ CONTEXTO T[EÉ]CNICO PROPORCIONADO POR EL USUARIO:[\s\S]*?(?=### REQUERIMIENTO \/ MENSAJE DEL USUARIO:|$)/gi, function(docBlock) {
                    let cardHtml = '<div style="margin-bottom: 10px;">';
                    const regex = /--- INICIO DOCUMENTO: (.*?) \((.*?), tipo: (.*?)\) ---([\s\S]*?)--- FIN DOCUMENTO: \1 ---/gi;
                    let m;
                    let count = 0;
                    while ((m = regex.exec(docBlock)) !== null) {
                        count++;
                        const fname = escapeHtml(m[1].trim());
                        const fsize = escapeHtml(m[2].trim());
                        const ftype = escapeHtml(m[3].trim());
                        const fcontent = escapeHtml(m[4].trim());
                        const words = fcontent.split(/\s+/).filter(Boolean).length;
                        cardHtml += `<div class="attached-doc-card">
                            <div class="attached-doc-card-header">
                                <svg class="icon" viewBox="0 0 24 24" style="width: 14px; height: 14px;"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg>
                                <strong>${fname}</strong>
                                <span class="attached-doc-card-meta">${fsize} (${ftype}) - ${words} palabras</span>
                            </div>
                            <details class="attached-doc-preview-details">
                                <summary>Ver texto extraído del documento</summary>
                                <pre class="attached-doc-preview-text">${fcontent.substring(0, 3000)}${fcontent.length > 3000 ? '\n... (truncado para vista previa)' : ''}</pre>
                            </details>
                        </div>`;
                    }
                    cardHtml += '</div>';
                    if (count > 0) {
                        const idx = attachedCards.length;
                        attachedCards.push(cardHtml);
                        return `___ATTACHEDBLOCK_${idx}___`;
                    }
                    return docBlock;
                });
                text = text.replace(/### REQUERIMIENTO \/ MENSAJE DEL USUARIO:\s*/gi, '');

                // 2. Extract code blocks with triple backticks and replace with placeholders
                const codeBlocks = [];
                text = text.replace(/```([a-zA-Z0-9_-]*)\r?\n([\s\S]*?)```/g, function(match, lang, code) {
                    const idx = codeBlocks.length;
                    const escaped = escapeHtml(code.trim());
                    const langBadge = lang ? `<div style="font-size: 10px; text-transform: uppercase; color: var(--text-muted); margin-bottom: 6px; font-weight: 600; letter-spacing: 0.05em;">${escapeHtml(lang)}</div>` : '';
                    codeBlocks.push(`<div class="code-box" style="margin: 10px 0; max-height: 300px;">${langBadge}${escaped}</div>`);
                    return `___CODEBLOCK_${idx}___`;
                });

                // 3. Parse Markdown Tables
                text = text.replace(/((?:\|[^\n]+\|\r?\n?)+)/g, function(tableMatch) {
                    const lines = tableMatch.trim().split(/\r?\n/).map(l => l.trim()).filter(Boolean);
                    if (lines.length < 2) return tableMatch;

                    let html = '<div class="chat-table-wrapper"><table class="chat-table">';
                    let isHeader = true;

                    for (let i = 0; i < lines.length; i++) {
                        const line = lines[i];
                        // Check if separator line (|---|---|)
                        if (/^\|[\s\-:|]+\|$/.test(line)) {
                            isHeader = false;
                            continue;
                        }

                        const cells = line.split('|').slice(1, -1).map(c => c.trim());
                        html += '<tr>';
                        cells.forEach(cell => {
                            if (isHeader) {
                                html += `<th>${cell}</th>`;
                            } else {
                                html += `<td>${cell}</td>`;
                            }
                        });
                        html += '</tr>';

                        if (isHeader && i === 0 && lines.length > 1 && /^\|[\s\-:|]+\|$/.test(lines[1])) {
                            // next line is separator
                        } else if (isHeader) {
                            isHeader = false;
                        }
                    }

                    html += '</table></div>';
                    return html;
                });

                // 4. Headings
                text = text.replace(/^#### (.*$)/gim, '<div style="font-weight: 600; font-size: 12px; color: var(--accent-blue); margin: 10px 0 4px 0;">$1</div>');
                text = text.replace(/^### (.*$)/gim, '<div style="font-weight: 600; font-size: 13px; color: var(--text-primary); margin: 12px 0 4px 0;">$1</div>');
                text = text.replace(/^## (.*$)/gim, '<div style="font-weight: 700; font-size: 14px; color: var(--text-primary); margin: 14px 0 6px 0;">$1</div>');
                text = text.replace(/^# (.*$)/gim, '<div style="font-weight: 700; font-size: 15px; color: var(--text-primary); margin: 16px 0 6px 0;">$1</div>');

                // 5. Bold and Inline Code
                text = text.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
                text = text.replace(/`([^`]+)`/g, function(match, code) {
                    return `<code>${escapeHtml(code)}</code>`;
                });

                // 6. Bullet points (- or *)
                text = text.replace(/^\s*[\-\*]\s+(.*$)/gim, '<div class="chat-list-row"><span class="chat-bullet">&bull;</span><div class="chat-list-body">$1</div></div>');

                // 7. Numbered lists (1. 2. etc.)
                text = text.replace(/^\s*(\d+)\.\s+(.*$)/gim, '<div class="chat-list-row"><span class="chat-num">$1.</span><div class="chat-list-body">$2</div></div>');

                // 8. Linebreaks and paragraph spacing
                text = text.replace(/\n\n/g, '<div style="height: 6px;"></div>');
                text = text.replace(/\n/g, '<br>');

                // 9. Restore code blocks
                codeBlocks.forEach((block, idx) => {
                    text = text.replace(`___CODEBLOCK_${idx}___`, block);
                });

                // 10. Restore thought blocks
                thoughtBlocks.forEach((block, idx) => {
                    text = text.replace(`___THOUGHTBLOCK_${idx}___`, block);
                });

                // 11. Restore attached document cards
                attachedCards.forEach((card, idx) => {
                    text = text.replace(`___ATTACHEDBLOCK_${idx}___`, card);
                });

                return text;
            }

            function appendChatMessage(role, text, suggestedQuestions = [], timestamp = null, attachedDocs = []) {
                const feed = document.getElementById('chatFeed');
                const div = document.createElement('div');
                div.className = 'message ' + role;

                const meta = document.createElement('div');
                meta.className = 'message-meta';

                const sender = document.createElement('span');
                sender.className = 'message-sender';
                sender.textContent = role === 'user' ? 'TÚ' : 'DEVTOOLS AI';

                const time = document.createElement('span');
                time.className = 'message-time';
                time.textContent = formatMessageTime(timestamp || new Date());

                meta.appendChild(sender);
                meta.appendChild(time);
                div.appendChild(meta);

                const bodyDiv = document.createElement('div');
                bodyDiv.className = 'message-body';

                if (attachedDocs && attachedDocs.length > 0) {
                    attachedDocs.forEach(d => {
                        const card = document.createElement('div');
                        card.className = 'attached-doc-card';
                        const sizeStr = d.sizeBytes < 1024 ? d.sizeBytes + ' B' : (d.sizeBytes / 1024).toFixed(1) + ' KB';
                        const words = d.wordCount || (d.content ? d.content.trim().split(/\s+/).length : 0);
                        const pages = d.pageCount ? ` - ${d.pageCount} páginas` : '';
                        card.innerHTML = `
                            <div class="attached-doc-card-header">
                                <svg class="icon" viewBox="0 0 24 24" style="width: 14px; height: 14px;"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline></svg>
                                <strong>${escapeHtml(d.fileName)}</strong>
                                <span class="attached-doc-card-meta">${sizeStr}${pages} (${words} palabras)</span>
                            </div>
                            <details class="attached-doc-preview-details">
                                <summary>Ver texto extraído del documento</summary>
                                <pre class="attached-doc-preview-text">${escapeHtml((d.content || '').substring(0, 3000))}${(d.content || '').length > 3000 ? '\n... (truncado para vista previa)' : ''}</pre>
                            </details>
                        `;
                        bodyDiv.appendChild(card);
                    });
                }

                const contentSpan = document.createElement('div');
                contentSpan.innerHTML = renderMarkdown(text);
                bodyDiv.appendChild(contentSpan);

                div.appendChild(bodyDiv);

                if (suggestedQuestions && suggestedQuestions.length > 0) {
                    const chipsDiv = document.createElement('div');
                    chipsDiv.className = 'chip-container';
                    suggestedQuestions.forEach(q => {
                        const chip = document.createElement('span');
                        chip.className = 'chip';
                        chip.textContent = q;
                        chip.onclick = () => sendChatMessage(q);
                        chipsDiv.appendChild(chip);
                    });
                    div.appendChild(chipsDiv);
                }

                feed.appendChild(div);
                feed.scrollTop = feed.scrollHeight;
            }

            async function loadEngineStatus() {
                try {
                    const res = await fetch('/api/ai/status');
                    if (!res.ok) return;
                    const data = await res.json();

                    const dot = document.getElementById('engineDot');
                    const sel = document.getElementById('engineSelect');
                    sel.innerHTML = '';

                    if (data.isOllamaOnline) {
                        dot.className = 'engine-status-dot';
                        dot.title = 'Ollama Conectado';

                        // Add options for installed models
                        let hasHermes = false;
                        (data.installedModels || []).forEach(m => {
                            const opt = document.createElement('option');
                            opt.value = m;
                            const isHermes = m.toLowerCase().includes('hermes');
                            if (isHermes) hasHermes = true;
                            const hermesTag = isHermes ? ' [Hermes 3 Agente]' : '';
                            opt.textContent = m + hermesTag + (m === data.activeModel ? ' [Activo]' : '');
                            if (m === data.activeModel) opt.selected = true;
                            sel.appendChild(opt);
                        });

                        // Option to pull hermes3:8b if not present
                        if (!hasHermes) {
                            const optPull = document.createElement('option');
                            optPull.value = '__pull_hermes__';
                            optPull.textContent = '+ Descargar hermes3:8b (Nous Hermes 3 Agente)';
                            sel.appendChild(optPull);
                        }

                        // Offline option
                        const optOff = document.createElement('option');
                        optOff.value = 'offline';
                        optOff.textContent = 'Heurísticas Offline';
                        if (data.provider === 'offline') optOff.selected = true;
                        sel.appendChild(optOff);
                    } else {
                        dot.className = 'engine-status-dot offline';
                        dot.title = 'Ollama Desconectado (Modo Offline)';
                        const opt = document.createElement('option');
                        opt.value = 'offline';
                        opt.textContent = 'Modo Heurístico (Ollama Offline)';
                        sel.appendChild(opt);
                    }
                } catch (e) {
                    console.error('Error loading AI engine status', e);
                }
            }

            async function switchEngineModel(model) {
                if (model === '__pull_hermes__') {
                    const confirmPull = confirm('¿Deseas descargar el modelo Nous Hermes 3 (hermes3:8b, ~4.7 GB) en Ollama para dotar al agente de razonamiento avanzado con scratchpad (<thought>)?');
                    if (!confirmPull) {
                        await loadEngineStatus();
                        return;
                    }

                    const sel = document.getElementById('engineSelect');
                    sel.disabled = true;
                    if (sel.options[sel.selectedIndex]) {
                        sel.options[sel.selectedIndex].textContent = 'Descargando hermes3:8b en Ollama (aguarda)...';
                    }

                    try {
                        const res = await fetch('/api/ai/pull-model', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ model: 'hermes3:8b' })
                        });
                        const data = await res.json();
                        if (data.success) {
                            alert(data.message);
                        } else {
                            alert('Aviso al descargar modelo: ' + data.message);
                        }
                    } catch (e) {
                        alert('Error al descargar modelo en Ollama: ' + e.message);
                    } finally {
                        sel.disabled = false;
                        await loadEngineStatus();
                    }
                    return;
                }

                try {
                    const res = await fetch('/api/ai/switch-model', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ model: model })
                    });
                    if (res.ok) {
                        await loadEngineStatus();
                    }
                } catch (e) {
                    console.error('Error switching model', e);
                }
            }

            function displayBlueprint(bp) {
                document.getElementById('bpHeaderName').textContent = bp.projectName;

                const pathInput = document.getElementById('txtScaffoldPath');
                if (pathInput && (!pathInput.value || pathInput.value.includes('Proyectos'))) {
                    pathInput.value = 'C:\\Proyectos\\' + bp.projectName.replace(/\s+/g, '');
                }

                // Tech stack table
                const stackBody = document.querySelector('#techStackTable tbody');
                stackBody.innerHTML = '';
                for (const [k, v] of Object.entries(bp.techStack || {})) {
                    const tr = document.createElement('tr');
                    tr.innerHTML = `<td style="font-weight: 500; color: var(--text-primary);">${k}</td><td>${v}</td>`;
                    stackBody.appendChild(tr);
                }

                // Conventions
                const convList = document.getElementById('conventionsList');
                convList.innerHTML = '';
                (bp.keyConventions || []).forEach(c => {
                    const li = document.createElement('li');
                    li.textContent = c;
                    convList.appendChild(li);
                });

                // Tokens CSS
                if (bp.frontendDesignSpec) {
                    document.getElementById('tokensThemeName').textContent = bp.frontendDesignSpec.themeName;
                    document.getElementById('tokensCssBox').textContent = bp.frontendDesignSpec.tokensCss || '/* No CSS tokens */';
                }
                renderLiveComponentPreview(bp);

                // ADR
                document.getElementById('adrTitle').textContent = bp.initialAdrTitle;
                document.getElementById('adrContentBox').textContent = bp.initialAdrContent;

                // Tree
                document.getElementById('dirTreeBox').textContent = bp.directoryStructure;

                // Spec Kit (SDD)
                currentSpecKit = bp.specKit || null;
                switchSpecKitFile(currentSpecKitActiveFile || 'constitution');

                // PRD
                const prdBox = document.getElementById('prdContentBox');
                if (prdBox) prdBox.textContent = bp.prdMarkdown || '/* PRD se generará automáticamente con el análisis */';

                // Suggestions & Roadmap
                const sugBox = document.getElementById('suggestionsContentBox');
                if (sugBox) sugBox.textContent = bp.suggestionsMarkdown || '/* Sugerencias y roadmap se generarán automáticamente */';

                // AGENTS.md
                const agBox = document.getElementById('agentsContentBox');
                if (agBox) agBox.textContent = bp.agentsMarkdown || '/* AGENTS.md se generará automáticamente */';

                // Render Mermaid C4
                renderMermaid();
            }

            function switchSpecKitFile(fileKey) {
                currentSpecKitActiveFile = fileKey;
                document.querySelectorAll('#bp-speckit .token-subview-btn').forEach(b => b.classList.remove('active'));
                if (fileKey === 'constitution') document.getElementById('btnSpeckitConst')?.classList.add('active');
                if (fileKey === 'spec') document.getElementById('btnSpeckitSpec')?.classList.add('active');
                if (fileKey === 'plan') document.getElementById('btnSpeckitPlan')?.classList.add('active');
                if (fileKey === 'tasks') document.getElementById('btnSpeckitTasks')?.classList.add('active');

                const box = document.getElementById('speckitContentBox');
                if (!box) return;

                if (!currentSpecKit) {
                    box.textContent = '/* Spec Kit no disponible aun. Escribe "genera spec kit" o guarda el plano */';
                    return;
                }

                if (fileKey === 'constitution') box.textContent = currentSpecKit.constitutionMarkdown || '/* constitution.md vacio */';
                else if (fileKey === 'spec') box.textContent = currentSpecKit.specMarkdown || '/* spec.md vacio */';
                else if (fileKey === 'plan') box.textContent = currentSpecKit.planMarkdown || '/* plan.md vacio */';
                else if (fileKey === 'tasks') box.textContent = currentSpecKit.tasksMarkdown || '/* tasks.md vacio */';
            }

            function downloadProjectSpecKitZip() {
                if (!currentProjectId) {
                    alert('Por favor selecciona o crea un proyecto antes de descargar el Spec Kit.');
                    return;
                }
                window.location.href = '/api/planning/projects/' + currentProjectId + '/export-speckit';
            }

            function downloadProjectPrd() {
                if (!currentProjectId) {
                    alert('Por favor selecciona o crea un proyecto antes de descargar el PRD.');
                    return;
                }
                window.location.href = '/api/planning/projects/' + currentProjectId + '/export-prd';
            }

            function copyBoxContent(boxId, btnEl) {
                const box = document.getElementById(boxId);
                if (!box) return;
                const text = box.textContent || '';
                if (!text) return;
                navigator.clipboard.writeText(text).then(() => {
                    if (btnEl) {
                        const span = btnEl.querySelector('span');
                        if (span) {
                            const orig = span.textContent;
                            span.textContent = 'Copiado!';
                            setTimeout(() => { span.textContent = orig; }, 1800);
                        }
                    }
                }).catch(err => {
                    console.error('Error al copiar:', err);
                });
            }

            function renderMermaid() {
                if (!currentBlueprint || !currentBlueprint.c4DiagramMermaid) return;
                const container = document.getElementById('mermaidContainer');
                container.innerHTML = '<div class="mermaid">' + currentBlueprint.c4DiagramMermaid + '</div>';
                mermaid.init(undefined, container.querySelectorAll('.mermaid'));
            }

            async function refreshProjects() {
                try {
                    const res = await fetch('/api/planning/projects');
                    const projects = await res.json();
                    const tbody = document.querySelector('#projectsTable tbody');
                    tbody.innerHTML = '';
                    if (projects.length === 0) {
                        tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; color: var(--text-muted);">No hay proyectos registrados en SQLite aun.</td></tr>';
                        return;
                    }
                    projects.forEach(p => {
                        const tr = document.createElement('tr');
                        const arch = p.architecturalStyle ? p.architecturalStyle.split('/')[0].trim() : 'Clean Arch';
                        const safeName = escapeHtml(p.name);
                        const safePath = escapeHtml(p.rootPath);
                        tr.innerHTML = `
                            <td><span class="badge-tag badge-muted">${p.id.substring(0, 8)}</span></td>
                            <td style="font-weight: 500; color: var(--text-primary); cursor: pointer; max-width: 180px;" class="cell-ellipsis" title="${safeName}" onclick="switchToProjectPlanner('${p.id}')">${safeName}</td>
                            <td style="font-family: 'JetBrains Mono', monospace; font-size: 11px; max-width: 250px;" class="cell-ellipsis" title="${safePath}">${safePath}</td>
                            <td><span class="badge-tag badge-blue">${p.messageCount} msg</span></td>
                            <td class="cell-ellipsis" style="max-width: 140px;" title="${escapeHtml(arch)}"><span class="badge-tag badge-green">${escapeHtml(arch)}</span></td>
                            <td>${new Date(p.createdAt).toLocaleDateString()}</td>
                            <td>
                                <div style="display: flex; gap: 6px;">
                                    <button class="btn-table-action" onclick="renameProject('${p.id}', '${safeName}', event)">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"></path></svg>
                                        <span>Editar</span>
                                    </button>
                                    <button class="btn-table-action danger" onclick="deleteProject('${p.id}', '${safeName}', event)">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                        <span>Eliminar</span>
                                    </button>
                                </div>
                            </td>
                        `;
                        tbody.appendChild(tr);
                    });
                } catch (e) { console.error(e); }
            }

            function switchToProjectPlanner(projectId) {
                switchTab('tab-planner');
                selectProject(projectId);
            }

            async function refreshKnowledge() {
                try {
                    const [resK, resD] = await Promise.all([fetch('/api/knowledge'), fetch('/api/documents')]);
                    const items = await resK.json();
                    const docs = await resD.json();

                    const tbodyK = document.querySelector('#knowledgeTable tbody');
                    tbodyK.innerHTML = '';
                    if (items.length === 0) {
                        tbodyK.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--text-muted);">Sin reglas de conocimiento registradas.</td></tr>';
                    } else {
                        items.forEach(k => {
                            const tr = document.createElement('tr');
                            tr.innerHTML = `
                                <td style="font-weight: 500; color: var(--text-primary); max-width: 160px;" class="cell-ellipsis" title="${escapeHtml(k.title)}">${escapeHtml(k.title)}</td>
                                <td><span class="badge-tag badge-blue">${escapeHtml(k.domain)}</span></td>
                                <td style="max-width: 250px;" class="cell-ellipsis" title="${escapeHtml(k.content)}">${escapeHtml(k.content)}</td>
                                <td><span class="badge-tag badge-muted">${escapeHtml(k.tags || '')}</span></td>
                                <td>${escapeHtml(k.source)}</td>
                                <td>
                                    <button class="btn-table-action danger" onclick="deleteKnowledgeRule('${k.id}', event)">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                        <span>Eliminar</span>
                                    </button>
                                </td>
                            `;
                            tbodyK.appendChild(tr);
                        });
                    }

                    const tbodyD = document.querySelector('#documentsTable tbody');
                    tbodyD.innerHTML = '';
                    if (docs.length === 0) {
                        tbodyD.innerHTML = '<tr><td colspan="6" style="text-align: center; color: var(--text-muted);">Sin registros ADR registrados.</td></tr>';
                    } else {
                        docs.forEach(d => {
                            const tr = document.createElement('tr');
                            tr.innerHTML = `
                                <td style="font-weight: 500; color: var(--text-primary); max-width: 200px;" class="cell-ellipsis" title="${escapeHtml(d.title)}">${escapeHtml(d.title)}</td>
                                <td><span class="badge-tag badge-green">${escapeHtml(d.docType)}</span></td>
                                <td>v${escapeHtml(d.version)}</td>
                                <td>#${d.projectId ? d.projectId.substring(0, 8) : 'global'}</td>
                                <td>${new Date(d.createdAt).toLocaleDateString()}</td>
                                <td>
                                    <button class="btn-table-action danger" onclick="deleteDocumentItem('${d.id}', event)">
                                        <svg class="icon" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                        <span>Eliminar</span>
                                    </button>
                                </td>
                            `;
                            tbodyD.appendChild(tr);
                        });
                    }
                } catch (e) { console.error(e); }
            }

            async function deleteProject(projectId, projectName, ev) {
                if (ev) ev.stopPropagation();
                const name = projectName || 'este proyecto';
                if (!confirm(`¿Confirmas que deseas eliminar permanentemente el proyecto "${name}" y todos sus artefactos asociados?`)) {
                    return;
                }

                try {
                    const res = await fetch('/api/planning/projects/' + projectId, { method: 'DELETE' });
                    if (!res.ok) throw new Error('No se pudo eliminar el proyecto');
                    
                    if (currentProjectId === projectId) {
                        startNewProjectSession();
                    }
                    await loadProjectsSidebar();
                    await refreshProjects();
                    await refreshOverview();
                } catch (e) {
                    alert('Error al eliminar proyecto: ' + e.message);
                }
            }

            async function renameProject(projectId, currentName, ev) {
                if (ev) ev.stopPropagation();
                const newName = prompt('Ingresa el nuevo nombre para el proyecto:', currentName);
                if (!newName || newName.trim() === '' || newName.trim() === currentName) return;

                try {
                    const res = await fetch('/api/planning/projects/' + projectId, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name: newName.trim() })
                    });
                    if (!res.ok) throw new Error('No se pudo actualizar el nombre del proyecto');
                    
                    if (currentProjectId === projectId) {
                        document.getElementById('activeProjectChatTitle').textContent = newName.trim();
                        document.getElementById('bpHeaderName').textContent = newName.trim();
                    }
                    await loadProjectsSidebar();
                    await refreshProjects();
                } catch (e) {
                    alert('Error al renombrar proyecto: ' + e.message);
                }
            }

            function renameCurrentProject() {
                if (!currentProjectId) return;
                const cur = projectsCache.find(p => p.id === currentProjectId);
                const oldName = cur ? cur.name : document.getElementById('activeProjectChatTitle').textContent;
                renameProject(currentProjectId, oldName);
            }

            function deleteCurrentProject() {
                if (!currentProjectId) return;
                const cur = projectsCache.find(p => p.id === currentProjectId);
                const name = cur ? cur.name : document.getElementById('activeProjectChatTitle').textContent;
                deleteProject(currentProjectId, name);
            }

            async function clearCurrentChat() {
                if (!currentProjectId) return;
                if (!confirm('¿Deseas reiniciar el historial de mensajes de este proyecto?')) return;

                try {
                    const res = await fetch('/api/planning/projects/' + currentProjectId + '/messages', { method: 'DELETE' });
                    if (!res.ok) throw new Error('No se pudo limpiar el chat');

                    const feed = document.getElementById('chatFeed');
                    feed.innerHTML = `
                        <div class="message assistant">
                            El historial de conversación de <strong>${document.getElementById('activeProjectChatTitle').textContent}</strong> ha sido reiniciado.
                            <br><br>
                            El plano arquitectónico y las decisiones previas se conservan activas.
                        </div>
                    `;
                    await loadProjectsSidebar();
                } catch (e) {
                    alert('Error al limpiar chat: ' + e.message);
                }
            }

            async function deleteDocumentItem(docId, ev) {
                if (ev) ev.stopPropagation();
                if (!confirm('¿Deseas eliminar este registro de decisión / documento?')) return;
                try {
                    await fetch('/api/documents/' + docId, { method: 'DELETE' });
                    await refreshKnowledge();
                } catch (e) {
                    alert('Error al eliminar documento: ' + e.message);
                }
            }

            async function deleteKnowledgeRule(ruleId, ev) {
                if (ev) ev.stopPropagation();
                if (!confirm('¿Deseas eliminar esta regla de conocimiento?')) return;
                try {
                    await fetch('/api/knowledge/' + ruleId, { method: 'DELETE' });
                    await refreshKnowledge();
                } catch (e) {
                    alert('Error al eliminar regla: ' + e.message);
                }
            }

            async function refreshToolkit() {
                try {
                    const [resP, resS] = await Promise.all([fetch('/api/toolkit/prompts'), fetch('/api/toolkit/skills')]);
                    const prompts = await resP.json();
                    const skills = await resS.json();

                    const tbodyP = document.querySelector('#promptsTable tbody');
                    tbodyP.innerHTML = '';
                    prompts.forEach(p => {
                        const tr = document.createElement('tr');
                        tr.innerHTML = `
                            <td style="font-family: 'JetBrains Mono', monospace; color: var(--text-primary);">${p.id}</td>
                            <td style="font-weight: 500;">${p.name}</td>
                            <td>${p.description}</td>
                            <td>${p.hasRules ? '<span class="badge-tag badge-green">Enforced</span>' : '<span class="badge-tag badge-muted">Standard</span>'}</td>
                        `;
                        tbodyP.appendChild(tr);
                    });

                    const tbodyS = document.querySelector('#skillsTable tbody');
                    tbodyS.innerHTML = '';
                    skills.forEach(s => {
                        const tr = document.createElement('tr');
                        tr.innerHTML = `
                            <td style="font-family: 'JetBrains Mono', monospace; color: var(--text-primary);">${s.id}</td>
                            <td style="font-weight: 500;">${s.name}</td>
                            <td>${s.description}</td>
                        `;
                        tbodyS.appendChild(tr);
                    });
                } catch (e) { console.error(e); }
            }

            async function refreshOverview() {
                try {
                    const [resP, resS, resPr] = await Promise.all([fetch('/api/toolkit/prompts'), fetch('/api/toolkit/skills'), fetch('/api/planning/projects')]);
                    const p = await resP.json();
                    const s = await resS.json();
                    const pr = await resPr.json();
                    document.getElementById('statPromptsCount').textContent = p.length;
                    document.getElementById('statSkillsCount').textContent = s.length;
                    document.getElementById('statProjectsCount').textContent = pr.length;
                } catch(e) {}
            }

            async function runCodeReview() {
                const code = document.getElementById('reviewCodeInput').value;
                const panel = document.getElementById('reviewResultPanel');
                const out = document.getElementById('reviewResultContent');

                panel.style.display = 'block';
                out.textContent = 'Ejecutando auditoria de codigo con reglas OWASP y Clean Code...';

                try {
                    const res = await fetch('/api/agents/review', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ fileName: 'Sample.cs', code: code })
                    });
                    const data = await res.json();
                    if (data.reviewReport) {
                        out.textContent = JSON.stringify(data.reviewReport, null, 2);
                    } else {
                        out.textContent = data.rawOutput || 'Auditoria completada sin incidencias criticas.';
                    }
                } catch (err) {
                    out.textContent = 'Error al ejecutar auditoria: ' + err.message;
                }
            }

            // Continuous Improvement JavaScript
            async function refreshImprovementTab() {
                try {
                    const res = await fetch('/api/improvement/latest');
                    if (res.ok) {
                        const report = await res.json();
                        if (report) {
                            renderAuditReport(report);
                            return;
                        }
                    }
                } catch(e) {}

                try {
                    const mRes = await fetch('/api/improvement/metrics');
                    if (mRes.ok) {
                        const metrics = await mRes.json();
                        renderMetricsOnly(metrics);
                    }
                } catch(e) {}
            }

            function renderMetricsOnly(metrics) {
                if (!metrics) return;
                const p = document.getElementById('impMetricProjects');
                const c = document.getElementById('impMetricCsFiles');
                const l = document.getElementById('impMetricLoc');
                const t = document.getElementById('impMetricTests');
                const a = document.getElementById('impMetricCleanArch');

                if (p) p.innerText = metrics.totalProjects || 0;
                if (c) c.innerText = metrics.totalCSharpFiles || 0;
                if (l) l.innerText = (metrics.totalLinesOfCode || 0).toLocaleString();
                if (t) t.innerText = metrics.totalTestCases || 0;
                if (a) a.innerText = metrics.cleanArchitectureCompliant ? "Conforme" : "Revisar";
            }

            function renderAuditReport(report) {
                if (!report) return;
                if (report.metrics) renderMetricsOnly(report.metrics);

                const mb = document.getElementById('impModelBadge');
                if (mb) mb.innerText = 'Modelo: ' + (report.modelUsed || 'hermes3:8b');

                if (report.qualityScores) {
                    const ob = document.getElementById('impOverallScoreBadge');
                    if (ob) ob.innerText = `Global: ${report.qualityScores.overallQualityScore}/100`;
                    const sm = document.getElementById('scoreMaintainability');
                    if (sm) sm.innerText = report.qualityScores.maintainabilityScore + '%';
                    const sr = document.getElementById('scoreReliability');
                    if (sr) sr.innerText = report.qualityScores.reliabilityScore + '%';
                    const sp = document.getElementById('scorePerformance');
                    if (sp) sp.innerText = report.qualityScores.performanceScore + '%';
                    const ss = document.getElementById('scoreSecurity');
                    if (ss) ss.innerText = report.qualityScores.securityScore + '%';
                }

                if (report.thoughtScratchpad) {
                    const tc = document.getElementById('hermesThoughtContent');
                    if (tc) tc.innerText = report.thoughtScratchpad;
                }

                const container = document.getElementById('impProposalsContainer');
                const proposals = report.proposals || [];
                const pc = document.getElementById('impProposalsCount');
                if (pc) pc.innerText = `${proposals.length} Propuestas`;

                if (!container) return;
                if (proposals.length === 0) {
                    container.innerHTML = '<div style="color: var(--text-muted); font-size: 12px; text-align: center; padding: 24px;">No se registran propuestas pendientes.</div>';
                    return;
                }

                let html = '';
                proposals.forEach(p => {
                    const impactClass = p.impact === 'Critical' ? 'badge-red' : (p.impact === 'High' ? 'badge-amber' : 'badge-blue');
                    const statusClass = p.status === 'Applied' ? 'badge-green' : (p.status === 'InProgress' ? 'badge-amber' : 'badge-blue');

                    html += `
                    <div class="imp-proposal-card">
                        <div class="imp-proposal-header">
                            <div>
                                <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 6px; flex-wrap: wrap;">
                                    <span class="badge-tag badge-blue" style="font-family: 'JetBrains Mono', monospace;">${escapeHtml(p.id)}</span>
                                    <span class="badge-tag ${impactClass}">Impacto: ${escapeHtml(p.impact)}</span>
                                    <span class="badge-tag badge-blue">${escapeHtml(p.category)}</span>
                                    <span class="badge-tag ${statusClass}" id="status-badge-${p.id}">${escapeHtml(p.status)}</span>
                                </div>
                                <div class="imp-proposal-title">${escapeHtml(p.title)}</div>
                                <div style="font-size: 11px; color: var(--accent-blue); font-family: 'JetBrains Mono', monospace; margin-top: 2px;">
                                    Archivo: ${escapeHtml(p.targetFile)}
                                </div>
                            </div>
                            <div>
                                <select onchange="changeProposalStatus('${p.id}', this.value)" style="background: var(--surface-active); border: 1px solid var(--border); color: var(--text-primary); font-size: 11px; padding: 4px 8px; border-radius: 4px; outline: none;">
                                    <option value="Pending" ${p.status === 'Pending' ? 'selected' : ''}>Pendiente</option>
                                    <option value="InProgress" ${p.status === 'InProgress' ? 'selected' : ''}>En Progreso</option>
                                    <option value="Applied" ${p.status === 'Applied' ? 'selected' : ''}>Aplicado</option>
                                    <option value="Dismissed" ${p.status === 'Dismissed' ? 'selected' : ''}>Descartado</option>
                                </select>
                            </div>
                        </div>
                        <div style="color: var(--text-secondary); font-size: 12px; margin-bottom: 8px; line-height: 1.5;">
                            ${escapeHtml(p.description)}
                        </div>
                        <div style="font-size: 11px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; margin-top: 8px;">
                            Plan de Accion Antigravity:
                        </div>
                        <div class="imp-action-plan-box">${escapeHtml(p.antigravityActionPlan)}</div>
                        <div style="font-size: 11px; color: var(--text-muted);">
                            <strong>Criterio de Verificacion:</strong> ${escapeHtml(p.verificationCriteria)}
                        </div>
                    </div>`;
                });
                container.innerHTML = html;
            }

            async function runHermesAudit() {
                const btn = document.getElementById('btnRunAudit');
                const origHtml = btn.innerHTML;
                btn.disabled = true;
                btn.innerHTML = '<span class="status-spinner" style="display:inline-block; vertical-align:middle; margin-right:6px;"></span> Auditando con Hermes 3...';

                try {
                    const res = await fetch('/api/improvement/audit', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ model: 'hermes3:8b' })
                    });
                    if (res.ok) {
                        const report = await res.json();
                        renderAuditReport(report);
                    } else {
                        alert('Error al ejecutar la auditoria de Hermes.');
                    }
                } catch(e) {
                    alert('Fallo de red al comunicar con el motor de auditoria: ' + e.message);
                } finally {
                    btn.disabled = false;
                    btn.innerHTML = origHtml;
                }
            }

            async function changeProposalStatus(proposalId, newStatus) {
                try {
                    const res = await fetch(`/api/improvement/proposals/${proposalId}/status`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ status: newStatus })
                    });
                    if (res.ok) {
                        const badge = document.getElementById(`status-badge-${proposalId}`);
                        if (badge) {
                            badge.innerText = newStatus;
                            badge.className = 'badge-tag ' + (newStatus === 'Applied' ? 'badge-green' : (newStatus === 'InProgress' ? 'badge-amber' : 'badge-blue'));
                        }
                    }
                } catch(e) {}
            }

            // Init sample code in reviewer
            document.getElementById('reviewCodeInput').value = "public class OrderService\n{\n    private string apiKey = \"sk-live-1234567890abcdef\";\n\n    public Order ProcessOrder(int orderId)\n    {\n        // Anti-pattern: sync-over-async\n        var order = FetchOrderFromApiAsync(orderId).Result;\n        return order;\n    }\n\n    private async Task<Order> FetchOrderFromApiAsync(int id) => new Order();\n}";

            // Initial load
            loadProjectsSidebar();
            refreshOverview();
            loadEngineStatus();
            setupDragAndDrop();
        </script>
    </body>
    </html>
    """;
}

public sealed record ReviewPayload(string? FileName, string? Code);
public sealed record RegisterProjectPayload(string Name, string? RootPath);
public sealed record AddKnowledgePayload(string Title, string Domain, string Content, string Tags, string Source);
public sealed record ScaffoldPayload(ProjectPlanBlueprint Blueprint, string? TargetPath);
public sealed record SwitchModelPayload(string? Provider, string? Model);
public sealed record UpdateProjectPayload(string Name, string? RootPath, string? Description);
public sealed record UpdateDocumentPayload(string Title, string MarkdownContent, string? Version);
