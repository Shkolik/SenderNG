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

using System.IO;
using System.Reactive.Subjects;
using CNC.Core;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using SenderNG.ViewModels;
using Xunit;

namespace SenderNG.Core.Tests;

public class MainViewModelSendTests
{
    // Succeeds only on synchronous throws, so the discarded task faulted
    // asynchronously - the exact case the pre-fix try/catch could not see.
    private sealed class FaultingGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _state = new(new MachineState(3));

        public bool IsConnected => true;
        public IObservable<MachineState> State => _state;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }

        public Task SendAsync(string line) =>
            Task.FromException(new IOException("transport write failed"));

        public void SendRealtime(byte code) { }
    }

    private static SettingsService MakeSettings() =>
        new(Path.Combine(Path.GetTempPath(),
            "senderng-send-tests-" + Guid.NewGuid().ToString("N")));

    private static ServiceProvider MakeProvider(SettingsService settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(_ => new DemoGrblService(settings.Load()));
        services.AddSingleton<RealGrblService>();
        return services.BuildServiceProvider();
    }

    private static MainViewModel NewViewModel(FaultingGrbl grbl, SettingsService settings)
        => new(grbl, settings, MakeProvider(settings));

    [Fact]
    public void Home_reports_failure_and_never_sent_when_the_send_faults()
    {
        var settings = MakeSettings();
        var vm = NewViewModel(new FaultingGrbl(), settings);

        vm.HomeCommand.Execute(null);

        Assert.NotNull(vm.Status);
        Assert.Contains("failed", vm.Status!);
        Assert.DoesNotContain("sent", vm.Status);
    }

    [Fact]
    public void Unlock_reports_failure_and_never_sent_when_the_send_faults()
    {
        var settings = MakeSettings();
        var vm = NewViewModel(new FaultingGrbl(), settings);

        vm.UnlockCommand.Execute(null);

        Assert.NotNull(vm.Status);
        Assert.Contains("failed", vm.Status!);
        Assert.DoesNotContain("sent", vm.Status);
    }

    [Fact]
    public void Check_reports_failure_and_never_sent_when_the_send_faults()
    {
        var settings = MakeSettings();
        var vm = NewViewModel(new FaultingGrbl(), settings);

        vm.CheckCommand.Execute(null);

        Assert.NotNull(vm.Status);
        Assert.Contains("failed", vm.Status!);
        Assert.DoesNotContain("sent", vm.Status);
    }
}
