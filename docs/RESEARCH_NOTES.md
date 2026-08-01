# 技术说明

本文件只保留对当前实现仍有约束力的技术结论，不记录已失效的开发计划和版本流水账。

## WPF UI 包标识

官方组件的 NuGet package ID 是 `WPF-UI`，C# 根命名空间是 `Wpf.Ui`。项目固定使用 `WPF-UI 4.3.0`，详细决策见 `docs/ADR/0001-wpf-ui-package-id.md`。

## Vortice.DXGI 显存字段

Vortice.DXGI 3.8.3 使用 SharpGen `PointerUSize` 表示 DXGI `SIZE_T`。直接转换为 `long` 可能先经过 `uint`，在 4 GiB 以上显存设备上溢出。当前实现先转为 `ulong`，再进行 checked `long` 转换：

```csharp
checked((long)(ulong)description.DedicatedVideoMemory)
```

`SharedSystemMemory` 使用同样的转换方式。

## 重复 SpecificAdapter key 与虚拟显示器

Windows 的 `SpecificAdapter` key 只包含 Vendor、Device 和 SubSys，不能保证不同 LUID 唯一。当前机器的物理核显与 GameViewer 虚拟显示适配器会生成相同 key。

当前实现通过 DisplayConfig 将 LUID 关联到设备实例路径，并根据 `ROOT\DISPLAY`、DXGI Software/Remote 等身份排除非物理适配器。歧义判断只比较仍可分配的物理适配器，因此虚拟显示器不会阻止物理核显分配；如果多个物理适配器仍产生相同 key，则继续禁用精确指定，不按名称或 GPU 编号猜测。

## PDH 数组读取

`PdhGetFormattedCounterArray` 的实例数可能在查询所需缓冲区与读取数据之间变化。第二次调用再次返回 `PDH_MORE_DATA` 时，当前实现会按新的大小重新分配并有限重试，避免采样线程因短暂的进程变化退出。

## 窗口主题

应用只在启动时应用 System/Light/Dark 主题，不启用运行期 `SystemThemeWatcher`。这是为了避免部分 Alt+Tab 或窗口消息之后只更新窗口背景、造成深浅色资源混用。
