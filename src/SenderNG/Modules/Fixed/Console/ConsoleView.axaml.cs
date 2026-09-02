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

using Avalonia.Controls;
using Avalonia.VisualTree;
using SenderNG.Modules.Fixed;

namespace SenderNG.Modules.Fixed;

public partial class ConsoleView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public ConsoleView()
    {
        InitializeComponent();

        // Keep the console pinned to the newest output, mirroring ioSender's
        // AlwaysScrollToEnd. The log is a read-only TextBox bound to the joined
        // ConsoleText string; TextChanged fires while the inner ScrollViewer
        // still has the *previous* extent, so scrolling there is always one
        // change behind (the "doesn't autoscroll" symptom). Instead we pin on
        // the ScrollViewer's ScrollChanged, which is raised after layout has
        // measured the freshly bound text.
        ConsoleText.TextChanged += (_, _) => ConsoleText.CaretIndex = ConsoleText.Text?.Length ?? 0;
        ConsoleText.LayoutUpdated += (_, _) => EnsureScrollViewer();
    }

    private void EnsureScrollViewer()
    {
        if (_scrollViewer is not null)
            return;

        // Template children (the ScrollViewer) exist only after first layout.
        var viewer = ConsoleText.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer is null)
            return;

        viewer.ScrollChanged += OnScrollChanged;
        viewer.ScrollToEnd(); // land at the bottom when the view (re)opens
        _scrollViewer = viewer;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Content grew (a new console line was measured) — pin to the end.
        // Offset-only changes (user scrolling, ScrollToEnd itself) are ignored,
        // so there is no feedback loop; on extent shrink (line trimming) the
        // ScrollViewer clamps the offset to the new end on its own.
        if (e.ExtentDelta.Y > 0)
            _scrollViewer?.ScrollToEnd();
    }
}