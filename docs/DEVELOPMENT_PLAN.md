# GPU Preference Manager 完整开发计划

> 文档用途：交付给 Codex 中的 GPT-5.6 Sol 直接实施。
> 文档版本：1.0
> 编制日期：2026-08-02
> 目标平台：Windows 11 x64
> 首要语言：简体中文
> 计划代号：GPU Preference Manager（后续可另定正式中文名）

---

## 0. 给 Codex 的执行指令

请把本文档作为项目的主规格说明，而不是一次性参考材料。开发时遵守以下原则：

1. **按里程碑开发，不要一开始就写完整 GUI。** 先完成可独立运行的适配器枚举、注册表解析和 PDH 采样诊断程序，确认底层数据正确后再接入 WPF。
2. **每个里程碑都必须可编译、可运行、可测试。** 不允许为了赶进度留下会破坏主流程的占位实现、空方法或未经标注的硬编码。
3. **底层 Windows 接口必须隔离。** UI 不得直接读写注册表、调用 PDH 或枚举进程；所有系统操作通过接口和服务完成。
4. **注册表修改必须事务化记录。** 每次修改前记录完整旧值，修改后重新读取校验；失败时立即尝试补偿恢复。
5. **保留未知注册表字段。** 只修改 GPU 相关字段，不得无条件覆盖整条 `REG_SZ`。
6. **先实现用户当前机器的正确性，再保证通用性。** 用户当前适配器和既有规则是第一套验收样本，但代码中不得硬编码显卡名称或设备 ID。
7. **每完成一个里程碑，更新 `docs/IMPLEMENTATION_STATUS.md`。** 写明已完成、未完成、已知问题、验证命令和下一步。
8. **遇到文档与实际系统行为不一致时，以可重复实验为准。** 将实验结果写入 `docs/RESEARCH_NOTES.md`，不要静默绕过。
9. **不要替用户自动结束应用。** 写入规则后只提示重新启动目标程序；“结束并重启”最多作为后续可选功能。
10. **最终发布前必须在真实 Windows 11 机器上完成手工验收清单。** 单元测试不能替代实际 GPU 调度验证。

---

## 1. 项目背景

用户在 Windows 11 上使用混合输出模式：显示器连接主板，由核显承担桌面显示和一部分系统图形负载，RX 6650 XT 主要用于大型游戏渲染。该方式可把约 1 GiB 左右的桌面与后台程序显存占用从独显转移到核显，避免 8 GiB 独显在大型游戏中爆显存；在特定游戏中，减少内存交换后可获得非常显著的流畅度和帧率提升。

Windows 图形设置允许为单个 EXE 配置：

- 让 Windows 决定；
- 节能 GPU；
- 高性能 GPU；
- 特定 GPU 1 / 特定 GPU 2。

现有操作流程的问题是：用户必须手动找到每个 EXE 路径、逐个添加、逐个选择 GPU，并且很难快速识别“当前正在占用独显显存但尚未配置 GPU 偏好”的程序。

本工具要把实时 GPU 占用、现有规则审计、批量设置、隐藏/忽略和滚动回滚整合到一个现代 Windows 桌面程序中。

---

## 2. 已知目标机器与注册表事实

### 2.1 当前显卡

| 角色 | 名称 | PNP / DXGI 关键字段 | 当前用途 |
|---|---|---|---|
| 核显 | AMD Radeon(TM) Graphics | Vendor `1002`，Device `164E`，SubSys `164E1002` | 桌面输出、节能应用 |
| 独显 | AMD Radeon RX 6650 XT | Vendor `1002`，Device `73EF`，SubSys `00001EFE` | 游戏和高性能应用 |
| 虚拟显示 | GameViewer Virtual Display Adapter | `ROOT\DISPLAY\0000` | 不应默认作为可分配 GPU |

注意：`Win32_VideoController.AdapterRAM` 在 RX 6650 XT 上返回约 4 GiB，不能用于判断实际 8 GiB 显存容量。显存容量应从 DXGI 获取。

### 2.2 当前注册表位置

```text
HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences
```

该键下：

- 值名称通常是 EXE 完整路径；
- 值类型为 `REG_SZ`；
- `DirectXUserGlobalSettings` 是全局设置，不是应用路径，必须单独处理。

### 2.3 当前机器已确认的规则

#### 精确指定核显

```text
SpecificAdapter=1002&164E&164E1002;GpuPreference=1073741824;
```

#### 精确指定 RX 6650 XT

```text
SpecificAdapter=1002&73EF&1EFE;GpuPreference=1073741824;
```

#### 通用高性能

```text
GpuPreference=2;
```

#### 当前全局高性能适配器

```text
DirectXUserGlobalSettings = HighPerfAdapter=1002&73EF&1EFE;
```

### 2.4 对规则格式的工程判断

当前机器表明 `SpecificAdapter` 可由下列字段构成：

```text
VendorId&DeviceId&SubSysId（十六进制大写，SubSysId 去除前导零）
```

例如：

```text
VendorId  = 0x1002
DeviceId  = 0x73EF
SubSysId  = 0x00001EFE
结果      = 1002&73EF&1EFE
```

此格式没有包含 PCI 总线地址或设备实例路径。因此，当系统中存在两张 Vendor、Device、SubSys 完全相同的显卡时，特定适配器规则可能无法区分它们。工具必须检测这种歧义；遇到歧义时禁用“精确指定其中一张相同显卡”，仅保留通用节能/高性能规则或要求用户验证。

`GpuPreference=1073741824` 即 `0x40000000`，在本项目中应视为 Windows 的不透明“SpecificAdapter 模式标志”，不要把它设计成普通可扩展枚举值。

---

## 3. 产品目标

### 3.1 核心目标

1. 实时列出当前有 GPU 显存占用的程序。
2. 按 EXE 路径聚合多进程实例，显示每张显卡上的专用显存、共享显存和 GPU 引擎活动。
3. 默认突出显示“当前占用独显专用显存且尚未明确指定 GPU”的程序。
4. 支持把选中程序设置为：
   - 指定核显；
   - 指定独显；
   - 通用节能；
   - 通用高性能；
   - 清除 GPU 偏好，恢复让 Windows 决定。
5. 已经设置过 GPU 偏好的程序自动进入“已指定”页面，不再干扰待处理列表。
6. 支持用户自定义“忽略”，忽略只影响工具界面，不改 Windows 注册表。
7. 每次注册表修改都进入备份库，支持撤销上一次、回滚到任意历史节点和恢复初始状态。
8. 显示“规则配置”与“实际观察到的 GPU 使用”，帮助用户核对规则是否生效。

### 3.2 非目标

第一版不实现：

