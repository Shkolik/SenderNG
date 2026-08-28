using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace SenderNG.Avalonia.Modules.Plugins;

public partial class OffsetsView : UserControl
{
    private readonly Grid _offsetHeader = new();
    private readonly Grid _toolHeader = new();

    public OffsetsView()
    {
        InitializeComponent();
        OffsetHeaderHost.Content = _offsetHeader;
        ToolHeaderHost.Content = _toolHeader;
        DataContextChanged += (_, _) => RebuildHeaders();
    }

    private void RebuildHeaders()
    {
        _offsetHeader.Children.Clear();
        _toolHeader.Children.Clear();
        if (DataContext is not OffsetsViewModel vm)
            return;

        var columns = new ColumnDefinitions { new ColumnDefinition(GridLength.Auto) };
        _offsetHeader.ColumnDefinitions = columns;
        AddHeader(_offsetHeader, 0, "Offset");
        for (var i = 0; i < vm.AxisNames.Count; i++)
        {
            columns.Add(new ColumnDefinition(GridLength.Star));
            AddHeader(_offsetHeader, i + 1, vm.AxisNames[i]);
        }

        var toolColumns = new ColumnDefinitions { new ColumnDefinition(GridLength.Auto) };
        _toolHeader.ColumnDefinitions = toolColumns;
        AddHeader(_toolHeader, 0, "Tool");
        for (var i = 0; i < Math.Min(3, vm.AxisNames.Count); i++)
        {
            toolColumns.Add(new ColumnDefinition(GridLength.Star));
            AddHeader(_toolHeader, i + 1, vm.AxisNames[i]);
        }
        toolColumns.Add(new ColumnDefinition(GridLength.Star));
        AddHeader(_toolHeader, toolColumns.Count - 1, "Radius");
        toolColumns.Add(new ColumnDefinition(GridLength.Auto));
    }

    private static void AddHeader(Grid grid, int column, string text) =>
        grid.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            [Grid.ColumnProperty] = column,
        });
}
