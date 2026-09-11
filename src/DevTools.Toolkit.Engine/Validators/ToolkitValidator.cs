using System.Text.Json;
using DevTools.Core.Models;

namespace DevTools.Toolkit.Engine.Validators;

public sealed record ValidationIssue(string Scope, string Message, bool IsError);

public static class ToolkitValidator
{
    public static List<ValidationIssue> Validate(string toolkitDirectory, ToolkitManifest manifest)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            issues.Add(new ValidationIssue("Manifest", "Manifest 'name' is required.", true));
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            issues.Add(new ValidationIssue("Manifest", "Manifest 'version' is required.", true));
        }

        foreach (var prompt in manifest.Prompts)
        {
            var pPath = Path.Combine(toolkitDirectory, prompt.Path);
            if (!File.Exists(pPath))
            {
                issues.Add(new ValidationIssue($"Prompt:{prompt.Id}", $"Target file does not exist: '{prompt.Path}'", true));
            }

            if (!string.IsNullOrWhiteSpace(prompt.RulesPath))
            {
                var rPath = Path.Combine(toolkitDirectory, prompt.RulesPath);
                if (!File.Exists(rPath))
                {
                    issues.Add(new ValidationIssue($"Prompt:{prompt.Id}", $"Rules file does not exist: '{prompt.RulesPath}'", true));
                }
            }

            if (!string.IsNullOrWhiteSpace(prompt.OutputSchema))
            {
                var sPath = Path.Combine(toolkitDirectory, prompt.OutputSchema);
                if (!File.Exists(sPath))
                {
                    issues.Add(new ValidationIssue($"Prompt:{prompt.Id}", $"Schema file does not exist: '{prompt.OutputSchema}'", true));
                }
            }
        }

        foreach (var skill in manifest.Skills)
        {
            var sPath = Path.Combine(toolkitDirectory, skill.Path);
            if (!File.Exists(sPath))
            {
                issues.Add(new ValidationIssue($"Skill:{skill.Id}", $"Skill definition file does not exist: '{skill.Path}'", true));
            }
            else
            {
                try
                {
                    var text = File.ReadAllText(sPath);
                    using var _ = JsonDocument.Parse(text);
                }
                catch (JsonException ex)
                {
                    issues.Add(new ValidationIssue($"Skill:{skill.Id}", $"Invalid JSON in skill definition: {ex.Message}", true));
                }
            }
        }

        return issues;
    }
}
