using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DevTools.Toolkit.Engine.Parsers;

public static class FrontmatterParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static (Dictionary<string, string> Frontmatter, string Body) Parse(string rawMarkdown)
    {
        if (string.IsNullOrWhiteSpace(rawMarkdown))
        {
            return (new Dictionary<string, string>(), string.Empty);
        }

        var trimmed = rawMarkdown.TrimStart();
        if (!trimmed.StartsWith("---"))
        {
            return (new Dictionary<string, string>(), rawMarkdown);
        }

        var secondIndex = trimmed.IndexOf("---", 3, StringComparison.Ordinal);
        if (secondIndex < 0)
        {
            return (new Dictionary<string, string>(), rawMarkdown);
        }

        var yamlText = trimmed.Substring(3, secondIndex - 3).Trim();
        var body = trimmed.Substring(secondIndex + 3).TrimStart('\r', '\n');

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(yamlText))
        {
            try
            {
                var dict = Deserializer.Deserialize<Dictionary<string, object>>(yamlText);
                if (dict is not null)
                {
                    foreach (var (key, value) in dict)
                    {
                        metadata[key] = value?.ToString() ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Fallback graceful parsing if complex yaml
            }
        }

        return (metadata, body);
    }
}
