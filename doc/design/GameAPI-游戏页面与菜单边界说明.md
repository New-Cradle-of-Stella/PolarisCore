# GameAPI 与 PolarisGameMenu API 归属说明

## 目的

明确 `PolarisAPI.Game` 与 `PolarisAPI.GameMenu` 的职责，消除游戏菜单相关 API 的重复入口。

## 确认结论

### 1. 查询游戏菜单：归 GameAPI

统一使用：

```csharp
PolarisAPI.Game.Menu.IsOpen
PolarisAPI.Game.Menu.Current
```

不再由 `PolarisAPI.GameMenu.IsOpen` 提供游戏页面状态查询。

### 2. 打开游戏菜单：归 GameAPI

统一使用：

```csharp
PolarisAPI.Game.Menu.Open()
PolarisAPI.Game.Menu.TryOpen(out GameMenu menu)
```

原 `PolarisAPI.GameMenu.Pause()` 迁移到 GameAPI。实现应统一走原版正常的菜单打开流程，不能保留两套
不同的打开逻辑。

### 3. 关闭游戏菜单：归 GameAPI

统一通过当前菜单实例关闭：

```csharp
PolarisAPI.Game.Menu.Current?.Close();
```

原 `PolarisAPI.GameMenu.Resume()` 迁移到 GameAPI。

### 4. 世界暂停策略：归 PolarisAPI.GameMenu

保留：

```csharp
PolarisAPI.GameMenu.SetWorldPause(bool enabled)
PolarisAPI.GameMenu.PauseWorldWhileOpen
```

移除重复且参数语义相反的：

```csharp
PolarisAPI.Game.World.SetPauseSimulation(bool simulation)
```

`SetWorldPause` 配置的是 Polaris 游戏菜单打开时是否暂停世界，因此属于菜单扩展策略，不属于游戏页面
实例操作。

### 5. 两种取消输入查询：都保留

```csharp
MainMenuAPI.IsCancelInputPressed()
PolarisAPI.Game.Input.WasPressed(GameInputAction.Cancel)
```

两者用途不同：

- `MainMenuAPI.IsCancelInputPressed()` 服务于标题菜单，按标题界面的固定 `Escape/X` 规则判断。
- `Game.Input.WasPressed(GameInputAction.Cancel)` 查询游戏动作映射，跟随玩家的改键设置。

它们不是重复 API，不进行合并。

## 最终归属

| 能力 | API |
| --- | --- |
| 查询游戏菜单 | `PolarisAPI.Game.Menu` |
| 打开游戏菜单 | `PolarisAPI.Game.Menu` |
| 关闭游戏菜单 | `GameMenu` 实例 |
| 设置菜单打开时是否暂停世界 | `PolarisAPI.GameMenu` |
| 注册游戏菜单分类 | `PolarisAPI.GameMenu` |
| 标题菜单固定取消键判断 | `MainMenuAPI` |
| 游戏动作取消键判断 | `PolarisAPI.Game.Input` |

## 迁移方式

旧方案是先标记 `Obsolete` 再删除。实际执行时按 v3 的破坏性变更窗口处理，旧入口<b>直接移除</b>，
不保留转发层——保留第二份入口正是本文要消除的问题。

## 迁移状态（已完成）

以下迁移已经落地，旧入口均已移除：

| 旧 API | 新 API | 状态 |
| --- | --- | --- |
| `PolarisAPI.GameMenu.IsOpen` | `PolarisAPI.Game.Menu.IsOpen` | 已移除 |
| `PolarisAPI.GameMenu.Pause()` | `PolarisAPI.Game.Menu.Open/TryOpen` | 已移除 |
| `PolarisAPI.GameMenu.Resume()` | `PolarisAPI.Game.Menu.Current?.Close()` | 已移除 |
| `Game.World.SetPauseSimulation(bool)` | `PolarisAPI.GameMenu.SetWorldPause(bool)` | 已移除 |

配套变更：

- `Game.Menu.Open/TryOpen` 走 `NelM2DBase.menu_open` 的原版正常请求流程，GameAPI 内不再存在
  直接调用 `UiGameMenu.activate()` 的第二套打开路径。
- `GameMenu` 包装器覆盖「请求已接受 → 打开 → 关闭/取消」整段生命周期：待处理期间
  `Menu.IsOpen == false`、`Menu.Current == null`，实际打开以 `GameMenuOpened` 回调为准；
  对待处理包装器调用 `Close()` 会撤回请求。
- v3 表格已同步：删除 `World.SetPauseSimulation`，补入 `Menu.IsOpen` 与 `Menu.TryOpen`。
