using DevTools.Core.Common;
using DevTools.Core.Configuration;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class SolutionPathResolverTests
{
    [Fact]
    public void FindSolutionRoot_FromAnyChildDirectory_FindsDevToolsRoot()
    {
        var current = Directory.GetCurrentDirectory();
        var root = SolutionPathResolver.FindSolutionRoot(current);

        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root, "DevTools.sln")), "Debe encontrar el archivo DevTools.sln en la raíz");
    }

    [Fact]
    public void FindToolkitDirectory_FindsDirectoryWithManifest()
    {
        var toolkitDir = SolutionPathResolver.FindToolkitDirectory();

        Assert.NotNull(toolkitDir);
        Assert.True(Directory.Exists(toolkitDir));
        Assert.True(File.Exists(Path.Combine(toolkitDir, "toolkit.manifest.json")), "Debe contener toolkit.manifest.json");
    }

    [Fact]
    public void FindConfigFile_FindsDevToolsConfig()
    {
        var configPath = SolutionPathResolver.FindConfigFile();

        Assert.NotNull(configPath);
        Assert.True(File.Exists(configPath));
        Assert.EndsWith("devtools.config.json", configPath);
    }

    [Fact]
    public void NormalizePath_ConvertsRelativePathToAbsolutePath()
    {
        var normalized = SolutionPathResolver.NormalizePath("./toolkit");

        Assert.NotNull(normalized);
        Assert.True(Path.IsPathRooted(normalized));
        Assert.True(Directory.Exists(normalized));
    }

    [Fact]
    public void DevToolsConfig_Normalize_ResolvesAbsolutePathForToolkit()
    {
        var config = new DevToolsConfig
        {
            Toolkit = new ToolkitConfig { Path = "./toolkit" }
        };

        var normalized = config.Normalize();

        Assert.True(Path.IsPathRooted(normalized.Toolkit.Path));
        Assert.Contains("toolkit", normalized.Toolkit.Path);
    }
}
