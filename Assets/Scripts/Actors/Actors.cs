using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// v69：浮动文字的类型。
    ///
    /// 为什么要有这个枚举：旧版是**每个发射点各自写颜色和字号**——
    /// 同一个"掉血"在玩家和敌人身上是两处硬编码，改一次要翻五个文件，而且已经开始不一致了。
    /// 现在发射点只声明"这是什么事件"，颜色 / 字号 / 要不要加感叹号
    /// 全部由 <see cref="PopupStyle"/> 一处决定。改规则只改那一处。
    /// </summary>
    public enum PopupKind
    {
        /// <summary>敌人受击掉血 → 白色</summary>
        DamageToEnemy,
        /// <summary>玩家受击掉血 → 红色</summary>
        DamageToPlayer,
        /// <summary>回血 → 绿色</summary>
        Heal,
        /// <summary>植物成熟 → 黄色</summary>
        PlantMature,
        /// <summary>植物缺水 → 红色放大 + 感叹号</summary>
        PlantThirst,
        /// <summary>收获 → 绿色</summary>
        Harvest,
        /// <summary>v164：收割获得金币 → **黄色**</summary>
        GoldGain,
        /// <summary>v164：收割获得经验 → **绿色**</summary>
        ExpGain,
        /// <summary>暴击 → 黄色放大 + 感叹号</summary>
        Crit,
        /// <summary>升级 → 金色</summary>
        LevelUp,
        /// <summary>肥料爆炸拟声词 → 红色粗体大字</summary>
        Bomb,
        /// <summary>其他信息</summary>
        Info
    }

    /// <summary>
    /// v69：浮动文字的统一样式表。
    /// 规则（来自需求）：玩家受击红、敌人受击白、回血绿、成熟黄、
    /// 缺水红放大加叹号、暴击黄放大加叹号。
    /// </summary>
    public static class PopupStyle
    {
        private static readonly Color Red = new Color(1.00f, 0.24f, 0.22f);
        private static readonly Color White = new Color(1.00f, 0.98f, 0.96f);
        private static readonly Color Green = new Color(0.42f, 1.00f, 0.48f);
        private static readonly Color Yellow = new Color(1.00f, 0.88f, 0.22f);
        private static readonly Color Gold = new Color(1.00f, 0.92f, 0.45f);

        public static Color ColorOf(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageToPlayer: return Red;
                case PopupKind.DamageToEnemy: return White;
                case PopupKind.Heal: return Green;
                case PopupKind.Harvest: return Green;
                // v164：需求"金币+ 黄色 / 经验值+ 绿色" ✓
                // 黄色沿用既有的 `Yellow`（与"成熟"同色）✗ 不新建一种黄 ✓
                case PopupKind.GoldGain: return Yellow;
                case PopupKind.ExpGain: return Green;
                case PopupKind.PlantMature: return Yellow;
                case PopupKind.PlantThirst: return Red;
                case PopupKind.Crit: return Yellow;
                case PopupKind.LevelUp: return Gold;
                case PopupKind.Bomb: return Red;
                default: return White;
            }
        }

        public static float ScaleOf(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.PlantThirst: return 1.75f;   // 缺水要显眼：玩家不管它，作物就不长
                case PopupKind.Crit: return 1.90f;
                case PopupKind.Bomb: return 2.75f;   // v91：2.30→2.75（需求"文本放大一点"）
                case PopupKind.LevelUp: return 1.30f;
                case PopupKind.PlantMature: return 1.25f;
                case PopupKind.Harvest: return 1.15f;
                // v164：收益弹字要**比植物名更显眼** ✗
                // 玩家收割时最想知道的是"我赚了多少" ✓ 而不是"我收了哪一株" ✓
                // 所以收益比植物名大一号：1.35 vs 1.15 ✓
                case PopupKind.GoldGain: return 1.35f;
                case PopupKind.ExpGain: return 1.35f;
                default: return 1.00f;
            }
        }

        /// <summary>这些类型要求以感叹号收尾（需求：缺水 / 暴击 / 爆炸拟声）。</summary>
        public static bool WantsBang(PopupKind k)
        {
            return k == PopupKind.PlantThirst || k == PopupKind.Crit;
        }

        /// <summary>把"类型 + 裸文本"整理成最终要画的字符串。</summary>
        public static string TextOf(PopupKind k, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (!WantsBang(k)) return raw;
            return raw.EndsWith("！") || raw.EndsWith("!") ? raw : raw + "！";
        }
    }

    /// <summary>v47：物体旁边的浮动文字（掉血 / 回血 / 成熟 / 缺水 / 收获）。
    /// 逻辑层只管发，表现层（ImGuiHud）把它投影到屏幕上画出来。
    /// v69：颜色与字号不再由发射点决定，改为只传 <see cref="Kind"/>。</summary>
    public struct FloatingTextEvent
    {
        public Vector2 Pos;
        public string Text;
        public PopupKind Kind;
        /// <summary>额外的字号倍率（在 PopupStyle 的基础上再乘）。0 表示用类型默认值。</summary>
        public float Scale;
    }

    /// <summary>所有角色的基类。位置用 (x, z) 平面表达为 Vector2。</summary>
    public abstract class Actor
    {
        /// <summary>
        /// 受击半径的显示补偿系数（v75）。
        ///
        /// 模型为了在俯视相机下看得清被放大了（WorldView.CharScale 1.75 → 2.60），
        /// 但碰撞半径是玩法数据、没跟着动——于是出现"看着打到了、其实没打到"。
        /// 这里按显示放大的**相对倍率**给受击半径补上：2.60 / 1.75 ≈ 1.486。
        ///
        /// ★ 这是玩法数值改动，不是纯表现 ★
        /// 判定框变大 = 玩家更容易打中敌人，**敌人也更容易打中玩家**，双向的。
        /// 要回到原手感就把这个值调回 1。
        /// </summary>
        public const float HitRadiusScale = 1.486f;

        /// <summary>按显示倍率补偿后的受击半径。</summary>
        public static float ScaledRadius(float baseRadius) => baseRadius * HitRadiusScale;

        public int Id;
        public Vector2 Pos;
        public Vector2 KnockbackVel;
        public float Radius = 0.45f;
        public Team Team;
        public readonly HealthPool Health = new HealthPool();

        protected Actor()
        {
            // v47：让生命池知道自己是哪个角色——治疗时才能在他旁边弹出 "+N"
            Health.Owner = this;
        }

        private bool _alive = true;

        /// <summary>
        /// 存活 = 未被显式回收 且 生命 &gt; 0。
        /// 由生命值派生，避免"HP 归零但 Alive 仍为 true"导致的尸体积压。
        /// </summary>
        public bool Alive
        {
            get => _alive && Health.Hp > 0f;
            set => _alive = value;
        }

        public abstract void Tick(float dt, World w);

        /// <summary>死亡判定：所有索敌、伤害、结算都必须用它。</summary>
        public bool IsDead => !Alive;

        /// <summary>受击无敌帧剩余时间（玩家与植物帮手使用；敌人不用）。</summary>
        public float InvulnRemaining;
        /// <summary>受击闪白剩余时间（表现层读取）。</summary>
        public float HitFlash;

        /// <summary>是否享受受击无敌帧。敌人不享受（否则异常跳伤会被吃掉）。</summary>
        public virtual bool UsesInvulnFrames => false;
        public bool IsInvulnerable => InvulnRemaining > 0f;

        /// <summary>受击成功后的统一处理：进无敌帧 + 触发闪白 + 弹出掉血数字。
        /// v87：crit 为真时弹**暴击样式**（黄色 / 放大 / 带叹号），而不是普通白字。</summary>
        public virtual void OnHitLanded(float amount, World w, bool crit = false)
        {
            HitFlash = 0.12f;
            if (UsesInvulnFrames)
            {
                float inv = w.Tuning.player.invulnTime;
                if (inv > 0f) InvulnRemaining = Mathf.Max(InvulnRemaining, inv);
            }
            // v47：敌人 / 植物 / 玩家掉血都在自己旁边弹数字
            if (amount >= 1f)
            {
                Bus.Publish(new FloatingTextEvent
                {
                    Pos = Pos,
                    Text = "-" + Mathf.RoundToInt(amount),
                    // v69：玩家受击红、敌人受击白
                    // v87：暴击统一走 Crit 类型（颜色/字号/叹号由 PopupStyle 一处决定）
                    Kind = crit ? PopupKind.Crit
                         : Team == Team.Player ? PopupKind.DamageToPlayer : PopupKind.DamageToEnemy,
                    // 大伤害额外放大，保留数值反馈的层次（暴击已经在 PopupStyle 里放大了，不再叠）
                    Scale = (crit || amount < 8f) ? 0f : 1.35f
                });
            }
        }

        protected void TickCommonTimers(float dt)
        {
            InvulnRemaining = Mathf.Max(0f, InvulnRemaining - dt);
            HitFlash = Mathf.Max(0f, HitFlash - dt);
        }

        public virtual void OnDamaged(float amount, Actor source, World w) { }
        public virtual void OnDeath(World w) { }
    }

    public sealed class PlayerActor : Actor
    {
        public const int WeaponHoe = 0;
        public const int WeaponCan = 1;
        public const int WeaponFertilizer = 2;

        public PlayerProgress Progress;
        public Vector2 MoveInput;
        public Vector2 AimDir = Vector2.up;

        /// <summary>三把武器各自独立冷却（切换不重置已有冷却）。</summary>
        public readonly float[] WeaponCds = new float[3];
        /// <summary>换武器前摇：&gt;0 时无法攻击。</summary>
        public float SwitchWindup;
        public int WeaponIndex;
        /// <summary>G 键锁定的目标 id（-1 = 未锁定，此时按鼠标方向攻击）。</summary>
        public int LockTargetId = -1;
        /// <summary>距上次受伤的时间（护盾回复判定用）。</summary>
        public float SinceDamaged = 999f;

        public override bool UsesInvulnFrames => true;

        /// <summary>受击结算：进入无敌帧 + 闪白 + 重置护盾回复计时。</summary>
        public override void OnHitLanded(float amount, World w, bool crit = false)
        {
            base.OnHitLanded(amount, w, crit);
            SinceDamaged = 0f;
        }

        public float MoveSpeed => Progress.Stats.Get(StatId.MoveSpeed);
        public float AttackSpeed => Progress.Stats.Get(StatId.AtkSpeed);
        public float RangeMult => Progress.Stats.Get(StatId.AtkRange);
        public bool IsLocked => LockTargetId >= 0;
        public bool CanAttack => SwitchWindup <= 0f && WeaponCds[WeaponIndex] <= 0f;

        /// <summary>
        /// 滚轮切换武器：delta &gt; 0 向后滚（下一个），&lt; 0 向前滚（上一个）。
        /// 这是唯一的切换方式（HUD 右侧面板只是可视化）。
        /// </summary>
        public void CycleWeapon(int delta, World w)
        {
            if (delta == 0) return;
            int count = 3;
            int next = ((WeaponIndex + delta) % count + count) % count;
            SwitchWeapon(next, w);
        }

        public string WeaponName
        {
            get
            {
                switch (WeaponIndex)
                {
                    case WeaponCan: return "水壶";
                    case WeaponFertilizer: return "肥料";
                    default: return "锄头";
                }
            }
        }

        /// <summary>换武器：切换瞬间进入前摇（前摇期间无法攻击）。</summary>
        public void SwitchWeapon(int index, World w, bool force = false)
        {
            int clamped = Mathf.Clamp(index, 0, 2);
            if (!force && clamped == WeaponIndex) return;
            WeaponIndex = clamped;
            SwitchWindup = Mathf.Max(0f, w.Tuning.tools.switchWindup);
        }

        public override void Tick(float dt, World w)
        {
            StatBlock st = Progress.Stats;

            // 属性变化同步到生命池（升级 / 买道具都会走这里）
            float targetMaxHp = st.Get(StatId.MaxHp);
            float targetMaxShield = st.Get(StatId.Shield);
            if (!Mathf.Approximately(Health.MaxHp, targetMaxHp))
            {
                float delta = targetMaxHp - Health.MaxHp;
                Health.MaxHp = targetMaxHp;
                if (delta > 0f) Health.Hp = Mathf.Min(Health.MaxHp, Health.Hp + delta);
                else Health.Hp = Mathf.Min(Health.Hp, Health.MaxHp);
            }
            if (!Mathf.Approximately(Health.MaxShield, targetMaxShield))
            {
                float shieldDelta = targetMaxShield - Health.MaxShield;
                Health.MaxShield = targetMaxShield;
                if (shieldDelta > 0f) Health.Shield = Mathf.Min(Health.MaxShield, Health.Shield + shieldDelta);
                else Health.Shield = Mathf.Min(Health.Shield, Health.MaxShield);
            }

            // v15：恢复"生命回复"——按 regen 随时间自动回血（数值由属性/道具/抽卡控制）
            float regen = 0f;
            if (regen > 0f) Health.Heal(regen * dt);

            // 护盾回复：未受伤时每秒回 max(最大护盾×百分比, 下限)
            PlayerBaseDef cfg = w.Tuning.player;
            SinceDamaged += dt;
            if (Health.MaxShield > 0f && SinceDamaged >= cfg.shieldRegenDelay && Health.Shield < Health.MaxShield)
            {
                float rate = Mathf.Max(Health.MaxShield * cfg.shieldRegenPct, cfg.shieldRegenMin);
                Health.Shield = Mathf.Min(Health.MaxShield, Health.Shield + rate * dt);
            }

            if (MoveInput.sqrMagnitude > 0.0001f)
            {
                Vector2 dir = MoveInput.normalized;
                Pos += dir * MoveSpeed * dt;
            }
            if (KnockbackVel.sqrMagnitude > 0.0001f)
            {
                Pos += KnockbackVel * dt;
                KnockbackVel = Vector2.Lerp(KnockbackVel, Vector2.zero, 1f - Mathf.Exp(-9f * dt));
            }
            Pos = World.ClampToArena(Pos, Radius, w.Tuning.farm.arenaHalf);

            InvulnRemaining = Mathf.Max(0f, InvulnRemaining - dt);
            TickCommonTimers(dt);
            SwitchWindup = Mathf.Max(0f, SwitchWindup - dt);
            for (int i = 0; i < WeaponCds.Length; i++) WeaponCds[i] = Mathf.Max(0f, WeaponCds[i] - dt);
        }
    }

    public sealed class PlantAlly : Actor
    {
        public SeedDef Def;
        public string MutationId;
        /// <summary>种子品质档位（0 白 / 1 绿 / 2 蓝 / 3 紫 / 4 金）。</summary>
        public int Quality;
        public int PlotIndex;
        public Vector2 Home;
        public bool Growing;

        public float FullHp, GrowingHp;
        // ★ v172：`HasMechanic` / `NightBoosted` / `OwnWorld` 已删除 ✗
        //
        // 这三个是"植物参战机制"的**唯一读取口**：旧机制名
        // （`NightBoost` / `HealAura` / `Magnet` / `Taunt` / `SlowField` /
        //   `HealOnKill` / `Thorns` / `Chain` / `DeathExplode`）
        // 全部经 `HasMechanic` 判定 ✓ 它们的分支也全部已删除 ✓
        //
        // ⇒ 删掉读取口 = 这 9 条旧机制在工程里**不再有名字**，
        //   于是"把它们接回来"没有落点：没有 `HasMechanic` 可以调 ✓
        //
        // ⚠️ 机制判定在本工程**只保留一处**：`PlantMechanics.Has(def, name)`
        //    （成长 / 收获 / 缺水三阶段，见 `PlantMechanics.cs`）✓
        //    `Def.Mechanic` 字段本身保留 —— 它是新机制表的载体，与新玩法无关 ✗

        public float AttackValue;     // z（攻击类）

        /// <summary>
        /// v113：植物**自己的**暴击率与暴击伤害（需求 ⑬）。
        /// 生成时算好并存下来：暴击率 = 品种基础 + 玩家属性增量；暴击伤害用品种自带值（缺省 2.0）。
        /// 这两项跟卡面上显示的是**同一对数字**，所以玩家看到的概率就是他实际会遇到的概率。
        /// </summary>
        public float CritRate, CritDmg;
        public float HealValue;       // q（恢复类）
        public float AttackInterval;
        public float RangeM;
        /// <summary>v9 判定范围：比攻击范围大一点；范围内无敌人时向玩家靠拢。</summary>
        /// <summary>v20：跟随角色时保持的最小距离。</summary>
        public float FollowMinDistance = 1.8f;

        public float DetectM;
        public float MoveSpeedM;
        public float KnockbackM;

        public float AttackTimer;
        public float LifeTime;
        public float DamageDone;
        public float HealDone;
        private int _ailmentRotation;

        public bool IsGuard => Def.Support == SupportMode.Guard;
        public bool IsHealer => Def.Support == SupportMode.Heal;
        public bool IsAttacker => !IsGuard && !IsHealer;
        public string Label => Def != null ? Def.Name : "?";

        /// <summary>植物与玩家一样享受受击无敌帧。</summary>
        public override bool UsesInvulnFrames => true;

        public void SetGrown()
        {
            if (!Growing) return;
            Growing = false;
            float delta = FullHp - Health.MaxHp;
            Health.MaxHp = FullHp;
            if (delta > 0f) Health.Hp = Mathf.Min(Health.MaxHp, Health.Hp + delta);
        }

        /// <summary>
        /// 成熟后离开地块（`PlotIndex = -1`）。
        /// ★ v166：注释原来写"变成自由作战的帮手"✗ —— 植物已不参战 ✓
        /// 现在它只服务 `SpawnPlantFromPlot` 的测试夹具；玩法路径上已无调用者 ✓
        /// </summary>
        public void FreeFromPlot()
        {
            PlotIndex = -1;
        }

        public override void Tick(float dt, World w)
        {
            LifeTime += dt;
            TickCommonTimers(dt);
            if (Growing) return; // 生长中：不动、不攻击

            float atkSpeed = Mathf.Max(0.05f, w.Player.Progress.Stats.Get(StatId.AtkSpeed));
            float speedMul = 1f;

            if (IsAttacker)
            {
                // v9：判定范围（DetectM）内找敌人；有敌人 → 靠近敌人；没敌人 → 靠拢玩家
                EnemyActor target = w.NearestEnemyTo(Pos, DetectM);
                if (target != null)
                {
                    float dist = Vector2.Distance(Pos, target.Pos);
                    if (dist > RangeM * 0.85f)
                    {
                        Vector2 dir = (target.Pos - Pos).normalized;
                        Pos += dir * MoveSpeedM * dt;
                        Pos = World.ClampToArena(Pos, Radius, w.Tuning.farm.arenaHalf);
                    }
                }
                else
                {
                    // v20：跟随角色时保持最小距离，不与角色重合
                    Vector2 anchor = w.Player != null && w.Player.Alive ? w.Player.Pos : Home;
                    float toAnchor = Vector2.Distance(Pos, anchor);
                    if (toAnchor > FollowMinDistance)
                        Pos = Vector2.MoveTowards(Pos, anchor, MoveSpeedM * dt);
                }

                // ★ v172：`HealAura`（治疗光环）与 `Magnet`（吸附掉落）两个 `HasMechanic` 分支已删除 ✗
                //   它们的前提是"植物在场上并跟着玩家" ⇒ 随"植物不参战"一起不可达 ✓

                AttackTimer -= dt * atkSpeed * speedMul;
                if (AttackTimer <= 0f && target != null)
                {
                    AttackTimer = Mathf.Max(0.1f, AttackInterval);
                    Fire(target, w);
                }
            }
            else if (IsHealer || Def.Special == "GuardHeal")
            {
                // ★ v172：同上 —— 治疗光环 / 吸附掉落两个分支已删除（不可达）✗

                AttackTimer -= dt * atkSpeed * speedMul;
                if (AttackTimer <= 0f)
                {
                    AttackTimer = Mathf.Max(0.3f, AttackInterval);
                    Heal(w, Def.Special == "HealAll");
                }
            }
        }

        private float DamageMultiplier(World w)
        {
            float m = 1f;   // v31：变异词条已删除
            return m;
        }

        private void Fire(EnemyActor target, World w)
        {
            float dmg = DamageMultiplier(w);
            Vector2 dir = (target.Pos - Pos).normalized;

            // v120：**远程攻击加细微角度散布**（需求）。
            // 只对远程 —— 近战是扇形判定，位置本身就有余量，加散布只会让"明明贴着敌人却打空"。
            // 每次攻击独立掷，所以多株齐射时不再叠成一条完美直线。
            if (Def.AttackKind == AttackKind.Ranged)
            {
                float jitt = w.Tuning.formula.aimJitterDeg;
                if (jitt > 0f)
                {
                    float rad = w.Rng.Range(-jitt, jitt) * Mathf.Deg2Rad;
                    float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                    dir = new Vector2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
                }
            }

            // v113：植物暴击 —— **在本次攻击的系数上掷一次**。
            //
            // 只改这一处就够了：下面所有分支（异常子弹 / 异常直击 / 近战 / 远程弹道）
            // 用的都是同一个局部变量 `dmg`，所以暴击自动覆盖全部路径。
            // 逐个分支去加必然会漏掉一条，表现为"某类植物不会暴击"这种怪现象。
            bool plantCrit = false;
            if (CritRate > 0f && w.Rng.Chance(CritRate)) { dmg *= CritDmg; plantCrit = true; }

            if (Def.AttackType == AttackType.Ailment)
            {
                AilmentKind kind = AilmentOf(Def.Special, ref _ailmentRotation);
                if (Def.AttackKind == AttackKind.Ranged)
                {
                    // 状态类子弹现在也有命中直接伤害（按异常值的比例）
                    float hit = AttackValue * dmg * w.Tuning.formula.ailmentHitRatio;
                    Projectile pshot = Projectile.PlantShot(this, Pos, dir, hit, AttackValue * dmg, kind,
                        target, Def.Special, w.Tuning.formula.plantProjectileSpeed);
                    pshot.Crit = plantCrit;   // v114：暴击标记随子弹走
                    w.SpawnProjectile(pshot);
                }
                else
                {
                    ApplyAilmentHit(target, AttackValue * dmg, kind, w, plantCrit);
                }
                return;
            }

            if (Def.AttackKind == AttackKind.Melee)
            {
                // 近战取"范围内最近的 N 个"（Cleave = 3），而不是列表顺序
                int maxHits = Def.Special == "Cleave" ? 3 : 1;
                var alreadyHit = new List<int>(maxHits);
                float kb = KnockbackM * (Def.Special == "HeavyKnockback" ? 1.6f : 1f);
                for (int k = 0; k < maxHits; k++)
                {
                    EnemyActor best = null;
                    float bestD = RangeM * RangeM;
                    for (int i = 0; i < w.Enemies.Count; i++)
                    {
                        EnemyActor e = w.Enemies[i];
                        if (!e.Alive || alreadyHit.Contains(e.Id)) continue;
                        float d = (e.Pos - Pos).sqrMagnitude;
                        if (d <= bestD) { bestD = d; best = e; }
                    }
                    if (best == null) break;
                    alreadyHit.Add(best.Id);
                    w.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical,
                        RawValue = AttackValue * dmg,
                        Source = this,
                        Target = best,
                        FromPlant = true,
                        Knockback = kb,
                        Ailment = AilmentKind.None,
                        // v114：把暴击标记传下去 —— 敌人挨打时会用它决定弹"暴击样式"还是普通白字。
                        // 不给这个标记的话，植物暴击就"伤害变大了但画面上看不出来"。
                        Crit = plantCrit
                    });
                }
            }
            else
            {
                Projectile pshot2 = Projectile.PlantShot(this, Pos, dir, AttackValue * dmg, 0f, AilmentKind.None,
                    target, Def.Special, w.Tuning.formula.plantProjectileSpeed);
                pshot2.Crit = plantCrit;   // v114：暴击标记随子弹走
                w.SpawnProjectile(pshot2);
            }
        }

        private void ApplyAilmentHit(EnemyActor target, float value, AilmentKind kind, World w, bool crit = false)
        {
            w.DealDamage(new DamageRequest
            {
                Type = DamageType.Ailment,
                RawValue = value * w.Tuning.formula.ailmentHitRatio,
                Source = this,
                Target = target,
                FromPlant = true,
                Ailment = kind,
                AilmentValue = value,
                Knockback = KnockbackM * 0.3f,
                Crit = crit        // v114：异常直击同样要能弹暴击样式
            });
        }

        /// <summary>
        /// ★ 植物 `Special` 字符串 → `AilmentKind` 的映射 ★
        ///
        /// ★ v172 查证结论（这条**保留**，不留悬案）★
        ///   ① `AilmentKind` 这个**枚举是活的** ✓ —— 消费者与植物无关：
        ///      · `RunSession.cs:3417` —— 插件线的灼烧（`BurnOnHit` 那条）**直接用枚举成员**
        ///      · `Combat.StatusRuntime.Apply` / `Projectile.Ailment` / `DamageRequest.Ailment`
        ///      · `SelfCheck` 的毒/火断言
        ///      ⇒ 所以枚举成员 `Poison/Burn/Freeze` **一个字节都不能删** ✓
        ///   ② 而**这个字符串→枚举的映射**只服务植物：唯一调用者是下方 `Fire()` ✓
        ///      ⇒ 插件线**不走它**（插件直接给枚举成员，不经过字符串）✓
        ///      ⇒ 也就是说 `Def.Special` 里的 `"Poison"/"Burn"/"Freeze"/"Rotate"` 这几个
        ///        **字符串**只可能来自植物表，而 `seeds.json` 16 条种子的 `special` 全是 `"None"` ✓
        ///
        /// ★ 为什么不删它 ★
        ///   删它必须同时改 `Fire()` 的异常分支（行为性改动），而它**不在**本次审计的删除清单里 ✓
        ///   收益（少 17 行不可达代码）小于"顺手改一条战斗分支"的风险 ✓
        ///   ⇒ 记在这里：**它随"植物参战"整条下线时一起删**（与 5 个 SelfCheck 夹具同一批）✓
        /// </summary>
        private static AilmentKind AilmentOf(string special, ref int rotation)
        {
            switch (special)
            {
                case "Poison": return AilmentKind.Poison;
                case "Burn": return AilmentKind.Burn;
                case "Freeze": return AilmentKind.Freeze;
                case "Rotate":
                {
                    AilmentKind[] cycle = { AilmentKind.Poison, AilmentKind.Burn, AilmentKind.Freeze };
                    AilmentKind k = cycle[rotation % cycle.Length];
                    rotation++;
                    return k;
                }
                default: return AilmentKind.Poison;
            }
        }

        private void Heal(World w, bool allTargets)
        {
            float radius = RangeM;
            PlayerActor player = w.Player;
            if (allTargets)
            {
                float total = 0f;
                for (int i = 0; i < w.Plants.Count; i++)
                {
                    PlantAlly p = w.Plants[i];
                    if (!p.Alive || p == this || p.Growing) continue;
                    if (Vector2.Distance(p.Pos, Pos) > radius) continue;
                    p.Health.Heal(HealValue);
                    total += HealValue;
                }
                if (player != null && !player.Health.IsDead && Vector2.Distance(player.Pos, Pos) <= radius)
                {
                    player.Health.Heal(HealValue);
                }
                HealDone += total;
                return;
            }

            // 单体：治疗血量比例最低的友军
            HealthPool best = null;
            float bestPct = 1.01f;
            for (int i = 0; i < w.Plants.Count; i++)
            {
                PlantAlly p = w.Plants[i];
                if (!p.Alive || p == this || p.Growing) continue;
                if (Vector2.Distance(p.Pos, Pos) > radius) continue;
                if (p.Health.HpPct < bestPct) { bestPct = p.Health.HpPct; best = p.Health; }
            }
            if (player != null && !player.Health.IsDead && Vector2.Distance(player.Pos, Pos) <= radius
                && player.Health.HpPct < bestPct)
            {
                bestPct = player.Health.HpPct;
                best = player.Health;
            }
            if (best == null) { best = Health; bestPct = Health.HpPct; }
            best.Heal(HealValue);
            HealDone += HealValue;
        }

        public override void OnDeath(World w)
        {
            if (MutationId == "volatile")
            {
                float dmg = AttackValue * 2f;
                for (int i = 0; i < w.Enemies.Count; i++)
                {
                    EnemyActor e = w.Enemies[i];
                    if (e.Alive && Vector2.Distance(e.Pos, Pos) <= 2.5f)
                    {
                        w.DealDamage(new DamageRequest
                        {
                            Type = DamageType.Magic, RawValue = dmg, Source = this, Target = e,
                            FromPlant = true, IgnoreDodge = true
                        });
                    }
                }
            }
            Bus.Publish(new PlantDiedEvent { SeedId = Def.Id, PlotIndex = PlotIndex, Lifetime = LifeTime });
        }
    }

    public sealed class Projectile
    {
        public Vector2 Pos, Vel;
        public float Damage;
        /// <summary>
        /// v114：这发子弹**出膛时是否暴击**。
        /// 暴击是"每次攻击"决定的，而子弹飞出去之后才结算伤害 ——
        /// 所以标记必须跟着子弹走，不能在命中时再掷（那样同一发子弹每次结算结果都不同）。
        /// </summary>
        public bool Crit;
        public float AilmentValue;
        public AilmentKind Ailment;
        public float Radius = 0.25f;
        public float Life = 3f;
        public float TravelLimit = 9f;
        /// <summary>v23：飞行结束（到末端）时爆炸（肥料炸弹）。</summary>
        /// <summary>
        /// v114 核查结论：**这个字段全工程从未被赋值**，所以下面的 `Explode(w)` 是**当前不可达**的。
        ///
        /// 我原本打算给它加一个"来源武器"字段，好让弹道爆炸也能读逐武器的基础爆炸范围——
        /// 查证之后发现那个问题**不存在**：没有子弹会爆炸，就没有来源要传。
        /// 差点给一段死代码加了一整套管线。
        ///
        /// 游戏里**真实的爆炸只剩一个来源**（v172 之后）：
        ///   ① 肥料炸弹  —— RunSession.ExplodeAt（按落点直接炸，不是飞行物）
        ///   ② ~~炭花死亡 —— PlantAlly 的 DeathExplode 机制~~ ★ v172 已删除 ✗ ★
        ///      （它随"植物不参战"失去前提 ⇒ `World.DealDamage` 里那条分支已整段移除 ✓）
        /// 仍然吃到 `baseExplodeRange` 的是 ①  ✓
        ///
        /// 保留这个字段而不是删掉：它看起来是为"溅射类子弹"预留的设计位。
        /// 真要启用时，需要同时补上「谁给它赋值」与「来源武器的基础爆炸范围」两件事。
        /// </summary>
        public bool ExplodeOnEnd;
        public float Traveled;
        public Team OwnerTeam;
        public Actor Source;
        public bool FromPlant;
        public int Pierce;
        public int Bounce;
        public string Special;
        public float Knockback;
        public bool Dead;
        public Color Tint = Color.white;

        public static Projectile PlantShot(PlantAlly owner, Vector2 from, Vector2 dir, float damage,
            float ailmentValue, AilmentKind ailment, EnemyActor target, string special, float speed)
        {
            var p = new Projectile
            {
                Pos = from,
                Vel = dir.normalized * (speed > 0f ? speed : 18f),
                Damage = damage,
                AilmentValue = ailmentValue,
                Ailment = ailment,
                Radius = special == "Splash" ? 0.5f : 0.28f,
                OwnerTeam = Team.Player,
                Source = owner,
                FromPlant = true,
                Pierce = special == "Pierce2" ? 2 : 0,
                Bounce = special == "Bounce1" ? 1 : 0,
                Special = special,
                Knockback = owner.KnockbackM * 0.5f,
                TravelLimit = owner.RangeM + 1.5f,
                Tint = GradeColor(owner.Def.Grade)
            };
            if (target != null) p.TravelLimit = Mathf.Max(p.TravelLimit, Vector2.Distance(from, target.Pos) + 1.5f);
            return p;
        }

        public static Projectile EnemyShot(Actor source, Vector2 from, Vector2 dir, float damage, float speed, Color tint)
        {
            return new Projectile
            {
                Pos = from,
                Vel = dir.normalized * speed,
                Damage = damage,
                Radius = 0.3f,
                OwnerTeam = Team.Enemy,
                Source = source,
                FromPlant = false,
                Tint = tint,
                TravelLimit = 22f,
                Life = 4f
            };
        }

        public static Color GradeColor(SeedGrade g)
        {
            switch (g)
            {
                case SeedGrade.D: return new Color(0.75f, 0.78f, 0.80f);
                case SeedGrade.C: return new Color(0.45f, 0.85f, 0.45f);
                case SeedGrade.B: return new Color(0.35f, 0.65f, 1.00f);
                case SeedGrade.A: return new Color(0.85f, 0.55f, 1.00f);
                default: return new Color(1.00f, 0.82f, 0.30f);
            }
        }

        public void Tick(float dt, World w)
        {
            Vector2 step = Vel * dt;
            Pos += step;
            Traveled += step.magnitude;
            Life -= dt;
            if (Life <= 0f || Traveled > TravelLimit)
            {
                if (ExplodeOnEnd) Explode(w);
                Dead = true;
                return;
            }

            if (OwnerTeam == Team.Player) TickPlayerShot(w);
            else TickEnemyShot(w);
        }

        /// <summary>v23：肥料炸弹爆炸。v45：伤害 =（爆炸伤害 + 攻击伤害）× 攻击倍率，半径受爆炸范围影响。</summary>
        private void Explode(World w)
        {
            if (!ExplodeOnEnd) return;
            StatBlock st = w.Player != null ? w.Player.Progress.Stats : null;
            // v109：爆炸 = 这发子弹**自己的普通伤害** × 爆炸倍率。
            // 子弹的 Damage 字段在发射时就已按普通攻击公式算好了，
            // 所以这里直接乘即可 —— 爆炸自动继承普通伤害的全部成长。
            float dmg = st != null
                ? DamageResolver.ExplodeFormula(Damage, st.Get(StatId.ExplodeDamage))
                : 0f;
            // v111：基础半径走统一入口（缺省 3 米）。
            // 这里暂时拿不到"发射它的那件武器"的定义——子弹没存来源 Def。
            // 要用逐武器的基础爆炸范围，需要给 Projectile 加一个来源字段（本次未做）。
            float radius = RunSession.BlastBaseRadius(0) * (st != null ? st.Get(StatId.ExplodeRange) : 1f);
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead) continue;
                if (Vector2.Distance(e.Pos, Pos) > radius) continue;
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Ailment, RawValue = dmg, Source = Source, Target = e, Knockback = Knockback
                });
            }
            Bus.Publish(new EnemyAttackFieldEvent { Pos = Pos, Radius = radius });
            w.MatureAndHarvestInRadius(Pos, radius);   // v45/v47：催熟 + 收割直接作用于本世界（不再靠全局事件广播）
            Bus.Publish(new FertilizerBlastEvent { Pos = Pos, Radius = radius });   // 仅表现层：WorldView 画爆炸环
        }

        private void TickPlayerShot(World w)
        {
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (!e.Alive) continue;
                if (Vector2.Distance(e.Pos, Pos) > e.Radius + Radius) continue;

                if (Special == "Splash")
                {
                    for (int j = 0; j < w.Enemies.Count; j++)
                    {
                        EnemyActor other = w.Enemies[j];
                        if (!other.Alive) continue;
                        if (Vector2.Distance(other.Pos, e.Pos) > 1.8f) continue;
                        Hit(other, w);
                    }
                }
                else
                {
                    Hit(e, w);
                }

                if (Bounce > 0)
                {
                    Bounce--;
                    EnemyActor next = w.NearestEnemyToExcept(Pos, 6f, e);
                    if (next != null)
                    {
                        Damage *= 0.7f;
                        if (AilmentValue > 0f) AilmentValue *= 0.7f;
                        Vector2 dir = (next.Pos - Pos).normalized;
                        Vel = dir * Vel.magnitude;
                        Traveled = 0f;
                        TravelLimit = Mathf.Max(3f, Vector2.Distance(Pos, next.Pos) + 2f);
                        return;
                    }
                }
                if (Pierce > 0) { Pierce--; return; }
                Dead = true;
                return;
            }
        }

        private void Hit(EnemyActor e, World w)
        {
            w.DealDamage(new DamageRequest
            {
                Type = Ailment == AilmentKind.None ? DamageType.Magic : DamageType.Ailment,
                RawValue = Damage,
                Source = Source,
                Target = e,
                FromPlant = FromPlant,
                Knockback = Knockback,
                Ailment = Ailment,
                AilmentValue = AilmentValue,
                Crit = Crit     // v114：出膛时定下的暴击，命中时照用
            });
        }

        private void TickEnemyShot(World w)
        {
            PlayerActor player = w.Player;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, Pos) <= player.Radius + Radius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = Damage, Source = Source, Target = player
                });
                Dead = true;
                return;
            }
            for (int i = 0; i < w.Plants.Count; i++)
            {
                PlantAlly p = w.Plants[i];
                // 幼苗不吃子弹（生长中完全免疫）
                if (!p.Alive || p.Growing) continue;
                if (Vector2.Distance(p.Pos, Pos) <= p.Radius + Radius)
                {
                    w.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical, RawValue = Damage, Source = Source, Target = p
                    });
                    Dead = true;
                    return;
                }
            }
        }
    }

    public sealed class Pickup
    {
        public Vector2 Pos;
        public int Gold;
        public float Exp;
        /// <summary>v9：爱心掉落物——拾取后回血（不是金币/经验）。</summary>
        /// <summary>v23：雨滴（拾取给水壶 +20 弹药）。</summary>
        public bool IsRain;
        public bool IsHeart;
        /// <summary>v32：经验球——击杀敌人掉落，拾取同时获得金币与经验。</summary>
        public bool IsExpOrb;
        public float Heal;
        public bool Dead;
        public float Life;

        public void Tick(float dt, World w)
        {
            Life += dt;
            PlayerActor player = w.Player;
            if (player == null || !player.Alive) return;

            float dist = Vector2.Distance(player.Pos, Pos);
            if (dist <= w.Tuning.waves.pickupRadius)
            {
                if (IsRain)
                {
                    // v23：雨滴 → 水壶 +20 弹药
                    Bus.Publish(new RainPickupEvent { Ammo = 20 });
                }
                else if (IsHeart)
                {
                    player.Health.Heal(Heal);
                    Bus.Publish(new HeartPickupEvent { Heal = Heal });
                }
                else
                {
                    // v32：经验球 → 同时获得金币与经验
                    // ★ v167：**这个分支现在不可达** ★
                    //  `SpawnPickup` 已删除 ⇒ 没有任何地方再创建 `IsExpOrb` 的拾取物 ✓
                    //  （雨滴与爱心各自在上一层 if 里 return，走不到这儿）✓
                    //  保留它是为了不改动两个本次不能碰的文件：
                    //    · `Pickup.IsExpOrb` 被 `SelfCheck.cs` 的旧断言读取 ✓
                    //    · `ExpOrbPickupEvent` / `PickupEvent` 的订阅在 `RunSession.cs` ✓
                    w.AddGold(Gold);
                    w.AddExp(Exp);
                    Bus.Publish(new ExpOrbPickupEvent { Gold = Gold, Exp = Exp });
                    Bus.Publish(new PickupEvent { Gold = Gold, Exp = Exp });
                }
                Dead = true;
                return;
            }
            if (dist <= w.Tuning.waves.dropAttractRadius)
            {
                Vector2 dir = (player.Pos - Pos).normalized;
                Pos += dir * (6f * dt);
            }
        }
    }

    public struct PickupEvent { public int Gold; public float Exp; }
    public struct HeartPickupEvent { public float Heal; }
    // v166：`HealOrbEvent` 已删除（光球链是死机制，只发不收）
    /// <summary>v23：雨滴落地。</summary>
    public struct RainDropEvent { public Vector2 Pos; }
    /// <summary>v37：肥料炸弹爆炸（用于催熟范围内的土地）。</summary>
    public struct FertilizerBlastEvent { public Vector2 Pos; public float Radius; }

    /// <summary>
    /// v91：肥料爆炸的**前摇预告**。爆炸不再是"按下即炸"，而是先亮 0.5 秒的区域标记再炸。
    /// 为什么要这个：肥料是范围伤害 + 催熟收割，按下就炸的话玩家来不及看清覆盖了哪些地。
    /// 前摇给了"这一下能炸到几块田"的判断时间。
    /// 与 FertilizerBlastEvent 分开：一个是"要炸了"，一个是"炸了"，表现层要画得完全不同。
    /// </summary>
    public struct FertilizerTelegraphEvent { public Vector2 Pos; public float Radius; public float Duration; }
    /// <summary>
    /// v32：拾取经验球（金币 + 经验）。
    /// ★ v167：**死事件** ✗ —— 敌人已不再掉经验球，"敌人只掉水"⇒ 它永远不会被发布 ✓
    ///   留着是因为 `RunSession.cs` 仍在 Subscribe/Unsubscribe 它（那个文件本次不能改）✓
    ///   清理时机：与 RunSession 一起做，不要单独删这里 ✓
    /// </summary>
    public struct ExpOrbPickupEvent { public int Gold; public float Exp; }
    /// <summary>
    /// v34：经验球掉落（遥测用）。
    /// ★ v167：**死事件** ✗ —— 唯一的发布点 `World.SpawnPickup()` 已删除 ✓
    ///   理由同上：`RunSession.OnExpOrbDropped` 还挂着它 ✓
    /// </summary>
    public struct ExpOrbDroppedEvent { public Vector2 Pos; public int Gold; public float Exp; }
    /// <summary>v23：水壶激光（表现层画一条线）。</summary>
    public struct BeamEvent { public Vector2 From; public Vector2 To; public float HalfWidth; }
    /// <summary>v23：雨滴被拾取（给水壶 +20 弹药）。</summary>
    public struct RainPickupEvent { public int Ammo; }
    // ★ v172：`PlantHealedEvent` 已删除 ✗ —— 它唯一的发布点是 `HealAura` 机制（已删）✓
    //   与 `ExpOrbDroppedEvent` 那条不同：那个还有 `RunSession` 订阅着，所以必须留 ✓
    //   这个**全工程 0 个 `Bus.Subscribe`**（查证方式：搜 `PlantHealedEvent`）⇒ 可以整条删 ✓
    /// <summary>v14：刷怪预警（闪烁 2 秒后真正生成）。</summary>
    public struct SpawnWarningEvent { public Vector2 Pos; public float Seconds; }
    // ★ v166 删除 `TreeProduceEvent` ★ —— 它的唯一发布者 `ProduceFromTree` 已删除，无人订阅 ✓
    // ⚠️ `TreeMatureEvent` **保留**：它是"一格作物成熟"的提示，`World.Simulate` 在每次成熟时发它，
    //    `WorldView.OnPlotMature` 订阅它做落地弹字 ⇒ 与新玩法无关，仍然活着 ✓
    public struct TreeMatureEvent { public int PlotIndex; }

    // v166：`HealOrb` 类已删除（"溢出植物化成治疗光球"—— 植物已不参战/不受伤，不需要治疗）
    public struct PlantDiedEvent { public string SeedId; public int PlotIndex; public float Lifetime; }

    /// <summary>单帧可变状态的唯一聚合点。所有玩法状态都在这里，无隐藏引擎状态。</summary>
    public sealed class World
    {
        public Pcg32 Rng;
        public Pcg32 VisualRng;
        public TuningDef Tuning;
        public FarmGrid Farm;
        public PlayerActor Player;

        public readonly List<PlantAlly> Plants = new List<PlantAlly>();
        public readonly List<EnemyActor> Enemies = new List<EnemyActor>();
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Pickup> Pickups = new List<Pickup>();

        public int WaveIndex = 1;
        public float Time;
        public int NextId = 1;
        public int KillCount;
        public int GoldEarned;
        public float ExpEarned;
        public float DamagedByEnemies;

        private readonly List<Actor> _pendingDeaths = new List<Actor>();

        public World(TuningDef tuning, ulong seed)
        {
            Tuning = tuning;
            Rng = new Pcg32(seed, 1);
            VisualRng = new Pcg32(seed, 0xF00D);
            Farm = new FarmGrid(tuning);
        }

        public static Vector2 ClampToArena(Vector2 p, float radius, float half)
        {
            float lim = Mathf.Max(0.1f, half - radius);
            return new Vector2(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim));
        }

        public int NextActorId() => NextId++;

        /// <summary>
        /// v45/v47：把半径内的"生长中"地块**立即催熟并收割**（肥料爆炸的作用），返回被处理的块数。
        /// 从事件回调搬进 World：Bus 是**跨会话的静态总线**，旧写法由 RunSession 订阅
        /// FertilizerBlastEvent 来做催熟，结果同一进程里的另一局（自检 / 无头模拟 / 机器人）
        /// 也会收到广播并催熟自己世界里的地块。逻辑不该依赖全局广播，事件只留给表现层。
        /// v47：不再只催熟——直接连收割一起做完（地块重新生长），玩家不需要再补一锄头。
        /// ★ v166：注释里的"呼叫植物帮手"已删 —— 收割不再产生帮手，收益是金币与经验 ✓
        /// </summary>
        public int MatureAndHarvestInRadius(Vector2 center, float radius)
        {
            if (Farm == null) return 0;
            int n = 0;
            for (int i = 0; i < Farm.Slots.Length; i++)
            {
                FarmPlot plot = Farm.Slots[i];
                if (plot == null) continue;
                // v91：**已成熟的也要收割**。
                // 旧写法是 `if (plot.State != PlotState.Growing) continue;` —— 只处理"生长中"，
                // 于是已经长熟的地反而被跳过：玩家看着一片成熟的田扔肥料，什么也没发生。
                // 现在两种状态都收，只是"生长中"的需要先催熟一步。
                if (plot.State != PlotState.Growing && plot.State != PlotState.Grown) continue;
                if (Vector2.Distance(plot.World, center) > radius + 1.0f) continue;
                if (plot.State == PlotState.Growing) plot.TickGrowth(999f);   // 未成熟的先催熟
                if (HarvestPlot(plot)) n++;
            }
            return n;
        }

        /// <summary>
        /// 收割一块成熟土地——**发放金币与经验** + 地块重新生长。
        /// 这里是收割逻辑的**唯一实现**（锄头 / 锄头扫掠 / 肥料都走它），RunSession 只负责提示与埋点。
        /// ★ v166：注释里的"召唤植物帮手"已删 —— v165 起收割不再产生战斗植物 ✓
        /// 收益按**格**算（每格 price/5 金币与 price/10 经验）⇒ 一列 N 格就是 N 倍 ✓
        /// </summary>
        public bool HarvestPlot(FarmPlot plot)
        {
            if (plot == null || !plot.IsRipe) return false;
            // ★ v166：收益按**这一格上长的作物**算，而不是"驱动这一列的种子"★
            // 一列里的每一格都是同一株的一份作物（"相当于每个植物多种 1 个"）⇒
            // 每一格独立结算一次 price/5 金币与 price/10 经验 ⇒ **收益随行数线性增长** ✓
            // 若读 plot.SeedId，除列首以外的格子会拿不到 def（按设计它们不登记 SeedId）
            // ⇒ 全部退化成 Mathf.Max(1, ...) 的保底 1 金币 ✗ 那就不是"多种 1 个"了 ✗
            // ★ v168：**作物的唯一真相是地块自己持有的定义**（`plot.CropDef`）★
            //
            // 改动前这里是 `TableDatabase.GetSeed(plot.CropSeedId)` —— 按 id **回查真表** ✗
            // 而 `GetSeed` 对**不存在的 id 抛 `KeyNotFoundException`**（只有**空** id 才返回 null ✓）
            // ⇒ 任何"地块上有定义、但那个 id 不在表里"的情况都会在**收割路径上**抛异常 ✗
            //   而收割是玩家每一帧都在走的路径 ⇒ 撞上就是挥一次锄头崩一次 ✓
            //
            // 这与本会话早先修过的那条是**同一个形状**：
            //   那时是 `plot.SeedId` 只有列首有值 ⇒ `GetSeed(null)` ⇒ ArgumentNullException ✓
            //   修法是"改读 `CropSeedId`"✓ —— 但那只把"读错字段"换成了"读对字段"，
            //   **"收割路径按 id 回查表"这条隐式契约还在** ✗ 于是这次被自造 id 的测试夹具绊倒 ✓
            //
            // 现在把契约去掉：`FarmPlot.CropDef` 就是 `BeginPlant` 时传进来的那份定义 ✓
            //   · 生产里它**就是** `TableDatabase` 里的那个对象 ⇒ `Price` 与旧实现**逐位相同** ✓
            //   · 它每格都有（不是只有列首）⇒ 同列任意一行都能正确结算 ✓
            //   · 它不需要集合查找，也不会因为"表里没这个 id"而改变行为 ✓
            SeedDef crop = plot.CropDef;
            if (crop == null)
            {
                // 不静默：地块没有携带定义只可能来自直接构造 `FarmPlot` 的调试路径 ✓
                // （玩家路径不可能 —— 能收割就说明 `BeginPlant` 跑过了 ✓）
                Log.Error($"收割时地块 {plot.Index} 没有作物定义（CropSeedId={plot.CropSeedId ?? "null"}）⇒ 收益退回保底 1");
            }

            // ★ v165：**删掉"收割即变成战斗植物"** ★
            //
            // 这里原来是：
            //     PlantAlly helper = SpawnPlantFromPlot(plot, def, null, false, 0);
            //     helper.FreeFromPlot();  helper.SetGrown();   // "解放：不再绑定地块 / 立刻参战"
            //
            // 那是**旧玩法的核心** ✗ —— 收割的奖励是"多一个战斗单位" ✓
            // 而新玩法里植物**完全不战斗** ⇒ 这条规则必须消失 ✓
            //
            // ★ 这是审计查出来的残留，而我自己没发现 ★
            // 我只改了"植物停战"的 Tick 门 ✗ 却没注意**收割还在生产新的战斗单位** ✓
            // ⇒ 于是"停战"是假的：植物不再行动，但玩家每收割一次就多一个"参战"的植物 ✓
            //
            // 现在收割的奖励改成**金币与经验**（见下）✓ 这才是新玩法的定义 ✓

            Vector2 at = plot.World;

            // ★ v168：收获阶段的三条机制，全部在这里接线 ★
            //
            // ① `DewDrop` —— 收获**返还蓄水**。必须在 `plot.Harvest` **之前**加：
            //    `Harvest` 里有一句 `if (Water >= WaterMax) NeedFullRefill = false;`
            //    先加后收 ⇒ 凝露足够多时该格会直接进入下一轮生长（这就是它的机制价值）✓
            //    反过来写的话，返还只影响"下一轮浇水的次数"，差别很微弱 ✓
            // ★ 两条来源合并成一次补水 ★
            //   · `PlantMechanics.HarvestDew(crop)` —— **植物**机制 DewDrop（生命之泉）
            //   · `HarvestDew` 字段                 —— **道具**规则 harvest_dew（露水壶），由 RunSession 写入
            // 合并而不是各写一次 `AddWater`：两次调用会**分别**被 WaterMax 钳一次，
            // 于是"植物 12 + 道具 4"在接近满水时可能只加出一部分，而且看不出是谁被截断 ✓
            int dew = PlantMechanics.HarvestDew(crop) + Mathf.Max(0, HarvestDew);
            if (dew > 0) plot.AddWater(dew);

            plot.Harvest(Tuning.farm);   // 地块回到"生长中"

            // ★ v165：**真正发放收益** ★
            //
            // 审计指出：这里只发了两条弹字，**一分钱都没给** ✗
            // ⇒ 弹字在撒谎 ✓ 而"金币与经验只从收割来"这句新玩法的核心定义**完全没接线** ✓
            //
            // 数值来源：由种子的 `price` 推导 ✗ 而不是另开一套字段 ✓
            // 理由：`price` 已经是"这株植物值多少"的既有一切依据 ✓
            // 再开 `goldYield/expYield` 会让"买价"与"收益"两个旋钮打架 ✓
            //
            // ★ v168：乘区补完 —— 等级 × 植物机制 × 道具机制 ★
            //   ① `plot.LevelYieldMult` = ×1.00 / ×1.35 / ×1.70（合成等级）
            //   ② `PlantMechanics.HarvestGoldMult` = ×1.30（丰收）/ ×1.60（缺水时的焦渴绽放）
            //   ③ `HarvestGoldMult` 字段 = 道具规则 `harvest_gold_pct`（由 `RunSession.SyncItemRules` 写入）
            // 三个乘区各自只有一个来源 ✓ 而"1 级 + 无机制 + 无道具"时结果是 `price/5` 原值
            // ⇒ 与 v165 的数值完全一致，改动**不会**把既有平衡顺手改掉 ✓
            float goldMult = (crop != null ? plot.LevelYieldMult : 1f)
                             * PlantMechanics.HarvestGoldMult(crop, plot)
                             * Mathf.Max(0f, HarvestGoldMult);
            float expMult = crop != null ? plot.LevelYieldMult : 1f;
            int goldGain = crop != null ? Mathf.Max(1, Mathf.RoundToInt(crop.Price / 5f * goldMult)) : 1;
            int expGain = crop != null ? Mathf.Max(1, Mathf.RoundToInt(crop.Price / 10f * expMult)) : 1;
            AddGold(goldGain);
            AddExp(expGain);

            Bus.Publish(new FloatingTextEvent
            {
                Pos = at,
                Text = "收获 " + (crop != null ? crop.Name : "植物"),
                Kind = PopupKind.Harvest
            });

            // 需求"植物成熟被收割后，弹字显示金币+、经验值+，分别为黄色和绿色" ✓
            //
            // 为什么要【两条独立弹字】而不是拼成一条 ✗
            // 因为颜色不同 ✓ 而一条弹字只有一个颜色 ✓
            // 拼成"金币+3 经验+5"就只能二选一染色 ⇒ 违背需求 ✓
            //
            // 为什么要【错开位置】✗
            // 三条弹字同点发出会完全重叠 ⇒ 只看得到最后一条 ✓
            Vector2 goldOffset = new Vector2(-0.45f, 0.35f);
            Vector2 expOffset = new Vector2(0.45f, 0.35f);

            Bus.Publish(new FloatingTextEvent
            {
                Pos = at + goldOffset,
                Text = "金币+" + goldGain,     // v165：带上实际数值 ✓ 弹字不该撒谎
                Kind = PopupKind.GoldGain      // 黄色 ✓
            });
            Bus.Publish(new FloatingTextEvent
            {
                Pos = at + expOffset,
                Text = "经验值+" + expGain,
                Kind = PopupKind.ExpGain       // 绿色 ✓
            });

            // ★ v168：收获阶段的第四条机制 —— `ChainHarvest`（圣树守卫）★
            //
            // 语义：收割**一格**成熟作物 ⇒ **同列其它已经成熟的格一并收获** ✓
            // ⇒ 玩家挥一次锄头就把整列收完，收益按格照常结算（每格各自 ×等级 ×机制）✓
            //
            // ★ 递归保护：`_inChainHarvest` ★
            // 被连锁收掉的那一格自己也是 `ChainHarvest`（同列同一株 ⇒ 机制相同），
            // 它收完又会去找同列的成熟格 ⇒ 没有守卫就是**无限递归** ✗
            // 守卫放在 `HarvestPlot` 内部而不是调用方：调用方有三个（锄头扇形 / 肥料半径 / 单点），
            // 靠调用方自觉是拦不住的 ✓（`_chaining` 在伤害连锁上原本就是同一套做法 ——
            // v172：那个 `_chaining` 已随植物连锁闪电一起删除，但这条守卫的理由**与它无关**：✗
            // 守卫保护的是"同列机制自己递归自己"，是本机制的固有属性 ✓）
            if (!_inChainHarvest && PlantMechanics.ChainsHarvest(crop) && Farm != null)
            {
                _inChainHarvest = true;
                try
                {
                    int col = plot.Column;
                    for (int r = 1; r < Farm.Rows; r++)
                    {
                        FarmPlot sib = Farm.Slot(col, r);
                        if (sib == null || sib == plot || !sib.IsRipe) continue;
                        HarvestPlot(sib);       // 递归一层，守卫保证不再往下展开
                    }
                }
                finally
                {
                    _inChainHarvest = false;
                }
            }
            return true;
        }

        /// <summary>
        /// ★ v168：本期**收获金币倍率**（道具规则 `harvest_gold_pct` 的消费点）★
        ///
        /// 由 `RunSession.SyncItemRules()` 写入；没买任何道具时恒为 1 ✓
        /// 与 `HarvestDew` 一样，它是"表里的 rule → 一个已算好的系数 → 结算处读它"这条链的中间一环 ✓
        /// 结算处**不再遍历道具表**：那会是第二份实现，也会让 `World` 反向依赖 `RunSession` ✗
        /// </summary>
        public float HarvestGoldMult = 1f;

        /// <summary>★ v168：每次收获**额外返还的蓄水**（道具规则 `harvest_dew` 的消费点）★</summary>
        public int HarvestDew;

        /// <summary>连锁收获的递归守卫（见 `HarvestPlot` 末尾）。</summary>
        private bool _inChainHarvest;

        // ------------------------------------------------------------ 生成
        public void SpawnEnemy(string enemyId, Vector2 pos, int wave, int bossIndex = 1)
        {
            EnemyDef def = TableDatabase.GetEnemy(enemyId);
            EnemyActor e;
            switch (def.Kind)
            {
                case EnemyKind.Shooter: e = new ShooterEnemy(); break;
                case EnemyKind.Jumper: e = new JumperEnemy(); break;
                case EnemyKind.Boss: e = new BossEnemy(); break;
                case EnemyKind.Wander: e = new WanderEnemy(); break;
                case EnemyKind.Grunt: e = new GruntEnemy(); break;
                // v45：映射必须穷举。历史上这里漏了 Wander，小猪被 default 静默降级成追击玩家的 GruntEnemy。
                default:
                    throw new ArgumentOutOfRangeException(nameof(def.Kind), def.Kind,
                        $"未实现该 kind 的敌人：{def.Id}");
            }
            e.Id = NextActorId();
            e.Def = def;
            e.Team = Team.Enemy;
            e.Pos = World.ClampToArena(pos, def.Radius, Tuning.farm.arenaHalf);
            e.Radius = Actor.ScaledRadius(def.Radius);      // v75：与放大后的模型体量对齐
            e.WaveIndex = wave;
            e.BossIndex = bossIndex;
            e.Health.Setup(def.HpForWave(wave, bossIndex), 0f);
            // v45：小猪在**生成瞬间**取一个随机初始方向（同一 seed 必然同方向），之后只被墙壁反射改写
            if (e is WanderEnemy wander) wander.InitHeading(Rng);
            Enemies.Add(e);
        }

        // ★ T2（2026-10）：`SpawnPlantFromPlot` **整段删除** ✗ ★
        //
        // 它是"植物参战"这条子树**最后一个产出点**：全工程唯一能造出 `PlantAlly` 实体的方法 ✓
        //
        // 删它的判据（不是"少几行"，而是"这条子树不存在"）：
        //   · 调用者：**5 → 0** ✓ —— T2 的 ① 先删掉了 SelfCheck 的 5 个夹具
        //     （旧行号 354 / 508 / 2084 / 3187 / 3188）⇒ 删完立刻成为**零调用者孤儿** ✓
        //   · 删掉之后 `Plants.Add(...)` 再无写者 ⇒ **`World.Plants` 可证明恒空** ✓
        //     ⇒ 树上所有 `Plants` 遍历（敌人跳跃 `Impact` / BOSS 冲锋 / `TickEnemyShot` /
        //       `NearestAlly` / `Heal` / `Cleanup` / `FindPlantOnPlot`）在运行时**全部空转** ✓
        //   · 配合 v173 已删的"植物 tick"闸门（见上方 `Simulate` 第 3 段）
        //     ⇒ "植物索敌 / 移动 / 攻击"这条链**从入口到推进全部不存在** ✓
        //
        // ★ 为什么只删到这里（这是 T2 的边界，不是半途而废）★
        //   `PlantAlly` **类本身** / `World.Plants` 列表 / `Projectile.PlantShot`
        //   三个**不能**跟着删 ✗ —— 因为它们被"敌人打植物"的循环**按类型引用**着：
        //     · `Enemies.cs:273`  `PlantAlly p = w.Plants[i];`（跳跃场 `Impact`）
        //     · `Enemies.cs:329`  同上（BOSS 冲锋）
        //     · `Actors.cs:906`   同上（`Projectile.TickEnemyShot`）
        //     · `Actors.cs:1648`  同上（`World.NearestAlly` —— **活的敌方索敌**）
        //   那几处是**上一轮审计裁决"保留"**的（理由：让"存活 / 死亡"这条链还在）✓
        //   ⇒ 保留它们 ⇒ 类型必须存在 ⇒ 类与列表必须存在 ✓
        //   ⇒ 所以 T2 把它删到"**没有任何东西能造出植物**"为止；再往下就是改那条裁决了 ✗
        //
        // ⚠️ 连带：`PlantSpawnedEvent`（同文件 :1912）**失去唯一发布点** ⇒ 它现在是**死事件** ✓
        //    而 `RunSession.cs:1559 / :1572` 仍在 Subscribe / Unsubscribe 它 ✓
        //    ⇒ 与 `ExpOrbDroppedEvent` 同一个先例：**结构体留着、永不被发布** ✓
        //      （删结构体会让 RunSession 编译不过 = 越界 ✗）

        // ── v10：树的产出源——玩家拥有的所有种子（由 RunSession 同步进来）

        /// <summary>
        /// v23：回合进度 0..1（旧"夜战"机制用）。
        /// ★ v168：`NightBoost` 早已随"植物不参战"一起失效，所以新设计**没有**任何机制依赖它 ——
        /// 与其再造一个"靠回合进度"的机制、赌它真的被写入，不如用确定被写入的 `World.Time` ✓
        ///
        /// ★ v172：它的**最后一个读者**（`PlantAlly.NightBoosted`，夜战植物伤害翻倍）已删除 ✗
        /// ⇒ 现在它**全工程 0 个读者、0 个写者**（查证方式：搜 `WaveProgress01`）✓
        /// ⚠️ 我没有顺手删掉它：它是"**回合**进度"，属于回合组的地界（本轮 `RunSession` 正在动回合）✓
        ///    ⇒ 留给回合组连"回合概念"一起清理 ✓ 在这里写明状态，避免下一个人以为它还活着 ✗
        /// </summary>
        public float WaveProgress01;

        // ★ v172：`AttractBonus`（掉落物吸附半径加成 / 生命之泉的 `Magnet` 机制）已删除 ✗
        //   它是**只写不读**的字段：全工程只有两处写（都在 `HealAura/Magnet` 分支里，已删），
        //   没有任何读者（查证方式：搜 `AttractBonus`）✓
        //   ⇒ 旧机制要复活就得**重新发明一个消费者**，而不是调一个现成的 API ✓

        // ★ v168：`HasWaterSave(plot)` 已删除 ✗ —— 它只认一条机制 `"WaterSave"` ✓
        //    机制从 1 条涨到 9 条之后，判据全部收敛到 `PlantMechanics`（机制名 → 效果 → 消费点）✓
        //    `World` 侧只保留三个问句：`WaterCostScale` / `GrowSpeedMult` / `ThirstyGrowMult` ✓

        public readonly List<SeedDef> OwnedSeeds = new List<SeedDef>();
        public readonly List<int> OwnedQualities = new List<int>();
        public readonly List<string> OwnedMutations = new List<string>();

        /// <summary>玩家拥有的种子变化时由 RunSession 调用（树按这份清单产出）。</summary>
        public void SyncOwnedSeeds(SeedBag bag)
        {
            OwnedSeeds.Clear();
            OwnedQualities.Clear();
            OwnedMutations.Clear();
            for (int i = 0; i < bag.SlotCount; i++)
            {
                SeedSlot slot = bag[i];
                if (slot.IsEmpty) continue;
                if (OwnedSeeds.Contains(slot.Def)) continue;   // 同种只产一株
                OwnedSeeds.Add(slot.Def);
                OwnedQualities.Add(slot.Quality);
                OwnedMutations.Add(slot.MutationId);
            }
        }

        // ★ v166 删除的死代码 `ProduceFromTree` + `SpawnPlantAt` ★
        //
        // 这是 v10"树成熟后按拥有的种子持续产出植物"的整条链 ✓
        // 它全工程**无调用者**：`TreeMatureEvent` 的唯一订阅者是 `WorldView.OnPlotMature`（纯表现），
        // 没有任何地方调用 `ProduceFromTree`；`SpawnPlantAt` 的唯一调用者又是 `ProduceFromTree` ✓
        // 而它的入口 `FarmPlot.BeginTree` 同样无调用者（已在 FarmGrid 删除）⇒ 整条链是死的 ✓
        //
        // 为什么必须删（不只是"清理"）：
        //   它是 **`SpawnPlantFromPlot` 的第二个"生产参战植物"调用点** ✗
        //   与 `RunSession.InteractPlotByAttack` 里刚删掉的那处是同一类残留 ✓
        //   留着它 ⇒ 将来谁复活"树"模型，"植物不战斗"就会再一次变成假的 ✓
        //   这正是本会话反复出现的"共享规则有多个副本、只删了一个" ✓
        //
        // 连带 `TreeProduceEvent`（同文件下方）也失去唯一发布者 ⇒ 一并删除 ✓
        // ⚠️ `SpawnPlantFromPlot` 本身**保留** —— 它的调用者只剩 SelfCheck 的 **5 个**夹具 ✓
        //    （v172 核对的真实行号：SelfCheck.cs **354 / 508 / 2084 / 3187 / 3188** ✓
        //      —— 旧注释写的 331/485/1953/2427/2428 是**过期行号**，已按 grep 更正 ✗）
        //
        // ★ v172 调查结论：那 5 个夹具测的【全部是已删除的机制】★ ✓
        //    · :354  "幼苗免疫伤害 / 成株可被伤害"      ⇒ 植物作为**可被攻击的战斗单位**
        //    · :508  "植物受击进入无敌帧"               ⇒ 同上（玩家那套另有断言覆盖）
        //    · :2084 "植物有判定范围且大于攻击范围"      ⇒ **植物索敌 AI**（DetectM）
        //    · :3187 "稀有度不影响植物生命 / 攻击"       ⇒ 植物**攻击力**这条战斗属性
        //    ⇒ 没有一个是"收割路径"✓ 而它们之所以还是绿的，是因为代码仍能跑（只是玩法上不可达）✓
        //    ⇒ 它们一删，本方法就变成**零调用者的孤儿** ⇒ 那时应连 `PlantAlly` / `World.Plants`
        //       一起下线（要动 RunSession / WorldView / DebugApi，不在本轮范围内）✓
        //    ⇒ 本轮**不删夹具**：只删夹具、留着工厂 = 更糟（孤儿工厂没人测，仍可被接回）✓

        // ★ v166 删除的死代码：**治疗光球整条链** ★
        //
        // 删掉的是：`HealOrbs` 列表 / `SpawnHealOrb` / `NearestAllyForOrb` / `TickHealOrbs`
        //           以及 `HealOrb` 类与 `HealOrbEvent`（结构体在文件下方一并删）
        //
        // 判定依据（两条，任一条都足够）：
        //   ① **`SpawnHealOrb` 全工程 0 个调用者** ⇒ 光球永远不会生成 ⇒ `TickHealOrbs` 在遍历一个空列表 ✓
        //      （`HealOrbEvent` 也**没有任何 `Bus.Subscribe`** —— 只发不收 ✓）
        //   ② 语义已经**不成立**：它的定义是"植物溢出 → 化成治疗光球 → 飞向最近的植物给它回血" ✗
        //      而新玩法里植物**不参战、不被攻击、不死亡** ⇒ **它们根本不需要治疗** ✓
        //      ⇒ 留着它 = 留一条永远跑不到、且语义已废的通路 ✓
        //
        // ⚠️ 与 `World.Plants` 的处置**不同**：`Plants` 本体保留（还有 5 处 SelfCheck 夹具），
        //    而光球链是**独立的死机制**，可以现在就整条删掉 ✓
        // ⚠️ `WavesDef.overflowOrbDelay` 配置字段暂时保留（`Defs.cs`）—— 现在没有读者，
        //    但它属于"波次配置表"，与波次组的地界重叠 ⇒ 留给回合组一起清理 ✓

        public void SpawnProjectile(Projectile p)
        {
            Projectiles.Add(p);
        }

        // ★ v167：`SpawnPickup(pos, gold, exp)` 已删除 ✗
        //
        // 删除理由（**它再无调用点**）✓
        //   · 全工程唯一调用点是敌人死亡处（`Cleanup()`）✓ 已随"敌人只掉水"一起移除 ✓
        //   · 其余位置**只读** `Pickup.IsExpOrb`，没有任何地方再调它 ✓
        //     （查证方式：全 Assets 搜 `SpawnPickup` / `new Pickup` / `IsExpOrb`）
        //   · 留着一个"唯一金币/经验来源"的工厂方法是**反向的**：
        //     谁调它谁就能把金币/经验变回怪物掉落，恰好破坏这次的需求 ✓
        //
        // ★ 但这三个东西**必须留着**（不是我忘了清）★
        //   · `ExpOrbDroppedEvent` / `ExpOrbPickupEvent`
        //       ⇒ `RunSession.cs` 仍在订阅它们（Start/Dispose 各两处）✓
        //         删掉结构体 = 那个文件编译不过 = 本次硬约束禁止改它 ✓
        //         ⇒ 现在的状态是"事件还在，但永远不会被发布"（死事件）✓
        //   · `Pickup.IsExpOrb`
        //       ⇒ 被 `SelfCheck.cs` 的旧断言读取 ✓ 删掉字段同样会让它编译不过 ✓
        //   · `Pickup.Tick` 里发金币/经验的那个 `else` 分支
        //       ⇒ 现在不可达（见该处注释）✓ 保留是为了不改动上面两处 ✓

        /// <summary>v9：爱心掉落物（拾取回血）。</summary>
        /// <summary>v23：从天上随机掉落一个雨滴（拾取 +20 水壶弹药）。</summary>
        private float _rainTimer;

        /// <summary>
        /// v164：**自然降雨已停用** ✗
        ///
        /// 需求："雨滴修改为只有敌人会掉落，**不再自然掉落**" ✓
        ///
        /// ★ 为什么保留这个方法体而不是删掉它 ★
        /// 因为"停用"和"删除"的验证成本不同 ✗
        /// 停用 = 一条 `return` + 明确注释 ⇒ 一眼可查、可回退 ✓
        /// 删除 = 要同时清掉 `_rainTimer` 字段、`SpawnRain` 的调用链、
        ///        以及 `RainDropEvent` 的订阅 —— 而 `SpawnRain` **现在被敌人掉落复用了** ✓
        /// 所以删除是**错的**：那个方法还在用 ✓ 只有"天空落雨"这个**触发源**要停 ✓
        ///
        /// 雨滴现在唯一的来源是 `Cleanup()` 里敌人死亡的那一处 ✓
        /// </summary>
        private void TickRain(float dt)
        {
            return;   // ★ 自然降雨已停用 —— 见上方注释，不是遗漏 ★

            // 以下为旧版的"天空落雨"逻辑，保留以便回退 ✗
            // 若将来要恢复，删掉上面那行 `return;` 即可 ✓
#pragma warning disable CS0162
            if (Player == null || !Player.Alive) return;
            _rainTimer -= dt;
            if (_rainTimer > 0f) return;
            _rainTimer = Rng.Range(1.5f, 3f);   // v25：雨量大幅增加
            float a = Rng.Range(0f, 6.2831853f);
            float r = Rng.Range(2f, 9f);
            Vector2 rainPos = ClampToArena(Player.Pos + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                0.5f, Tuning.farm.arenaHalf);
            SpawnRain(rainPos);
#pragma warning restore CS0162
        }

        /// <summary>
        /// v167：**每个敌人死亡时掉落雨滴的概率** = 0.25。
        ///
        /// ★ 它现在是**水分的唯一来源**（自然降雨已停用，见 `TickRain`）★
        /// 所以这个常数直接决定：水壶能开几枪、几块地能活着 ✓
        ///
        /// ★ 算式（先取证再定值，两步分开算）★
        ///
        /// 第 1 步 · 每波击杀数（实测，不是估的）
        ///   无头回归 `DebugApi.RunRegression`（seed 20240607，Balanced）：
        ///   报告 Docs/reports/sim-20261016-194309.txt ＝ 击杀 21 / 波次 2 ⇒ **≈10.5 只/波** ✓
        ///   生成侧交叉验证：刷怪拍数 ≈ 0.3466×波长（interval 4→2 秒的积分 (D/2)·ln2）✓
        ///   每拍数量 = 2+⌊0.8W⌋+⌊2·progress⌋+⌊3.67√(W−1)⌋，实际被**击杀速率**卡住 ✓
        ///   ⇒ 取 **10~15 只/波**（前期）✓
        ///   ⚠️ 旧注释的"一次普通刷怪约 3~4 只"**不成立**（差约 3 倍）✓
        ///
        /// 第 2 步 · 期望掉落 = 击杀 × p（每具尸体**恰好掷一次**，雨滴无第二个来源）
        ///   0.15 ⇒ 1.58 滴/波 ｜ **0.25 ⇒ 2.63 滴/波** ｜ 0.30 ⇒ 3.15 滴/波
        ///   0.45 ⇒ 4.73 滴/波 ｜ 0.55 ⇒ 5.78 滴/波
        ///
        /// 第 3 步 · 用**需求侧**定值（这才是判据，不是掉落条数）
        ///   供给：每波开局 `min(100, +50)`；每滴 +20 ⇒
        ///     0.15 ⇒ 82 弹药/波 ✗ 低于浇水需求（见下）✓
        ///     0.25 ⇒ 50 + 2.63×20 = **103 ⇒ 撞 100 上限** ✓
        ///     0.30 / 0.45 / 0.55 同样撞上限 ⇒ **它们是同一档**，不是三档 ✓
        ///   支出：地面补水现在也扣弹药（见 `RunSession.WaterPerAmmo`）⇒
        ///     · 2 块地 ≈ 54 弹药/波 ⇒ 一边打一边浇**前期可行** ✓
        ///     · 4 块地 ≈ 108 弹药/波 ⇒ 与供给持平 ⇒ 地全保就打不动枪 ✓
        ///     · 8 块地 ≈ 216 弹药/波 ⇒ 供给的两倍 ⇒ **必须弃地** ✓
        ///   ⇒ 0.25 是"**刚好够把地浇住**"的那一档 ⇒ 取舍成立、又不卡死 ✓
        /// </summary>
        public const float RainDropChancePerKill = 0.25f;

        /// <summary>v23：生成一个雨滴拾取物。</summary>
        public void SpawnRain(Vector2 pos)
        {
            SpawnHeart(pos, 0f);   // 复用拾取物容器，标记为雨滴
            if (Pickups.Count > 0) Pickups[Pickups.Count - 1].IsRain = true;
            Bus.Publish(new RainDropEvent { Pos = pos });
        }
        public void SpawnHeart(Vector2 pos, float heal)
        {
            Pickups.Add(new Pickup { Pos = pos, IsHeart = true, Heal = heal });
        }

        public PlantAlly FindPlantOnPlot(int plotIndex)
        {
            if (plotIndex < 0) return null;
            for (int i = 0; i < Plants.Count; i++)
                if (Plants[i].PlotIndex == plotIndex && Plants[i].Alive) return Plants[i];
            return null;
        }

        // ------------------------------------------------------------ 查询
        public EnemyActor NearestEnemyTo(Vector2 p, float maxRange)
        {
            EnemyActor best = null;
            float bestD = maxRange <= 0f ? float.MaxValue : maxRange * maxRange;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.IsDead) continue;
                float d = (e.Pos - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>按 id 找敌人（G 键锁定用）；找不到返回 null。</summary>
        public EnemyActor FindEnemyById(int id)
        {
            if (id < 0) return null;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.Id == id && !e.IsDead) return e;
            }
            return null;
        }

        public EnemyActor NearestEnemyToExcept(Vector2 p, float maxRange, EnemyActor except)
        {
            EnemyActor best = null;
            float bestD = maxRange * maxRange;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.IsDead || e == except) continue;
                float d = (e.Pos - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ★ v172：`TauntPlantNear(from, radius)` 与 `SlowFieldFactor(pos)` 两个方法**已整条删除** ✗
        //
        // 为什么是"删方法"而不是"只删里面的分支" ✓
        //   两个方法体在删掉 `HasMechanic("Taunt") / ("SlowField")` 判据之后，
        //   剩下的就只是「遍历 `Plants`（恒空）并返回 null / 1f」—— 一个**纯空操作** ✓
        //   ⇒ 按本工程对死机制的判据（"删完只剩遍历空列表"），方法本身就该消失 ✓
        //   ⇒ 它们不是"待用的查询接口"，而是旧机制的两个消费点 ✓
        //
        // ⚠️ 调用者已同步处理：`Enemies.cs` 的 `GruntEnemy` / `ShooterEnemy` 原来先问
        //    "6 格内有没有嘲讽植物"，没有才打玩家 ⇒ 现在**直接打玩家** ✓
        //    （嘲讽恒为 null ⇒ 这条改动**不改变任何既有行为**，只是把死掉的前半段删掉 ✓）
        //
        // ⚠️ `SlowFieldFactor` 的调用者：全工程 0 个（查证方式：搜 `SlowFieldFactor`）✗
        //    它原本挂在敌人移速计算上，而那条线已经先被删了 ⇒ 它是**孤儿** ✓

        public Actor NearestAlly(Vector2 p, bool playerOnly = false)
        {
            Actor best = Player != null && !Player.IsDead ? Player : null;
            float bestD = best == null ? float.MaxValue : (best.Pos - p).sqrMagnitude;
            if (playerOnly) return best;
            for (int i = 0; i < Plants.Count; i++)
            {
                PlantAlly pl = Plants[i];
                // 幼苗不可被攻击：敌人索敌时直接跳过生长中的植物
                if (pl.IsDead || pl.Growing) continue;
                float d = (pl.Pos - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pl; }
            }
            return best;
        }

        // ------------------------------------------------------------ 结算
        public void DealDamage(in DamageRequest req)
        {
            DamageResult r = DamageResolver.Resolve(req, this);
            if (req.Target is PlayerActor && r.Dealt > 0f) DamagedByEnemies += r.Dealt;

            // ★ v172：v23「植物机制统一挂点」整段已删除 ✗ —— 它是本任务里最要紧的一处 ✓
            //
            // 删掉的是四条机制分支（全部以 `HasMechanic` 为判据、全部以 `PlantAlly` 为前置）：
            //   · `HealOnKill`（圣树守卫击杀 → 玩家 +1 生命）—— 连带 `lethal` 局部变量
            //   · `Thorns`（荆棘南瓜受击 → 反弹 50%）
            //   · `Chain`（星藤连锁闪电，弹 3 个敌人）—— 连带 `_chaining` 递归守卫
            //   · `DeathExplode`（炭花阵亡 → 范围内爆炸）
            //
            // ★ 为什么这四条必须删、而不是"留着反正跑不到"★
            //   这里是**结算层**：`DealDamage` 是每一下伤害都必经的主干 ✓
            //   把机制挂在这一层，等于把"植物参战"接在主干上 ✗
            //   ⇒ 只要 `Plants` 里出现**一个**元素，这四条立刻同时生效 ✓
            //   ⇒ 判据不是"它现在跑不跑"，而是"删完之后这条路还在不在" ✓
            //
            // ⚠️ `EnemyAttackFieldEvent` 结构**保留** ✓ —— 跳跃精英的落地冲击还在发它
            //    （`Enemies.cs` 的 `Impact()`），这里只是少了炭花那一个发布点 ✓
        }

        public void AddGold(int baseGold)
        {
            int g = Mathf.RoundToInt(baseGold * Player.Progress.Stats.Get(StatId.GoldMult));
            GoldEarned += g;
            Bus.Publish(new GoldChangedEvent { Delta = g, Total = GoldEarned });
        }

        public void AddExp(float baseExp)
        {
            float e = baseExp * Player.Progress.Stats.Get(StatId.ExpMult);
            ExpEarned += e;
            if (Player.Progress.AddExp(e, out int levels) && levels > 0)
            {
                Farm.SetMaxPlots(Player.Progress.MaxPlots);
                Bus.Publish(new LevelUpEvent { Level = Player.Progress.Level, MaxPlots = Player.Progress.MaxPlots });
            }
        }

        // ------------------------------------------------------------ 单帧推进
        public void Simulate(float dt)
        {
            Time += dt;

            // 0) v166：**田地按存活时间加排**（每 farm.rowGrowthSeconds 一排，封顶 farm.maxRows）。
            //    放在这里而不是 RunSession：`Time` 是本类累加的世界时间，
            //    只在世界真正推进时增长 ⇒ 暂停（商店/菜单）不算"存活"✓
            //    实现是"绝对时间的纯函数"（见 FarmGrid.SyncSurvival）⇒
            //    调用多少次、掉多少帧，结果都一样 ✓
            Farm.SyncSurvival(Time);

            // 1) 耕地：v23 自动生长（缺水停长；成熟后进入"待收获"，需锄头收获）
            for (int i = 0; i < Farm.Slots.Length; i++)
            {
                FarmPlot plot = Farm.Slots[i];
                if (plot.State != PlotState.Growing) continue;
                float speed = Mathf.Max(0.05f, Player != null ? Player.AttackSpeed : 1f);
                // ★ v166：WaterSave（铁根巨像）—— 搬到地块层，并且**实现它的字面语义** ★
                //
                // 作用域：从"全场任一植物 ⇒ 全场受益"改成**只看这一格的作物** ✓
                //   为什么读 `CropDef`：一列里只有列首登记 `SeedId`（1:1 契约），而 `CropDef` 每格都有 ✓
                //   为什么"逐格"等于"逐列"：一列 = 一株植物，同列每格 `CropDef` 相同 ✓
                //
                // ★ 语义修正（这条比作用域更关键）★
                //   旧实现：`TickGrowth2(dt * 2)` ⇒ 生长与漏水**一起** ×2
                //           ⇒ 每秒水耗**翻倍**、长熟总水量不变 ✗ **与名字 WaterSave 方向相反** ✗
                //   新实现：`TickGrowth2(dt, 0.5f)` ⇒ 生长不变、**该列水耗真的减半** ✓
                //   ⇒ 玩家有理由为"省水"而种它 ✓ 它才是一条真实的经济机制 ✓
                //
                // ★ v168：这里成为**成长阶段机制的唯一消费点** ★
                //   四个成长类机制全部在这三行里生效：
                //     `WaterCostScale`  —— WaterSave（漏水时钟 ×0.5）
                //     `GrowSpeedMult`   —— Haste（×1.25）/ ElderGrowth（随存活时间 +4%/分钟，≤+60%）
                //     `NeighborGrowSpeedMult` —— NeighborBoost（左右相邻列 ×1.12）
                //     `ThirstyGrowMult` —— ThirstTolerant（缺水时仍以 40% 速度生长）
                //   一律读 `plot.CropDef`（每格都有）而不是 `plot.SeedId`（只有列首有）✓
                SeedDef crop = plot.CropDef;
                float waterCost = PlantMechanics.WaterCostScale(crop);
                float growSpeed = PlantMechanics.GrowSpeedMult(crop, Time)
                                  * PlantMechanics.NeighborGrowSpeedMult(Farm, plot);
                float thirstGrow = PlantMechanics.ThirstyGrowMult(crop);
                bool ripe = plot.TickGrowth2(dt * speed * growSpeed, waterCost, thirstGrow);
                if (ripe)
                {
                    Bus.Publish(new TreeMatureEvent { PlotIndex = plot.Index });
                    // v47：成熟提示——在土地旁边弹出
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = plot.World, Text = "成熟", Kind = PopupKind.PlantMature
                    });
                }
                // v47：缺水提示每次"从有水变成无水"只弹一次，补水后重置（见 FarmPlot.AddWater）
                else if (plot.Thirsty && !plot.ThirstyNotified)
                {
                    plot.ThirstyNotified = true;
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = plot.World, Text = "缺水", Kind = PopupKind.PlantThirst
                    });
                }
            }

            // 2) 玩家（移动 / 再生 / 冷却衰减 / 生命池与属性同步）
            if (Player != null && Player.Alive) Player.Tick(dt, this);

            // 3) 植物
            // ★ v173：原来这里那段"植物 tick"【已整体删除】✗ —— 连开关都不剩 ✓ ★
            //    v162 曾用一个 `GameplayMode.PlantsFight` 开关把"植物索敌 / 移动 / 攻击"停用 ✓
            //    但 v172 删掉那 9 条机制之后，开关后面**已经没有东西可以开关** ✓
            //    ⇒ v173 把开关与这个闸门**一起删除** ✓
            //    ⇒ "植物参战"不再是一个选项：它**不是被关掉，而是不存在** ✓
            //      （想回退也没有拉杆 ✓ 而机制本身在 v172 已无从重写 ✓）
            //    `Plants` 列表本身保留 —— 收割 / 成长 / 浇水仍在使用（整条下线见待办 T2）✓
            //    植物不索敌、不移动、不攻击 ⇒ 没有状态需要推进 ⇒ 这里不 tick 是正确的 ✓

            // 4) 敌人
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.Alive) e.Tick(dt, this);
            }

            TickRain(dt);
            // v166：`TickHealOrbs(dt)` 已删除 —— 光球链是死机制（`SpawnHealOrb` 无调用者、语义已废），
            //       详见本文件"治疗光球整条链"那段注释 ✓

            // 5) 弹道
            for (int i = 0; i < Projectiles.Count; i++)
            {
                Projectile p = Projectiles[i];
                if (!p.Dead) p.Tick(dt, this);
            }

            // 6) 掉落
            for (int i = 0; i < Pickups.Count; i++)
            {
                Pickup p = Pickups[i];
                if (!p.Dead) p.Tick(dt, this);
            }

            // 7) 清理与死亡结算
            Cleanup();
        }

        private void Cleanup()
        {
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor e = Enemies[i];
                if (!e.IsDead) continue;
                e.Alive = false;
                KillCount++;
                if (!e.NoDrop)
                {
                    // ★ v167：**敌人不再掉经验球** ✗（v39 的 1~3 / 3~5 球整段删除）
                    //
                    // 需求："敌人只掉水" ✓
                    // 判据（新玩法的定义）：植物是金币与经验的**产出形式**
                    //                    ⇒ 金币与经验**只从收割来** ✓
                    // 发放点唯一：`HarvestPlot()` —— 金币 = 种子价/5、经验 = 种子价/10 ✓
                    //
                    // 所以这里**不能**再 SpawnPickup ✗
                    // 那等于给金币/经验开第二条来源 ⇒ 与"收割是唯一产出"直接冲突 ✓
                    //
                    // ⚠️ 副作用（预期，不是回归）：删除同时移除了
                    //    `Rng.RangeInt` ×2 与 `Rng.InsideCircle` ×orbs 的取数 ✓
                    //    ⇒ 同种子的跑局结果会变（RNG 流位置变了）✓
                    //    确定性承诺的是"同代码 + 同种子 ⇒ 同结果"，**不是**跨版本同结果 ✓
                    // v9：按"爱心掉落率"概率掉一颗爱心（拾取回血）
                    // v110：爱心掉落率是**等级**（0~3），要换算成实际概率再掷。
                    // 直接拿等级当概率用会得到"300% 掉落率"这种东西。
                    int heartLv = Mathf.RoundToInt(Player.Progress.Stats.Get(StatId.HeartDropRate));
                    float heartRate = TuningDef.HeartDropRateOf(heartLv);
                    if (heartRate > 0f && Rng.Chance(heartRate))
                        SpawnHeart(e.Pos + Rng.InsideCircle(0.6f), Mathf.Max(1f, Player.Progress.Stats.Get(StatId.HeartHeal)));

                    // ★ v167：**雨滴判定移进 `if (!e.NoDrop)` 里面** ★
                    //
                    // 原来它在那个 if 的**外面** ⇒ 被标 `NoDrop` 的敌人**照样掉雨** ✗
                    // 谁会被标 NoDrop：BOSS 出场清场（`EnemyActor.ClearField` 给满场小怪打标）✓
                    //   清场是**演出性清空**，规则上明确"不给掉落"✓
                    //   而雨滴就是掉落物 ⇒ 它必须一起吃这条规则 ✓
                    // 不改的后果：BOSS 一出场就白送一批水（每只 25%，一波 5~10 只 ≈ +1~2 滴）✗
                    //
                    // 设计意义：雨滴只从敌人身上掉 ⇒ 水分【必须靠打怪】✓
                    //   ⇒ "浇水"与"战斗"被强制绑在一起 ✓ 而水壶的弹药也是水 ⇒
                    //      **打怪 → 拿水 → 要么浇地、要么当弹药** ✓ 这才是核心取舍 ✓
                    // 概率与数量：沿用旧版拾取时的 +20 弹药 ✗ 只把【来源】从天空改成敌人 ✓
                    if (Rng.Chance(RainDropChancePerKill))
                        SpawnRain(e.Pos + Rng.InsideCircle(0.7f));
                }

                Bus.Publish(new EnemyKilledEvent { EnemyId = e.Def.Id, Pos = e.Pos, Gold = e.Def.Gold, Exp = e.Def.Exp });
                Enemies.RemoveAt(i);
            }

            for (int i = Plants.Count - 1; i >= 0; i--)
            {
                PlantAlly p = Plants[i];
                if (!p.IsDead) continue;
                p.Alive = false;
                if (p.PlotIndex >= 0)
                {
                    FarmPlot plot = Farm.Slots[p.PlotIndex];
                    if (plot.State == PlotState.Growing || plot.State == PlotState.Grown) plot.OnPlantDied();
                }
                Plants.RemoveAt(i);
            }

            for (int i = Projectiles.Count - 1; i >= 0; i--)
                if (Projectiles[i].Dead) Projectiles.RemoveAt(i);

            for (int i = Pickups.Count - 1; i >= 0; i--)
                if (Pickups[i].Dead) Pickups.RemoveAt(i);
        }

        /// <summary>
        /// 强制结束波次用：清掉场上残留敌人（不给掉落，避免"挂机刷钱"）。
        /// 返回被清掉的数量。
        /// </summary>
        public int DespawnAllEnemies()
        {
            int n = Enemies.Count;
            for (int i = 0; i < Enemies.Count; i++) Enemies[i].Alive = false;
            Enemies.Clear();
            for (int i = Projectiles.Count - 1; i >= 0; i--)
                if (Projectiles[i].OwnerTeam == Team.Enemy) Projectiles.RemoveAt(i);
            return n;
        }

        // ★ 2026-10：`SettleWaveEnd()` 已删除 ✗
        //
        // 它做三件事（场上掉落吸附入账 / 全体植物回血 30% / 玩家护盾填满），
        // 唯一的调用方是 `RunSession.TickCombat` 里那段"波末结算 → 商店/抽卡"——
        // 而那段在计时制下**永不触发**，2026-10 已整块删除 ✓
        // 于是它变成【0 调用者】：留着的唯一效果就是"看着像还有波末结算" ✗
        //
        // 注：`DespawnAllEnemies()`（上面那个）**仍在用** ✓ —— 分钟硬上限与"分钟走完清残敌"
        // 两条路都会调它，与回合概念无关（它按分钟收尾，不按波次）✓

        // ★ v165：`ResetFarmForNewWave()` 已删除 ✗
        //
        // 它的唯一调用方是 `RunSession.TickCombat` 里那句
        // `if (T.farm.resetOnWaveEnd) World.ResetFarmForNewWave();` ——
        // 也就是"每回合结束把全部植物清空、所有地块刷回未开垦"。
        //
        // 计时制下这就是**每分钟抹掉一次玩家的产出** ✗ 而需求是"植物是产出形式" ✓
        // 调用方与配置字段 `farm.resetOnWaveEnd` 一起删除，所以这里不留一个没人调用的方法。
        // （自检里那两条"回合结束清空植物 / 田地回到未开垦"的旧断言也一并删掉，
        //   换成了反向断言"跨分钟不再清空植物"——见 SelfCheck。）
    }

    public struct PlantSpawnedEvent
    {
        public string SeedId;
        public SeedGrade Grade;
        public string MutationId;
        public int PlotIndex;
    }

    public struct GoldChangedEvent { public int Delta; public int Total; }
}
