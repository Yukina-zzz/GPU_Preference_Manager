# 真实机器手工验收报告

日期：2026-08-02
环境：Windows 11 x64，.NET SDK 10.0.302，AMD Radeon(TM) Graphics + AMD Radeon RX 6650 XT

## 已实际验证

- [x] WPF 主窗口可启动、显示实时应用库存和适配器卡片，并可正常关闭；
- [x] DXGI 枚举 RX 6650 XT 专用显存 8,541,929,472 bytes（7.96 GiB），不是 WMI 的 4 GiB；
- [x] RX 6650 XT key 为 `1002&73EF&1EFE`，全局高性能规则唯一映射到该适配器；
- [x] 当前真实注册表能只读识别 18 条应用规则和 1 条全局设置；
- [x] PDH 真实采样和应用库存命令返回数据，实例解析错误数为 0；
- [x] 随机测试键上的写入、精确校验、未知字段保留、备份、撤销、冲突和 Pending 恢复通过自动测试；
- [x] Portable 与 Single EXE 均能由 Windows SDK 生成，并在当前机器成功显示主窗口、正常关闭（退出码 0）。

## 仍需人工执行，当前不得标为通过

- [ ] 在真实 `UserGpuPreferences` 上选择一个可安全重启的测试应用，完成核显→独显→撤销→baseline 的逐字符恢复流程；
- [ ] 重启测试应用后，与任务管理器交叉核对实际 GPU 调度；
- [ ] 大型游戏场景对比独显显存和任务管理器；
- [ ] 连续运行至少 1 小时并记录 CPU、工作集、滚动位置和崩溃情况；
- [ ] 模拟写入进程被杀后的真实启动恢复；
- [ ] 在干净 Windows 11 x64 环境分别启动 Portable ZIP 和 Single EXE；
- [ ] 确认杀毒软件/SmartScreen 行为。

未执行原因：主任务最初明确禁止写入真实 `UserGpuPreferences`；一小时稳定性、目标应用重启后的 GPU 调度和干净机器兼容性也不能由当前自动测试诚实替代。

## 当前硬件不确定项

DisplayConfig 已把第二个 LUID 可靠关联到 `ROOT\DISPLAY\0000`，界面显示为 `GameViewer Virtual Display Adapter` 并排除分配。它与物理核显的 SpecificAdapter key 仍同为 `1002&164E&164E1002`，因此精确指定核显继续保持禁用，不按名称差异猜测 Windows 的 key 匹配行为。