- 驱动级强制 GPU；
- 修改 AMD 驱动内部配置文件；
- 自动结束或强制重启游戏；
- 远程监控其他电脑；
- 内核驱动、Windows 服务或常驻系统托盘守护进程；
- 为每个游戏自动生成性能优化参数；
- 自动修改 `DirectXUserGlobalSettings`；
- 承诺所有应用都服从 Windows GPU 偏好。

本工具管理的是 Windows 的“GPU 偏好”，不是不可绕过的 GPU 强制规则。应用本身仍可能直接枚举适配器，或者在两张显卡上同时创建资源。

---

## 4. 目标用户体验

### 4.1 首次启动

1. 创建本地数据目录和 SQLite 数据库。
2. 读取并永久保存第一次看到的完整 `UserGpuPreferences` 初始快照。
3. 枚举 DXGI 适配器。
4. 从现有注册表规则和全局高性能适配器推断显卡角色。
5. 如果角色明确，直接进入主界面；如果不明确，弹出一次“显卡角色确认”：
   - 哪一张是核显/节能 GPU；
   - 哪一张是独显/高性能 GPU；
   - 哪些适配器不参与规则分配。
6. 启动后台 GPU 采样。
7. 默认打开“待处理”页面。

### 4.2 待处理页面

默认显示满足以下条件的程序：

- 当前有 GPU 显存占用；
- 完整 EXE 路径可用；
- 没有明确 GPU 偏好；
- 未被用户忽略；
- 默认独显专用显存大于 16 MiB。

默认按独显专用显存从高到低排序。

用户可以多选，然后执行：

- 指定核显；
- 指定独显；
- 通用节能；
- 通用高性能；
- 忽略；
- 打开文件位置；
- 复制路径。

写入后：

- 行立即从待处理列表移出；
- 进入“已指定”页；
- 显示“需重新启动目标程序后生效”；
- 历史页增加一笔事务。

### 4.3 已指定页面

按规则分类显示：

- 指定核显；
- 指定独显；
- 通用节能；
- 通用高性能；
- Windows 决定/默认；
- 未知或部分可识别规则。

支持：

- 改为另一张 GPU；
- 清除 GPU 偏好；
- 查看原始注册表字符串；
- 查看当前实际 GPU 使用；
- 打开文件位置。

### 4.4 全部占用页面

显示所有当前 GPU 进程，包括：

- 已配置；
- 未配置；
- 已忽略；
- 路径不可读；
- 系统进程；
- 虚拟/软件适配器上的活动。

此页主要用于诊断，不应默认隐藏小占用项目。

### 4.5 历史与回滚页面

每条事务显示：

- 时间；
- 操作类型；
- 目标 GPU；
- 涉及程序数量；
- 成功/失败/已回滚状态；
- 修改前和修改后规则；
- 撤销按钮；
- 回滚到此节点按钮。

还提供：

- 恢复初始状态；
- 导出当前完整注册表备份；
- 打开备份目录；
- 固定某个备份，防止滚动清理。

---

## 5. 技术选型

### 5.1 主技术栈

```text
语言：C# 14
运行时：.NET 10 LTS
桌面框架：WPF
界面组件：Wpf.Ui
MVVM：CommunityToolkit.Mvvm
DXGI 封装：Vortice.DXGI
Win32 / PDH 绑定：Microsoft.Windows.CsWin32
本地数据库：Microsoft.Data.Sqlite
日志：Serilog + Serilog.Sinks.File
测试：xUnit + FluentAssertions（或仅 xUnit Assert，避免不必要依赖）
```

### 5.2 选择理由

- WPF 对复杂 DataGrid、虚拟化、数据绑定和 Windows 桌面工具更成熟。
- Wpf.Ui 提供 Windows 11 Fluent 风格、导航、主题和现代控件，不改变 WPF 的核心部署模型。
- CommunityToolkit.Mvvm 通过源生成器减少属性和命令样板代码，适合 AI 持续维护。
- Vortice.DXGI 提供现代 C# DXGI 封装，避免手写 COM 接口和释放逻辑。
- CsWin32 为 PDH、进程查询和 Shell API 生成正确的 P/Invoke 定义。
- SQLite 适合保存不可变初始快照、事务历史、忽略列表和设置。
- .NET 10 是 LTS，支持 WPF 和单文件/自包含发布。

### 5.3 初始建议包版本

在项目初始化时使用以下已知稳定版本，并通过中央包管理锁定；开始实施前允许 Codex检查 NuGet 是否有兼容的稳定修订版，但不得使用浮动版本号。

| 包 | 建议起始版本 |
|---|---:|
| WPF-UI（C# 命名空间为 `Wpf.Ui`） | 4.3.0 |
| CommunityToolkit.Mvvm | 8.4.2 |
| Vortice.DXGI | 3.8.3 |
| Microsoft.Windows.CsWin32 | 0.3.298 |
| Microsoft.Data.Sqlite | 10.0.10 |
| Serilog | 4.4.0 |
| Serilog.Sinks.File | 7.0.0 |

如果实际还原时出现兼容问题，优先选择同一主版本的最新稳定修订版，并在 `docs/ADR/` 中记录调整原因。

---

## 6. 解决方案结构

```text
GpuPreferenceManager/
├─ GpuPreferenceManager.sln
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
├─ README.md
├─ LICENSE
├─ docs/
│  ├─ DEVELOPMENT_PLAN.md
│  ├─ IMPLEMENTATION_STATUS.md
│  ├─ RESEARCH_NOTES.md
│  ├─ REGISTRY_FORMAT.md
│  ├─ GPU_COUNTERS.md
│  ├─ MANUAL_TEST_PLAN.md
│  └─ ADR/
├─ src/
│  ├─ GpuPreferenceManager.Core/
│  ├─ GpuPreferenceManager.Windows/
│  └─ GpuPreferenceManager.App/
├─ tests/
│  ├─ GpuPreferenceManager.Core.Tests/
│  ├─ GpuPreferenceManager.Windows.Tests/
│  └─ GpuPreferenceManager.IntegrationTests/
└─ tools/
   └─ GpuProbe/
```

### 6.1 `GpuPreferenceManager.Core`

纯业务层，不直接引用 WPF、注册表、PDH 或 DXGI。包含：

- 数据模型；
- 注册表规则解析与序列化；
- 路径标准化；
- GPU 指标聚合；
- 规则分类；
- 事务差异与回滚计划；
- 接口定义；
- 纯单元测试目标。

### 6.2 `GpuPreferenceManager.Windows`

Windows 基础设施层。包含：

- DXGI 适配器枚举；
- PDH GPU 计数器采样；
- 进程路径和启动时间读取；
- 注册表仓储；
- `.reg` 备份导出；
- SQLite 数据库；
- 文件图标提取；
- Windows 特定集成测试。

### 6.3 `GpuPreferenceManager.App`

WPF 应用层。包含：

- App 启动与依赖注入；
- 页面、ViewModel、导航；
- DataGrid、筛选、排序和批量命令；
- 对话框、通知、主题；
- 配置页面；
- 未处理异常入口。

