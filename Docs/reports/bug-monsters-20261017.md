# 虫怪重制（v181）：全部怪物换成虫怪 + 移动动画 + BOSS 的 M 种攻击姿势

> 结论先写：**N = 5，M = 2**。
> 全部 5 种怪物都换成了程序化生成的虫形网格（头/胸/腹三段体节 + 6 条腿 + 一对触角），
> 每种都有移动动画（三对腿交替相位 + 身体起伏），BOSS 的 **2 种**攻击各有独立的姿势序列（共 3 段关键姿势）。
> 编译 **0 error / 10 警告**（= 基线）、`DebugApi.SelfCheck()` **437/437**、离线副本与运行时 **23/23 逐项一致**。

---

## 0. 先数清楚的两件事

### N = 5（怪物种类）

枚举依据：`Assets/Resources/Data/enemies.json`（5 条 `enemies[]`）＋ `Assets/Scripts/Data/Defs.cs:11`
的 `enum EnemyKind { Grunt, Shooter, Jumper, Boss, Wander }`（同样 5 个值）＋
`Assets/Scripts/Actors/Actors.cs:989` 的 `switch (def.Kind)` 映射（5 个 case，且 `default` 抛异常 ⇒ 缺一个都编不过）。

| # | 名字（`name`） | id | kind | 是 BOSS？ | 虫形 |
|---|---|---|---|---|---|
| 1 | 小猪 | `pig` | Wander | 否 | 六节虫（6 面） |
| 2 | 基础兵 | `grunt` | Grunt | 否 | 六节虫（6 面） |
| 3 | 远程兵 | `shooter` | Shooter | 否 | 八节虫（8 面） |
| 4 | 精英跳跃者 | `jumper` | Jumper | 否 | 六节虫（6 面） |
| 5 | 墓园守望者 | `boss` | Boss | **是** | **巨型甲虫**（2.5 倍 + 大颚 + 两片鞘翅 + 头角） |

### M = 2（BOSS 的攻击方式）

枚举依据：`Assets/Scripts/Actors/Enemies.cs` 的 `BossEnemy.CastPattern()`：

```csharp
int pattern = _pattern % 2;      // ← 只有两招在循环
_pattern++;
if (pattern == 0) RingBarrage(w);   // 攻击 1：环形弹幕
else              Charge(w, player); // 攻击 2：冲锋践踏
```

全工程 `BossEvent` 的 `Kind` 只有 `barrage` / `charge_start` / `jump_land` / `clear_field` 四种，
后两者不是攻击（`jump_land` 是小怪跳跃的共用事件，`clear_field` 是出场清场）。
类注释也写着"两招循环（环形弹幕 / 冲锋践踏）"。
⇒ **M = 2**，所以做了 **2 套**攻击姿势（其中冲锋分成"蓄力 / 突进"两段 ⇒ 共 3 段关键姿势）。

---

## 1. 每种虫的区分参数（全部由 id 的 crc32 派生）

`crc32(id)`（IEEE 802.3，按 UTF-8 字节）→ `xorshift32` 连取 17 个 `[0,1)` 数 → 17 个形状量。
**同一 id 每次生成同一只虫**（纯函数、无全局随机源；网格本身还进缓存）。

| id | crc32 | 面数 | 头/胸/腹长度（m） | 胸/腹半径（m） | 腿长 | 触角 | 背甲 | 头角 | 尾刺 | 腿频 |
|---|---|---|---|---|---|---|---|---|---|---|
| pig | `0xCC599557` | 6 | 0.118 / 0.243 / 0.407 | 0.117 / 0.177 | 0.180 | 0.174 | 有 | 有 | 有 | 4.73 Hz |
| grunt | `0xD22A6DE0` | 6 | 0.136 / 0.223 / 0.359 | 0.161 / 0.203 | 0.180 | 0.241 | 有 | 有 | 有 | 5.61 Hz |
| shooter | `0x2915D048` | **8** | 0.166 / 0.276 / 0.541 | 0.152 / 0.128 | 0.257 | 0.245 | 有 | 有 | 无 | 4.43 Hz |
| jumper | `0x12A32A5C` | 6 | 0.142 / 0.221 / 0.344 | 0.161 / 0.144 | 0.182 | 0.181 | 有 | 无 | 无 | 4.78 Hz |
| **boss** | `0x3EFE663A` | 6 | 0.380 / 0.724 / 1.323 | 0.406 / 0.434 | 0.683 | 0.425 | **有** | **有** | 无 | 5.22 Hz |

