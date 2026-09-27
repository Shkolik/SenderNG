using System.Diagnostics;
using System.Reflection;

namespace SenderNG.Modules.Plotter.Nex.Rendering;

/// <summary>
/// Reflection-based diagnostics for VulkanImmediateCommands pool state.
/// Reads _freeStack.Count, _inFlightQueue.Count and per-buffer states
/// without depending on any public API.
/// </summary>
internal static class PoolDiagnostics
{
    public static void LogLine(string line)
    {
        // Every writer serializes here. LogLine is reached from the pool thread
        // (a failed scene build logs its verdict) as well as from the UI thread
        // (the render tick and Initialize), and StreamWriter is not thread-safe:
        // concurrent WriteLine calls can interleave a line or throw. The lock is
        // re-entrant, so the Log getter locking again below is safe.
        lock (LogLock)
        {
            try
            {
                Log.WriteLine(line);
            }
            catch
            {
            }
        }
    }

    private static Type? _vulkanContextType;
    private static Type? _commandsType;
    private static FieldInfo? _immediateField;
    private static FieldInfo? _uploadImmediateField;
    private static FieldInfo? _freeStackField;
    private static FieldInfo? _inFlightField;
    private static FieldInfo? _buffersField;
    private static FieldInfo? _maxBuffersField;
    private static PropertyInfo? _hasDedicatedTransferQueue;
    private static PropertyInfo? _uploadImmediateProp;
    private static PropertyInfo? _deviceQueuesProp;
    private static MethodInfo? _freeStackCount;
    private static MethodInfo? _inFlightCount;
    private static MethodInfo? _bufferAvailable;
    private static MethodInfo? _bufferEncoding;
    private static bool _initialized;
    private static bool _topologyLogged;
    private static StreamWriter? _log;
    private static readonly object LogLock = new();

