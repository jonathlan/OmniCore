namespace OmniCore.Host;

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OmniCore.Abstractions;
using OmniCore.Domain;
using OmniCore.Models;
using YamlDotNet.RepresentationModel;

/// <summary>Metadata discovery requested by saving or checking a local engine. Never runs inference.</summary>
public interface ILocalEngineModelDiscoveryHost
{
    Task<LocalEngineModelDiscoveryResult> DiscoverModelsAsync(string providerId, string expectedRevision,
        CancellationToken cancellationToken);
}

public sealed record LocalEngineModelDiscoveryResult(bool Success, int ModelCount, string MessageKey,
    LocalEnginePreferencesSnapshot Snapshot);

/// <summary>The live supervisor supplies an already-owned managed endpoint without starting a process.</summary>
public interface ILocalEngineCatalogEndpointSource
{
    LocalServerEndpoint? CatalogEndpoint(string providerId, CancellationToken cancellationToken);
}

internal static class LocalModelIdentity
{
    // A model ID is opaque API data, including Windows paths. It is never a filesystem target.
    internal static bool Valid(string value) => value.Length is > 0 and <= 512
        && !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Any(char.IsControl);

    internal static string DisplayName(string id)
    {
        var name = id.Replace('\\', '/').Split('/')[^1];
        return name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? name[..^5] : name;
    }
}

public sealed partial class TuiLocalEngineHost
{
    private sealed record DiscoveredLocalModel(string Id, long Context, double? Parameters);