### 6.4 `tools/GpuProbe`

独立控制台诊断工具，必须早于 GUI 完成。输出：

- DXGI 适配器；
- LUID；
- Vendor/Device/SubSys；
- 推导的 SpecificAdapter；
- 当前注册表规则；
- PDH 原始实例名；
- 解析后的 PID、LUID、显存和引擎；
- 映射失败项。

该工具是底层研究和回归诊断的重要部分，正式项目中保留，不要在 GUI 完成后删除。

---

## 7. 项目配置标准

`Directory.Build.props` 建议至少包含：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.22000.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.22000.0</SupportedOSPlatformVersion>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <PlatformTarget>x64</PlatformTarget>
    <DebugType>embedded</DebugType>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

WPF 项目额外设置：

```xml
<UseWPF>true</UseWPF>
<OutputType>WinExe</OutputType>
```

发布配置：

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<PublishTrimmed>false</PublishTrimmed>
```

不要对 WPF 第一版开启 trimming。SQLite 和 UI 框架包含本机或反射相关依赖，强行裁剪会增加不可见风险。

同时提供两个发布产物：

1. **Portable Folder ZIP**：最可靠，便于诊断和杀毒软件兼容；
2. **Self-contained Single EXE**：便于用户直接运行，但需接受文件较大和本机库可能解压到临时目录。

---

## 8. 核心领域模型

### 8.1 适配器模型

```csharp
public sealed record GpuAdapterInfo(
    GpuAdapterId Id,
    string Name,
    long DedicatedVideoMemoryBytes,
    long SharedSystemMemoryBytes,
    string SpecificAdapterKey,
    GpuAdapterRole Role,
    AdapterIdentityConfidence IdentityConfidence,
    bool IsSoftware,
    bool IsRemote,
    bool IsAssignable);

public readonly record struct GpuAdapterId(
    uint LuidLowPart,
    int LuidHighPart,
    uint VendorId,
    uint DeviceId,
    uint SubSystemId);
```

角色枚举：

```csharp
Unknown
IntegratedOrPowerSaving
DiscreteOrHighPerformance
Other
Excluded
```

身份可信度：

```csharp
VerifiedByExistingRule
DerivedFromDxgi
UserConfirmed
Ambiguous
```

### 8.2 进程实例模型

```csharp
public readonly record struct ProcessInstanceKey(
    int ProcessId,
    long CreationTimeFileTime);

public sealed record ProcessInfoSnapshot(
    ProcessInstanceKey Key,
    string ProcessName,
    string? ExecutablePath,
    string? FileDescription,
    bool IsProtectedOrInaccessible);
```

必须使用 PID + 创建时间识别进程生命周期，避免 PID 被复用后把新进程错误合并到旧记录。

### 8.3 GPU 采样模型

```csharp
public sealed record ProcessAdapterGpuUsage(
    ProcessInstanceKey Process,
    GpuAdapterId Adapter,
    long DedicatedBytes,
    long SharedBytes,
    IReadOnlyDictionary<string, double> EngineUtilization,
    DateTimeOffset SampleTime);
```

### 8.4 EXE 聚合模型

```csharp
public sealed record ExecutableGpuUsage(
    string ExecutablePath,
    string DisplayName,
    IReadOnlyList<ProcessInstanceKey> Processes,
    IReadOnlyDictionary<GpuAdapterId, AggregatedAdapterUsage> AdapterUsages,
    GpuPreferenceRule Rule,
    bool IsIgnored,
    DateTimeOffset LastSeen);
```

### 8.5 GPU 偏好规则

```csharp
public sealed record GpuPreferenceRule(
    GpuPreferenceKind Kind,
    string? SpecificAdapterKey,
    int? RawGpuPreference,
    IReadOnlyList<RegistryRuleToken> Tokens,
    string RawValue);
```

规则分类：

```csharp
NoRule
WindowsDecides
GenericPowerSaving
GenericHighPerformance
SpecificAdapter
Unknown
```

---

## 9. DXGI 适配器枚举设计

### 9.1 数据来源

使用 Vortice.DXGI 枚举 `IDXGIAdapter1`，读取：

- Description；
- AdapterLuid；
- VendorId；
- DeviceId；
- SubSysId；
- DedicatedVideoMemory；
- SharedSystemMemory；
- Adapter flags。

不要用 WMI 的 `AdapterRAM` 作为显存容量。

### 9.2 SpecificAdapterKey 生成

```csharp
static string BuildSpecificAdapterKey(uint vendorId, uint deviceId, uint subSystemId)
    => $"{vendorId:X4}&{deviceId:X4}&{subSystemId:X}";
```

用户机器验收结果必须是：

```text
1002, 164E, 164E1002 -> 1002&164E&164E1002
1002, 73EF, 00001EFE -> 1002&73EF&1EFE
```

### 9.3 可信度判定

1. 扫描现有 `SpecificAdapter=` 值。
2. 如果生成的 key 与现有规则完全匹配，标记 `VerifiedByExistingRule`。
3. 如果没有现有规则但 DXGI 字段完整，标记 `DerivedFromDxgi`。
4. 用户在设置页确认角色后，标记 `UserConfirmed`。
5. 如果两个适配器生成相同 key，标记 `Ambiguous`，禁用精确指定。

### 9.4 角色推断

按顺序：

1. `DirectXUserGlobalSettings.HighPerfAdapter` 对应者推断为高性能/独显；
2. 已有大量 `SpecificAdapter` 规则与显存容量辅助判断；
3. 软件、远程和虚拟适配器默认排除；
4. 无法唯一判断时要求用户选择一次。

不得通过显卡名称中是否包含 `RX`、`RTX`、`Graphics` 等字符串作为唯一逻辑。

---

## 10. PDH GPU 数据采集设计

### 10.1 计数器

核心计数器：

```text
\GPU Process Memory(*)\Dedicated Usage
\GPU Process Memory(*)\Shared Usage
\GPU Engine(*)\Utilization Percentage
```

可选总览计数器：

```text
\GPU Adapter Memory(*)\Dedicated Usage
\GPU Adapter Memory(*)\Shared Usage
```

### 10.2 API

通过 CsWin32 调用：

```text
PdhOpenQueryW
PdhAddEnglishCounterW
PdhCollectQueryData
PdhGetFormattedCounterArrayW
PdhCloseQuery
```

使用 `PdhAddEnglishCounterW`，避免中文/英文系统的计数器名称本地化差异。通配符计数器使用 `PdhGetFormattedCounterArrayW` 获取实例数组。

如果目标系统上 `PdhAddEnglishCounterW + wildcard` 行为异常，保留降级实现：把语言中立路径转换为本地化路径后通过 `PdhAddCounterW` 添加。该降级必须由实际错误触发，而不是默认走复杂路径。

### 10.3 采样频率

- 默认：2 秒；
- 可选：1 秒、2 秒、5 秒；
- 第一帧用于预热速率计数器，不立即显示利用率；
- 内存计数器可在首帧显示；
- 使用后台 `Task` 和 `CancellationToken`；
- 使用容量为 1 的 bounded channel，UI 忙时丢弃旧样本，绝不堆积。

### 10.4 实例名解析

典型 GPU Process Memory：

```text
pid_1234_luid_0x00000000_0x0000ABCD_phys_0
```

典型 GPU Engine：

```text
pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_1_engtype_3D
```

实现独立的 `PdhGpuInstanceNameParser`，要求：

- 不区分大小写；
- 支持十六进制位数变化；
- 支持实例末尾的 `#1` 等重复实例后缀；
- 不认识的 engine type 原样保留；
- 解析失败不抛到采样主循环，只记录诊断日志；
- 对 LUID 两段顺序进行真实 DXGI 匹配验证。

