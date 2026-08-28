using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using CNC.Core.Services;

namespace SenderNG.Avalonia.Modules.Plugins;

public partial class OffsetsView : UserControl
{
    private readonly StackPanel _offsetRows = new() { Spacing = 2 };
    private readonly StackPanel _toolRows = new() { Spacing = 2 };
    private readonly Grid _offsetHeader = new();
    private readonly Grid _toolHeader = new();

    public OffsetsView()
    {
        InitializeComponent();
        OffsetHeaderHost.Content = _offsetHeader;
        OffsetRowsHost.Content = _offsetRows;
        ToolHeaderHost.Content = _toolHeader;
        ToolRowsHost.Content = _toolRows;
        DataContextChanged += (_, _) => Rebuild();
    }

    private void Rebuild()
    {
        _offsetRows.Children.Clear();
        _toolRows.Children.Clear();
        _offsetHeader.Children.Clear();
        _toolHeader.Children.Clear();
        if (DataContext is not OffsetsViewModel vm)
            return;

        BuildHeader(_offsetHeader, "Offset", vm.AxisNames);
        foreach (var row in vm.Rows)
        {
            var grid = CreateGrid(vm.AxisNames.Count);
            AddCell(grid, new TextBlock { Text = row.Code, Width = 48, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold }, 0);
            for (var i = 0; i < vm.AxisNames.Count; i++)
            {
                var box = new TextBox { Text = row.GetValueAt(i), Margin = new Thickness(2), CornerRadius = new CornerRadius(0) };
                var index = i;
                box.TextChanged += (_, _) => row.SetValueAt(index, box.Text ?? string.Empty);
                AddCell(grid, box, i + 1);
            }
            _offsetRows.Children.Add(grid);
        }

        var toolAxes = vm.AxisNames.Take(3).ToArray();
        BuildHeader(_toolHeader, "Tool", toolAxes, "Radius", true);
        foreach (var tool in vm.Tools)
        {
            var grid = CreateGrid(toolAxes.Length + 2);
            AddCell(grid, new TextBlock { Text = tool.DisplayName, Width = 48, VerticalAlignment = VerticalAlignment.Center }, 0);
            for (var i = 0; i < toolAxes.Length; i++)
            {
                var box = new TextBox { Text = tool.GetOffsetAt(i), Margin = new Thickness(2), CornerRadius = new CornerRadius(0) };
                var index = i;
                box.TextChanged += (_, _) => tool.SetOffsetAt(index, box.Text ?? string.Empty);
                AddCell(grid, box, i + 1);
            }
            var radius = new TextBox { Text = tool.Radius, Margin = new Thickness(2), CornerRadius = new CornerRadius(0) };
            radius.TextChanged += (_, _) => tool.Radius = radius.Text ?? string.Empty;
            AddCell(grid, radius, toolAxes.Length + 1);
            var save = new Button { Content = "Save", Width = 60, CornerRadius = new CornerRadius(0), Command = vm.SaveToolCommand, CommandParameter = tool };
            AddCell(grid, save, toolAxes.Length + 2);
            _toolRows.Children.Add(grid);
        }
    }

    private static Grid CreateGrid(int axisCount)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var i = 0; i < axisCount; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        return grid;
    }

    private static void AddCell(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void BuildHeader(Grid grid, string first, IReadOnlyList<string> axes, string? extra = null, bool addButton = false)
    {
        grid.ColumnDefinitions = new ColumnDefinitions { new ColumnDefinition(GridLength.Auto) };
        for (var i = 0; i < axes.Count + (extra is null ? 0 : 1) + (addButton ? 1 : 0); i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        AddHeader(grid, 0, first);
        for (var i = 0; i < axes.Count; i++) AddHeader(grid, i + 1, axes[i]);
        if (extra is not null) AddHeader(grid, axes.Count + 1, extra);
    }

    private static void AddHeader(Grid grid, int column, string text)
    {
        var header = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        AddCell(grid, header, column);
    }
}
