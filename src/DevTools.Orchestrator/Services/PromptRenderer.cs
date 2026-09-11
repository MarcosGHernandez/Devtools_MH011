using System.Text;
using DevTools.Core.Models;

namespace DevTools.Orchestrator.Services;

public static class PromptRenderer
{
    public static string Render(PromptDefinition prompt, AgentRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(prompt.Body);

        if (!string.IsNullOrWhiteSpace(prompt.RulesContent))
        {
            sb.AppendLine();
            sb.AppendLine("## Applicable Quality Rules:");
            sb.AppendLine(prompt.RulesContent);
        }

        if (!string.IsNullOrWhiteSpace(prompt.OutputSchemaContent))
        {
            sb.AppendLine();
            sb.AppendLine("## Required Output JSON Schema:");
            sb.AppendLine(prompt.OutputSchemaContent);
        }

        var rendered = sb.ToString();

        if (!string.IsNullOrWhiteSpace(request.TargetFilePath))
        {
            rendered = rendered.Replace("{{file}}", request.TargetFilePath);
        }

        if (!string.IsNullOrWhiteSpace(request.CodeContent))
        {
            rendered = rendered.Replace("{{code}}", request.CodeContent);
        }

        foreach (var (k, v) in request.Arguments)
        {
            rendered = rendered.Replace($"{{{{{k}}}}}", v);
        }

        return rendered;
    }
}
