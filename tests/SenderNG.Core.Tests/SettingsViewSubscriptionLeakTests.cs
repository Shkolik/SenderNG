
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
using Microsoft.Extensions.DependencyInjection;
using SenderNG.Core.Localization;
using SenderNG.Modules.Excellon;
using SenderNG.Modules.Fixed;
using System.Reflection;
using Xunit;

namespace SenderNG.Core.Tests;

public class SettingsViewSubscriptionLeakTests
{
    // Field-like auto-events expose their invocation list through a backing field of the
    // same name. Assert.NotNull rather than "return 0": if an event is ever rewritten with
    // explicit add/remove the field disappears, and a silent 0 would pass for the wrong reason.
    private static int SubscriberCount(object source, string eventName)
    {
        var field = source.GetType().GetField(
            eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(field);
        var handler = field!.GetValue(source) as Delegate;
        return handler?.GetInvocationList().Length ?? 0;
    }

    private static (string Dir, SettingsService Service) MakeSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "senderng-settingsleak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, new SettingsService(dir));
    }

    private static IServiceProvider BuildServices(SettingsService settings, LocalizationService loc) =>
        new ServiceCollection()
            .AddSingleton(settings)
            .AddSingleton(loc)
            .BuildServiceProvider();

    [Fact]
    public void Repeated_CreateSettingsView_calls_do_not_accumulate_Program_subscribers()
    {
        var (dir, settings) = MakeSettings();
        try
        {
            var loc = LocalizationService.LoadFromDirectory(
                Path.Combine(AppContext.BaseDirectory, "Locale"));
            var services = BuildServices(settings, loc);

            var settingsBaseline = SubscriberCount(settings, "Changed");
            var locBaseline = SubscriberCount(loc, "Changed");

            var module = new ProgramModule();
            // AttachServices is mandatory: without it _services stays null, CreateSettingsView
            // takes its null guard and returns null forever, and this test would pass without
            // ever growing the count. ModulePanelTests constructs `new ProgramModule()` bare,
            // which is exactly that trap - hence the Assert.NotNull on every returned view.
            module.AttachServices(services);

            Assert.NotNull(module.CreateSettingsView());
            var settingsAfterFirst = SubscriberCount(settings, "Changed");

            for (var i = 0; i < 24; i++)
                Assert.NotNull(module.CreateSettingsView());

            // Bounded at exactly one retained leaf VM, and no growth across 24 further opens.
            Assert.Equal(settingsAfterFirst, SubscriberCount(settings, "Changed"));
            Assert.InRange(SubscriberCount(settings, "Changed"), settingsBaseline, settingsBaseline + 1);
            Assert.Equal(locBaseline + 1, SubscriberCount(loc, "Changed"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