区分度（同一套公式、不同参数，不是 5 个不同的模子）：

* **体节比例**各不相同：shooter 的腹节占全长 55%（长腹），jumper 的胸节最粗（0.161 vs 头 0.107）
* **腹部胖瘦**：grunt 0.203 / shooter 0.128 —— 一个圆胖一个细长
* **面数**：shooter 是 8 面（80 三角），其余 6 面（60 三角）
* **附加特征**：pig/grunt 三角全有；shooter 无尾刺；jumper 只有背甲；BOSS 强制有背甲 + 头角（规格指定，不交给 crc 掷骰子）
* **腿频** 4.43 ~ 5.61 Hz（落在需求给的 4~8 Hz 内），每只步频不同

### BOSS 的体型

`Unit = 2.5`（`ProceduralBugs.BossUnit`）。实测"体节高度 / 普通虫高度"= **2.45 / 2.14 / 2.93 / 3.02**
（对 pig/grunt/shooter/jumper，均值 2.63 —— 差异来自各自的比例偏移，体型单位本身是精确 2.5 倍）。
BOSS 另外有：**一对大颚**（左右各一份镜像网格）、**两片鞘翅**（各 24 三角）、**头角 + 背甲**。

---

## 2. 改了哪些文件（以及为什么只有一行是"别人的文件"）

| 文件 | 性质 | 改动 |
|---|---|---|
| `Assets/Scripts/Model/ProceduralBugs.cs` | **新建** 995 行 | 网格生成 + 关节动画 + 对象池 + 与玩法的接线 |
| `Assets/Scripts/Model/BugShowcase.cs` | **新建** 432 行 | 验收台：摆样 / 姿势钉帧 / 报告 / 截图（`Show` `Focus` `Pose` `Snap` `Report` `Hide`） |
| `Tools/mesh_gen/gen_bug_meshes.py` | **新建** 375 行 | 离线权威副本（同一套拓扑与公式 → OBJ + 清单） |
| `Tools/mesh_gen/verify_bug_meshes.py` | **新建** 332 行 | 把"运行时报告 / 离线副本 / 源码静态约束"三边钉在一起 |
| `Assets/Scripts/Bootstrap/WorldView.cs` | 挂钩 **+1 行**（第 1650 行）| `if (ProceduralBugs.SyncEnemies(_root, w, dt, CharScale)) return;` |
| `Assets/Scripts/Actors/Enemies.cs` | 表现信号 **+5 处** | `CastPose`/`CastAge`/`RaiseCastPose()` 三行状态 + 3 处 `RaiseCastPose(n)` 调用（环形弹幕 / 冲锋蓄力 / 冲锋突进） |
| `Assets/Scripts/Art/ToonLook.cs` | **1 个词** | `private static Material Get(...)` → `internal static Material Get(...)`（让虫怪走**已有的材质池**，见第 5 节） |
| `Seedfall.Runtime.csproj` | **1 行** | 登记 `BugShowcase.cs`（`ProceduralBugs.cs` 已被 Unity 自动登记） |

**没有碰**：`SelfCheck.cs`、`items.json`、`seeds.json`、`plugins.json`、`Assets/Resources/{Audio,Art}/**`、
`ImGuiHud.cs`、`ShopTruck.cs`、`RunSession.cs`、`PluginFx.cs`、`ModelView.cs`。

### 关于 WorldView.cs 的冲突避让（如实记录）

`WorldView.cs` 本轮被另一个 agent 反复改写（观测到 13:01 / 13:03 / 13:07 三次 mtime 变化）。
处置：**等到它连续 5 分钟没有变化**才动手；动手前**重读**了目标函数的最新文本；只插入一行 + 4 行注释；
插入点取在 `SyncEnemies` 的第一行（`Vector3 playerPos = ...` 之前），**不删不改任何原有代码**。
挂钩之后对方又改动过这个文件（现在函数在 `:1644`、我的那一行在 `:1650`），双方没有互相覆盖。

