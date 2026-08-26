using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SenderNG.Avalonia.Modules.Plugins;

public partial class ProbeView : UserControl
{
    public ProbeView() => InitializeComponent();

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
