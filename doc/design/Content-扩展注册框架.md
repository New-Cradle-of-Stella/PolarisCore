# Polaris.Content：扩展文件注册框架

## 解决什么问题

Polaris 里已经有五个模块各自定义了一种自定义文件扩展/内容类型：`.plang`
（PolarisLang）、原始资源与 PixelLiner（PolarisRes）、`.pai`/`.pnpc`
（PolarisAI）、`.pmap`（PolarisMap）、物品/插件/技能目录（PolarisAddons）。核对
下来，其中"扫描标了某个特性的类 → 实例化 → 调用注册方法"和"按 Key 存进一个
字典、重复 Key 要判断是覆盖还是冲突"这两步几乎是重复造轮子：`.plang` 与
`.pnpc` 的实现几乎逐行相同，`.pai`（`BehaviorRepository`）和 Addons
（`AddonCatalogBuilder`）的字典+去重判断也是同一套逻辑的另一种写法。

`Polaris.Content`（本文件夹）把这两步收敛成两个可直接复用的类型，新增一种
扩展文件时不用再重写它们：

- **`ContentRegistrarScanner`** ——发现＋调用。替代
  `PlangRegistryScanner`/`PnpcRegistry.ScanModules` 这类手写扫描循环。
- **`ContentCatalog<TKey, TValue>`** ——按 Key 注册＋冲突处置。替代
  `PlangRuntime`+`PlangConflictGuard`、`PnpcRegistry`、`BehaviorRepository`、
  `AddonCatalogBuilder` 里各自的字典和 if/throw。
- **`ContentDiagnostic`/`ContentDiagnosticSeverity`** ——统一的诊断值类型，
  替代 `PlangConflict`/`PaiDiagnostic`/`PnpcDiagnostic` 这类各自定义的小结构。
- **`ContentHotReloadWatcher`** ——按时间戳轮询目录的通用热重载工具，从
  `PolarisAI` 的 `PaiHotReload` 抽出来，供任何"目录里的文件会被模组作者
  实时编辑"的场景复用。

## 新增一种扩展文件时怎么接入

1. **发现**：给生成的/手写的注册类标一个自定义特性，实现一个只含
   `Register(...)` 的接口（签名随意，`ContentRegistrarScanner` 不关心），
   然后调用：
   ```csharp
   ContentRegistrarScanner.ScanAndRun<MyAutoRegistrationAttribute, IMyRegistrar>(
       (registrar, type) => registrar.Register(/* 你的注册协议 */));
   ```
2. **注册**：内容用一个 `ContentCatalog<TKey, TValue>` 存起来，构造时选好
   冲突策略——多个模组共享同一个全局命名空间（如本地化 Key、NPC id）通常选
   `Aggregate`，扫描全部结束后 `Seal(...)` 一次性汇总成一条致命错误；
   单文件覆盖式的内容（如 `.pai` 行为树按文件路径去重）通常选
   `ThrowImmediately`，调用方在 try/catch 里单条上报即可。
3. **热重载**（可选）：磁盘上的文件需要免重启生效时，用
   `ContentHotReloadWatcher` 包一层，在组件的 `Update()` 里 `Tick`。

## 这个框架不管什么

刻意没有往这四个类型里塞的东西，遇到时不必强凑：

- **依赖注入/IoC** ——PolarisAddons 的 `AddonServices` 是独立的组合根，跟
  "注册一个 Key 对应的内容"是两回事。
- **多挂载点优先级解析与引用计数租借** ——PolarisRes 的 `MountTable`/
  `ResourceCache` 需要的语义（按优先级找文件、按引用计数释放）比一个扁平
  的键值目录复杂，硬套 `ContentCatalog` 会丢语义。
- **进程间协议** ——PolarisMap 的 `PmapHotReloadServer` 是跟 PolarisTools
  外部编辑器对话的命名管道协议服务端，不是文件轮询，跟
  `ContentHotReloadWatcher` 是两种不同的东西。
- **解析/校验/编译的具体规则** ——每种文件格式的语法、Schema、编译产物
  是该格式自己的事；`.pai`/`.pnpc` 的这部分特意留在
  `PolarisAI.Authoring`（不依赖 PolarisCore，供外部工具单独引用）。

## 各模块落地情况

见各模块 README 与提交记录；概览见仓库根 `doc/PROJECT_STRUCTURE.md`。
