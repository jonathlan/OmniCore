using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace OmniCore.Cli;

public sealed partial class TuiApp
{
    private readonly TuiModelDisplayNames _modelDisplayNames;

    private string ModelHeadingLabel() => (_observedModel ?? _selectedModel) is { } id
        ? _modelDisplayNames.Read(id) ?? id : _localization.Resolve("shell.automatic_no_turn");

    private void EditModelDisplayName(string modelId, Action back)
    {
        if (_window is null) return;
        CloseOverlay();
        var frame = OverlayFrame(Pref("model_short_name_title"), 12);
        frame.Id = "model-display-name";
        var identity = new Label { X = 2, Y = 2, Width = Dim.Fill(4), Height = 2, Text = "ID: " + CatalogDisplay(modelId, 512) };
        var guide = new Label { X = 2, Y = 4, Width = Dim.Fill(4), Height = 2, Text = Pref("model_short_name_help") };
        var input = new TextField { Id = "model-display-name-input", X = 2, Y = 6, Width = Dim.Fill(4), Text = _modelDisplayNames.Read(modelId) ?? "" };
        var status = new Label { X = 2, Y = 8, Width = Dim.Fill(4), Height = 2 };
        var save = new Button { Id = "model-display-name-save", X = Pos.AnchorEnd(14), Y = Pos.AnchorEnd(2), Text = Pref("save") };
        var cancel = new Button { X = 2, Y = Pos.AnchorEnd(2), Text = Pref("cancel") };
        void Return() { CloseOverlay(); back(); }
        save.Accepted += (_, e) =>
        {
            e.Handled = true;
            if (!TuiModelDisplayNames.TryNormalize(input.Text.ToString(), out var name))
            { status.Text = Pref("model_short_name_invalid"); input.SetFocus(); return; }
            try { _modelDisplayNames.Save(modelId, name); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = _localization.Resolve("shell.could_not_save_unchanged"); return; }
            RenderState(); Return();
        };
        cancel.Accepted += (_, e) => { e.Handled = true; Return(); };
        frame.KeyDown += (_, key) => { if (!key.Handled && key.KeyCode == KeyCode.Esc) { key.Handled = true; Return(); } };
        EnterNavigation.Focus(save, input);
        frame.Add(identity, guide, input, status, save, cancel);
        ApplyTheme(frame); _overlay = frame; _window.Add(frame); input.SetFocus();
    }
}
