
/*

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
using SenderNG.Core.Services;
using SenderNG.Localization;
using SenderNG.Services;
using SenderNG.ViewModels;

namespace SenderNG;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private KeymapDispatcher? _dispatcher;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += (_, _) => BindingCommit.CommitFocused(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.FocusMdiRequested -= OnFocusMdi;

        _dispatcher?.Dispose();
        _dispatcher = null;
        _viewModel = DataContext as MainViewModel;

        if (_viewModel is null)
            return;

        _viewModel.FocusMdiRequested += OnFocusMdi;
        if (_viewModel.Hotkeys is { } hotkeys)
        {
            AppHotkeys.Current = hotkeys;
            _dispatcher = new KeymapDispatcher(this, new KeymapController(hotkeys), _viewModel.ModuleIdForView);
        }
    }

    private void OnFocusMdi(object? sender, EventArgs e)
    {
        MdiBox.Focus();
        MdiBox.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.SelectAll();
    }
}