LUID 映射策略：

1. 按常见顺序构造 HighPart/LowPart；
2. 在 DXGI 适配器字典中查找；
3. 若未匹配，尝试交换两段；
4. 如果只有一种顺序可匹配，缓存本次系统会话的顺序；
5. 两种都不匹配时保留原始实例并标记“未知适配器”。

### 10.5 聚合规则

#### 内存

- 同一 PID、同一 LUID、同一 `phys` 的重复记录取最大值，避免重复实例被双倍累计；
- 不同 `phys` 分区在同一 LUID 下求和；
- 同一 EXE 的多个 PID 求和；
- 保留每个 PID 明细供详情面板查看。

#### 引擎利用率

- 按 PID + LUID + engine type 保存；
- 同类型多个 engine node 取最大值，避免简单求和超过 100% 造成误导；
- 详情面板可显示 Top 3 引擎；
- 主列表显示最活跃引擎和利用率。

### 10.6 异常数据

- 忽略 PDH 状态无效的项；
- 负数归零并记录一次告警；
- 若单进程独显专用显存明显大于适配器总专用显存的 125%，标记“计数器疑似异常”，但不要直接删除；
- UI 标注“数据来自 Windows 性能计数器，与任务管理器采样时刻可能不同”。

---

## 11. 进程信息读取

### 11.1 路径获取顺序

优先使用：

1. `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`；
2. `QueryFullProcessImageNameW`；
3. 读取进程创建时间 `GetProcessTimes`；
4. `Process.MainModule.FileName` 仅作为后备；
5. 最后才使用 CIM/WMI 后备，且不得阻塞采样循环。

### 11.2 路径标准化

内部比较使用：

- `StringComparer.OrdinalIgnoreCase`；
- 去除外围引号；
- `Path.GetFullPath`；
- 统一目录分隔符；
- 保留原始显示大小写；
- 不主动解析符号链接和 8.3 短路径，避免权限和性能问题。

注册表中已有路径与实时路径比较时使用标准化键，但写回时使用实际完整路径。

### 11.3 不可访问进程

对于系统或受保护进程：

- 显示 PID、进程名和 GPU 数据；
- 路径不可读时禁用新增规则操作；
- 如果注册表中已有同名完整路径规则，可显示该规则，但不要仅凭进程名自动关联；
- 提供“以管理员身份重启工具”作为可选诊断入口，不默认提权。

---

## 12. 注册表规则引擎

### 12.1 仓储接口

```csharp
public interface IGpuPreferenceRegistry
{
    Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
    Task<RegistryValueState?> ReadValueAsync(string executablePath, CancellationToken cancellationToken);
    Task WriteValueAsync(string executablePath, string value, CancellationToken cancellationToken);
    Task DeleteValueAsync(string executablePath, CancellationToken cancellationToken);
}
```

注册表路径必须可通过依赖注入覆盖，以便测试时使用临时测试键，严禁测试直接修改真实 `UserGpuPreferences`。

### 12.2 解析器

输入：

```text
SpecificAdapter=1002&73EF&1EFE;GpuPreference=1073741824;SomeFutureField=1;
```

解析为有序 token：

```text
SpecificAdapter -> 1002&73EF&1EFE
GpuPreference   -> 1073741824
SomeFutureField -> 1
```

要求：

- 保留未知字段；
- 保留没有 `=` 的异常 token；
- 保留字段原始顺序；
- 键名比较不区分大小写；
- 序列化统一以分号结尾；
- 修改 GPU 字段时只移除或替换 `SpecificAdapter`、`GpuPreference`；
- 不得丢失未来 Windows 添加的字段。

### 12.3 写入规则

#### 指定适配器

```text
SpecificAdapter=<adapterKey>;GpuPreference=1073741824;
```

#### 通用节能

```text
GpuPreference=1;
```

#### 通用高性能

```text
GpuPreference=2;
```

#### 恢复 Windows 决定

- 删除 `SpecificAdapter`；
- 删除 `GpuPreference`；
- 如果没有其他 token，删除整个注册表值；
- 如果仍有其他 token，保留该值。

### 12.4 全局设置

`DirectXUserGlobalSettings`：

- 不作为应用条目；
- 第一版只读；
- 解析 `HighPerfAdapter`；
- 显示在“显卡与系统”页；
- 不纳入批量应用规则操作。

---

## 13. 事务、备份与回滚

### 13.1 数据目录

```text
%LocalAppData%\GpuPreferenceManager\
├─ data.db
├─ settings.json
├─ Backups\
├─ Logs\
└─ Diagnostics\
```

提供“便携数据模式”作为后续选项。默认不把数据库写在 EXE 同目录，避免只读目录和更新覆盖。

### 13.2 SQLite 表

#### `schema_info`

```text
version INTEGER PRIMARY KEY
applied_utc TEXT NOT NULL
```

#### `baseline_snapshots`

```text
id INTEGER PRIMARY KEY
created_utc TEXT NOT NULL
registry_json TEXT NOT NULL
registry_hash TEXT NOT NULL
adapter_json TEXT NOT NULL
windows_build TEXT NOT NULL
tool_version TEXT NOT NULL
```

第一条 baseline 永久保留，不自动覆盖。

#### `transactions`

```text
id INTEGER PRIMARY KEY
created_utc TEXT NOT NULL
operation_type TEXT NOT NULL
target_adapter_key TEXT NULL
status TEXT NOT NULL
note TEXT NULL
registry_before_hash TEXT NOT NULL
registry_after_hash TEXT NULL
tool_version TEXT NOT NULL
```

状态：

```text
Pending
Applied
PartiallyApplied
Failed
RolledBack
Superseded
```

#### `transaction_items`

```text
id INTEGER PRIMARY KEY
transaction_id INTEGER NOT NULL
value_name TEXT NOT NULL
before_exists INTEGER NOT NULL
before_kind INTEGER NULL
before_value TEXT NULL
after_exists INTEGER NOT NULL
after_kind INTEGER NULL
after_value TEXT NULL
apply_status TEXT NOT NULL
error TEXT NULL
```

