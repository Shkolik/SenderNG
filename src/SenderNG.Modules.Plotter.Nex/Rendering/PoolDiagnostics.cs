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
        try
        {
            Log.WriteLine(line);
        }
        catch
        {
        }
    }

    private static Type? _vulkanContextType;
    private static Type? _commandsType;
    private static FieldInfo? _immediateField;
    private static FieldInfo? _freeStackField;
    private static FieldInfo? _inFlightField;
    private static FieldInfo? _buffersField;
    private static FieldInfo? _maxBuffersField;
    private static MethodInfo? _freeStackCount;
    private static MethodInfo? _inFlightCount;
    private static MethodInfo? _bufferAvailable;
    private static MethodInfo? _bufferEncoding;
    private static bool _initialized;
    private static StreamWriter? _log;

    private static StreamWriter Log
    {
        get
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

    public static string Dump(object? context, string label)
    {
        try
        {
            Initialize();
            if (_immediateField is null || _commandsType is null)
            {
                var msg = $"[{label}] not initialized";
                Log.WriteLine(msg);
                return msg;
            }

            var immediate = _immediateField.GetValue(context);
            if (immediate is null)
            {
                var msg = $"[{label}] immediate is null";
                Log.WriteLine(msg);
                return msg;
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
                var submitted = 0;
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
                submitted = (int)inFlightCount! - (avail + enc > 0 ? 0 : 0); // just report
                sb.Append($" bufs={total} avail={avail} enc={enc}");
            }

            var line = sb.ToString();
            Log.WriteLine(line);
            return line;
        }
        catch (Exception ex)
        {
            var msg = $"[{label}] diag error: {ex.Message}";
            try { Log.WriteLine(msg); } catch { }
            return msg;
        }
    }
}
