# 实现状态

更新日期：2026-08-02
主规格：`docs/DEVELOPMENT_PLAN.md`
应用版本：0.8.0

## 总体状态

M0–M9 的代码、自动测试、诊断命令和发布链均已实现，解决方案可在 Windows 11 x64、.NET SDK 10.0.302 下以 Release 配置零警告构建。主规格第 23 节中需要修改真实 `UserGpuPreferences`、重启目标应用、连续运行一小时和干净机器验证的条目尚未执行，因此不能把版本冒充为完成全部真实机器验收的 1.0.0。

## 里程碑

### M0–M2：工程、注册表只读和 DXGI

- 解决方案、中央包管理、锁文件、CI、严格分析器和 xUnit；
- 保留未知字段/顺序/异常 token 的规则 parser/serializer；
- `UserGpuPreferences` 完整只读快照与当前机器脱敏 fixture；
- DXGI LUID、显存、flags 和 `SpecificAdapter` key；
- 全局高性能角色推断、重复 key 歧义检测；
- `GpuProbe registry` 和 `GpuProbe adapters`。

### M3–M4：PDH 和应用库存

- CsWin32 PDH 英文 wildcard 数组读取；
- Dedicated/Shared/Engine 三类计数器和实例名 parser；
- LUID 映射、phys/重复实例聚合、PID 创建时间防复用；
- `QueryFullProcessImageName`、文件描述、并发限制；
- 按 EXE 聚合、注册表规则/忽略状态、30 秒峰值和分类；
- `GpuProbe sample`、`GpuProbe inventory`。
- PDH wildcard 数组在实例数变化导致二次 `PDH_MORE_DATA` 时重新扩容，避免采样线程退出；
- `GpuProbe inventory` 读取完整进程快照并补查活跃 PID，与主界面的进程树归组保持一致；
- 30 秒峰值窗口会移除已经完全过期的路径缓存。

### M5：WPF 主界面

- 使用原生 Windows 标题栏，具备标准拖动、最小化、最大化和关闭行为；
- 按 Windows 11 任务管理器的信息层级采用固定左侧导航、页头搜索/操作区和独立滚动内容；
- 待处理、已指定、全部占用、忽略、需留意、历史、适配器和设置页；
- 虚拟化只读 DataGrid、固定尺寸模板选择框、稳定路径行 key、原位刷新和连续两帧删除；
- 主表显示程序、实际使用 GPU、独显/其他 GPU 专用显存、当前偏好和状态；完整路径、PID 聚合、原始规则及逐适配器采样位于详情；
- 勾选立即同步详情与命令状态，不再进入 DataGrid 编辑事务，持续采样刷新不会触发 `AddNew/EditItem` 异常；
- 显卡页使用设备管理器一致名称、分隔明确的设备键和独立竖向滚动；
- 将“异常”改为“需留意”，明确说明系统/受保护进程和未知规则不代表显卡故障；
- 无唯一适配器或无有效选择时禁用相应命令；采样失败时保留注册表管理并显示错误。

### M6：SQLite、baseline、设置和备份

- 版本化 SQLite schema、首次 baseline 只创建一次；
- ignored apps、settings.json、窗口状态；
- UTF-16LE `.reg` 导出和 SHA-256；
- 数据库损坏时保留为 `data.corrupt.<timestamp>.db` 并重建；
- 历史页只读展示。

### M7–M8：受控写入与回滚

- 特定核显/独显、通用节能/高性能、清除偏好和批量操作；
- 串行写入、写前备份、Pending 日志、逐项读回校验和失败补偿；
- 全局设置保持只读，SpecificAdapter 必须身份唯一且可分配；
- 多 EXE 进程组逐项显示独立注册表规则，写入前必须明确选择实际 EXE；主表对规则不一致的组给出汇总提示；
- 现有值不是 `REG_SZ` 时拒绝写入，回滚也不会把 `REG_EXPAND_SZ` 等类型错误恢复为 `REG_SZ`；
- 注册表值在快照枚举期间被并发删除时跳过该瞬时项，不中断监控；
- 撤销、外部修改冲突策略、回滚到节点、恢复 baseline；
- Pending 启动恢复和备份滚动清理能力；
- 写入自动测试只触碰随机 `HKCU\Software\GpuPreferenceManager.Tests\<Guid>`。

