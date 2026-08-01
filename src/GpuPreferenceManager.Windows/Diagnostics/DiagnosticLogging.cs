using System.Globalization;
using Serilog;
using Serilog.Core;

namespace GpuPreferenceManager.Windows.Diagnostics;

/// <summary>
/// 应用与命令行工具共用的日志基础配置。
/// </summary>
public static class DiagnosticLogging
{
    /// <summary>
    /// 创建每日滚动的本地诊断日志记录器。
    /// </summary>
    public static Logger CreateLogger(string componentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentName);
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logDirectory = Path.Combine(localData, "GpuPreferenceManager", "Logs");
        Directory.CreateDirectory(logDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Component", componentName)
            .WriteTo.File(
                Path.Combine(logDirectory, componentName + "-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();
    }
}
