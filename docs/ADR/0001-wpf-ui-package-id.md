# ADR 0001：修正 WPF UI 的 NuGet 包标识

- 状态：接受
- 日期：2026-08-02

## 背景

项目早期设计稿把界面库的 NuGet 包标识写为 `Wpf.Ui 4.3.0`。NuGet 中该标识属于另一个最高仅为 3.4.2.7 的包；Lepo 官方 WPF UI 4.3.0 的实际包标识为 `WPF-UI`，其 C# 命名空间才是 `Wpf.Ui`。

## 决策

中央包管理和 App 项目使用 `WPF-UI 4.3.0`，保留代码中的 `Wpf.Ui` 命名空间。

## 验证

以 NuGet 官方包页和 V3 flat-container API 为准，并通过 `dotnet restore` 验证。
