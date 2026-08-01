# GPU 性能计数器说明

实时采样通过英文 PDH wildcard 计数器读取：

```text
\GPU Process Memory(*)\Dedicated Usage
\GPU Process Memory(*)\Shared Usage
\GPU Engine(*)\Utilization Percentage
```

实例名 parser 提取 PID、LUID 高低段、物理分区、引擎编号、引擎类型和重复后缀。LUID 与 DXGI 适配器关联时同时验证字段顺序，不能关联的实例仅进入诊断，不猜测 GPU 编号。

同一计数器重复实例取最大值，物理分区求和；引擎按类型取最大值；之后按进程创建时间防止 PID 复用，再按规范化 EXE 路径合并多进程。首帧只用于 PDH 预热，引擎百分比从后续帧起有效。

PDH 不可用时，UI 显示错误状态并保留注册表管理能力。诊断 ZIP 包含采样结果或明确错误码。
