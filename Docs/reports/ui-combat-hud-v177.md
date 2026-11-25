# 战斗场景 UI：去底板 + 种子袋按阶段门控 —— 交付报告（v177）

> 任务来源：用户逐条点名的「战斗场景 UI」这一组（第 9 项 + 第 10 项）。
> 改动文件：**只有** `Assets/Scripts/UI/ImGuiHud.cs` 一个（未新增 `.cs` 文件 ⇒ `csproj` 的 39 条 `Compile` 未动）。
> 未触碰：`ShopTruck.cs`、`RunSession.cs` 的商店/波次逻辑、`Assets/Scripts/Art/**`、`seeds.json`、`enemies.json`。

---

## 0. 一句话结论

| 项 | 状态 | 判据 |
|---|---|---|
| ① 血条 / 护盾 / 经验去底板 | **完成** | 前后对比图 `1-hp-bar-{before,after}.png` |
| ② 倒计时去底板 | **完成** | `2-countdown-{before,after}.png`（before/after **都是战斗阶段**，顶部为「第 1 分钟」） |
| ③ 充能（水壶储水 / 肥料）去底板 | **完成** | `3-charge-{before,after}.png` |
| ④ 切换武器（三把 + 插件槽）去底板 | **完成** | `4-weapon-switch-{before,after}.png` |
| ⑤ 种子袋在战斗场景整块不绘制 | **完成**（按阶段门控，非删除） | `5-seed-bag-before.png`（有）/ `5-seed-bag-after.png`（该区域只剩场景） |
| ⑥ 编译 0 error | **完成** | 见 §5 |
| ⑦ `SelfCheck` 425/425 | **完成** | 桥返回原文见 §5 |
| ⑧ `HUD-AUDIT` 仍通过 | **完成** | 战斗阶段与准备阶段各跑一次，均「通过：本帧画出的所有文本都放得下」 |
| ⑨ 最坏底图可读性实测 | **完成** | 夜 t=150 / 黄昏 t=75 / 农田机位 三张（§4） |

**没做到的**见 §7（两条：`耕地提示` 属我**主动扩大**的范围；`按键说明` 我**保留**了底板并说明理由）。

---

## 1. 取证：五类元素的绘制点，以及每处「画底板」的那几句

全部在 `Assets/Scripts/UI/ImGuiHud.cs`（行号为改后文件）。

| 元素 | 绘制函数（改后行号） | 原来画底板的那几句 |
|---|---|---|
| 血条 / 护盾 / 经验 | `DrawVitals` `:1927` | `Panel(new Rect(10,10,330,122))`；三根 `Bar(...)` 内部各画 `UiSkin.BarFrameStyle` 深色底槽 |
| 倒计时 | `DrawWave` `:2588` | `Panel(new Rect(x, 8, 640, 66))` |
| 充能 | `DrawAmmoBar` `:1478` | `Panel(r)`；两根 `Bar(...)` 的 `BarFrameStyle` 底槽 |
| 切换武器 | `DrawWeaponPanel` `:2337` / `DrawWeaponBox` `:2251` | `Panel(new Rect(x,150,380,558))`；`DrawWeaponBox` 里 `GUI.DrawTexture(box, White)` 整行填色（当前=亮绿 0.32/0.55/0.30@0.95，其余=暗灰 0.13/0.15/0.17@0.90）；空插件槽 `GUI.DrawTexture(cell, White)` 0.22/0.23/0.26@0.95 |
| 种子袋 / 植物格 | `DrawPlantPanel` `:2570` + `DrawPlantCell` | `Panel(new Rect(10,354,380,440))`；每格 `PaperFill(cell)` + `ScrollFrame(cell)` |
| （相邻，非点名）耕地双进度条 | `DrawPlotPrompt` `:2674` | `Panel(new Rect(x,y,620,88))` + 两根 `Bar` |
| （相邻，非点名）按键说明 | `DrawKeyHelp` | `Panel(...)` —— **保留未动**，见 §7 |

底板共两张贴图：`Resources/Art/UI/panel.png`（奶油色九宫格，统一 `Panel()` 在用）与 `bar_frame.png`（16×16 深色条底槽，`UiSkin.BarFrameStyle`）。

---

## 2. 改法：三个无底板原语，口径只有一处

新增（`ImGuiHud.cs:1780-1894`）：

