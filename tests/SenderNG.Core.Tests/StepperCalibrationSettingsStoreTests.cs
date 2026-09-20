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

using System.Text.Json;
using CNC.Core.Services;
using SenderNG.Modules.Plugins;

namespace SenderNG.Core.Tests;

public class StepperCalibrationSettingsStoreTests
{
    [Fact]
    public void Fresh_profile_defaults_distance_to_100()
    {
        Assert.Equal(100d, StepperCalibrationSettingsStore.Load(new MachineProfile()).Distance);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var profile = new MachineProfile();
        StepperCalibrationSettingsStore.Save(profile, new StepperCalibrationSettings { Distance = 42.5 });
        Assert.Equal(42.5, StepperCalibrationSettingsStore.Load(profile).Distance);
    }

    [Fact]
    public void Load_tolerates_json_element_shape()
    {
        var profile = new MachineProfile();
        StepperCalibrationSettingsStore.Save(profile, new StepperCalibrationSettings { Distance = 33 });
        var json = JsonSerializer.Serialize(profile);
        var roundTripped = JsonSerializer.Deserialize<MachineProfile>(json)!;
        Assert.Equal(33d, StepperCalibrationSettingsStore.Load(roundTripped).Distance);
    }

    [Fact]
    public void Corrupt_section_falls_back_to_default()
    {
        var profile = new MachineProfile();
        profile.ModuleSettings["stepper-calibration"] = "not-an-object";
        Assert.Equal(100d, StepperCalibrationSettingsStore.Load(profile).Distance);
    }
}