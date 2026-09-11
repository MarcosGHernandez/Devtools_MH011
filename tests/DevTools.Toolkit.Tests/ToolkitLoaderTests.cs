using FluentAssertions;
using DevTools.Toolkit.Engine.Loaders;
using DevTools.Toolkit.Engine.Validators;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class ToolkitLoaderTests
{
    private static string FindToolkitPath()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "toolkit");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "toolkit.manifest.json")))
            {
                return candidate;
            }
            var parent = Directory.GetParent(current);
            if (parent is null) break;
            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate 'toolkit' directory from test execution folder.");
    }

    [Fact]
    public async Task LoadManifestAsync_ShouldLoadValidManifest()
    {
        // Arrange
        var toolkitDir = FindToolkitPath();
        var loader = new FileToolkitLoader();

        // Act
        var manifest = await loader.LoadManifestAsync(toolkitDir);

        // Assert
        manifest.Should().NotBeNull();
        manifest.Name.Should().Be("devtools-standard-toolkit");
        manifest.Prompts.Should().NotBeEmpty();
        manifest.Skills.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadAllPromptsAsync_ShouldParsePromptsAndFrontmatter()
    {
        // Arrange
        var toolkitDir = FindToolkitPath();
        var loader = new FileToolkitLoader();

        // Act
        var prompts = await loader.LoadAllPromptsAsync(toolkitDir);

        // Assert
        prompts.Should().NotBeEmpty();
        var codeReview = prompts.FirstOrDefault(p => p.Id == "code-review.system");
        codeReview.Should().NotBeNull();
        codeReview!.Name.Should().Be("Comprehensive Code Review");
        codeReview.RulesContent.Should().NotBeNullOrWhiteSpace();
        codeReview.OutputSchemaContent.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Validate_ShouldReportZeroErrorsOnStandardToolkit()
    {
        // Arrange
        var toolkitDir = FindToolkitPath();
        var loader = new FileToolkitLoader();
        var manifest = await loader.LoadManifestAsync(toolkitDir);

        // Act
        var issues = ToolkitValidator.Validate(toolkitDir, manifest);

        // Assert
        issues.Should().BeEmpty();
    }
}
