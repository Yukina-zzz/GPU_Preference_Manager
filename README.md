# GPU Preference Manager

这个项目最初是为“混合输出”场景做的：比如显示器分别接在核显和独显上时，可以集中查看每个程序实际在用哪张 GPU，占用多少显存，并直接调整它的 GPU 偏好，不必每次都进入 Windows 设置，手动寻找 EXE、逐个添加再修改。

GPU Preference Manager 是面向 Windows 11 混合显卡环境的 WPF 桌面工具。它把实时 GPU 占用、应用偏好、批量修改、历史记录和可恢复备份放在同一界面中。

## AI 创作声明

> [!IMPORTANT]
> **本项目几乎完全由 AI 创作（Almost entirely AI-created）。** 项目的架构、代码、测试、界面和文档主要由 OpenAI Codex 根据用户提出的需求与真实机器反馈生成和迭代；用户负责产品方向、体验反馈及最终验收。本项目尚未经过独立的专业安全审计，使用注册表写入功能前请自行评估风险并保留备份。

> 本工具管理的是 Windows GPU **偏好**，不是驱动级强制规则。应用仍可能自行枚举适配器、同时使用多张 GPU，或忽略系统偏好。修改后应重启目标程序并结合任务管理器核对实际 GPU 活动。

## 主要能力

- 通过 DXGI 枚举适配器、LUID 和真实专用显存；
- 按应用进程树归组多种 EXE、多个进程、多物理分区和多 GPU 的 PDH 数据；
- 待处理、已指定、全部占用、忽略、异常、历史、适配器和设置视图；
- 批量设置特定适配器、通用节能、通用高性能或清除偏好；
- 支持单个程序右键快捷设置，多 EXE 应用会明确选择实际写入目标；
- 可在本软件内手动修正高性能/节能 GPU 角色，管理适配器排除与恢复，不改写 Windows 全局角色；
- 写前 UTF-16 `.reg` 备份、写后精确读取校验、失败补偿；
- 首次 baseline、事务历史、撤销、冲突检测、回滚到节点和恢复 baseline；
- 日志、设置、窗口状态、图标缓存和诊断 ZIP；诊断包默认不包含完整 SQLite 数据库。

## 安全边界

- 程序只管理当前用户 `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`；
- `DirectXUserGlobalSettings` 在首版保持只读；
- 自动测试的写操作仅针对 `HKCU\Software\GpuPreferenceManager.Tests\<Guid>`；
- 适配器身份有歧义时禁用精确指定，不按名称或 GPU 0/1 猜测；
- 不会自动结束或重启目标应用；
- 数据和备份位于 `%LocalAppData%\GpuPreferenceManager`，不上传遥测。

## 构建和运行

要求 Windows 11 x64 与 .NET SDK 10.0.302 或兼容的 10.0.x SDK。

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet run --project .\src\GpuPreferenceManager.App
```

底层诊断命令：

```powershell
dotnet run --project .\tools\GpuProbe -- registry
dotnet run --project .\tools\GpuProbe -- adapters
dotnet run --project .\tools\GpuProbe -- sample --seconds 30
dotnet run --project .\tools\GpuProbe -- inventory --seconds 10
```

显式运行真实硬件集成测试：

```powershell
$env:GPM_RUN_WINDOWS_HARDWARE_TESTS = '1'
dotnet test -c Release --no-build
```

## 发布

最新正式版本可从 [GitHub Releases](https://github.com/Yukina-zzz/GPU_Preference_Manager/releases/latest) 下载。

```powershell
.\scripts\publish.ps1
```

脚本生成 `artifacts\release\GpuPreferenceManager-0.9.0-win-x64-portable.zip` 和 `GpuPreferenceManager-0.9.0-win-x64-single.exe`。两者均为自包含 win-x64 发布且关闭裁剪。

## 项目文档

- [实现状态](docs/IMPLEMENTATION_STATUS.md)
- [注册表格式](docs/REGISTRY_FORMAT.md)
- [GPU 计数器说明](docs/GPU_COUNTERS.md)
- [技术说明](docs/RESEARCH_NOTES.md)

## 当前版本

版本为 0.9.0。显卡角色和排除状态现在可以在本软件中手动修正；程序列表支持按具体 EXE 右键快捷设置，右键操作期间不会被采样刷新打断。右侧详情改为分区卡片布局，GPU、偏好、进程和采样信息更紧凑。适配器仍只在程序启动时枚举。