| 原语 | 作用 | 为什么这样 |
|---|---|---|
| `StrokeRect(r, c, t)` | 只画四条边 | IMGUI 没有 stroke 原语 |
| `BareFrame(r, stroke, weight, shadow)` | 无底板的框：投影 + **外暗圈 + 内亮圈**两道描边 | 亮底图上外圈暗线划边界，暗底图上内圈亮线划边界 ⇒ 一张图在两种底上都成立 |
| `BarBare(r, pct, fill, text)` | 无底板的条：投影 + 填充（内缩 1.5px）+ 描边 + 内侧压线 + 描边文字 | 填充必须**内缩**，否则满值时填充盖住描边，"条"就没有边界了 |
| `HudLabel(r, text, style, outline, alpha)` | 四向近黑描边文字，描边宽度可调 | 既有 `LabelOutlined` 写法相同但把 1.5px 写死；30px 计时器用 1.5px 是糊的 |
| `HudBody/HudBold/HudTitle/HudCenter/HudTiny` | 压在场景上的近白文字样式 | 默认样式是**墨棕**（给奶油底配的）。不改默认值是刻意的 —— 那会打坏商店/菜单/按键说明 |

**颜色只有四处常量**（`ImGuiHud.cs:79-110`）：`HudStroke`(1,.95,.80@0.92)、`HudStrokeDim`(1,.95,.78@**0.66**)、`HudStrokeCurrent`(0.62,1,0.55@0.95)、`HudShadow`(0.02,0.03,0.02@0.36)、`HudInnerLine`(0,0,0@0.42)。

> `HudStrokeDim` 的 0.66 是**量出来的，不是定的**：第一版 0.40 在农田木色底上，0/100 的空条只剩一圈线，
> 与木色同值 ⇒ "护盾 0/2 / 经验 0/20" 读成"没有这条"。提到 0.66 后三种底图都成立。

**「当前是哪一把武器」没有减弱，而是四个通道**：
① 描边色（绿 vs 暖白）② 描边粗细（2.5px vs 1.2px，2 倍）③ 外侧投影只在当前那把上画 ④ 名字前的 `▶`。
任一通道失效（色盲 / 缩图 / 截图压缩）还剩三个。

### 2.1 有意保留的「填充」

- 条里的**填充色块**保留 —— 它就是数值本身，需求要求"保留数值与形态本身"。
- 已装插件槽仍是**品质色实心块** —— 那里填的是**信息**（这件插件是蓝的/紫的），去掉等于丢信息而非去底板。
- 空插件槽从"实心灰块"改成**只描边**（原来是浮在场景上的灰砖，比框本身更抢眼）。

---

## 3. 种子袋：按阶段门控，不是删除

**改法**（`ImGuiHud.cs:1169`）：

```csharp
if (s.Phase == PhaseState.Prepare) DrawPlantPanel(s);
```

- 战斗阶段 **条件不成立 ⇒ 连方法都不进**：没有调用、没有测量、没有绘制。**不是**"照画但不显示"的隐藏开关。
- 面板本体与 `DrawPlantCell` 保留（Esc 菜单中栏、商店种子卡仍在用 `DrawPlantCell`）。

### 3.1 取证：不删也丢不掉任何交互

| 问题 | 取证 | 结论 |
|---|---|---|
| 这块面板上有按钮吗？ | `DrawPlantCell` 里**只有 `GUI.Label`**，一个 `GUI.Button` 都没有 | 战斗里它**本来就是只读**的 ⇒ 门控它不切断任何可点入口 |
| `input.SelectSeed` 谁写？ | 全工程**唯一**赋正值处是 `SelfCheck.cs:2403/2405`（单测）；`ImGuiHud.ConsumeInto` **从不写它**；`GameBootstrap:180` 写 `-1` | `RunSession:2897/3479` 是**死路**（改前就是死的，不是本次造成） |
| `input.SlotClicked` 谁写？ | 只有 `PendingSlotClick`，而它**全工程从未被赋值** | `RunSession:2900 HandleBagClick` 也是死路 |
| `Bag.Selected` 真正会变的地方？ | `RunSession:2909` —— 商店里点某格「合成 → N 级」→ `PendingSlotMerge` → `input.SlotMergeClicked` → `Bag.MergeSlot` 成功时把 `Selected` 指到那格 | **选种/合成两个活入口都在商店**，不在这块面板上 |
| 玩家还看得见种子吗？ | ① 准备阶段这块面板**照常画**（§4 的 `ui-after-prepare-seedbag.png`）② 商店左下列表是同一份 `s.Bag`，且**带「合成 → N 级」与「出售」两个可点按钮**（`DrawShop`）③ Esc 菜单中栏 3×2 用同一个 `DrawPlantCell` | 三处都在 |

