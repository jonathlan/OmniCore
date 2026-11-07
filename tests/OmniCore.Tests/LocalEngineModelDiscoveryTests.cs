namespace OmniCore.Tests;

using System.Net;
using System.Text;
using OmniCore.Host;

public sealed class LocalEngineModelDiscoveryTests
{
    internal static HttpResponseMessage Catalog(string id, long context = 98304) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"data\":[{\"id\":" + System.Text.Json.JsonSerializer.Serialize(id)
            + ",\"max_model_len\":" + context + ",\"meta\":{\"n_ctx_train\":262144,\"n_params\":35000000000}}]}", Encoding.UTF8, "application/json")
    };

    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Theory]
    [InlineData(@"D:\Fixture Models\Portable Model.gguf", "http://127.0.0.1:12345", "/v1/models")]
    [InlineData("/fixture/models/portable.gguf", "http://127.0.0.1:12345/v1/", "/v1/models")]
    [InlineData("served-model-alias", "http://127.0.0.1:12345/v1", "/v1/models")]
    [InlineData("gateway-model", "http://127.0.0.1:12345/gateway/openai", "/gateway/openai/models")]
    public async Task Discovery_preserves_opaque_IDs_and_uses_active_context_without_inference(string id, string endpoint, string path)
    {
        using var directory = new TempDirectory(); var calls = 0;
        var host = new TuiLocalEngineHost(directory.Path, discoveryHttp: () => new(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("http://127.0.0.1:12345" + path, request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization); calls++;
            return Catalog(id);
        })));
        var saved = host.Save(new("engine", "attach", endpoint), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        Assert.Equal(0, calls);
        var result = await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.MessageKey); Assert.Equal(1, result.ModelCount); Assert.Equal(1, calls);
        var registry = new ConfigLoader().Load(File.ReadAllText(Path.Combine(directory.Path, "providers.yaml")),
            File.ReadAllText(Path.Combine(directory.Path, "models.yaml"))).Registry;
        var model = Assert.Single(registry.Models());
        Assert.Equal(id, model.Id); Assert.Equal("engine", model.ProviderId);
        Assert.Equal(98304, model.ContextWindow); Assert.Equal(35d, model.ParameterCountBillions);
        Assert.False(model.OutputLimitConfigured);
    }

    [Theory]
    [InlineData("{\"data\":[]}", "discoveryEmpty", true)]
    [InlineData("{\"error\":\"not an API\"}", "discoveryInvalid", false)]
    [InlineData("{\"data\":[{\"id\":\"bad\\u001bname\"}]}", "discoveryInvalid", false)]
    [InlineData("{\"data\":[{\"id\":\"same\"},{\"id\":\"same\"}]}", "discoveryInvalid", false)]
    public async Task Empty_or_invalid_metadata_does_not_create_or_clear_the_catalog(string json, string key, bool success)
    {
        using var directory = new TempDirectory();
        var host = new TuiLocalEngineHost(directory.Path, discoveryHttp: () => new(new Handler(_ =>
            new(HttpStatusCode.OK) { Content = new StringContent(json) })));
        var saved = host.Save(new("engine", "attach", "http://127.0.0.1:12345"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        var result = await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken);
        Assert.Equal(success, result.Success); Assert.Equal(key, result.MessageKey);
        Assert.False(File.Exists(Path.Combine(directory.Path, "models.yaml")));
    }

    [Fact]
    public async Task Changed_endpoint_during_discovery_rejects_the_old_response_without_overwriting_configuration()
    {
        using var directory = new TempDirectory();
        TuiLocalEngineHost? host = null;
        host = new(directory.Path, discoveryHttp: () => new(new Handler(_ =>
        {
            var current = host!.Read(CancellationToken.None);
            host.Save(new("engine", "attach", "http://127.0.0.1:23456"), current.Revision, CancellationToken.None);
            return Catalog("old-endpoint-model");
        })));
        var saved = host.Save(new("engine", "attach", "http://127.0.0.1:12345"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        var result = await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Equal("discoveryChanged", result.MessageKey);
        Assert.False(File.Exists(Path.Combine(directory.Path, "models.yaml")));
        Assert.Equal("http://127.0.0.1:23456", host.Read(CancellationToken.None).Engines.Single(row => row.ProviderId == "engine").BaseUrl);
    }

    [Fact]
    public async Task Repeated_discovery_preserves_user_limits_aliases_and_other_models()
    {
        using var directory = new TempDirectory();
        var host = new TuiLocalEngineHost(directory.Path, discoveryHttp: () => new(new Handler(_ => Catalog("existing", 98304))));
        var saved = host.Save(new("engine", "attach", "http://127.0.0.1:12345"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        const string original = "models:\n  existing: { provider: engine, context: 4096, maxOutput: 512, aliases: [mine] }\n  other: { provider: local, context: 8192 }\n";
        var path = Path.Combine(directory.Path, "models.yaml"); File.WriteAllText(path, original);
        var result = await host.DiscoverModelsAsync("engine", host.Read(CancellationToken.None).Revision, TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        var registry = new ConfigLoader().Load(File.ReadAllText(Path.Combine(directory.Path, "providers.yaml")), File.ReadAllText(path)).Registry;
        Assert.Equal(4096, registry.Model("existing")!.ContextWindow);
        Assert.Equal(512, registry.Model("existing")!.MaxOutputTokens);
        Assert.Equal(["mine"], registry.Model("existing")!.Aliases);
        Assert.NotNull(registry.Model("other"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public async Task An_unstarted_managed_engine_is_never_launched_implicitly_to_discover_models()
    {
        using var directory = new TempDirectory();
        var host = new TuiLocalEngineHost(directory.Path, discoveryHttp: () => throw new InvalidOperationException("must not connect"));
        var saved = host.Save(new("engine", "managed", "auto", "fixture-server.exe"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        var result = await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Equal("discoveryNotReady", result.MessageKey);
        Assert.False(File.Exists(Path.Combine(directory.Path, "models.yaml")));
    }

    [Fact]
    public async Task HTTP_failure_preserves_configuration_and_can_be_retried_without_readding_the_engine()
    {
        using var directory = new TempDirectory(); var calls = 0;
        var host = new TuiLocalEngineHost(directory.Path, discoveryHttp: () => new(new Handler(_ =>
            ++calls == 1 ? new(HttpStatusCode.ServiceUnavailable) : Catalog("recovered-model"))));
        var saved = host.Save(new("engine", "attach", "http://127.0.0.1:12345"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        Assert.False((await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken)).Success);
        Assert.True((await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken)).Success);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Existing_IDs_owned_by_another_provider_are_never_reassigned()
    {
        using var directory = new TempDirectory();
        var host = new TuiLocalEngineHost(directory.Path, discoveryHttp: () => new(new Handler(_ => Catalog("same-model"))));
        host.Save(new("engine", "attach", "http://127.0.0.1:12345"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        var path = Path.Combine(directory.Path, "models.yaml");
        const string models = "models:\n  same-model: { provider: local, context: 4096 }\n";
        File.WriteAllText(path, models);
        var result = await host.DiscoverModelsAsync("engine", host.Read(CancellationToken.None).Revision, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Equal("discoveryConflict", result.MessageKey);
        Assert.Equal(models, File.ReadAllText(path));
    }

    [Fact]
    public async Task Managed_discovery_uses_only_the_already_owned_endpoint_and_keeps_its_key_out_of_configuration()
    {
        using var directory = new TempDirectory(); var supervisor = new OwnedEndpoint();
        var host = new TuiLocalEngineHost(directory.Path, supervisor, () => new(new Handler(request =>
        {
            Assert.Equal("http://127.0.0.1:23456/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fixture-only-token", request.Headers.Authorization?.Parameter);
            return Catalog("managed-served-model");
        })));
        var saved = host.Save(new("engine", "managed", "auto", "fixture-server.exe"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        var result = await host.DiscoverModelsAsync("engine", saved.Revision, TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.MessageKey); Assert.Equal(0, supervisor.Starts);
        foreach (var file in Directory.GetFiles(directory.Path, "*.yaml")) Assert.DoesNotContain("fixture-only-token", File.ReadAllText(file));
    }

    private sealed class OwnedEndpoint : ITuiLocalEngineSupervisor, ILocalEngineCatalogEndpointSource
    {
        public int Starts;
        public LocalServerEndpoint? CatalogEndpoint(string providerId, CancellationToken cancellationToken) =>
            new("http://127.0.0.1:23456/v1", "fixture-only-token", true);
        public IReadOnlyList<LocalEngineRuntimeStatus> Read(CancellationToken cancellationToken) => [];
        public LocalEngineRuntimeActionResult EnsureReady(string providerId, CancellationToken cancellationToken)
        { Starts++; return new(true, "localEngines.runtime.managedRunning"); }
        public LocalEngineRuntimeActionResult Stop(string providerId, CancellationToken cancellationToken) => new(true, "localEngines.runtime.managedStopped");
    }
}