### M9：完善与发布

- System/Light/Dark 启动主题和窗口状态；
- 基于路径+最后写入时间的 EXE 图标缓存；
- 日志、PDH 错误状态和诊断 ZIP；
- 诊断包含版本、适配器、注册表快照、PDH、脱敏设置、日志和数据库 schema，不含整个 SQLite 数据库；
- Portable 和 SingleFile 发布配置及 `scripts/publish.ps1`；
- README、发行说明、许可证清单、注册表/计数器文档和手工报告。

## 当前机器已确认结果

```text
AMD Radeon RX 6650 XT
SpecificAdapter: 1002&73EF&1EFE
Dedicated VRAM: 7.96 GiB (8,541,929,472 bytes)
DirectXUserGlobalSettings.HighPerfAdapter -> AMD Radeon RX 6650 XT

AMD Radeon(TM) Graphics
SpecificAdapter: 1002&164E&164E1002
```

DisplayConfig 已把第二个 LUID 关联到 `ROOT\DISPLAY\0000`，界面正确显示为 `GameViewer Virtual Display Adapter` 并标记为 `Excluded`。物理核显与该虚拟路径仍生成相同的 `1002&164E&164E1002`，因此物理核显继续标记为 `Ambiguous` 且不可精确分配；名称已纠正，但不会因名称不同就错误假设 Windows 的 SpecificAdapter key 可以区分二者。

0.3.1 移除了会在 Alt+Tab/窗口消息后只改变背景的 `SystemThemeWatcher`。0.4.0 进一步恢复原生窗口框架，并以自动脚本验证标准窗控、三轮 Alt+Tab、1000×620 最小尺寸、显卡页滚动、勾选即时启用和持续采样稳定性。

0.4.1 将默认采样间隔调整为 1 秒，并自动迁移使用旧版 2 秒默认值的 schema 1 设置。分类视图不再对“待处理”额外套用独显显存阈值，因此普通程序会且只会出现在待处理、已指定、忽略或需留意之一。应用表格支持展开同一路径下的具体 PID 和启动时间，合并显存列、启用换行并移除横向滚动条。选中详情使用独立持久高亮，不受集合刷新或焦点变化影响。

适配器歧义检测现在只比较可分配的物理硬件；已排除的虚拟、软件和远程适配器不会再污染物理 GPU 身份。当前机器的 GameViewer Virtual Display Adapter 因 `ROOT\DISPLAY\0000` 自动排除并从默认列表隐藏，物理 AMD Radeon(TM) Graphics 因而恢复为可精确分配的节能 GPU。

0.5.0 不再每秒全量刷新集合视图，改用实时筛选和实时排序，展开状态可跨采样保持。父应用行可展开为全宽子进程表，逐项显示显示名、可执行文件、PID、实际 GPU 和专用显存。名称、实际 GPU、显存和偏好列支持排序；顶部提供默认有效占用、独显大于 0、独显至少 10 MiB 和全部四种筛选。低于显示阈值的残留分配、零占用及已排除适配器不会进入实际 GPU 或显存摘要。

## 自动验收命令

```powershell
dotnet restore --locked-mode
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
$env:GPM_RUN_WINDOWS_HARDWARE_TESTS = '1'
dotnet test -c Release --no-build
dotnet run --project .\tools\GpuProbe -- registry
dotnet run --project .\tools\GpuProbe -- adapters
dotnet run --project .\tools\GpuProbe -- sample --seconds 30
dotnet run --project .\tools\GpuProbe -- inventory --seconds 10
dotnet format --verify-no-changes
.\scripts\publish.ps1
```

