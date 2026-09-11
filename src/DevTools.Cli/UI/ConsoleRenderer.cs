using Spectre.Console;
using DevTools.Core.Models;

namespace DevTools.Cli.UI;

public static class ConsoleRenderer
{
    public static void RenderBanner()
    {
        AnsiConsole.WriteLine();
        var rule = new Rule("[grey]DEVTOOLS // AI AGENTS & TOOLKIT[/]")
        {
            Border = BoxBorder.Rounded
        }.LeftJustified();
        AnsiConsole.Write(rule);
        AnsiConsole.MarkupLine("[grey]Classic Minimalist Interface | .NET 9 LTS Ready[/]");
        AnsiConsole.WriteLine();
    }

    public static void RenderManifest(ToolkitManifest manifest, string toolkitPath)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Property[/]").LeftAligned())
            .AddColumn(new TableColumn("[grey]Value[/]").LeftAligned());

        table.AddRow("Name", $"[bold white]{manifest.Name}[/]");
        table.AddRow("Version", $"[white]{manifest.Version}[/]");
        table.AddRow("Author", $"[white]{manifest.Author ?? "N/A"}[/]");
        table.AddRow("License", $"[white]{manifest.License ?? "N/A"}[/]");
        table.AddRow("Toolkit Path", $"[grey]{toolkitPath}[/]");
        table.AddRow("Registered Prompts", $"[bold white]{manifest.Prompts.Count}[/]");
        table.AddRow("Registered Skills", $"[bold white]{manifest.Skills.Count}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void RenderPromptsTable(IReadOnlyList<PromptDefinition> prompts)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]CATALOG OF PROMPTS[/]")
            .AddColumn(new TableColumn("[grey]ID[/]"))
            .AddColumn(new TableColumn("[grey]Name[/]"))
            .AddColumn(new TableColumn("[grey]Description[/]"))
            .AddColumn(new TableColumn("[grey]Rules[/]"));

        foreach (var p in prompts)
        {
            var hasRules = !string.IsNullOrEmpty(p.RulesContent) ? "[green]Yes[/]" : "[grey]No[/]";
            table.AddRow($"[bold white]{p.Id}[/]", p.Name, p.Description ?? "-", hasRules);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void RenderSkillsTable(IReadOnlyList<SkillDefinition> skills)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]CATALOG OF MULTI-DOMAIN SKILLS[/]")
            .AddColumn(new TableColumn("[grey]ID[/]"))
            .AddColumn(new TableColumn("[grey]Name[/]"))
            .AddColumn(new TableColumn("[grey]Category / Domain[/]"))
            .AddColumn(new TableColumn("[grey]Description[/]"));

        foreach (var s in skills)
        {
            var category = "core";
            if (s.FilePath.Contains("databases", StringComparison.OrdinalIgnoreCase)) category = "Database";
            else if (s.FilePath.Contains("apis", StringComparison.OrdinalIgnoreCase)) category = "API & Contracts";
            else if (s.FilePath.Contains("ui", StringComparison.OrdinalIgnoreCase)) category = "UI / Frontend";
            else if (s.FilePath.Contains("pipelines", StringComparison.OrdinalIgnoreCase)) category = "Data Pipelines";

            table.AddRow($"[bold white]{s.Id}[/]", s.Name, $"[grey]{category}[/]", s.Description ?? "-");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void RenderReviewReport(CodeReviewReport report)
    {
        var scoreColor = report.Score >= 80 ? "green" : (report.Score >= 50 ? "yellow" : "red");

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Score[/]").Centered())
            .AddColumn(new TableColumn("[grey]Verdict[/]").Centered())
            .AddColumn(new TableColumn("[grey]Critical[/]").Centered())
            .AddColumn(new TableColumn("[grey]High[/]").Centered())
            .AddColumn(new TableColumn("[grey]Medium[/]").Centered())
            .AddColumn(new TableColumn("[grey]Low[/]").Centered());

        summaryTable.AddRow(
            $"[{scoreColor} bold]{report.Score} / 100[/]",
            $"[bold white]{report.Verdict}[/]",
            $"[red]{report.Metrics.CriticalCount}[/]",
            $"[yellow]{report.Metrics.HighCount}[/]",
            $"[grey]{report.Metrics.MediumCount}[/]",
            $"[grey]{report.Metrics.LowCount}[/]"
        );

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[grey]Summary:[/] [white]{report.Summary}[/]");
        AnsiConsole.WriteLine();

        if (report.Issues.Count > 0)
        {
            var issuesTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[bold white]ACTIONABLE FINDINGS[/]")
                .AddColumn(new TableColumn("[grey]Severity[/]"))
                .AddColumn(new TableColumn("[grey]Rule[/]"))
                .AddColumn(new TableColumn("[grey]Location[/]"))
                .AddColumn(new TableColumn("[grey]Finding & Recommendation[/]"));

            foreach (var issue in report.Issues)
            {
                var sevColor = issue.Severity switch
                {
                    ReviewSeverity.CRITICAL => "red bold",
                    ReviewSeverity.HIGH => "yellow bold",
                    ReviewSeverity.MEDIUM => "blue",
                    _ => "grey"
                };

                var loc = issue.Line.HasValue ? $"{issue.File}:{issue.Line}" : issue.File;
                var text = $"[white]{issue.Message}[/]\n[grey]-> {issue.Recommendation}[/]";

                if (issue.SuggestedFix?.ReplacementCode is not null)
                {
                    text += $"\n[green]Fix: {issue.SuggestedFix.ReplacementCode}[/]";
                }

                issuesTable.AddRow($"[{sevColor}]{issue.Severity}[/]", issue.RuleId, $"[grey]{loc}[/]", text);
            }

            AnsiConsole.Write(issuesTable);
        }
        else
        {
            AnsiConsole.MarkupLine("[green]No issues found. Clean code standards met.[/]");
        }

        AnsiConsole.WriteLine();
    }

    public static void RenderProjectsTable(IReadOnlyList<DevTools.Core.Entities.ProjectEntity> projects)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]TRACKED PROJECTS[/]")
            .AddColumn(new TableColumn("[grey]Name[/]"))
            .AddColumn(new TableColumn("[grey]Root Path[/]"))
            .AddColumn(new TableColumn("[grey]Last Score[/]").Centered())
            .AddColumn(new TableColumn("[grey]Last Audited[/]").Centered());

