/*
 * VulkanCapability.cs - ask the Vulkan runtime what this machine can actually
 * do, before the engine is allowed to try. The engine cannot answer this
 * question itself (see the class remarks), so we ask it ourselves.
 */

using HelixToolkit.Nex;
using Vortice.Vulkan;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>What the Vulkan runtime on this machine can do, as far as Plot 3D
/// is concerned.</summary>
internal enum VulkanCapability
{
    /// <summary>No usable Vulkan: the loader is missing, fails to initialize,
    /// cannot create an instance, or reports no devices at all. The machine
    /// needs a Vulkan driver before anything else can work.</summary>
    LoaderUnavailable,

    /// <summary>Devices are present but none reports Vulkan 1.3. The machine has
    /// a working Vulkan stack; its GPU or driver is too old.</summary>
    NoDevice13,

    /// <summary>A Vulkan 1.3 device is present.</summary>
    Available,
}

/// <summary>Determines whether this machine can run Plot 3D, and says which of
/// the two failure reasons applies when it cannot.</summary>
/// <remarks>
/// <para>
/// This exists because the engine cannot be trusted to report the reason. In
/// the shipped <c>HelixToolkit.Nex 1.3.0-alpha.41</c> - a Release build, as NuGet
/// packages are - <c>ResultCode.CheckResult()</c> only logs on error, because its
/// assertion is compiled out (<c>HxDebug.EnableDebugAssertions</c> comes from
/// <c>#if DEBUG</c> <em>inside the engine assembly</em>). And
/// <c>VulkanContext.InitContext()</c> discards the return values of
/// <c>InitPhysicalDevice()</c> and <c>InitDevice()</c> before returning
/// <c>ResultCode.Ok</c> unconditionally. So a machine whose GPU predates Vulkan
/// 1.3 gets <c>Ok</c> back and a silently broken viewport: the engine's own
/// "does not support Vulkan 1.3 or higher" check does run, but its verdict is
/// logged and thrown away.
/// </para>
/// <para>
/// So the verdict is ours, and deliberately positive: we enumerate devices and
/// confirm at least one reports 1.3, rather than inferring capability from a
/// success that carries no information. Only <em>existence</em> is checked, never
/// ranking or selection - the engine still picks its own device, and we never
/// name or interrogate a specific GPU in anything the user sees. That keeps the
/// message honest without re-implementing <c>FindBestDevice</c>.
/// </para>
/// <para>
/// <b>Known residual gap.</b> The check is version-only. A device that reports
/// 1.3 but lacks the queue families or extensions the engine needs will still
/// pass here, and the engine will then return <c>Ok</c> for it. Closing that
/// would mean re-implementing device selection, which is explicitly out of
/// scope. The affected machines get a broken viewport rather than a message,
/// which is the pre-existing behaviour this feature improves on but does not
/// fully eliminate.
/// </para>
/// </remarks>
internal static class VulkanCapabilities
{
    /// <summary>No real machine reports more than a handful of Vulkan devices.
    /// A driver reporting an absurd count is not to be trusted with a
    /// stack-allocated array, so the count is clamped and the array is
    /// heap-allocated: an oversized count then costs an ordinary (catchable)
    /// allocation failure instead of an uncatchable stack overflow.
    /// <para>
    /// The residual cost of clamping is that on such a machine only the first
    /// <see cref="MaxDevices"/> devices are inspected, so a 1.3 device beyond
    /// that cut would be missed and reported as <see cref="VulkanCapability.NoDevice13"/>.
    /// That needs more than 64 GPUs to hit, and a driver that miscounts this
    /// badly is not a case worth failing closed on.
    /// </para>
    /// </summary>
    private const int MaxDevices = 64;

    /// <summary>Probed at most once per process, and only ever by one thread.
    /// <para>
    /// The <c>Lazy</c> is load-bearing, not an optimisation. Vortice resolves
    /// instance-level entry points into process-global state, so two threads
    /// probing at once race inside <c>vkLoadInstanceOnly</c> and the loser
    /// access-violates (0xC0000005) - an intermittent test-host crash, not a
    /// catchable exception. Single-execution publication makes the race
    /// impossible, and matches the fact that the answer cannot change while the
    /// process is alive.
    /// </para>
    /// </summary>
    private static readonly Lazy<VulkanCapability> _cached =
        new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Probe the Vulkan runtime on this machine. Safe to call from any
    /// thread, any number of times; the work happens once.</summary>
    public static VulkanCapability Detect() => _cached.Value;

    /// <summary>The result code to report for a capability, or <c>null</c> when
    /// the machine is fine.
    /// <para>
    /// Pure, and the single place a capability becomes a reported verdict - the
    /// scene constructor calls this rather than repeating the mapping, so the
    /// policy is exercised in production and not only by tests. It is kept
    /// separate from <see cref="Detect"/> because the Vulkan calls cannot be
    /// tested without a suitable GPU, while the thing that decides what the user
    /// is told can and should be.
    /// </para>
    /// </summary>
    public static ResultCode? VerdictFor(VulkanCapability capability) => capability switch
    {
        VulkanCapability.LoaderUnavailable => ResultCode.NotSupported,
        VulkanCapability.NoDevice13 => ResultCode.RuntimeError,
        VulkanCapability.Available => null,
    };

