namespace DevTools.Core.Common;

public static class SolutionPathResolver
{
    public static string FindSolutionRoot(string? startDirectory = null)
    {
        var dir = !string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory)
            ? startDirectory
            : Directory.GetCurrentDirectory();

        var curr = dir;
        while (!string.IsNullOrEmpty(curr))
        {
            if (File.Exists(Path.Combine(curr, "DevTools.sln")) || Directory.GetFiles(curr, "*.sln").Length > 0)
            {
                return curr;
            }

            var parent = Directory.GetParent(curr);
            if (parent is null) break;
            curr = parent.FullName;
        }

        return dir;
    }

    public static string FindToolkitDirectory(string? startDirectory = null)
    {
        var root = FindSolutionRoot(startDirectory);
        var direct = Path.Combine(root, "toolkit");
        if (Directory.Exists(direct) && File.Exists(Path.Combine(direct, "toolkit.manifest.json")))
        {
            return direct;
        }

        var dir = !string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory)
            ? startDirectory
            : Directory.GetCurrentDirectory();

        var curr = dir;
        while (!string.IsNullOrEmpty(curr))
        {
            var candidate = Path.Combine(curr, "toolkit");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "toolkit.manifest.json")))
            {
                return candidate;
            }

            var parent = Directory.GetParent(curr);
            if (parent is null) break;
            curr = parent.FullName;
        }

        return Path.Combine(root, "toolkit");
    }

    public static string? FindConfigFile(string? startDirectory = null)
    {
        var root = FindSolutionRoot(startDirectory);
        var rootConfig = Path.Combine(root, "devtools.config.json");
        if (File.Exists(rootConfig)) return rootConfig;

        var dir = !string.IsNullOrWhiteSpace(startDirectory) && Directory.Exists(startDirectory)
            ? startDirectory
            : Directory.GetCurrentDirectory();

        var curr = dir;
        while (!string.IsNullOrEmpty(curr))
        {
            var candidate = Path.Combine(curr, "devtools.config.json");
            if (File.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(curr);
            if (parent is null) break;
            curr = parent.FullName;
        }

        return null;
    }

    public static string NormalizePath(string path, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        var root = baseDirectory ?? FindSolutionRoot();
        return Path.GetFullPath(Path.Combine(root, path));
    }
}
