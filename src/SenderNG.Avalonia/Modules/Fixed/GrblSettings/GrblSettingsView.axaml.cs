/*
 * GrblSettingsView.axaml.cs - GRBL settings view
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
using Avalonia.Controls.Selection;
using Avalonia.Input;

namespace SenderNG.Avalonia.Modules.Fixed;

public partial class GrblSettingsView : UserControl
{
    public GrblSettingsView()
    {
        InitializeComponent();
        Tree.SelectionChanged += OnTreeSelectionChanged;
    }

    /// <summary>
    /// Only leaf settings are selectable — a click on a group node just collapses /
    /// expands it (via the chevron); group selections are discarded so the detail
    /// pane keeps showing the previously chosen setting.
    /// </summary>
    private static void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TreeView tree)
            return;
        if (tree.SelectedItem is GrblSettingGroupItem)
            tree.SelectedItem = null;
    }

    /// <summary>
    /// Clicking anywhere on a group label expands/collapses it — the TreeViewItem
    /// container's IsExpanded is two-way bound to the group item's property.
    /// </summary>
    private static void OnGroupTapped(object? sender, TappedEventArgs e)
    {
        if (sender is StyledElement { DataContext: GrblSettingGroupItem group })
            group.IsExpanded = !group.IsExpanded;
    }
}