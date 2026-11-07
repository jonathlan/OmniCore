#pragma warning disable CS0618 // Exercise the pinned multiline editors with physical Enter.
using OmniCore.Cli;
using OmniCore.Client;
using OmniCore.Host;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace OmniCore.Tests;

public sealed partial class TuiWiringTests
{
    // Never retry Enter: a retry could conceal double activation or commit a draft.
    private static void EnterOnce(TuiFixture fx, Func<bool> effect, string message)
    {
        fx.InjectKey(new Key(KeyCode.Enter));
        fx.Wait(effect, message);
    }

    private static View EnterControl(TuiFixture fx, string id) =>
        Descendants(fx.App.Overlay!).Single(view => view.Id == id);

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("it")]
    public void Enter_on_mode_choices_focuses_apply_without_committing_and_next_Enter_applies(string locale) => RunTuiTest(fx =>
    {
        fx.StartTui(localeSelection: new LocaleSelection(locale, LocaleSelectionSource.CommandLine));
        fx.Invoke(fx.App.ShowModePreferences);
        var before = fx.Server.Query("modeAuthority", default)?.Json;
        var choices = (ListView)EnterControl(fx, "mode-options");
        fx.Invoke(() => { choices.SelectedItem = 0; choices.SetFocus(); });
        EnterOnce(fx, () => EnterControl(fx, "mode-apply").HasFocus, "Enter lleva a Aplicar");
        Assert.Equal(before, fx.Server.Query("modeAuthority", default)?.Json);
        EnterOnce(fx, () => fx.App.Overlay?.SubViews.Any(view => view.Id == "message-close") == true, "otro Enter aplica la opción y muestra el aviso de éxito");
        Assert.Contains("plan", fx.Server.Query("modeAuthority", default)!.Json, StringComparison.OrdinalIgnoreCase);
        EnterOnce(fx, () => fx.App.Overlay is null, "Enter cierra el aviso de éxito");
    });

    [Theory]
    [InlineData("reasoning-options")]
    [InlineData("reasoning-budget")]
    public void Enter_on_effort_fields_focuses_apply_and_preserves_the_draft(string id) => RunTuiTest(fx =>
    {
        var data = Path.Combine(fx.Root, "enter-reasoning");
        var config = Path.Combine(data, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "providers.yaml"), "providers:\n  fixture: {family: AnthropicMessages, baseUrl: https://fixture.invalid, billingMode: Local}\n");
        File.WriteAllText(Path.Combine(config, "models.yaml"), "models:\n  declared:\n    provider: fixture\n    context: 8192\n    maxOutput: 4096\n    reasoning: {supported: true, effortLevels: [budget]}\n");
        var policies = ModelPolicyHost.Create(data);
        policies.Select(ModelPolicyHost.WorkspaceSelectionId(Environment.CurrentDirectory), new("fixture", "declared"), "declared", true, default);
        fx.StartTui(policies: policies);
        fx.Invoke(() => fx.App.ShowReasoningPreferences());
        var before = fx.Server.Query("reasoningPreference", default)?.Json;
        if (id == "reasoning-budget")
            fx.Invoke(() => { ((ListView)EnterControl(fx, "reasoning-options")).SelectedItem = 2; EnterControl(fx, id).Text = "2048"; });
        fx.Invoke(() => EnterControl(fx, id).SetFocus());
        EnterOnce(fx, () => EnterControl(fx, "reasoning-apply").HasFocus, "Enter lleva a Aplicar esfuerzo");
        Assert.Equal(before, fx.Server.Query("reasoningPreference", default)?.Json);
        EnterOnce(fx, () => fx.App.Overlay is null, "Aplicar conserva su acción nativa");
    });

    [Fact]
    public void Enter_on_security_choices_requires_apply_then_a_separate_confirmation() => RunTuiTest(fx =>
    {
        var host = SecurityPreferencesHost.Create(fx.Workspace, Path.Combine(fx.Root, "enter-security-data"), Path.Combine(fx.Root, "enter-security-config"));
        fx.StartTui(securityPreferences: host);
        var before = fx.Server.Query("sessionSecurityDefault", default)?.Json;
        fx.Invoke(fx.App.ShowExecutionPermissions);
        ClickId(fx, "session-security-default-change");
        fx.Invoke(() => { var list = (ListView)EnterControl(fx, "security-ceiling-options"); list.SelectedItem = 3; list.SetFocus(); });
        EnterOnce(fx, () => EnterControl(fx, "security-ceiling-apply").HasFocus, "Enter sólo enfoca Aplicar permisos");
        Assert.Equal(before, fx.Server.Query("sessionSecurityDefault", default)?.Json);
        EnterOnce(fx, () => fx.App.Overlay?.Id == "security-preferences-confirm", "Aplicar abre confirmación");
        Assert.Equal(before, fx.Server.Query("sessionSecurityDefault", default)?.Json);
        // The safe button retains focus; Enter cancels instead of raising permissions.
        EnterOnce(fx, () => fx.App.Overlay?.Id == "security-ceiling", "Enter predeterminado cancela confirmación");
        Assert.Equal(before, fx.Server.Query("sessionSecurityDefault", default)?.Json);
    });

    [Theory]
    [InlineData("policy-session-cap", "policy-preferences-save")]
    [InlineData("policy-route-meta", "policy-preferences-save-routes")]
    public void Enter_chooses_the_save_button_for_the_edited_policy_section(string field, string action) => RunTuiTest(fx =>
    {
        var host = PolicyPreferencesHost.Create(fx.Workspace, Path.Combine(fx.Root, "enter-policy-data"), Path.Combine(fx.Root, "enter-policy-config"));
        fx.StartTui(policyPreferences: host);
        fx.Invoke(() => fx.App.ShowPolicyPreferences("models_execution"));
        var before = host.Read().Revision;
        fx.Invoke(() => EnterControl(fx, field).SetFocus());
        EnterOnce(fx, () => EnterControl(fx, action).HasFocus, "Enter enfoca el guardado de su sección");
        Assert.Equal(before, host.Read().Revision);
    });

    [Fact]
    public void Enter_in_the_model_editor_focuses_save_and_does_not_replace_multiline_newlines() => RunTuiTest(fx =>
    {
        var host = new ModelCatalogFixture();
        fx.StartTui(modelCatalogPreferences: host);
        fx.Invoke(fx.App.ShowModelCatalogPreferences);
        fx.Invoke(() => EnterControl(fx, "model-catalog-list").SetFocus());
        EnterOnce(fx, () => fx.App.Overlay?.Id == "model-catalog-editor", "Enter de la lista abre el editor");
        var field = Descendants(fx.App.Overlay!).OfType<TextField>().First();
        fx.Invoke(() => field.SetFocus());
        EnterOnce(fx, () => EnterControl(fx, "model-catalog-save").HasFocus, "Enter del campo lleva a Guardar");
        Assert.Equal(0, host.Saves);
        var multiline = (TextView)EnterControl(fx, "model-catalog-aliases");
        fx.Invoke(() => { multiline.Text = "one"; multiline.SetFocus(); });
        EnterOnce(fx, () => multiline.Text.ToString()!.Contains('\n'), "Enter mantiene el salto de línea");
        Assert.True(multiline.HasFocus);
        Assert.Equal(0, host.Saves);
        fx.Invoke(() => EnterControl(fx, "model-catalog-save").SetFocus());
        EnterOnce(fx, () => host.Saves == 1, "Enter del botón guarda exactamente una vez");
    });

    [Fact]
    public void Enter_checkboxes_toggle_once_respect_native_cancellation_and_never_submit() => RunTuiTest(fx =>
    {
        fx.StartTui();
        fx.Invoke(fx.App.ShowModePreferences);
        CheckBox check = null!;
        var changes = 0;
        var accepts = 0;
        fx.Invoke(() =>
        {
            check = new CheckBox { X = 2, Y = 17, Text = "fixture", Value = CheckState.UnChecked };
            check.ValueChanged += (_, _) => changes++;
            check.Accepted += (_, _) => accepts++;
            fx.App.Overlay!.Add(check);
            EnterNavigation.Attach(check);
            EnterNavigation.Attach(check); // Reapplying a theme must not add another toggle.
            check.SetFocus();
        });
        EnterOnce(fx, () => changes == 1, "Enter activa una sola vez la casilla");
        Assert.Equal(CheckState.Checked, check.Value);
        Assert.Equal(0, accepts);
        Assert.Equal("mode-preferences", fx.App.Overlay!.Id);
        fx.Invoke(() => check.ValueChanging += (_, args) => args.Handled = true);
        fx.InjectKey(new Key(KeyCode.Enter));
        fx.Invoke(() => { Assert.Equal(1, changes); Assert.Equal(CheckState.Checked, check.Value); });
    });

    [Fact]
    public void Enter_routes_do_not_override_handled_keys_or_focus_unavailable_targets() => RunTuiTest(fx =>
    {
        fx.StartTui();
        fx.Invoke(fx.App.ShowModePreferences);
        var list = (ListView)EnterControl(fx, "mode-options");
        var apply = EnterControl(fx, "mode-apply");
        fx.Invoke(() => { apply.Enabled = false; list.SetFocus(); });
        fx.InjectKey(new Key(KeyCode.Enter));
        fx.Invoke(() => Assert.True(list.HasFocus));
        fx.Invoke(() => { apply.Enabled = true; list.KeyDown += (_, key) => { if (key.KeyCode == KeyCode.Enter) key.Handled = true; }; });
        // A separate control has a pre-existing handler before registering the route.
        TextField field = null!;
        fx.Invoke(() =>
        {
            field = new TextField { X = 2, Y = 17, Width = 20 };
            field.KeyDown += (_, key) => key.Handled = true;
            fx.App.Overlay!.Add(field);
            EnterNavigation.Focus(apply, field);
            field.SetFocus();
        });
        fx.InjectKey(new Key(KeyCode.Enter));
        fx.Invoke(() => Assert.True(field.HasFocus));
    });

    [Fact]
    public void Enter_in_local_engine_multiline_arguments_keeps_newlines_and_in_a_field_focuses_save() => RunTuiTest(fx =>
    {
        var host = new TuiLocalEngineHost(Path.Combine(fx.Root, "enter-local-config"));
        fx.StartTui(localEngines: host);
        KeyWithEffect(fx, KeyCode.F4, () => fx.App.Overlay is not null, "preferencias visibles");
        ChoosePreferenceCategory(fx, "local_engines");
        ClickId(fx, "local-engine-add");
        var before = host.Read(default).Revision;
        var arguments = (TextView)EnterControl(fx, "local-engine-arguments");
        fx.Invoke(() => { arguments.Text = "--fixture"; arguments.SetFocus(); });
        EnterOnce(fx, () => arguments.Text.ToString()!.Contains('\n'), "Enter añade una línea a los argumentos");
        Assert.True(arguments.HasFocus);
        fx.Invoke(() => EnterControl(fx, "local-engine-endpoint").SetFocus());
        EnterOnce(fx, () => EnterControl(fx, "local-engine-save").HasFocus, "Enter del campo lleva a Guardar motor");
        Assert.Equal(before, host.Read(default).Revision);
    });

    [Fact]
    public void Enter_edits_the_selected_security_rule_then_focuses_apply_without_saving() => RunTuiTest(fx =>
    {
        var host = SecurityPreferencesHost.Create(fx.Workspace, Path.Combine(fx.Root, "enter-rules-data"), Path.Combine(fx.Root, "enter-rules-config"));
        fx.StartTui(securityPreferences: host);
        fx.Invoke(fx.App.ShowExecutionPermissions);
        ClickId(fx, "security-rules-open");
        var before = host.Read().Revision;
        fx.Invoke(() => EnterControl(fx, "security-tool-id").SetFocus());
        EnterOnce(fx, () => EnterControl(fx, "security-decision").HasFocus, "Enter lleva a la decisión de la regla");
        EnterOnce(fx, () => EnterControl(fx, "security-save").HasFocus, "Enter lleva a Aplicar regla");
        Assert.Equal(before, host.Read().Revision);
    });

    [Fact]
    public void Enter_in_diagnostics_opens_readable_details_then_focuses_back() => RunTuiTest(fx =>
    {
        fx.StartTui();
        fx.Invoke(() => fx.App.ShowDiagnosticsPreferences("diagnostics_data"));
        fx.Invoke(() => EnterControl(fx, "diagnostics-configuration").SetFocus());
        EnterOnce(fx, () => EnterControl(fx, "diagnostics-details").HasFocus, "Enter abre el detalle de diagnóstico");
        EnterOnce(fx, () => EnterControl(fx, "diagnostics-back").HasFocus, "Enter del detalle lleva a Volver");
    });

    [Fact]
    public void Enter_in_command_search_accepts_the_selected_row_and_details_lead_to_prepare() => RunTuiTest(fx =>
    {
        fx.StartTui();
        KeyWithEffect(fx, KeyCode.F4, () => fx.App.Overlay is not null, "preferencias visibles");
        ChoosePreferenceCategory(fx, "extensions_skills");
        ClickId(fx, "preferences-commands-open");
        fx.Invoke(() => EnterControl(fx, "commands-filter").SetFocus());
        EnterOnce(fx, () => EnterControl(fx, "commands-detail").HasFocus, "Enter de búsqueda abre detalle");
        EnterOnce(fx, () => EnterControl(fx, "commands-prepare").HasFocus, "Enter del detalle lleva a Preparar");
        Assert.Equal("", fx.App.Composer!.Text.ToString());
    });
}
#pragma warning restore CS0618
