using System.Collections.ObjectModel;
using OmniCore.Client;
using OmniCore.Protocol;
using OmniCore.Host;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace OmniCore.Cli;

public sealed partial class TuiApp
{
    private readonly Dictionary<string, (string Text, TuiAttachmentReference[] Attachments)> _conversationDrafts = new();
    private bool _conversationSelectionPending;
    private bool _showOpeningSelector;
    internal bool ConversationSelectionPending => _conversationSelectionPending;

    private bool HandleConversationSlash(string input)
    {
        if (!CommandLineParser.TryParse(input, out var parsed) || parsed is null
            || parsed.Name is not ("new" or "sessions" or "resume-session" or "rename-session" or "clear" or "compact")) return false;
        _composer!.Text = ""; HideCommandHelper();
        if (HasActiveModelWork) { ShowMessage(_localization.Resolve("shell.conversation.busy")); return true; }
        if (parsed.Name == "clear" && parsed.Arguments.Count == 0)
        {
            _state = new(_state.Header, ConversationModel.Empty(), _state.Sidebar, _state.Composer,
                _state.StatusLine, _state.Overlays, _state.Connection);
            _renderedConversation = null; RenderState(); return true;
        }
        if (parsed.Name == "new" && parsed.Arguments.Count == 0) { ChangeConversation("conversation.new"); return true; }
        if (parsed.Name is "sessions" or "resume-session" && parsed.Arguments.Count == 0)
        { ShowConversations(); return true; }
        if (parsed.Name == "resume-session" && parsed.Arguments.Count == 1)
        { ChangeConversation("conversation.resume", parsed.Arguments[0]); return true; }
        if (parsed.Name == "compact" && parsed.Arguments.Count == 0)
        {
            var ack = ConversationCommand("conversation.compact");
            if (ack.Status != "ok") ShowMessage(_localization.ResolveWire(ack.Error ?? "conversation.failed"));
            else { _turnStatus = _localization.Resolve(ack.Outcome?.Kind == RuntimeCommandOutcomeKind.NoOp
                ? "shell.conversation.nothing_to_compact" : "shell.conversation.compacted"); PollEvents(); }
            return true;
        }
        if (parsed.Name == "rename-session" && parsed.Arguments.Count > 0)
        {
            RenameCurrentConversation(string.Join(' ', parsed.Arguments));
            return true;
        }
        ShowMessage(_localization.Resolve("shell.conversation.arguments")); return true;
    }

