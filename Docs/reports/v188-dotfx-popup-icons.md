# v188 特效表现 ＋ 弹字图标（交付报告）

> 需求原文：毒素地面可见效果 / 灼烧冒小火花 / 减速蓝色向下箭头 / 弹字图标前缀（小火苗·水滴·骷髅头）/ 肥料蘑菇云 1 秒渐渐消散且子爆继承。
> 允许改的文件：`WorldView.cs`、`PluginDots.cs`、`PluginFx.cs`、`Actors.cs`（弹字段）、`ImGuiHud.cs`（弹字绘制段）、`DebugApi.cs`（只加探针）、`Tools/mesh_gen/`。

## 一、结论（先给出判据）

| # | 要求 | 判据 | 结果 |
|---|---|---|---|
| ① | 毒区地面**实际可见** | 图 `item1a_poison_visible2.jpg` ＋ 读数 `毒区块=1(r=2.40 件=3 活=是)｜气泡=4` | ✅ 暗橄榄绿 9 边形渍 + 酸性边环 + 上升气泡 |
| ① | 到期**当帧消失、不残留** | 图 `item1b_poison_expired.jpg` ＋ 读数 `毒区块=0｜在场特效件合计=0` | ✅ 到期后同帧销毁（无淡出尾巴） |
| ② | 灼烧敌人**冒小火花**，跟人走 | 图 `item2_burn_zoom.jpg`（同一帧左 1 层 / 右 3 层） | ✅ 橙色火星在敌人身上持续升起 |
| ② | **层数越多越密** | 读数：1 层=14 颗/秒、3 层=42 颗/秒；实测在场 `灼烧火星` 5 颗（1 层）↔ 18 颗（3 层） | ✅ 疏/中/密可分层 |
| ③ | 减速敌人**很多小的向下蓝箭头** | 图 `item3_slow_zoom.jpg`（同帧左 1 层 / 右 5 层） | ✅ 尖端朝下、绕身下落、明暗循环 |
| ③ | 层数越多越多（5 层封顶，给读数） | `SlowArrowCount(n)=3n`；实测 `减速箭头 共=3 亮=3 需要=#25→3支` ↔ `共=18 亮=15 需要=#29→15支` | ✅ 1..5 层 = 3/6/9/12/15 支 |
| ④ | 三种伤害弹字带**专属图标前缀** | 图 `item4_popup_zoom.jpg`（同帧三条）＋ `PROBE_DOTPOPUP` 读数 | ✅ 火苗+`-18`(橙) / 水滴+`-20`(蓝) / 骷髅+`-10`(绿) |
| ④ | 不能用 emoji（说明做法） | 见下文「四、为什么不用 emoji」 | ✅ 走 11×11 位掩码贴图 |
| ⑤ | 蘑菇云**持续 1 秒渐渐消散** | 读数：出生时 `最长剩=1.00s`；0.5s `0.65`、0.78s `0.32`、1.02s `0.10`、1.28s `0.00` | ✅ 整朵在 1.0s 内清空 |
| ⑤ | **子爆炸同样继承** | 出生读数 `茎=16 帽=32 卷=20 壳开=2 壳散=2`（= 主爆 8/16/10 + 子爆 8/16/10 两朵）；图 `item5_start.jpg` 可见主爆与子爆两朵云 | ✅ 主爆/子爆走同一个 `SpawnFertMushroom` |

## 二、实现要点（按文件）

### `Assets/Scripts/Combat/PluginDots.cs`
- 新增 `DotPopupHint`：**持续伤害 → 弹字**的同一调用栈交接（`Set` 在 `Hit` 前、`Clear` 在 `finally`）。
  不能靠 `DamageRequest.Type` 分辨：肥料爆炸（`RunSession.BlastDamageAt`）用的也是 `DamageType.Ailment`。
- `Hit(...)` 多带 `DotKind kind, int stacks`，由 `Settle` 传入（`Burn`/`Slow`/`PoisonZone`）。

