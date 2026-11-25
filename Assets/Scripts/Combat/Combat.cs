using System;
using UnityEngine;

namespace Seedfall
{
    /// <summary>生命与护盾。护盾先于生命承受伤害。</summary>
    public sealed class HealthPool
    {
        public float MaxHp;
        public float MaxShield;
        public float Hp;
        public float Shield;

        /// <summary>v47：所属角色——治疗时才能在他身边弹出 "+N"。</summary>
        public Actor Owner;

        /// <summary>
        /// ★★ v196：**这一池血是否只收整数伤害** ★★ —— 只有**玩家**的池是 `true`。
        ///
        /// 判据（用户原话"玩家伤害不再转换为整数，允许小数"）：
        ///   · **玩家 → 敌人** 的伤害**保留小数**（`false`）：武器 4.55 打 800 血的怪 ⇒ 实扣 4.55 ✗ 不是 5
        ///   · **敌人 → 玩家** 的伤害**仍是整数血点**（`true`）：玩家血池是 10 点整数域，
        ///     敌人曲线值经 `EnemyDef.PlayerDamagePoints` 已经是整数点 ⇒ 这里再 round 是恒等（不双算）✓
        ///
        /// ── 为什么是"按池"而不是"按每次呼叫传来源" ──
        /// `ApplyDamageSplit` 有 7 个呼叫点（`Resolve` / 灼烧溅射 / 毒 / 火 / 破冰 /
        /// 自检夹具 / 调试探针），再加一个 `ApplyDamage` 转发层。若把来源做成参数，
        /// 那就是**7 处都要传对**，漏一处就静默回到"全取整"或"全小数" ✗
        /// 而本作里"谁会被谁打"是**结构性的**：玩家只挨敌人的，敌人只挨玩家的
        /// （含玩家造成的 DoT / 灼烧溅射）⇒ 池的归属就是来源，一个布尔值就够 ✓
        /// 与 `EnemyDef.PlayerHpUnit` 同一个思路：量纲由**目标**决定，不由调用点决定 ✓
        ///
        /// ⚠️ 唯一写者是 `Actor` 的构造函数（`Health.IntegerOnly = this is PlayerActor`）✗
        ///    别在呼叫点改它：那是把"这一池的量纲"变成"这一次的口气"✗
        /// </summary>
        public bool IntegerOnly;

        public bool IsDead => Hp <= 0f;
        public float HpPct => MaxHp <= 0f ? 0f : Mathf.Clamp01(Hp / MaxHp);

        public void Setup(float maxHp, float maxShield, bool refill = true)
        {
            MaxHp = Mathf.Max(1f, maxHp);
            MaxShield = Mathf.Max(0f, maxShield);
            if (refill)
            {
                Hp = MaxHp;
                Shield = MaxShield;
            }
            else
            {
                Hp = Mathf.Min(Hp, MaxHp);
                Shield = Mathf.Min(Shield, MaxShield);
            }
        }

        /// <summary>返回实际被吸收的总量（护盾 + 生命）。</summary>
        public float ApplyDamage(float amount, bool bypassShield = false)
            => ApplyDamageSplit(amount, bypassShield, out _, out _);

        /// <summary>
        /// ★ v186：把「护盾吸收」与「生命吸收」**分开返回** ★ ——
        /// 需求⑤要求玩家受击时弹**两行**：扣血红 / 扣护盾蓝 ⇒
        /// 而"扣了多少血、扣了多少盾"只有这一层知道（扣的顺序也在这里：盾先）。✗
        ///
        /// 为什么不是"调用方自己看 Shield 前后差"：
        /// 那样一来同一个数值就有了**两份算法**，盾正好被扣光的那一下两边会算得不一样 ✓
        /// `ApplyDamage` 仍保留（其余十几个调用点只要总量），它转调本函数 ⇒ 只有一份实现 ✓
        /// </summary>
        public float ApplyDamageSplit(float amount, bool bypassShield, out float shieldAbsorbed, out float hpAbsorbed)
        {
            shieldAbsorbed = 0f;
            hpAbsorbed = 0f;
            if (amount <= 0f || IsDead) return 0f;
            // ════════════════════════════════════════════════════════════════
            // ★★ v196：**取整只对「敌人 → 玩家」这一侧** ★★
            //   （用户原话："玩家伤害不再转换为整数，允许小数"）
            //
            // 改前（v194/v195）这里是**无条件** `Mathf.Max(1f, Mathf.Round(amount))`，
            // 于是玩家打出去的小数伤害也被抬成整数 ✗ —— 实测后果：
            //   · 锄头 (4 + 0.55) × 1.0 = **4.55** 打 800 血的怪 ⇒ 实扣 **5**（虚增 0.45）
            //   · 毒跳伤 `0.15 × 125 = 18.75` ⇒ 实扣 **19**（每一跳都虚增 1/4 点）
            // 这正是"上一轮把玩家→敌人也取整了"的那个位置 ✓
            //
            // ── 现在的分工（判据见 `IntegerOnly` 的注释）──
            //   `IntegerOnly == true`（玩家池）⇒ 仍是 `max(1, round(x))`，行为与改前**逐位相同**
            //   `IntegerOnly == false`（敌人池）⇒ **原样进池**，一位不动
            //
            // ── 为什么敌人侧**没有**"至少 1 点"的下限 ──
            // 那个下限的存在理由是"0.4 点的伤害取整成 0 会让持续伤害变成假的"（v194 注释）。
            // 而小数侧根本不需要它：0.4 就扣 0.4，血条照样动 ⇒ 下限会**凭空造伤害** ✗
            // （`amount <= 0` 已经在上面返回 0，不会凭空造；`> 0` 的输入原样落账 ✓）
            // ════════════════════════════════════════════════════════════════
            if (IntegerOnly) amount = Mathf.Max(1f, Mathf.Round(amount));
            if (!bypassShield && Shield > 0f)
            {
                shieldAbsorbed = Mathf.Min(Shield, amount);
                Shield -= shieldAbsorbed;
                amount -= shieldAbsorbed;
            }
            if (amount > 0f)
            {
                hpAbsorbed = Mathf.Min(Hp, amount);
                Hp -= hpAbsorbed;
            }
            if (Hp < 0f) Hp = 0f;
            return shieldAbsorbed + hpAbsorbed;
        }