#### `ignored_apps`

```text
normalized_path TEXT PRIMARY KEY
display_path TEXT NOT NULL
created_utc TEXT NOT NULL
note TEXT NULL
```

#### `adapter_preferences`

```text
specific_adapter_key TEXT PRIMARY KEY
display_name TEXT NOT NULL
role TEXT NOT NULL
is_excluded INTEGER NOT NULL
confirmed_utc TEXT NULL
```

#### `backup_files`

```text
id INTEGER PRIMARY KEY
transaction_id INTEGER NULL
file_path TEXT NOT NULL
created_utc TEXT NOT NULL
is_baseline INTEGER NOT NULL
is_pinned INTEGER NOT NULL
sha256 TEXT NOT NULL
```

### 13.3 修改流程

一次批量写入必须执行：

1. 获取进程内 `SemaphoreSlim` 写锁；
2. 重新读取当前完整注册表快照；
3. 如果还没有 baseline，创建 baseline；
4. 创建完整 `.reg` 修改前备份；
5. 在 SQLite 中插入 `Pending` 事务和所有 item；
6. 逐项写入注册表；
7. 每项写入后重新读取并比较精确字符串；
8. 全部成功则把事务标记为 `Applied`；
9. 部分失败则立即按 `before` 尝试补偿恢复；
10. 记录最终状态和错误；
11. 刷新 UI 注册表快照；
12. 释放写锁。

SQLite 和注册表不可能构成真正的跨资源原子事务，因此必须使用可恢复日志状态，而不是假装完全原子。

### 13.4 启动恢复

启动时如果存在 `Pending` 事务：

1. 读取每个 item 当前注册表值；
2. 与 `after` 一致：标记为 `Applied`；
3. 与 `before` 一致：标记为 `Failed` 或 `RolledBack`；
4. 混合状态：标记 `PartiallyApplied` 并提示用户选择完成写入或恢复修改前状态。

### 13.5 `.reg` 备份

每次修改前导出完整键，文件名：

```text
Initial_20260802_020600.reg
Before_T000001_20260802_021500.reg
```

建议工具自己生成 `.reg`，使用 UTF-16LE：

```text
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences]
"X:\\Path\\App.exe"="GpuPreference=2;"
```

必须正确转义：

- 反斜杠；
- 双引号；
- 换行等特殊字符。

### 13.6 滚动策略

- baseline 备份永久保留；
- 固定备份永久保留；
- 普通 `.reg` 默认保留最近 100 个；
- SQLite 事务历史默认不自动删除；
- 日志保留 14 天；
- 清理在启动后的后台低优先级任务执行。

### 13.7 回滚类型

#### 撤销单笔事务

- 默认仅当当前值仍等于该事务的 `after` 时恢复；
- 发现外部修改时显示冲突；
- 用户可选“跳过冲突”或“强制使用历史值”。

#### 回滚到历史节点

- 计算目标节点之后所有事务的逆向差异；
- 作为一笔新的“历史回滚事务”应用；
- 不删除旧历史。

#### 恢复初始状态

- 先备份当前状态；
- 恢复 baseline 中所有值；
- 删除 baseline 中不存在、但当前存在的应用值；
- `DirectXUserGlobalSettings` 也按 baseline 恢复；
- 整个操作自身也进入事务历史，因此可以再次撤销。

---

## 14. 应用分类逻辑

### 14.1 页面分类

```text
待处理 = 当前有 GPU 占用 AND 无明确规则 AND 未忽略 AND 路径可用
已指定 = 存在明确 GPU 规则
忽略   = 在 ignored_apps 中
异常   = 规则未知、路径失效、适配器歧义或数据映射失败
全部   = 所有实时记录
```

### 14.2 “已指定”的判定

不能仅判断注册表值是否存在。必须解析 GPU 字段：

- 只有未知图形字段，没有 `GpuPreference`/`SpecificAdapter`：仍视为“未指定 GPU”；
- `GpuPreference=1`：通用节能；
- `GpuPreference=2`：通用高性能；
- `SpecificAdapter` + `0x40000000`：特定适配器；
- 其他数值：未知规则。

### 14.3 失效规则

规则中的 EXE 路径不存在时：

- 不从注册表自动删除；
- 进入“异常/失效规则”；
- 支持用户手动清理；
- 后续版本可检测软件新路径并复制规则。

---

## 15. UI 信息架构

### 15.1 导航

```text
待处理
已指定
全部占用
忽略
历史与回滚
显卡与系统
设置
```

### 15.2 主窗口

- Wpf.Ui `FluentWindow`；
- 左侧 NavigationView；
- 顶部显示采样状态、上次刷新时间和暂停按钮；
- 支持系统主题、浅色、深色；
- 默认窗口大小约 1280×760；
- 记忆窗口位置和大小；
- 支持 Windows 11 Mica，但 Mica 失败时无条件降级普通背景。

### 15.3 顶部显卡概览卡

每张参与管理的 GPU 显示：

- 名称；
- 角色；
- 专用显存总量；
- 当前适配器专用/共享占用；
- SpecificAdapter key；
- 身份可信度。

虚拟和排除适配器折叠到“其他适配器”。

### 15.4 DataGrid 列

| 列 | 内容 |
|---|---|
| 选择 | 多选复选框 |
| 程序 | 图标、文件描述、EXE 名称 |
| 完整路径 | 可选显示，支持复制 |
| 进程 | PID 数量；详情中列出 PID |
| 独显专用 | 主要排序列 |
| 独显共享 | 观察溢出和跨适配器资源 |
| 核显专用 | 如系统报告则显示 |
| 核显共享 | 观察转移效果 |
| GPU 引擎 | 3D、Copy、Decode 等 |
| 当前偏好 | 未指定、核显、独显、通用节能等 |
| 30 秒峰值 | 独显专用显存峰值 |
| 状态 | 需重启、路径不可读、计数器异常等 |

### 15.5 顶部批量操作栏

- 指定核显；
- 指定独显；
- 通用节能；
- 通用高性能；
- 清除 GPU 偏好；
- 忽略/取消忽略；
- 刷新；
- 暂停采样。

### 15.6 详情面板

点击行后从右侧打开详情：

- 文件图标和完整路径；
- 文件版本、产品名、发布者（能读取时）；
- 所有 PID 和启动时间；
- 每张 GPU 的专用/共享显存；
- 每种引擎利用率；
- 当前规则分类；
- 原始注册表字符串；
- 最近 30 秒小型曲线图；
- 打开文件位置、复制路径、查看历史。

第一版曲线图可使用简单 WPF Polyline，不引入大型图表库。

### 15.7 稳定刷新

实时刷新不得造成：

