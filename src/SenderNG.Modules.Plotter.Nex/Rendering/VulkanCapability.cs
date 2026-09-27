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
    NoUsableVulkan,

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
/// the shipped <c>HelixToolkit.Nex 1.3.0-alpha.41</c>, <c>ResultCode.CheckResult()</c>
/// logs the error and returns the code unchanged - it never throws. That is not
/// a Release-compilation detail: <c>CheckResult</c> calls <c>HxDebug.Assert(false)</c>,
/// which logs unconditionally and escalates to <c>Debug.Assert</c> only when
/// <c>HxDebug.EnableDebugAssertions</c> is true. That flag is a public property
/// defaulting to <c>false</c>, raised only by <c>HxDebug</c>'s static constructor
/// under <c>#if DEBUG</c> in the engine's own build - and a NuGet package is a
/// Release build. Verified by running it: <c>CheckResult()</c> on
/// <c>NotSupported</c> returns <c>NotSupported</c> without throwing, with
/// <c>EnableDebugAssertions == false</c>.
/// </para>
/// <para>
/// The call site is not compiled out, either. <c>VulkanContext.InitContext()</c>
/// discards the return values of <c>InitPhysicalDevice()</c> and
/// <c>InitDevice()</c> and returns <c>ResultCode.Ok</c> unconditionally, so a
/// machine whose GPU predates Vulkan 1.3 gets <c>Ok</c> back and a silently
/// broken viewport. The engine's own "does not support Vulkan 1.3 or higher"
/// check does run - its verdict is logged and then discarded.
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
    /// <summary>How many devices we are willing to look at. No real machine
    /// reports more than a handful; the array is heap-allocated and the count
    /// clamped to this, so a driver reporting an absurd count costs an ordinary
    /// (catchable) allocation failure rather than an uncatchable stack overflow.
    /// <para>
    /// The cost of clamping is that on such a machine only the first
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
        VulkanCapability.NoUsableVulkan => ResultCode.NotSupported,
        VulkanCapability.NoDevice13 => ResultCode.RuntimeError,
        VulkanCapability.Available => null,

        // Total on purpose, and this arm is the reason the function is total
        // rather than a compile error. The call site is the scene constructor,
        // outside its try/catch, so a SwitchExpressionException here would escape
        // the constructor entirely - the blank, unexplained tab that this whole
        // feature exists to prevent. An unnamed value cannot come from the
        // probe, which only ever returns the three above; it can only come from
        // a cast or a future enum member, and neither is a reason to crash.
        //
        // It maps to NotSupported because that is the verdict we already use
        // whenever we cannot demonstrate a working loader, so an unclassifiable
        // capability degrades to the same safe answer instead of inventing a
        // reason. The alternative - treating it as Available - would silently
        // claim the machine is fine on the strength of a value we do not
        // understand.
        _ => ResultCode.NotSupported,
    };

    /// <summary>Do the actual probe. Never throws: any unexpected managed
    /// failure degrades to <see cref="VulkanCapability.NoUsableVulkan"/>,
    /// because we cannot demonstrate a working loader in that case, and the
    /// fallback panel is a better outcome than an unhandled exception on the
    /// scene-construction thread pool. Note that a native access violation would
    /// <em>not</em> be caught here - see the comment on <c>vkLoadInstanceOnly</c>.
    /// <para>
    /// Nothing is logged on this path, and there is no cause to log: a capability
    /// is a plain enum, so the swallowed exception is genuinely lost. The view
    /// in Task 5 logs the <em>verdict</em> - the capability, and the reported
    /// code - when it reports the failure, which is the part that identifies
    /// the machine. A second line here would not add the missing detail, and
    /// this file has no logger of its own to add it with.
    /// </para>
    /// </summary>
    private static VulkanCapability Probe()
    {
        try
        {
            if (Vulkan.vkInitialize() != VkResult.Success)
                return VulkanCapability.NoUsableVulkan;

            return ProbeDevices();
        }
        catch (Exception)
        {
            return VulkanCapability.NoUsableVulkan;
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
    /// Every failure below is <see cref="VulkanCapability.NoUsableVulkan"/>,
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
            return VulkanCapability.NoUsableVulkan;

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
            // This first call passes no array, so it is a pure count query. It
            // can only return Success or an error - never Incomplete, which is
            // the one asymmetry with the second call below and the reason the
            // two are not written the same way.
            if (Vulkan.vkEnumeratePhysicalDevices(instance, &count, null) != VkResult.Success)
                return VulkanCapability.NoUsableVulkan;

            // Defensive. The spec says a successful query reports at least one
            // device, so this should be unreachable, but a zero-length
            // allocation would be legal and we would then fall through to
            // reporting NoDevice13 - "your driver is too old" - on a machine
            // that has no GPU at all. The one-word guard is cheaper than
            // trusting the spec about a driver bug.
            if (count == 0)
                return VulkanCapability.NoUsableVulkan;

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
                    return VulkanCapability.NoUsableVulkan;

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