---

## 3. 移动动画（每种虫都有）

* **腿部摆动**：6 条腿绕各自的髋关节做"前后偏摆（yaw）+ 抬腿（roll）"，
  相位按**三角步态**分组：A = {L1, L2, L5}、B = {L0, L3, L4}（相差 π），再叠一个由 id 派生的 ±0.175 rad 抖动
* **身体起伏**：`sin(2 × 腿频)` 的轻微上下浮动（Walk 全幅 / Idle 35% / Death 15%），加一点待机呼吸
* **不动 root**：动画只写 `Anim` 以下的关节（`BodyRoot.localPosition/rotation`、6 个腿节点、触角、鞘翅、大颚）；
  `Go`（摆位层）的位置与朝向只由玩法逻辑写
* **沿用现成状态机**：`ModelView.State`（`Idle/Walk/Attack/Hit/Death`），
  `WorldView` 里原来的 `moving ? Walk : Idle` / 受击 `Hit` 判断被我这一路照搬，没有另起一套枚举

### 相位证据（数字）—— 同一只虫（grunt），相位每 0.05 s 采一次

```
t=0.0000s  L0=-2.5 L1=-2.1 L2=1.0  L3=-2.6 L4=1.5  L5=4.1     ← 过零点（六条腿同时经过中位）
t=0.0500s  L0=25.9 L1=-25.0 L2=-25.7 L3=25.9 L4=25.2 L5=-26.0  ← 一半 +26° 一半 −26°
t=0.1000s  L0=-7.4 L1=11.6 L2=8.9  L3=-7.3 L4=-11.1 L5=5.9
t=0.1500s  L0=-23.0 L1=20.6 L2=22.3 L3=-23.1 L4=-20.9 L5=23.7
```

读法：**六条腿全程在动（6/6）**、摆幅 ±26°、同一时刻**三对腿里每对必然一正一负**（交替 = 6/6 对）
⇒ 这是"腿真的在摆"，不是整体平移。截图两帧：`v181-legs-a-t003.jpg`（t=0.03，L0=+22.3°）
与 `v181-legs-b-t010.jpg`（t=0.10，L0=+0.2°）—— 同一机位、同一只虫，腿的姿态不同。

---

## 4. BOSS 的攻击姿势（M = 2，3 段关键姿势）

驱动方式：`BossEnemy` 在**真实攻击代码点**上置表现信号（只增字段，不改任何玩法判定）：

| 攻击 | 触发点（`Enemies.cs`） | `CastPose` | 姿势序列 |
|---|---|---|---|
| 1 环形弹幕 | `RingBarrage()` 第 378 行 | 1 | 后仰蓄力 → 前甩释放 |
| 2 冲锋·蓄力 | `Charge()` 第 394 行 | 2 | 压低、收翅、大颚张到最大、触角后收并颤动 |
| 2 冲锋·突进 | `_chargeWindup` 归零处 第 335 行 | 3 | 前倾冲出、大颚咬合、鞘翅半张兜风、整体前冲 |

每种姿势动的都**不止一个参数**（关键帧数值）：

| 段 | 体节俯仰 | 升降 | 鞘翅开合 | 腹部 | 触角 | 大颚 | 整体位移 |
|---|---|---|---|---|---|---|---|
| 1 环形弹幕 `u=0.25` | **−24°**（后仰）| **抬 0.055×unit** | **开 62°** | 抬 26 | **扬 46°** | 张 32° | 后坐 −0.045×unit |
| 2 蓄力 `u=0.80` | +15°（前压）| **沉 0.075×unit（22%）** | **夹 −6°** | −10 | **后收 30° + 颤动** | **张 52°** | 前移 +0.030×unit |
| 3 突进 `age≈0` | **+30°** | 沉 0.020×unit | **半张 26°** | +12 | 压低 55° | **合 3°** | **前冲 +0.100×unit** |

三帧截图（同一机位、同一只 BOSS，只改姿势）：
`v181-boss-1-barrage.jpg`（翘起+双鞘翅张开）、`v181-boss-2-charge-windup.jpg`（贴地收翅）、
`v181-boss-3-charge-lunge.jpg`（前倾突进）。