        foreach (var p in projects)
        {
            var scoreColor = p.LastQualityScore >= 80 ? "green" : (p.LastQualityScore >= 50 ? "yellow" : "red");
            var auditedText = p.LastAuditedAt.HasValue ? p.LastAuditedAt.Value.ToString("yyyy-MM-dd HH:mm") : "[grey]Never[/]";

            table.AddRow($"[bold white]{p.Name}[/]", $"[grey]{p.RootPath}[/]", $"[{scoreColor}]{p.LastQualityScore}%[/]", auditedText);
        }

        if (projects.Count == 0)
        {
            table.AddRow("[grey]No projects registered yet[/]", "[grey]-[/]", "[grey]-[/]", "[grey]-[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void RenderKnowledgeTable(IReadOnlyList<DevTools.Core.Entities.KnowledgeItemEntity> knowledge)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]KNOWLEDGE BASE[/]")
            .AddColumn(new TableColumn("[grey]Domain[/]"))
            .AddColumn(new TableColumn("[grey]Title[/]"))
            .AddColumn(new TableColumn("[grey]Tags[/]"))
            .AddColumn(new TableColumn("[grey]Content Snippet[/]"));

        foreach (var k in knowledge)
        {
            var snippet = k.Content.Length > 60 ? k.Content.Substring(0, 57) + "..." : k.Content;
            table.AddRow($"[bold white]{k.Domain}[/]", k.Title, $"[grey]{k.Tags}[/]", $"[white]{snippet}[/]");
        }

        if (knowledge.Count == 0)
        {
            table.AddRow("[grey]-[/]", "[grey]No knowledge items saved yet[/]", "[grey]-[/]", "[grey]-[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void RenderDocumentsTable(IReadOnlyList<DevTools.Core.Entities.DocumentationEntity> docs)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]DOCUMENTS & ADRs[/]")
            .AddColumn(new TableColumn("[grey]Type[/]"))
            .AddColumn(new TableColumn("[grey]Title[/]"))
            .AddColumn(new TableColumn("[grey]Version[/]"))
            .AddColumn(new TableColumn("[grey]Date[/]"));

        foreach (var d in docs)
        {
            table.AddRow($"[bold white]{d.DocType}[/]", d.Title, $"[grey]v{d.Version}[/]", d.CreatedAt.ToString("yyyy-MM-dd"));
        }

        if (docs.Count == 0)
        {
            table.AddRow("[grey]-[/]", "[grey]No documentation stored yet[/]", "[grey]-[/]", "[grey]-[/]");
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public static void RenderBlueprint(DevTools.Core.Models.ProjectPlanBlueprint blueprint)
    {
        var rule = new Rule($"[bold white]ARCHITECTURE BLUEPRINT: {blueprint.ProjectName.ToUpperInvariant()}[/]")
        {
            Border = BoxBorder.Rounded
        }.LeftJustified();
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[grey]Executive Summary:[/] [white]{blueprint.ExecutiveSummary}[/]");
        AnsiConsole.MarkupLine($"[grey]Rationale:[/] [white]{blueprint.ArchitecturalRationale}[/]");
        AnsiConsole.WriteLine();

        var techTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]RECOMMENDED TECH STACK[/]")
            .AddColumn(new TableColumn("[grey]Layer[/]"))
            .AddColumn(new TableColumn("[grey]Selection[/]"));

        foreach (var (k, v) in blueprint.TechStack)
        {
            techTable.AddRow($"[bold white]{k}[/]", $"[white]{v}[/]");
        }

        AnsiConsole.Write(techTable);
        AnsiConsole.WriteLine();

        var convTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold white]ARCHITECTURAL CONVENTIONS PERSISTED[/]")
            .AddColumn(new TableColumn("[grey]Standard[/]"));

        foreach (var conv in blueprint.KeyConventions)
        {
            convTable.AddRow($"[white]{conv}[/]");
        }

        AnsiConsole.Write(convTable);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine($"[green]Initial ADR Created:[/] [bold white]{blueprint.InitialAdrTitle}[/]");
        AnsiConsole.MarkupLine("[grey]Saved automatically to SQLite database Documentation table.[/]");
        AnsiConsole.WriteLine();
    }
}
