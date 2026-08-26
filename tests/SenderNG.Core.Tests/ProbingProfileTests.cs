using SenderNG.Core.Modules.Probing;
using Xunit;

namespace SenderNG.Core.Tests;

public class ProbingProfileTests
{
    [Fact]
    public void Default_profile_matches_ioSender_defaults()
    {
        var p = ProbingProfile.CreateDefault();

        Assert.Equal("<Default>", p.Name);
        Assert.Equal(0d, p.RapidsFeedRate);
        Assert.Equal(100d, p.ProbeFeedRate);
        Assert.Equal(25d, p.LatchFeedRate);
        Assert.Equal(10d, p.ProbeDistance);
        Assert.Equal(0.5d, p.LatchDistance);
        Assert.Equal(2d, p.ProbeDiameter);
        Assert.Equal(5d, p.XYClearance);
        Assert.Equal(5d, p.Offset);
        Assert.Equal(0d, p.ProbeOffsetX);
        Assert.Equal(0d, p.ProbeOffsetY);
        Assert.Equal(3d, p.Depth);
        Assert.Equal(1d, p.TouchPlateHeight);
        Assert.Equal(1d, p.FixtureHeight);
        Assert.False(p.TouchPlateIsXY);
    }

    [Fact]
    public void Add_creates_profile_from_working_values()
    {
        var profiles = new ProbingProfiles();
        profiles.Profiles.Add(ProbingProfile.CreateDefault());
        var working = ProbingProfile.CreateDefault();
        working.ProbeFeedRate = 250;
        working.ProbeDistance = 20;

        var added = profiles.Add("foam", working);

        Assert.Equal("foam", added.Name);
        Assert.Equal(250d, added.ProbeFeedRate);
        Assert.Equal(20d, added.ProbeDistance);
        Assert.Equal(2, profiles.Profiles.Count);
    }

    [Fact]
    public void Update_copies_working_values_into_existing_profile()
    {
        var profiles = new ProbingProfiles();
        var p = profiles.Add("p", ProbingProfile.CreateDefault());
        var working = ProbingProfile.CreateDefault();
        working.ProbeDiameter = 6d;
        working.TouchPlateIsXY = true;

        profiles.Update(p, working);

        Assert.Equal(6d, p.ProbeDiameter);
        Assert.True(p.TouchPlateIsXY);
    }

    [Fact]
    public void Delete_removes_profile_but_never_the_last_one()
    {
        var profiles = new ProbingProfiles();
        var a = profiles.Add("a", ProbingProfile.CreateDefault());
        var b = profiles.Add("b", ProbingProfile.CreateDefault());

        Assert.True(profiles.Delete(a));
        Assert.Single(profiles.Profiles);

        Assert.False(profiles.Delete(b));
        Assert.Single(profiles.Profiles);
    }

    [Fact]
    public void EnsureDefault_seeds_ioSender_default_when_empty()
    {
        var profiles = new ProbingProfiles();
        profiles.EnsureDefault();
        Assert.Single(profiles.Profiles);
        Assert.Equal("<Default>", profiles.Profiles[0].Name);
    }
}

public class ProbingProfileStoreTests : IDisposable
{
    private readonly string _dir;

    public ProbingProfileStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "senderng-profiles-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Load_creates_default_when_file_missing()
    {
        var store = new ProbingProfileStore(Path.Combine(_dir, "probing.json"));
        var profiles = store.Load();
        Assert.Single(profiles.Profiles);
        Assert.Equal("<Default>", profiles.Profiles[0].Name);
    }

    [Fact]
    public void Save_then_Load_roundtrips_profiles()
    {
        var store = new ProbingProfileStore(Path.Combine(_dir, "probing.json"));
        var profiles = store.Load();
        profiles.Add("v2", ProbingProfile.CreateDefault());
        profiles.Profiles[1].ProbeFeedRate = 300;

        Assert.True(store.Save(profiles));

        var reloaded = new ProbingProfileStore(Path.Combine(_dir, "probing.json")).Load();
        Assert.Equal(2, reloaded.Profiles.Count);
        Assert.Equal(300d, reloaded.Profiles[1].ProbeFeedRate);
        Assert.Equal(100d, reloaded.Profiles[0].ProbeFeedRate);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_default()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "probing.json"), "not json {{{");
        var store = new ProbingProfileStore(Path.Combine(_dir, "probing.json"));
        var profiles = store.Load();
        Assert.Single(profiles.Profiles);
        Assert.Equal("<Default>", profiles.Profiles[0].Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