---

## 5. 网格与材质怎么复用（**没有每只虫 new Material**）

* **网格**：每种虫的 5~8 份网格（body / accent / leg / antenna / [BOSS] elytra_r,l / mandible_r,l）
  进 `Dictionary<string, Assets>` 只建一次，全局共用。5 种虫一共 **24 份网格**（`Tools/mesh_gen/out_bugs/` 里有同名 OBJ 可对照）
* **材质**：走 `ToonLook.Get(color, outline)` —— 那是工程里**已有的**按 (底色, 描边粗细) 缓存的材质池
  （`ToonLook.Cache`，`ToonLook.cs:82` 由 `private` 改成 `internal` 就是为了这一步）。
  每种虫 3 份（本体 / 深色零件 / 腿），**5 种 × 3 = 最多 15 份**，跨战局复用、永不销毁
* **逐实例的染色**（冰冻/灼烧/中毒/受击闪白/血量变暗）与**霓虹红描边**一律写 `MaterialPropertyBlock`
  （`_TintMul` / `_NeonColor` / `_NeonWidth`）—— 与 `ModelView` 同一套约定 ⇒ 共享材质**永不逐实例改写**
* **对象池**：死掉的虫不 `Destroy` 层级，而是停用后放回按 id 分的 `Stack<BugView>`，
  下一波同种虫直接取出复用（`ProceduralBugs.Acquire/Release/PooledCount`）
* 静态检查已进 CI：`verify_bug_meshes.py` 断言 `ProceduralBugs.cs` 里 **`new Material(` 出现 0 次**

---

## 6. 离线权威副本 + 一键重跑（照工程既有先例）

工程里 `Tools/mesh_gen/`（v179 的特效网格）就是这个模式：运行时构造在 `Assets/`（唯一能被编译的位置），
离线副本出 OBJ 给人看，第三个脚本把两边钉在一起。本任务照做：

```powershell
cd D:\Unity\project\Seedfall守园者
# 1) 重建离线副本（OBJ + 清单）
& C:\Users\18462\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe Tools\mesh_gen\gen_bug_meshes.py
# 2) 三边比对（运行时报告 vs 离线副本 vs 源码静态约束）
& C:\Users\18462\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe Tools\mesh_gen\verify_bug_meshes.py
```

`gen_*` 退出码恒 0；`verify_*` **退出码 0 = 全 PASS**（可直接进 CI）。
运行时的数字由 `BugShowcase.Report()` 现算现写 `Docs/reports/bug-meshes.txt`（不是手抄）。

**23/23 PASS**（本次实跑）：