        public void Heal(float amount)
        {
            // ★★ v306 纯插入守卫：训练场里**一律不回血**（用户原话："不要出现回血"）★★
            //
            //  ── 为什么拦在**这里**，而不是去每个来源打补丁 ──
            //    本方法是本工程回血的**唯一收口点**。全部来源（grep `\.Heal\(` 逐条核对，
            //    见交付报告 ③）都汇到它：
            //      · 红心拾取       `Actors.cs:1194`  `player.Health.Heal(Heal);`
            //      · 生命回复 regen `Actors.cs:828`   `Health.Heal(regen * dt);`（当前 `regen` 恒 0）
            //      · 整点回满       `RunSession.cs:5979` `p.Health.Heal(p.Health.MaxHp * healPct);`
            //      · 吸血 settle    `RunSession.cs:6704` `owner.Health.Heal(heal);`
            //      · 余温（击杀回血）`RunSession.cs:6844` `p.Health.Heal(p.Health.MaxHp * heal);`
            //      · 中毒吸血       `Combat.cs:707`     `w.Player.Health.Heal(dealt * …);`
            //    ⇒ 一处守卫 = 六个来源全部失效，且**没有第七个来源能绕过它** ✓
            //
            //  ── 守卫为 false（= 正常战斗）时，行为**逐字节不变** ──
            //    `TrainingMode.NoHeal` 的初值是 `false`，全文唯一的写者是
            //    `TrainingMode.Enable()` / `Disable()`，而 `Enable()` 只被训练场景的
            //    `TrainingBootstrap.Awake` 调用（`SeedfallArena.unity` 里没有这个组件）
            //    ⇒ 战斗模式下这一行等于 `if (false) return;` ⇒ 下一行原样执行 ✓
            //
            //  ⚠️ 只拦**生命**。`RefillShield()` / `Setup(refill:…)` / 训练场稻草人自己的
            //     "掉到 50% 就写满"（`TrainingBootstrap.cs:350` 直接写 `Health.Hp`，不走本方法）
            //     都不受影响 —— 那些不是"回血" ✓
            if (TrainingMode.NoHeal) return;
            if (amount <= 0f || IsDead) return;
            float before = Hp;
            Hp = Mathf.Min(MaxHp, Hp + amount);
            // v47：回血在角色旁边弹 "+N"（只报整点，避免毒的零头刷屏）
            float gained = Hp - before;
            if (gained >= 1f && Owner != null)
            {
                Bus.Publish(new FloatingTextEvent
                {
                    Pos = Owner.Pos,
                    Text = "+" + Mathf.RoundToInt(gained),
                    Kind = PopupKind.Heal
                });
            }
        }

        public void RefillShield() => Shield = MaxShield;

        public void Kill() => Hp = 0f;
    }

    public struct DamageRequest
    {
        public DamageType Type;
        /// <summary>
        /// ★★ v192：**目标不同，这个数的量纲不同**（血池整数化之后必须写清楚）★★
        ///
        ///   · 打**敌人**：就是武器算出来的伤害（武器基础值 + 属性）× 倍率 —— 一切照旧 ✓
        ///   · 打**玩家**：它是"敌人曲线值"（第 1 分钟基础兵 **0.455** 这种），
        ///     `Resolve` 会过 `EnemyDef.PlayerDamagePoints` 换成**整数血点**（0.455 → 5）✓
        ///
        /// ⚠️ 所以**别拿血点当 RawValue 传给玩家**：玩家血池 100 点，
        /// 传 30 的意图若是"扣 30 点血"，实际会扣 `Round(30 × 10) = 300` ⇒ 一击必死 ✗
        /// 想"扣 30 点"就传 **3**（= 30 / hpUnit）。自检与探针里所有打玩家的夹具都按这一条写 ✓
        /// </summary>
        public float RawValue;
        public Actor Source;
        public Actor Target;
        // ★ T2：`DamageRequest.FromPlant` 字段**已删除** ✗ ★
        //   它的写者（`PlantAlly.Fire` / `ApplyAilmentHit` / `OnDeath` / `Projectile.PlantShot`）
        //   与读者（下面 `Resolve` 里的两处）**全部随植物实体一起下线** ✓
        //   ⇒ 零写者零读者 ⇒ 字段本身没有存在理由 ✓
        //   ⚠️ **不需要**为它保留"设计位"：它是"这一发伤害来自植物帮手"的标记，
        //      而"植物帮手"这个来源在新玩法里不存在 ✓（与 `Projectile.ExplodeOnEnd` 那种
        //      "看起来是通用的溅射设计位"不同 —— 那个是**武器**语义，这个是**植物**语义 ✓）
        public float Knockback;
        public AilmentKind Ailment;
        public float AilmentValue;
        public bool IgnoreDodge;
        public bool IsReflect;

        /// <summary>
        /// v87：这一击是否暴击。
        ///
        /// 暴击倍率是在 AttackFormula 里**乘进 RawValue** 的，所以数值本身看不出暴击与否；
        /// 之前这个标记没有往下传，于是暴击和普通命中弹出来的都是同一种白字——
        /// 玩家报的"暴击文字疑似异常"就是这个（PopupKind.Crit 定义了却从来没人发出）。
        /// 默认 false 是对的：绝大多数构造点（异常伤害、反射、环境伤害）本来就不该暴击。
        /// </summary>
        public bool Crit;

        /// <summary>
        /// ★★ v286：**这一路伤害"只吃灼印倍率、不把印摘掉"** ★★
        ///
        /// 用户原话："灼印插件疑似在与毒区、自爆等插件配合时不生效。"
        ///
        /// ── 为什么需要一个字段，而不是在闸门里按 `DamageType` 猜 ──
        ///   闸门（`World.DealDamage`）里能看到的只有 `Type`，而**同一个 `Ailment`** 既被
        ///   毒区 tick 用（`PluginDotTable.Hit`），又被肥料爆炸用（`RunSession.BlastDamageAt`）。
        ///   靠 `Type` 分辨 ⇒ 要么放行毒区 tick（它就会偷吃印），要么连爆炸一起挡掉 ✗
        ///   所以"这一路该不该摘印"必须是**调用方声明的事实**，不是闸门能推断的东西 ✓
        ///
        /// ── 谁该写 true（只有两处，别再多）──
        ///   · **持续伤害 tick**：`PluginDotTable.Hit`（灼烧 / 减速 / 毒区每秒一跳）✓
        ///     一跳的量只有"这一炮的 50%×层数"，却会出现在每一次主爆之前 ⇒
        ///     改前它**必然**是第一个摘印的人 ⇒ 大爆永远吃不到倍率 ✓（这正是用户报的症状）
        ///   · **子爆**：`RunSession.BlastDamageAt(..., mainBlast: false)`（主爆 × 0.5）✓
        ///     它同样排在下一次主爆之前 ⇒ 同一个病 ✓
        /// 其余（近战直击 / 激光 / 剑气 / 主爆 / 回响 / 弹射）一律保持默认 false = 会摘印 ✓
        ///
        /// ⚠️ 默认值就是 false（结构体默认值）⇒ 既有构造点**一个字都不用改**，
        ///   行为与 v285 逐位相同（除了"闪避不再白吃一颗印"，那一条在闸门里）✓
        /// </summary>
        public bool NoMarkConsume;
    }

