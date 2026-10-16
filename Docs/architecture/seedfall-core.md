# Seedfall 守园者 · 核心架构

> 工程：`D:\Unity\project\Seedfall守园者`（Unity 6000.4.6f1 / URP 17.4 / Input System 1.19）
> 运行时程序集：`Seedfall.Runtime`（`Assets/Scripts/Seedfall.Runtime.asmdef`）｜测试：`Seedfall.Tests`（EditMode）

## 目标与验收

**目标**：可玩原型 —— 40×40 竞技场、中央 16 格耕地（初始 6）、掘地/播种/洒水/施肥/铲除、16 种种子长成植物帮手自动作战、玩家三把工具亲自参战、四类敌人 + 10 波（双 Boss）、经验等级、货币商店、胜负结算；**逻辑与表现分离、同种子可复现**。

**验收命令**
```powershell
# 1) 编译：Unity 打开工程，控制台 0 error（脚本改动后必须读一次控制台）
# 2) EditMode 单测：Test Runner 或 run_tests(mode=EditMode, assembly_names=["Seedfall.Tests"])
# 3) 无头回归（编辑器关闭时）：
#    Unity.exe -batchmode -nographics -projectPath "D:\Unity\project\Seedfall守园者" `
#      -executeMethod Seedfall.EditorTools.SeedfallMenu.RunAll -logFile -
# 4) 编辑器内回归（推荐，无需关编辑器）：DebugApi.RunRegression("20240607")
#    → 报告写到 Docs/reports/sim-<时间戳>.txt
# 5) play 模式冒烟 + 断言：unity_test(seconds=35) + unity_assert(...)
```

## 现状取证（本次落地时）

| 事实 | 证据 |
|---|---|
| 落地前工程是 URP 模板空壳：仅 3 个 .cs（TutorialInfo 2 + Editor/YoweiBridge.cs） | Assets 清单 |
| 无任何游戏美术/音频资源 | Assets 仅 Settings/TutorialInfo/Readme |
| `activeInputHandler: 1` → **只能用新 Input System**，`Input.GetKey` 会抛异常 | `ProjectSettings.asset:923` |
| 包：inputsystem 1.19.0 / URP 17.4.0 / test-framework 1.6.0 / ugui 2.0.0 | `Packages/manifest.json` |

**结论**：世界与 UI 全部由代码在运行时构建（避免手写 .unity/.prefab 的不可复现），场景只保留 `SeedfallApp`（GameBootstrap）+ `DebugApi` 两个宿主对象。

## 子系统与职责

| 目录 | 职责 | 不负责 |
|---|---|---|
| `Scripts/Core` | 固定步长时钟 `GameClock`、确定性随机 `Pcg32`、事件总线 `Bus`、日志 | 任何玩法规则 |
| `Scripts/Data` | JSON 配置 DTO（枚举用字符串存、运行时解析）、`TableDatabase` 载入与校验 | 运行时状态 |
| `Scripts/Stats` | `StatBlock`（基础→加→乘→裁剪）、`PlayerProgress`（等级/经验/成长） | 战斗结算 |
| `Scripts/Combat` | `DamageResolver`（全部公式）、`HealthPool`、`StatusRuntime`（中毒/烧伤/冰冻） | 目标选择、AI |
| `Scripts/Farm` | `FarmPlot`（状态机 + 生长）、`FarmGrid`（4×4 槽位 + 螺旋解锁） | 玩家输入、渲染 |
| `Scripts/Actors` | `Actor`/`PlayerActor`/`PlantAlly`/4 类敌人/`Projectile`/`Pickup`/`World` | 波次调度、商店 |
| `Scripts/Flow` | `RunSession`（唯一逻辑入口）、`SeedBag`、`WaveDirector`、`ShopSystem`、`Telemetry` | 具体实体行为 |
| `Scripts/UI` | `ImGuiHud`（原型 IMGUI；点击写回 FrameInput） | 玩法逻辑 |
| `Scripts/Bootstrap` | `GameBootstrap`（装配场景/输入/固定步长驱动）、`WorldView`（表现同步） | 规则决策 |
| `Scripts/Automation` | `RunBot`（四策略）、`RunSimulator`（无头跑局 + 报告） | 参与正式玩法 |
| `Scripts/Debug` | `DebugApi`（自动化验证与现场诊断入口） | — |

**分层硬规则**：`Flow/Actors → Combat/Farm → Stats → Core`，反向引用禁止。`MonoBehaviour` 只做"驱动 + 表现"，规则写在纯 C# 类里（可无引擎单测）。

## 关键接口签名

```csharp
// Core
public struct Pcg32 { public Pcg32(ulong seed, ulong seq = 1);
    public uint NextUInt(); public float NextFloat(); public float Range(float a, float b);
    public int RangeInt(int a, int b); public bool Chance(float p); public Pcg32 Fork(ulong seq); }
public sealed class GameClock { public const float FixedDt = 1f/60f;
    public void Advance(float dt, int maxCatchUp, Action<float> onFixedTick); public int TickIndex { get; } }

