using Spectre.Console;
using DevTools.Cli.UI;
using DevTools.Core.Models;
using DevTools.Orchestrator.Services;
using DevTools.Orchestrator.Skills;
using DevTools.Toolkit.Engine.Loaders;
using DevTools.Toolkit.Engine.Validators;

namespace DevTools.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConsoleRenderer.RenderBanner();

        var toolkitDir = ResolveToolkitDirectory(args);
        if (!Directory.Exists(toolkitDir) || !File.Exists(Path.Combine(toolkitDir, "toolkit.manifest.json")))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Toolkit not found at '[grey]{toolkitDir}[/]'.");
            AnsiConsole.MarkupLine("[grey]Use --toolkit <path> or run from the project root.[/]");
            return 1;
        }

        var loader = new FileToolkitLoader();
        var manifest = await loader.LoadManifestAsync(toolkitDir);

        using var dbContext = DevTools.Data.DbInitializer.CreateDbContext();
        await DevTools.Data.DbInitializer.InitializeAsync(dbContext);
        var projectRepo = new DevTools.Data.Repositories.SqliteProjectRepository(dbContext);
        var knowledgeRepo = new DevTools.Data.Repositories.SqliteKnowledgeRepository(dbContext);
        var docRepo = new DevTools.Data.Repositories.SqliteDocumentationRepository(dbContext);

        var config = LoadConfiguration();
        var providerOverride = GetArgValue(args, "--provider");
        var chatClient = DevTools.Orchestrator.Factories.ChatClientFactory.CreateClient(config, providerOverride);

        var orchestrator = new DefaultAgentOrchestrator(
            loader,
            toolkitDir,
            chatClient: chatClient,
            skillExecutors: [new LocalGitSkillExecutor()]
        );

        var activeEngine = chatClient is not null ? (providerOverride ?? config.Ai.DefaultProvider) : "Offline Heuristics (Ready for Ollama / OpenAI)";

        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "info";

        switch (command)
        {
            case "info":
            case "status":
                ConsoleRenderer.RenderManifest(manifest, toolkitDir);
                AnsiConsole.MarkupLine($"[grey]AI Execution Engine:[/] [bold white]{activeEngine}[/]");
                var validationIssues = ToolkitValidator.Validate(toolkitDir, manifest);
                if (validationIssues.Count == 0)
                {
                    AnsiConsole.MarkupLine("[green]Toolkit validation: 0 errors. Ready.[/]");
                }
                else
                {
                    foreach (var issue in validationIssues)
                    {
                        AnsiConsole.MarkupLine($"[yellow][[{issue.Scope}]] {issue.Message}[/]");
                    }
                }
                break;

            case "prompts":
                var prompts = await loader.LoadAllPromptsAsync(toolkitDir);
                ConsoleRenderer.RenderPromptsTable(prompts);
                break;

            case "skills":
                var skills = await loader.LoadAllSkillsAsync(toolkitDir);
                ConsoleRenderer.RenderSkillsTable(skills);
                break;

            case "review":
                var targetFile = args.Length > 1 ? args[1] : null;
                string codeToReview;
                if (!string.IsNullOrEmpty(targetFile) && File.Exists(targetFile))
                {
                    codeToReview = await File.ReadAllTextAsync(targetFile);
                    AnsiConsole.MarkupLine($"[grey]Auditing file:[/] [bold white]{targetFile}[/]");
                }
                else
                {
                    targetFile = "InlineSample.cs";
                    codeToReview = @"
public class UserService
{
    private string apiKey = ""sk-live-1234567890abcdef"";

    public User GetUser(int id)
    {
        // Anti-pattern: sync over async
        var user = FetchFromApiAsync(id).Result;
        return user;
    }

    private async Task<User> FetchFromApiAsync(int id) => new User();
}";
                    AnsiConsole.MarkupLine("[grey]No file specified. Auditing sample code with deliberate anti-patterns...[/]");
                }

                var response = await orchestrator.ExecuteAsync(new AgentRequest
                {
                    AgentId = "reviewer",
                    PromptId = "code-review.system",
                    TargetFilePath = targetFile,
                    CodeContent = codeToReview
                });

                if (response.ReviewReport is not null)
                {
                    ConsoleRenderer.RenderReviewReport(response.ReviewReport);
                }
                else if (response.RawOutput is not null)
                {
                    AnsiConsole.WriteLine(response.RawOutput);
                }
                break;

            case "git":
                AnsiConsole.MarkupLine("[grey]Executing git-inspector skill...[/]");
                var gitSkill = new LocalGitSkillExecutor();
                var gitResult = await gitSkill.ExecuteAsync(new DevTools.Core.Interfaces.SkillExecutionRequest
                {
                    SkillId = "git-inspector",
                    Parameters = new Dictionary<string, object?> { ["scope"] = "unstaged" }
                });

                if (gitResult.Success && gitResult.OutputJson is not null)
                {
                    AnsiConsole.MarkupLine("[bold white]Git Inspector Output:[/]");
                    AnsiConsole.WriteLine(gitResult.OutputJson);
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Git error:[/] {gitResult.Error}");
                }
                break;

            case "plan":
            case "interview":
                string projName;
                string projDesc;
                string archStyle;
                string frontend;
                string dbEngine;
                string loadProfile;

                if (args.Length >= 3)
                {
                    projName = args[1];
                    projDesc = args[2];
                    archStyle = args.Length > 3 ? args[3] : "Clean Architecture / Vertical Slices";
                    frontend = args.Length > 4 ? args[4] : "React + Minimalist Design System";
                    dbEngine = args.Length > 5 ? args[5] : "PostgreSQL + EF Core 9";
                    loadProfile = "Medium";
                }
                else
                {
                    AnsiConsole.MarkupLine("[bold white]INTERACTIVE PROJECT DISCOVERY & ARCHITECTURE PLANNER[/]");
                    AnsiConsole.MarkupLine("[grey]Answer a few quick questions to formulate your architecture, ADRs and project scaffolding.[/]\n");

                    projName = AnsiConsole.Ask<string>("[grey]1. Project Name:[/] ");
                    projDesc = AnsiConsole.Ask<string>("[grey]2. What core problem does it solve?[/] ");

                    archStyle = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[grey]3. Select Architectural Pattern:[/]")
                            .AddChoices(
                                "Modular Monolith",
                                "Clean Architecture / Vertical Slices",
                                "Event-Driven / CQRS",
                                "Microservices"
                            ));

                    frontend = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[grey]4. Select Frontend & UI Architecture:[/]")
                            .AddChoices(
                                "React + Minimalist Design System",
                                "Blazor WebAssembly + Classic Minimalist",
                                "Next.js + Design Tokens",
                                "Svelte + Minimalist CSS",
                                "CLI Terminal Only"
                            ));

                    dbEngine = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[grey]5. Select Database Engine:[/]")
                            .AddChoices(
                                "PostgreSQL + EF Core 9",
                                "SQLite Embebido (Zero-Config)",
                                "SQL Server + EF Core",
                                "MongoDB / DocumentDB"
                            ));

                    loadProfile = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("[grey]6. Expected Traffic / Scale:[/]")
                            .AddChoices(
                                "Low (<1,000 daily users)",
                                "Medium (<50,000 daily users)",
                                "High Enterprise (>100,000 daily users)"
                            ));
                }

                var plannerService = new DevTools.Orchestrator.Services.ProjectPlanningService(projectRepo, docRepo, knowledgeRepo, chatClient);
                var interviewAnswers = new DevTools.Core.Models.ProjectInterviewAnswers
                {
                    ProjectName = projName,
                    Description = projDesc,
                    ArchitecturalStyle = archStyle,
                    FrontendStack = frontend,
                    DatabaseType = dbEngine,
                    ExpectedLoad = loadProfile
                };

                DevTools.Core.Models.ProjectPlanBlueprint planBlueprint = null!;
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("[grey]Synthesizing architecture, ADRs and conventions...[/]", async ctx =>
                    {
                        planBlueprint = await plannerService.SynthesizePlanAsync(interviewAnswers, persistToDatabase: true);
                    });

                ConsoleRenderer.RenderBlueprint(planBlueprint);

                var scaffoldTarget = Path.Combine(Directory.GetCurrentDirectory(), "scaffold-" + projName.Replace(" ", "-").ToLowerInvariant());
                if (args.Length < 3)
                {
                    if (AnsiConsole.Confirm($"[bold white]Scaffold directory structure now?[/] ([grey]{scaffoldTarget}[/])"))
                    {
                        await plannerService.ScaffoldProjectDirectoryAsync(scaffoldTarget, planBlueprint);
                        AnsiConsole.MarkupLine($"[green]Scaffolding created successfully at:[/] {scaffoldTarget}");
                    }
                }
                break;

            case "projects":
                if (args.Length >= 3 && string.Equals(args[1], "register", StringComparison.OrdinalIgnoreCase))
                {
                    var name = args[2];
                    var path = args.Length > 3 ? args[3] : Directory.GetCurrentDirectory();
                    var p = await projectRepo.RegisterProjectAsync(name, path);
                    AnsiConsole.MarkupLine($"[green]Registered project:[/] [bold white]{p.Name}[/] at [grey]{p.RootPath}[/]");
                }
                else
                {
                    var projects = await projectRepo.ListProjectsAsync();
                    ConsoleRenderer.RenderProjectsTable(projects);
                }
                break;

            case "knowledge":
                if (args.Length >= 4 && string.Equals(args[1], "add", StringComparison.OrdinalIgnoreCase))
                {
                    var title = args[2];
                    var domain = args[3];
                    var content = args.Length > 4 ? args[4] : title;
                    var tags = args.Length > 5 ? args[5] : "";
                    var item = await knowledgeRepo.AddKnowledgeAsync(title, domain, content, tags);
                    AnsiConsole.MarkupLine($"[green]Knowledge item stored:[/] [bold white]{item.Title}[/] ([grey]{item.Domain}[/])");
                }
                else if (args.Length >= 3 && string.Equals(args[1], "search", StringComparison.OrdinalIgnoreCase))
                {
                    var query = args[2];
                    var results = await knowledgeRepo.SearchKnowledgeAsync(query);
                    AnsiConsole.MarkupLine($"[grey]Search results for:[/] '[bold white]{query}[/]'");
                    ConsoleRenderer.RenderKnowledgeTable(results);
                }
                else
                {
                    var items = await knowledgeRepo.ListKnowledgeAsync();
                    ConsoleRenderer.RenderKnowledgeTable(items);
                }
                break;

            case "audit":
            case "improve":
                var targetPath = GetArgValue(args, "--target") ?? Directory.GetCurrentDirectory();
                var modelArg = GetArgValue(args, "--model") ?? (chatClient is not null ? DevTools.Orchestrator.Factories.ChatClientFactory.CurrentResolvedModel : "hermes3:8b");
                AnsiConsole.MarkupLine("[bold white]HERMES 3 CONTINUOUS IMPROVEMENT & ARCHITECTURAL AUDIT[/]");
                AnsiConsole.MarkupLine($"[grey]Target Path:[/] [bold]{targetPath}[/]");
                AnsiConsole.MarkupLine($"[grey]Model:[/] [bold blue]{modelArg}[/]\n");

                var improvementService = new HermesContinuousImprovementService(
                    chatClient: chatClient,
                    config: config
                );

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Scanning codebase metrics and evaluating ISO/IEC 25010 characteristics...", async _ =>
                    {
                        var report = await improvementService.AuditProjectAsync(targetPath, modelArg);

                        AnsiConsole.MarkupLine("\n[bold white]CODEBASE METRICS SUMMARY[/]");
                        var mTable = new Table().Border(TableBorder.Rounded);
                        mTable.AddColumn("Metric");
                        mTable.AddColumn("Value");
                        mTable.AddRow("Total Projects", report.Metrics.TotalProjects.ToString());
                        mTable.AddRow("C# Files", report.Metrics.TotalCSharpFiles.ToString());
                        mTable.AddRow("Lines of Code", report.Metrics.TotalLinesOfCode.ToString("N0"));
                        mTable.AddRow("Unit Tests", report.Metrics.TotalTestCases.ToString());
                        mTable.AddRow("Clean Architecture", report.Metrics.CleanArchitectureCompliant ? "[green]Compliant[/]" : "[yellow]Review[/]");
                        mTable.AddRow("Scan Duration", $"{report.Metrics.ScanDurationMs} ms");
                        AnsiConsole.Write(mTable);

                        AnsiConsole.MarkupLine("\n[bold white]ISO/IEC 25010 SOFTWARE QUALITY SCORES[/]");
                        var qTable = new Table().Border(TableBorder.Rounded);
                        qTable.AddColumn("Characteristic");
                        qTable.AddColumn("Score");
                        qTable.AddRow("Maintainability", $"{report.QualityScores.MaintainabilityScore}%");
                        qTable.AddRow("Reliability", $"{report.QualityScores.ReliabilityScore}%");
                        qTable.AddRow("Performance Efficiency", $"{report.QualityScores.PerformanceScore}%");
                        qTable.AddRow("Security", $"{report.QualityScores.SecurityScore}%");
                        qTable.AddRow("[bold]Overall Quality Score[/]", $"[bold green]{report.QualityScores.OverallQualityScore}/100[/]");
                        AnsiConsole.Write(qTable);

                        if (!string.IsNullOrWhiteSpace(report.ThoughtScratchpad))
                        {
                            AnsiConsole.MarkupLine("\n[bold blue]NOUS HERMES 3 COGNITIVE SCRATCHPAD (<thought>)[/]");
                            var panel = new Panel(report.ThoughtScratchpad.Trim())
                            {
                                Header = new PanelHeader("[bold]Hermes 3 Deep Reasoning[/]"),
                                Border = BoxBorder.Rounded
                            };
                            AnsiConsole.Write(panel);
                        }

                        AnsiConsole.MarkupLine("\n[bold white]PRIORITIZED CONTINUOUS IMPROVEMENT PROPOSALS[/]");
                        var pTable = new Table().Border(TableBorder.Rounded);
                        pTable.AddColumn("ID");
                        pTable.AddColumn("Impact");
                        pTable.AddColumn("Category");
                        pTable.AddColumn("Title");
                        pTable.AddColumn("Antigravity Directive");

                        foreach (var p in report.Proposals)
                        {
                            var impactColor = p.Impact switch
                            {
                                ProposalImpact.Critical => "red",
                                ProposalImpact.High => "yellow",
                                _ => "blue"
                            };
                            pTable.AddRow(
                                $"[grey]{p.Id}[/]",
                                $"[{impactColor}]{p.Impact}[/]",
                                p.Category.ToString(),
                                p.Title,
                                p.AntigravityActionPlan.Length > 80 ? p.AntigravityActionPlan[..77] + "..." : p.AntigravityActionPlan
                            );
                        }
                        AnsiConsole.Write(pTable);
                    });
                break;

            case "docs":
                if (args.Length >= 4 && string.Equals(args[1], "add", StringComparison.OrdinalIgnoreCase))
                {
                    var title = args[2];
                    var docType = args[3];
                    var contentPath = args.Length > 4 ? args[4] : "";
                    var content = File.Exists(contentPath) ? await File.ReadAllTextAsync(contentPath) : contentPath;
                    var doc = await docRepo.SaveDocumentAsync(title, docType, content);
                    AnsiConsole.MarkupLine($"[green]Document saved:[/] [bold white]{doc.Title}[/] ([grey]{doc.DocType}[/])");
                }
                else
                {
                    var docs = await docRepo.ListDocumentsAsync();
                    ConsoleRenderer.RenderDocumentsTable(docs);
                }
                break;

            case "import":
                if (args.Length < 2)
                {
                    AnsiConsole.MarkupLine("[red]Usage:[/] devtools import <url> [category]");
                    return 1;
                }

                if (!Uri.TryCreate(args[1], UriKind.Absolute, out var remoteUrl))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Invalid absolute URL specified.");
                    return 1;
                }

                var targetCategory = args.Length > 2 ? args[2] : null;
                AnsiConsole.MarkupLine($"[grey]Importing remote skill from:[/] [white]{remoteUrl}[/]");
                var importer = new DevTools.Toolkit.Engine.Importers.SkillWebImporter();
                var importResult = await importer.ImportFromUrlAsync(toolkitDir, remoteUrl, targetCategory);

                if (importResult.Success)
                {
                    AnsiConsole.MarkupLine($"[green]Successfully imported skill:[/] [bold white]{importResult.SkillId}[/]");
                    AnsiConsole.MarkupLine($"[grey]Saved to:[/] {importResult.InstalledPath}");
                    AnsiConsole.MarkupLine("[grey]Registered in toolkit.manifest.json automatically.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Import failed:[/] {importResult.ErrorMessage}");
                }
                break;

            case "help":
            default:
                RenderHelp();
                break;
        }

        return 0;
    }

    private static string ResolveToolkitDirectory(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--toolkit")
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        return DevTools.Core.Common.SolutionPathResolver.FindToolkitDirectory();
    }

    private static void RenderHelp()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]AVAILABLE COMMANDS[/]")
            .AddColumn(new TableColumn("[grey]Command[/]"))
            .AddColumn(new TableColumn("[grey]Description[/]"));

        table.AddRow("[bold white]info[/]", "Display toolkit manifest, paths, and health status");
        table.AddRow("[bold white]prompts[/]", "List all structured prompts registered in the toolkit");
        table.AddRow("[bold white]skills[/]", "List all declared skills and functional tools across domains");
        table.AddRow("[bold white]review <file>[/]", "Run clean code & security review on a source file");
        table.AddRow("[bold white]projects [register][/]", "List tracked projects or register current workspace in DB");
        table.AddRow("[bold white]knowledge [add|search][/]", "Query or store team knowledge, conventions and architectural rules");
        table.AddRow("[bold white]docs [add][/]", "List or save ADRs and generated architectural documentation");
        table.AddRow("[bold white]plan[/]", "Interactive architectural interview, C4 synthesis, ADR generation & scaffolding");
        table.AddRow("[bold white]audit [--target][/]", "Run Hermes 3 architectural audit, ISO/IEC 25010 scoring and scratchpad");
        table.AddRow("[bold white]import <url> [cat][/]", "Extract and register a skill definition from the web/GitHub");
        table.AddRow("[bold white]git[/]", "Execute git-inspector to inspect repository status and diffs");
        table.AddRow("[bold white]help[/]", "Show this help screen");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static DevTools.Core.Configuration.DevToolsConfig LoadConfiguration()
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
            catch
            {
                // Fallback default config on invalid JSON
            }
        }

        return new DevTools.Core.Configuration.DevToolsConfig().Normalize();
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