- 当前选中行跳失；
- 排序频繁抖动；
- DataGrid 滚动位置重置；
- 每 2 秒重建整张 ObservableCollection。

实现方式：

- 用稳定的规范化路径作为行 key；
- 对现有 ViewModel 原位更新；
- 仅新增/删除发生变化的行；
- 删除项目需连续两个样本未出现；
- 排序刷新节流到最多每 2 秒一次；
- 用户正在编辑筛选或打开上下文菜单时不强制重新排序。

---

## 16. 服务与接口

建议接口：

```csharp
public interface IGpuAdapterCatalog
{
    Task<IReadOnlyList<GpuAdapterInfo>> RefreshAsync(CancellationToken cancellationToken);
}

public interface IGpuMetricsSampler : IAsyncDisposable
{
    IAsyncEnumerable<GpuMetricsSnapshot> SampleAsync(
        TimeSpan interval,
        CancellationToken cancellationToken);
}

public interface IProcessInfoProvider
{
    ValueTask<ProcessInfoSnapshot> GetAsync(int pid, CancellationToken cancellationToken);
}

public interface IGpuPreferenceRegistry
{
    Task<RegistrySnapshot> ReadSnapshotAsync(CancellationToken cancellationToken);
    Task ApplyAsync(IReadOnlyList<RegistryMutation> mutations, CancellationToken cancellationToken);
}

public interface IHistoryStore
{
    Task EnsureBaselineAsync(...);
    Task<long> BeginTransactionAsync(...);
    Task CompleteTransactionAsync(...);
    Task<IReadOnlyList<HistoryEntry>> QueryAsync(...);
}

public interface IGpuPreferenceChangeService
{
    Task<ChangeResult> ApplyPreferenceAsync(
        IReadOnlyList<string> executablePaths,
        GpuPreferenceTarget target,
        CancellationToken cancellationToken);
}

public interface IRollbackService
{
    Task<RollbackPreview> PreviewUndoAsync(long transactionId, CancellationToken cancellationToken);
    Task<ChangeResult> UndoAsync(long transactionId, ConflictPolicy policy, CancellationToken cancellationToken);
    Task<ChangeResult> RestoreBaselineAsync(ConflictPolicy policy, CancellationToken cancellationToken);
}
```

协调服务：

```text
GpuMonitoringCoordinator
ApplicationInventoryService
GpuPreferenceChangeService
BackupService
RollbackService
RegistryRefreshCoordinator
SettingsService
```

---

## 17. 并发与线程模型

- PDH 查询只在单个后台采样任务中使用；
- DXGI 枚举在启动和设备变化时执行，不每帧执行；
- 进程路径查询使用并发限制，例如最多 8 个；
- SQLite 使用短连接或单写者策略；
- 所有注册表写操作串行化；
- UI 通过 Dispatcher 只接收已经聚合的数据；
- 采样生产者与 UI 消费者之间使用 bounded channel；
- 关闭应用时按顺序取消采样、等待写事务、关闭数据库和日志。

设备变化：第一版可在用户点击刷新和每 60 秒检查一次适配器集合。后续可监听设备通知。

---

## 18. 日志与诊断

### 18.1 日志

路径：

```text
%LocalAppData%\GpuPreferenceManager\Logs\app-.log
```

策略：

- 每日滚动；
- 保留 14 天；
- 单文件上限 10 MiB；
- 默认 Information；
- 调试版允许切换 Debug；
- 不上传，不含遥测。

记录内容：

- 应用版本、Windows build；
- 枚举到的适配器；
- SpecificAdapter 映射可信度；
- PDH 初始化与错误；
- 无法解析的实例名；
- 注册表事务 ID 和结果；
- 回滚冲突；
- 未处理异常。

### 18.2 诊断导出

后续完整版本支持导出 ZIP：

```text
app-version.txt
adapters.json
registry-snapshot.json
pdh-raw-sample.json
settings-redacted.json
logs/
database-schema.sql
```

不要自动包含整个 SQLite 数据库，除非用户明确勾选，因为其中包含本机程序路径历史。

---

## 19. 设置项

第一版：

- 采样间隔：1/2/5 秒；
- 待处理显存阈值：默认 16 MiB；
- 默认排序列；
- 是否显示系统进程；
- 是否显示小占用；
- 主题：跟随系统/浅色/深色；
- 备份数量：默认 100；
- 日志级别；
- 适配器角色和排除状态；
- 是否启动时自动开始采样。

设置存入 `settings.json`，使用版本化 schema。事务和关键历史仍存 SQLite。

---

## 20. 性能与资源目标

在用户机器或同级系统上：

- 默认 2 秒采样时，空闲平均 CPU 占用目标低于 1%；
- 工作集目标低于 150 MiB；
- 200 个 GPU 计数器实例时 UI 不明显卡顿；
- 单次 UI 应用快照耗时目标低于 100 ms；
- 注册表读取不进入采样热路径；
- 文件图标和版本信息使用缓存；
- 不保存长期 GPU 时间序列，只保留内存中的 30 秒滚动窗口。

性能目标不是绝对发布阻断值，但明显超出时必须记录和分析。

---

## 21. 错误处理

### 21.1 PDH 不可用

- 主界面显示“GPU 性能计数器不可用”；
- 注册表规则管理仍可使用；
- 提供重试；
- 诊断页显示 PDH 错误码；
- 不让应用崩溃。

### 21.2 数据库损坏

- 先把损坏文件重命名为 `data.corrupt.<timestamp>.db`；
- 尝试创建新数据库；
- 保留 `.reg` 备份；
- 明确提示历史数据库不可用；
- 不自动删除损坏文件。

### 21.3 注册表写入失败

- 保留修改前 `.reg`；
- 标记事务失败；
- 尝试逐项恢复；
- 显示具体失败路径和错误；
- 其他成功项不得被静默掩盖。

### 21.4 特定适配器歧义

- 禁用对应精确写入按钮；
- 显示原因；
- 仍允许通用节能/高性能；
- 不猜测 GPU 编号。

### 21.5 应用退出

- 写入规则不依赖目标程序仍在运行；
- 行可在短暂 grace period 后从实时页消失；
- 已指定页仍从注册表库存显示该应用。

---

## 22. 测试计划

### 22.1 Core 单元测试

#### RegistryRuleParserTests

覆盖：

- 空值；
- `GpuPreference=1/2/0`；
- SpecificAdapter；
- 字段顺序变化；
- 重复字段；
- 未知字段；
- 没有等号的 token；
- 大小写变化；
- 清除 GPU 字段后保留未知字段；
- 序列化分号结尾。

#### SpecificAdapterKeyTests

必须包含用户真实 fixture：

```text
1002 / 164E / 164E1002 -> 1002&164E&164E1002
1002 / 73EF / 00001EFE -> 1002&73EF&1EFE
```

覆盖 NVIDIA、Intel 和 SubSysId 为零的情况。

