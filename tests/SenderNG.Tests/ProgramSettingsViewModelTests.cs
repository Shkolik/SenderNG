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

using CNC.Core.Services;
using SenderNG.Core.Localization;
using SenderNG.Modules.Fixed;
using Xunit;

namespace SenderNG.Tests;

public class ProgramSettingsViewModelTests
{
    private static (SettingsService Settings, LocalizationService Loc, ProgramSettingsViewModel ViewModel) Spawn()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-program-" + Guid.NewGuid().ToString("N"));
        var settings = new SettingsService(dir);
        settings.Save(settings.Load());
        var loc = LocalizationService.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "Locale"));
        return (settings, loc, new ProgramSettingsViewModel(settings, loc));
    }

    [Fact]
    public void Options_localize_at_boot()
    {
        var (_, _, vm) = Spawn();

        Assert.Equal("Keep comments", vm.Options[0].Name);
        Assert.Equal("Strip all comments", vm.Options[1].Name);
    }

    [Fact]
    public void Options_relocalize_on_locale_change()
    {
        var (_, loc, vm) = Spawn();

        loc.SetLocale("ru");

        Assert.Equal("Сохранять комментарии", vm.Options[0].Name);
        Assert.Equal("Удалять все комментарии", vm.Options[1].Name);
    }

    [Fact]
    public void Relocalization_preserves_the_selected_index()
    {
        var (_, loc, vm) = Spawn();
        vm.SelectedIndex = 1;

        loc.SetLocale("ru");

        Assert.Equal(1, vm.SelectedIndex);
    }
}