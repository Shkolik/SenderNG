
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
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SenderNG.ViewModels;

namespace SenderNG;

public partial class App : Application
{
    public static IHost? Host { get; private set; }

    /// <summary>Command-line args from <see cref="Program.Main"/> (Avalonia's lifetime
    /// doesn't forward them), read when building the host in <see cref="OnFrameworkInitializationCompleted"/>.</summary>
    public static string[] StartupArgs { get; set; } = [];

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // State emissions can arrive on background threads (demo poll timer,
        // serial reads); capture the UI dispatcher so UiThread can marshal them.
        UiThread.Initialize();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The .ico, not the .png: Win32Icon parses the ICO directory and
            // hands Windows the frame closest to the requested size (16 for the
            // title bar, 24/32 for the taskbar). A PNG takes the
            // CreateIconIndirect fallback instead - one 512px downscale reused
            // everywhere. The stream is already copied into a MemoryStream by
            // IconImpl, so disposing it here is safe.
            using var iconStream = AssetLoader.Open(new Uri("avares://SenderNG/Assets/appIcon.ico"));
            var appIcon = new WindowIcon(iconStream);

            Host = HostSetup.BuildHost(StartupArgs);
            var vm = Host.Services.GetRequiredService<MainViewModel>();

            // Before MainWindow, not after: registering a default on a property
            // of an already-constructed window only worked because Icon is not
            // read in the constructor.
            Window.IconProperty.OverrideDefaultValue<Window>(appIcon);

            desktop.MainWindow = new MainWindow { DataContext = vm };

            // The app starts disconnected; open the connect dialog right after
            // the window is shown so the user picks Serial / Network / Demo first.
            desktop.MainWindow.Opened += (_, _) => _ = vm.ShowConnectDialogAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
