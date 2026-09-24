
/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Collections.ObjectModel;

namespace SenderNG.Core.Modules.Probing;

/// <summary>
/// Collection of named probing profiles with add/update/delete semantics,
/// ported from ioSender's <c>ProbingProfiles</c>. The last remaining profile
/// cannot be deleted (a valid profile must always exist).
/// </summary>
public class ProbingProfiles
{
    public ObservableCollection<ProbingProfile> Profiles { get; private set; } = new();

    /// <summary>Adds a profile copying the current working values from <paramref name="data"/>.</summary>
    public ProbingProfile Add(string name, ProbingProfile data)
    {
        var profile = new ProbingProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<Default>" : name,
            RapidsFeedRate = data.RapidsFeedRate,
            ProbeFeedRate = data.ProbeFeedRate,
            LatchFeedRate = data.LatchFeedRate,
            ProbeDistance = data.ProbeDistance,
            LatchDistance = data.LatchDistance,
            ProbeDiameter = data.ProbeDiameter,
            Offset = data.Offset,
            ProbeOffsetX = data.ProbeOffsetX,
            ProbeOffsetY = data.ProbeOffsetY,
            XYClearance = data.XYClearance,
            Depth = data.Depth,
            TouchPlateHeight = data.TouchPlateHeight,
            TouchPlateIsXY = data.TouchPlateIsXY,
            FixtureHeight = data.FixtureHeight,
        };
        Profiles.Add(profile);
        return profile;
    }

    public void Update(ProbingProfile profile, ProbingProfile data)
    {
        if (profile is null)
            return;

        profile.RapidsFeedRate = data.RapidsFeedRate;
        profile.ProbeFeedRate = data.ProbeFeedRate;
        profile.LatchFeedRate = data.LatchFeedRate;
        profile.ProbeDistance = data.ProbeDistance;
        profile.LatchDistance = data.LatchDistance;
        profile.ProbeDiameter = data.ProbeDiameter;
        profile.Offset = data.Offset;
        profile.ProbeOffsetX = data.ProbeOffsetX;
        profile.ProbeOffsetY = data.ProbeOffsetY;
        profile.XYClearance = data.XYClearance;
        profile.Depth = data.Depth;
        profile.TouchPlateHeight = data.TouchPlateHeight;
        profile.TouchPlateIsXY = data.TouchPlateIsXY;
        profile.FixtureHeight = data.FixtureHeight;
    }

    /// <summary>Deletes a profile; the last remaining profile is never removed.</summary>
    public bool Delete(ProbingProfile profile)
    {
        if (profile is null || Profiles.Count <= 1)
            return false;
        return Profiles.Remove(profile);
    }

    /// <summary>Seeds the collection with ioSender's &lt;Default&gt; profile when empty.</summary>
    public void EnsureDefault()
    {
        if (Profiles.Count == 0)
            Profiles.Add(ProbingProfile.CreateDefault());
    }
}