// Stats
public sealed class StatBlock { public float Get(StatId id); public void SetBase(StatId id, float v);
    public void AddBase(StatId id, float d); public void AddModifier(in StatModifier m);
    public void Evaluate(); /* 加法→乘法→上限裁剪 */ }
public sealed class PlayerProgress { public int Level { get; } public float Exp { get; } public StatBlock Stats { get; }
    public bool AddExp(float amount, out int levelsGained); public static float ExpToNextFor(int level, GrowthDef g); }

// Combat（公式唯一实现）
public static class DamageResolver {
    public static float AttackFormula(float x, float y, float m, float n, bool crit, float critDmg, float minDamage = 1f);
    public static float HealFormula(float x, float y, float m, float n);
    public static float GuardFormula(float x, float maxHp, float m, float n);
    public static float LeechFormula(float dealt, float leech, float leechBase);
    public static float AilmentThreshold(float maxHp, FormulaDef f);
    public static DamageResult Resolve(in DamageRequest req, World w); }

// Farm
public enum PlotState { Locked, Empty, Dug, Growing, Grown }
public enum PlotAction { None, Dig, Sow, Water, Fertilize }
public sealed class FarmPlot {
    public PlotState State { get; } public PlotAction NextAction { get; }
    public float RequiredTime(PlotAction a, FarmDef f);
    public bool TickAction(PlotAction a, float dt, float atkSpeed, FarmDef f, SeedDef sowSeed);
    public bool TickGrowth(float dt);       // 独立倒计时，与玩家操作并行
    public bool RemoveGrowing(); }
public sealed class FarmGrid { public FarmPlot[] Slots; public int UnlockedCount; public int MaxPlots;
    public Vector2 WorldOf(int x, int y); public int NearestUnlocked(Vector2 p, float maxDist);
    public void SetMaxPlots(int n); }

// Flow（唯一逻辑入口）
public struct FrameInput { public Vector2 Move, AimWorld; public bool Interact, RemovePlant, Hoe, Can, Fert, Skip, Reroll;
    public int SelectSeed, SellSlot, BuyOffer, InitialChoice; }
public sealed class RunSession {
    public PhaseState Phase { get; } public int WaveIndex; public int Gold;
    public World World; public PlayerProgress Progress; public SeedBag Bag;
    public void Start(ulong seed); public void FixedTick(float dt, in FrameInput input);
    public void StartWave(int index); public void JumpToWave(int index);   // 后两个供调试/自动化
    public float FarmActionSeconds; public float CombatSeconds; }          // 验收指标用

// Automation
public enum BotStrategy { Idle, Farmer, Balanced, Rush }
public static class RunSimulator {
    public static SimResult Run(BotStrategy s, ulong seed, float maxSeconds = 1500f);
    public static bool VerifyDeterminism(BotStrategy s, ulong seed, out string report);
    public static string BuildRegressionReport(ulong seed, out bool determinismOk);
    public static string WriteReport(string content, string fileName = null); }
