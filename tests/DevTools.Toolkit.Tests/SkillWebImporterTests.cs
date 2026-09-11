using System.Net;
using System.Text;
using FluentAssertions;
using DevTools.Toolkit.Engine.Importers;
using DevTools.Toolkit.Engine.Loaders;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class SkillWebImporterTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public MockHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task ImportFromUrlAsync_ShouldDownloadAndRegisterSkill()
    {
        // Arrange
        var tempToolkitDir = Path.Combine(Path.GetTempPath(), "devtools_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempToolkitDir);

        var manifestPath = Path.Combine(tempToolkitDir, "toolkit.manifest.json");
        await File.WriteAllTextAsync(manifestPath, """
        {
          "name": "test-toolkit",
          "version": "1.0.0",
          "prompts": [],
          "skills": []
        }
        """);

        var sampleRemoteSkillJson = """
        {
          "id": "remote-sample-skill",
          "name": "Remote Sample Skill",
          "category": "databases",
          "version": "1.0.0",
          "description": "A sample skill imported from the web.",
          "parameters": {
            "type": "object",
            "properties": {
              "input": { "type": "string" }
            }
          }
        }
        """;

        var httpClient = new HttpClient(new MockHttpMessageHandler(sampleRemoteSkillJson));
        var importer = new SkillWebImporter(httpClient);

        // Act
        var result = await importer.ImportFromUrlAsync(tempToolkitDir, new Uri("https://example.com/sample-skill.json"));

        // Assert
        result.Success.Should().BeTrue();
        result.SkillId.Should().Be("remote-sample-skill");
        File.Exists(result.InstalledPath!).Should().BeTrue();

        // Verify it was registered in manifest
        var loader = new FileToolkitLoader();
        var updatedManifest = await loader.LoadManifestAsync(tempToolkitDir);
        updatedManifest.Skills.Should().ContainSingle(s => s.Id == "remote-sample-skill");

        // Clean up
        try { Directory.Delete(tempToolkitDir, true); } catch { }
    }
}
