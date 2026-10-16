/*
 * AvaloniaDialogServiceHeadlessTests.cs - headless window-property tests for the
 * dialog service: preferred size, resizable flags and the minimum-size floor.
 */

/*

Copyright (c) 2026 Andrew Shkolik

All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

Â· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

Â· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

Â· Neither the name of the copyright holder nor the names of its contributors may
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
using Avalonia.Headless.XUnit;
using CNC.Core.Abstractions;
using SenderNG.Services;
using Xunit;

namespace SenderNG.Tests;

public class AvaloniaDialogServiceHeadlessTests
{
    private sealed class FixedViewFactory : IViewFactory
    {
        private readonly Control _view;
        public FixedViewFactory(Control view) => _view = view;
        public void Register<TViewModel, TView>() where TView : class { }
        public object Create(object viewModel) => _view;
    }

    [AvaloniaFact]
    public async Task Resizable_dialog_gets_the_preferred_size_and_the_minimum_floor()
    {
        var view = new Border();
        var service = new AvaloniaDialogService(new FixedViewFactory(view));

        var task = service.ShowDialogAsync("Size contract", new object(),
            preferredSize: (900.0, 560.0), minSize: (720.0, 480.0));

        // The headless host has no main window, so the service takes its Show()
        // branch; the window exists once ShowDialogAsync has been called.
        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(view));
        try
        {
            Assert.True(window.CanResize);
            Assert.Equal(SizeToContent.Manual, window.SizeToContent);
            Assert.Equal(900.0, window.Width);
            Assert.Equal(560.0, window.Height);
            Assert.Equal(720.0, window.MinWidth);
            Assert.Equal(480.0, window.MinHeight);
        }
        finally
        {
            window.Close();
        }
        await task;
    }

    [AvaloniaFact]
    public async Task Size_to_content_dialog_ignores_the_minimum_floor()
    {
        var view = new Border();
        var service = new AvaloniaDialogService(new FixedViewFactory(view));

        var task = service.ShowDialogAsync("No size contract", new object(),
            preferredSize: null, minSize: (720.0, 480.0));

        var window = Assert.IsType<Window>(TopLevel.GetTopLevel(view));
        try
        {
            Assert.False(window.CanResize);
            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            // A non-resizable window has no floor to apply: the platform default stands.
            Assert.Equal(0, window.MinWidth);
            Assert.Equal(0, window.MinHeight);
        }
        finally
        {
            window.Close();
        }
        await task;
    }

    /// <summary>Contract: the app-settings dialog (the one the floor exists for)
    /// declares both a preferred size and a floor inside it.</summary>
    [Fact]
    public void App_settings_module_declares_a_floor_within_its_preferred_size()
    {
        var module = new SenderNG.Modules.Fixed.AppSettingsModule();

        Assert.Equal<(double, double)?>((900.0, 560.0), module.DialogPreferredSize);
        var preferred = module.DialogPreferredSize!.Value;
        var floor = module.DialogMinimumSize;
        Assert.NotNull(floor);
        Assert.True(floor!.Value.Width <= preferred.Width && floor.Value.Height <= preferred.Height,
            $"floor {floor} must fit inside preferred {preferred}");
    }
}