### `Assets/Scripts/Actors/Actors.cs`（弹字 / `PopupStyle` 段）
- `PopupKind` 末尾追加 `DamageBurn / DamageSlow / DamagePoison`（既有 13 个值编号不动）。
- `PopupStyle.ColorOf`：灼烧橙 `(1.00,0.62,0.16)`、减速蓝 `(0.42,0.78,1.00)`、中毒绿 `(0.46,1.00,0.42)`。
- `PopupStyle.IconOf(kind)`：唯一分派点 → `PopupIcon.Flame/Drop/Skull`。
- `PopupStyle.DamageKindFor(DotKind)`：持续效果种类 → 弹字档。
- `ScaleOf`：三种持续伤害 **0.88**（每秒一跳的字不该压住敌人本体）。
- `OnHitLanded`：`DotPopupHint` 有值 ⇒ 走三档专属（优先于暴击）。

### `Assets/Scripts/UI/ImGuiHud.cs`（弹字绘制段）
- `DrawPopups` 里加**图标前缀**：`GUI.DrawTexture` + `GUI.color = 弹字色`，四向描边与文字同一套；
  图标+数字作为**一对**以世界点居中（文字框整体让出 `iconPx + gap`）。
- 11×11 位掩码（`IconFlame/IconDrop/IconSkull`）→ `Texture2D`（Point 采样、`DontSave`），三张只生成一次。

### `Assets/Scripts/Bootstrap/WorldView.cs`
- `SyncDotFx(s, dt)`（挂在 `SyncEnemies` 之后）：
  - `SyncPoisonZones`：每帧从 `PluginDotTable.Rows` 认领毒区行（0.15m 同块，与 `AddPoisonZone` 同口径），
    **没被认领的当帧 `SetActive(false)` + 销毁**；三层贴地件（9 边形渍 / 7 边形内层 / 边环）+ 气泡。
  - `SyncBurnSparks`：发射率 `BurnSparkRate(stacks)=14×层数`，火星 0.26~0.46m、寿命 0.28~0.40s、向上飘。
  - `SyncSlowArrows`：`SlowArrowCount(stacks)=3×层数`（封顶 15），复用 `StatusArrowMesh()`（尖端朝下金字塔），
    颜色取 `PopupStyle.ColorOf(PopupKind.DamageSlow)`，绕身下落 + 缩放呼吸。
- `SpawnFertMushroom(blastAt, R, fx)`：茎/帽/翻卷 + 两层球壳（撑开 / 消散），**主爆与子爆共用**。
  寿命设计成"结束时刻 = Total + |T|"落在 **0.62~1.00s** 区间 ⇒ 整朵云 1 秒内清空、且块是**一块块**消失的。
- `DotFxReport()`：表现层读数（毒区块/气泡/火星/箭头需求/蘑菇云五组构件 + 最长剩余寿命）。
- `Clear()` 里 `ClearDotFx()`（换局不残留引用）。

### `Assets/Scripts/Debug/DebugApi.cs`（只加探针）
- `ProbeDotFx(spec)`：活会话里搭"带层数的敌人 + 毒区"，spec = `burn:N,slow:N,poison:N,dist:F,span:F,side:F`。
- `DotFxStatus()`：只读表现层读数。
- `ProbeDotPopups()`：走真路（`Dots.Tick` → `Hit` → `OnHitLanded`）让三种持续伤害各结算一次，打印 kind/色/图标。
- `ProbeFertCloud(spec)`：玩家前方引爆（`sub` = 装 `fert_subburst`，子爆同框），返回构件数量。

## 三、硬约束核对

