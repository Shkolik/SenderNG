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

using CNC.Core.Abstractions;
using CNC.Core.Services;
using SenderNG.Core.Models;

namespace SenderNG.Core.Tests;

public class DemoGrblServiceModalTests
{
    private static DemoGrblService NewDemo(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"demo-modal-{Guid.NewGuid():N}.txt");
        return new DemoGrblService(new MachineProfile(), path, pollIntervalMs: 20);
    }

    [Fact]
    public async Task Reads_default_modal_state()
    {
        var demo = NewDemo(out var _);
        await demo.ConnectAsync("demo");
        Assert.Equal(new ModalState(true, true), await demo.ReadModalStateAsync());
    }

    [Fact]
    public async Task Tracks_units_and_distance_mode()
    {
        var demo = NewDemo(out var _);
        await demo.ConnectAsync("demo");
        await demo.SendAsync("G20");
        await demo.SendAsync("G91");
        Assert.Equal(new ModalState(false, false), await demo.ReadModalStateAsync());
    }

    [Fact]
    public async Task Controlled_stop_lands_idle_without_losing_position()
    {
        var demo = NewDemo(out var _);
        await demo.ConnectAsync("demo");
        await demo.SendAsync("G21G91G0X25");
        MachineState? state = null;
        using var sub = demo.State.Subscribe(s => state = s);
        var before = state!.Positions[0];
        demo.SendRealtime(GrblRealtime.Stop);
        Assert.Equal("Idle", state!.Status);
        Assert.Equal(before, state.Positions[0]);
    }
}