### 3.2 为什么不"彻底删掉"

"一个面板**不显示**"和"它**不存在**"是两件事：前者代价为零，后者会把"准备阶段看一眼我背了哪 6 个种子"一起删掉。所以只把它从**战斗**这个不该出现的场合移走。

> 顺带一条**改前就存在**的缺陷（不在本项范围，未修，供决策）：`input.SelectSeed` 与
> `input.SlotClicked` 两条路都断了 ⇒ 玩家**今天就已经**只能种第 0 槽（除商店合成会把 Selected 挪走）。
> 要恢复"任意槽选种"需要动 `GameBootstrap`（读数字键）或 `RunSession`，两者都不在我这次的授权范围内。

---

## 4. 截图清单（全部有 HUD，`ScreenCapture`，抓图前已把 Unity 提到前台）

目录：`Docs/captures/`

| 用途 | 文件 |
|---|---|
| 全景 · 改前（战斗） | `ui-before-full.png` |
| 全景 · 改后（战斗，顶部「00:09 第 1 分钟」） | `ui-after-combat-noon.png` |
| 全景 · 改后（准备阶段，**种子袋仍在**） | `ui-after-prepare-seedbag.png` |
| 最坏底图 · 夜（t=150） | `ui-after-combat-night.png` |
| 最坏底图 · 黄昏（t=75） | `ui-after-combat-dusk.png` |
| 最坏底图 · 农田木色机位 | `ui-after-combat-farm.png` |

分项前后对比（同一帧同一矩形，脚本 `Tools/crop_hud_items.py` 生成）：`Docs/captures/ui-compare/`
`1-hp-bar` / `2-countdown` / `3-charge` / `4-weapon-switch` / `5-seed-bag`，各 `-before.png` `-after.png`。
最坏底图的同款裁切：`Docs/captures/ui-compare-worst/`。

### 4.1 可读性论证（去底板后靠什么）

三层叠加，每层治一种底图，互相不可替代：

1. **投影**（整条/整框向下偏移 2~2.5px 的暗块）→ 治**亮底图**（麦田、沙地、正午草地、农田木色）。
   亮描边在亮底上会失明，暗投影不会。
2. **描边**（1.5px 暖白；框是"外暗+内亮"双圈）→ 治**暗底图**（夜间林地、深绿灌木）。
   暗投影在暗底上会失明，亮描边不会。
3. **文字四向描边**（近黑 @0.92）→ 治**高对比杂乱底图**（花丛、红果、木栅栏）。
   这是本工程既有做法（`DrawPopups` / `LabelOutlined` / 商店车「E」），不是新发明的路子。

**实测结论**：

- 夜（最暗，`ui-after-combat-night.png`）：三条血条、两根充能条、三个武器框、计时器全部清晰可读。
- 黄昏（半亮半暗，`ui-after-combat-dusk.png`）：第三个武器框的文字压在暗红废墟上，仍可读。
- 农田木色（最亮、最接近"麦田"，`ui-after-combat-farm.png`）：**这一张是最坏的** ——
  它是把 `HudStrokeDim` 从 0.40 提到 0.66 的**原因**；改后空条（0%）也看得见形态。

⚠️ **诚实标注**：`ui-before-full.png` 拍在光照修复（Forward+ `_CLUSTER_LIGHT_LOOP`）**之前**，
`ui-after-*` 拍在**之后** ⇒ 改后的底图比改前**更亮**（对描边更不利）。
所以"去背景前后"这一组是**同一时段、同一底图**（两者都是默认 t=0），但"光照条件"不自称相同；
§4 的三张最坏底图全部拍在修复之后，可读性论证以它们为主证据，不受该差异影响。

---

## 5. 验证（真实命令与真实返回）

```powershell
cd D:\Unity\project\Seedfall守园者
dotnet build Seedfall.Runtime.csproj -t:Rebuild -v minimal --nologo
#   → 10 个警告 / 0 个错误
dotnet build Seedfall.Tests.csproj    -t:Rebuild -v minimal --nologo
#   → 10 个警告 / 0 个错误
```

> 途中出现过一次 `CS2012`（`Temp\obj\...\Seedfall.Runtime.dll` 被占用），等 5 秒重跑即 0 error —— 与任务书一致，是假错误。
> 另：并发的另一个 subagent 在编辑 `Assets/Scripts/Debug/DebugApi.cs` 时曾短暂留下
> `:2067 private static string FirstLine(string text)` 缺方法体的中间态，导致我一次编译报 `CS1002`。
> **我没有动别人的文件**，等其完成后重编即 0 error。

