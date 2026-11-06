using System.Reflection;
using OmniCore.Cli;
using OmniCore.Client;
using OmniCore.Domain;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace OmniCore.Tests;

public sealed partial class TuiWiringTests
{
    [Theory]
    [InlineData("es", 80, 24)]
    [InlineData("en", 80, 24)]
    [InlineData("fr", 80, 24)]
    [InlineData("it", 80, 24)]
    [InlineData("es", 180, 80)]
    [InlineData("en", 180, 80)]
    [InlineData("fr", 180, 80)]
    [InlineData("it", 180, 80)]
    public void Opening_selector_explains_choice_and_keeps_controls_separate_when_resized(string locale, int columns, int rows) => RunTuiTest(fx =>
    {
        var current = fx.Server.LastSessionId()!;
        var before = fx.Server.AcquireStore().CurrentSequence(current);
        fx.StartTui(initialColumns: columns, initialRows: rows, selectConversationOnOpen: true,
            localeSelection: new(locale, LocaleSelectionSource.CommandLine));
        fx.Wait(() => fx.App.Overlay?.Id == "conversation-selector", "opening selector laid out");
        void Verify() => fx.Read(() =>
        {
            var frame = fx.App.Overlay!;
            AssertOverlayCenteredAndOpaque(fx);
            Assert.True(frame.Frame.Width <= 100);
            Assert.True(frame.Frame.Height <= 27);
            var intro = frame.SubViews.Single(view => view.Id == "conversation-intro");
            Assert.Equal(new Localization(locale).Resolve("shell.conversation.open_help"), intro.Text);
            Assert.Contains(locale == "fr" ? "Échap" : "Esc", intro.Text);
            Assert.True(frame.SubViews.Single(view => view.Id == "conversation-list").Frame.Height >= 2);
            var views = frame.SubViews.Where(view => view.Visible).ToArray();
            foreach (var view in views)
            {
                Assert.True(view.Frame.X >= 0 && view.Frame.Y >= 0
                    && view.Frame.Right <= frame.Viewport.Width && view.Frame.Bottom <= frame.Viewport.Height, view.Id);
                foreach (var other in views.Where(other => !ReferenceEquals(view, other)))
                    Assert.False(view.Frame.IntersectsWith(other.Frame), view.Id + " overlaps " + other.Id);
            }
        });
        Verify();
        ExportPreferenceFrame(fx, columns, rows, "opening-clear-" + locale, new Localization(locale).Resolve("shell.conversation.continue_last"));
        var resizedColumns = columns == 80 ? 180 : 80;
        var resizedRows = rows == 24 ? 80 : 24;
        fx.Invoke(() => fx.Application.Driver!.SetScreenSize(resizedColumns, resizedRows));
        fx.Wait(() => fx.App.MainWindow!.Frame.Width == resizedColumns
            && fx.App.Overlay!.Frame.Width == Math.Min(100, resizedColumns - 6), "selector adapts to terminal resize");
        Verify();
        KeyWithEffect(fx, KeyCode.Esc, () => fx.App.Overlay is null, "cancel explicit selection");
        Assert.True(fx.App.ConversationSelectionPending);
        Assert.Equal(current, fx.Server.LastSessionId());
        Assert.Equal(before, fx.Server.AcquireStore().CurrentSequence(current));
    }, noColor: false);

    [Theory]
    [InlineData(80, 25)]
    [InlineData(180, 80)]
    public void Semantic_overlay_IDs_keep_their_panel_surface_and_center(int columns, int rows) => RunTuiTest(fx =>
    {
        fx.StartTui(initialColumns: columns, initialRows: rows);
        foreach (var key in new[] { KeyCode.F6, KeyCode.F3, KeyCode.F1, KeyCode.F1 | KeyCode.CtrlMask, KeyCode.P | KeyCode.CtrlMask })
        {
            KeyWithEffect(fx, key, () => fx.App.Overlay is not null, "open " + key);
            fx.Read(() => AssertOverlayCenteredAndOpaque(fx));
            KeyWithEffect(fx, KeyCode.Esc, () => fx.App.Overlay is null, "close " + key);
        }
        OpenGlobalPalette(fx); PaletteSearch(fx, "updates.open");
        KeyWithEffect(fx, KeyCode.Enter, () => fx.App.Overlay?.Id == "release-updates", "open updates");
        fx.Read(() => AssertOverlayCenteredAndOpaque(fx));
    }, noColor: false);

