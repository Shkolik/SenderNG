
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
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;
using SenderNG.Core.Localization;
using SenderNG.Core.Settings;
using Xunit;

namespace SenderNG.Tests;

[Collection("LocalizationState")]
public class LocExtensionHeadlessTests
{
    private sealed class StubProvider : IGrblSettingsProvider
    {
        public IReadOnlyList<string> ValueLines { get; init; } = [];
        public Task<IReadOnlyList<string>> GetSettingLinesAsync() => Task.FromResult(ValueLines);
        public Task<IReadOnlyList<string>> GetSettingDetailLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetSettingGroupLinesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> GetSettingDescriptionAsync(int id) => Task.FromResult<string?>(null);
    }

    [AvaloniaFact]
    public async Task Loc_extension_resolves_and_relocalizes_in_a_view()
    {
        var prev = LocalizationService.Current;
        try
        {
            LocalizationService.Current = HeadlessTestCatalog.Locale;
            var provider = new StubProvider();
            var grbl = new GrblServiceProxy(
                new DemoGrblService(new MachineProfile(), localization: HeadlessTestCatalog.Locale));
            var vm = new GrblSettingsViewModel(provider, grbl, HeadlessTestCatalog.Locale);
            vm.LoadAsync().GetAwaiter().GetResult();

            var view = new GrblSettingsView { DataContext = vm };
            var window = new Window { Width = 920, Height = 560, Content = view };
            window.Show();
            for (var i = 0; i < 4; i++)
                await Pump();

            var search = window.GetVisualDescendants().OfType<TextBox>()
                .First(t => t.PlaceholderText is not null);
            Assert.Equal("Search settings...", search.PlaceholderText);

            if (HeadlessTestCatalog.Locale.AvailableLocales.Contains("ru"))
            {
                try
                {
                    HeadlessTestCatalog.Locale.SetLocale("ru");
                    for (var i = 0; i < 4; i++)
                        await Pump();
                    Assert.Equal("Поиск настроек...", search.PlaceholderText);
                }
                finally
                {
                    HeadlessTestCatalog.Locale.SetLocale("en");
                    for (var i = 0; i < 4; i++)
                        await Pump();
                }
            }

            window.Close();
        }
        finally
        {
            LocalizationService.Current = prev;
        }
    }

    private static async Task Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(16);
        await Task.Yield();
    }
}