`Library/ScriptAssemblies/Seedfall.Runtime.dll` 由 Unity 自身编译产出（`2026/10/17 10:18`），未新增 `.cs` 文件故 `csproj` 的 39 条 `Compile` 无需改动。

`SelfCheck`（经 `/input` 调 `DebugApi.SelfCheck`，原样返回）：

```
自检 PASS：425/425 通过
```

`HUD-AUDIT`（`HudAudit()` → 等一帧 → `HudAuditReport()`）：

- 战斗阶段：`HUD-AUDIT 通过：上一帧画出的所有文本都放得下`
- 准备阶段：`HUD-AUDIT 通过：上一帧画出的所有文本都放得下`

> 关于 `SelfCheck.cs:1997` 那条「14 字」断言（`V50Checks` 里的 `const int maxDesc = 14`）：
> **它检查的是 `TableDatabase.Seeds[i].Desc.Length ≤ 14`，即 seeds.json 的数据长度，不读任何 HUD 宽度。**
> 本次没有改任何种子描述、也没有改种子卡/植物格的列宽 ⇒ 它不依赖我的改动，**425/425 里它仍然通过**。
> 因此我**没有改它**（改动一条与本次无关的断言会引入不必要的风险）。
> 若将来有人调整种子卡信息区宽度，把 `14` 换成 `可用宽度 ÷ 每字宽` 的计算式即可 —— 那时它才需要参数化。

---

## 6. 一处超出点名范围的改动（我必须申明）

**耕地双进度条（`DrawPlotPrompt`）也去掉了底板。** 理由与回退方式写在 `ImGuiHud.cs:2656` 的方法注释里：

- 它**不在**用户点名的四类里，但它是**同一种形态**：一对进度条，就挂在刚去底的充能条上方 20px。
  不改它，画面底部会形成"上面一块奶油板、下面一对裸条"的对照，读起来像漏做了一块。
- 于是我把规则统一成：**条 / 计时 / 武器框 = 无底板；成段的文字列表 = 保留底板**。
  依据不是"哪块好看"，而是这两类对底板的依赖不同：条有形状与填充色，描边+投影够；8 行文字没有形状，
  靠底板把"这一整块是一份说明"框出来，去掉就散在草地上了。
- **回退只需两行**：把该方法里两个 `BarBare` 换回 `Bar`，并在 `y` 处补回 `Panel(new Rect(x, y, w, 88));`。

**按键说明（`DrawKeyHelp`）的底板我保留了**，理由同上（它是文字列表，不是条），并且它可以按 Tab 隐藏。
如果你要求"整屏都不许有底板"，说一句，`DrawKeyHelp` 的 `Panel(...)` 也是两行的事。

---

## 7. 没做到 / 未验证

1. **没有在真机上验证其它分辨率与 `ui.scale ≠ 1.35`**。所有坐标都是虚拟坐标（`GUI.matrix` 缩放），
   逻辑上不随分辨率变，但我只实测了 1920×1080 / scale 1.35 这一种。
2. **没有做 4K / 超宽屏的裁切检查**。`DrawWeaponPanel` 的 x 依赖 `_virtualW`，理论安全（既有布局），未实测。
3. **没有验证"包里"的画面**（只验证了编辑器 play 模式）。
4. **`Bag.Selected` 的选种入口仍是断的**（§3.2）——改前就断，不在本次授权范围，未修。
5. **`ui-before-full.png` 与改后图的光照条件不同**（§4.1 已标注）。
6. **没能证明"零回归"**：`425/425` 与 `HUD-AUDIT` 是我能拿到的全部自动判据；像素级的"排版没挪动"
   只由"布局坐标一个都没改"这条代码事实支撑（`DrawVitals` 的 20/48/74/98 四行仍是原值等）。

---

## 8. 新增的辅助脚本（不是游戏代码，不参与编译）

| 文件 | 用途 |
|---|---|
| `Tools/ui_probe.ps1` | 桥的单次调用（`status` / `send` / `capture` / `log`）；`capture` 会先把 Unity 提到前台 |
| `Tools/ui_shot.ps1` | 带重试的战斗阶段抓图（并发 agent 会抢占编辑器，单次必失败） |
| `Tools/ui_shots_all.ps1` | 一次跑完：正午 / 夜 t=150 / 黄昏 t=75 / 农田机位 |
| `Tools/crop_hud_items.py` | 把整屏图裁成五项的前后对比；坐标写着各自对应的 `Panel(...)` 原句，防止裁到隔壁 |