    private static StreamWriter Log
    {
        get
        {
            // Locked as well as LogLine, because this is a plain null check and
            // two threads reaching it together would each open the file - and
            // since the writer appends: false, the second would truncate what the
            // first had just written and leak its handle.
            lock (LogLock)
            {
                if (_log is null)
                {
                    var path = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "SenderNG", "pool_diag.log");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    _log = new StreamWriter(path, append: false) { AutoFlush = true };
                }
                return _log;
            }
        }
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        var vulkanAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "HelixToolkit.Nex.Graphics.Vulkan");
        if (vulkanAsm is null) { Trace.WriteLine("[PoolDiag] Vulkan assembly not found"); return; }

        _vulkanContextType = vulkanAsm.GetType("HelixToolkit.Nex.Graphics.Vulkan.VulkanContext");
        _commandsType = vulkanAsm.GetType("HelixToolkit.Nex.Graphics.Vulkan.VulkanImmediateCommands");

        if (_vulkanContextType is null || _commandsType is null)
        {
            Trace.WriteLine($"[PoolDiag] Types not found: ctx={_vulkanContextType is not null} cmd={_commandsType is not null}");
            return;
        }

        _immediateField = _vulkanContextType.GetField("_immediate",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _uploadImmediateField = _vulkanContextType.GetField("_uploadImmediate",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _uploadImmediateProp = _vulkanContextType.GetProperty("UploadImmediate",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _hasDedicatedTransferQueue = _vulkanContextType.GetProperty("HasDedicatedTransferQueue",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _deviceQueuesProp = _vulkanContextType.GetProperty("DeviceQueues",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        _freeStackField = _commandsType.GetField("_freeStack",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _inFlightField = _commandsType.GetField("_inFlightQueue",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _buffersField = _commandsType.GetField("_buffers",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _maxBuffersField = _commandsType.GetField("_maxCommandBuffers",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Try multiple names for the collection count methods
        _freeStackCount = _freeStackField?.FieldType.GetMethod("get_Count")
            ?? _freeStackField?.FieldType.GetProperty("Count")?.GetGetMethod();
        _inFlightCount = _inFlightField?.FieldType.GetMethod("get_Count")
            ?? _inFlightField?.FieldType.GetProperty("Count")?.GetGetMethod();

        Trace.WriteLine($"[PoolDiag] init: immediate={_immediateField is not null} freeStack={_freeStackField is not null} inFlight={_inFlightField is not null} buffers={_buffersField is not null}");

        // Find CommandBuffer type and its properties
        if (_buffersField is not null)
        {
            var cbType = _buffersField.FieldType.GetElementType();
            _bufferAvailable = cbType?.GetMethod("get_IsAvailable",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _bufferEncoding = cbType?.GetMethod("get_IsEncoding",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Trace.WriteLine($"[PoolDiag] cbType={cbType?.Name} available={_bufferAvailable is not null} encoding={_bufferEncoding is not null}");
        }
    }

    /// <summary>
    /// One-shot dump of the queue topology that decides whether uploads are ordered
    /// with renders: HasDedicatedTransferQueue (if true, async uploads use a separate
    /// queue with no ordering vs the graphics queue), and whether the context created a
    /// distinct _uploadImmediate instance or falls back to _immediate.
    /// </summary>
    public static void LogQueueTopologyOnce(object? context)
    {
        if (_topologyLogged) return;
        _topologyLogged = true;
        LogLine("[queue] " + QueueTopology(context));
    }

    /// <summary>Reflection string describing the context's queue topology (no I/O).</summary>
    public static string QueueTopology(object? context)
    {
        try
        {
            Initialize();
            if (context is null || _vulkanContextType is null)
            {
                return "context not available";
            }

            var sb = new System.Text.StringBuilder();

            var hasDedicated = _hasDedicatedTransferQueue?.GetValue(context);
            sb.Append($"hasDedicatedTransferQueue={hasDedicated}");

            if (_deviceQueuesProp is not null)
            {
                var dq = _deviceQueuesProp.GetValue(context);
                if (dq is not null)
                {
                    var g = dq.GetType()
                        .GetField("GraphicsQueueFamilyIndex",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(dq);
                    var t = dq.GetType()
                        .GetField("TransferQueueFamilyIndex",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(dq);
                    var invalidToken = dq.GetType()
                        .GetField("INVALID",
                            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null)
                        ?? HxInvalid;
                    sb.Append($" graphicsFamily={g} transferFamily={t} invalid={invalidToken}");
                }
            }

            var upload = _uploadImmediateField?.GetValue(context);
            var uploadAlloc = upload is not null
                ? $"{upload.GetType().Name}"
                : "(null -> falls back to _immediate)";
            sb.Append($" uploadImmediate={uploadAlloc}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"probe error: {ex.Message}";
        }
    }

    private static readonly uint HxInvalid = 0xFFFFFFFF;

    public static string Dump(object? context, string label)
    {
        try
        {
            Initialize();
            LogQueueTopologyOnce(context);

            if (_immediateField is null || _commandsType is null)
            {
                var msg = $"[{label}] not initialized";
                LogLine(msg);
                return msg;
            }

            var renderDump = PoolState(context, _immediateField, label);
            var uploadDump = PoolState(context, _uploadImmediateField, $"{label}.upload");
            var line = $"{renderDump} | {uploadDump} | pending={StagingRegions(context)}{TransferQueueInfo(context)}";
            LogLine(line);
            return line;
        }
        catch (Exception ex)
        {
            var msg = $"[{label}] diag error: {ex.Message}";
            LogLine(msg);
            return msg;
        }
    }

    /// <summary>Pool state (free/in-flight/buffer availability) of one of the context's
    /// VulkanImmediateCommands instances, addressed by reflection field.</summary>
    private static string PoolState(object? context, FieldInfo? instanceField, string label)
    {
        try
        {
            if (instanceField is null)
            {
                return $"[{label}] n/a";
            }

            var immediate = instanceField.GetValue(context);
            if (immediate is null)
            {
                return $"[{label}] (null -> shared instance)";
            }

            var freeStack = _freeStackField?.GetValue(immediate);
            var inFlight = _inFlightField?.GetValue(immediate);
            var buffers = _buffersField?.GetValue(immediate) as Array;
            var maxBuf = _maxBuffersField?.GetValue(immediate);

            var freeCount = _freeStackCount?.Invoke(freeStack, null);
            var inFlightCount = _inFlightCount?.Invoke(inFlight, null);

            var sb = new System.Text.StringBuilder();
            sb.Append($"[{label}] free={freeCount} inFlight={inFlightCount} max={maxBuf}");

            if (buffers is not null && _bufferAvailable is not null && _bufferEncoding is not null)
            {
                var avail = 0;
                var enc = 0;
                var total = buffers.Length;
                for (var i = 0; i < total; i++)
                {
                    var buf = buffers.GetValue(i);
                    if (buf is null) continue;
                    var isAvail = (bool)_bufferAvailable.Invoke(buf, null)!;
                    var isEnc = (bool)_bufferEncoding.Invoke(buf, null)!;
                    if (isAvail) avail++;
                    if (isEnc) enc++;
                }
                sb.Append($" bufs={total} avail={avail} enc={enc}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[{label}] pool error: {ex.Message}";
        }
    }

    /// <summary>Whether the async transfer-queue path is ever engaged: reads the transfer
    /// queue's pending-request collection count (BlockingCollection) + the staging device's
    /// outstanding regions (uploads staged but not yet recycled).</summary>
    private static string TransferQueueInfo(object? context)
    {
        try
        {
            if (context is null || _vulkanContextType is null)
            {
                return " | tq=n/a";
            }
            var tqField = _vulkanContextType.GetField("_transferQueue",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (tqField is null)
            {
                return " | tq=max?";
            }
            var tq = tqField.GetValue(context);
            if (tq is null)
            {
                return " | tq=null";
            }
            var queueField = tq.GetType().GetField("_uploadQueue",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var pending = queueField?.FieldType.GetProperty("Count")?.GetGetMethod()
                ?.Invoke(queueField.GetValue(tq), null);
            return $" | tqPending={pending}";
        }
        catch
        {
            return " | tq=err";
        }
    }

    /// <summary>Number of outstanding staging regions on the context's staging device
    /// (uploads staged but not yet recycled), read via reflection.</summary>
    private static int StagingRegions(object? context)
    {
        try
        {
            if (context is null || _vulkanContextType is null)
            {
                return -1;
            }
            var stagingField = _vulkanContextType.GetField("_stagingDevice",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (stagingField is null)
            {
                return -2;
            }
            var staging = stagingField.GetValue(context);
            if (staging is null)
            {
                return -3;
            }
            var regionsField = staging.GetType().GetField("_regions",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (regionsField is null)
            {
                return -4;
            }
            var regions = regionsField.GetValue(staging);
            var count = regions?.GetType().GetProperty("Count")?.GetGetMethod()?.Invoke(regions, null)
                ?? (object?)regions?.GetType().GetMethod("get_Count")?.Invoke(regions, null);
            return count is int i ? i : -5;
        }
        catch
        {
            return -6;
        }
    }
}
