GPU Preference Manager 0.8.0
================================

Windows 11 x64 GPU 偏好审计与管理工具。

项目初衷：在显示器分别连接核显和独显等混合输出场景中，集中查看每个程序实际使用的 GPU，并直接调整应用偏好，避免反复进入 Windows 设置手动寻找 EXE、逐个添加和修改。

AI 创作声明：本项目的架构、代码、测试、界面和文档几乎完全由 OpenAI Codex 根据用户需求与真实机器反馈创作；用户负责产品方向、体验反馈及最终验收。本项目尚未经过独立的专业安全审计。

重要：GPU 偏好不是驱动级强制规则。写入后请重启目标程序，并结合任务管理器核对实际 GPU 活动。

运行：双击 GpuPreferenceManager.exe。
数据：%LocalAppData%\GpuPreferenceManager
备份：%LocalAppData%\GpuPreferenceManager\Backups
日志：%LocalAppData%\GpuPreferenceManager\Logs

程序不会自动结束目标应用，不会自动修改 DirectXUserGlobalSettings，也不上传遥测。
