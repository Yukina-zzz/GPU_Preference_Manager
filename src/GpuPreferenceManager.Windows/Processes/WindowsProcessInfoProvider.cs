using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GpuPreferenceManager.Core.Metrics;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace GpuPreferenceManager.Windows.Processes;

public sealed class WindowsProcessInfoProvider : IProcessInfoProvider
{
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly TimeSpan ProcessCacheLifetime = TimeSpan.FromSeconds(3);
    private readonly object _cacheGate = new();
    private DateTimeOffset _cacheTime;
    private List<ProcessInfoSnapshot> _cachedProcesses = [];

    public ValueTask<ProcessInfoSnapshot> GetAsync(int processId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int? parentProcessId = EnumerateProcessEntries()
            .FirstOrDefault(entry => entry.ProcessId == processId).ParentProcessId;
        bool hasVisibleWindow = EnumerateVisibleProcessIds().Contains(processId);
        return ValueTask.FromResult(ReadProcess(processId, parentProcessId, hasVisibleWindow));
    }

    public ValueTask<IReadOnlyList<ProcessInfoSnapshot>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheGate)
        {
            if (_cachedProcesses.Count > 0 && DateTimeOffset.UtcNow - _cacheTime < ProcessCacheLifetime)
            {
                return ValueTask.FromResult<IReadOnlyList<ProcessInfoSnapshot>>(_cachedProcesses);
            }
        }

        HashSet<int> visibleProcessIds = EnumerateVisibleProcessIds();
        List<ProcessInfoSnapshot> snapshots = [];
        foreach ((int processId, int? parentProcessId) in EnumerateProcessEntries())
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshots.Add(ReadProcess(processId, parentProcessId, visibleProcessIds.Contains(processId)));
        }

        lock (_cacheGate)
        {
            _cachedProcesses = snapshots;
            _cacheTime = DateTimeOffset.UtcNow;
            return ValueTask.FromResult<IReadOnlyList<ProcessInfoSnapshot>>(_cachedProcesses);
        }
    }

    private static ProcessInfoSnapshot ReadProcess(int processId, int? parentProcessId, bool hasVisibleWindow)
    {
        string processName = TryGetProcessName(processId);
        using SafeFileHandle handle = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            checked((uint)processId));
        if (handle.IsInvalid)
        {
            return new ProcessInfoSnapshot(
                new(processId, 0),
                processName,
                null,
                null,
                true,
                parentProcessId,
                hasVisibleWindow);
        }

        string? executablePath = QueryPath(handle);
        long creationTime = QueryCreationTime(handle);
        string? description = executablePath is null ? null : TryGetDescription(executablePath);
        return new ProcessInfoSnapshot(
            new(processId, creationTime),
            processName,
            executablePath,
            description,
            executablePath is null,
            parentProcessId,
            hasVisibleWindow);
    }

    private static HashSet<int> EnumerateVisibleProcessIds()
    {
        HashSet<int> processIds = [];
        EnumWindows((window, unused) =>
        {
            if (!IsWindowVisible(window) || IsWindowCloaked(window))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(window, out uint processId);
            if (processId > 0)
            {
                processIds.Add(checked((int)processId));
            }

            return true;
        }, 0);
        return processIds;
    }

    private static bool IsWindowCloaked(nint window)
    {
        const uint dwmwaCloaked = 14;
        return DwmGetWindowAttribute(window, dwmwaCloaked, out uint cloaked, sizeof(uint)) == 0
            && cloaked != 0;
    }

    private static List<(int ProcessId, int? ParentProcessId)> EnumerateProcessEntries()
    {
        nint snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == -1)
        {
            return [];
        }

        try
        {
            ProcessEntry32 entry = new() { Size = checked((uint)Marshal.SizeOf<ProcessEntry32>()) };
            if (!Process32First(snapshot, ref entry))
            {
                return [];
            }

            List<(int ProcessId, int? ParentProcessId)> result = [];
            do
            {
                if (entry.ProcessId > 0)
                {
                    result.Add((
                        checked((int)entry.ProcessId),
                        entry.ParentProcessId == 0 ? null : checked((int)entry.ParentProcessId)));
                }

                entry.Size = checked((uint)Marshal.SizeOf<ProcessEntry32>());
            }
            while (Process32Next(snapshot, ref entry));

            return result;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    private static string? QueryPath(SafeFileHandle handle)
    {
        char[] buffer = new char[32_768];
        uint size = checked((uint)buffer.Length);
        return PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
            ? new string(buffer, 0, checked((int)size))
            : null;
    }

    private static long QueryCreationTime(SafeFileHandle handle)
    {
        if (!PInvoke.GetProcessTimes(handle, out var creation, out _, out _, out _))
        {
            return 0;
        }

        return ((long)(uint)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
    }

    private static string TryGetProcessName(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return $"PID {processId}";
        }
    }

    private static string? TryGetDescription(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (Exception exception) when (exception is FileNotFoundException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, uint attribute, out uint value, int size);
}
