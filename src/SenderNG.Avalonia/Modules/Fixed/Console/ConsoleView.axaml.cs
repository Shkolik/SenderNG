/*
 * ConsoleView.axaml.cs - Console view
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.


*/

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
