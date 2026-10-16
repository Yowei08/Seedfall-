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
        {
            if (amount <= 0f || IsDead) return 0f;
            float absorbed = 0f;
            if (!bypassShield && Shield > 0f)
            {
                float s = Mathf.Min(Shield, amount);
                Shield -= s;
                amount -= s;
                absorbed += s;
            }
            if (amount > 0f)
            {
                float h = Mathf.Min(Hp, amount);
                Hp -= h;
                absorbed += h;
            }
            if (Hp < 0f) Hp = 0f;
            return absorbed;
        }

        public void Heal(float amount)
        {
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
    }

    public readonly struct DamageResult
    {
        public readonly float Dealt;
        public readonly bool Crit;
        public readonly bool Dodged;
        public readonly bool Killed;

        public DamageResult(float dealt, bool crit, bool dodged, bool killed)
        {
            Dealt = dealt; Crit = crit; Dodged = dodged; Killed = killed;
        }
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
            float v = (baseValue + damageStat) * atkMult;
            if (crit) v *= critDmg;
            return Mathf.Max(minDamage, v);
        }

        /// <summary>
        /// 爆炸伤害 = **普通攻击公式的结果** × 爆炸倍率（需求 v109）。
        ///
        /// 旧版是一条**独立**的公式 `(爆炸伤害 + 攻击伤害) × 攻击倍率 × 工具倍率`，
        /// 其中"爆炸伤害"是个绝对值（初始 18）。问题有两个：
        ///   ① 它只吃 `攻击伤害` 这一项，**不吃植物的基础伤害** ——
        ///      于是"给植物堆基础伤害"对爆炸毫无帮助，两套成长脱节
        ///   ② 绝对值无法随强度体系缩放：玩家攻击力涨 10 倍，爆炸还是那 18 点
        /// 现在改成**乘在普通公式之后**，爆炸自动继承普通伤害的全部成长 ✓
        /// `tuning.player.explodeDamage` 也随之从绝对值改为**倍率**（初始 100% = 1.0）。
        /// </summary>
        public static float ExplodeFormula(float normalDamage, float explodeMult)
            => Mathf.Max(0f, normalDamage * Mathf.Max(0f, explodeMult));

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
            if (target == null || !target.Alive) return new DamageResult(0f, false, false, false);

            // ★ T2：**幼苗免疫伤害**的守卫已删除 ✗ ★
            //   原来是 `if (target is PlantAlly seedling && seedling.Growing) return 空结果;` ✓
            //   植物已不参战 ⇒ 不存在"生长中的植物被攻击"这件事 ⇒ 守卫没有可命中的目标 ✓
            //   ⚠️ 它不是"某条规则的唯一实现"：生长中的地块是 `FarmPlot`，
            //      而地块**根本不在 `DamageRequest.Target` 的值域里**（只可能是 Actor）✓
            //      ⇒ 删掉不会让"幼苗可以被打了"，因为幼苗已经不是 Actor ✓

            // 受击无敌帧（玩家；不可被"无视闪避"绕过）
            if (!req.IsReflect && target.IsInvulnerable)
                return new DamageResult(0f, false, false, false);

            // 闪避：只有玩家有闪避属性
            if (!req.IsReflect && !req.IgnoreDodge && target is PlayerActor player)
            {
                float dodge = player.Progress.Stats.Get(StatId.Dodge);
                if (w.Rng.Chance(dodge))
                {
                    Bus.Publish(new EvadeEvent { ActorId = target.Id });
                    return new DamageResult(0f, false, true, false);
                }
            }

            // v9：非异常命中命中"灼烧中的敌人"→ 爆炸（范围伤害）
            if (target is EnemyActor burning && burning.Status.Burning && req.Type != DamageType.Ailment && req.RawValue > 0f)
            {
                float blastDamage = req.RawValue * w.Tuning.formula.burnExplodeFactor;
                if (blastDamage > 0f) BurnExplosion(w, burning, blastDamage);
            }
            // v9：非异常命中 → 给"寒冷"叠加一层（每层移速 -20%，满层冰冻 1 秒）
            if (target is EnemyActor chilled && req.Type != DamageType.Ailment && req.RawValue > 0f)
                chilled.Status.AddChill(1, req.RawValue, w.Tuning.formula);

            float dealt = target.Health.ApplyDamage(req.RawValue);
            bool killed = target.Health.IsDead;

            // 受击结算：进无敌帧 + 闪白 + 重置护盾回复计时
            if (dealt > 0f) target.OnHitLanded(dealt, w, req.Crit);

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
            return new DamageResult(dealt, false, false, killed);
        }
    }

    public struct EvadeEvent { public int ActorId; }
    public struct EnemyKilledEvent { public string EnemyId; public Vector2 Pos; public int Gold; public float Exp; }
    public struct LevelUpEvent { public int Level; public int MaxPlots; }
    public struct PhaseChangedEvent { public PhaseState From; public PhaseState To; public int Wave; }

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
            if (ChillStacks <= 0) return 1f;
            float slow = Mathf.Clamp01(f.chillSlowPerStack * ChillStacks);
            return Mathf.Max(0.1f, 1f - slow);
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
                    if (w.Player != null && !w.Player.Health.IsDead && dealt > 0f)
                        w.Player.Health.Heal(dealt * f.poisonLeechRatio);
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
}
