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
