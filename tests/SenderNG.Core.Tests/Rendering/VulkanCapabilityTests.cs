/*
 * VulkanCapabilityTests.cs - the policy half of the Vulkan capability probe
 */

using HelixToolkit.Nex;
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
    [InlineData("Available", null)]
    public void VerdictFor_maps_each_capability_to_its_result_code(
        string capability,
        string? expected)
    {
        var parsed = Enum.Parse<VulkanCapability>(capability);
        var actual = VulkanCapabilities.VerdictFor(parsed);

        Assert.Equal(expected, actual?.ToString());
    }

    // The two failure verdicts drive two different pieces of user-facing copy
    // with different instructions, so collapsing them is the bug this guards.
    [Fact]
    public void The_two_failure_capacities_produce_different_verdicts()
    {
        var loader = VulkanCapabilities.VerdictFor(VulkanCapability.LoaderUnavailable);
        var device = VulkanCapabilities.VerdictFor(VulkanCapability.NoDevice13);

        Assert.NotNull(loader);
        Assert.NotNull(device);
        Assert.NotEqual(loader, device);
    }

    // Guards the specific defect a review found: instance creation returning
    // ErrorIncompatibleDriver used to be folded into "no 1.3 device", so a
    // machine with no installable driver was told to go and update a driver it
    // did not have. The invariant is therefore that NoDevice13 is the *only*
    // capability claiming the driver is too old: every other non-healthy
    // capability must resolve to the NotSupported verdict, and Available must
    // stay non-failure. Adding a fourth capability mapped to RuntimeError fails
    // this test, which is the point.
    [Fact]
    public void Only_a_too_old_device_is_reported_as_a_too_old_device()
    {
        var runtimeFailures = Enum.GetValues<VulkanCapability>()
            .Where(c => VulkanCapabilities.VerdictFor(c) == ResultCode.RuntimeError)
            .ToArray();

        Assert.Equal([VulkanCapability.NoDevice13], runtimeFailures);
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
}
