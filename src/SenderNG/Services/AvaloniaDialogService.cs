
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CNC.Core.Abstractions;

namespace SenderNG.Services;

public class AvaloniaDialogService : IDialogService
{
    private readonly Func<object, object> _viewResolver;

    public AvaloniaDialogService(Func<object, object> viewResolver)
    {
        _viewResolver = viewResolver;
    }

    public async Task<bool?> ShowDialogAsync(string title, object viewModel, (double Width, double Height)? preferredSize = null)
    {
        var window = new Window
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#f6f6f6")),
            Content = _viewResolver(viewModel),
        };

        if (preferredSize is { } size)
        {
            // Explicit size: manual layout so user resizes propagate to content.
            window.SizeToContent = SizeToContent.Manual;
            window.Width = size.Width;
            window.Height = size.Height;
            window.CanResize = true;
            window.MinWidth = 480;
            window.MinHeight = 320;
        }
        else
        {
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.CanResize = false;
        }

        bool? result = null;
        if (viewModel is IClosableDialog closable)
            closable.CloseRequested += (_, r) =>
            {
                result = r;
                window.Close(r);
            };

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            window.Show();
            return result;
        }

        await window.ShowDialog(owner);
        return result;
    }
}