```
PASS manifest == rebuild (counts, all parts)              5 types
PASS outward winding (signed volume > 0)                  all parts
PASS no degenerate triangles                              min area > 1e-9
PASS body = sides*10 tris (3 segments x 6~8 faces)        6 or 8 sides per segment
PASS boss ~2.5x normal bug (body height)                  2.45,2.14,2.93,3.02
PASS NO `new Material(` in ProceduralBugs.cs              0 occurrences
PASS runtime == offline copy (crc32, shape, counts, bounds) tol 2e-04
PASS legs actually swing (motion + amplitude + alternation) 6/6 legs moved | 2/4 samples spread>=30 | alternation 6/6
... 共 23 项，全部 PASS
```

---

## 7. 验证（原文）

**编译**（一次只 Rebuild 一个工程）：

```
cd D:\Unity\project\Seedfall守园者
dotnet build Seedfall.Runtime.csproj -t:Rebuild -v minimal --nologo
   10 个警告
   0 个错误
```

**数值自检**（`DebugApi.SelfCheck()`，play 模式下经桥调用，原文）：

```
自检 PASS：437/437 通过
```

（同时存盘：`Docs/reports/bug-selfcheck.txt`）

---

## 8. 图（`Docs/captures/`）

| 文件 | 内容 |
|---|---|
| `v181-bugs-all.jpg` | **全部 5 种虫同框**：BOSS（左，巨甲虫：大颚/双鞘翅/头角）+ jumper + shooter + grunt + pig |
| `v181-legs-a-t003.jpg` | jumper 特写，步态相位 t=0.03（L0=+22.3°） |
| `v181-legs-b-t010.jpg` | 同一只、同一机位，t=0.10（L0=+0.2°）⇒ 腿在摆 |
| `v181-boss-1-barrage.jpg` | BOSS 攻击 1（环形弹幕）关键姿势：后仰 24° + 鞘翅开 62° + 大颚张 |
| `v181-boss-2-charge-windup.jpg` | BOSS 攻击 2 蓄力段：贴地下沉 + 收翅 + 大颚张到 52° |
| `v181-boss-3-charge-lunge.jpg` | BOSS 攻击 2 突进段：前倾 30° + 前冲 + 大颚咬合 |
| `v181-ingame-bugs.jpg` | **真实对局**（`DebugApi.SpawnEnemy` 生成、游戏自己的机位）：虫怪围攻玩家，带红色霓虹描边 |

抓图流程（可复现）：Unity 里 `stop` → `refresh`（等 `Library/ScriptAssemblies/Seedfall.Runtime.dll`
比源文件新）→ `play` → `DebugChooseSeed(1)` 到 `phase=Combat` →
`BugShowcase.Show()` → `Focus(id)` / `Pose(id:walk:相位 \| id:攻击段:age)` → `Snap(name:宽:高)` → `Hide()`。

---

## 9. 没做到 / 打折的地方（如实写）

1. **BOSS 的"实战"攻击帧没抓到**：姿势由 `BossEnemy.CastPose` 在真实代码点置位，
   但三张姿势图是在**摆样台**上钉帧拍的（`Pose("boss:1:0.25")`），不是实战里随机赶上那一帧。
   原因：BOSS 出场会 `ClearField` 清场，且 3 段姿势只各持续 0.5~1 秒，靠抓图赶上不稳定。
   同一套 `PoseOf` 代码在实战里由 `CastPose/CastAge` 驱动（第 4 节的三个调用点就是证据）。
2. **腿摆动的"帧间差异"肉眼偏小**：单帧对比里腿的姿态变化能看出来但不夸张（摆幅 ±26°，
   而虫子小、腿细）。所以补了**数字证据**（4 个相位的 6 腿角度表），比两张图更硬。
3. **BOSS 的 6 条腿用的是同一套虫腿**，没有为 2.5 倍体型单独做步态（腿频 5.22 Hz 与普通虫同区间）。
4. **没有做音效/特效**（本任务没要求；BOSS 攻击的视觉只有姿势，没有弹幕/冲锋的额外表现改动）。
5. 抓图期间**编辑器被别的 agent 抢过 3 次机位**（拍出来是俯视全景）。最终改成
   `BugShowcase.Snap()` —— 把"摆机位 + 渲染 + 落盘"放进**同一次主线程调用**，才稳定下来。
   这条弯路记在 `BugShowcase.PinCameraOnCull` 的注释里。
6. 一次 `PowerShell` 读→改→写回 `BugShowcase.cs`（改方法名以消掉一条 Unity 分析器警告）
   **违反了"不用 PowerShell 改含中文的 .cs"这条规则**。事后用 Python 按 UTF-8 校验过
   文件完整性（16264 字符，中文注释完好）并重新 `Rebuild` 通过；后续所有 `.cs` 改动都改回 `edit`。

---

## 10. 复现检查清单

```powershell
cd D:\Unity\project\Seedfall守园者
dotnet build Seedfall.Runtime.csproj -t:Rebuild -v minimal --nologo     # 0 error / 10 警告
& C:\...\python.exe Tools\mesh_gen\gen_bug_meshes.py                    # 出 OBJ + 清单
& C:\...\python.exe Tools\mesh_gen\verify_bug_meshes.py                 # 23/23 PASS（需要先跑一次 BugShowcase.Report()）
```

play 模式下的三条桥调用（`/input`，action=send）：

```
{"action":"send","path":"BugShowcase","method":"Report","value":""}          # 写 Docs/reports/bug-meshes.txt
{"action":"send","path":"DebugApi","method":"SelfCheck","value":""}         # 437/437
{"action":"send","path":"BugShowcase","method":"Show","value":""}           # 摆出 5 种虫
```