当前最终结果：普通测试 52 通过、0 失败、3 个硬件测试按策略跳过；显式硬件测试 55 通过、0 失败、0 跳过。Release 构建 0 警告、0 错误，格式检查通过。`GpuProbe registry`、`adapters`、30 秒 `sample` 和 10 秒 `inventory` 均以退出码 0 完成；采样中 `unparsed=0`。

0.4.0 UI 自动验收确认：标准最小化/最大化/关闭窗控存在；1000×620 最小尺寸下表格、详情和适配器页均可滚动；勾选后选择数量、详情、“设置偏好”和“忽略”即时更新；连续采样未出现 `Refresh/AddNew/EditItem` 异常；三轮 Alt+Tab 后未出现黑色背景。Portable 与 SingleFile 两种实际发布产物均在当前 Windows 11 机器成功显示、切换到“显卡与系统”页并以退出码 0 正常关闭。

- Portable ZIP：87,079,175 bytes，SHA-256 `9e50cf88f6869b0ab1b2936a7464268b07b712537138d5cf1366b12250566078`；
- Single EXE：209,244,472 bytes，SHA-256 `44c870fb3a4564b30ceaf758111cd855d77a98319fccdca27701a05065c9e249`。

0.4.1 最终验收：Release 构建 0 警告、0 错误；包含真实 Windows 硬件项在内共 51 项测试全部通过，0 失败、0 跳过。UI 自动验收确认 1 秒连续刷新期间选中高亮、详情和命令状态稳定，多进程行可展开，且主表不再出现横向滚动条。当前机器只显示 RX 6650 XT 与物理 Radeon 核显两个可分配设备，自动排除 2 个非物理适配器。

- 0.4.1 Portable ZIP：87,081,773 bytes，SHA-256 `f85195ce82c83873337269a34237e8fb2b2ea67a608dfa52ac089df74e2a8789`；
- 0.4.1 Single EXE：209,252,664 bytes，SHA-256 `89d8041865c601409847356851d6292ec11741ac28eacde1d304a8d9dfa2996b`。

0.5.0 最终结果：包含真实 Windows 硬件项在内共 52 项测试全部通过，0 失败、0 跳过；Release 构建 0 警告、0 错误。真实鼠标 UI 验收确认多进程父行在三轮 1 秒采样后仍保持展开，Chrome 两个子进程分别显示 `chrome.exe`、PID、核显和各自显存。

- 0.5.0 Portable ZIP：87,085,713 bytes，SHA-256 `eef1089f54b2f76ab142daed9d2d13594b2665d6844f9d5e03ce91afba08eb6c`；
- 0.5.0 Single EXE：209,256,760 bytes，SHA-256 `6881f6454bbbf297cb78db1e641fea6b882e1777cedabcea290d92fa5626303a`。

0.8.0 基础功能复核修复：多 EXE 组可以逐个选择偏好作用目标，规则不一致时主表不再伪装为单一规则；注册表写入和回滚安全拒绝非 `REG_SZ` 原值；PDH 数组可在实例变化时重试扩容；`GpuProbe inventory` 与主界面统一使用完整进程快照；注册表并发删除和峰值缓存过期不会中断或持续积累。按用户要求，运行期间重新枚举显卡未实现，适配器仍在监控启动时枚举一次。UI 自动验收确认原生窗口控制、选择即时响应、连续采样及多进程展开稳定，并确认详情页存在明确的 EXE 目标选择器。

- 0.8.0 Portable ZIP：87,097,397 bytes，SHA-256 `e5b45b6f0c26dd6873424942bcf89573a47801ae35d8718e1a7282951afecbae`；
- 0.8.0 Single EXE：209,286,968 bytes，SHA-256 `1b01e5a660c84391f0ad66deb2d3bef1670e6109000517f46cb1b8ba35ab218b`。

## 尚未完成的真实机器验收

见 `docs/MANUAL_TEST_REPORT.md`。主要是：真实 DirectX 键写入和逐字符恢复、目标程序重启后的实际 GPU 调度、一小时稳定性、异常杀进程，以及干净 Windows 11 环境的两种发行包验证。自动测试不能替代这些项目。