    /// <summary>
    /// ★ v184：**一次伤害结算的全部读数** —— 三处（敌人受到伤害 / 武器实际伤害 / UI 弹字）的唯一来源 ★
    ///
    /// 旧版这个结构体只有 `Dealt`，而且 `Crit` 在 `Resolve` 里**写死成 `false`**
    /// （`new DamageResult(dealt, false, false, killed)`）✗
    /// 后果：`DamageResult.Crit` 永远是假，任何"按结算结果判暴击"的读者都会判错 ✓
    /// 真正的暴击标记当时只有 `req.Crit` 一条路传下去（`OnHitLanded`），所以弹字是对的、
    /// 而结算结果本身是错的 —— 这种"两处真相"正是需求③要消灭的东西 ✗
    ///
    /// 现在两个数**同时**由 `Resolve` 写出，谁都不许自己再算一份：
    ///   · `RawValue` —— 武器这次出手算出来的伤害（= 卡面"武器实际伤害"）
    ///   · `Dealt`    —— 真正从血/盾里扣掉的量（= "敌人受到伤害"；击杀时可能小于 RawValue）
    ///
    /// ★ v186：三处的同步**改成"一个对象传下去"**（用户需求③原话：
    ///   "最终计算出来的伤害和暴击效果同步于敌人受到的伤害和UI弹字显示的伤害（以参数形式传）"）★
    /// 旧写法是 `OnHitLanded(float amount, World w, bool crit)` —— 数和一个暴击标记**分两个参数**传 ✗
    /// 于是弹字那侧"能拿到数"，但拿不到"这个数是怎么来的"：它想分辨"暴击样式"要靠第二个 bool，
    /// 想分辨"扣的是盾还是血"根本没有第三个参数可拿 ✗
    /// 现在签名是 `OnHitLanded(in DamageResult r, World w)` ⇒ 弹字**只从这一个参数取数** ✓
    /// 谁都不能绕开它自己再算一遍（算出来的第二份数就是上一轮"弹字绑错伤害"的根）✓
    /// </summary>
    public readonly struct DamageResult
    {
        /// <summary>本次请求的伤害（武器侧算出来的值，未经血量裁剪）。</summary>
        public readonly float RawValue;
        /// <summary>真正扣掉的血/盾量（敌人受到伤害）。</summary>
        public readonly float Dealt;
        /// <summary>
        /// ★ v186：`Dealt` 的两个分量 —— 护盾吸收 / 生命吸收（`Dealt == ShieldDealt + HpDealt`）★
        /// 需求⑤：玩家受击要弹**两行**（扣血红 / 扣护盾蓝）⇒ 颜色分得开的前提是数也分得开 ✓
        /// 敌人身上 `ShieldDealt` 一般是 0 ⇒ 那一行不掉血，弹字与改前逐位相同 ✓
        /// </summary>
        public readonly float ShieldDealt;
        public readonly float HpDealt;
        public readonly bool Crit;
        public readonly bool Dodged;
        public readonly bool Killed;

        public DamageResult(float rawValue, float dealt, bool crit, bool dodged, bool killed,
                            float shieldDealt, float hpDealt)
        {
            RawValue = rawValue; Dealt = dealt; Crit = crit; Dodged = dodged; Killed = killed;
            ShieldDealt = shieldDealt; HpDealt = hpDealt;
        }

        /// <summary>什么也没发生（闪避 / 无敌帧 / 目标已死）。</summary>
        public static readonly DamageResult Miss = new DamageResult(0f, 0f, false, false, false, 0f, 0f);
    }

    /// <summary>全部伤害/辅助公式的唯一实现（策划案 §3、§4）。</summary>
    public static class DamageResolver
    {
        /// <summary>
        /// z = (基准伤害 + 伤害属性) × 伤害倍率 × crit，下限 minDamage。
        ///
        /// v45：攻击伤害（加法）与攻击倍率（百分比，1.0 = 100%）拆成两条独立属性，各自成长、各自显示。
        /// 攻击倍率为 100% 时结果与 v44 完全一致（可回归验证）。
        ///
        /// v162（A3）：**删除 inheritMult（继承率）参数**，把 `基准 + 伤害 × 继承率` 改成 `基准 + 伤害`。
        ///
        /// ★ 为什么继承率必须删掉，而不是留成恒为 1 的参数 ★
        /// 它当初存在的理由是"武器伤害是从玩家身上**继承**来的"——`基准 + 玩家攻击 × 继承率`，
        /// 那个比例表达的是"这件武器继承了玩家攻击力的百分之几"（锄头 35% / 水壶 30%）。
        /// 新版武器是玩家自己的三把工具，各自吃一条**专属的**伤害属性
        /// （锄头→近战伤害 / 水壶→远程伤害 / 肥料→爆炸伤害），
        /// "继承多少"这个中间量不再存在——**直接相加就是它的语义**。
        /// 留一个恒为 1 的参数只会让每个调用点都传一个无意义的数，
        /// 而且会让下一个人以为"改这个比例能调伤害"（它已不再是设计上的旋钮）。
        /// </summary>
        public static float AttackFormula(float baseValue, float damageStat, float atkMult,
                                          bool crit, float critDmg, float minDamage = 1f)
        {
            // ★★ v195（用户原话："近战伤害、远程伤害、爆炸伤害为负数视作 0 参与伤害计算"）★★
            //   **钳的是这个加数本身**，不是整条结果 ✗ —— 这一点是需求的分水岭：
            //     · 钳结果（改前）：`max(minDamage, (基础 − 3) × 倍率)` ⇒ 减益把伤害拉到基础
            //       之**下**，再被 0.1 兜成一个"很小但非零"的数 ⇒ 玩家买到 `−3 近战` 后
            //       面板写 −3、实战却是 0.1 伤害 ⇒ 显示与结算不一致 ✓
            //     · 钳加数（现在）：`max(0, 属性)` ⇒ `−3` 与 `0` **逐位等价**，
            //       而存储值仍是 −3（`StatBlock.FloorOf` 对三条伤害属性返回负无穷，
            //       所以面板照样印得出来"近战伤害 −3"）✓
            //   `minDamage` 保留：它管的是"基础伤害 × 倍率"整条的下限（0 伤害不该发生）✓
            float damageAdd = NonNegativeDamageStat(damageStat);
            float v = (baseValue + damageAdd) * atkMult;
            if (crit) v *= critDmg;
            return Mathf.Max(minDamage, v);
        }