#### PdhGpuInstanceNameParserTests

覆盖：

- Process Memory；
- Engine；
- 3D、Copy、VideoDecode、Compute；
- `#1` 后缀；
- 大小写；
- 非法输入；
- LUID 高低段交换。

#### GpuUsageAggregatorTests

覆盖：

- 一个 EXE 多 PID；
- 一个 PID 多 GPU；
- 多 phys；
- 重复实例取最大；
- 进程退出；
- PID 复用；
- 峰值窗口。

#### RollbackPlannerTests

覆盖：

- 完整撤销；
- 删除新增值；
- 恢复被删除值；
- 外部修改冲突；
- 回滚到历史节点；
- baseline 恢复。

### 22.2 Windows 集成测试

所有注册表写测试使用：

```text
HKCU\Software\GpuPreferenceManager.Tests\<Guid>
```

不得触碰真实 DirectX 键。

测试：

- DXGI 至少枚举一个适配器；
- SpecificAdapter key 非空；
- PDH 查询能初始化；
- 有 GPU 程序运行时能取得至少一项；
- QueryFullProcessImageName 能读取测试进程路径；
- SQLite 迁移、事务和重开；
- `.reg` 导出内容可解析；
- 模拟 Pending 事务启动恢复。

硬件/系统不满足时，集成测试应明确 Skip，而不是假通过。

### 22.3 UI 测试

不强制引入昂贵的 UI 自动化框架。最低要求：

- ViewModel 单元测试；
- 命令可用状态测试；
- 筛选分类测试；
- 选择保持测试；
- 手工 UI 验收。

---

## 23. 用户机器手工验收

### 23.1 适配器

- 显示 AMD Radeon(TM) Graphics；
- 显示 AMD Radeon RX 6650 XT；
- RX 6650 XT 专用显存容量约 8 GiB，不得显示 4 GiB；
- GameViewer Virtual Display Adapter 不作为默认可分配目标；
- 核显 key 为 `1002&164E&164E1002`；
- 独显 key 为 `1002&73EF&1EFE`；
- 全局高性能适配器映射到 RX 6650 XT。

### 23.2 现有规则识别

必须正确识别：

- `Endfield.exe`：指定 RX 6650 XT；
- `helldivers2.exe`：指定 RX 6650 XT；
- `wallpaperui.exe`：指定核显；
- `dwm.exe`：指定核显；
- 多个 `java.exe`：通用高性能；
- `DirectXUserGlobalSettings`：不出现在应用列表。

### 23.3 实时数据

- 运行大型游戏时独显专用显存接近任务管理器；
- 运行壁纸、截图和 PowerToys 时能看到核显共享/专用占用；
- 同一程序多进程能按 EXE 合并；
- 详情能看到 PID；
- 程序退出后列表在两个样本内移除；
- 不出现每次刷新滚动位置跳回顶部。

### 23.4 写入与回滚

使用一个可安全重启的测试应用：

1. 修改前记录原始注册表值；
2. 指定核显；
3. 确认注册表精确为核显规则；
4. 重启测试应用；
5. 在工具和任务管理器中核对实际 GPU；
6. 改为独显；
7. 撤销上一笔；
8. 确认恢复到核显规则；
9. 再撤销或回滚到初始；
10. 确认原始值逐字符恢复；
11. 检查 `.reg` 和 SQLite 事务均存在。

### 23.5 异常

- 手动在注册表增加未知字段，工具修改 GPU 后未知字段仍存在；
- 手动修改某条规则后再撤销旧事务，工具能提示冲突；
- 杀死工具写入过程或模拟 Pending，重启后能恢复/完成；
- 路径不可访问进程不会导致界面崩溃；
- 暂停采样后注册表管理仍可使用。

---

## 24. 开发里程碑

### M0：仓库与工程基线

交付：

- 解决方案和项目结构；
- 中央包管理；
- 编码规范；
- 日志基础；
- xUnit；
- CI 编译和测试；
- `GpuProbe` 空壳可运行。

完成标准：

```text
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

全部通过。

### M1：规则模型和注册表只读

交付：

- 规则 parser/serializer；
- 快照模型；
- 真实 DirectX 键只读；
- `GpuProbe registry` 输出分类；
- 完整单元测试。

完成标准：能正确分类用户已贴出的所有规则。

### M2：DXGI 适配器与 SpecificAdapter 映射

交付：

- Vortice.DXGI 枚举；
- LUID 和显存容量；
- SpecificAdapter key；
- 角色推断；
- 重复 key 检测；
- `GpuProbe adapters`。

完成标准：用户机器两个 key 和 8 GiB 独显容量正确。

### M3：PDH 原始采样

交付：

- CsWin32 NativeMethods 配置；
- 三个核心计数器；
- wildcard 数组读取；
- 实例名 parser；
- LUID 映射；
- `GpuProbe sample --seconds 30`。

完成标准：能输出 Endfield 或其他 GPU 程序按 PID/适配器的显存和引擎。

### M4：聚合与应用库存

交付：

- 进程路径和创建时间；
- 按 EXE 聚合；
- 加入注册表规则；
- 忽略状态接口；
- 30 秒峰值；
- 分类服务。

完成标准：控制台可输出与最终表格等价的 JSON。

### M5：WPF 只读 MVP

交付：

- Fluent 主窗口；
- 待处理/已指定/全部占用；
- DataGrid 虚拟化；
- 筛选、排序、搜索；
- 详情面板；
- 暂停/刷新；
- 不提供写入按钮或按钮禁用。

完成标准：连续运行 1 小时无崩溃，界面刷新稳定。

### M6：数据库、baseline 和备份

交付：

- SQLite migration；
- baseline；
- settings；
- ignored apps；
- `.reg` 导出；
- 历史页只读展示。

完成标准：首次启动只创建一次 baseline，重启后不覆盖。

### M7：受控写入

交付：

- 指定核显/独显；
- 通用节能/高性能；
- 清除 GPU 偏好；
- 批量操作；
- 写前备份；
- 写后校验；
- 失败补偿；
- “需重启目标程序”提示。

完成标准：测试键自动测试通过，真实机器手工测试通过。

### M8：回滚

交付：

- 撤销事务；
- 冲突检测；
- 回滚到节点；
- 恢复 baseline；
- Pending 启动恢复；
- 备份滚动清理。

完成标准：真实规则可逐字符恢复。

### M9：完善与发布

交付：

- 主题和窗口状态；
- 图标缓存；
- 错误页面；
- 诊断导出；
- Portable ZIP；
- Single EXE；
- README；
- 手工测试报告；
- 许可证清单。

完成标准：完成本文第 23 节全部手工验收。

---

## 25. CI 与代码质量

GitHub Actions 或等价 CI：

```text
windows-latest
- setup-dotnet 10.x
- dotnet restore --locked-mode
- dotnet build -c Release --no-restore
- dotnet test -c Release --no-build
- dotnet publish App -c Release -r win-x64
```

要求：

- 提交 `packages.lock.json`；
- 禁止警告；
- 格式检查可使用 `dotnet format --verify-no-changes`；
- Windows 硬件相关集成测试通过环境变量显式开启，普通 CI 默认跳过；
- 每个 PR 不上传包含用户本机数据的产物。

---

## 26. 编码规范

- 启用 nullable；
- 公共 API 使用 XML 文档，内部显然代码无需过度注释；
- 不使用 `async void`，事件处理器除外；
- 所有 I/O 接受 CancellationToken；
- 不在 ViewModel 中捕获泛型 `Exception` 后静默忽略；
- Win32 handle 必须使用 SafeHandle 或明确 `Dispose`；
- DXGI COM 对象必须 `Dispose`；
- byte 使用 `long`，显示层才格式化 MiB/GiB；
- 时间存 UTC，UI 转本地时间；
- 数据库 SQL 参数化；
- 注册表路径和 EXE 路径不得通过字符串拼接构造 SQL；
- 不用显卡名称作为持久身份主键；
- 不用 GPU 0/GPU 1 作为持久身份；
- 不用 WMI AdapterRAM。

---

## 27. 发布与分发

### 27.1 Portable ZIP

目录示例：

```text
GpuPreferenceManager.exe
THIRD-PARTY-NOTICES.txt
LICENSE
README.txt
```

数据仍写入 `%LocalAppData%`。

### 27.2 单文件 EXE

发布命令示例：

```powershell
dotnet publish .\src\GpuPreferenceManager.App\GpuPreferenceManager.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

