
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
using Avalonia.Input.Platform;
using SenderNG.ViewModels;

namespace SenderNG.Views;

public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private AboutViewModel? _wired;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wired is { } old)
        {
            old.CloseRequested -= OnCloseRequested;
            old.CopyRequested -= OnCopyRequested;
        }

        _wired = null;
        if (DataContext is AboutViewModel vm)
        {
            vm.CloseRequested += OnCloseRequested;
            vm.CopyRequested += OnCopyRequested;
            _wired = vm;
        }
    }

    private void OnCloseRequested(object? sender, bool result) => Close();

    private void OnCopyRequested(object? sender, string text) => _ = WriteClipboardAsync(text);

    private async Task WriteClipboardAsync(string text)
    {
        var vm = _wired;
        var ok = false;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard is { } clip)
            {
                await clip.SetTextAsync(text);
                ok = true;
            }
        }
        catch
        {
            // Clipboard unavailable or write failed — leave the label alone.
        }

        vm?.SetCopyResult(ok);
    }
}