- 编译：`dotnet build Seedfall.Runtime.csproj -t:Rebuild` ⇒ **0 error / 10 warnings**（与基线一致）。
- 自检：`DebugApi.SelfCheck` ⇒ **`自检 PASS：439/439 通过`**（分母未变，未删任何断言）。
- 零分配：每帧只有读表 + 写 transform；网格走 `_fxMeshCache`（5730/5732/5740 与既有键不撞）、材质走 `VfxMatCached`、
  粒子走既有 `_fxMeshes`。新建对象只发生在"层数变大 / 毒区新出现 / 气泡发射 / 一次爆炸"（事件频率，非帧频率）。
- 未碰：`Flow/RunSession.cs`、`ModelView.cs`、`Model/**`、`GameBootstrap.cs`、`ShopTruck.cs`、`Data/*.json`、`Resources/**`、`Tools/audio_gen/**`；
  `WorldView.cs` 里 **boss 入场段**与**水球特效段**（另一 agent 的）保持原样。

## 四、为什么不用 emoji（做法与理由）

- 本 HUD 的字体是 `Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16)`（`ImGuiHud.cs`），
  `🔥`(U+1F525) / `💧`(U+1F4A7) / `💀`(U+1F480) 属于 emoji 平面：**雅黑没有这些字形**，
  IMGUI 的动态字形光栅器也**不认彩色 emoji 字形表**（COLR/CBDT），结果只会是豆腐块/空白 —— 与系统是否装了 Segoe UI Emoji 无关。
- 所以图标走**程序化小贴图**：11×11 位掩码 → `Texture2D`（Point 采样）→ `GUI.DrawTexture`，
  颜色直接取 `PopupStyle.ColorOf(kind)` ⇒ **图标色 = 弹字色是同一个数**，不是两份配置。
- 与字体/平台/打包流程完全解耦；分辨率点采样，放大后仍是方块像素（与本工程的低面数像素语言一致）。

## 五、没做到 / 已知取舍（如实写）

1. **①③⑤ 的图来自"广角玩法机位"，②③ 的对比图做了 3× 放大裁切**（同一张截图裁，不重拍不修图）。
   本机三名 agent 同时操作同一个 Unity 编辑器，`BattleView` 特写机位常被别人的 `FocusFarm/ArtShotOff` 抢走，
   所以最终证据固定在默认玩法机位下拍摄 —— 那也是玩家真正看到的机位。
2. **蘑菇云 3 帧是慢放 2.5×/4× 拍的**（`FxHold` 通道，本工程既有做法）：图上时间被拉长，
   但"1 秒"这条结论由读数给出（出生 `最长剩=1.00s`，1.28s 时 `0.00`），不靠图。
3. **气泡的"消掉"靠缩小**（缩到 35%），不是 alpha 淡出 —— 共享材质契约不允许逐实例改 alpha（`VfxMatCached` 的说明）。
4. **灼烧火星上限 90 颗**：满层 + 慢放 + 多敌人时会被截断，密度对比在最极端情形下不再线性。
5. `DotPopupHint` 是**静态字段**：它成立的依据是"`Hit` → `DealDamage` → `OnHitLanded` 同一调用栈同步"。
   将来若有人把伤害结算改成异步/延迟队列，这里必须改成参数传递（否则标签会串味）。

## 六、证据文件（`Docs/captures/`）

- `fx_r1/item1a_poison_visible2.jpg`、`fx_r1/item1b_poison_expired.jpg`、`fx_r1/item1_poison_zoom.jpg`
- `fx_r1/pair_burn_2.jpg`（全图）+ `fx_r1/item2_burn_zoom.jpg`（3× 裁切，左 1 层 / 右 3 层）
- `fx_r1/pair_slow_2.jpg`（全图）+ `fx_r1/item3_slow_zoom.jpg`（3× 裁切，左 1 层 / 右 5 层）
- `item4_popups_c.png`（ScreenCapture，含 IMGUI）+ `fx_r1/item4_popup_zoom.jpg`（2× 裁切）
- `fx_r1/item5_start.jpg` / `fx_r1/item5_mid.jpg` / `fx_r1/item5_end.jpg` / `fx_r1/item5_gone.jpg`
