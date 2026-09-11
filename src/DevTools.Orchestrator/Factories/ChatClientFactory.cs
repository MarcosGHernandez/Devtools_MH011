using System.ClientModel;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using OpenAI;
using DevTools.Core.Configuration;

namespace DevTools.Orchestrator.Factories;

public sealed record OllamaStatus(
    bool IsOnline,
    string? ActiveModel,
    List<string> InstalledModels,
    string PreferredModel
);

public static class ChatClientFactory
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static string CurrentProvider { get; private set; } = "offline";
    public static string? CurrentResolvedModel { get; private set; }

    public static IChatClient? CreateClient(DevToolsConfig config, string? overrideProvider = null, string? overrideModel = null)
    {
        var providerName = overrideProvider ?? config.Ai.DefaultProvider;
        CurrentProvider = providerName;

        if (string.Equals(providerName, "offline", StringComparison.OrdinalIgnoreCase))
        {
            CurrentResolvedModel = "deterministic-heuristics";
            return null; // Signals orchestrator to use offline heuristic engine
        }

        // Check if provider is configured in dictionary
        if (!config.Ai.Providers.TryGetValue(providerName, out var settings))
        {
            if (string.Equals(providerName, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                settings = new AiProviderSettings
                {
                    Endpoint = "http://localhost:11434/v1",
                    ModelId = overrideModel ?? "qwen2.5-coder:7b"
                };
            }
            else if (string.Equals(providerName, "openai", StringComparison.OrdinalIgnoreCase))
            {
                settings = new AiProviderSettings
                {
                    ApiKeyEnvironmentVariable = "OPENAI_API_KEY",
                    ModelId = overrideModel ?? "gpt-4o"
                };
            }
            else
            {
                CurrentResolvedModel = null;
                return null;
            }
        }

        var modelToUse = overrideModel ?? settings.ModelId;

        // 1. Ollama / Local OpenAI-compatible endpoint
        if (!string.IsNullOrEmpty(settings.Endpoint))
        {
            var baseUrl = settings.Endpoint.TrimEnd('/');
            // If it's an Ollama endpoint, dynamically resolve best installed model
            if (baseUrl.Contains("11434") || string.Equals(providerName, "ollama", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedModel = ResolveBestOllamaModel(modelToUse, baseUrl);
                if (resolvedModel is not null)
                {
                    modelToUse = resolvedModel;
                }
            }

            CurrentResolvedModel = modelToUse;

            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(settings.Endpoint)
            };
            var credential = new ApiKeyCredential(settings.ApiKey ?? "ollama-dummy-key");
            var client = new OpenAIClient(credential, options);
            return client.GetChatClient(modelToUse).AsIChatClient();
        }

        // 2. Cloud OpenAI
        var apiKey = settings.ApiKey;
        if (string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(settings.ApiKeyEnvironmentVariable))
        {
            apiKey = Environment.GetEnvironmentVariable(settings.ApiKeyEnvironmentVariable);
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            CurrentResolvedModel = modelToUse;
            var client = new OpenAIClient(apiKey);
            return client.GetChatClient(modelToUse).AsIChatClient();
        }

        CurrentResolvedModel = null;
        return null;
    }

    public static async Task<OllamaStatus> GetOllamaStatusAsync(string? endpoint = null, string preferredModel = "qwen2.5-coder:7b")
    {
        var baseUri = (endpoint ?? "http://localhost:11434").TrimEnd('/');
        if (baseUri.EndsWith("/v1")) baseUri = baseUri[..^3];

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>($"{baseUri}/api/tags", cts.Token);
            var models = resp?.Models?.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).ToList() ?? new List<string>();

            var resolved = PickBestModel(preferredModel, models);
            return new OllamaStatus(true, resolved, models, preferredModel);
        }
        catch
        {
            return new OllamaStatus(false, null, new List<string>(), preferredModel);
        }
    }

    private static string? ResolveBestOllamaModel(string preferredModel, string endpoint)
    {
        try
        {
            var baseUri = endpoint.TrimEnd('/');
            if (baseUri.EndsWith("/v1")) baseUri = baseUri[..^3];

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var task = _httpClient.GetFromJsonAsync<OllamaTagsResponse>($"{baseUri}/api/tags", cts.Token);
            task.Wait(TimeSpan.FromSeconds(2));

            if (task.IsCompletedSuccessfully && task.Result?.Models is not null)
            {
                var models = task.Result.Models.Select(m => m.Name).ToList();
                return PickBestModel(preferredModel, models);
            }
        }
        catch
        {
            // Silently ignore during sync resolution; will fallback
        }

        return null;
    }

    private static string? PickBestModel(string preferredModel, List<string> installedModels)
    {
        if (installedModels.Count == 0) return null;

        // 1. Exact match for preferred model
        var exact = installedModels.FirstOrDefault(m => string.Equals(m, preferredModel, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // 2. Any qwen2.5-coder
        var qwenCoder = installedModels.FirstOrDefault(m => m.Contains("qwen2.5-coder", StringComparison.OrdinalIgnoreCase));
        if (qwenCoder is not null) return qwenCoder;

        // 3. Any qwen
        var qwen = installedModels.FirstOrDefault(m => m.Contains("qwen", StringComparison.OrdinalIgnoreCase));
        if (qwen is not null) return qwen;

        // 4. llama3.1
        var llama31 = installedModels.FirstOrDefault(m => m.Contains("llama3.1", StringComparison.OrdinalIgnoreCase));
        if (llama31 is not null) return llama31;

        // 5. llama3.2
        var llama32 = installedModels.FirstOrDefault(m => m.Contains("llama3.2", StringComparison.OrdinalIgnoreCase));
        if (llama32 is not null) return llama32;

        // 6. mistral
        var mistral = installedModels.FirstOrDefault(m => m.Contains("mistral", StringComparison.OrdinalIgnoreCase));
        if (mistral is not null) return mistral;

        // 7. sentinel-chat
        var sentinel = installedModels.FirstOrDefault(m => m.Contains("sentinel-chat", StringComparison.OrdinalIgnoreCase));
        if (sentinel is not null) return sentinel;

        // 8. Fallback to first non-embed model
        var nonEmbed = installedModels.FirstOrDefault(m => !m.Contains("embed", StringComparison.OrdinalIgnoreCase));
        return nonEmbed ?? installedModels.FirstOrDefault();
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelItem>? Models { get; set; }
    }

    private sealed class OllamaModelItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