        /// <summary>
        /// v195：三条伤害属性（近战 / 远程 / 爆炸）代入公式前一律**当 0**。
        /// 唯一实现 —— `AttackFormula` 与所有"按伤害属性比例"的派生量都走它。
        /// `NaN` 也归零（`NaN > 0` 为 false）⇒ 一个坏数据不会让整条伤害变成 NaN 传到血条上 ✓
        /// </summary>
        public static float NonNegativeDamageStat(float damageStat)
            => damageStat > 0f ? damageStat : 0f;

        /// <summary>
        /// ★★ v195：**`ExplodeFormula` 已删除**（连同 `StatId.ExplodeDamage`）★★
        /// 用户原话："删除爆炸倍率这个参数删掉，肥料伤害 =（基础伤害 + 爆炸伤害）× 伤害倍率"
        /// ⇒ 肥料不再有"第二层乘区"，三把武器**同一个式子** ✓
        /// 旧语义留档：爆炸 = `普通公式结果 × 爆炸倍率(2.5)` —— 那是"肥料一炮 ≈ 2.5 刀"的来源，
        /// 而用户要的是"肥料就是一把武器"，所以乘区整条删掉，不做成恒为 1 的参数 ✗
        /// （留一个恒 1 的参数 = 下一个人以为它能调，而它已经被设计定为 1）
        /// </summary>

        // ══════════════════════════════════════════════════════════════════════
        // ★ v184：**武器一次出手的伤害 —— 唯一的出口** ★
        //
        // 用户口径（需求①）：
        //     伤害 = (武器基础伤害 + 对应类型的基础伤害[近战/远程/爆炸]) × 伤害倍率
        // 需求②：
        //     每次攻击 ⇒ 按暴击率掷一次 ⇒ 命中则 **整条算完的伤害** × 暴击伤害（初始 200%）
        //
        // 三条攻击路径（锄头 `DoHoeSwing` / 水壶 `FireBeam` / 肥料 `ExplodeAt`）
        // 必须**全部**走下面这两个函数 ⇒ 公式改一处，三条线同时改 ✓
        //
        // ★ 旧版它们各写一行 `AttackFormula(...)`，于是真的走歪过 ★
        //   · 锄头/肥料乘了 `ToolMult(品质)`，水壶没乘 ⇒ 三条线**质量层不一致** ✗
        //   · 肥料那一行写死 `false, 1f` ⇒ 它**永远不暴击**，而武器卡上却照着
        //     "普通/暴击两个数"在显示 ⇒ 卡面承诺了引擎不给的东西 ✗
        //   ⇒ 需求③"三处必须是同一个值"的根不在弹字，而在这里：先得只有一份算法 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **掷一次暴击**（需求②的唯一实现）。
        ///
        /// `Rng.Chance` 内部已经钳过 0..1，这里再钳一次是刻意的：
        /// 它让"暴击率永远是一个概率"成为**不依赖配置表的不变量** ✗
        /// （`caps.critRate` 是数据，数据被改成 2 也不会让这里必暴 ✓）
        ///
        /// ★ 必须是 `ref`，不能按值传 ★
        /// `Pcg32` 是**可变 struct**（`Pcg32.cs:9`，`_state` 在 `NextUInt` 里自增）✗
        /// 按值传的话 `Chance` 推进的是**副本**，调用方的流一次都不会前进 ⇒
        /// 每次掷骰都从同一个 state 出发 ⇒ **暴击结果是固定的**（要么次次暴击、要么永不暴击）
        /// 而且不报错、不抛异常 —— 只是"暴击率看起来完全没用" ✓
        /// 这类 bug 只有靠"按引用传可变状态"这条纪律防住，靠读代码很难看出来 ✓
        /// </summary>
        public static bool RollCrit(ref Pcg32 rng, float critRate)
            => rng.Chance(Mathf.Clamp01(critRate));

        /// <summary>
        /// ★ v186：**武器伤害 = 用户给的那条式子的唯一实现** ★
        ///
        ///     伤害     = (武器基础 + 对应类型伤害[近战/远程/爆炸]) × 伤害倍数
        ///     暴击效果 = 伤害 × 暴击伤害          ← **内部计算，不在 UI 显示**
        ///     每次攻击 = Rng.Chance(Clamp01(暴击率)) 命中 ⇒ 取暴击效果
        ///
        /// ★ **公式里没有"品质"这一项，一行都不能有** ★
        /// 用户原话："删除武器品质的概念，武器没有品质区分"✗
        /// 上一版每把武器还乘一层 `quality.mult`（卡面印成 `× 品质 1.00`）——
        /// 即使当前表里 `mult` 恰好是 [1,1,1,1,1]、数值上不漂移，
        /// 它也**在式子里留了一个能改变伤害的口子** ✗ ⇒ 本轮把那层彻底删掉：
        /// `weaponBase` 就是武器的基础值本身，除此之外只有一个加项与一个乘区 ✓
        ///
        /// ★ 暴击为什么在这一个函数里，而不是拆成"普通伤害 + 暴击伤害"两次调用 ★
        /// 需求把"暴击效果"定义为**整条算完的伤害**再 × 暴击伤害 ⇒ 顺序是
        /// `(基础 + 属性) × 倍率` **然后** `× 暴击伤害` ✓
        /// 拆成两次调用会把乘法顺序交出去（写成 `(基础×暴击伤害 + 属性) × 倍率` 也编译得过）✗
        /// </summary>
        public static float WeaponDamage(float weaponBase, float damageStat, float damageMult,
                                         bool crit, float critDmg, float minDamage)
            => AttackFormula(weaponBase, damageStat, damageMult, crit, critDmg, minDamage);

        /// <summary>
        /// 肥料：**与锄头/水壶逐字同一个式子**（v195 删除爆炸倍率后）。
        ///
        /// 保留这个别名而不是让调用点改写成 `WeaponDamage` 的理由：调用点（`RunSession.ExplodeAt`、
        /// 武器卡预览、自检）写的是"肥料这一刀"，读代码的人应当一眼看出**它走的是哪条线** ✓
        /// 而实现**只有一份**（转调 `WeaponDamage`）⇒ 公式改一处，三条线同时改 ✓
        /// </summary>
        public static float WeaponBlastDamage(float weaponBase, float damageStat, float damageMult,
                                              bool crit, float critDmg, float minDamage)
            => WeaponDamage(weaponBase, damageStat, damageMult, crit, critDmg, minDamage);

        /// <summary>q = (x + y·m)·n（辅助恢复）；n = 攻击倍率。</summary>
        public static float HealFormula(float x, float y, float m, float n)
        {
            return Mathf.Max(0f, (x + y * m) * n);
        }