    [Fact]
    public void Long_notice_wraps_scrolls_and_keeps_its_full_content_after_resize() => RunTuiTest(fx =>
    {
        fx.StartTui(initialColumns: 120, initialRows: 40);
        var content = string.Join('\n', Enumerable.Range(1, 80).Select(index => $"Line {index}: " + new string('x', 180)));
        fx.Invoke(() => typeof(TuiApp).GetMethod("ShowMessage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fx.App, [content]));
        fx.Wait(() => fx.App.Overlay?.Id == "message-dialog", "long notice visible");
        fx.Invoke(() => fx.Application.Driver!.SetScreenSize(80, 25));
        fx.Wait(() => fx.App.MainWindow!.Frame.Width == 80 && fx.App.Overlay!.Frame.Height <= 23, "notice adapts to shorter window");
        fx.Read(() =>
        {
            AssertOverlayCenteredAndOpaque(fx);
#pragma warning disable CS0618 // Inspect the production read-only message viewer.
            var body = Descendants(fx.App.Overlay!).OfType<TextView>().Single(view => view.Id == "message-content");
#pragma warning restore CS0618
            Assert.Equal(content, body.Text.Replace("\r\n", "\n"));
            Assert.True(body.ReadOnly && body.WordWrap && body.CanFocus);
            var close = Descendants(fx.App.Overlay!).Single(view => view.Id == "message-close");
            Assert.True(close.HasFocus);
            Assert.True(body.Frame.Bottom <= close.Frame.Top);
            Assert.True(close.Frame.Bottom <= fx.App.Overlay!.Viewport.Height);
            body.SetFocus();
        });
        fx.InjectKey(new Key(KeyCode.End | KeyCode.CtrlMask));
        fx.Wait(() => fx.Read(() => Descendants(fx.App.Overlay!).Single(view => view.Id == "message-content").Viewport.Y > 0), "last lines reachable by keyboard");
        ExportPreferenceFrame(fx, 80, 25, "notice-long", "Line 80:");
        ClickId(fx, "message-close");
        fx.Wait(() => fx.App.Overlay is null, "notice closes normally");
    }, noColor: false);

    private static void AssertOverlayCenteredAndOpaque(TuiFixture fx)
    {
        var frame = fx.App.Overlay!;
        var window = fx.App.MainWindow!;
        Assert.InRange(Math.Abs(frame.Frame.X * 2 + frame.Frame.Width - window.Viewport.Width), 0, 1);
        Assert.InRange(Math.Abs(frame.Frame.Y * 2 + frame.Frame.Height - window.Viewport.Height), 0, 1);
        Assert.InRange(frame.Frame.Bottom, 1, window.Viewport.Height);
        var expected = frame.Id is "mode-preferences" or "security-preferences" ? "#13222E" : "#202C3B";
        Assert.Equal(new Color(expected), frame.GetScheme().Normal.Background);
        Assert.False(frame.ViewportSettings.HasFlag(ViewportSettingsFlags.Transparent));
    }

    [Theory]
    [InlineData(KeyCode.Esc)]
    [InlineData(KeyCode.Enter)]
    public void Long_permission_details_remain_readable_and_safe_response_is_preserved(KeyCode response) => RunTuiTest(fx =>
    {
        var interaction = InteractionId.New();
        var target = string.Join('/', Enumerable.Repeat("long-folder-name", 30)) + "/FINAL-MARKER.txt";
        fx.AppendInteraction(interaction, "Permission", System.Text.Json.JsonSerializer.Serialize(new
        { operation = "tool.authorize", toolOrExecutable = "filesystem.read", target }),
            "[{\"id\":\"allow_once\"},{\"id\":\"deny\"}]", "deny");
        fx.StartTui(initialColumns: 80, initialRows: 25);
        fx.Wait(() => fx.App.Overlay is not null, "permission prompt visible");
        fx.Read(() =>
        {
#pragma warning disable CS0618 // Read-only interaction body from the production renderer.
            var body = Descendants(fx.App.Overlay!).OfType<TextView>().Single(view => view.Id == "interaction-content");
#pragma warning restore CS0618
            Assert.Contains(target, body.Text);
            Assert.True(body.ReadOnly && body.WordWrap);
            Assert.True(fx.App.Overlay!.SubViews.OfType<Button>().Last().HasFocus);
            body.SetFocus();
        });
        fx.InjectKey(new Key(KeyCode.End | KeyCode.CtrlMask));
        fx.Wait(() => fx.Read(() => Descendants(fx.App.Overlay!).Single(view => view.Id == "interaction-content").Viewport.Y > 0), "permission subject can scroll");
        KeyWithEffect(fx, response, () => fx.App.Overlay is null, "reading keeps the safe server option");
        Assert.Equal("deny", fx.Decoded<InteractionResolved>().Single(item => item.InteractionId.Equals(interaction)).OptionId);
    });
}
