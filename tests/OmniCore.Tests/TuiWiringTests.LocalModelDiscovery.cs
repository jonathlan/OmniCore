using OmniCore.Host;
using Terminal.Gui.Input;
using Terminal.Gui.Drivers;
using Terminal.Gui.Views;

namespace OmniCore.Tests;

public sealed partial class TuiWiringTests
{
    [Fact]
    public void Saving_a_local_engine_discovers_its_models_and_populates_F3_without_editing_models_yaml() => RunTuiTest(fx =>
    {
        var data = Path.Combine(fx.Root, "portable-local-discovery");
        var config = Path.Combine(data, "config");
        const string servedId = @"D:\Fixture Models\Portable Model.gguf";
        var requests = 0;
        var host = new TuiLocalEngineHost(config, discoveryHttp: () => new(new LocalEngineModelDiscoveryTests.Handler(request =>
        {
            Assert.Equal("http://127.0.0.1:23456/v1/models", request.RequestUri!.AbsoluteUri);
            Interlocked.Increment(ref requests);
            return LocalEngineModelDiscoveryTests.Catalog(servedId);
        })));
        host.Save(new("local", "attach", "http://127.0.0.1:12345"), host.Read(CancellationToken.None).Revision, CancellationToken.None);
        var policies = ModelPolicyHost.Create(data);
        fx.StartTui(policies: policies, localEngines: host, initialColumns: 110, initialRows: 35);
        KeyWithEffect(fx, KeyCode.F4, () => fx.App.Overlay is not null, "preferencias visibles");
        ChoosePreferenceCategory(fx, "local_engines");
        Assert.Equal(0, requests); // Opening the page is passive.
        ClickId(fx, "local-engine-edit");
        fx.Invoke(() => Descendants(fx.App.Overlay!).OfType<TextField>().Single(view => view.Id == "local-engine-endpoint").Text = "http://127.0.0.1:23456");
        ClickId(fx, "local-engine-save");
        fx.Wait(() => fx.App.LocalEngineDiscoveryTask?.IsCompleted == true, "guardar descubre el modelo automáticamente");
        fx.Wait(() => policies.Models.Any(model => model.Id == servedId), "registro recargado sin reiniciar");
        Assert.Equal(1, requests);
        Assert.Equal("Portable Model", policies.DisplayName(servedId));
        KeyWithEffect(fx, KeyCode.Esc, () => fx.App.Overlay is null, "preferencias cerradas");
        KeyWithEffect(fx, KeyCode.F3, () => fx.App.Overlay?.Id == "omni-model-palette", "selector F3 visible");
        fx.Wait(() => PickerItems(fx).Any(item => item.Contains("Portable Model", StringComparison.Ordinal)), "modelo detectado visible en F3");
        Assert.DoesNotContain("Inicia sesión", OverlayText(fx.App.Overlay!));
        Assert.Equal(1, requests); // Selecting a view never sends inference.
        var registered = new ConfigLoader().Load(File.ReadAllText(Path.Combine(config, "providers.yaml")), File.ReadAllText(Path.Combine(config, "models.yaml"))).Registry;
        Assert.Equal(98304, registered.Model(servedId)!.ContextWindow);
        Assert.Equal("http://127.0.0.1:23456/v1", registered.Provider("local")!.BaseUrl);
    });
}
