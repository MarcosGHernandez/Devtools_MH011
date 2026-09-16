using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using DevTools.Core.Entities;
using DevTools.Core.Interfaces;
using DevTools.Core.Models;

namespace DevTools.Orchestrator.Services;

public sealed class ProjectPlanningService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IDocumentationRepository _docRepo;
    private readonly IKnowledgeRepository _knowledgeRepo;
    private readonly IChatClient? _chatClient;
    private readonly IProjectScaffoldingService _scaffoldingService;

    public IProjectScaffoldingService ScaffoldingService => _scaffoldingService;

    public ProjectPlanningService(
        IProjectRepository projectRepo,
        IDocumentationRepository docRepo,
        IKnowledgeRepository knowledgeRepo,
        IChatClient? chatClient = null,
        IProjectScaffoldingService? scaffoldingService = null)
    {
        _projectRepo = projectRepo;
        _docRepo = docRepo;
        _knowledgeRepo = knowledgeRepo;
        _chatClient = chatClient;
        _scaffoldingService = scaffoldingService ?? new ProjectScaffoldingService();
    }

    public async Task<ProjectPlanBlueprint> SynthesizePlanAsync(
        ProjectInterviewAnswers answers,
        bool persistToDatabase = true,
        CancellationToken cancellationToken = default)
    {
        ProjectPlanBlueprint blueprint;

        if (_chatClient is not null)
        {
            blueprint = await GenerateWithLlmAsync(answers, cancellationToken);
        }
        else
        {
            blueprint = GenerateWithArchitecturalHeuristics(answers);
        }

        if (persistToDatabase)
        {
            // 1. Register or update project
            var project = await _projectRepo.RegisterProjectAsync(
                name: answers.ProjectName,
                rootPath: Path.Combine(Directory.GetCurrentDirectory(), answers.ProjectName.Replace(" ", "-").ToLowerInvariant()),
                cancellationToken: cancellationToken
            );

            // 2. Persist Initial ADR in Documentation
            await _docRepo.SaveDocumentAsync(
                title: blueprint.InitialAdrTitle,
                docType: "ADR",
                markdownContent: blueprint.InitialAdrContent,
                projectId: project.Id,
                version: "1.0.0",
                cancellationToken: cancellationToken
            );

            // 3. Persist Key Conventions in Knowledge Base
            foreach (var convention in blueprint.KeyConventions)
            {
                await _knowledgeRepo.AddKnowledgeAsync(
                    title: $"{answers.ProjectName}: {convention.Split(':')[0]}",
                    domain: "Architecture",
                    content: convention,
                    tags: $"{answers.ProjectName.ToLowerInvariant()},{answers.ArchitecturalStyle.ToLowerInvariant()}",
                    source: "ProjectDiscovery",
                    cancellationToken: cancellationToken
                );
            }
        }

        return blueprint;
    }

    public async Task<PlanningChatResponse> ProcessPlanningChatAsync(
        PlanningChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N")[..8];
        var msg = request.UserMessage.Trim();
        var effectiveMsg = BuildEffectiveUserPrompt(msg, request.AttachedDocuments);

        bool isNewProjectIntent = IsNewProjectIntent(msg) || string.IsNullOrWhiteSpace(request.ProjectId) || request.ProjectId == "null";

        ProjectEntity? project = null;
        if (!isNewProjectIntent && !string.IsNullOrWhiteSpace(request.ProjectId) && Guid.TryParse(request.ProjectId, out var pGuid))
        {
            project = await _projectRepo.GetProjectByIdAsync(pGuid, cancellationToken);
        }

        var answers = (!isNewProjectIntent && request.CurrentAnswers is not null)
            ? request.CurrentAnswers
            : new ProjectInterviewAnswers
        {
            ProjectName = isNewProjectIntent ? ExtractProjectNameFromPrompt(msg) : (project?.Name ?? request.ProjectName ?? "NuevoProyecto"),
            Description = isNewProjectIntent ? msg : (project?.Description ?? "Initial discovery in progress"),
            ArchitecturalStyle = project?.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project?.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project?.DatabaseType ?? "PostgreSQL + EF Core 9"
        };

        // 0. Check for explicit Agent CRUD Commands (Delete, Rename, Clear Chat, Modify Stack/Conventions/ADRs)
        var directAgentAction = await TryExecuteAgentActionAsync(sessionId, msg, project, answers, cancellationToken);
        if (directAgentAction is not null)
        {
            return directAgentAction;
        }

        // Parse user intent / answers incrementally
        string name = answers.ProjectName;
        string desc = answers.Description;
        string arch = answers.ArchitecturalStyle;
        string front = answers.FrontendStack;
        string db = answers.DatabaseType;
        string load = answers.ExpectedLoad;

        var lower = msg.ToLowerInvariant();

        // 1. Detect project name or domain description
        if (isNewProjectIntent || answers.Description == "Initial discovery in progress" || lower.Contains("crear") || lower.Contains("proyecto") || lower.Contains("app") || lower.Contains("saas") || lower.Contains("plataforma"))
        {
            desc = msg;
            var extracted = ExtractProjectNameFromPrompt(msg);
            if (!string.IsNullOrWhiteSpace(extracted) && extracted != "NuevoProyecto")
            {
                name = extracted;
            }
            else if (msg.Length < 30 && !msg.Contains(' '))
            {
                name = SanitizeIdentifier(msg);
            }
        }

        // 2. Detect Architecture selections
        if (lower.Contains("clean architecture") || lower.Contains("vertical slices") || lower.Contains("hexagonal"))
        {
            arch = "Clean Architecture / Vertical Slices";
        }
        else if (lower.Contains("monolith") || lower.Contains("monolito"))
        {
            arch = "Modular Monolith";
        }
        else if (lower.Contains("microservice") || lower.Contains("microservicio"))
        {
            arch = "Microservices / Event-Driven";
        }
        else if (lower.Contains("cqrs") || lower.Contains("event"))
        {
            arch = "CQRS / Event-Driven";
        }

        // 3. Detect Frontend selections
        if (lower.Contains("blazor"))
        {
            front = "Blazor WebAssembly + Classic Minimalist";
        }
        else if (lower.Contains("react"))
        {
            front = "React + Minimalist Design System";
        }
        else if (lower.Contains("next") || lower.Contains("next.js"))
        {
            front = "Next.js + Design Tokens";
        }
        else if (lower.Contains("svelte"))
        {
            front = "Svelte + Minimalist CSS";
        }
        else if (lower.Contains("cli") || lower.Contains("terminal"))
        {
            front = "CLI Terminal Only (Spectre.Console)";
        }

        // 4. Detect Database selections
        if (lower.Contains("postgres") || lower.Contains("postgresql"))
        {
            db = "PostgreSQL + EF Core 9";
        }
        else if (lower.Contains("sqlite"))
        {
            db = "SQLite Embebido (Zero-Config)";
        }
        else if (lower.Contains("sql server") || lower.Contains("sqlserver"))
        {
            db = "SQL Server + EF Core";
        }
        else if (lower.Contains("mongo") || lower.Contains("nosql"))
        {
            db = "MongoDB / DocumentDB";
        }

        var updatedAnswers = new ProjectInterviewAnswers
        {
            ProjectName = name,
            Description = desc,
            ArchitecturalStyle = arch,
            FrontendStack = front,
            DatabaseType = db,
            ExpectedLoad = load
        };

        // Ensure project exists in repository
        if (project is null)
        {
            var rootPath = Path.Combine(Directory.GetCurrentDirectory(), name.Replace(" ", "-").ToLowerInvariant());
            project = await _projectRepo.RegisterProjectAsync(name, rootPath, cancellationToken: cancellationToken);
        }

        // 1. Save user message to database history
        await _projectRepo.AddMessageAsync(project.Id, "user", effectiveMsg, cancellationToken: cancellationToken);

        // Synthesize blueprint
        var blueprint = await SynthesizePlanAsync(updatedAnswers, persistToDatabase: true, cancellationToken: cancellationToken);
        var blueprintJson = JsonSerializer.Serialize(blueprint);

        string assistantReply;
        List<string> questions;

        if (_chatClient is not null)
        {
            var (llmReply, llmQuestions) = await GenerateLlmChatTurnAsync(project, updatedAnswers, effectiveMsg, cancellationToken);
            assistantReply = llmReply;
            questions = llmQuestions;
        }
        else
        {
            var (heuristicReply, heuristicQuestions) = GenerateHeuristicChatTurn(updatedAnswers, effectiveMsg);
            assistantReply = heuristicReply;
            questions = heuristicQuestions;
        }

        var questionsJson = JsonSerializer.Serialize(questions);

        // 2. Save assistant reply to database history
        await _projectRepo.AddMessageAsync(
            project.Id,
            "assistant",
            assistantReply,
            questionsJson,
            blueprintJson,
            cancellationToken
        );

        // Update project metadata
        await _projectRepo.UpdateProjectMetadataAsync(
            project.Id,
            desc,
            arch,
            front,
            db,
            blueprintJson,
            cancellationToken
        );

        return new PlanningChatResponse
        {
            SessionId = sessionId,
            ProjectId = project.Id.ToString(),
            AssistantReply = assistantReply,
            SuggestedQuestions = questions,
            UpdatedAnswers = updatedAnswers,
            GeneratedBlueprint = blueprint,
            ReadyToScaffold = true
        };
    }

    public async IAsyncEnumerable<StreamingPlanningChunk> StreamPlanningChatAsync(
        PlanningChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N")[..8];
        var msg = request.UserMessage.Trim();
        var effectiveMsg = BuildEffectiveUserPrompt(msg, request.AttachedDocuments);

        bool isNewProjectIntent = IsNewProjectIntent(msg) || string.IsNullOrWhiteSpace(request.ProjectId) || request.ProjectId == "null";

        ProjectEntity? project = null;
        if (!isNewProjectIntent && !string.IsNullOrWhiteSpace(request.ProjectId) && Guid.TryParse(request.ProjectId, out var pGuid))
        {
            project = await _projectRepo.GetProjectByIdAsync(pGuid, cancellationToken);
        }

        var answers = (!isNewProjectIntent && request.CurrentAnswers is not null)
            ? request.CurrentAnswers
            : new ProjectInterviewAnswers
        {
            ProjectName = isNewProjectIntent ? ExtractProjectNameFromPrompt(msg) : (project?.Name ?? request.ProjectName ?? "NuevoProyecto"),
            Description = isNewProjectIntent ? msg : (project?.Description ?? "Initial discovery in progress"),
            ArchitecturalStyle = project?.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = project?.FrontendStack ?? "React + Minimalist Design System",
            DatabaseType = project?.DatabaseType ?? "PostgreSQL + EF Core 9"
        };

        // 0. Check for explicit Agent CRUD or Scaffolding Commands
        var directAgentAction = await TryExecuteAgentActionAsync(sessionId, msg, project, answers, cancellationToken);
        if (directAgentAction is not null)
        {
            yield return new StreamingPlanningChunk
            {
                Type = "action",
                ActionResult = directAgentAction.ExecutedAction,
                Content = directAgentAction.AssistantReply,
                Suggestions = directAgentAction.SuggestedQuestions,
                Blueprint = directAgentAction.GeneratedBlueprint,
                ProjectId = directAgentAction.ProjectId,
                ProjectName = directAgentAction.ExecutedAction?.AffectedEntityName
            };
            yield return new StreamingPlanningChunk
            {
                Type = "done",
                ProjectId = directAgentAction.ProjectId,
                ProjectName = directAgentAction.ExecutedAction?.AffectedEntityName
            };
            yield break;
        }

        // Parse user intent / answers incrementally
        string name = answers.ProjectName;
        string desc = answers.Description;
        string arch = answers.ArchitecturalStyle;
        string front = answers.FrontendStack;
        string db = answers.DatabaseType;
        string load = answers.ExpectedLoad;

        var lower = msg.ToLowerInvariant();

        if (isNewProjectIntent || answers.Description == "Initial discovery in progress" || lower.Contains("crear") || lower.Contains("proyecto") || lower.Contains("app") || lower.Contains("saas") || lower.Contains("plataforma"))
        {
            desc = msg;
            var extracted = ExtractProjectNameFromPrompt(msg);
            if (!string.IsNullOrWhiteSpace(extracted) && extracted != "NuevoProyecto")
            {
                name = extracted;
            }
            else if (msg.Length < 30 && !msg.Contains(' '))
            {
                name = SanitizeIdentifier(msg);
            }
        }

        if (lower.Contains("clean architecture") || lower.Contains("vertical slices") || lower.Contains("hexagonal"))
        {
            if (lower.Contains("vertical slices")) arch = "Vertical Slices";
            else if (lower.Contains("hexagonal") || lower.Contains("ports and adapters")) arch = "Hexagonal (Ports & Adapters)";
            else arch = "Clean Architecture";
        }

        if (lower.Contains("blazor")) front = "Blazor WebAssembly + Minimalist Slate";
        else if (lower.Contains("react")) front = "React + Minimalist Design System";
        else if (lower.Contains("next")) front = "Next.js + Tailwind / Slate UI";
        else if (lower.Contains("vue")) front = "Vue 3 + Vite Minimal";
        else if (lower.Contains("svelte")) front = "SvelteKit Minimal";

        if (lower.Contains("postgres")) db = "PostgreSQL + EF Core 9";
        else if (lower.Contains("sqlite")) db = "SQLite + EF Core 9";
        else if (lower.Contains("sql server")) db = "SQL Server + EF Core";
        else if (lower.Contains("cockroach")) db = "CockroachDB (Distributed SQL)";
        else if (lower.Contains("mongo") || lower.Contains("nosql")) db = "MongoDB / DocumentDB";

        var updatedAnswers = new ProjectInterviewAnswers
        {
            ProjectName = name,
            Description = desc,
            ArchitecturalStyle = arch,
            FrontendStack = front,
            DatabaseType = db,
            ExpectedLoad = load
        };

        if (project is null)
        {
            var rootPath = Path.Combine(Directory.GetCurrentDirectory(), name.Replace(" ", "-").ToLowerInvariant());
            project = await _projectRepo.RegisterProjectAsync(name, rootPath, cancellationToken: cancellationToken);
        }

        await _projectRepo.AddMessageAsync(project.Id, "user", effectiveMsg, cancellationToken: cancellationToken);

        var blueprint = await SynthesizePlanAsync(updatedAnswers, persistToDatabase: true, cancellationToken: cancellationToken);

        string assistantReply = string.Empty;
        List<string> questions = [];

        if (_chatClient is not null)
        {
            var historyEntities = await _projectRepo.GetMessagesAsync(project.Id, cancellationToken);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildSystemPrompt(updatedAnswers))
            };

            var recentTurns = historyEntities.TakeLast(8);
            foreach (var turn in recentTurns)
            {
                var role = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? ChatRole.User
                    : ChatRole.Assistant;
                messages.Add(new ChatMessage(role, turn.Content));
            }

            if (!messages.Any(m => m.Role == ChatRole.User && m.Text == effectiveMsg))
            {
                messages.Add(new ChatMessage(ChatRole.User, effectiveMsg));
            }

            var options = new ChatOptions
            {
                Temperature = 0.35f,
                MaxOutputTokens = 1200
            };

            var sb = new StringBuilder();
            bool streamedAny = false;

            IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
            try
            {
                enumerator = _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProjectPlanningService] Streaming initialization exception: {ex.Message}");
            }

            if (enumerator is not null)
            {
                await using (enumerator)
                {
                    while (true)
                    {
                        ChatResponseUpdate? currentUpdate = null;
                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync();
                            if (hasNext) currentUpdate = enumerator.Current;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ProjectPlanningService] Streaming exception: {ex.Message}");
                            break;
                        }

                        if (!hasNext || currentUpdate is null) break;

                        var text = currentUpdate.Text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            streamedAny = true;
                            sb.Append(text);
                            if (!sb.ToString().Contains("PREGUNTAS_SUGERIDAS", StringComparison.OrdinalIgnoreCase))
                            {
                                yield return new StreamingPlanningChunk
                                {
                                    Type = "token",
                                    Content = text
                                };
                            }
                        }
                    }
                }
            }

            var rawOutput = sb.ToString();
            if (streamedAny && !string.IsNullOrWhiteSpace(rawOutput))
            {
                questions = ExtractSuggestedQuestions(rawOutput);
                assistantReply = CleanAssistantReply(rawOutput);
            }
            else
            {
                var (heuristicReply, heuristicQuestions) = GenerateHeuristicChatTurn(updatedAnswers, effectiveMsg);
                assistantReply = heuristicReply;
                questions = heuristicQuestions;
                yield return new StreamingPlanningChunk { Type = "token", Content = assistantReply };
            }
        }
        else
        {
            var (heuristicReply, heuristicQuestions) = GenerateHeuristicChatTurn(updatedAnswers, effectiveMsg);
            assistantReply = heuristicReply;
            questions = heuristicQuestions;
            yield return new StreamingPlanningChunk { Type = "token", Content = assistantReply };
        }

        var questionsJson = JsonSerializer.Serialize(questions);
        var blueprintJson = JsonSerializer.Serialize(blueprint);

        var savedMsg = await _projectRepo.AddMessageAsync(
            project.Id,
            "assistant",
            assistantReply,
            questionsJson,
            blueprintJson,
            cancellationToken
        );

        await _projectRepo.UpdateProjectMetadataAsync(
            project.Id,
            desc,
            arch,
            front,
            db,
            blueprintJson,
            cancellationToken
        );

        yield return new StreamingPlanningChunk
        {
            Type = "done",
            Content = assistantReply,
            Suggestions = questions,
            Blueprint = blueprint,
            MessageId = savedMsg.Id.ToString(),
            ProjectId = project.Id.ToString(),
            ProjectName = project.Name
        };
    }

    public Task<bool> DeleteProjectAsync(Guid id, CancellationToken cancellationToken = default)
        => _projectRepo.DeleteProjectAsync(id, cancellationToken);

    public Task<ProjectEntity?> UpdateProjectDetailsAsync(Guid id, string name, string? rootPath, string? description, CancellationToken cancellationToken = default)
        => _projectRepo.UpdateProjectDetailsAsync(id, name, rootPath, description, cancellationToken);

    public Task<bool> ClearProjectChatAsync(Guid id, CancellationToken cancellationToken = default)
        => _projectRepo.ClearProjectMessagesAsync(id, cancellationToken);

    public Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
        => _docRepo.DeleteDocumentAsync(id, cancellationToken);

    public Task<DocumentationEntity?> UpdateDocumentAsync(Guid id, string title, string markdownContent, string version = "1.0.0", CancellationToken cancellationToken = default)
        => _docRepo.UpdateDocumentAsync(id, title, markdownContent, version, cancellationToken);

    public Task<bool> DeleteKnowledgeAsync(Guid id, CancellationToken cancellationToken = default)
        => _knowledgeRepo.DeleteKnowledgeAsync(id, cancellationToken);

    public Task<ProjectScaffoldingResult> ScaffoldProjectDirectoryAsync(string targetPath, ProjectPlanBlueprint blueprint, CancellationToken cancellationToken = default)
        => _scaffoldingService.GenerateOnDiskAsync(blueprint, targetPath, cancellationToken);

    private async Task<PlanningChatResponse?> TryExecuteAgentActionAsync(
        string sessionId,
        string msg,
        ProjectEntity? project,
        ProjectInterviewAnswers answers,
        CancellationToken cancellationToken)
    {
        var lower = msg.ToLowerInvariant().Trim();

        // 1. DELETE PROJECT
        if (project is not null && (
            lower.Contains("elimina este proyecto") ||
            lower.Contains("eliminar este proyecto") ||
            lower.Contains("borra este proyecto") ||
            lower.Contains("borrar este proyecto") ||
            lower.Contains("elimina el proyecto") ||
            lower.Contains("eliminar proyecto") ||
            lower.Contains("borra el proyecto") ||
            lower == "delete project"))
        {
            var pName = project.Name;
            var pId = project.Id;
            await _projectRepo.DeleteProjectAsync(pId, cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = string.Empty,
                AssistantReply = $"El proyecto **{pName}** y todos sus artefactos asociados (conversaciones, plano C4, decisiones ADR y matriz técnica) han sido eliminados de la base de datos local SQLite.",
                SuggestedQuestions =
                [
                    "¿Qué nuevo proyecto o arquitectura deseas inicializar?",
                    "¿Deseas probar una plantilla rápida de microservicios o SaaS?",
                    "¿Quieres consultar el catálogo de skills o prompts?"
                ],
                UpdatedAnswers = new ProjectInterviewAnswers
                {
                    ProjectName = "Nuevo Proyecto",
                    Description = "Inicia una nueva sesión para comenzar a planificar",
                    ArchitecturalStyle = "Clean Architecture",
                    FrontendStack = "React + Minimalist Design System",
                    DatabaseType = "PostgreSQL + EF Core 9"
                },
                GeneratedBlueprint = null,
                ReadyToScaffold = false,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.DeleteProject,
                    Success = true,
                    IsProjectDeleted = true,
                    AffectedEntityId = pId.ToString(),
                    AffectedEntityName = pName,
                    Message = $"El proyecto '{pName}' fue eliminado exitosamente."
                }
            };
        }

        // 2. CLEAR CHAT
        if (project is not null && (
            lower.Contains("limpia el chat") ||
            lower.Contains("limpiar chat") ||
            lower.Contains("borra el chat") ||
            lower.Contains("borra los mensajes") ||
            lower.Contains("borrar historial") ||
            lower.Contains("limpiar conversacion") ||
            lower.Contains("reinicia la conversacion") ||
            lower == "clear chat"))
        {
            await _projectRepo.ClearProjectMessagesAsync(project.Id, cancellationToken);
            var resetMsg = $"El historial de conversación del proyecto **{project.Name}** ha sido reiniciado. El plano de arquitectura y los ADRs existentes se conservan intactos.";
            await _projectRepo.AddMessageAsync(project.Id, "assistant", resetMsg, cancellationToken: cancellationToken);

            ProjectPlanBlueprint? existingBp = TryGetBlueprint(project.LatestBlueprintJson);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = resetMsg,
                SuggestedQuestions =
                [
                    "¿Deseas refinar el modelo de datos o agregar caché?",
                    "¿Quieres profundizar en las convenciones arquitectónicas?",
                    "¿Revisamos el diagrama C4 de componentes?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = existingBp,
                ReadyToScaffold = existingBp is not null,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.ClearChat,
                    Success = true,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    Message = "Historial de conversación limpiado exitosamente."
                }
            };
        }

        // 3. RENAME / UPDATE PROJECT NAME
        if (project is not null && (
            lower.Contains("cambia el nombre del proyecto a ") ||
            lower.Contains("cambia el nombre a ") ||
            lower.Contains("renombra el proyecto a ") ||
            lower.Contains("renombrar el proyecto a ") ||
            lower.Contains("modifica el nombre a ") ||
            lower.Contains("cambiar nombre a ")))
        {
            string newName = ExtractAfterPrefix(msg, [
                "cambia el nombre del proyecto a ",
                "cambia el nombre a ",
                "renombra el proyecto a ",
                "renombrar el proyecto a ",
                "modifica el nombre a ",
                "cambiar nombre a "
            ]);

            if (!string.IsNullOrWhiteSpace(newName))
            {
                var oldName = project.Name;
                await _projectRepo.UpdateProjectDetailsAsync(project.Id, newName, null, null, cancellationToken);

                var bpToUpdate = TryGetBlueprint(project.LatestBlueprintJson) 
                    ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);
                bpToUpdate = bpToUpdate with { ProjectName = newName };
                var newBpJson = JsonSerializer.Serialize(bpToUpdate);
                await _projectRepo.UpdateProjectMetadataAsync(project.Id, null, null, null, null, newBpJson, cancellationToken);

                var reply = $"El proyecto ha sido renombrado exitosamente de **{oldName}** a **{newName}**. El plano de arquitectura y el diagrama C4 se han sincronizado con la nueva identidad.";
                await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
                await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

                return new PlanningChatResponse
                {
                    SessionId = sessionId,
                    ProjectId = project.Id.ToString(),
                    AssistantReply = reply,
                    SuggestedQuestions =
                    [
                        "¿Deseas ajustar la ruta de almacenamiento en disco?",
                        "¿Verificamos el diagrama C4 actualizado?",
                        "¿Procedemos a estructurar en disco?"
                    ],
                    UpdatedAnswers = answers with { ProjectName = newName },
                    GeneratedBlueprint = bpToUpdate,
                    ReadyToScaffold = true,
                    ExecutedAction = new AgentActionResult
                    {
                        ActionType = AgentActionType.UpdateProject,
                        Success = true,
                        AffectedEntityId = project.Id.ToString(),
                        AffectedEntityName = newName,
                        UpdatedBlueprint = bpToUpdate,
                        Message = $"Proyecto renombrado a '{newName}'."
                    }
                };
            }
        }

        // 4. TECH STACK MODIFICATION (Add/Replace/Remove technologies)
        if (project is not null && (
            lower.Contains("modifica el stack") ||
            lower.Contains("actualiza el stack") ||
            lower.Contains("cambia la base de datos a ") ||
            lower.Contains("cambia postgres por ") ||
            lower.Contains("cambia postgresql por ") ||
            lower.Contains("cambia mongodb por ") ||
            lower.Contains("agrega redis") ||
            lower.Contains("añade redis") ||
            lower.Contains("agrega kafka") ||
            lower.Contains("añade kafka") ||
            lower.Contains("agrega rabbitmq") ||
            lower.Contains("agrega al stack") ||
            lower.Contains("quita del stack") ||
            lower.Contains("elimina del stack") ||
            lower.Contains("cambia el backend a ") ||
            lower.Contains("cambia el frontend a ")))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            var stack = new Dictionary<string, string>(bp.TechStack);
            string changeDetail = string.Empty;

            if (lower.Contains("redis"))
            {
                stack["Cache / Sesiones"] = "Redis 7 / Valkey distribuido";
                changeDetail = "Añadida capa de caché de alto rendimiento con Redis 7.";
            }
            if (lower.Contains("kafka"))
            {
                stack["Message Broker / Streaming"] = "Apache Kafka (Event-Driven Streaming)";
                changeDetail = (changeDetail + " Añadido bus de eventos con Apache Kafka.").Trim();
            }
            if (lower.Contains("rabbitmq"))
            {
                stack["Message Broker"] = "RabbitMQ (AMQP Message Queue)";
                changeDetail = (changeDetail + " Añadido Message Broker con RabbitMQ.").Trim();
            }
            if (lower.Contains("cockroach"))
            {
                stack["Base de Datos"] = "CockroachDB (Distributed SQL)";
                changeDetail = (changeDetail + " Base de datos cambiada a CockroachDB.").Trim();
            }
            else if (lower.Contains("mongo"))
            {
                stack["Base de Datos"] = "MongoDB 7 (Document DB)";
                changeDetail = (changeDetail + " Base de datos cambiada a MongoDB.").Trim();
            }
            else if (lower.Contains("postgres"))
            {
                stack["Base de Datos"] = "PostgreSQL 16 + EF Core 9";
                changeDetail = (changeDetail + " Base de datos cambiada a PostgreSQL.").Trim();
            }
            else if (lower.Contains("sqlite"))
            {
                stack["Base de Datos"] = "SQLite Embebido (Zero-Config)";
                changeDetail = (changeDetail + " Base de datos cambiada a SQLite.").Trim();
            }

            if (lower.Contains("blazor"))
            {
                stack["Frontend Framework"] = "Blazor WebAssembly + Classic Minimalist";
                changeDetail = (changeDetail + " Frontend cambiado a Blazor WebAssembly.").Trim();
            }
            else if (lower.Contains("next"))
            {
                stack["Frontend Framework"] = "Next.js 14 + React Server Components";
                changeDetail = (changeDetail + " Frontend cambiado a Next.js.").Trim();
            }
            else if (lower.Contains("svelte"))
            {
                stack["Frontend Framework"] = "SvelteKit + Minimalist Design System";
                changeDetail = (changeDetail + " Frontend cambiado a SvelteKit.").Trim();
            }

            if (string.IsNullOrEmpty(changeDetail))
            {
                changeDetail = "Stack tecnológico actualizado con las tecnologías solicitadas.";
            }

            bp = bp with { TechStack = stack };
            var bpJson = JsonSerializer.Serialize(bp);
            await _projectRepo.UpdateProjectMetadataAsync(project.Id, null, null, null, null, bpJson, cancellationToken);

            var reply = $"Como Agente de Arquitectura, he actualizado el **Stack Tecnológico** del proyecto:\n\n- {changeDetail}\n\nLa matriz tecnológica en el panel de artefactos ha sido actualizada en tiempo real.";
            await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
            await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = reply,
                SuggestedQuestions =
                [
                    "¿Deseas agregar convenciones arquitectónicas para esta nueva tecnología?",
                    "¿Actualizamos el diagrama C4 para reflejar estos componentes?",
                    "¿Procedemos con la generación de un ADR que justifique este cambio?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = bp,
                ReadyToScaffold = true,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.UpdateTechStack,
                    Success = true,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    UpdatedBlueprint = bp,
                    Message = changeDetail
                }
            };
        }

        // 5. CONVENTIONS ADDITION OR REMOVAL
        if (project is not null && (
            lower.Contains("agrega la convencion ") ||
            lower.Contains("añade la convencion ") ||
            lower.Contains("agrega una convencion ") ||
            lower.Contains("agrega la regla ") ||
            lower.Contains("elimina la convencion ") ||
            lower.Contains("quita la convencion ")))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            var conventions = new List<string>(bp.KeyConventions);
            string actionText;
            bool isRemove = lower.Contains("elimina") || lower.Contains("quita");

            if (isRemove)
            {
                var term = ExtractAfterPrefix(msg, ["elimina la convencion ", "quita la convencion ", "elimina la regla "]);
                conventions.RemoveAll(c => c.Contains(term, StringComparison.OrdinalIgnoreCase));
                actionText = $"Convención relacionada con '{term}' eliminada del plano.";
            }
            else
            {
                var newConv = ExtractAfterPrefix(msg, ["agrega la convencion ", "añade la convencion ", "agrega una convencion ", "agrega la regla "]);
                if (!string.IsNullOrWhiteSpace(newConv))
                {
                    conventions.Add(newConv.Trim());
                    actionText = $"Nueva convención agregada: '{newConv.Trim()}'.";
                }
                else
                {
                    actionText = "Convenciones actualizadas.";
                }
            }

            bp = bp with { KeyConventions = conventions };
            var bpJson = JsonSerializer.Serialize(bp);
            await _projectRepo.UpdateProjectMetadataAsync(project.Id, null, null, null, null, bpJson, cancellationToken);

            var reply = $"Como Agente de Arquitectura, he modificado las **Convenciones Arquitectónicas**:\n\n- {actionText}\n\nPuedes consultar la lista actualizada en la pestaña *Stack & Convenciones*.";
            await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
            await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = reply,
                SuggestedQuestions =
                [
                    "¿Deseas registrar este cambio en un Registro de Decisión Arquitectónica (ADR)?",
                    "¿Alineamos el linter o auditor de código con esta convención?",
                    "¿Verificamos los siguientes pasos de implementación?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = bp,
                ReadyToScaffold = true,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = isRemove ? AgentActionType.RemoveConvention : AgentActionType.AddConvention,
                    Success = true,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    UpdatedBlueprint = bp,
                    Message = actionText
                }
            };
        }

        // 6. ADR DELETION OR CREATION
        if (project is not null && (
            lower.Contains("elimina el adr") ||
            lower.Contains("borra el adr") ||
            lower.Contains("crea un adr para ") ||
            lower.Contains("crea el adr ")))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            if (lower.Contains("elimina") || lower.Contains("borra"))
            {
                var docs = await _docRepo.ListDocumentsAsync(project.Id, "ADR", cancellationToken);
                foreach (var d in docs)
                {
                    await _docRepo.DeleteDocumentAsync(d.Id, cancellationToken);
                }

                bp = bp with { InitialAdrTitle = "Sin ADR Activo", InitialAdrContent = "/* No hay registros de decisión arquitectónica activos */" };
                var bpJson = JsonSerializer.Serialize(bp);
                await _projectRepo.UpdateProjectMetadataAsync(project.Id, null, null, null, null, bpJson, cancellationToken);

                var reply = "Se han eliminado los Registros de Decisión Arquitectónica (ADRs) asociados a este proyecto en SQLite y en el plano técnico.";
                await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
                await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

                return new PlanningChatResponse
                {
                    SessionId = sessionId,
                    ProjectId = project.Id.ToString(),
                    AssistantReply = reply,
                    SuggestedQuestions =
                    [
                        "¿Deseas formular un nuevo ADR con una decisión específica?",
                        "¿Continuamos con la revisión de seguridad OWASP?",
                        "¿Revisamos el diagrama C4?"
                    ],
                    UpdatedAnswers = answers,
                    GeneratedBlueprint = bp,
                    ReadyToScaffold = true,
                    ExecutedAction = new AgentActionResult
                    {
                        ActionType = AgentActionType.DeleteAdr,
                        Success = true,
                        AffectedEntityId = project.Id.ToString(),
                        AffectedEntityName = project.Name,
                        UpdatedBlueprint = bp,
                        Message = "ADRs eliminados exitosamente."
                    }
                };
            }
            else
            {
                var topic = ExtractAfterPrefix(msg, ["crea un adr para ", "crea el adr para ", "crea el adr "]);
                var newTitle = $"ADR: {topic}";
                var newContent = $"""
                # {newTitle}

                - **Estado**: Aceptado
                - **Fecha**: {DateTime.UtcNow:yyyy-MM-dd}
                - **Decisor**: AI Software Architect & Tech Lead

                ## Contexto
                El sistema requiere una directiva técnica formal para {topic}.

                ## Decisión
                Se establece la adopción de las mejores prácticas y estándares de la industria para {topic} en el marco de .NET 9 y arquitectura limpia.

                ## Consecuencias
                - Positivas: Cohesión arquitectónica, mantenibilidad a largo plazo y trazabilidad.
                - Negativas / Trade-offs: Requiere rigurosidad en los pull requests y cumplimiento de estándares.
                """;

                await _docRepo.SaveDocumentAsync(newTitle, "ADR", newContent, project.Id, "1.0.0", cancellationToken);
                bp = bp with { InitialAdrTitle = newTitle, InitialAdrContent = newContent };
                var bpJson = JsonSerializer.Serialize(bp);
                await _projectRepo.UpdateProjectMetadataAsync(project.Id, null, null, null, null, bpJson, cancellationToken);

                var reply = $"Como Agente de Arquitectura, he generado y guardado en SQLite el **{newTitle}**.\n\nPuedes consultar el contenido completo en la pestaña *ADR (Decisión)*.";
                await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
                await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

                return new PlanningChatResponse
                {
                    SessionId = sessionId,
                    ProjectId = project.Id.ToString(),
                    AssistantReply = reply,
                    SuggestedQuestions =
                    [
                        "¿Deseas ajustar las consecuencias o alternativas consideradas en este ADR?",
                        "¿Añadimos pruebas de arquitectura para validar este ADR?",
                        "¿Revisamos la estructura en disco para este componente?"
                    ],
                    UpdatedAnswers = answers,
                    GeneratedBlueprint = bp,
                    ReadyToScaffold = true,
                    ExecutedAction = new AgentActionResult
                    {
                        ActionType = AgentActionType.CreateAdr,
                        Success = true,
                        AffectedEntityId = project.Id.ToString(),
                        AffectedEntityName = newTitle,
                        UpdatedBlueprint = bp,
                        Message = $"ADR '{newTitle}' creado exitosamente."
                    }
                };
            }
        }

        // 7. SCAFFOLD PROJECT ON DISK
        if (project is not null && (
            (lower.Contains("genera") || lower.Contains("crea") || lower.Contains("inicializa") || lower.Contains("scaffold")) &&
            (lower.Contains("solucion") || lower.Contains("solución") || lower.Contains("disco") || lower.Contains("archivos") || lower.Contains("scaffold") || lower.Contains("proyecto en disco"))))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            string targetPath;
            var pathMatch = System.Text.RegularExpressions.Regex.Match(msg, @"(?i)(?:en\s+)?([a-zA-Z]:\\[^\r\n""'<>|?*]+)");
            if (pathMatch.Success)
            {
                targetPath = pathMatch.Groups[1].Value.Trim();
            }
            else
            {
                var safeName = SanitizeIdentifier(project.Name);
                targetPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "scaffolding", safeName);
            }

            var scaffoldResult = await _scaffoldingService.GenerateOnDiskAsync(bp, targetPath, cancellationToken);

            var replySb = new StringBuilder();
            if (scaffoldResult.Success)
            {
                replySb.AppendLine($"Como Agente de Arquitectura, he generado exitosamente la solución y archivos en disco para **{project.Name}**.");
                replySb.AppendLine();
                replySb.AppendLine($"- **Ruta física**: `{scaffoldResult.OutputPath}`");
                replySb.AppendLine($"- **Archivos creados**: {scaffoldResult.TotalFilesCreated} archivos (.sln, .csproj por capas, docker-compose.yml, tests, README, ADRs).");
                replySb.AppendLine();
                replySb.AppendLine("### Próximos pasos en tu terminal:");
                replySb.AppendLine("```bash");
                replySb.AppendLine($"cd \"{scaffoldResult.OutputPath}\"");
                replySb.AppendLine("docker compose up -d");
                replySb.AppendLine("dotnet build");
                replySb.AppendLine("dotnet test --nologo");
                replySb.AppendLine("```");
                if (scaffoldResult.Warnings.Count > 0)
                {
                    replySb.AppendLine();
                    replySb.AppendLine($"*Aviso: {string.Join(" ", scaffoldResult.Warnings)}*");
                }
            }
            else
            {
                replySb.AppendLine($"Hubo un error al generar la solución en disco: {scaffoldResult.Message}");
            }

            var reply = replySb.ToString();
            await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
            await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = reply,
                SuggestedQuestions =
                [
                    "¿Deseas descargar también el archivo comprimido .ZIP de la solución?",
                    "¿Añadimos más entidades de dominio o casos de uso al proyecto?",
                    "¿Revisamos la configuración de seguridad en el Program.cs?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = bp,
                ReadyToScaffold = true,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.ScaffoldProject,
                    Success = scaffoldResult.Success,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    TargetDirectory = scaffoldResult.OutputPath,
                    FilesCreatedCount = scaffoldResult.TotalFilesCreated,
                    UpdatedBlueprint = bp,
                    Message = scaffoldResult.Message
                }
            };
        }

        // 8. EXPORT PROJECT ZIP
        if (project is not null && (
            (lower.Contains("exporta") || lower.Contains("descarga") || lower.Contains("baja")) &&
            (lower.Contains("zip") || lower.Contains("comprimido"))))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            var downloadUrl = $"/api/planning/projects/{project.Id}/export-zip";
            var reply = $"Como Agente de Arquitectura, he empaquetado la solución completa de **{project.Name}** (.sln, proyectos por capas, docker-compose, ADRs y pruebas unitarias) en un archivo `.zip` listo para descargar.\n\nPuedes descargarlo inmediatamente desde este enlace: [Descargar paquete ZIP]({downloadUrl}) o mediante el botón *Descargar ZIP* en la pestaña *Estructura en Disco*.";

            await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
            await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = reply,
                SuggestedQuestions =
                [
                    "¿Deseas generar los archivos directamente en una carpeta local de tu disco duro?",
                    "¿Ajustamos alguna dependencia antes de que comiences a programar?",
                    "¿Exportamos la documentación de arquitectura en Markdown?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = bp,
                ReadyToScaffold = true,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.ExportProjectZip,
                    Success = true,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    DownloadUrl = downloadUrl,
                    UpdatedBlueprint = bp,
                    Message = "Descarga de paquete ZIP preparada exitosamente."
                }
            };
        }

        // 9. EXPORT DOCUMENTATION (MARKDOWN)
        if (project is not null && (
            (lower.Contains("exporta") || lower.Contains("descarga") || lower.Contains("genera")) &&
            (lower.Contains("documentacion") || lower.Contains("documentación") || lower.Contains("docs") || lower.Contains("markdown") || lower.Contains("architecture.md"))))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            var downloadUrl = $"/api/planning/projects/{project.Id}/export-docs";
            var reply = $"Como Agente de Arquitectura, he generado el paquete formal de documentación técnica para **{project.Name}** (incluyendo `ARCHITECTURE.md` con el diagrama C4 Mermaid y registros de decisión `ADRs`).\n\nPuedes descargarlo directamente en: [Descargar Documentación Markdown]({downloadUrl}).";

            await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
            await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = reply,
                SuggestedQuestions =
                [
                    "¿Deseas generar el scaffolding de código en disco?",
                    "¿Añadimos un nuevo ADR antes de exportar?",
                    "¿Revisamos el diagrama C4?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = bp,
                ReadyToScaffold = true,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.ExportDocumentation,
                    Success = true,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    DownloadUrl = downloadUrl,
                    UpdatedBlueprint = bp,
                    Message = "Documentación formal generada exitosamente."
                }
            };
        }

        // 10. EXPORT AGENTS.MD / ANTIGRAVITY RULES
        if (project is not null && (
            (lower.Contains("exporta") || lower.Contains("descarga") || lower.Contains("genera") || lower.Contains("crea")) &&
            (lower.Contains("agents.md") || lower.Contains("agents") || lower.Contains("antigravity") || lower.Contains("reglas de agente"))))
        {
            var bp = TryGetBlueprint(project.LatestBlueprintJson)
                ?? await SynthesizePlanAsync(answers, persistToDatabase: false, cancellationToken: cancellationToken);

            var downloadUrl = $"/api/planning/projects/{project.Id}/export-agents";
            var reply = $"Como Agente de Arquitectura, he generado el archivo formal **`AGENTS.md`** para **{project.Name}** adaptado a Google Antigravity y agentes de IA.\n\nContiene directivas estrictas de aislamiento de capas (Clean Architecture en .NET 9), pureza del Dominio, estándares C# 13, comandos CLI de verificación (`dotnet build`, `dotnet test --nologo`), directiva de cero emojis y criterios de calidad ISO/IEC 25010.\n\nPuedes descargarlo directamente en: [Descargar AGENTS.md]({downloadUrl}) o mediante el botón *Exportar AGENTS.md* en la pestaña *Estructura en Disco*.";

            await _projectRepo.AddMessageAsync(project.Id, "user", msg, cancellationToken: cancellationToken);
            await _projectRepo.AddMessageAsync(project.Id, "assistant", reply, cancellationToken: cancellationToken);

            return new PlanningChatResponse
            {
                SessionId = sessionId,
                ProjectId = project.Id.ToString(),
                AssistantReply = reply,
                SuggestedQuestions =
                [
                    "¿Deseas descargar también el archivo README.md o ARCHITECTURE.md?",
                    "¿Generamos la solución completa con sus archivos en disco?",
                    "¿Revisamos el cumplimiento de requisitos de calidad ISO/IEC 25010?"
                ],
                UpdatedAnswers = answers,
                GeneratedBlueprint = bp,
                ReadyToScaffold = true,
                ExecutedAction = new AgentActionResult
                {
                    ActionType = AgentActionType.ExportAgentsMarkdown,
                    Success = true,
                    AffectedEntityId = project.Id.ToString(),
                    AffectedEntityName = project.Name,
                    DownloadUrl = downloadUrl,
                    UpdatedBlueprint = bp,
                    Message = "Archivo AGENTS.md para Antigravity generado exitosamente."
                }
            };
        }

        return null;
    }

    private static string ExtractAfterPrefix(string text, string[] prefixes)
    {
        var lower = text.ToLowerInvariant();
        foreach (var prefix in prefixes)
        {
            var idx = lower.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var candidate = text.Substring(idx + prefix.Length).Trim();
                var clean = candidate.Trim('"', '\'', '.', ',', ';', '\r', '\n');
                return clean;
            }
        }
        return string.Empty;
    }

    private static bool IsNewProjectIntent(string msg)
    {
        var lower = msg.ToLowerInvariant().Trim();
        return lower.StartsWith("quiero un proyecto") ||
               lower.StartsWith("quiero crear un proyecto") ||
               lower.StartsWith("crea un proyecto") ||
               lower.StartsWith("crear un proyecto") ||
               lower.StartsWith("nuevo proyecto") ||
               lower.StartsWith("inicia un proyecto") ||
               lower.StartsWith("iniciar un proyecto") ||
               lower.StartsWith("diseña un proyecto") ||
               lower.StartsWith("diseñar un proyecto") ||
               lower.StartsWith("comienza un proyecto") ||
               lower.StartsWith("empezar un proyecto") ||
               lower.StartsWith("quiero un sistema") ||
               lower.StartsWith("quiero crear un sistema") ||
               lower.StartsWith("crea un sistema") ||
               lower.StartsWith("crear un sistema") ||
               lower.StartsWith("nuevo sistema") ||
               lower.StartsWith("quiero una plataforma") ||
               lower.StartsWith("quiero crear una plataforma") ||
               lower.StartsWith("crea una plataforma") ||
               lower.StartsWith("crear una plataforma") ||
               lower.StartsWith("nueva plataforma") ||
               lower.StartsWith("quiero una app") ||
               lower.StartsWith("quiero crear una app") ||
               lower.StartsWith("crea una app") ||
               lower.StartsWith("crear una app") ||
               lower.StartsWith("nueva app") ||
               lower.StartsWith("quiero un saas") ||
               lower.StartsWith("crea un saas") ||
               lower.StartsWith("nuevo saas");
    }

    private static string ExtractProjectNameFromPrompt(string msg)
    {
        var lower = msg.ToLowerInvariant();
        string[] indicators = [
            "un proyecto para ", "un proyecto de ", "el proyecto de ", "el proyecto para ",
            "proyecto para ", "proyecto de ",
            "un sistema para ", "un sistema de ", "el sistema de ", "el sistema para ",
            "sistema para ", "sistema de ",
            "una plataforma para ", "una plataforma de ", "la plataforma de ", "la plataforma para ",
            "plataforma para ", "plataforma de ",
            "una app para ", "una app de ", "la app de ", "la app para ",
            "app para ", "app de ",
            "un saas para ", "un saas de ", "el saas de ", "el saas para ",
            "saas para ", "saas de ",
            "un software para ", "un software de ", "software para ", "software de ",
            "control de ", "registro de ", "gestion de ", "gestión de "
        ];

        foreach (var ind in indicators)
        {
            var idx = lower.IndexOf(ind, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var raw = msg.Substring(idx + ind.Length).Trim();
                var stopWords = new[] { ",", ".", ";", "\n", " que ", " donde ", " con " };
                var minStopIdx = raw.Length;
                foreach (var sw in stopWords)
                {
                    var sIdx = raw.IndexOf(sw, StringComparison.OrdinalIgnoreCase);
                    if (sIdx > 0 && sIdx < minStopIdx) minStopIdx = sIdx;
                }
                var candidate = raw[..minStopIdx].Trim();
                if (candidate.Length >= 3)
                {
                    var words = candidate.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                    var sb = new StringBuilder();
                    foreach (var w in words.Take(5))
                    {
                        if (w.Length > 0)
                        {
                            sb.Append(char.ToUpperInvariant(w[0]));
                            if (w.Length > 1) sb.Append(w[1..].ToLowerInvariant());
                        }
                    }
                    var clean = SanitizeIdentifier(sb.ToString());
                    if (clean.Length >= 3)
                    {
                        return clean.EndsWith("Platform") || clean.EndsWith("System") || clean.EndsWith("App") 
                            ? clean 
                            : clean + "Platform";
                    }
                }
            }
        }
        return "NuevoProyecto";
    }

    public static ProjectPlanBlueprint? TryGetBlueprint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ProjectPlanBlueprint>(json); }
        catch { return null; }
    }

    public async Task<IReadOnlyList<PlanningChatMessage>> GetProjectMessagesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var entities = await _projectRepo.GetMessagesAsync(projectId, cancellationToken);
        return entities.Select(e => new PlanningChatMessage
        {
            Id = e.Id.ToString(),
            Role = e.Role,
            Content = e.Content,
            Timestamp = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
            SuggestedActions = !string.IsNullOrEmpty(e.SuggestedQuestionsJson)
                ? JsonSerializer.Deserialize<List<string>>(e.SuggestedQuestionsJson) ?? []
                : [],
            BlueprintJson = e.BlueprintJson
        }).ToList();
    }

    public async Task<IReadOnlyList<ProjectSummaryDto>> GetProjectsSummaryAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _projectRepo.ListProjectsAsync(cancellationToken);
        var result = new List<ProjectSummaryDto>();

        foreach (var p in projects)
        {
            var msgs = await _projectRepo.GetMessagesAsync(p.Id, cancellationToken);
            ProjectPlanBlueprint? bp = null;
            if (!string.IsNullOrEmpty(p.LatestBlueprintJson))
            {
                try { bp = JsonSerializer.Deserialize<ProjectPlanBlueprint>(p.LatestBlueprintJson); } catch { }
            }

            result.Add(new ProjectSummaryDto
            {
                Id = p.Id.ToString(),
                Name = p.Name,
                RootPath = p.RootPath,
                Description = p.Description,
                ArchitecturalStyle = p.ArchitecturalStyle,
                FrontendStack = p.FrontendStack,
                DatabaseType = p.DatabaseType,
                MessageCount = msgs.Count,
                CreatedAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = DateTime.SpecifyKind(p.LastAuditedAt ?? p.CreatedAt, DateTimeKind.Utc),
                LatestBlueprint = bp
            });
        }

        return result;
    }

    private static ProjectPlanBlueprint GenerateWithArchitecturalHeuristics(ProjectInterviewAnswers answers)
    {
        var style = answers.ArchitecturalStyle;
        var frontend = answers.FrontendStack;
        var db = answers.DatabaseType;

        var stack = new Dictionary<string, string>
        {
            ["Backend Runtime"] = ".NET 9 (C# 13)",
            ["Architecture Pattern"] = style,
            ["Frontend Layer"] = frontend,
            ["Database & Persistence"] = db,
            ["API & Contracts"] = "REST (OpenAPI 3.1) / Minimal APIs",
            ["Testing Suite"] = "xUnit, FluentAssertions, Testcontainers, Playwright",
            ["UI Styling"] = "Classic Minimalist Design System with Fluid Typography"
        };

        var dirTree = $"""
        {answers.ProjectName}/
        ├── src/
        │   ├── {answers.ProjectName}.Domain/          # Core entities, value objects, domain invariants
        │   ├── {answers.ProjectName}.Application/     # Use cases, commands, queries, mediator/handlers
        │   ├── {answers.ProjectName}.Infrastructure/  # EF Core DbContext, Repositories, External APIs
        │   └── {answers.ProjectName}.Web/             # Minimal APIs, Authentication, OpenAPI
        ├── frontend/                                  # {frontend}
        │   ├── src/components/                        # Atomic components (atoms, molecules)
        │   ├── src/tokens/                            # Design tokens (colors, typography, spacing)
        │   └── src/views/
        ├── tests/
        │   ├── {answers.ProjectName}.UnitTests/
        │   └── {answers.ProjectName}.IntegrationTests/
        ├── docs/
        │   └── adr/                                   # Architecture Decision Records
        ├── docker-compose.yml
        └── README.md
        """;

        var mermaid = $"""
        graph TD
            User([End User]) --> Frontend["Frontend ({frontend})"]
            Frontend --> API["{answers.ProjectName} API (.NET 9)"]
            API --> AppLayer["Application Layer (Use Cases)"]
            AppLayer --> Domain["Domain Layer (Core Invariants)"]
            AppLayer --> Infra["Infrastructure Layer"]
            Infra --> DB[("Database ({db})")]
        """;

        var adrTitle = $"ADR 001: Selection of {style} and {frontend}";
        var adrContent = $"""
        # {adrTitle}

        * Status: Accepted
        * Deciders: Lead Architect & Project Discovery Team
        * Date: {DateTime.UtcNow:yyyy-MM-dd}

        ## Context and Problem Statement
        We are launching `{answers.ProjectName}` with the goal of: "{answers.Description}".
        Expected load: {answers.ExpectedLoad}. Target users: {answers.TargetUsers}.

        ## Decision Drivers
        * High maintainability, fast test execution, and strict domain invariants.
        * Consistent classic minimalist visual presentation.
        * Clear team boundaries and decoupling between frontend and domain logic.

        ## Decision Outcome
        Chosen Architecture: **{style}** with **{frontend}** and **{db}**.

        ### Positive Consequences
        * Clean separation of concerns allows independent testing of core business logic.
        * Frontend design system isolates tokens from components.
        * Data schema migrations remain controlled and testable.

        ### Negative Consequences
        * Initial boilerplate required for mapping between domain and DTO models.
        """;

        var keyConventions = new List<string>
        {
            "Domain Purity: The Domain layer must have zero dependencies on external frameworks or databases.",
            $"Design System: Frontend adheres to classic minimalist aesthetic using design tokens for slate colors and spacing.",
            "Testing Standard: Every public command and query handler must be backed by an xUnit unit test following the AAA pattern.",
            $"Database Migrations: All schema modifications must be additive without breaking zero-downtime deployments."
        };

        var tokensCss = """
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

        var frontendSpec = new FrontendDesignSpec
        {
            ThemeName = "Classic Minimalist Slate",
            PrimarySansFont = "'Inter', -apple-system, BlinkMacSystemFont, sans-serif",
            PrimaryMonoFont = "'JetBrains Mono', monospace",
            ColorPalette = new Dictionary<string, string>
            {
                ["Background"] = "#0d0f12",
                ["Surface"] = "#14171d",
                ["Border"] = "#242b35",
                ["TextPrimary"] = "#f0f3f6",
                ["TextSecondary"] = "#8c97a8",
                ["Accent"] = "#3b82f6"
            },
            ComponentRules = new List<string>
            {
                "Buttons: Subtle borders, high contrast labels, zero heavy drop shadows.",
                "Inputs: 1px border with #242b35, focus ring 2px slate-blue.",
                "Cards: Clean #14171d background, 6px border-radius.",
                "Accessibility: WCAG 2.2 AA compliant contrast minimum 4.5:1."
            },
            TokensCss = tokensCss
        };

        return new ProjectPlanBlueprint
        {
            ProjectName = answers.ProjectName,
            ExecutiveSummary = $"Architectural blueprint for '{answers.ProjectName}'. Designed for {answers.TargetUsers} to solve: {answers.Description}.",
            ArchitecturalRationale = $"Adopted {style} to balance operational simplicity with robust domain separation, backed by {db} and a {frontend} frontend.",
            TechStack = stack,
            DirectoryStructure = dirTree,
            C4DiagramMermaid = mermaid,
            InitialAdrTitle = adrTitle,
            InitialAdrContent = adrContent,
            KeyConventions = keyConventions,
            FrontendDesignSpec = frontendSpec
        };
    }

    private Task<ProjectPlanBlueprint> GenerateWithLlmAsync(ProjectInterviewAnswers answers, CancellationToken ct)
    {
        return Task.FromResult(GenerateWithArchitecturalHeuristics(answers));
    }

    private async Task<(string Reply, List<string> Questions)> GenerateLlmChatTurnAsync(
        ProjectEntity project,
        ProjectInterviewAnswers answers,
        string latestUserMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var historyEntities = await _projectRepo.GetMessagesAsync(project.Id, cancellationToken);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildSystemPrompt(answers))
            };

            // Add previous history turns (limit last 8 messages for latency & context efficiency)
            var recentTurns = historyEntities.TakeLast(8);
            foreach (var turn in recentTurns)
            {
                var role = string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase) 
                    ? ChatRole.User 
                    : ChatRole.Assistant;
                messages.Add(new ChatMessage(role, turn.Content));
            }

            // Ensure the latest user message is present
            if (!messages.Any(m => m.Role == ChatRole.User && m.Text == latestUserMessage))
            {
                messages.Add(new ChatMessage(ChatRole.User, latestUserMessage));
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(60)); // 60s timeout for local Ollama

            var options = new ChatOptions
            {
                Temperature = 0.35f,
                MaxOutputTokens = 1200
            };

            var response = await _chatClient!.GetResponseAsync(messages, options, cancellationToken: cts.Token);
            var rawText = response.Text ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(rawText))
            {
                var parsedQuestions = ExtractSuggestedQuestions(rawText);
                var cleanedReply = CleanAssistantReply(rawText);

                return (cleanedReply, parsedQuestions);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectPlanningService] LLM Chat Turn Exception: {ex.Message}");
            // Fallback seamlessly to dynamic heuristics if local model times out or is busy
        }

        return GenerateHeuristicChatTurn(answers, latestUserMessage);
    }

    private static (string Reply, List<string> Questions) GenerateHeuristicChatTurn(ProjectInterviewAnswers answers, string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var sb = new StringBuilder();
        var questions = new List<string>();

        if (lower.Contains("crear") || lower.Contains("saas") || lower.Contains("proyecto") || lower.Contains("sistema") || lower.Contains("plataforma") || lower.Contains("app"))
        {
            sb.AppendLine($"Diseñando la arquitectura técnica para **{answers.ProjectName}**.");
            sb.AppendLine();
            sb.AppendLine($"He consolidado los cimientos técnicos a partir de tu requerimiento: *\"{answers.Description}\"*.");
            sb.AppendLine();
            sb.AppendLine("### Recomendaciones Arquitectonicas:");
            sb.AppendLine($"- **Arquitectura**: `{answers.ArchitecturalStyle}` con desacoplamiento de capas, inversión de dependencias e invariantes de dominio.");
            sb.AppendLine($"- **Frontend**: `{answers.FrontendStack}` con Design Tokens y micro-interacciones sobrias.");
            sb.AppendLine($"- **Persistencia**: `{answers.DatabaseType}` configurado para integridad referencial y alto rendimiento.");
            sb.AppendLine();
            sb.AppendLine("El plano técnico, el diagrama C4, el ADR 001 y las especificaciones se encuentran listos en el panel lateral.");

            questions.Add("¿Deseas modelar el esquema inicial de entidades con EF Core?");
            questions.Add("¿Afinamos las directivas de seguridad o autenticación?");
            questions.Add("¿Procedemos a estructurar la solución en disco (scaffold)?");
        }
        else if (lower.Contains("base de datos") || lower.Contains("database") || lower.Contains("postgres") || lower.Contains("sql") || lower.Contains("nosql") || lower.Contains("mongo"))
        {
            sb.AppendLine($"Respecto a la capa de persistencia para **{answers.ProjectName}**:");
            sb.AppendLine();
            sb.AppendLine("### Recomendaciones de Persistencia:");
            sb.AppendLine($"1. **Motor recomendado**: `{answers.DatabaseType}`.");
            sb.AppendLine("   - Consistencia ACID en transacciones financieras y de dominio crítico.");
            sb.AppendLine("   - Excelente soporte relacional combinado con columnas JSONB para esquemas dinámicos.");
            sb.AppendLine("   - Migraciones predecibles y versionadas en código con EF Core 9.");
            sb.AppendLine();
            sb.AppendLine("2. **Estrategia de rendimiento & aislamiento**:");
            sb.AppendLine("   - Índices compuestos en claves foráneas y campos de filtrado concurrente.");
            sb.AppendLine("   - Separación estricta de modelos de lectura y escritura (patrón CQRS / AsNoTracking en queries).");
            sb.AppendLine("   - Estrategia de caching L2 con Redis para catálogos y tokens de sesión.");

            questions.Add("¿Deseas modelar el esquema inicial de entidades con EF Core?");
            questions.Add("¿Requieres soporte para multi-tenancy o particionado por cliente?");
            questions.Add("¿Procedemos a estructurar la solución en disco (scaffold)?");
        }
        else if (lower.Contains("seguridad") || lower.Contains("auth") || lower.Contains("token") || lower.Contains("jwt") || lower.Contains("roles"))
        {
            sb.AppendLine($"Para la estrategia de seguridad e identidad en **{answers.ProjectName}**:");
            sb.AppendLine();
            sb.AppendLine("### Recomendaciones de Seguridad:");
            sb.AppendLine("1. **Autenticación & Autorización**:");
            sb.AppendLine("   - Tokens JWT firmados con algoritmo asimétrico (RS256) con ciclo de vida corto y rotación de Refresh Tokens.");
            sb.AppendLine("   - Autorización basada en Claims y Roles granulares (RBAC).");
            sb.AppendLine("   - Prevención de ataques OWASP: Rate limiting, cabeceras CSP estrictas y mitigación de CSRF.");
            sb.AppendLine();
            sb.AppendLine("2. **Auditoría & Trazabilidad**:");
            sb.AppendLine("   - Interceptores en DbContext para registrar de forma automática `CreatedAt`, `CreatedBy`, `ModifiedAt`.");
            sb.AppendLine("   - Hashing criptográfico de credenciales mediante Argon2id.");

            questions.Add("¿Integramos un proveedor externo de OAuth2 (Google, GitHub, Azure AD)?");
            questions.Add("¿Definimos los roles de usuario principales del sistema?");
            questions.Add("¿Revisamos las directivas de seguridad en el ADR 001?");
        }
        else if (lower.Contains("frontend") || lower.Contains("ui") || lower.Contains("ux") || lower.Contains("diseño") || lower.Contains("componente"))
        {
            sb.AppendLine($"Para la arquitectura frontend y experiencia de usuario en **{answers.ProjectName}**:");
            sb.AppendLine();
            sb.AppendLine("### Recomendaciones de Frontend:");
            sb.AppendLine($"1. **Capa Visual & Estilos**: `{answers.FrontendStack}`.");
            sb.AppendLine("   - Sistema de diseño basado en Design Tokens (paleta Classic Slate, escala tipográfica Inter/JetBrains Mono).");
            sb.AppendLine("   - Componentes atómicos independientes (botones, modales, tablas de datos, tarjetas con contraste AA).");
            sb.AppendLine("   - Cero dependencias pesadas innecesarias; CSS nativo rápido y predecible.");
            sb.AppendLine();
            sb.AppendLine("2. **Gestión de Estado & Red**:");
            sb.AppendLine("   - Cliente HTTP fuertemente tipado con interceptores de autenticación y reintentos.");
            sb.AppendLine("   - Actualizaciones optimistas en acciones críticas del usuario.");

            questions.Add("¿Deseas exportar la hoja de estilos de tokens CSS a disco?");
            questions.Add("¿Requieres soporte para temas oscuros y claros alternables?");
            questions.Add("¿Generamos la estructura de carpetas frontend en el scaffolding?");
        }
        else if (lower.Contains("arquitectura") || lower.Contains("patron") || lower.Contains("clean") || lower.Contains("vertical") || lower.Contains("microservicio") || lower.Contains("monolito"))
        {
            sb.AppendLine($"Analizando las directrices de arquitectura para **{answers.ProjectName}**:");
            sb.AppendLine();
            sb.AppendLine("### Recomendaciones de Arquitectura:");
            sb.AppendLine($"1. **Patrón Seleccionado**: `{answers.ArchitecturalStyle}`.");
            sb.AppendLine("   - **Aislamiento del Dominio**: El núcleo de negocio permanece libre de dependencias de infraestructura o librerías de terceros.");
            sb.AppendLine("   - **Casos de Uso**: Cada flujo se encapsula como un comando o consulta independiente, facilitando pruebas unitarias rápidas.");
            sb.AppendLine("   - **Inversión de Dependencias**: Los adaptadores de infraestructura implementan contratos definidos en el núcleo.");
            sb.AppendLine();
            sb.AppendLine("2. **Evolución y Despliegue**:");
            sb.AppendLine("   - Monolito modular o cortes verticales que permiten una futura migración a microservicios sin reescrituras completas.");
            sb.AppendLine("   - CI/CD preconfigurado con validación estricta de compilación y cobertura de pruebas.");

            questions.Add("¿Configuramos la solución con proyectos separados en .NET 9?");
            questions.Add("¿Deseas añadir Mediator / Wolverine para desacoplar comandos y queries?");
            questions.Add("¿Estructuramos ahora la solución en disco (scaffold)?");
        }
        else if (lower.Contains("antigravity") || lower.Contains("agents.md") || lower.Contains("documentacion") || lower.Contains("documentación") || lower.Contains("readme"))
        {
            sb.AppendLine($"AI DevTools Studio cuenta con soporte nativo de primera clase para **Google Antigravity** y agentes de codificación autónomos.");
            sb.AppendLine();
            sb.AppendLine("### Capacidades de Generación de Documentación:");
            sb.AppendLine("1. **`AGENTS.md` y `.agents/AGENTS.md`**:");
            sb.AppendLine("   - Reglas de aislamiento estricto de capas (Clean Architecture en .NET 9: Dominio puro sin dependencias externas).");
            sb.AppendLine("   - Estándares de codificación (C# 13, null safety, Result pattern para manejo de errores sin excepciones costosas).");
            sb.AppendLine("   - Comandos CLI preconfigurados para agentes (`dotnet build`, `dotnet test --nologo`, `docker compose up -d`).");
            sb.AppendLine("   - Directiva de cero emojis y trazabilidad técnica.");
            sb.AppendLine();
            sb.AppendLine("2. **`README.md` & `ARCHITECTURE.md`**:");
            sb.AppendLine("   - Guía completa de inicio rápido y prerrequisitos.");
            sb.AppendLine("   - Diagrama C4 a nivel de contenedores y componentes en sintaxis nativa Mermaid.");
            sb.AppendLine("   - Registros de decisión arquitectónica formales (`ADR-001`).");
            sb.AppendLine();
            sb.AppendLine("3. **Bucle de Feedback de Calidad (ISO/IEC 25010)**:");
            sb.AppendLine("   - Evaluación continua de completitud de requisitos (adecuación funcional, confiabilidad, seguridad, eficiencia, mantenibilidad).");
            sb.AppendLine("   - Diálogo activo para resolver ambigüedades antes de iniciar la escritura de código en disco.");

            questions.Add("¿Deseas exportar el archivo AGENTS.md para Google Antigravity?");
            questions.Add("¿Evaluamos la completitud de requisitos según estándares ISO/IEC 25010?");
            questions.Add("¿Procedemos a estructurar la solución completa en disco?");
        }
        else if (lower.Contains("calidad") || lower.Contains("requisito") || lower.Contains("feedback") || lower.Contains("estandar") || lower.Contains("iso"))
        {
            sb.AppendLine($"El motor de planificación incorpora un bucle de **Feedback de Calidad** alineado con el estándar **ISO/IEC 25010**:");
            sb.AppendLine();
            sb.AppendLine("### Evaluación de Calidad de Requisitos para **" + answers.ProjectName + "**:");
            sb.AppendLine("1. **Adecuación Funcional**: Cobertura de reglas de negocio, flujos principales y casos límite.");
            sb.AppendLine("2. **Confiabilidad y Tolerancia a Fallos**: Manejo de reintentos, idempotencia en endpoints y consistencia transaccional.");
            sb.AppendLine("3. **Seguridad**: Autenticación RBAC, cifrado en reposo y tránsito, validación en capas.");
            sb.AppendLine("4. **Eficiencia de Desempeño**: Estrategia de caché Redis, índices en base de datos y paginación.");
            sb.AppendLine("5. **Mantenibilidad**: Clean Architecture, separación de interfaces, tests unitarios automatizados.");
            sb.AppendLine();
            sb.AppendLine("Para garantizar que la especificación esté 100% lista para codificación, es fundamental afinar los siguientes puntos clave.");

            questions.Add("¿Cuáles son los roles y permisos exactos (RBAC) que interactuarán con el sistema?");
            questions.Add("¿Cuáles son las tolerancias horarias y reglas de justificación requeridas?");
            questions.Add("¿Qué volumen de concurrencia y retención histórica de datos se proyecta?");
        }
        else
        {
            sb.AppendLine($"Diseñando la arquitectura técnica para **{answers.ProjectName}**.");
            sb.AppendLine();
            sb.AppendLine($"He consolidado los cimientos técnicos a partir de tu requerimiento: *\"{answers.Description}\"*.");
            sb.AppendLine();
            sb.AppendLine("### Recomendaciones Arquitectonicas:");
            sb.AppendLine($"- **Arquitectura**: `{answers.ArchitecturalStyle}` con desacoplamiento de capas e invariantes de dominio.");
            sb.AppendLine($"- **Frontend**: `{answers.FrontendStack}` con Design Tokens y micro-interacciones sobrias.");
            sb.AppendLine($"- **Persistencia**: `{answers.DatabaseType}` configurado para integridad referencial y alto rendimiento.");
            sb.AppendLine();
            sb.AppendLine("El plano técnico, el diagrama C4, el ADR 001 y las especificaciones se encuentran actualizados en el panel lateral.");

            questions.Add("¿Deseas profundizar en las entidades del modelo de dominio?");
            questions.Add("¿Afinamos las directivas de seguridad o autenticación?");
            questions.Add("¿Procedemos a estructurar la solución en disco (scaffold)?");
        }

        return (sb.ToString(), questions);
    }

    private static List<string> ExtractSuggestedQuestions(string rawText)
    {
        var questions = new List<string>();
        var marker = "PREGUNTAS_SUGERIDAS";
        var startIndex = rawText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (startIndex >= 0)
        {
            var content = rawText[(startIndex + marker.Length)..];
            var endMarker = "/PREGUNTAS_SUGERIDAS";
            var endIndex = content.IndexOf(endMarker, StringComparison.OrdinalIgnoreCase);
            if (endIndex >= 0)
            {
                content = content[..endIndex];
            }

            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim().TrimStart('[', ']', '#', ' ', '-', '*', '1', '2', '3', '.', ':');
                if (!string.IsNullOrWhiteSpace(trimmed) && (trimmed.EndsWith('?') || trimmed.Length > 12) && !trimmed.Contains("PREGUNTAS", StringComparison.OrdinalIgnoreCase))
                {
                    questions.Add(StripEmojis(trimmed));
                }
            }
        }

        if (questions.Count == 0)
        {
            questions.Add("¿Deseas agregar autenticación OAuth2 / JWT y control de roles?");
            questions.Add("¿Prefieres comunicación síncrona REST OpenAPI o gRPC para alto rendimiento?");
            questions.Add("¿Estructuramos ahora la solución en disco con sus carpetas y tokens iniciales?");
        }

        return questions.Take(3).ToList();
    }

    public static string BuildEffectiveUserPrompt(string userMessage, IReadOnlyList<AttachedDocumentModel>? attachedDocs)
    {
        if (attachedDocs is null || attachedDocs.Count == 0)
        {
            return userMessage;
        }

        var sb = new StringBuilder();
        sb.AppendLine("### DOCUMENTOS ADJUNTOS / CONTEXTO TÉCNICO PROPORCIONADO POR EL USUARIO:");
        sb.AppendLine("El usuario ha adjuntado los siguientes documentos o fragmentos de código para que los analices, proceses y utilices como referencia técnica estricta:");
        sb.AppendLine();

        foreach (var doc in attachedDocs)
        {
            var typeInfo = string.IsNullOrWhiteSpace(doc.FileType) ? "texto plano" : doc.FileType;
            sb.AppendLine($"--- INICIO DOCUMENTO: {doc.FileName} ({doc.SizeBytes} bytes, tipo: {typeInfo}) ---");
            sb.AppendLine(doc.Content);
            sb.AppendLine($"--- FIN DOCUMENTO: {doc.FileName} ---");
            sb.AppendLine();
        }

        sb.AppendLine("### REQUERIMIENTO / MENSAJE DEL USUARIO:");
        sb.AppendLine(userMessage);

        return sb.ToString().Trim();
    }

    public static string BuildSystemPrompt(ProjectInterviewAnswers answers)
    {
        return $"""
        Eres el Principal Software Architect & Tech Lead de AI DevTools Studio, potenciado con la arquitectura agéntica de Nous Hermes 3.
        Tu propósito es dialogar de forma ágil, analítica y de alto criterio técnico con el desarrollador, diseñando la solución ideal para su proyecto.

        CONTEXTO DEL PROYECTO ACTUAL:
        - Nombre: {answers.ProjectName}
        - Descripción: {answers.Description}
        - Estilo Arquitectónico: {answers.ArchitecturalStyle}
        - Frontend: {answers.FrontendStack}
        - Base de Datos: {answers.DatabaseType}

        DIRECTIVAS OBLIGATORIAS:
        1. CERO EMOJIS: Queda terminantemente prohibido utilizar emojis o caracteres pictográficos en cualquier parte de tu respuesta. Usa una estética sobria, limpia y profesional.
        2. DOMINIO CONCRETO Y RELEVANCIA: Responde de forma directa, inteligente y contextual a lo que el usuario está diciendo o solicitando. Modela y aborda explícitamente las entidades, reglas de negocio, invariantes y flujos que el usuario plantea en su requerimiento específico, diseñando agregados y atributos clave según el dominio consultado. Queda prohibido responder con textos genéricos o cambiar de tema.
        3. RAZONAMIENTO AGÉNTICO HERMES (<thought>):
        Antes de emitir tu propuesta técnica o cuando se te presenten requerimientos arquitectónicos complejos, puedes utilizar un bloque de razonamiento interno delimitado por <thought>...</thought> donde analices de forma concisa:
        - Evaluación de dominio, agregados e invariantes.
        - Compensaciones arquitectónicas (acoplamiento, patrones, concurrencia, persistencia).
        - Evaluación de calidad de software contra la norma ISO/IEC 25010 (mantenibilidad, confiabilidad, seguridad, eficiencia).
        Tras cerrar </thought>, entrega directamente tu respuesta técnica estructurada, sin preámbulos vacíos.
        4. CRITERIO ARQUITECTÓNICO: Explica el diseño de capas en .NET 9 Clean Architecture (Domain, Application, Infrastructure, Web/Api), EF Core 9, concurrencia, reglas de negocio encapsuladas, y la estrategia de persistencia relacional.
        5. FORMATO: Utiliza Markdown bien estructurado (negritas, subtítulos `###`, listas y bloques de código cuando aporten valor).
        6. PREGUNTAS SUGERIDAS: Al final de tu mensaje, incluye EXACTAMENTE 3 preguntas técnicas relevantes y contextuales para seguir guiando la arquitectura, delimitadas ESTRICTAMENTE así:
        [PREGUNTAS_SUGERIDAS]
        - Pregunta técnica 1
        - Pregunta técnica 2
        - Pregunta técnica 3
        [/PREGUNTAS_SUGERIDAS]
        7. HABILIDADES DE AGENTE CRUD: Tienes capacidad y autoridad ejecutiva para diseñar, modificar y refinar la arquitectura. Si el usuario te ordena modificar el stack, agregar tecnologías (como Redis, Kafka, RabbitMQ), renombrar el proyecto o alterar convenciones, asume el rol ejecutivo, confirma detalladamente los cambios aplicados en la arquitectura y explica su impacto técnico sin titubear.
        8. COMPATIBILIDAD CON GOOGLE ANTIGRAVITY Y DOCUMENTACION: El sistema genera documentación de nivel enterprise para pasar directamente a Google Antigravity y agentes de IA: AGENTS.md (con reglas de aislamiento de capas .NET 9 Clean Architecture, estándares C# 13, comandos CLI de verificación y cero emojis), .agents/AGENTS.md, README.md, ARCHITECTURE.md (con diagrama C4 Mermaid) y ADR-001. Cuando el usuario pregunte por Antigravity o documentación para codificación, confirma con seguridad estas capacidades y explica cómo se estructura y exporta.
        9. FEEDBACK DE CALIDAD Y COMPLETITUD DE REQUISITOS (ISO/IEC 25010): Evalúa activamente la completitud de los requerimientos del proyecto contra estándares de calidad de software (ISO/IEC 25010: adecuación funcional, confiabilidad, seguridad, eficiencia de desempeño, mantenibilidad y portabilidad). No te limites a asentir: evalúa qué requisitos críticos faltan por especificar (políticas de tolerancia a fallos, concurrencia, límites operativos, reglas de negocio de borde, RBAC) y haz preguntas concretas para cerrar las brechas antes de comenzar la codificación.
        10. DOCUMENTOS ADJUNTOS Y CONTEXTO TÉCNICO: Cuando el usuario adjunte documentos (código fuente C#, SQL, especificaciones JSON/YAML, requerimientos Markdown o diagramas), procesa y analiza su contenido exhaustivamente. Extrae entidades de negocio, esquemas de tablas, modelos de dominio, configuraciones o directrices arquitectónicas contenidas en los documentos. En tu scratchpad (<thought>), razona sobre los documentos adjuntos y luego refleja sus dependencias, restricciones y modelos en tu respuesta técnica y en el plano arquitectónico.
        """;
    }

    public static (string? Thought, string CleanedText) ExtractThought(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return (null, string.Empty);

        var thoughtStart = rawText.IndexOf("<thought>", StringComparison.OrdinalIgnoreCase);
        var thoughtEnd = rawText.IndexOf("</thought>", StringComparison.OrdinalIgnoreCase);

        if (thoughtStart >= 0 && thoughtEnd > thoughtStart)
        {
            var thought = rawText.Substring(thoughtStart + 9, thoughtEnd - (thoughtStart + 9)).Trim();
            var remaining = (rawText[..thoughtStart] + rawText[(thoughtEnd + 10)..]).Trim();
            return (thought, remaining);
        }

        return (null, rawText);
    }

    public static string CleanAssistantReply(string rawText)
    {
        var (thought, withoutThought) = ExtractThought(rawText);

        var marker = "PREGUNTAS_SUGERIDAS";
        var startIndex = withoutThought.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        string textToProcess = withoutThought;
        if (startIndex >= 0)
        {
            var cutIndex = startIndex;
            while (cutIndex > 0 && (withoutThought[cutIndex - 1] == '[' || withoutThought[cutIndex - 1] == '#' || withoutThought[cutIndex - 1] == ' ' || withoutThought[cutIndex - 1] == '\r' || withoutThought[cutIndex - 1] == '\n'))
            {
                cutIndex--;
                if (withoutThought[cutIndex] == '\n') break;
            }
            textToProcess = withoutThought[..cutIndex].Trim();
        }

        // Deduplicate pathological loop repetitions from LLMs
        var lines = textToProcess.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();
        string? prevLine = null;
        int repeatCount = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) && string.Equals(trimmed, prevLine, StringComparison.OrdinalIgnoreCase))
            {
                repeatCount++;
                if (repeatCount > 1) continue;
            }
            else
            {
                prevLine = string.IsNullOrEmpty(trimmed) ? null : trimmed;
                repeatCount = 0;
            }
            sb.AppendLine(line);
        }

        var cleanedResponse = StripEmojis(sb.ToString().Trim());

        if (!string.IsNullOrWhiteSpace(thought))
        {
            var cleanThought = StripEmojis(thought.Trim());
            return $"<details class=\"hermes-thought-card\"><summary>Razonamiento de Hermes 3 (Scratchpad)</summary>\n\n{cleanThought}\n\n</details>\n\n{cleanedResponse}";
        }

        return cleanedResponse;
    }

    private static string StripEmojis(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Strip emoji surrogate pairs and misc pictographic symbols
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(\uD83C[\uDF00-\uDFFF]|\uD83D[\uDC00-\uDE4F]|\uD83D[\uDE80-\uDEFF]|\uD83E[\uDD00-\uDDFF]|[\u2600-\u27BF])",
            string.Empty
        );
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
}