    private void RenameCurrentConversation(string title)
    {
        title = title.Trim();
        if (title.Length is 0 or > 120 || title.Any(char.IsControl))
        { ShowMessage(_localization.Resolve("shell.conversation.arguments")); return; }
        try
        {
            var row = ConversationJson.Decode(_client.Query("conversations", CancellationToken.None)?.Json ?? "{}")
                ?.Conversations.FirstOrDefault(item => item.Current);
            if (row is null) { ShowMessage(_localization.Resolve("conversation.failed")); return; }
            if (_client is not ITrustedUserActionClient trusted)
            { ShowMessage(_localization.Resolve("conversation.trusted_required")); return; }
            var payload = "{" + JsonObj.Field("cmd", "conversation.rename") + ","
                + JsonObj.Field("sessionId", row.Id) + "," + JsonObj.Field("title", title) + ","
                + JsonObj.FieldRaw("expectedSequence", row.JournalSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "}";
            var ack = trusted.SendUserAction(WireEnvelope.Command(Ids.NewV7(), payload), CancellationToken.None);
            if (ack.Status != "ok") { ShowMessage(_localization.ResolveWire(ack.Error ?? "conversation.failed")); return; }
            _turnStatus = _localization.Resolve("shell.conversation.renamed");
            _state = RefreshState(); RenderState();
        }
        catch (System.Text.Json.JsonException)
        { ShowMessage(_localization.Resolve("conversation.failed")); }
    }

    private CommandAck ConversationCommand(string name, string? session = null)
    {
        if (_client is not ITrustedUserActionClient trusted)
            return new(Ids.NewV7(), "error", "conversation.trusted_required", RuntimeCommandOutcome.Rejected());
        return trusted.SendUserAction(WireEnvelope.Command(Ids.NewV7(), "{" + JsonObj.Field("cmd", name)
            + (session is null ? "" : "," + JsonObj.Field("sessionId", session)) + "}"), CancellationToken.None);
    }

    private bool ChangeConversation(string command, string? session = null, Label? errorLabel = null)
    {
        if (HasActiveModelWork) { ShowMessage(_localization.Resolve("shell.conversation.busy")); return false; }
        // Explicitly continuing the recovered selection is a presentation choice. It must
        // also work when recovery preserved an unfinished Turn, without mutating it.
        var current = ConversationJson.Decode(_client.Query("conversations", CancellationToken.None)?.Json ?? "{}")
            ?.Conversations.FirstOrDefault(row => row.Current)?.Id;
        var continuingRecovered = _conversationSelectionPending && command == "conversation.resume" && session == current;
        var ack = continuingRecovered ? new CommandAck(Ids.NewV7(), "ok", null) : ConversationCommand(command, session);
        if (ack.Status != "ok")
        {
            var message = _localization.ResolveWire(ack.Error ?? "conversation.failed");
            if (errorLabel is not null) errorLabel.Text = message; else ShowMessage(message);
            return false;
        }
        var openingDraft = _conversationSelectionPending ? _composer?.Text ?? "" : "";
        if (!_conversationSelectionPending)
            _conversationDrafts[_cursorSession ?? ""] = (_composer?.Text ?? "", _pendingAttachments.ToArray());
        _conversationSelectionPending = false;
        _showOpeningSelector = false;
        _lastSequence = 0; _observedModel = null; _turnUsage = null; _turnStatus = null;
        _activeInteraction = null; _pendingQuotaResume = null; _pendingEscalationResume = null;
        _state = RefreshState();
        _pendingAttachments.Clear();
        if (_conversationDrafts.TryGetValue(_cursorSession ?? "", out var draft))
        { _composer!.Text = draft.Text; _pendingAttachments.AddRange(draft.Attachments); }
        else _composer!.Text = openingDraft;
        _renderedConversation = null; _followConversationEnd = true; RenderAttachmentChips(); RenderState();
        return true;
    }

    private void ShowConversations()
    {
        if (_window is null || _overlay is not null) return;
        if (HasActiveModelWork) { ShowMessage(_localization.Resolve("shell.conversation.busy")); return; }
        ConversationRow[] rows;
        try { rows = ConversationJson.Decode(_client.Query("conversations", CancellationToken.None)?.Json ?? "{}")?.Conversations.ToArray() ?? []; }
        catch (System.Text.Json.JsonException) { ShowMessage(_localization.Resolve("conversation.failed")); return; }
        var frame = OverlayFrame(_localization.Resolve(_conversationSelectionPending
            ? "shell.conversation.open_title" : "shell.conversation.title"), 25);
        frame.Id = "conversation-selector";
        frame.Width = Dim.Func(_ => Math.Min(100, Math.Max(1, _window.Viewport.Width - 6)));
        frame.Height = Dim.Func(_ => Math.Min(27, Math.Max(1, _window.Viewport.Height - 4)));
        frame.BorderStyle = LineStyle.Single;
        var heading = frame.SubViews.Single(view => view.Id == "omni-menu-title");
        frame.Remove(heading); heading.Dispose(); // The visible border already carries the title.
        var actionRows = _conversationSelectionPending ? 4 : 3;
        var footerRows = actionRows + 3;
        var intro = new MenuInformationLabel { Id = "conversation-intro", X = 2, Y = 1, Width = Dim.Fill(2),
            Height = Dim.Func(_ => Math.Clamp(frame.Viewport.Height - actionRows - 8, 0, 3)),
            Text = _localization.Resolve("shell.conversation.open_help"), CanFocus = false };
        var searchLabel = new Label { Id = "conversation-search-label", X = 2, Y = Pos.Bottom(intro), Text = Pref("search"), CanFocus = false };
        var search = new TextField { Id = "conversation-search", X = 2, Y = Pos.Bottom(searchLabel), Width = Dim.Fill(2), Height = 1 };
        var list = new ListView { Id = "conversation-list", X = 2, Y = Pos.Bottom(search) + 1, Width = Dim.Fill(2), Height = Dim.Fill(footerRows), ShowMarks = false,
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar };
        var notice = new MenuInformationLabel { Id = "conversation-notice", X = 2, Y = Pos.AnchorEnd(footerRows), Width = Dim.Fill(2), Height = 2, CanFocus = false };
        static Button ActionButton(string id, string text, int rowFromEnd)
        {
            var button = new CompactRowButton { Id = id, Text = text, X = 2, Y = Pos.AnchorEnd(rowFromEnd),
                Height = 1, NoDecorations = true, NoPadding = true, ShadowStyle = ShadowStyles.None };
            button.Margin.Thickness = new(0);
            button.Border.Thickness = new(0);
            button.Padding.Thickness = new(0);
            return button;
        }
        var filtered = rows;
        void Update()
        {
            filtered = rows.Where(row => row.Title.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)
                || row.Id.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            list.SetSource(new ObservableCollection<string>(filtered.Select(row => (row.Current ? "✓ " : "  ")
                + row.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · "
                + (row.Title.Length > 0 ? row.Title : _localization.Resolve("shell.new_session")))));
            if (filtered.Length > 0) list.SelectedItem = 0;
            list.Enabled = filtered.Length > 0;
            notice.Text = filtered.Length == 0 ? _localization.Resolve("shell.conversation.empty") : "";
        }
        void Open()
        {
            if (list.SelectedItem is not int index || index < 0 || index >= filtered.Length) return;
            if (ChangeConversation("conversation.resume", filtered[index].Id, notice)) { CloseOverlay(); _composer!.SetFocus(); }
        }
        list.Accepted += (_, args) => { args.Handled = true; Open(); };
        var open = ActionButton("conversation-open", _localization.Resolve("shell.conversation.resume"), 4);
        search.TextChanged += (_, _) => { Update(); open.Enabled = filtered.Length > 0; };
        list.ValueChanged += (_, _) => open.Enabled = list.SelectedItem is int at && at >= 0 && at < filtered.Length;
        search.KeyDown += (_, key) => { if (key.KeyCode == KeyCode.Enter) { key.Handled = true; Open(); } };
        open.Accepted += (_, _) => Open();
        var create = ActionButton("conversation-new", _localization.Resolve("shell.conversation.new"), 3);
        create.Accepted += (_, _) => { if (ChangeConversation("conversation.new", errorLabel: notice)) { CloseOverlay(); _composer!.SetFocus(); } };
        var current = rows.FirstOrDefault(row => row.Current);
        var continueLast = ActionButton("conversation-continue-last", _localization.Resolve("shell.conversation.continue_last"), 5);
        continueLast.Visible = _conversationSelectionPending;
        continueLast.Enabled = current is not null;
        continueLast.Accepted += (_, _) =>
        { if (current is not null && ChangeConversation("conversation.resume", current.Id, notice)) { CloseOverlay(); _composer!.SetFocus(); } };
        var close = ActionButton("conversation-close", Pref("close"), 2);
        close.Accepted += (_, _) => CloseOverlay();
        frame.Add(intro, searchLabel, search, list, notice, continueLast, open, create, close);
        PreferenceNavigation.Attach(frame, wrap: false);
        frame.KeyDown += (_, key) =>
        {
            if (!search.HasFocus && key.KeyCode is KeyCode.CursorLeft or KeyCode.CursorRight) key.Handled = true;
        };
        Update();
        open.Enabled = filtered.Length > 0; ApplyTheme(frame);
        _overlay = frame; _window.Add(frame);
        if (_conversationSelectionPending) create.SetFocus();
        else if (list.Enabled) list.SetFocus();
        else search.SetFocus();
    }
}
