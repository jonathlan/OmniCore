using System.Collections.ObjectModel;
using OmniCore.Host;
using OmniCore.Protocol;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace OmniCore.Cli;

public sealed partial class TuiApp
{
    private void ShowCompanionSettings()
    {
        if (_window is null || _turnHost?.CompanionHub is not { } hub) { ShowMessage("La mascota necesita una sesión de OmniCore."); return; }
        CloseCompanionHub(); CloseOverlay();
        var frame = OverlayFrame(Pref("companion_editor_title"), _window.Frame.Height - 3); frame.Id = "companion-settings";
        frame.Width = Dim.Percent(90); frame.Height = Dim.Fill(2);
        frame.ViewportSettings &= ~ViewportSettingsFlags.Transparent; frame.BorderStyle = LineStyle.Single;
        if (frame.SubViews.FirstOrDefault(v => v.Id == "omni-menu-title") is { } heading) { frame.Remove(heading); heading.Dispose(); }
        var panel = new View { Id = "companion-settings-fields", X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill(3),
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar };
        var models = new ListView { Id = "companion-settings-model", X = 2, Y = 2, Width = Dim.Fill(2), Height = 3, ShowMarks = false };
        var context = new Label { Id = "companion-settings-context", X = 2, Y = 5, Width = Dim.Fill(2), Height = 1, Text = "Cargando…" };
        var memory = new ListView { Id = "companion-settings-memory", X = 2, Y = 19, Width = Dim.Fill(2), Height = 3, ShowMarks = false, CanFocus = false };
#pragma warning disable CS0618
        var edit = new TextView { Id = "companion-settings-memory-text", X = 2, Y = 23, Width = Dim.Fill(2), Height = 4,
            BorderStyle = LineStyle.Single, WordWrap = true };
#pragma warning restore CS0618
        var project = new TextField { Id = "companion-settings-memory-project", X = 26, Y = 27, Width = Dim.Fill(2) };
        var projectTitle = new Label { X = 2, Y = 27, Width = 23, Text = Pref("companion_editor_project") };
        var usage = new Label { Id = "companion-settings-usage", X = 2, Y = 34, Width = Dim.Fill(2), Height = 2 };
        var status = new Label { Id = "companion-settings-status", X = 2, Y = 0, Width = Dim.Fill(2), Height = 1 };
        panel.Add(status, MenuIndent.Heading(Pref("companion_editor_model"), 1), models, context,
            MenuIndent.Heading(Pref("companion_editor_appearance"), 9), MenuIndent.Heading(Pref("companion_editor_memory"), 17), memory,
            new Label { X = 2, Y = 22, Width = Dim.Fill(2), Height = 1, Text = Pref("companion_editor_text") }, edit, projectTitle, project,
            MenuIndent.Heading(Pref("companion_editor_usage"), 33), usage);
        frame.Add(panel);
        Action syncMemoryActions = () => { };
        CompanionModelOption[] all = [], options = []; CompanionMemoryEntry[] entries = [];
        var localOnly = false; var attention = true; var category = 0;
        string? selectedId = null;
        CompanionMemoryEntry? SelectedFact() => memory.SelectedItem is { } i && i >= 0 && i < entries.Length ? entries[i] : null;
        void Report(Exception error) => _uiActions.Enqueue(() => { if (_overlay == frame) status.Text = error.Message; });
        void Persist(Action action) => _ = Task.Run(() => { try { action(); } catch (Exception error) { Report(error); } });
        CompanionRowButton Row(string id, string text, Pos x, Pos y, Action clicked)
        {
            var button = new CompanionRowButton { Id = id, Text = text, X = x, Y = y, Height = 1, NoDecorations = true, NoPadding = true, ShadowStyle = ShadowStyles.None };
            button.Accepted += (_, args) => { args.Handled = true; clicked(); };
            if (id == "companion-settings-close") frame.Add(button); else panel.Add(button);
            return button;
        }
        void UpdateContext()
        {
            var item = models.SelectedItem is { } i && i >= 0 && i < options.Length ? options[i] : null;
            context.Text = item is null ? "Elige el modelo de la mascota" : $"Contexto: {CompanionContext.Capacity(item.Local, item.ContextWindow):N0} tokens · {(item.Local ? "Local" : item.ContextWindow > 0 ? "Ventana del modelo remoto" : "Respaldo; capacidad sin declarar")}";
        }
        void ReloadMemory()
        {
            var requested = category;
            _ = Task.Run(() =>
            {
                try
                {
                    var facts = hub.MaintainMemory(CompanionCoordinator.MemoryCategories[requested]);
                    _uiActions.Enqueue(() => { if (_overlay != frame || category != requested) return; entries = facts;
                        memory.SetSource(new ObservableCollection<string>(facts.Select(f => (f.Status == "resolved" ? "✓ " : "") + f.Text.Replace('\n', ' '))));
                        memory.CanFocus = facts.Length > 0;
                        if (facts.Length > 0) memory.SelectedItem = 0;
                        edit.Text = facts.FirstOrDefault()?.Text ?? ""; project.Text = facts.FirstOrDefault()?.Project ?? ""; syncMemoryActions(); });
                }
                catch (Exception error) { Report(error); }
            });
        }
        CompanionRowButton? local = null, notices = null, animate = null, visible = null, restrict = null, reduced = null, visual = null, categories = null, modelSave = null;
        void FilterModels()
        {
            options = all.Where(m => !localOnly || m.Local).ToArray(); models.SetSource(new ObservableCollection<string>(options.Select(m => (_modelDisplayNames.Read(m.Id) ?? m.Id) + " · " + m.Provider)));
            if (options.Length > 0) models.SelectedItem = Math.Max(0, Array.FindIndex(options, m => m.Id == selectedId));
            if (local is not null) local.Text = "Solo local: " + (localOnly ? "Sí" : "No"); UpdateContext();
            if (local is not null) local.Enabled = true;
            if (modelSave is not null) modelSave.Enabled = options.Length > 0;
        }
        local = Row("companion-settings-local", "Solo local: No", 2, 6, () => { localOnly = !localOnly; FilterModels(); });
        local.Enabled = false;
        modelSave = Row("companion-settings-save", "Guardar modelo", 2, 7, () =>
        {
            selectedId = models.SelectedItem is { } i && i >= 0 && i < options.Length ? options[i].Id : null;
            var settings = new CompanionChatSettings(selectedId, localOnly);
            Persist(() => { hub.SaveChatSettings(settings, all); _uiActions.Enqueue(() => { if (_overlay == frame) status.Text = "Modelo: " + settings.ModelId; }); });
        });
        modelSave.Enabled = false;
        notices = Row("companion-settings-notices", "Avisos: Sí", 2, 10, () => { _companionQuiet = !_companionQuiet;
            notices!.Text = "Avisos: " + (_companionQuiet ? "No" : "Sí"); var value = _companionQuiet; Persist(() => hub.SetQuiet(value)); RenderCompanionBadge(); });
        animate = Row("companion-settings-attention", "Atención: Sí", 2, 11, () => { attention = !attention; animate!.Text = "Atención: " + (attention ? "Sí" : "No"); var value = attention;
            if (!value) _companionAttentionUntil = default; Persist(() => hub.SetAttention(value)); });
        visible = Row("companion-settings-visible", "Visible: " + (_pet?.Discovered == true && _pet.Preferences.Visible ? "Sí" : "No"), 2, 12, () =>
        { RunPetAction(_pet?.Discovered != true ? "discover" : _pet.Preferences.Visible ? "hide" : "show", false); visible!.Text = "Visible: " + (_pet?.Preferences.Visible == true ? "Sí" : "No"); });
        restrict = Row("companion-settings-restrict", "Restringir: " + (_pet?.Preferences.Restricted == true ? "Sí" : "No"), 2, 13, () =>
        { ToggleCompanionRestriction(); restrict!.Text = "Restringir: " + (_pet?.Preferences.Restricted == true ? "Sí" : "No"); });
        reduced = Row("companion-settings-motion", "Movimiento: " + (_pet?.Preferences.ReducedMotion == true ? "Reducido" : "Normal"), 2, 14, () =>
        { RunPetAction(_pet?.Preferences.ReducedMotion == true ? "motion" : "reduced", false); reduced!.Text = "Movimiento: " + (_pet?.Preferences.ReducedMotion == true ? "Reducido" : "Normal"); });
        visual = Row("companion-settings-visual", "Visual: " + (_pet?.Preferences.RenderMode ?? "auto"), 2, 15, () =>
        { string[] modes = ["auto", "graphics", "unicode", "text", "hidden"]; var mode = modes[(Array.IndexOf(modes, _pet?.Preferences.RenderMode ?? "auto") + 1) % modes.Length];
            RunPetAction("mode " + mode, false); visual!.Text = "Visual: " + mode; });
        string[] names = ["Personal", "Trabajo", "Agentes", "Seguimientos"];
        categories = Row("companion-settings-category", "Memoria: Personal", 2, 18, () => { category = (category + 1) % names.Length; categories!.Text = "Memoria: " + names[category]; ReloadMemory(); });
        memory.ValueChanged += (_, _) => { if (SelectedFact() is { } fact) { edit.Text = fact.Text; project.Text = fact.Project ?? ""; } syncMemoryActions(); };
        models.ValueChanged += (_, _) => UpdateContext();
        var addMemory = Row("companion-settings-add", "Añadir", 2, 28, () =>
        { var text = edit.Text?.ToString() ?? ""; var scope = project.Text?.ToString(); var selectedCategory = CompanionCoordinator.MemoryCategories[category];
            Persist(() => { hub.RememberCategorized(text, selectedCategory, string.IsNullOrWhiteSpace(scope) ? null : scope); ReloadMemory(); }); });
        var saveMemory = Row("companion-settings-edit", "Guardar recuerdo", 2, 29, () =>
        { var fact = SelectedFact(); if (fact is null) return; var text = edit.Text?.ToString() ?? ""; var scope = project.Text?.ToString();
            Persist(() => { hub.EditMemory(fact, text, string.IsNullOrWhiteSpace(scope) ? null : scope); ReloadMemory(); }); });
        var forget = Row("companion-settings-forget", "Olvidar", 2, 30, () => { var fact = SelectedFact(); if (fact is not null) Persist(() => { hub.Forget(fact.Id); ReloadMemory(); }); });
        var resolve = Row("companion-settings-resolve", "Resuelto", 2, 31, () => { var fact = SelectedFact(); if (fact is not null) Persist(() => { hub.ResolveMemory(fact.Id); ReloadMemory(); }); });
        var close = Row("companion-settings-close", Pref("close"), 2, Pos.AnchorEnd(1), CloseOverlay);
        syncMemoryActions = () => saveMemory.Enabled = forget.Enabled = resolve.Enabled = SelectedFact() is not null;
        syncMemoryActions();
        EnterNavigation.Focus(modelSave, models);
        EnterNavigation.Focus(edit, memory);
        EnterNavigation.Focus(project, edit);
        EnterNavigation.Focus(() => SelectedFact() is null ? addMemory : saveMemory, project);
        edit.KeyDown += (_, key) =>
        {
            if (key.Handled || key.KeyCode != (KeyCode.Enter | KeyCode.ShiftMask)) return;
            key.Handled = true;
            edit.InvokeCommand(Command.NewLine);
        };
        EnterNavigation.TraverseTabs(models, local, modelSave, notices, animate, visible, restrict, reduced, visual,
            categories, memory, edit, project, addMemory, saveMemory, forget, resolve, close);
        ConfigurePreferenceScroll(panel, 37);
        _overlay = frame; _window.Add(frame); ApplyTheme(frame, PreferencesSurface);
        CompactCompanionButtons(panel); CompactCompanionButtons(frame); models.SetFocus(); RenderCompanionBadge();
        _ = Task.Run(() =>
        {
            try
            {
                var settings = hub.ChatSettings(); var available = _turnHost.CompanionModels(); var quiet = hub.ReadQuiet(); var animation = hub.AttentionEnabled(); var totals = hub.ChatUsage();
                _uiActions.Enqueue(() => { if (_overlay != frame) return; all = available; localOnly = settings.LocalOnly; selectedId = settings.ModelId;
                    _companionQuiet = quiet; attention = animation; notices.Text = "Avisos: " + (quiet ? "No" : "Sí"); animate.Text = "Atención: " + (attention ? "Sí" : "No");
                    var incomplete = totals.Any(t => t.MeasuredTokenRequests < t.Requests) ? " · incompletos" : "";
                    var reported = totals.Sum(t => t.ReportedCostResponses) > 0 ? totals.Sum(t => t.ReportedUsd).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : "s/d";
                    var estimated = totals.Sum(t => t.PricedResponses) > 0 ? totals.Sum(t => t.EstimatedUsd).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : "s/d";
                    usage.Text = $"Consultas: {totals.Sum(t => t.Requests)} · Tokens: {totals.Sum(t => t.Input):N0} / {totals.Sum(t => t.Output):N0}{incomplete}\nUSD: {reported} reportados · {estimated} estimados";
                    status.Text = ""; FilterModels(); ReloadMemory(); });
            }
            catch (Exception error) { Report(error); }
        });
    }
}
