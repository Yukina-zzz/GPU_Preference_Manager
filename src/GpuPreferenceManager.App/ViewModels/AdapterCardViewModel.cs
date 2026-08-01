using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.App.ViewModels;

public sealed class AdapterCardViewModel
{
    public AdapterCardViewModel(GpuAdapterInfo source) => Source = source;

    public GpuAdapterInfo Source { get; }

    public string Name => Source.Name;

    public string Type => Source.IsVirtual
        ? "虚拟显示适配器"
        : Source.IsSoftware
            ? "软件渲染适配器"
            : Source.Role == GpuAdapterRole.DiscreteOrHighPerformance
                ? "高性能 GPU"
                : Source.Role == GpuAdapterRole.IntegratedOrPowerSaving
                    ? "节能 GPU"
                    : "图形适配器";

    public string Memory => $"专用显存 {FormatBytes(Source.DedicatedVideoMemoryBytes)}  ·  共享内存上限 {FormatBytes(Source.SharedSystemMemoryBytes)}";

    public string SpecificAdapter => Source.SpecificAdapterKey.Replace("&", "  /  ", StringComparison.Ordinal);

    public string Assignment => Source.IsAssignable
        ? "可以作为精确 GPU 偏好目标"
        : Source.IdentityConfidence == AdapterIdentityConfidence.Ambiguous
            ? "不可精确指定：SpecificAdapter 身份与另一适配器重复"
            : "不参与 GPU 偏好分配";

    public string DevicePath => Source.DeviceInstancePath ?? "Windows 未提供设备实例路径";

    public bool IsAssignable => Source.IsAssignable;

    public string AssignmentBadge => Source.IsAssignable ? "可精确分配" : "不可精确分配";

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):F2} GiB"
        : $"{bytes / (1024d * 1024):F1} MiB";
}