        /// <summary>护卫盾 = (x + maxHp·m)·n，只继承最大生命值；n = 攻击倍率。</summary>
        public static float GuardFormula(float x, float maxHp, float m, float n)
        {
            return Mathf.Max(1f, (x + maxHp * m) * n);
        }

        /// <summary>w = z·(0.1 + leech)。</summary>
        public static float LeechFormula(float dealt, float leech, float leechBase)
        {
            return Mathf.Max(0f, dealt * (leechBase + leech));
        }

        public static float AilmentThreshold(float maxHp, FormulaDef f)
        {
            return Mathf.Max(f.freezeThresholdMin, maxHp * f.freezeThresholdRatio);
        }

        /// <summary>灼烧爆炸：对半径内其它敌人造成伤害（不再产生新的灼烧，防止链式爆炸）。</summary>
        private static void BurnExplosion(World w, EnemyActor source, float damage)
        {
            float radius = w.Tuning.formula.burnExplodeRadius;
            int maxTargets = Mathf.Max(1, Mathf.RoundToInt(w.Tuning.formula.burnSpreadTargets));
            int hits = 0;
            for (int i = 0; i < w.Enemies.Count && hits < maxTargets; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == source || e.IsDead) continue;
                if (Vector2.Distance(e.Pos, source.Pos) > radius) continue;
                e.Health.ApplyDamage(damage);
                hits++;
            }
            Bus.Publish(new BurnExplodeEvent { Pos = source.Pos, Damage = damage });
        }

