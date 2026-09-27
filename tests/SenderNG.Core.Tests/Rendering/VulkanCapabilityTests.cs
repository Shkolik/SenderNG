/*
 * VulkanCapabilityTests.cs - the policy half of the Vulkan capability probe
 */

using SenderNG.Modules.Plotter.Nex.Rendering;
using Xunit.Abstractions;

namespace SenderNG.Core.Tests;

public class VulkanCapabilityTests
{
    private readonly ITestOutputHelper _output;

    public VulkanCapabilityTests(ITestOutputHelper output) => _output = output;

    // Both parameter types are internal, and a public test method cannot take an
    // internal parameter type (CS0051) - see the same note in
    // Plot3DFailureTests. Enum member names are what ToString() returns, so the
    // expectations are passed as strings and the comparison stays type-safe.
    [Theory]
    [InlineData("LoaderUnavailable", "NotSupported")]
    [InlineData("NoDevice13", "RuntimeError")]
    [InlineData("Available", "None")]
    public void ToFailure_maps_each_capability_to_its_kind(
        string capability,
        string expected)
    {
        var parsed = Enum.Parse<VulkanCapability>(capability);
        Assert.Equal(expected, VulkanCapabilities.ToFailure(parsed).ToString());
    }

    // The mapping decides which of two different instructions the user is given,
    // so it is pinned separately from the Vulkan calls it feeds.
    [Fact]
    public void LoaderUnavailable_and_NoDevice13_map_to_different_kinds()
    {
        var loader = VulkanCapabilities.ToFailure(VulkanCapability.LoaderUnavailable);
        var device = VulkanCapabilities.ToFailure(VulkanCapability.NoDevice13);

        Assert.NotEqual(loader, device);
    }

    // Detect() runs on the scene-construction thread pool, where an escaping
    // exception would leave the tab blank with no explanation. Its contract is
    // that it never throws and only ever returns a defined value. The specific
    // value is machine-dependent, so it is reported rather than asserted.
    [Fact]
    public void Detect_never_throws_and_returns_a_defined_capability()
    {
        var capability = VulkanCapabilities.Detect();

        _output.WriteLine($"Detected capability on this machine: {capability}");
        Assert.True(
            Enum.IsDefined(capability),
            $"Detect() returned an undefined capability: {capability}");
    }

    // The scene is only built once per process, and the probe is published
    // through a Lazy precisely so that concurrent callers cannot race inside
    // Vortice's process-global loader state. This pins that single-execution
    // behaviour, which is what makes the probe safe to call from the
    // scene-construction thread pool.
    [Fact]
    public void Detect_is_stable_across_repeated_and_concurrent_calls()
    {
        var first = VulkanCapabilities.Detect();

        var others = new VulkanCapability[8];
        Parallel.For(0, others.Length, _ => others[_] = VulkanCapabilities.Detect());

        Assert.All(others, c => Assert.Equal(first, c));
    }
}
