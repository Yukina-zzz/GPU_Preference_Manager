# 实现状态

更新日期：2026-08-02
应用版本：0.9.0

## 当前能力

- 使用 DXGI 枚举 GPU，并结合 DisplayConfig/设备实例信息显示设备管理器名称、排除虚拟和软件适配器；
- 解析和无损序列化 `UserGpuPreferences`，保留未知字段及异常 token；
- 通过 PDH 采集逐进程 GPU 引擎、专用显存和共享显存，并按完整进程树归组；
- 支持前台应用/后台进程、搜索、自定义筛选、排序和逐进程展开；
- 多 EXE 应用逐项显示独立规则，写入前明确选择实际可执行文件；
- 支持不依赖批量勾选的单项右键偏好与忽略操作，菜单打开期间冻结表格更新；
- 支持本地手动指定高性能/节能 GPU、手动排除、恢复和强制恢复自动排除项；
- 支持通用节能、通用高性能、特定适配器及清除偏好；
- 使用 SQLite 保存 baseline、事务历史和忽略列表，写入前导出 `.reg` 备份；
- 支持读回校验、失败补偿、冲突检测、撤销、回滚到节点和恢复 baseline；
- 提供 `GpuProbe registry/adapters/sample/inventory`、诊断 ZIP、Portable 和 SingleFile 发布，并由 GitHub Actions 生成校验值和正式 Release。

## 安全边界

- 只管理当前用户的 `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`；
- `DirectXUserGlobalSettings` 保持只读；
- 非 `REG_SZ` 应用值拒绝写入和类型降级回滚；
- 适配器身份歧义时始终禁止精确分配；虚拟、软件和远程设备默认排除，强制恢复也不会绕过设备键安全检查；
- 测试写入仅使用随机 `HKCU\Software\GpuPreferenceManager.Tests\<Guid>`；
- 不自动结束或重启目标程序，不承诺应用一定遵循 Windows GPU 偏好；
- EXE 当前未进行代码签名，首次下载可能触发 SmartScreen 提示。

## 当前验收结果

- Windows 11 x64、.NET SDK 10.0.302，Release 构建 0 警告、0 错误；
- 普通测试 59 通过，3 个硬件测试按策略跳过；
- 3 个显式真实硬件测试通过，0 失败、0 跳过；
- `dotnet format --verify-no-changes` 通过；
- 四项 `GpuProbe` 验收命令退出码均为 0，30 秒 PDH 采样 `unparsed=0`；
- UI 自动验收确认原生窗口控制、选择即时响应、持续采样和多进程展开稳定；
- `SQLitePCLRaw.lib.e_sqlite3` 已从命中高危公告 `GHSA-2m69-gcr7-jv3q` 的 2.1.11 固定到 2.1.12，发布前重新执行 NuGet 漏洞扫描。

## 当前机器适配器结果

- AMD Radeon RX 6650 XT：`1002&73EF&1EFE`，可分配，高性能角色；
- AMD Radeon(TM) Graphics：`1002&164E&164E1002`，可分配，节能角色；
- GameViewer Virtual Display Adapter：自动识别并排除；
- Microsoft Basic Render Driver：根据 DXGI Software 标志排除。

## 有意未实现

- 运行期间重新枚举显卡；显卡热插拔、驱动重启或拓扑变化后需重启本程序；
- 自动结束或重启目标应用；
- 驱动级强制指定 GPU。

## 仍需外部环境验证

- 在可安全重启的真实应用上完成偏好切换、进程重启和任务管理器交叉核对；
- 连续运行一小时的资源与稳定性观察；
- 干净 Windows 11 x64 设备上的安装、SmartScreen 与杀毒软件行为；
- 其他 GPU 厂商、更多虚拟显示驱动和远程桌面拓扑。

## 0.9.0 发布文件

发布文件及最新 SHA-256 以 GitHub Release 页面和随附的 `SHA256SUMS.txt` 为准。