        public static DamageResult Resolve(in DamageRequest req, World w)
        {
            Actor target = req.Target;
            if (target == null || !target.Alive) return DamageResult.Miss;

            // ★ T2：**幼苗免疫伤害**的守卫已删除 ✗ ★
            //   原来是 `if (target is PlantAlly seedling && seedling.Growing) return 空结果;` ✓
            //   植物已不参战 ⇒ 不存在"生长中的植物被攻击"这件事 ⇒ 守卫没有可命中的目标 ✓
            //   ⚠️ 它不是"某条规则的唯一实现"：生长中的地块是 `FarmPlot`，
            //      而地块**根本不在 `DamageRequest.Target` 的值域里**（只可能是 Actor）✓
            //      ⇒ 删掉不会让"幼苗可以被打了"，因为幼苗已经不是 Actor ✓

            // 受击无敌帧（玩家；不可被"无视闪避"绕过）
            if (!req.IsReflect && target.IsInvulnerable)
                return DamageResult.Miss;

            // 闪避：只有玩家有闪避属性
            if (!req.IsReflect && !req.IgnoreDodge && target is PlayerActor player)
            {
                float dodge = player.Progress.Stats.Get(StatId.Dodge);
                if (w.Rng.Chance(dodge))
                {
                    Bus.Publish(new EvadeEvent { ActorId = target.Id });
                    // ★ v197：虚化 + 灰色「闪避」（用户需求①）★
                    // 挂在**这一处**的理由：这是全工程**唯一**掷闪避的地方
                    // （`Rng.Chance(dodge)` 的消费点只有这一行，见 `Combat.Resolve`），
                    // 放在别处就得再赌一次"别的地方也会闪" ✗
                    // 它只做表现（虚化计时 + 一条文字弹字），**不改结算**：
                    // 下面返回的仍是"这一下打空"（`Dodged = true`、`Dealt = 0`）✓
                    player.OnEvaded();
                    return new DamageResult(req.RawValue, 0f, false, true, false, 0f, 0f);
                }
            }

            // v9：非异常命中命中"灼烧中的敌人"→ 爆炸（范围伤害）
            if (target is EnemyActor burning && burning.Status.Burning && req.Type != DamageType.Ailment && req.RawValue > 0f)
            {
                float blastDamage = req.RawValue * w.Tuning.formula.burnExplodeFactor;
                if (blastDamage > 0f) BurnExplosion(w, burning, blastDamage);
            }
            // ★ v9：非异常命中 → 给"寒冷"叠加一层（每层移速 -20%，满层冰冻 1 秒）
            if (target is EnemyActor chilled && req.Type != DamageType.Ailment && req.RawValue > 0f)
                chilled.Status.AddChill(1, req.RawValue, w.Tuning.formula);

            // ════════════════════════════════════════════════════════════════════
            // ★★ v192：**落到玩家身上的伤害取整**（用户需求②"敌人伤害处理，全部转换为整数"）★★
            //
            // ── 为什么换算必须发生在这一行之前 ──
            // 敌人的 `RawValue` 是曲线值（v194 新刻度下第 1 分钟基础兵 **4.55**、远程弹 **3.03**、
            // 精英砸地 **6.07**；v193 那套旧刻度是 0.455 / 0.303 / 0.607）。
            // 玩家血池在 v192 整体放大成 `hpUnit = 10` 倍（玩家 **100 血 / 20 盾**）⇒
            // 两边同在"血点"这个整数域 ⇒ `血点 = Round(4.55 × 10) = 46` ✓
            // 若这里**不换**，4.55 打 100 血 = 4.55% ⇒ 血量表与 `tuning.json` 会对不上 ✗
            // 若把换算放进 `DamageForWave`（曲线层），探针读到的曲线值就不再是"设计值" ✗
            //
            // ── 为什么只对**玩家**取整 ──
            // ★ v194 更新：这一条**只保留了"曲线值 → 血点"的换算**，取整本身已经下移到
            //   `HealthPool.ApplyDamageSplit`（三条伤害来源的唯一落点，见那里的注释）★
            // 这里仍然必须留一个"玩家专属"的分支，因为**量纲不同**：
            //   打玩家传进来的是 `enemies.json` 的**曲线值**（第 1 分钟基础兵 4.55），
            //   要过 `PlayerDamagePoints` 换成血点（46）才能进池；
            //   打敌人传进来的是**武器算完的伤害**（锄头 40、肥料 125），量纲已经是伤害本身 ✓
            // ★★ 注意 v194 的"双算"是**用户裁决过的**：曲线值已经 ×10（base 0.7 → 7.0），
            //   而 `hpUnit` 仍是 10 ⇒ 第 1 分钟基础兵 46 血点（改前 5）。这是设计决定，不是 bug ✓
            //   详见 `tuning.json` 的 `_note_v194_unit` 与 `EnemyDef.PlayerDamagePoints` 的注释 ✓
            // ════════════════════════════════════════════════════════════════════
            float amount = target is PlayerActor ? EnemyDef.PlayerDamagePoints(req.RawValue) : req.RawValue;

            // ════════════════════════════════════════════════════════════════════
            // ★★ v197：**护佑灯的格挡**（道具 7："获得盾牌能挡一次伤害，触发后 30 秒 CD"）★★
            //   位置必须在 `ApplyDamageSplit` **之前** —— 挡掉的意思是"这一下没有落到我身上"，
            //   而不是"落了之后再补回来"（后者会照成盾/血的清零、弹字、护盾回复计时全部走一遍）✗
            //   `IsReflect` 与 `IgnoreDodge` 都不是它的条件：格挡挡的是**任何**伤害 ✓
            //   返回的 `DamageResult` 与闪避同形（`Dealt = 0`），所以弹字/击杀判定都自然对齐 ✓
            // ════════════════════════════════════════════════════════════════════
            if (target is PlayerActor blockedPlayer)
            {
                if (ProgMechanics.TryBlockPlayerHit())
                {
                    blockedPlayer.PopupText("格挡", PopupKind.Evade);
                    return new DamageResult(req.RawValue, 0f, false, true, false, 0f, 0f);
                }
            }

            float dealt = target.Health.ApplyDamageSplit(amount, false, out float shieldPart, out float hpPart);
            bool killed = target.Health.IsDead;
            // ★ v197「蓄势齿轮 / 蓄力线圈」：**受伤即把两条爬升清零**（用户原话"受伤重置"）
            //   条件用 `hpPart > 0`（真的掉血）而不是 `dealt > 0`：被护盾吸收的那一下
            //   对玩家来说"没挨到"，不该把爬升清零 —— 那是护盾本来该做的事 ✓
            if (target is PlayerActor && hpPart > 0f) ProgMechanics.OnPlayerDamaged();
            // ★ v186：**先把结果装好，再往下传**（需求③："三处同步，以参数形式传"）★
            // 弹字不再拿到"一个 float + 一个 bool"，而是拿到**这一个 `DamageResult`** ✓
            // ⇒ 弹字显示的数**只能是** `Dealt`（它就是扣掉的血盾量），没有第二条路能算出别的数 ✓
            var result = new DamageResult(req.RawValue, dealt, req.Crit && dealt > 0f, false, killed,
                                          shieldPart, hpPart);

            // 受击结算：进无敌帧 + 闪白 + 重置护盾回复计时 + 弹字（弹字用 `result` 里的数）
            if (dealt > 0f) target.OnHitLanded(result, w);

            // 击退（位移由 Actor 自己在 Tick 内衰减，保持确定性）
            if (req.Knockback > 0.001f && req.Source != null && target is EnemyActor)
            {
                Vector2 dir = target.Pos - req.Source.Pos;
                if (dir.sqrMagnitude > 0.0001f)
                    target.KnockbackVel += dir.normalized * req.Knockback;
            }

            // 异常累积（异常本身不造成直接伤害）
            if (req.Ailment != AilmentKind.None && target is EnemyActor enemy)
            {
                enemy.Status.Apply(req.Ailment, req.AilmentValue, w.Tuning.formula,
                    1f);
            }

            // ★ T2：**生命汲取**（植物帮手的攻击 → 玩家回血）整段已删除 ✗ ★
            //   它有两层不可达，任一层都足够：
            //   ① 前置 `req.FromPlant` —— 那个字段已随植物一起删除 ✓
            //   ② ★ 它本来就是死块 ★：`float leech = 0f;` 是**局部常量、全程无写**，
            //      而 `LeechFormula(dealt, 0f, 0f) = max(0, dealt × (0 + 0)) = 0` ✓
            //      ⇒ `if (w2 > 0f)` **恒假** ⇒ 这个块在删之前就从来没执行过 ✓
            //   ⚠️ `DamageResolver.LeechFormula` **保留** —— 它仍被 `SelfCheck` 的公式断言直接检验 ✓
            //      （这正是上一轮审计把它列为"不能清空"的那处；本轮实测证明它是死块 ⇒ 授权删除 ✓）

            // ★ v172：荆棘南瓜反伤（`Special == "Thorns"`）已删除 ✗
            //
            // 它是**双重不可达**的（审计结论，我复核同意 ✓）：
            //   ① 前置 `target is PlantAlly` ⇒ 需要一株植物帮手实体 ✓
            //   ② 再加 `thorn.Def.Special == "Thorns"` ⇒ 而 `seeds.json` 里 **16 条种子的
            //      `special` 全部是 `"None"`** ✓（查证方式：读 seeds.json）
            //   ⇒ 即使有植物，也不存在带 "Thorns" 的品种 ✓
            //
            // ⚠️ 副作用（已核查，不是回归）：`DamageRequest.IsReflect` 的**唯一写者**就是下面
            //    这个分支（`IsReflect = true`）⇒ 删掉之后该字段**永远为 false** ✓
            //    我**保留**该字段与上面两处 `!req.IsReflect` 守卫 ✓ 理由与本文件外
            //    `Actors.cs` 的 `ExplodeOnEnd` 同一个先例：它标记的是"反伤**不该**被无敌帧/闪避拦下"
            //    这条**结算规则**；删守卫等于悄悄改掉规则，将来谁接一个反伤就会踩到
            //    "反伤被闪避"这个 bug ✓ ⇒ 保留规则、删除唯一失效的消费者 ✓

            // ★ T2：`req.FromPlant && req.Source is PlantAlly` 那一支已删除 ✗ ★
            //   `FromPlant` 字段与 `PlantAlly` 类型都不复存在 ⇒ 只剩敌人这一支 ✓
            //   ★ 这正是原来 `else if` 的既有行为（`FromPlant` 为假时走的就是这里）⇒ 无行为改动 ✓
            if (req.Source is EnemyActor e) e.DamageDone += dealt;

            target.OnDamaged(dealt, req.Source, w);
            if (killed) target.OnDeath(w);
            // ★ v184：`Crit` 不再写死 `false` —— 它必须与 `req.Crit`（也就是掷骰的那一次）一致 ★
            // 旧写法 `new DamageResult(dealt, false, false, killed)` 让"结算结果里的暴击"
            // 恒为假，与 `OnHitLanded(.., req.Crit)`（弹字那一侧）分成两套说法 ✗
            // ★ v186：`Crit` / 血盾分量都在这一个对象里 —— 弹字拿的就是它（见上文的 `result`）★
            return result;
        }
    }

    public struct EvadeEvent { public int ActorId; }
    public struct EnemyKilledEvent { public string EnemyId; public Vector2 Pos; public int Gold; public float Exp; }
    public struct LevelUpEvent { public int Level; public int MaxPlots; }
    public struct PhaseChangedEvent { public PhaseState From; public PhaseState To; public int Wave; }

    /// <summary>
    /// ★★ v192：**整点回血（正式机制）** —— 用户需求⑦：★
    /// "现在每分钟整点会触发一次恢复玩家健康状态，疑似回合的机制遗留成为 bug，利用这个 bug 改为机制"。
    ///
    /// ── 它替代的是什么 ──
    /// 计时制下 `Clock.WaveIndex > WaveIndex` **每 60 秒必然成立一次** ⇒
    /// `RunSession.StartWave` 每分钟跑一次，而它末尾的 `ResetPlayerForNewWave` 里有一句
    /// `Heal(MaxHp)` + `RefillShield()` —— 那是**回合制**（"一回合开始补满状态"）的遗物 ✓
    /// 在回合制里它有意义；在计时制里它就退化成"玩家什么都没做、每分钟白回满一次"。
    /// 玩家**要把它转正**：行为保留（数值进 `tuning.player.minuteHealPercent`，默认 1.0 = 回满，
    /// 与改前逐位等价），并把它作为**可预期的机制**广播出来 ✓
    ///
    /// ── 谁发布 / 谁订阅 ──
    ///   发布：`RunSession.ResetPlayerForNewWave`（每分钟一次，与 `Telemetry.Emit("player_reset")` 同处）
    ///   订阅：**表现层**（`Bootstrap/WorldView.cs`）—— 播一次全屏回血特效
    ///         `Bus.Subscribe&lt;MinuteHealEvent&gt;(OnMinuteHeal)`（`Bus` 的签名是 `Action&lt;T&gt;`，`T : struct` ✓）
    /// ⚠️ 逻辑层只用它做广播，**不读它做任何决策** ⇒ 表现层没有订阅它也不会影响玩法 ✓
    /// </summary>
    public struct MinuteHealEvent
    {
        /// <summary>第几分钟（= `WaveIndex`，从 1 起）。</summary>
        public int Wave;
        /// <summary>本次**实际**回的生命（到达 `maxHp` 会被截断 ⇒ 满血时是 0）。</summary>
        public float HealHp;
        /// <summary>本次实际补回的护盾。</summary>
        public float HealShield;
        /// <summary>回完之后的生命 / 上限 —— 表现层可以直接印"100/100"。</summary>
        public float Hp, MaxHp;
        /// <summary>玩家位置（全屏特效不需要，但局部特效/飘字需要）。</summary>
        public Vector2 Pos;
    }

    /// <summary>
    /// 单个敌人身上的异常状态（v9 重做）：
    ///   毒   —— 命中即中毒，每 1 秒结算一次、共 4 次；健康敌人贴身累计 1 秒后被扩散。
    ///   火   —— 命中即灼烧，每 1 秒结算一次、共 4 次；**灼烧中的敌人被非异常攻击命中时爆炸**（范围伤害）。
    ///   冰   —— 命中进入寒冷：此后每次受非异常命中 +25%、每层移速 -20%；满 4 层冰冻 1 秒，随后高额破冰伤害。
    /// </summary>
    public sealed class StatusRuntime
    {
        public float PoisonAmount;
        public int PoisonTicksLeft;
        public float PoisonTimer;
        public float BurnAmount;
        public int BurnTicksLeft;
        public float BurnTimer;
        /// <summary>寒冷层数（0..4）与累计承受的非异常伤害（决定破冰伤害）。</summary>
        public int ChillStacks;
        public float ChillDamage;
        public bool Frozen;
        public float FrozenRemaining;
        /// <summary>贴身接触中毒敌人的累计时间（扩散用）。</summary>
        public float PoisonContactTimer;
        /// <summary>
        /// ★ 阶段 3：**插件减速**（`can_slow`）的移速降低比例（0 = 没减速，0.12 = −12%）★
        ///
        /// 为什么挂在 `StatusRuntime` 上而不是另存一张表：
        /// `SpeedFactor` 是**唯一**读移速的地方（`EnemyActor.MoveStep` 两处都走它）✓
        /// 插件减速若自己另算一份乘数，就会出现"两套移速"，而两边迟早分开 ✗
        ///
        /// 谁写它：`PluginDotTable.SyncSlow` —— 每帧按表里 `Slow` 行的层数**全量重算** ✓
        /// （不是"施法时改一次"：那样行到期/目标死亡时没人把它还原 ✓）
        /// 与既有的寒冷（`ChillStacks`）**各自独立**：寒冷是命中叠的，这里是插件挂的 ✓
        /// </summary>
        public float PluginSlow;
        /// <summary>最近一次毒结算是否把伤害转成了玩家生命（毒系特性保留）。</summary>
        public float TickTimer;

        public bool Poisoned => PoisonTicksLeft > 0;
        public bool Burning => BurnTicksLeft > 0;
        public bool Chilled => ChillStacks > 0 && !Frozen;
        public bool Slowed => Chilled;
        public bool HasAny => Poisoned || Burning || ChillStacks > 0 || Frozen;

        public float SpeedFactor(FormulaDef f)
        {
            if (Frozen) return 0f;
            // 阶段 3：插件减速（`can_slow`）与寒冷**各自乘一档** ⇒ 两条来源互不吞掉对方
            float plugin = Mathf.Clamp01(1f - Mathf.Clamp01(PluginSlow));
            if (ChillStacks <= 0) return plugin;
            float slow = Mathf.Clamp01(f.chillSlowPerStack * ChillStacks);
            return Mathf.Max(0.1f, 1f - slow) * plugin;
        }

        /// <summary>异常命中：进入对应状态（毒/火给 4 次结算，冰给一层寒冷）。</summary>
        public void Apply(AilmentKind kind, float value, FormulaDef f, float ailmentSpeed)
        {
            if (value <= 0f) return;
            int ticks = Mathf.Max(1, Mathf.RoundToInt(f.ailmentTickCount));
            switch (kind)
            {
                case AilmentKind.Poison:
                    PoisonAmount = Mathf.Max(PoisonAmount, 0f) + value;
                    if (PoisonTicksLeft <= 0) PoisonTimer = Mathf.Max(0.05f, f.tickInterval);
                    PoisonTicksLeft = Mathf.Max(PoisonTicksLeft, ticks);
                    break;
                case AilmentKind.Burn:
                    BurnAmount = Mathf.Max(BurnAmount, 0f) + value;
                    if (BurnTicksLeft <= 0) BurnTimer = Mathf.Max(0.05f, f.tickInterval);
                    BurnTicksLeft = Mathf.Max(BurnTicksLeft, ticks);
                    break;
                case AilmentKind.Freeze:
                    AddChill(1, 0f, f);
                    break;
            }
        }

        /// <summary>非异常命中：给寒冷 +1 层，并累计用于破冰的伤害。</summary>
        public void AddChill(int stacks, float damage, FormulaDef f)
        {
            if (Frozen) return;
            int max = Mathf.Max(1, Mathf.RoundToInt(1f / Mathf.Max(0.05f, f.chillPerHit)));
            ChillStacks = Mathf.Clamp(ChillStacks + stacks, 0, max);
            ChillDamage += Mathf.Max(0f, damage);
            if (ChillStacks >= max)
            {
                Frozen = true;
                FrozenRemaining = Mathf.Max(0.1f, f.freezeDuration);
            }
        }

        public void Tick(float dt, EnemyActor owner, World w)
        {
            FormulaDef f = w.Tuning.formula;

            // 毒 / 火：每 1 秒结算一次，各 4 次
            if (PoisonTicksLeft > 0)
            {
                PoisonTimer -= dt;
                if (PoisonTimer <= 0f)
                {
                    PoisonTimer += Mathf.Max(0.05f, f.tickInterval);
                    PoisonTicksLeft--;
                    float dealt = owner.Health.ApplyDamage(f.poisonTickFactor * PoisonAmount);
                    // ★ v192：**回血量要换到玩家血池的单位上** ★
                    // `dealt` 是"敌人掉的血"（敌人 HP 域，没放大）；玩家血池整体 ×hpUnit
                    // ⇒ 这里不乘 `PlayerHpUnit` 的话，"毒系回血 = 毒伤 ×30%" 会静默退化成 1/10 ✗
                    if (w.Player != null && !w.Player.Health.IsDead && dealt > 0f)
                        w.Player.Health.Heal(dealt * f.poisonLeechRatio * EnemyDef.PlayerHpUnit);
                    if (PoisonTicksLeft <= 0) PoisonAmount = 0f;
                    if (owner.Health.IsDead) { owner.OnDeath(w); return; }
                }
            }
            if (BurnTicksLeft > 0)
            {
                BurnTimer -= dt;
                if (BurnTimer <= 0f)
                {
                    BurnTimer += Mathf.Max(0.05f, f.tickInterval);
                    BurnTicksLeft--;
                    owner.Health.ApplyDamage(f.burnTickFactor * BurnAmount);
                    if (BurnTicksLeft <= 0) BurnAmount = 0f;
                    if (owner.Health.IsDead) { owner.OnDeath(w); return; }
                }
            }

            // 毒扩散：健康敌人贴身接触中毒敌人累计 poisonContactSeconds 秒后被感染
            if (!Poisoned && f.poisonContactSeconds > 0f)
            {
                float source = NearestPoisonAmount(w, owner, f.poisonContactRadius);
                if (source > 0.01f)
                {
                    PoisonContactTimer += dt;
                    if (PoisonContactTimer >= f.poisonContactSeconds)
                    {
                        PoisonContactTimer = 0f;
                        Apply(AilmentKind.Poison, source * Mathf.Max(0f, f.poisonSpreadRatio), f, 1f);
                        Bus.Publish(new PoisonSpreadEvent { From = owner.Pos, To = owner.Pos, Amount = source });
                    }
                }
                else PoisonContactTimer = 0f;
            }

            // 冰冻：1 秒后破冰，造成高额伤害
            if (Frozen)
            {
                FrozenRemaining -= dt;
                if (FrozenRemaining <= 0f)
                {
                    Frozen = false;
                    float breakDamage = f.freezeBreakFactor * ChillDamage;
                    ChillStacks = 0;
                    ChillDamage = 0f;
                    if (breakDamage > 0f)
                    {
                        owner.Health.ApplyDamage(breakDamage);
                        Bus.Publish(new FreezeBreakEvent { Pos = owner.Pos, Damage = breakDamage });
                        if (owner.Health.IsDead) { owner.OnDeath(w); return; }
                    }
                }
            }
        }

        /// <summary>热点：找贴身范围内中毒最深的敌人的毒量。</summary>
        private static float NearestPoisonAmount(World w, EnemyActor self, float radius)
        {
            float best = 0f;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == self || e.IsDead || !e.Status.Poisoned) continue;
                if (Vector2.Distance(e.Pos, self.Pos) > radius) continue;
                best = Mathf.Max(best, e.Status.PoisonAmount);
            }
            return best;
        }
    }
    public struct FreezeEvent { public Vector2 Pos; }
    public struct FreezeBreakEvent { public Vector2 Pos; public float Damage; }
    public struct PoisonSpreadEvent { public Vector2 From; public Vector2 To; public float Amount; }
    public struct BurnExplodeEvent { public Vector2 Pos; public float Damage; }

    /// <summary>
    /// ★ v185：一发**会飞的**锄头剑气（表现层订阅，`WorldView.OnSwordWave`）★
    ///
    /// ── 为什么不能继续借用 `BeamEvent` ──
    /// 旧版剑气是一瞬间铺满整条走廊，于是它**借了水壶激光的 `BeamEvent`**：
    /// `From = 玩家`、`To = 走廊末端`、`HalfWidth = 近战框半宽` —— 表现层画一根 0.12 秒的柱体，
    /// 与"瞬时判定"在语义上恰好对得上（都是"这一刻，这一整段"）✓
    /// 而现在是**子弹**：走廊在第 0 帧只是"一小格"，之后逐帧往前挪 ✓
    /// 若还发 `BeamEvent`，就会出现"判定已经判完了、光柱还铺满全程"的假同步 ✗
    /// ⇒ 一发剑气 = 一条事件：只说"从哪儿、往哪边、飞多远、框多大"，**飞行由表现层自己走**，
    ///   而速度与距离与逻辑层是**同一对常数**（`RunSession.SwordWaveSpeed` / `SwordWaveDistance`）✓
    ///
    /// ── 字段为什么要带 `HalfLen` / `HalfWidth` ──
    /// 弯月的尺寸必须**等于判定框**，不能靠表现层"看着像"。
    /// 两个数由逻辑层从 `MeleeBoxHalfWidth(range, arc)` 和 `range / 2` 算好传进来 ⇒
    /// 框一变宽，弯月同一帧跟着变宽（"看到的 = 打得到的"）✓
    /// </summary>
    public struct SwordWaveEvent
    {
        /// <summary>**出生时的框心**（框的后缘压在玩家脚下 ⇒ 框心 = 脚下 + 朝向 × 半长）。</summary>
        public Vector2 Origin;
        /// <summary>单位朝向。</summary>
        public Vector2 Dir;
        /// <summary>判定框的**前向半长**（米）= 攻击范围 / 2。</summary>
        public float HalfLen;
        /// <summary>判定框的**横向半宽**（米）= `MeleeBoxHalfWidth(range, arc)`。</summary>
        public float HalfWidth;
        /// <summary>总飞行距离（米）= 攻击范围 × 系数。</summary>
        public float Dist;
        /// <summary>飞行速度（米/秒）。寿命由 `Dist / Speed` 派生。</summary>
        public float Speed;
        /// <summary>插件外观（与挥击弧同一路 ⇒ 装霜锋后弯月也是冰蓝的）。</summary>
        public FxSpec Fx;
    }
}
