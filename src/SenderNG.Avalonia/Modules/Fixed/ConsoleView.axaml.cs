using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.VisualTree;
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

    // View-only concern (keep the console pinned to the newest output), so it
    // lives here rather than in the view model. The log is a read-only TextBox
    // (ioSender style); after each line lands, jump the caret/scroll to the end
    // so new output is always visible, mirroring ioSender's AlwaysScrollToEnd.
    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;
        var box = this.FindControl<TextBox>("ConsoleText");
        if (box is null || box.Text is null)
            return;
        box.CaretIndex = box.Text.Length;
        box.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
    }
}
