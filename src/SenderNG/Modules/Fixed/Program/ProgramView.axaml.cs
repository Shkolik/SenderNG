/*
 * ProgramView.axaml.cs - Program view
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SenderNG.Modules.Fixed;

public partial class ProgramView : UserControl
{
    /// <summary>Uniform row height in the program grid: 24px row + 1px bottom hairline.
    /// Keep in sync with the ItemTemplate in ProgramView.axaml.</summary>
    private const double RowHeight = 25;

    private ProgramViewModel? _vm;
    private ScrollViewer? _scroller;

    public ProgramView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.ScrollRequested -= ScrollToRow;
            _vm = DataContext as ProgramViewModel;
            if (_vm is not null)
                _vm.ScrollRequested += ScrollToRow;
        };
    }

    /// <summary>
    /// Follow the send frontier during a run, keeping the last-sent line near
    /// the vertical middle of the viewport so the next commands stay visible
    /// above the fold. Scrolling is computed from the uniform row height
    /// (index * RowHeight) instead of ScrollIntoView, which only reveals the
    /// row at an edge and doesn't keep it centered as the frontier advances.
    /// </summary>
    private void ScrollToRow(int index)
    {
        // The inner ScrollViewer only exists once the ListBox template has been
        // applied; requests before that are dropped and retried on the next
        // update (the frontier keeps moving during a run).
        var scroller = _scroller ??=
            ProgramList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller is null)
            return;

        var target = index * RowHeight - (scroller.Viewport.Height - RowHeight) / 2;
        var max = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        scroller.Offset = new Vector(0, Math.Clamp(target, 0, max));
    }
}
