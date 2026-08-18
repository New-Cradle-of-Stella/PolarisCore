# PolarisCore

Polaris 的核心运行时与唯一 BepInEx 插件入口。它提供公共 API、组件宿主、基础诊断契约和游戏绑定，并由 [Polaris](https://github.com/New-Cradle-of-Stella/Polaris) 聚合仓库作为 Git submodule 引用。

此仓库通常通过 Polaris 聚合仓库构建，以便获得兄弟模块和共享构建配置。

## Game API 文档

- `doc/specs/Polaris-Game-API-Spec-v3-静态与实例模型.xlsx`：Game API v3 公共表面规格。
- `doc/design/GameAPI-游戏页面与菜单边界说明.md`：菜单、暂停和取消输入的职责边界。
