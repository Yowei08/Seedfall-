# Seedfall 守园者 · 工程文档索引

「种地 × 幸存者」融合玩法：把种子种成植物帮手，用地换时间、用时间换火力，撑过 10 波（双 Boss）。

## 交付物

| 文档 | 内容 |
|---|---|
| [设计文档](design/seedfall-garden-survivors.md) | 一句话内核、核心循环与时长、13 条议题决策、全部数值与公式、16 个种子、4 类敌人、波次表、经济、UI、埋点与验收、风险 |
| [架构文档](architecture/seedfall-core.md) | 子系统边界与硬性分层、关键接口签名、配置表 schema、两张状态机、单帧顺序、确定性策略、失败模式降级、测试覆盖、未决项 |
| [迭代日志](reports/iteration-log.md) | 4 组对比数据（含一次被数据否定的改动）、软锁 bug 的发现与修复、阶段结论与下一步 |
| [回归报告目录](reports/) | `sim-<时间戳>.txt`：策略矩阵 + 确定性校验，每次跑局自动落盘 |

## 怎么跑起来

1. Unity Hub / 编辑器打开 `D:\Unity\project\Seedfall守园者`（Unity 6000.4.6f1）。
2. 打开场景 `Assets/Scenes/SeedfallArena.unity`，按 Play。
3. 操作（v5）：`WASD` 移动｜**鼠标左键 朝鼠标方向主动攻击**｜滚轮 换武器（0.5s 前摇）｜`G` 锁定后自动攻击｜`E` 耕作（按住）｜`Q` 铲幼苗｜**鼠标点击 选种 / 合体 / 购买**｜空格 商店进下一波｜`R` 刷新｜`Tab` 显示/隐藏左侧按键说明（**键位说明只在这里**）｜`Esc` 菜单。
4. **每波开头有 10 秒种植时间**（不刷怪），先开垦播种；回合结束田地被刷新回"未开垦"。

**开局流程**：三选一 D 级种子 → 10 秒种植窗口内掘地/播种/洒水/施肥 → 幼苗**不会被攻击**，长大后自动作战 → 捡钱升级 → 波间商店买种子/道具。

## 自动化

| 入口 | 用途 |
|---|---|
| 编辑器菜单 `Tools/Seedfall/校验配置表` | 只校验 JSON 合法性 |
| 编辑器菜单 `Tools/Seedfall/策略矩阵回归` | 四策略各跑满 10 波 |
| 编辑器菜单 `Tools/Seedfall/确定性校验` | 同种子两次跑局逐字段对比 |
| `DebugApi.RunRegression("20240607")` | 矩阵 + 确定性，报告落盘 `Docs/reports/` |
| `DebugApi.SelfCheck()` | **71 条规则/公式断言**（含改版规则），无人值守可跑 |
| `DebugApi.Status()` / `PlotDump()` / `PlantDiag()` | 现场诊断（阶段/波次/血量/武器/锁定/无敌/种植时间） |
| `DebugApi.AuditRotations()` | **批量旋转审计**（play 后 8 秒自动跑）：① 场景实例按偏差降序前 10（分类：`MDL_` 外层 / `ANIM_` 净旋转 / 手写）② Resources 里每个预制体根节点旋转清单 ③ `ANIM_player` 个案分解 ④ **立姿断言 PASS/FAIL**。用来一次定论"哪个模型倒下了" |
| `DebugApi.SetBot("Balanced")` | 让 Bot 接管一局（用于无人值守冒烟） |
| `DebugApi.SetWeapon("can")` / `ScrollWeapon("1")` / `ToggleLock()` / `ToggleMenu()` | 验证滚轮换武器、G 锁定、Esc 菜单 |
| `DebugApi.GrantSeed("pea_shooter::0")` / `ClickSlot("0")` | 验证品质与"点击两个相同种子合体" |
| `DebugApi.ForceShop()` | 直接进入商店，用于验证商店 UI |
| `DebugApi.Capture("hud.png")` | **整屏截图（含 IMGUI 覆盖层）** 到 `Docs/captures/`，用于 HUD/轮盘视觉回归 |
| `DebugApi.GrantSeed("frost_ivy:twin")` / `GrantItem(...)` / `AddGold` / `JumpToWave(5)` | 造场景验证特定分支 |
| `-batchmode -executeMethod Seedfall.EditorTools.SeedfallMenu.RunAll` | CI 无头回归（关闭编辑器后执行） |

### 读数陷阱（踩过的，动手前先看这三条）

| 陷阱 | 事实 |
|---|---|
| `unity_probe` 的 `rotation` 是**世界**旋转 | 它读的是 `transform.eulerAngles`（见 `YoweiBridge.cs:868`），**不是** `localRotation`。要判"模型本身姿态"时必须自己分清外层 `MDL_` / 内层 `ANIM_` |
| `eulerAngles.x` **取不到 180** | Unity 按 asin 取，落在 `[0,90] ∪ (270,360]`。所以 `Ry(yaw)·Rx(180)` 会报成 `(0, yaw+180, 180)` —— 看到 `z=180` **不等于**绕 Z 转了 180° |
| 根节点的 `rotation.x = 270` **不是不变量** | 这批 fbx 文件自己声明 `UpAxis=1`（Y-up），270 是**资产根 Model 自带**的旋转（Blender 导出留下的），不是导入器加的轴转换。所以 `rotation.x = 0` 也可能完全正确。**而且"应用一次(270✓)"与"应用两次(540=180✗)"在根节点的 rotation.x 上长得一模一样** ⇒ 判据必须用「净旋转 + 世界 AABB 立姿」，不能用根节点角度 |

## 目录

```
Assets/
  Resources/Data/       数值配置（tuning / seeds / enemies / waves / items）
  Scripts/
    Core/               固定步长时钟、确定性随机、事件总线
    Data/               配置 DTO 与校验
    Stats/              属性块与等级成长
    Combat/             伤害/辅助公式、生命池、异常状态
    Farm/               耕地格状态机与网格解锁
    Actors/             玩家、植物帮手、四类敌人、弹道、掉落、World
    Flow/               局会话、种子袋、波次导演、商店、埋点
    UI/                 原型 HUD（IMGUI）
    Bootstrap/          场景装配、输入、表现同步
    Automation/         跑局 Bot、无头模拟与报告
    Debug/              自动化验证与诊断入口
  Editor/               编辑器菜单入口（含 CI -executeMethod）
  Tests/EditMode/       公式与配置表单测
  Scenes/SeedfallArena.unity
Docs/                   本文档目录
```
