using System.Collections.Specialized;
using Avalonia.Controls;
using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Avalonia.Modules.Fixed;

public partial class ConsoleView : UserControl
{
    public ConsoleView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => Detach();
    }

    private INotifyCollectionChanged? _subscribed;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Detach();
        if (DataContext is ConsoleViewModel { ConsoleLines: INotifyCollectionChanged lines })
        {
            _subscribed = lines;
            lines.CollectionChanged += OnLinesChanged;
        }
    }

    private void Detach()
    {
        if (_subscribed is not null)
        {
            _subscribed.CollectionChanged -= OnLinesChanged;
            _subscribed = null;
        }
    }

    // View-only concern (keep viewport pinned to the newest line), so it lives here
    // rather than in the view model.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;
        var list = this.FindControl<ListBox>("ConsoleList");
        if (list is not null && list.ItemCount > 0)
            list.ScrollIntoView(list.ItemCount - 1);
    }
}
