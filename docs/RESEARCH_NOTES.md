# 研究记录

## 2026-08-02：WPF-UI 4.3.0 的 NuGet 标识

### 现象与结论

主规格原写作 `Wpf.Ui 4.3.0`，NuGet restore 无法找到该版本。官方组件的 NuGet package ID 实际是 `WPF-UI`，而 `Wpf.Ui` 是其 C# 根命名空间；点号形式的 `Wpf.Ui`/`WPF.UI` 是无关包且不存在 4.3.0。

### 修正

中央包管理和 App 项目统一使用 `WPF-UI` 4.3.0，并在主规格技术栈表中注明命名空间。决策记录见 `docs/ADR/0001-wpf-ui-package-id.md`。修正后 Windows .NET SDK 10.0.302 能正常 restore、build 和启动窗口。

## 2026-08-02：Vortice.DXGI 3.8.3 的显存字段转换

### 现象

在用户当前 Windows 11 x64 机器上读取 RX 6650 XT（8 GiB）时，直接把 `AdapterDescription1.DedicatedVideoMemory` 转为 `long` 会抛出 `OverflowException`。异常栈进入 `System.UIntPtr.ToUInt32()`，看起来像 DXGI 返回了异常的大数值，但实际并非驱动数据错误。

### 原因

Vortice.DXGI 3.8.3 使用 SharpGen 的 `PointerUSize` 表示 DXGI `SIZE_T`。该类型同时定义了到 `uint` 和 `ulong` 的隐式转换。表达式 `(long)pointerUSize` 选择了先转 `uint` 的路径，8 GiB 超过 `uint.MaxValue`，因此溢出。

### 可重复修正

先显式转换为 `ulong`，再以 checked 方式转为领域模型要求的 `long`：

```csharp
checked((long)(ulong)description.DedicatedVideoMemory)
```

同样处理 `SharedSystemMemory`。修正后 DXGI 输出 RX 6650 XT 专用显存约 8 GiB（实际枚举为 7.96 GiB）。

## 2026-08-02：当前机器出现重复的核显 SpecificAdapter key

### 可重复结果

`IDXGIFactory1.EnumAdapters1` 在当前机器返回四条记录：

- RX 6650 XT 一条；
- `AMD Radeon(TM) Graphics` 两条，两者 LUID 不同；
- Microsoft Basic Render Driver 一条，带 DXGI `Software` 标志。

两条核显记录的 Vendor、Device、SubSys 均为 `1002 / 164E / 164E1002`，因此都生成 `1002&164E&164E1002`。当前 DXGI 描述中没有返回 `GameViewer Virtual Display Adapter` 这个名称，两条记录也都没有 `Software` 或 `Remote` 标志。

### 当前处理

严格执行主规格的重复 key 规则：两条核显记录均标记为 `Ambiguous` 和不可精确分配，不根据名称、枚举顺序或 GPU 编号猜测真实核显。RX 6650 XT 仍能由全局高性能 key 唯一匹配。

### 后续研究项

M3 之前不需要解决此问题。进入需要实际写入的里程碑前，应研究用 PNP/DisplayConfig/SetupAPI 或可由 LUID 查询的设备实例信息，把物理核显和虚拟显示路径可靠关联；未建立可重复关联前，保持安全禁用。

### 0.3.1 后续实验结果

使用 `GetDisplayConfigBufferSizes`、`QueryDisplayConfig(QDC_ALL_PATHS)` 和 `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_ADAPTER_NAME)`，可以把 DXGI LUID 可靠映射到设备接口路径。将接口路径转换为设备实例路径后，只读查询 `HKLM\SYSTEM\CurrentControlSet\Enum` 的 `FriendlyName/DeviceDesc`，得到：

```text
LUID 00000000:000165BC -> PCI\... -> AMD Radeon(TM) Graphics
LUID 00000000:00021322 -> ROOT\DISPLAY\0000 -> GameViewer Virtual Display Adapter
```

因此界面现在显示设备管理器名称，并用 `ROOT\DISPLAY` 身份将 GameViewer 标记为虚拟和 `Excluded`，没有硬编码产品名称。由于两个 DXGI 描述生成的 SpecificAdapter key 仍相同，精确指定物理核显继续保持安全禁用。

## 2026-08-02：Alt+Tab 后窗口背景变黑

### 原因与修正

应用资源字典和控件使用完整 WPF-UI 主题，但 `SystemThemeWatcher` 会在部分窗口消息后单独更新 FluentWindow 背景，出现黑色背景与浅色控件混用。移除运行期 watcher，保留启动时由 `ApplicationThemeManager` 一次性应用 System/Light/Dark。

修正后自动发送三轮 Alt+Tab，窗口背景、导航、表格和控件仍保持同一浅色主题。
