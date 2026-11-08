namespace OmniCore.Tests;

using OmniCore.Domain;
using OmniCore.Host;
using Terminal.Gui.Drivers;

public sealed partial class TuiWiringTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_model_choice_authorizes_only_the_exact_selected_route(bool differentEndpoint) => RunTuiTest(fx =>
    {
        var data = Path.Combine(fx.Root, "selected-route");
        var config = Path.Combine(data, "config");
        Directory.CreateDirectory(config);
        var endpoint = differentEndpoint ? "https://other.example/v1" : "https://paid.example/v1";
        File.WriteAllText(Path.Combine(config, "providers.yaml"),
            "providers:\n  paid-provider: {family: OpenAiChatCompatible, baseUrl: " + endpoint + ", billingMode: MeteredCurrency}\n");
        File.WriteAllText(Path.Combine(config, "models.yaml"),
            "models:\n  paid-model: {provider: paid-provider, context: 8192}\n");
        var policies = ModelPolicyHost.Create(data);
        var host = new RouteResumeTurnHost();
        fx.StartTui(policies: policies, turnHost: host);
        KeyWithEffect(fx, KeyCode.F3, () => fx.App.Overlay?.Id == "omni-model-palette", "open model picker");
        ChoosePickerItem(fx, "paid-model");
        ConfirmSelectedModelPreferences(fx, "paid-model");
        var inputCount = fx.Decoded<UserInputReceived>().Count();
        var (interaction, _) = PublishRouteConsent(fx, escalation: false);
        if (differentEndpoint)
        {
            fx.Wait(() => fx.App.Overlay is not null, "a different endpoint still requires confirmation");
            Assert.Equal(0, host.ResumeCount);
            Assert.DoesNotContain(fx.Decoded<InteractionResolved>(), item => item.InteractionId == interaction);
            return;
        }
        fx.Wait(() => host.ResumeCount == 1, "explicit model choice resumes without a second confirmation");
        fx.Wait(() => fx.App.Overlay is null, "no provider dialog remains");
        Assert.Equal("allow_route", Assert.Single(fx.Decoded<InteractionResolved>(), item => item.InteractionId == interaction).OptionId);
        Assert.Single(fx.Decoded<SessionRoutingPolicyRevised>(), item => item.InteractionId == interaction);
        Assert.Equal(inputCount, fx.Decoded<UserInputReceived>().Count());
        var (second, _) = PublishRouteConsent(fx, escalation: false);
        fx.Wait(() => fx.App.Overlay is not null, "selection consent is consumed only once");
        Assert.DoesNotContain(fx.Decoded<InteractionResolved>(), item => item.InteractionId == second);
        Assert.Equal(1, host.ResumeCount);
    });
}
