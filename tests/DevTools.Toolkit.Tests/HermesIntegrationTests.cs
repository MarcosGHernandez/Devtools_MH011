using DevTools.Core.Models;
using DevTools.Orchestrator.Factories;
using DevTools.Orchestrator.Services;
using FluentAssertions;
using Xunit;

namespace DevTools.Toolkit.Tests;

public class HermesIntegrationTests
{
    [Fact]
    public void PickBestModel_WhenPreferredIsHermes3_SelectsExactMatch()
    {
        var installed = new List<string> { "qwen2.5-coder:7b", "hermes3:8b", "llama3.1:8b" };
        var selected = ChatClientFactory.PickBestModel("hermes3:8b", installed);

        selected.Should().Be("hermes3:8b");
    }

    [Fact]
    public void PickBestModel_WhenHermesRequestedByGenericName_ResolvesHermesVariant()
    {
        var installed = new List<string> { "qwen2.5-coder:7b", "hermes3:8b", "llama3.1:8b" };
        var selected = ChatClientFactory.PickBestModel("hermes", installed);

        selected.Should().Be("hermes3:8b");
    }

    [Fact]
    public void PickBestModel_WhenHermesInstalledAndAgentRequested_SelectsHermes3()
    {
        var installed = new List<string> { "hermes3:8b", "llama3.1:8b" };
        var selected = ChatClientFactory.PickBestModel("agent", installed);

        selected.Should().Be("hermes3:8b");
    }

    [Fact]
    public void PickBestModel_WhenNoHermesRequested_FallsBackToQwenCoderOrPriority()
    {
        var installed = new List<string> { "qwen2.5-coder:7b", "hermes3:8b" };
        var selected = ChatClientFactory.PickBestModel("default", installed);

        selected.Should().Be("qwen2.5-coder:7b");
    }

    [Theory]
    [InlineData("hermes3:8b", true)]
    [InlineData("hermes3", true)]
    [InlineData("nous-hermes-2", true)]
    [InlineData("Hermes-Agent-Model", true)]
    [InlineData("qwen2.5-coder:7b", false)]
    [InlineData("llama3.1:8b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsHermesModel_AccuratelyDetectsHermesFamily(string? modelName, bool expected)
    {
        ChatClientFactory.IsHermesModel(modelName).Should().Be(expected);
    }

    [Fact]
    public void ExtractThought_WhenThoughtBlockPresent_ExtractsScratchpadCorrectly()
    {
        var raw = """
        <thought>
        Analizando los requisitos de alta concurrencia y tolerancia a fallos.
        Recomiendo aplicar el patrón Outbox para la publicación de eventos hacia Kafka.
        </thought>
        ### Propuesta de Arquitectura
        Se implementa Clean Architecture con .NET 9.
        """;

        var (thought, cleaned) = ProjectPlanningService.ExtractThought(raw);

        thought.Should().NotBeNull();
        thought.Should().Contain("Analizando los requisitos de alta concurrencia");
        thought.Should().Contain("patrón Outbox");

        cleaned.Should().NotContain("<thought>");
        cleaned.Should().NotContain("</thought>");
        cleaned.Should().Contain("### Propuesta de Arquitectura");
    }

    [Fact]
    public void ExtractThought_WhenNoThoughtBlock_ReturnsNullThought()
    {
        var raw = "### Propuesta Estándar\nSin bloque de pensamiento.";
        var (thought, cleaned) = ProjectPlanningService.ExtractThought(raw);

        thought.Should().BeNull();
        cleaned.Should().Be(raw);
    }

    [Fact]
    public void CleanAssistantReply_FormatsHermesThoughtCardAndStripsSuggestedQuestions()
    {
        var raw = """
        <thought>
        Evaluación ISO/IEC 25010: La mantenibilidad se maximiza aislando el dominio.
        </thought>
        Hemos estructurado el proyecto según Clean Architecture.

        [PREGUNTAS_SUGERIDAS]
        - ¿Añadimos soporte para Redis Distributed Cache?
        - ¿Configuramos autenticación JWT?
        - ¿Definimos entidades de dominio?
        [/PREGUNTAS_SUGERIDAS]
        """;

        var result = ProjectPlanningService.CleanAssistantReply(raw);

        result.Should().Contain("<details class=\"hermes-thought-card\">");
        result.Should().Contain("Razonamiento de Hermes 3 (Scratchpad)");
        result.Should().Contain("Evaluación ISO/IEC 25010");
        result.Should().Contain("Hemos estructurado el proyecto según Clean Architecture.");
        result.Should().NotContain("PREGUNTAS_SUGERIDAS");
        result.Should().NotContain("¿Añadimos soporte para Redis");
    }

    [Fact]
    public void BuildSystemPrompt_ContainsHermes3AndQualityDirectives()
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = "OrderProcessingPlatform",
            Description = "Plataforma de alta concurrencia",
            ArchitecturalStyle = "Clean Architecture",
            FrontendStack = "Next.js",
            DatabaseType = "PostgreSQL"
        };

        var prompt = ProjectPlanningService.BuildSystemPrompt(answers);

        prompt.Should().Contain("Nous Hermes 3");
        prompt.Should().Contain("<thought>");
        prompt.Should().Contain("ISO/IEC 25010");
        prompt.Should().Contain("OrderProcessingPlatform");
        prompt.Should().Contain("CERO EMOJIS");
    }
}
