using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>持续效果的三种形态。**只有这三种**（阶段 3 的 12 件插件里带"每秒"的正好三条）。</summary>
    public enum DotKind
    {
        /// <summary>灼烧：挂在**一只敌人**身上，每秒按其层数掉血。来源：`hoe_burn`。</summary>
        Burn = 0,
        /// <summary>减速：挂在**一只敌人**身上，减移速 + 每秒掉血。来源：`can_slow`。</summary>
        Slow = 1,
        /// <summary>毒区：留在**一个位置**上，每秒伤害半径内的所有敌人。来源：`fert_poison`。</summary>
        PoisonZone = 2,
    }

    /// <summary>
    /// 持续效果表里的**一行** = "谁、每秒多少、剩多久、叠了几层" ✗
    ///
    /// ★ 为什么前两项（谁 / 每秒）不能合并成一个"总量" ★
    /// 因为四个字段各自决定一件不同的事，任何一个丢了都会出现"看得见但算不对"：
    ///   · `Kind` + `EnemyId` → **谁**挨（`Slow`/`Burn` 按敌人，`PoisonZone` 按位置）
    ///   · `PerSecond`        → **每秒多少**（已经乘过层数，直接拿去结算）
    ///   · `Remain`           → **剩多久**（"时长不叠"这条规矩就体现在这里：只取 max 不累加）
    ///   · `Stacks`           → **叠了几层**（报告与减速幅度用它，**不再乘进 PerSecond 两次**）
    /// ★ 结算一律走"**每满 1 秒一次**"（`Acc` 累加），不是每帧按比例掉血 ★
    /// 每帧掉血会让"每秒掉多少"这句话变成不可测量的 —— 而报告里要贴的正是这个数 ✗
    /// </summary>
    public struct DotRow
    {
        public DotKind Kind;
        /// <summary>挂在哪只敌人身上（`PoisonZone` 恒 0 —— 它按位置命中，不认敌人）。</summary>
        public int EnemyId;
        /// <summary>毒区圆心（`Burn`/`Slow` 不用）。</summary>
        public Vector2 Center;
        /// <summary>毒区半径（`Burn`/`Slow` 不用）。</summary>
        public float Radius;
        /// <summary>**每秒掉多少血**（已含层数）。</summary>
        public float PerSecond;
        /// <summary>**还剩多少秒**（到 0 就整行移除）。</summary>
        public float Remain;
        /// <summary>**叠了几层**。</summary>
        public int Stacks;
        /// <summary>距离下一次"满 1 秒"结算还差多少秒。</summary>
        public float Acc;
        /// <summary>每层减速比例（只有 `Slow` 用；其余为 0）。</summary>
        public float PctPerStack;
    }

    /// <summary>
    /// ★ v188：**持续伤害 → 弹字图标** 的唯一交接点 ★
    ///
    /// ── 为什么需要它（而不是把种类塞进 `DamageRequest`）──
    ///   用户需求：灼烧 / 减速 / 中毒三种伤害的弹字要带各自的图标前缀。
    ///   弹字是在 `Actor.OnHitLanded` 里发的（`Actors.cs`），而它**只拿到 `DamageResult`**；
    ///   而 `DamageResult` 里没有"这是第几种持续效果"这个信息 ✗
    ///   看起来能省事的做法是给 `DamageRequest` 加一个字段 —— 但那是 `Combat.cs`（本轮不动）✗
    ///   退一步看 `DamageRequest.Type`：它在持续伤害里是 `Ailment`，
    ///   而**肥料爆炸**同样是 `Ailment`（`RunSession.BlastDamageAt:5156`）⇒ 分不开 ✗
    ///
    /// ── 它为什么是安全的 ──
    ///   `PluginDotTable.Hit` → `World.DealDamage` → `Resolve` → `OnHitLanded` → `Bus.Publish`
    ///   **整条链路同步、单线程、同一个调用栈** ⇒ "先立标签、同步结算、finally 清掉"
    ///   就是一次**栈作用域**的传参，不存在跨帧残留 ✓
    ///   清空放在 `finally`：任何一条提前返回/抛异常的分支都不会把标签漏给下一条弹字 ✓
    ///
    /// ── 零分配 ──
    ///   两个静态字段（`DotKind?` 是 `Nullable&lt;DotKind&gt;` 结构体，不进堆）✓
    ///   每帧几十次赋值 = 几十次写字段，没有装箱、没有 `new` ✓
    /// </summary>
    public static class DotPopupHint
    {
        /// <summary>本次结算的持续效果种类；`null` = 不是持续伤害（普通攻击 / 爆炸 / 反射）。</summary>
        public static DotKind? Kind;
        /// <summary>本次结算时叠了几层（给读数与"层数越多越密"的对照）。</summary>
        public static int Stacks;

        public static void Set(DotKind kind, int stacks)
        {
            Kind = kind;
            Stacks = stacks;
        }

        public static void Clear()
        {
            Kind = null;
            Stacks = 0;
        }
    }

    /// <summary>
    /// **持续效果表**（阶段 3 新增）—— 灼烧 / 减速 / 毒区共用的一张表。
    ///
    /// ★ 为什么必须是"一张表"，而不是塞进 `StatusRuntime` ★
    /// 既有的 `StatusRuntime`（`Combat.cs`）是**敌人异常状态**的家：毒/火/冰的结算都由
    /// *敌人自己* 在 `Tick` 里推（谁被 tick 到谁就该掉血）✓ 而本轮的三种效果有一条
    /// `StatusRuntime` 表达不了的东西：**毒区属于位置，不属于任何敌人** ✗
    /// （一只后走进毒区的敌人**也应该**每秒掉血 —— 它不是"施法时被打到的那只"）
    /// ⇒ 状态不能再挂在敌人身上，必须有一张**独立于敌人生命周期的表** ✓
    ///
    /// ★ 第二件 `StatusRuntime` 表达不了的事：可读的"剩余时间 / 层数" ★
    /// `StatusRuntime` 用的是 `TicksLeft`（还剩几次）→ 换算成秒要再乘 `tickInterval`；
    /// 而规格给玩家的说法是"**持续 3 秒**、**剩多少秒**" ✓（v200：5 秒 → 3 秒，每秒的量不变）
    /// ⇒ 表里直接存秒数，报告与自检读的就是屏幕上的那个数（不换算、不四舍五入）✓
    ///
    /// ★ 第三件：**敌人死亡结算** ★
    /// 敌人一死，挂在他身上的行必须**当场消失**：留着的话下一次结算会往一个
    /// 已从 `World.Enemies` 移除的对象上打伤害（或更糟：`Id` 被复用后打到新敌人）✗
    /// 结算点有两处，缺一不可：
    ///   ① `OnEnemyDied(id)` —— 由本表**自己结算致死的那一下**时调用（DoT 打死人）
    ///   ② `Settle` 找不到目标 / 目标 `IsDead` —— 由**别人**打死的敌人在这里自然清算
    ///
    /// ★ 确定性 ★
    /// 只用 `dt` 与列表顺序，不用 `Time.deltaTime`、不用随机数 ⇒ 同种子同输入必得同结果 ✓
    /// </summary>
    public sealed class PluginDotTable
    {
        // ── 规格给死的四个数（12 件插件的描述里写着，代码里只此一处）──
        //
        // ★★ v200：三种持续效果统一 **3 秒**（用户原话："同步更新灼烧和中毒，统一是每秒扣 50%，
        //   按照各自的继承伤害，持续 3 秒"）★★
        //   改前三条都是 **5 秒**；每秒的量**一个数都没动**（仍是各自继承伤害 ×0.50，
        //   见 `WeaponSystem.BurnPerLayerPerSecond / SlowPerLayerPerSecond / PoisonPerLayerPerSecond`）✓
        //   ⇒ 总伤害从"5 跳"降到"3 跳"（同一份每秒值 × 3），这是"持续 3 秒"的必然结果，
        //     不是顺手砍一刀 ✓ 判据：`SelfCheck` 的灼烧三条（剩 / 每秒 / 到期）＋ 探针 `ProbeV200`
        //     打出的三个 `Remain` 读数，改前 5.00s、改后 3.00s ✓

        /// <summary>灼烧持续 **3 秒**（**时长不叠**：再叠只加层，不续时）</summary>
        public const float BurnSeconds = 3f;
        /// <summary>减速持续 **3 秒**（同上：不续时）。</summary>
        public const float SlowSeconds = 3f;
        /// <summary>毒区持续 **3 秒**/层。</summary>
        public const float PoisonSeconds = 3f;
        /// <summary>移速降低的**下限**（−60%）：层数再多也不超过它。
        /// ★ v197：现在它只是**兜底**（减速已改成 1 层、每层 −30%，见 `SlowMaxStacks`）——
        ///   留着是因为它是"减法类幅度"的安全阀：将来若有人把每层比例调到 0.8，
        ///   没有这一行就会得到"移速 ×0.2"这种没人设计过的值 ✓</summary>
        public const float SlowFloor = 0.60f;
        /// <summary>
        /// ★ v206：灼烧最多 **1 层** —— 与 `hoe_burn` 的 `maxStack`（表里同步收到 1）对齐 ★
        ///
        /// ── 改前是什么 ✗ ──
        ///   `maxStack 3`（插件层数）× 本表的 `BurnMaxStacks 3`（表上限）
        ///   ⇒ 命中一次叠 1 层、连烧三次叠满 3 层 ⇒ `PerSecond = 每层每秒 × 3`
        ///   ⇒ 用户看到的正是"层数把伤害叠上去"（灼烧是本轮三件里**唯一**还有叠层的）✓
        ///
        /// ── 现在：层数整条路封掉 ★（用户原话："修改灼烧插件，毒区插件，减速插件的
        ///   持有上限为 1，不再拥有叠层效果"）──
        ///   插件侧 `maxStack 1`（每次命中只叠 1 层）× 表侧 `BurnMaxStacks 1`
        ///   ⇒ `Stacks` 恒为 1 ⇒ `PerSecond` 恒为"近战伤害 ×0.50"，**连烧十次也不翻倍** ✓
        ///   时长那条规矩**没动**：仍是 `Remain = Max(Remain, 3 秒)`（再命中只刷新、不累加）✓
        ///   每秒的量、继承比例、3 秒时长**一个数都没改**（用户只要求"不再叠层"）✓
        ///
        /// ⚠️ 连带变化（写明）：**每秒伤害从"最多 150%"降到恒定 50%"** ——
        ///   这是"无层数"这个要求的必然结果，不是顺手砍的 ✓
        ///   判据：`SelfCheck` 的 ④⑤ 两条（原来是"3 次 ⇒ 3 层 ⇒ 每秒 ×3"，
        ///   现在改成"3 次 ⇒ 仍 1 层 ⇒ 每秒 ×1"，**改期望值、不删断言**）✓
        /// </summary>
        public const int BurnMaxStacks = 1;

        /// <summary>
        /// ★ v206：毒区最多 **1 层** —— 与 `fert_poison` 的 `maxStack`（v200 已收到 1）对齐 ★
        ///
        /// ── 为什么它需要单独一个常量 ✗ ──
        ///   v200 只把**插件侧** `maxStack` 改成 1，而 `AddPoisonZone` 的合并分支是
        ///   `r.Stacks += layers` —— **完全没有上限** ✗
        ///   ⇒ 同一处（0.15 米内）再炸一次就 +1 层 ⇒ `PerSecond = 爆炸伤害 ×0.50 × 层数`
        ///   ⇒ "拥有上限 1 层"这句话只在**商店里**成立，实战里毒区照样越炸越毒 ✓
        ///   ⇒ 本轮把执法点补进表里：两处写入（新增行 / 合并行）都被 `Mathf.Min` 截住 ✓
        ///     **不是**靠调用方"记得只传 1 层" —— 那种约定迟早被绕过 ✓
        ///
        /// ⚠️ 连带变化（写明）：`fert_poison` 描述里那条旧规则
        ///   "同一处再炸⇒ 叠得更毒（+1 层 = 每秒再 +50%）"**已经不成立**，
        ///   同一轮已把描述改成"同一处再炸只把毒区刷回 3 秒" ✓（描述与代码同源）
        /// </summary>
        public const int PoisonMaxStacks = 1;
        /// <summary>
        /// ★ v197：减速最多 **1 层** —— 用户原话"减速插件修改为减少 30% 移速（**没有层数效果**）" ★
        ///
        /// ── 改前是什么 ✗ ──
        ///   `maxStack 3`（插件层数）× `amount 0.12`（每层比例）× `SlowMaxStacks 5`（表的上限）
        ///   ⇒ 命中一次就叠 1 层、连打 5 次叠满 −60% ⇒ "层数"这件事在**两处**同时生效：
        ///      插件层数决定"每次叠几层"，表上限决定"最多叠到几层" ✓（两个旋钮，读起来就乱）
        ///
        /// ── 现在 ✗ 层数整条路封掉 ★
        ///   插件侧 `maxStack 1`（每次命中只叠 **1** 层）× 表侧 `SlowMaxStacks 1`
        ///   ⇒ `Stacks` 恒为 1 ⇒ `SlowFactor = 1 − min(0.60, 1 × 0.30) = **×0.70**` 恒定 ✓
        ///   ⇒ "连打五次变成 −60%" 这条**副作用**与"买第二件叠到 −60%"一起消失 ✓
        ///   ⚠️ 连带变化（写明）：**减速的每秒伤害**也只剩 1 层
        ///      （`PerSecond = 每层每秒 × 层数`）⇒ 由"远程命中 ×0.50 × 5"降到 "×0.50 × 1" ✓
        ///      这是"无层数"这个要求的**必然结果**，不是随手砍的 ✓
        ///   ⚠️ 它**不是**本表唯一的减速来源：寒冷（`chillSlowPerStack`）走的是 `ChillStacks`，
        ///      与这里完全独立（见 `StatusRuntime.SpeedFactor` 的两档相乘）⇒ 不受影响 ✓
        /// </summary>
        public const int SlowMaxStacks = 1;

        private readonly List<DotRow> _rows = new List<DotRow>();

        // 累计统计：只进日志与自检（不参与任何判定）
        public int AppliedBurn, AppliedSlow, AppliedPoison;
        public int ExpiredRows;
        public int DeathClears;
        public float DamageDealt;
        /// <summary>
        /// ★ v279：被**单跳上限**截住的结算次数（`tuning.dots.tickMaxHpFraction`，默认 25% MaxHp）。
        /// 只进读数：`CappedTicks > 0` 说明"某个 DoT 的每秒值已经大到该考虑调表了"，
        /// 不参与任何判定（夹紧本身在 `Hit` 里无条件执行）✓
        /// </summary>
        public int CappedTicks;

        public int Count => _rows.Count;
        public IReadOnlyList<DotRow> Rows => _rows;

        public void Clear()
        {
            _rows.Clear();
            AppliedBurn = AppliedSlow = AppliedPoison = 0;
            ExpiredRows = DeathClears = 0;
            DamageDealt = 0f;
            CappedTicks = 0;
        }

        // ─────────────────────────── 施加 ───────────────────────────

        /// <summary>
        /// 施加灼烧。`perLayerPerSecond` = **每层**每秒掉多少血（调用方按
        /// "**锄头这一刀的实际伤害** × `tuning.dots.burnPercent`"算好 —— v279 起不再读属性）。
        ///
        /// "时长不叠"的写法就是 `Remain = Max(Remain, BurnSeconds)`：
        /// 已经在烧的目标再被点一次，**剩余时间只会回到满值，不会累加** ✓
        /// （若写成 `Remain += BurnSeconds`，连点三次就能烧 9 秒 —— 那是另一条数值设计 ✗）
        /// </summary>
        public void ApplyBurn(int enemyId, float perLayerPerSecond, int layers)
        {
            if (layers <= 0 || perLayerPerSecond <= 0f) return;
            AppliedBurn++;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Kind != DotKind.Burn || _rows[i].EnemyId != enemyId) continue;
                DotRow r = _rows[i];
                r.Stacks = Mathf.Min(BurnMaxStacks, r.Stacks + layers);
                r.PerSecond = perLayerPerSecond * r.Stacks;
                r.Remain = Mathf.Max(r.Remain, BurnSeconds);      // ★ 时长不叠
                _rows[i] = r;
                return;
            }
            int st = Mathf.Min(BurnMaxStacks, layers);
            _rows.Add(new DotRow
            {
                Kind = DotKind.Burn, EnemyId = enemyId,
                PerSecond = perLayerPerSecond * st, Remain = BurnSeconds, Stacks = st
            });
        }

        /// <summary>
        /// 施加减速。`pctPerLayer` = 每层移速降低（v197：0.30），`perLayerPerSecond` = 每层每秒掉血
        /// （v279：= **水壶这一束的实际伤害** × `tuning.dots.slowPercent`）。
        ///
        /// ★ v197：**层数封在 1**（`SlowMaxStacks`）—— 用户要求"没有层数效果" ✓
        ///   ⇒ 同一只敌人被连打 N 次，`Stacks` 恒为 1、时长取 max（不叠加）、幅度恒为 `pctPerLayer` ✓
        ///   ⇒ 下面两处 `Mathf.Min(SlowMaxStacks, …)` 就是唯一的执法点：
        ///      **不是**在调用方"记得只传 1 层" —— 那种约定迟早被绕过 ✓
        /// </summary>
        public void ApplySlow(int enemyId, float perLayerPerSecond, float pctPerLayer, int layers)
        {
            if (layers <= 0 || pctPerLayer <= 0f) return;
            AppliedSlow++;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Kind != DotKind.Slow || _rows[i].EnemyId != enemyId) continue;
                DotRow r = _rows[i];
                r.Stacks = Mathf.Min(SlowMaxStacks, r.Stacks + layers);   // v197：上限 1 ⇒ 再打也不变
                r.PerSecond = perLayerPerSecond * r.Stacks;
                r.PctPerStack = pctPerLayer;
                r.Remain = Mathf.Max(r.Remain, SlowSeconds);      // ★ 时长不叠
                _rows[i] = r;
                return;
            }
            int st = Mathf.Min(SlowMaxStacks, layers);
            _rows.Add(new DotRow
            {
                Kind = DotKind.Slow, EnemyId = enemyId,
                PerSecond = perLayerPerSecond * st, Remain = SlowSeconds,
                Stacks = st, PctPerStack = pctPerLayer
            });
        }

        /// <summary>
        /// 留下一块毒区。`perLayerPerSecond` = **每层**每秒掉多少血（调用方按
        /// "**肥料这一炮的实际伤害** × `tuning.dots.poisonPercent`"算好）。
        /// 与灼烧相反，毒区**按位置**：同一个位置再炸一次就合并到同一行（而不是并排铺两块重叠区域）
        /// —— 但层数被 <see cref="PoisonMaxStacks"/>（v206：1）截住 ⇒ 合并只**刷新时长/半径**，
        /// 每秒的量不再因为"同一处多炸几次"而变大 ✓
        /// </summary>
        public void AddPoisonZone(Vector2 center, float radius, float perLayerPerSecond, int layers)
        {
            if (radius <= 0f || layers <= 0 || perLayerPerSecond <= 0f) return;
            AppliedPoison++;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Kind != DotKind.PoisonZone) continue;
                if ((_rows[i].Center - center).sqrMagnitude > 0.0225f) continue;   // 0.15m 内算同一块
                DotRow r = _rows[i];
                // ★ v206：层数封在 1（两处执法点之一）—— 改前是 `r.Stacks += layers`（无上限）
                r.Stacks = Mathf.Min(PoisonMaxStacks, r.Stacks + layers);
                r.PerSecond = perLayerPerSecond * r.Stacks;
                r.Remain = Mathf.Max(r.Remain, PoisonSeconds);
                r.Radius = Mathf.Max(r.Radius, radius);
                _rows[i] = r;
                return;
            }
            // ★ v206：层数封在 1（两处执法点之二）
            int st = Mathf.Min(PoisonMaxStacks, layers);
            _rows.Add(new DotRow
            {
                Kind = DotKind.PoisonZone, EnemyId = 0, Center = center, Radius = radius,
                PerSecond = perLayerPerSecond * st, Remain = PoisonSeconds, Stacks = st
            });
        }

        // ─────────────────────────── 查询 ───────────────────────────

        /// <summary>某只敌人的**移速乘数**（1 = 没被减速）。没有行 ⇒ 1 ✓</summary>
        public float SlowFactor(int enemyId)
        {
            float pct = 0f;
            for (int i = 0; i < _rows.Count; i++)
            {
                DotRow r = _rows[i];
                if (r.Kind != DotKind.Slow || r.EnemyId != enemyId) continue;
                pct += r.Stacks * r.PctPerStack;
            }
            return 1f - Mathf.Min(SlowFloor, pct);
        }

        /// <summary>某只敌人身上"还剩多少秒"的灼烧（没有则 0）。给报告/自检读。</summary>
        public float BurnRemain(int enemyId) => RemainOf(DotKind.Burn, enemyId);
        /// <summary>某只敌人身上"还剩多少秒"的减速。</summary>
        public float SlowRemain(int enemyId) => RemainOf(DotKind.Slow, enemyId);
        /// <summary>某只敌人身上叠了几层灼烧。</summary>
        public int BurnStacks(int enemyId) => StacksOf(DotKind.Burn, enemyId);
        /// <summary>某只敌人身上叠了几层减速。</summary>
        public int SlowStacks(int enemyId) => StacksOf(DotKind.Slow, enemyId);
        /// <summary>某只敌人身上"每秒掉多少"的灼烧。</summary>
        public float BurnPerSecond(int enemyId) => PerSecondOf(DotKind.Burn, enemyId);
        /// <summary>某只敌人身上"每秒掉多少"的减速。</summary>
        public float SlowPerSecond(int enemyId) => PerSecondOf(DotKind.Slow, enemyId);
        /// <summary>毒区行数。</summary>
        public int PoisonZoneCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _rows.Count; i++) if (_rows[i].Kind == DotKind.PoisonZone) n++;
                return n;
            }
        }

        private float RemainOf(DotKind kind, int id)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Kind == kind && _rows[i].EnemyId == id) return _rows[i].Remain;
            return 0f;
        }
        private int StacksOf(DotKind kind, int id)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Kind == kind && _rows[i].EnemyId == id) return _rows[i].Stacks;
            return 0;
        }
        private float PerSecondOf(DotKind kind, int id)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Kind == kind && _rows[i].EnemyId == id) return _rows[i].PerSecond;
            return 0f;
        }

        // ─────────────────────────── 推进 ───────────────────────────

        /// <summary>
        /// 推进 <paramref name="dt"/> 秒。
        ///
        /// ★ 为什么外层是"遍历行"，内层才是"满 1 秒" ★
        /// 一行一秒可能跨过多个结算点（`dt` 大于 1 时，例如掉帧或自检直接推进 2.5 秒）✓
        /// 用 `while` 而不是 `if`：`if` 会把跨过去的那些秒**丢掉**，而"每秒掉多少"是硬承诺 ✗
        /// </summary>
        public void Tick(float dt, World w, PlayerActor source)
        {
            if (dt <= 0f || w == null || _rows.Count == 0) return;
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                // ★ v186 修复①：**本表可能在一次结算里被缩小** ★
                // `Settle` → `Hit` → `w.DealDamage` 打死目标 → `OnEnemyDied` 把属于这只敌人的行
                // **当场删掉** —— 其中就包含正在推进的这一行，也可能包含**下标更小**的别的行。
                // 两种后果都是同一类错误：
                //   · 删掉的是**下标更小**的行 ⇒ 上面的行整体下移 ⇒ 旧的 `i` 已经偏大 ⇒ 越界
                //   · 删掉的正是这一行 ⇒ `_rows.RemoveAt(i)` 要删一个已不存在的下标 ⇒ 越界
                // 实机形状：表里有几行持续效果、其中一行在"满 1 秒"里把目标打死 ⇒
                //   抛 `ArgumentOutOfRangeException: Index was out of range. Must be non-negative
                //   and less than the size of the collection.`（Unity 控制台按下标越界报）
                // ⇒ 修复分两步：① 每个下标先校验还存不存在；② 写回/删除都**按行身份重新定位** ✓
                if (i >= _rows.Count) continue;

                DotRow r = _rows[i];
                r.Remain -= dt;
                r.Acc += dt;
                bool keep = true;
                while (r.Acc >= 1f && keep)
                {
                    r.Acc -= 1f;
                    keep = Settle(ref r, w, source);
                }
                // ★ 修复②：重新定位这一行（`i` 只在"表没被动过"时还可信）★
                int at = i < _rows.Count && SameRow(_rows[i], r) ? i : IndexOfRow(r);
                if (!keep || r.Remain <= 0f)
                {
                    if (keep) ExpiredRows++;      // 时长走完；目标死了的那条由 Settle 记在 DeathClears
                    if (at >= 0) _rows.RemoveAt(at);   // `at < 0` = 已被 `OnEnemyDied` 删掉 ✓
                    continue;
                }
                if (at >= 0) _rows[at] = r;
            }
            SyncSlow(w);
        }

        /// <summary>
        /// ★ v186：**按身份**比较两行是不是同一行 ★
        ///
        /// 为什么不能按下标：`Settle` 会让下标整体漂移（见 `Tick` 里的说明）。
        /// 为什么 `(Kind, EnemyId)` 不够、还要比 `Center`：毒区是**按位置**的，
        /// 同一张表里可以同时存在**两块**不同位置的毒区（`AddPoisonZone` 只在 0.15m 内合并）✓
        /// 而 `Burn` / `Slow` 在 `ApplyBurn` / `ApplySlow` 里就是"同敌人只留一行" ⇒ 两元组已唯一 ✓
        /// </summary>
        private static bool SameRow(in DotRow a, in DotRow b)
        {
            if (a.Kind != b.Kind || a.EnemyId != b.EnemyId) return false;
            if (a.Kind != DotKind.PoisonZone) return true;
            return (a.Center - b.Center).sqrMagnitude <= 1e-6f;
        }

        /// <summary>按身份找这一行现在的下标；找不到 = 它已经被销毁（返回 -1）。</summary>
        private int IndexOfRow(in DotRow r)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (SameRow(_rows[i], r)) return i;
            return -1;
        }

        /// <summary>
        /// 结算"一秒"的量。返回 false = 这一行**该整行消失**（目标已经不存在）。
        /// </summary>
        private bool Settle(ref DotRow r, World w, PlayerActor source)
        {
            if (r.Kind == DotKind.PoisonZone)
            {
                // 毒区不认敌人：半径内**所有**活着的敌人各吃一份（后走进来的也算）
                for (int i = 0; i < w.Enemies.Count; i++)
                {
                    EnemyActor e = w.Enemies[i];
                    if (e == null || e.IsDead) continue;
                    if (Vector2.Distance(e.Pos, r.Center) > r.Radius) continue;
                    Hit(e, r.PerSecond, w, source, DotKind.PoisonZone, r.Stacks);
                }
                return true;      // 区域本身留到期满（地上一块毒，不因为没人踩就消失）
            }

            EnemyActor target = FindEnemy(w, r.EnemyId);
            if (target == null || target.IsDead)
            {
                OnEnemyDied(r.EnemyId);      // ★ 敌人死亡结算：把属于他的行全部清掉
                return false;
            }
            Hit(target, r.PerSecond, w, source, r.Kind, r.Stacks);
            if (target.IsDead)
            {
                OnEnemyDied(r.EnemyId);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 打一下"持续伤害"。
        /// ★ `DamageType.Ailment` + `IgnoreDodge` ★
        /// 前者让 `DamageResolver` **跳过**"非异常命中 → 灼烧爆炸 / 叠寒冷"那两段
        /// （否则一秒一跳的 DoT 会自己给自己叠寒冷、自己引爆自己）✓
        /// 后者让持续伤害**不被闪避**：玩家闪避是"躲开攻击"，躲不开已经烧在身上的火 ✓
        /// ★ 也**不**经过 `PluginPostHit` ★ ⇒ 吸血对持续伤害不生效（规格原文）✓
        /// </summary>
        private void Hit(EnemyActor e, float amount, World w, PlayerActor source, DotKind kind, int stacks)
        {
            if (amount <= 0f) return;
            // ★★ v279：**单次 DoT 的上限** = 目标 `MaxHp × tuning.dots.tickMaxHpFraction`（默认 25%）★★
            //  为什么要它：三种持续伤害改成"继承武器这一击的实际伤害"之后，加值全部是**乘数**
            //  （暴击、富豪、多次挥舞的折减、将来的伤害道具）⇒ 一次 DoT 可能被放大到"一秒打掉大半管血"，
            //  那是一条没有设计过的曲线。上限把"再怎么叠也不会一秒秒杀"写成常量，并放进表里可调 ✓
            //  为什么夹在**这里**（每次结算、逐目标），而不是施加时夹一次：
            //    · 毒区是**按位置**的 —— 施加时只能保护"当时在场的那几只"，后走进来的照样吃满 ✗
            //    · 灼烧/减速挂在一只敌人身上，但**目标可能成长**（换波次重设血量的是另一批对象）✓
            //  `DotTickCap` 在 `MaxHp ≤ 0` 时返回 0 ⇒ 这里的 `cap > 0f` 就变成"不夹"（防御性写法）✓
            float cap = WeaponSystem.DotTickCap(e.Health.MaxHp);
            if (cap > 0f && amount > cap)
            {
                amount = cap;
                CappedTicks++;
            }
            float before = e.Health.Hp;
            // ★ v188：把"这一下是谁打的"交给弹字 ★
            // `w.DealDamage` 一路走到 `Actor.OnHitLanded` → `Popup(...)` 发 `FloatingTextEvent`，
            // **全都在同一个调用栈里同步完成** ⇒ 在这里立一个"本次结算的持续效果种类"，
            // 弹字那边读一次就够。为什么不能靠 `DamageRequest.Type` 分辨：
            //   肥料爆炸（`RunSession.BlastDamageAt`）用的也是 `DamageType.Ailment` ✗
            //   ⇒ 只看 Type 会把爆炸的白字也画成骷髅头 ✗
            // 用 `try/finally` 而不是"设完再清"：`DealDamage` 内部若有任何一条分支提前抛，
            // 标签会留在静态字段上，把**下一条**普通弹字染色 —— 这是最难查的一类串味 ✓
            DotPopupHint.Set(kind, stacks);
            try
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Ailment,
                    RawValue = amount,
                    Source = source,
                    Target = e,
                    IgnoreDodge = true,
                    // ★★ v286：**持续伤害吃灼印倍率，但不摘印** ★★
                    //   用户原话："灼印插件疑似在与毒区、自爆等插件配合时不生效。"
                    //   改前这一跳走的是闸门的"读即摘"路径 ⇒ 它是**每一次主爆之前最先命中的那个伤害**
                    //   （毒区每 1 秒一跳，量只有放毒那一炮的 50%×层数）⇒
                    //   印记被这最弱的一跳用掉，主爆/下一记直击反而一点倍率都吃不到 ✓
                    //   现在：倍率照吃（`World.DealDamage` 里 `MarkMultiplier` 对所有来源生效），
                    //   但**不把印摘掉** ⇒ 印记留给下一次直接伤害 ✓
                    //   为什么是"吃但不动"而不是"完全不参与"：用户要的是"毒区也被印记放大"，
                    //   而"印记 = 放大下一次**重击**"的语义不能被一跳弱伤害废掉 —— 两条都要成立，
                    //   唯一的交集就是这一条 ✓
                    NoMarkConsume = true
                });
            }
            finally { DotPopupHint.Clear(); }
            float dealt = before - e.Health.Hp;
            if (dealt > 0f) DamageDealt += dealt;
        }

        /// <summary>
        /// ★ 敌人死亡结算 ★ 把挂在 <paramref name="enemyId"/> 上的行全部移除。
        /// 由 `Settle` 在本表自己打出致命一击时调用，也可被外部直接调用。
        /// </summary>
        public void OnEnemyDied(int enemyId)
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                if (_rows[i].Kind == DotKind.PoisonZone) continue;   // 毒区属于地面
                if (_rows[i].EnemyId != enemyId) continue;
                _rows.RemoveAt(i);
                DeathClears++;
            }
        }

        /// <summary>
        /// 把减速写回每只敌人的状态（`StatusRuntime.PluginSlow`）。
        ///
        /// ★ 为什么每帧**全量重算**，而不是在施加/移除时改 ★
        /// 增量写法要维护"行没了要把敌人的减速降回去"，而行的消失有四个来源
        /// （到期 / 目标死亡 / 换波 / 清场）—— 漏一个就会留下一只**永久减速**的敌人 ✗
        /// 全量重算的代价是 O(敌人 × 行)，两边都是十几个，可忽略 ✓
        ///
        /// ★ 为什么公开，而且 `PluginPostHit` 里也要手调一次 ★
        /// `Tick` 每帧跑，但"命中当场"到"下一帧"之间这一会儿，敌人的 `PluginSlow` 还是旧值 ✗
        /// 实时玩法里那是 16ms、无害；而**探针**是"打一下 → 立刻读移速" ⇒
        /// 不手调就会读到 1.00，把一条真的接上了的效果报成"没变化"✓
        /// （本工程的老毛病：**读数窗口**与**生效窗口**错开 ⇒ 假红 ✓）
        /// </summary>
        public void SyncSlow(World w)
        {
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.Status == null) continue;
                e.Status.PluginSlow = 1f - SlowFactor(e.Id);      // 0 = 没减速
            }
        }

        private static EnemyActor FindEnemy(World w, int id)
        {
            for (int i = 0; i < w.Enemies.Count; i++)
                if (w.Enemies[i] != null && w.Enemies[i].Id == id) return w.Enemies[i];
            return null;
        }

        /// <summary>一行摘要，给日志/自检/报告直接用（"谁、每秒多少、剩多久、几层"）。</summary>
        public string Describe()
        {
            if (_rows.Count == 0) return "（空表）";
            var sb = new StringBuilder();
            for (int i = 0; i < _rows.Count; i++)
            {
                DotRow r = _rows[i];
                if (i > 0) sb.Append("｜");
                switch (r.Kind)
                {
                    case DotKind.Burn:
                        sb.Append("灼烧#").Append(r.EnemyId); break;
                    case DotKind.Slow:
                        sb.Append("减速#").Append(r.EnemyId); break;
                    default:
                        sb.Append("毒区@(").Append(r.Center.x.ToString("0.0")).Append(',')
                          .Append(r.Center.y.ToString("0.0")).Append(")r").Append(r.Radius.ToString("0.0"));
                        break;
                }
                sb.Append(" 每秒").Append(r.PerSecond.ToString("0.00"))
                  .Append(" 剩").Append(r.Remain.ToString("0.00")).Append("s")
                  .Append(" ×").Append(r.Stacks).Append("层");
                if (r.Kind == DotKind.Slow)
                    sb.Append(" 移速×").Append(SlowFactor(r.EnemyId).ToString("0.00"));
            }
            return sb.ToString();
        }
    }
}
