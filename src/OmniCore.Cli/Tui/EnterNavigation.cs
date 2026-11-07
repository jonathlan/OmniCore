using System.Runtime.CompilerServices;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace OmniCore.Cli;

/// <summary>Explicit Enter routes keep editing separate from committing a form.</summary>
internal static class EnterNavigation
{
    private static readonly ConditionalWeakTable<CheckBox, object> CheckBoxes = new();

    public static void Focus(View target, params View[] sources) => Focus(() => target, sources);

    public static void FocusFields(View scope, View target, bool includeLists = false)
    {
        foreach (var child in scope.SubViews)
        {
            if (child is TextField || includeLists && child is ListView) Focus(target, child);
            else FocusFields(child, target, includeLists);
        }
    }

#pragma warning disable CS0618 // Pinned Terminal.Gui scrollable text panes.
    public static void FocusReadOnly(View scope, View target)
    {
        foreach (var child in scope.SubViews)
        {
            if (child is TextView { ReadOnly: true }) Focus(target, child);
            else FocusReadOnly(child, target);
        }
    }
#pragma warning restore CS0618

    public static void Focus(Func<View?> target, params View[] sources)
    {
        foreach (var source in sources)
            source.KeyDown += (_, key) =>
            {
                if (key.Handled || key.KeyCode != KeyCode.Enter) return;
                var next = target();
                if (next is null || !Available(source) || !Available(next)) return;
                // Consume before focus changes: the same Enter must never press the target.
                key.Handled = true;
                next.SetFocus();
            };
    }

    public static void TraverseForm(View[] fields, Button save, Button cancel)
        => Traverse([.. fields, save, cancel], fields.Length);

    public static void TraverseTabs(params View[] order) => Traverse(order, 0);

    private static void Traverse(View[] order, int enterFieldCount)
    {
        for (var i = 0; i < order.Length; i++)
        {
            var index = i;
            order[i].KeyDown += (_, key) =>
            {
                if (key.Handled) return;
                var delta = key.KeyCode == (KeyCode.Tab | KeyCode.ShiftMask) ? -1
                    : key.KeyCode == KeyCode.Tab || key.KeyCode == KeyCode.Enter && index < enterFieldCount ? 1 : 0;
                if (delta == 0) return;
                key.Handled = true;
                for (var step = 1; step < order.Length; step++)
                {
                    var next = order[(index + delta * step + order.Length) % order.Length];
                    if (Available(next) && next.SetFocus()) return;
                }
            };
        }
    }

    public static void Activate(ListView target, TextField source)
    {
        source.KeyDown += (_, key) =>
        {
            if (key.Handled || key.KeyCode != KeyCode.Enter || !Available(target)) return;
            key.Handled = true;
            if (target.Source is not { Count: > 0 }) return;
            target.SetFocus();
            target.InvokeCommand(Command.Accept);
        };
    }

    public static void Attach(CheckBox checkBox)
    {
        if (CheckBoxes.TryGetValue(checkBox, out _)) return;
        CheckBoxes.Add(checkBox, new());
        checkBox.KeyDown += (_, key) =>
        {
            if (key.Handled || key.KeyCode != KeyCode.Enter || !Available(checkBox)) return;
            key.Handled = true;
            // Use the native activation pipeline, including ValueChanging cancellation
            // and radio-group handlers. Accept would leave the checked value unchanged.
            checkBox.InvokeCommand(Command.Activate);
        };
    }

    private static bool Available(View view)
    {
        if (!view.CanFocus || view.SuperView is null) return false;
        for (View? current = view; current is not null; current = current.SuperView)
            if (!current.Visible || !current.Enabled) return false;
        return true;
    }
}
