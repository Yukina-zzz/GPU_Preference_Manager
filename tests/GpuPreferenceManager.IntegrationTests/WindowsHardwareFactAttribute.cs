using Xunit;

namespace GpuPreferenceManager.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class WindowsHardwareFactAttribute : FactAttribute
{
    public WindowsHardwareFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GPM_RUN_WINDOWS_HARDWARE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "设置 GPM_RUN_WINDOWS_HARDWARE_TESTS=1 后运行真实 Windows 硬件测试。";
        }
    }
}
