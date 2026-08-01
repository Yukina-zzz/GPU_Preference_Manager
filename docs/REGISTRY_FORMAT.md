# UserGpuPreferences 注册表格式

目标键为当前用户的 `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`。普通值名是 EXE 完整路径，值通常为 `REG_SZ`；`DirectXUserGlobalSettings` 是全局值，应用列表和普通写入必须排除它。

项目已识别的 GPU token：

```text
GpuPreference=1;                                  通用节能
GpuPreference=2;                                  通用高性能
SpecificAdapter=1002&73EF&1EFE;GpuPreference=1073741824;  特定适配器
```

`SpecificAdapter` key 由 `VendorId&DeviceId&SubSysId` 构成，使用大写十六进制并移除 SubSysId 前导零。`1073741824` 仅作为 Windows 的不透明特定适配器模式标志。

序列化器保持 token 顺序、未知字段和无等号异常 token。修改 GPU 规则时会移除所有旧 GPU token，再写入目标 token；清除偏好时只清除 GPU token。相同 key 对应多个不同 LUID 时标记为歧义并禁止精确写入。
