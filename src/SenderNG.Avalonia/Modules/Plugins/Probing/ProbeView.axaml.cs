using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SenderNG.Avalonia.Modules.Plugins;

public partial class ProbeView : UserControl
{
    // Letters and other non-numeric characters are blocked in the probing
    // numeric inputs (left column + tool panels) so the double/int bindings
    // never hit a validation error. The editable profile ComboBox hosts a
    // TextBox that must keep accepting arbitrary text, so it is excluded.
    // The bindings also use NumericTextConverter, which silently ignores any
    // remaining unparseable text (dangling sign, empty field, paste), so no
    // red validation border or conversion exception can ever surface.
    private static readonly Regex DecimalPattern = new(@"^[+-]?\d*[.,]?\d*$", RegexOptions.Compiled);
    private static readonly Regex IntPattern = new(@"^[+-]?\d*$", RegexOptions.Compiled);

    public ProbeView()
    {
        InitializeComponent();
        AddHandler(InputElement.TextInputEvent, OnNumericTextInput, RoutingStrategies.Tunnel);
    }

    private void OnNumericTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is not TextBox box || string.IsNullOrEmpty(e.Text))
            return;

        // The editable profile ComboBox contains a TextBox for profile names.
        if (box.FindAncestorOfType<ComboBox>() is not null)
            return;

        var pattern = box.Classes.Contains("int") ? IntPattern : DecimalPattern;
        var text = box.Text ?? string.Empty;
        var caret = Math.Min(box.SelectionStart, box.SelectionEnd);
        var length = Math.Abs(box.SelectionEnd - box.SelectionStart);
        var result = text.Remove(caret, length).Insert(caret, e.Text);
        if (!pattern.IsMatch(result))
            e.Handled = true;
    }

    // Avalonia does not auto-open a Button's ContextMenu on click (WPF did);
    // open it explicitly from the "..." profile-actions button.
    private void ProfileMenu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Open(button);
        }
    }
}
