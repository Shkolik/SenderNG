
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

using System.Reactive.Subjects;
using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class DroViewModelTests
{
    private sealed class BehaviorSubjectGrbl : IGrblService
    {
        private readonly BehaviorSubject<MachineState> _subject;
        public BehaviorSubjectGrbl(BehaviorSubject<MachineState> subject) => _subject = subject;
        public bool IsConnected => true;
        public IObservable<MachineState> State => _subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public void State_push_shows_axis_letters_and_positions()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new DroViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState { NumAxes = 3, Status = "Idle", Positions = [1, 2, 3] });

        Assert.Equal(3, vm.Axes.Count);
        Assert.Equal("X", vm.Axes[0].Letter);
        Assert.Equal("Y", vm.Axes[1].Letter);
        Assert.Equal("Z", vm.Axes[2].Letter);
        Assert.Equal(1, vm.Axes[0].Position);
        Assert.Equal(2, vm.Axes[1].Position);
        Assert.Equal(3, vm.Axes[2].Position);
    }

    [Fact]
    public void NumAxes_drives_row_count()
    {
        var subject = new BehaviorSubject<MachineState>(new MachineState(3));
        var vm = new DroViewModel(new BehaviorSubjectGrbl(subject));

        subject.OnNext(new MachineState(4));

        Assert.Equal(4, vm.Axes.Count);
        Assert.Equal("A", vm.Axes[3].Letter);

        subject.OnNext(new MachineState(2));

        Assert.Equal(2, vm.Axes.Count);
    }

    [Fact]
    void Initial_subject_value_is_applied_on_construction()
    {
        var subject = new BehaviorSubject<MachineState>(
            new MachineState { NumAxes = 3, Positions = [5, 6, 7] });
        var vm = new DroViewModel(new BehaviorSubjectGrbl(subject));

        Assert.Equal(3, vm.Axes.Count);
        Assert.Equal(5, vm.Axes[0].Position);
    }
}

public class DroProxyInitialStateTests
{
    private sealed class FakeGrbl(BehaviorSubject<MachineState> subject) : IGrblService
    {
        public bool IsConnected => true;
        public IObservable<MachineState> State => subject;
        public Task ConnectAsync(string port) => Task.CompletedTask;
        public void Disconnect() { }
        public Task SendAsync(string line) => Task.CompletedTask;
        public void SendRealtime(byte code) { }
    }

    [Fact]
    public void Axes_populate_immediately_through_proxy()
    {
        // Regression: startup wiring routes the demo service through
        // GrblServiceProxy; its State must replay the current machine state,
        // otherwise the DRO shows no axis rows until the first real emission.
        var inner = new FakeGrbl(new BehaviorSubject<MachineState>(new MachineState(6)));
        var proxy = new GrblServiceProxy(inner);

        var vm = new DroViewModel(proxy);

        Assert.Equal(6, vm.Axes.Count);
    }
}
