/*
 * VulkanCapability.cs - ask the Vulkan runtime what this machine can actually
 * do, before the engine is allowed to try. The engine cannot answer this
 * question itself (see the class remarks), so we ask it ourselves.
 */

using Vortice.Vulkan;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>What the Vulkan runtime on this machine can do, as far as Plot 3D
/// is concerned.</summary>
internal enum VulkanCapability
{
    /// <summary>No usable Vulkan loader: the loader is missing, failed to
    /// initialize, or has no drivers installed. The machine needs a Vulkan
    /// driver before anything else can work.</summary>
    LoaderUnavailable,

    /// <summary>The loader works, but no device on this machine reports
    /// Vulkan 1.3. The machine has Vulkan; its GPU or driver is too old.</summary>
    NoDevice13,

    /// <summary>A Vulkan 1.3 device is present.</summary>
    Available,
}

/// <summary>Determines whether this machine can run Plot 3D, and says which of
/// the two failure reasons applies when it cannot.</summary>
/// <remarks>
/// <para>
/// This exists because the engine cannot be trusted to report the reason. In
/// the shipped <c>HelixToolkit.Nex 1.3.0-alpha.41</c> (built Release, which is
/// what a NuGet package is), <c>ResultCode.CheckResult()</c> only logs on error
/// and returns the code — its assertion is compiled out — and
/// <c>VulkanContext.InitContext()</c> discards the return value of
/// <c>InitPhysicalDevice()</c> before returning <c>ResultCode.Ok</c>
/// unconditionally. So a machine with a Vulkan 1.2 GPU gets <c>Ok</c> back and
/// a silently broken viewport. Its own "does not support Vulkan 1.3 or higher"
/// check does run, but its verdict is logged and thrown away.
/// </para>
/// <para>
/// So the verdict here is ours, and deliberately positive: we enumerate devices
/// and confirm at least one reports 1.3, rather than inferring capability from
/// a success that carries no information. This reverses an earlier "no device
/// enumeration" constraint, which was written on the belief that the engine's
/// own return code was meaningful. It is not.
/// </para>
/// <para>
/// Only <em>existence</em> is checked, never ranking or selection: the engine
/// still picks its own device, and we never name or interrogate a specific GPU
/// in anything the user sees. That keeps the message honest without
/// re-implementing <c>FindBestDevice</c>.
/// </para>
/// </remarks>
internal static class VulkanCapabilities
{
    /// <summary>Probed at most once per process, and only ever by one thread.
    /// <para>
    /// The <c>Lazy</c> is load-bearing, not an optimisation. Vortice resolves
    /// instance-level entry points into process-global state, so two threads
    /// probing at once race inside <c>vkLoadInstanceOnly</c> and the loser
    /// access-violates (0xC0000005) — observed as an intermittent test-host
    /// crash, not as a catchable exception. Single-execution publication makes
    /// the race impossible, and matches the fact that the answer cannot change
    /// while the process is alive.
    /// </para>
    /// </summary>
    private static readonly Lazy<VulkanCapability> _cached =
        new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Probe the Vulkan runtime on this machine. Safe to call from any
    /// thread, any number of times; the work happens once.</summary>
    public static VulkanCapability Detect() => _cached.Value;

    /// <summary>Do the actual probe. Never throws: any unexpected failure
    /// degrades to <see cref="VulkanCapability.LoaderUnavailable"/>, because we
    /// cannot demonstrate a working loader in that case, and the fallback panel
    /// plus the diagnostics log are a better outcome than an unhandled exception
    /// on the scene-construction thread pool. Note that a native access
    /// violation would <em>not</em> be caught here — see the comment on
    /// <c>vkLoadInstanceOnly</c>.</summary>
    private static VulkanCapability Probe()
    {
        try
        {
            if (Vulkan.vkInitialize() != VkResult.Success)
                return VulkanCapability.LoaderUnavailable;

            return HasDevice13() ? VulkanCapability.Available : VulkanCapability.NoDevice13;
        }
        catch (Exception)
        {
            return VulkanCapability.LoaderUnavailable;
        }
    }

    /// <summary>Whether any physical device reports Vulkan 1.3 or newer.
    /// <para>
    /// Needs a <see cref="VkInstance"/> to enumerate against, so a throwaway
    /// one is created and destroyed. An API-1.0 instance is enough:
    /// <c>vkEnumeratePhysicalDevices</c> and
    /// <c>vkGetPhysicalDeviceProperties</c> are both 1.0 entry points, and
    /// asking for 1.0 means the probe cannot itself fail for want of a newer
    /// loader.
    /// </para>
    /// </summary>
    private static unsafe bool HasDevice13()
    {
        VkUtf8ReadOnlyString appName = "SenderNG"u8;
        VkUtf8ReadOnlyString engineName = "SenderNG"u8;

        var appInfo = new VkApplicationInfo
        {
            pNext = null,
            pApplicationName = appName,
            applicationVersion = new VkVersion(1, 0, 0),
            pEngineName = engineName,
            engineVersion = new VkVersion(1, 0, 0),
            apiVersion = new VkVersion(1, 0, 0),
        };

        var createInfo = new VkInstanceCreateInfo
        {
            sType = VkStructureType.InstanceCreateInfo,
            pApplicationInfo = &appInfo,
        };

        if (Vulkan.vkCreateInstance(&createInfo, null, out var instance) != VkResult.Success)
            return false;

        // Mandatory, and not optional politeness: Vortice dispatches
        // instance-level entry points through pointers it resolves at load time.
        // Without this call, vkEnumeratePhysicalDevices does not return an
        // error - it dereferences a null dispatch pointer and takes the process
        // down with an access violation (0xC0000005), which no try/catch in this
        // file can intercept. Found by running the probe, not by reading it.
        Vulkan.vkLoadInstanceOnly(instance);

        try
        {
            uint count = 0;
            if (Vulkan.vkEnumeratePhysicalDevices(instance, &count, null) != VkResult.Success
                || count == 0)
            {
                return false;
            }

            var devices = stackalloc VkPhysicalDevice[(int)count];
            if (Vulkan.vkEnumeratePhysicalDevices(instance, &count, devices) != VkResult.Success)
                return false;

            for (var i = 0; i < (int)count; i++)
            {
                Vulkan.vkGetPhysicalDeviceProperties(devices[i], out var properties);
                if (properties.apiVersion >= new VkVersion(1, 3, 0))
                    return true;
            }

            return false;
        }
        finally
        {
            // The probe runs at most once per process, but an undestroyed
            // instance is a real leak and the finally costs nothing.
            Vulkan.vkDestroyInstance(instance, null);
        }
    }

    /// <summary>Map a capability onto the user-facing failure kind.
    /// <para>
    /// Pure and separate from <see cref="Detect"/> so the policy can be tested
    /// without a GPU: the Vulkan calls above cannot be, and the mapping is the
    /// part that decides what the user is told.
    /// </para>
    /// </summary>
    public static Plot3DFailure ToFailure(VulkanCapability capability) => capability switch
    {
        VulkanCapability.LoaderUnavailable => Plot3DFailure.NotSupported,
        VulkanCapability.NoDevice13 => Plot3DFailure.RuntimeError,
        _ => Plot3DFailure.None,
    };
}
