using System.Diagnostics;
using System.Text.Json;
using DevTools.Core.Interfaces;

namespace DevTools.Orchestrator.Skills;

public sealed class LocalGitSkillExecutor : ISkillExecutor
{
    public string SupportedSkillId => "git-inspector";

    public async Task<SkillExecutionResult> ExecuteAsync(SkillExecutionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var repoPath = request.Parameters.TryGetValue("repositoryPath", out var p) && p is string path && !string.IsNullOrWhiteSpace(path)
                ? path
                : Directory.GetCurrentDirectory();

            var scope = request.Parameters.TryGetValue("scope", out var s) && s is string scopeStr
                ? scopeStr
                : "staged";

            var diffArgs = scope switch
            {
                "staged" => "diff --cached",
                "unstaged" => "diff",
                "all" => "diff HEAD",
                "last-commit" => "diff HEAD~1 HEAD",
                _ => "diff --cached"
            };

            var branchOutput = await RunGitCommandAsync(repoPath, "rev-parse --abbrev-ref HEAD", cancellationToken);
            var statusOutput = await RunGitCommandAsync(repoPath, "status --porcelain", cancellationToken);
            var diffOutput = await RunGitCommandAsync(repoPath, diffArgs, cancellationToken);

            var changedFiles = statusOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Length > 3 ? line.Substring(3).Trim() : line)
                .ToList();

            var resultPayload = new
            {
                currentBranch = branchOutput.Trim(),
                changedFiles,
                diff = diffOutput
            };

            return new SkillExecutionResult
            {
                SkillId = SupportedSkillId,
                Success = true,
                OutputJson = JsonSerializer.Serialize(resultPayload, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult
            {
                SkillId = SupportedSkillId,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<string> RunGitCommandAsync(string workingDir, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch git process.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
        {
            return $"Error: {stderr}";
        }

        return stdout;
    }
}
