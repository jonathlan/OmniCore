namespace OmniCore.Host;

using System.Text.Json;
using System.Text.Json.Serialization;
using OmniCore.Abstractions;
using OmniCore.Domain;
using OmniCore.Models;

/// <summary>Reported controls for each exact Anthropic API-key route; never used for an OAuth route.</summary>
internal sealed record AnthropicCatalogReasoning(Dictionary<string, AnthropicReasoningSnapshot> Providers)
{
    internal const string FileName = "anthropic-catalog-reasoning.json";

    internal static AnthropicCatalogReasoning Read(string? json)
    {
        try
        {
            if (json is not null && JsonSerializer.Deserialize(json,
                AnthropicCatalogReasoningJson.Default.AnthropicCatalogReasoning) is { Providers: not null } data)
                return data;
        }
        catch (JsonException) { }
        return new(new Dictionary<string, AnthropicReasoningSnapshot>(StringComparer.Ordinal));
    }

    internal static AnthropicCatalogReasoning? ReadDirectory(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try { return File.Exists(path) ? Read(File.ReadAllText(path)) : null; }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { return null; }
    }

    internal string? Refresh(ProviderDescriptor provider, IReadOnlyList<AvailableAnthropicModel> catalog, bool truncated)
    {
        var snapshot = Providers.GetValueOrDefault(provider.Id);
        var levels = truncated && snapshot is { Levels: not null } && snapshot.BaseUrl == provider.BaseUrl
            ? new Dictionary<string, string[]>(snapshot.Levels, StringComparer.Ordinal) : new(StringComparer.Ordinal);
        foreach (var model in catalog)
        {
            var declared = SupportedLevels(model.ReasoningEffortLevels ?? (model.ThinkingSupported is true ? ["budget"] : []));
            if (declared.Length > 0) levels[model.Id] = declared;
            else levels.Remove(model.Id);
        }
        if (levels.Count == 0) Providers.Remove(provider.Id);
        else Providers[provider.Id] = new(provider.BaseUrl, levels);
        return Providers.Count == 0 ? null : JsonSerializer.Serialize(this,
            AnthropicCatalogReasoningJson.Default.AnthropicCatalogReasoning);
    }

    internal ReasoningCapability? ForModel(ProviderDescriptor? provider, string modelId)
    {
        if (provider is not { Family: ProviderFamily.AnthropicMessages, Auth.Kind: AuthKind.ApiKey }
            || !Providers.TryGetValue(provider.Id, out var snapshot) || snapshot?.BaseUrl != provider.BaseUrl
            || snapshot.Levels is null || !snapshot.Levels.TryGetValue(modelId, out var raw) || raw is null) return null;
        var levels = SupportedLevels(raw);
        return levels.Length == 0 ? null : new ReasoningCapability(true, levels);
    }

    private static string[] SupportedLevels(IEnumerable<string> levels) => levels
        .Where(level => level is "adaptive" or "budget" || AnthropicMessagesProvider.IsEffortLevel(level))
        .Distinct(StringComparer.Ordinal).ToArray();
}

internal sealed record AnthropicReasoningSnapshot(string BaseUrl, Dictionary<string, string[]> Levels);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AnthropicCatalogReasoning))]
internal sealed partial class AnthropicCatalogReasoningJson : JsonSerializerContext;
