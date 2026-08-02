using CommunityToolkit.Mvvm.ComponentModel;
using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.App.ViewModels;

public sealed record AdapterRoleChoice(AdapterOverrideRole Value, string Label);

public sealed class AdapterCardViewModel : ObservableObject
{
    private readonly Func<AdapterCardViewModel, AdapterOverrideRole, Task> _roleChanged;
    private AdapterOverrideRole _selectedRole;
    private bool _isApplyingRole;

    public AdapterCardViewModel(
        GpuAdapterInfo source,
        Func<AdapterCardViewModel, AdapterOverrideRole, Task> roleChanged)
    {
        Source = source;
        _roleChanged = roleChanged;
        _selectedRole = source.RoleSource == AdapterRoleSource.UserOverride
            ? source.Role == GpuAdapterRole.DiscreteOrHighPerformance
                ? AdapterOverrideRole.DiscreteOrHighPerformance
                : AdapterOverrideRole.IntegratedOrPowerSaving
            : AdapterOverrideRole.Automatic;
    }

    public static IReadOnlyList<AdapterRoleChoice> RoleChoices { get; } =
    [
        new(AdapterOverrideRole.Automatic, "自动判断"),
        new(AdapterOverrideRole.DiscreteOrHighPerformance, "高性能 GPU"),
        new(AdapterOverrideRole.IntegratedOrPowerSaving, "节能 GPU"),
    ];

    public GpuAdapterInfo Source { get; }

    public string Name => Source.Name;

    public string Type => Source.IsVirtual
        ? "虚拟显示适配器"
        : Source.IsSoftware
            ? "软件渲染适配器"
            : Source.IsRemote
                ? "远程显示适配器"
                : Source.Role == GpuAdapterRole.DiscreteOrHighPerformance
                    ? "高性能 GPU"
                    : Source.Role == GpuAdapterRole.IntegratedOrPowerSaving
                        ? "节能 GPU"
                        : "图形适配器";

    public string RoleSource => Source.RoleSource == AdapterRoleSource.UserOverride
        ? "角色来源：本软件手动指定"
        : Source.Role is GpuAdapterRole.DiscreteOrHighPerformance or GpuAdapterRole.IntegratedOrPowerSaving
            ? "角色来源：自动判断"
            : "角色来源：尚未确定";

    public string Memory => $"专用显存 {FormatBytes(Source.DedicatedVideoMemoryBytes)}  ·  共享内存上限 {FormatBytes(Source.SharedSystemMemoryBytes)}";

    public string SpecificAdapter => Source.SpecificAdapterKey.Replace("&", "  /  ", StringComparison.Ordinal);

    public string Assignment => Source.IsAssignable
        ? Source.ExclusionMode == AdapterExclusionMode.ForceIncluded
            ? "已强制恢复，可以作为精确 GPU 偏好目标"
            : "可以作为精确 GPU 偏好目标"
        : Source.IdentityConfidence == AdapterIdentityConfidence.Ambiguous
            ? "不可精确指定：SpecificAdapter 身份与另一适配器重复"
            : Source.AutomaticExclusionReasons.HasFlag(AdapterAutomaticExclusionReason.MissingIdentity)
                ? "不可精确指定：缺少有效的 PCI 设备身份"
                : "不参与 GPU 偏好分配";

    public string DevicePath => Source.DeviceInstancePath ?? "Windows 未提供设备实例路径";

    public string ExclusionReason => Source.ExclusionMode == AdapterExclusionMode.Excluded
        ? $"手动排除{FormatAutomaticReasons("；自动规则同时识别为：")}"
        : $"自动排除：{FormatAutomaticReasons(string.Empty).TrimStart('；', ' ')}";

    public bool IsAssignable => Source.IsAssignable;

    public bool IsExcluded => Source.Role == GpuAdapterRole.Excluded;

    public bool IsManuallyExcluded => Source.ExclusionMode == AdapterExclusionMode.Excluded;

    public bool IsForceIncluded => Source.ExclusionMode == AdapterExclusionMode.ForceIncluded;

    public bool CanCustomize => Source.CanCustomize;

    public bool CanSetRole => Source.CanCustomize && Source.IsAssignable && !IsExcluded;

    public bool CanExclude => Source.CanCustomize && !IsExcluded;

    public bool CanRestore => Source.CanCustomize && IsExcluded;

    public string RestoreText => IsManuallyExcluded ? "恢复" : "强制恢复";

    public string AssignmentBadge => Source.IsAssignable ? "可精确分配" : "不可精确分配";

    public AdapterOverrideRole SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (_selectedRole == value || _isApplyingRole)
            {
                return;
            }

            AdapterOverrideRole previous = _selectedRole;
            _selectedRole = value;
            OnPropertyChanged();
            _ = ApplyRoleAsync(previous, value);
        }
    }

    private async Task ApplyRoleAsync(AdapterOverrideRole previous, AdapterOverrideRole value)
    {
        _isApplyingRole = true;
        try
        {
            await _roleChanged(this, value);
        }
        catch
        {
            _selectedRole = previous;
            OnPropertyChanged(nameof(SelectedRole));
        }
        finally
        {
            _isApplyingRole = false;
        }
    }

    private string FormatAutomaticReasons(string prefix)
    {
        List<string> reasons = [];
        AdapterAutomaticExclusionReason value = Source.AutomaticExclusionReasons;
        if (value.HasFlag(AdapterAutomaticExclusionReason.Virtual)) reasons.Add("虚拟显示适配器");
        if (value.HasFlag(AdapterAutomaticExclusionReason.Software)) reasons.Add("软件渲染适配器");
        if (value.HasFlag(AdapterAutomaticExclusionReason.Remote)) reasons.Add("远程显示适配器");
        if (value.HasFlag(AdapterAutomaticExclusionReason.MissingIdentity)) reasons.Add("缺少有效设备身份");
        if (value.HasFlag(AdapterAutomaticExclusionReason.NoUsableMemory)) reasons.Add("未报告可用显存");
        return reasons.Count == 0 ? string.Empty : prefix + string.Join("、", reasons);
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):F2} GiB"
        : $"{bytes / (1024d * 1024):F1} MiB";
}