    public async Task<LocalEngineModelDiscoveryResult> DiscoverModelsAsync(string providerId, string expectedRevision,
        CancellationToken cancellationToken)
    {
        LocalEngineModelDiscoveryResult Result(bool success, int count, string key) =>
            new(success, count, key, Read(CancellationToken.None));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = ReadFiles();
            RequireRevision(original, expectedRevision);
            var loaded = Load(original.Providers, original.Models);
            var provider = loaded.Registry.Provider(providerId);
            if (provider?.LocalHost is null || provider.Family != ProviderFamily.OpenAiChatCompatible)
                return Result(false, 0, "discoveryUnsupported");
            LocalServerEndpoint? endpoint = provider.LocalHost.Mode == LocalHostMode.Managed
                ? (_supervisor as ILocalEngineCatalogEndpointSource)?.CatalogEndpoint(providerId, cancellationToken)
                : new(provider.BaseUrl, null, false);
            if (endpoint is null) return Result(false, 0, "discoveryNotReady");
            if (!SafeEndpoint(endpoint.BaseUrl, "attach")) return Result(false, 0, "discoveryInvalid");
            if (provider.Auth.Kind != AuthKind.None && endpoint.ApiKey is null)
                return Result(false, 0, "discoveryUnsupported");
            var baseUrl = endpoint.BaseUrl.TrimEnd('/');
            var uri = new Uri(baseUrl + (new Uri(baseUrl).AbsolutePath.Trim('/') is "" ? "/v1/models" : "/models"));
            using var http = _discoveryHttp?.Invoke() ?? CreateDiscoveryClient(endpoint.BaseUrl, provider.TrustedCertificatePath);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (endpoint.ApiKey is { Length: > 0 } key) request.Headers.Authorization = new("Bearer", key);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Result(false, 0, "discoveryOffline");
            const int maximumBytes = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maximumBytes) return Result(false, 0, "discoveryInvalid");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (bytes.Length + count > maximumBytes) return Result(false, 0, "discoveryInvalid");
                bytes.Write(buffer, 0, count);
            }
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var catalog = ParseLocalModels(document.RootElement);
            if (catalog.Count == 0) return Result(true, 0, "discoveryEmpty");
            cancellationToken.ThrowIfCancellationRequested();
            using var lease = ProviderConfigurationLease.Acquire(_directory);
            var current = ReadFiles();
            RequireRevision(current, expectedRevision); // A changed endpoint invalidates the entire response.
            var currentRegistry = Load(current.Providers, current.Models).Registry;
            if (catalog.Any(model => currentRegistry.Model(model.Id) is { } existing && existing.ProviderId != providerId))
                return Result(false, 0, "discoveryConflict");
            if (catalog.All(model => currentRegistry.Model(model.Id) is not null))
                return Result(true, catalog.Count, "discoverySuccess");
            var root = Parse(current.Models ?? "models: {}\n");
            var modelsKey = new YamlScalarNode("models");
            if (!root.Children.TryGetValue(modelsKey, out var node)) root.Add(modelsKey, node = new YamlMappingNode());
            if (node is not YamlMappingNode models) return Result(false, 0, "discoveryInvalid");
            foreach (var model in catalog)
            {
                if (currentRegistry.Model(model.Id) is not null) continue; // Never overwrite user limits or policies.
                var entry = new YamlMappingNode();
                Put(entry, "provider", providerId);
                entry.Add("context", new YamlScalarNode(model.Context.ToString(CultureInfo.InvariantCulture))
                    { Style = YamlDotNet.Core.ScalarStyle.Plain });
                if (model.Parameters is { } parameters) entry.Add("parametersBillions",
                    new YamlScalarNode(parameters.ToString("R", CultureInfo.InvariantCulture)) { Style = YamlDotNet.Core.ScalarStyle.Plain });
                models.Add(new YamlScalarNode(model.Id), entry);
            }
            var updated = Serialize(root);
            if (Encoding.UTF8.GetByteCount(updated) > 256 * 1024) return Result(false, 0, "discoveryInvalid");
            _ = Load(current.Providers, updated);
            var temporary = ModelsFile + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, updated, new UTF8Encoding(false));
                cancellationToken.ThrowIfCancellationRequested();
                RequireRevision(ReadFiles(), expectedRevision);
                _ = SidebarConfiguration.ReadFile(ModelsFile);
                File.Move(temporary, ModelsFile, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Result(true, catalog.Count, "discoverySuccess");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Result(false, 0, "discoveryOffline"); }
        catch (LocalEngineConfigurationException) { return Result(false, 0, "discoveryChanged"); }
        catch (HttpRequestException) { return Result(false, 0, "discoveryOffline"); }
        catch (Exception error) when (error is JsonException or InvalidDataException or InvalidOperationException
            or ArgumentException or IOException or UnauthorizedAccessException or ConfigValidationException)
        { return Result(false, 0, "discoveryInvalid"); }
    }

    private static HttpClient CreateDiscoveryClient(string endpoint, string? certificate)
    {
        var handler = OmniHost.CreateTlsHandler(endpoint, certificate);
        handler.AllowAutoRedirect = false;
        handler.UseCookies = false;
        return new(handler);
    }

    private static IReadOnlyList<DiscoveredLocalModel> ParseLocalModels(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 256) throw new InvalidDataException();
        var models = new List<DiscoveredLocalModel>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var value)
                || value.ValueKind != JsonValueKind.String || value.GetString() is not { } id || !LocalModelIdentity.Valid(id))
                throw new InvalidDataException();
            long context = 8192;
            if (item.TryGetProperty("max_model_len", out var maximum))
            {
                if (!maximum.TryGetInt64(out context) || context is < 1 or > int.MaxValue) throw new InvalidDataException();
            }
            else if (root.TryGetProperty("models", out var extra) && extra.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in extra.EnumerateArray())
                    if (model.ValueKind == JsonValueKind.Object && model.TryGetProperty("slug", out var slug)
                        && slug.ValueKind == JsonValueKind.String && slug.GetString() == id
                        && model.TryGetProperty("context_window", out var window))
                    { if (!window.TryGetInt64(out context) || context is < 1 or > int.MaxValue) throw new InvalidDataException(); break; }
            }
            double? parameters = null;
            if (item.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("n_params", out var count) && count.TryGetDouble(out var total)
                && double.IsFinite(total) && total > 0 && total <= 100_000_000_000_000d) parameters = total / 1_000_000_000d;
            if (models.Any(model => model.Id == id)) throw new InvalidDataException();
            models.Add(new(id, context, parameters));
        }
        return models;
    }
}