### 27.3 版本

采用 SemVer：

```text
0.1.0 只读 MVP
0.2.0 写入与备份
0.3.0 回滚与完善
1.0.0 完成全部验收
```

应用标题和日志中显示版本与 Git commit 短哈希。

---

## 28. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| GPU 性能计数器返回异常值 | 显示不准 | 状态校验、异常标记、与任务管理器对比 |
| PDH 英文 wildcard 在部分系统行为不同 | 无法采样 | 提供本地化路径降级实现 |
| Windows 更新改变注册表内部格式 | 错写规则 | token 保留、模板验证、写后读取、备份回滚 |
| 两张相同型号/子系统显卡 key 相同 | 无法精确区分 | 检测歧义，禁用精确写入 |
| 虚拟显示适配器混入 | 分配错误 | 默认排除软件/远程/虚拟适配器，允许手动配置 |
| WPF 高频刷新卡顿 | 使用体验差 | 原位更新、虚拟化、bounded channel、节流排序 |
| SQLite 与注册表无法原子提交 | 中断导致部分写入 | Pending 日志、写后校验、启动恢复 |
| 单文件发布触发误报或解压问题 | 无法运行 | 同时提供 Portable ZIP |
| 应用不服从 GPU 偏好 | 用户误判 | 明确称为偏好，显示实际 GPU 活动 |
| 软件更新改变 EXE 路径 | 旧规则失效 | 失效规则页；后续实现路径迁移建议 |

---

## 29. 后续版本候选

不阻塞 1.0：

- Electron/WindowsApps 更新路径迁移；
- 文件签名、ProductName、OriginalFilename 辅助匹配；
- 软件新版本自动提示继承旧规则；
- 系统托盘和开机启动；
- GPU 适配器设备变化通知；
- 任务管理器风格小型曲线；
- 导入/导出规则配置；
- 规则模板组，例如“桌面程序全部核显”；
- 监测新出现的未配置独显占用并通知；
- 命令行接口；
- MSIX 安装包；
- 多语言资源。

---

## 30. 最终 Definition of Done

项目达到 1.0 必须同时满足：

1. 能正确枚举用户核显、RX 6650 XT 和虚拟适配器；
2. 能正确显示 RX 6650 XT 8 GiB 容量；
3. 能正确解析用户现有 SpecificAdapter 和通用高性能规则；
4. 能实时按 EXE 和适配器显示专用/共享显存；
5. 待处理页能准确排除已经明确配置 GPU 的程序；
6. 能批量指定核显、独显、通用节能和通用高性能；
7. 清除偏好时不破坏未知注册表字段；
8. 每次修改都有 SQLite 事务和 `.reg` 修改前备份；
9. 能撤销、回滚到节点和恢复 baseline；
10. 中断后的 Pending 事务可恢复；
11. 连续运行至少 1 小时无崩溃、无明显内存持续增长；
12. Portable ZIP 和单文件 EXE 均可在干净 Windows 11 x64 环境运行；
13. 所有自动测试通过；
14. 完成真实机器手工验收并保存测试报告；
15. README 明确说明 GPU 偏好不是驱动级强制。

---

## 31. 建议的第一轮 Codex 任务

不要直接实现整个项目。第一轮只完成 M0-M2，并提交以下结果：

1. 创建解决方案、项目和测试结构；
2. 实现注册表规则 parser/serializer；
3. 实现真实注册表只读快照；
4. 实现 DXGI 适配器枚举；
5. 生成 SpecificAdapter key；
6. 实现 `GpuProbe registry` 和 `GpuProbe adapters`；
7. 添加用户机器 fixture 单元测试；
8. 输出 `docs/IMPLEMENTATION_STATUS.md`；
9. 不实现任何真实注册表写入；
10. 不开始 WPF 页面。

第一轮验收命令：

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project .\tools\GpuProbe -- registry
dotnet run --project .\tools\GpuProbe -- adapters
```

预期关键输出：

```text
AMD Radeon(TM) Graphics
SpecificAdapter: 1002&164E&164E1002

AMD Radeon RX 6650 XT
SpecificAdapter: 1002&73EF&1EFE
Dedicated VRAM: about 8 GiB

DirectXUserGlobalSettings.HighPerfAdapter -> RX 6650 XT
```

第一轮通过后，再进入 M3 的 PDH 采样研究。

---

## 32. 技术参考

以下资料用于实施时核对 API 行为：

1. Microsoft Learn：.NET 10 新增功能和 LTS 支持。
2. Microsoft Learn：WPF Overview。
3. Microsoft Learn：单文件部署。
4. Microsoft Learn：`PdhAddEnglishCounterW`。
5. Microsoft Learn：`PdhGetFormattedCounterArrayW`。
6. Microsoft Learn：`IDXGIFactory4::EnumAdapterByLuid`。
7. Microsoft Learn：CsWin32 调用 Win32 API。
8. Microsoft Learn：CommunityToolkit.Mvvm。
9. Microsoft Learn：Microsoft.Data.Sqlite。
10. Wpf.Ui 官方 GitHub。
11. Vortice.DXGI NuGet / 官方仓库。

开发过程中发现的新资料和实测结论统一追加到 `docs/RESEARCH_NOTES.md`，不要直接改写历史实验结果。