```

## 配置表 schema（`Assets/Resources/Data/*.json`）

| 文件 | 根字段 | 关键字段（单位） |
|---|---|---|
| `tuning.json` | `player/growth/caps/farm/plotUnlockOrder/formula/tools/shop/seedBag/waves/meta` | `moveSpeed` m/s｜`digTime/sowTime/waterTime/fertilizeTime` s｜`ailmentDelay` s｜`waves.hardCapFactor` 倍 |
| `seeds.json` | `seeds[]`、`mutations[]` | `baseValue` 点｜`inheritMult` 无量纲｜`growTime` s｜`range` m｜`attackInterval` s |
| `enemies.json` | `enemies[]` | `baseHp/hpPerWave/hpPerBoss` 点｜`moveSpeed` m/s｜`attackRange/safeRange/radius` m |
| `waves.json` | `waves[]` | `duration` s｜`batches[].startAt/interval` s |
| `items.json` | `items[]` | `stat` ∈ `StatId` 名｜`add` 层增加值｜`special`（`ChargeCap` 等非属性效果） |

**枚举以字符串存储、运行时解析**（`DefParser.ParseEnum`）——因为 `JsonUtility` 对枚举的字符串解析不可靠。
**校验**（`TableDatabase.Validate`）：id 唯一、枚举可解析、数值 > 0、波次引用的敌人存在、`maxPlots ≤ 槽位总数`；失败抛异常并中止开局，绝不静默取默认值。

## 状态机

### 局流程
| 状态 | 事件 | 动作 | 下一状态 | 非法转移 |
|---|---|---|---|---|
| Boot | 表加载成功 | 建世界、解锁 6 格 | Prepare | 表失败 → 报错停流程 |
| Prepare | 选种子 / 倒计时归零 | 入袋、开局 | Combat | — |
| Combat | 时长到 且 场上清空（Boss 波：Boss 死） | 掉落入账、植物回 30%、护盾回满 | Shop | 超 `duration×1.35` 强制收尾 |
| Combat | 玩家 HP ≤ 0 | 停表、写 `run_end` | Defeat | — |
| Shop | 倒计时归零 / 空格 | 充能回满、波次 +1 | Combat | `WaveIndex > 10` → Victory |
| 任意 | Esc | `Paused`（冻结 FixedTick） | 恢复 | — |

### 耕地格
| 状态 | 事件 | 动作 | 下一状态 |
|---|---|---|---|
| Locked | `maxPlots` 覆盖该格 | 解锁 | Empty |
| Empty | 掘地进度满 | — | Dug |
| Dug | 播种进度满 **且有充能** | 扣 1 充能、建生长体（40% 生命） | Growing |
| Growing | 洒水 / 施肥 | 生长 ×0.70 / ×0.50（下限 ×0.35），进度按比例保留 | Growing |
| Growing | 生长归零 | 生长体转正（补满生命差） | Grown |
| Growing | 铲除 / 生长体被摧毁 | 归还地块 | Empty |
| Grown | 植物死亡 | 归还地块 | Empty |

非法事件（如 Empty 收到 Sow）**忽略并返回 false**，不抛异常、不改状态（有单测覆盖）。

## 数据流与单帧顺序

```
输入（Input System / RunBot / HUD 点击）→ FrameInput
 → RunSession.FixedTick(dt)                     [1/60 固定步长，唯一逻辑入口]
     1. WaveDirector.Tick            刷怪（消费 World.Rng）
     2. HandlePlayer                 工具攻击 + 耕地交互（耕作时定身）
     3. World.Simulate
          3.1 耕地生长推进（独立倒计时）
          3.2 玩家 Tick（移动/再生/冷却/属性同步）
          3.3 植物 Tick（索敌/移动/开火/治疗）
          3.4 敌人 Tick（目标选择/移动/攻击/跳跃/弹幕）
          3.5 弹道推进与命中
          3.6 掉落吸附与拾取（→ 金币/经验 → 升级 → 解锁耕地）
          3.7 Cleanup：死亡结算（掉落、地块回收、Boss 爆裂等）
     4. 波次完成 / 硬上限 / 玩家死亡判定
 → WorldView.Sync（表现同步，绝不回写逻辑）
 → ImGuiHud.Draw（只读快照）
```

## 确定性与存档
- 逻辑跑固定 1/60 步长；渲染帧率不影响结果（`GameClock` 最多追 5 帧，超出丢弃并计数）。
- 单一 `Pcg32(seed)` 逻辑流；表现层用 `Fork(0xF00D)` 派生流，**绝不消费逻辑流**。
- 碰撞为手写圆形判定，不依赖 PhysX → 无浮点不确定性。
- `World` 内所有可变态都是值类型或纯 C# 对象，无隐藏引擎状态。
- 存档：原型不落盘；拓展 E-07 计划 `Application.persistentDataPath/seedfall_save.json` + `version` 字段。

## 失败模式与降级

| 失败 | 可观测信号 | 降级路径 |
|---|---|---|
| 配置表缺失/字段非法 | `TableDatabase.Validate` 抛异常 + 控制台 error | 中止开局，不进入战斗 |
| 引用了不存在的 id | `GetSeed/GetEnemy` 抛 `KeyNotFoundException` + error | 拒绝该次生成，绝不用默认值顶替 |
| 种子袋满 | `SeedBag.AddOrStack` 返回 false + reason | UI 提示"袋满：先出售" |
| 波次无法结束（只跑不打） | 超过 `duration×1.35` | 强制收尾 + `wave_timeout` 埋点（已实现并复测） |
| Boss 清场时玩家正在拾取 | — | 其余怪物金币 50% 直接入账，不丢失收益 |
| 帧率过低 | `GameClock.OverrunTicks` 增长 | 单帧最多追 5 个固定步，超出丢弃 |
| 编辑器/桥未开（自动化） | 工具报 not connected | 改用 `-batchmode -executeMethod` 或在编辑模式调 `DebugApi.RunRegression` |

## 单测覆盖（`Assets/Tests/EditMode/FormulaTests.cs`）
攻击/恢复/护卫/汲取公式手算值、异常阈值、升级曲线、属性加法→乘法→上限裁剪、耕作三段时长（掘地/播种/攻速缩短）、洒水施肥乘法与下限、非法动作忽略、初始 6 格解锁集合、`Pcg32` 确定性与派生流隔离、配置表数量与校验、Boss 血量随 index 成长。

## 未决项（需拍板）

| ID | 未决 | 选项 | 建议 |
|---|---|---|---|
| A-1 | UI 技术栈 | IMGUI（当前）/ uGUI 代码构建 / UI Toolkit | 生产切 uGUI 或 UI Toolkit；逻辑已与 `IHud` 解耦 |
| A-2 | 世界构建方式 | 代码运行时构建（当前）/ 预制体 + 手工场景 | 美术介入后转 B |
| A-3 | 碰撞方案 | 手写圆判定（当前）/ 引擎物理 | 需要击退惯性手感时再换 B |
| A-5 | 程序集划分 | 单 `Seedfall.Runtime`（当前）/ 拆 `Seedfall.Logic`（无 UnityEngine 依赖） | 引入纯逻辑 CI 时再拆 |
| A-6 | 目标平台 | PC 优先（当前）/ 移动端优先 | 移动端适配见拓展 E-11 |
