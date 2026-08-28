using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SenderNG.Avalonia.Modules.Plugins;

public partial class WorkParametersView : UserControl
{
    public WorkParametersView() => InitializeComponent();

    private void CoordinateSystemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WorkParametersViewModel vm || sender is not ComboBox combo || !combo.IsDropDownOpen)
            return;
        if (combo.SelectedItem is string code)
            vm.SelectCoordinateSystemCommand.Execute(code);
    }

    private void ToolChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WorkParametersViewModel vm || sender is not ComboBox combo || !combo.IsDropDownOpen)
            return;
        if (combo.SelectedItem is ToolRow tool)
            vm.SelectToolCommand.Execute(tool);
    }
}