    /// <summary>Do the actual probe. Never throws: any unexpected managed
    /// failure degrades to <see cref="VulkanCapability.LoaderUnavailable"/>,
    /// because we cannot demonstrate a working loader in that case, and the
    /// fallback panel is a better outcome than an unhandled exception on the
    /// scene-construction thread pool. Note that a native access violation would
    /// <em>not</em> be caught here - see the comment on <c>vkLoadInstanceOnly</c>.
    /// <para>
    /// Nothing is logged on this path. The scene constructor's failure is
    /// reported and logged by the view in Task 5, which sees the code and the
    /// exception; a second log line here would only duplicate it.
    /// </para>
    /// </summary>
    private static VulkanCapability Probe()
    {
        try
        {
            if (Vulkan.vkInitialize() != VkResult.Success)
                return VulkanCapability.LoaderUnavailable;

            return ProbeDevices();
        }
        catch (Exception)
        {
            return VulkanCapability.LoaderUnavailable;
        }
    }

    /// <summary>Whether a 1.3 device exists, or why we cannot tell.
    /// <para>
    /// Enumerating needs a <see cref="VkInstance"/>, so a throwaway one is
    /// created and destroyed. An API-1.0 instance is enough:
    /// <c>vkEnumeratePhysicalDevices</c> and <c>vkGetPhysicalDeviceProperties</c>
    /// are both 1.0 entry points, and asking for 1.0 means the probe cannot
    /// itself fail for want of a newer loader. It does not hide newer devices:
    /// each device's own <c>apiVersion</c> is reported through
    /// <c>vkGetPhysicalDeviceProperties</c> regardless of the instance version
    /// that enumerated it.
    /// </para>
    /// <para>
    /// Every failure below is <see cref="VulkanCapability.LoaderUnavailable"/>,
    /// not <see cref="VulkanCapability.NoDevice13"/>. That distinction is the
    /// whole point: <c>vkCreateInstance</c> returning
    /// <c>ErrorIncompatibleDriver</c> means no installable driver is compatible,
    /// which is "there is no usable Vulkan here", not "your driver is too old".
    /// Reporting it as the latter would tell a driverless machine to go looking
    /// for an upgrade it does not need.
    /// </para>
    /// </summary>
    private static unsafe VulkanCapability ProbeDevices()
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
            return VulkanCapability.LoaderUnavailable;

        // Mandatory, and not optional politeness: Vortice dispatches
        // instance-level entry points through pointers it resolves at load time.
        // Without this call, vkEnumeratePhysicalDevices does not return an
        // error - it dereferences a null dispatch pointer and takes the process
        // down with an access violation (0xC0000005), which no try/catch in this
        // file can intercept. Found by running the probe, not by reading it.
        // (It returns void, so there is nothing to check - which is part of why
        // the access violation it prevents cannot be handled locally.)
        Vulkan.vkLoadInstanceOnly(instance);

        try
        {
            uint count = 0;
            if (Vulkan.vkEnumeratePhysicalDevices(instance, &count, null) != VkResult.Success)
                return VulkanCapability.LoaderUnavailable;

            // A machine with Vulkan but no devices at all has no driver to
            // upgrade, so this is the "no usable Vulkan" bucket too.
            if (count == 0)
                return VulkanCapability.LoaderUnavailable;

            if (count > MaxDevices)
                count = MaxDevices;

            var devices = new VkPhysicalDevice[count];
            fixed (VkPhysicalDevice* pDevices = devices)
            {
                // Incomplete, not Success, is the expected result of the clamp
                // above: we asked for room for fewer devices than the driver
                // claims, which is exactly how the API says "I have more". It
                // still filled the array, so the devices in hand are real and
                // worth inspecting - and if one of them is 1.3 we have our
                // answer. Treating Incomplete as a failure would misreport a
                // perfectly good machine as having no usable Vulkan, purely
                // because it had an implausible number of GPUs.
                var enumerated = Vulkan.vkEnumeratePhysicalDevices(instance, &count, pDevices);
                if (enumerated is not (VkResult.Success or VkResult.Incomplete))
                    return VulkanCapability.LoaderUnavailable;

                for (var i = 0; i < devices.Length; i++)
                {
                    Vulkan.vkGetPhysicalDeviceProperties(pDevices[i], out var properties);
                    if (properties.apiVersion >= new VkVersion(1, 3, 0))
                        return VulkanCapability.Available;
                }
            }

            return VulkanCapability.NoDevice13;
        }
        finally
        {
            // The probe runs at most once per process, but an undestroyed
            // instance is a real leak and the finally costs nothing.
            Vulkan.vkDestroyInstance(instance, null);
        }
    }
}
