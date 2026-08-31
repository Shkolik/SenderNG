/*
 * WorkParametersView.axaml.cs - Work Parameters view
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
using Avalonia.Interactivity;

namespace SenderNG.Modules.Fixed;

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
