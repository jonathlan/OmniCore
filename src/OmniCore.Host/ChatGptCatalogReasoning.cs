namespace OmniCore.Host;

using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCore.Domain;
using OmniCore.Models;

/// <summary>Replaceable account metadata, separate from the user's YAML and readable across a package rollback.</summary>
internal sealed record ChatGptCatalogReasoning(string ProviderId, Dictionary<string, string[]> Levels)
{
    internal const string FileName = "chatgpt-catalog-reasoning.json";
    internal const string BaseUrl = "https://chatgpt.com/backend-api";

    internal static ChatGptCatalogReasoning? Read(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ChatGptCatalogReasoningJson.Default.ChatGptCatalogReasoning)
                : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static string? Serialize(string providerId, IReadOnlyList<AvailableChatGptModel> catalog)
    {
        var levels = catalog.Where(model => model.ReasoningEffortLevels is not null)
            .ToDictionary(model => model.Id, model => SupportedLevels(model.ReasoningEffortLevels!), StringComparer.Ordinal);
        return levels.Count == 0 ? null : JsonSerializer.Serialize(new ChatGptCatalogReasoning(providerId, levels),
            ChatGptCatalogReasoningJson.Default.ChatGptCatalogReasoning);
    }

    internal ReasoningCapability? ForModel(ProviderDescriptor? provider, string modelId)
    {
        if (provider is not { Family: ProviderFamily.OpenAIResponses, Profile: "codex" }
            || provider.Id != ProviderId || provider.BaseUrl.TrimEnd('/') != BaseUrl
            || Levels is null || !Levels.TryGetValue(modelId, out var advertised) || advertised is null) return null;
        var levels = SupportedLevels(advertised);
        return levels.Length == 0 ? null : new ReasoningCapability(true, levels);
    }

    private static string[] SupportedLevels(IEnumerable<string> levels) => levels
        .Where(effort => OpenAIResponsesProvider.CanSendReasoningEffort(effort, ResponsesProfile.Codex))
        .Distinct(StringComparer.Ordinal).ToArray();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChatGptCatalogReasoning))]
internal sealed partial class ChatGptCatalogReasoningJson : JsonSerializerContext;
