using System;
using UnityEngine;

namespace Seedfall
{
    public struct JumpTelegraphEvent { public Vector2 From, To; public float Radius, Duration; }
    public struct BossEvent { public string Kind; public Vector2 Pos; }
    public struct EnemyAttackFieldEvent { public Vector2 Pos; public float Radius; }

    // ══════════════════════════════════════════════════════════════════════
    // ★ v201：**新技能的演出事件**（逻辑 → 表现，单向）★
    //
    // 约束（与既有 `BossEvent` / `JumpTelegraphEvent` 同一条）：
    //   · 只带"表现需要的最小事实"：位置 / 半径 / 时长 / 阶段号
    //   · **表现层绝不回读** —— 逻辑不订阅自己发出的事件（否则就是两套真相）
    //   · 没有事件发送能力的读者（自检、探针）读的是 **与事件同源的公开字段**
    //     （`BossEnemy.RoarRingsLeft` / `MothPhaseIndex` / `MothLaserStage` …）
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>旧 BOSS「吼叫」：第 <see cref="Ring"/> 个圆环波（0 起），从 <see cref="Pos"/> 向外扩散。</summary>
    public struct BossRoarEvent { public Vector2 Pos; public int Ring; public float Radius; }

    /// <summary>旧 BOSS「蓄力爆炸」的蓄力开始：地面预警圈（半径 / 时长给表现层画红圈）。</summary>
    public struct BossBlastChargeEvent { public Vector2 Pos; public float Radius, Duration; }

    /// <summary>旧 BOSS「蓄力爆炸」真正炸开的那一帧。</summary>
    public struct BossBlastEvent { public Vector2 Pos; public float Radius; }

    /// <summary>飞蛾的弹幕（<see cref="Count"/> 发，方向由逻辑算好，表现只做枪口闪光）。</summary>
    public struct MothBarrageEvent { public Vector2 Pos; public int Count; }

    /// <summary>飞蛾冲刺的**预警 → 突进**（<see cref="Duration"/> 是这一段的时长）。</summary>
    public struct MothDashEvent { public Vector2 From, To; public float Duration; public bool Impact; }

    /// <summary>
    /// 蛹的激光：<see cref="Stage"/> 0=预警（圈在落点上）1=落下（光柱）2=灼烧区仍在。
    /// 落点写在 <see cref="Pos"/>，半径 <see cref="Radius"/>。
    /// </summary>
    public struct MothLaserEvent { public Vector2 Pos; public float Radius; public int Stage; }

    /// <summary>飞蛾「消失 / 现身」：<see cref="Hidden"/> true=消失了，false=现身。</summary>
    public struct MothVanishEvent { public Vector2 Pos; public bool Hidden; }

    /// <summary>飞蛾「从天而降的跳砸」预警：从 <see cref="From"/> 落到 <see cref="To"/>。</summary>
    public struct MothSlamEvent { public Vector2 From, To; public float Radius, Duration; }

    /// <summary>飞蛾形态切换（0=毛毛虫 1=蛹 2=飞蛾）。</summary>
    public struct MothFormEvent { public Vector2 Pos; public int Phase; }

    public abstract class EnemyActor : Actor
    {
        public EnemyDef Def;
        public int WaveIndex = 1;
        public int BossIndex = 1;
        public float TouchTimer;
        public float AttackTimer;
        public float DamageDone;
        public bool NoDrop;
        public readonly StatusRuntime Status = new StatusRuntime();

        /// <summary>
        /// ★★ v210：**接触判定体缩放** —— 敌人「撞到玩家」这一层单独乘的系数 ★★
        ///
        /// ── 为什么它和 <see cref="Actor.Radius"/> 分开 ──
        ///   `Radius`（= 表里的 `radius` × `Actor.HitRadiusScale` 1.486）今天同时喂三件事：
        ///     ① 玩家打敌人的**命中距离**（`range + Radius`，见 `RunSession.MeleeJudgeRadius`）
        ///     ② 战场边界钳制（<see cref="MoveStep"/> 的 `ClampToArena`）
        ///     ③ 敌人**撞玩家**的接触判据（本文件的 `reach` / 追击停距 / 撞车停距）
        ///   用户要的是「**别被贴身蹭到**」（体验），不是「敌人模型变小」（表现）⇒ 只压瘦 ③：
        ///     · ① 与 ② 保持按真实体宽走 —— 否则命中距离会跟着变紧、敌人会走进墙里 ✗
        ///
        /// ── 为什么是 0.70 ──
        ///   ① 敌人**判定体**本来就比模型窄（模型尺寸 = `RawHeight × CharScale`，与 `radius` 无关）
        ///      ⇒ 「看着撞上了却没撞」的抱怨来自判定侧，接触层再瘦一档是同一个方向 ✓
        ///   ② 0.70 让「被蹭」只剩**真的压到身上**才掉血；同时追击停距与撞车停距**同乘本系数**
        ///      ⇒ `reach − stop` 恒等于那个 0.06m 常数的关系一位不变（停下来之后仍在接触范围内，
        ///      不会出现「贴住了却不掉血」的新毛病）✓
        ///   ③ 只改这一层、不动 `radius` 的乘数：命中距离只随表里的 `radius` 缩，
        ///      本系数**不会**再额外压命中距离 ✓
        /// </summary>
        public const float ContactScale = 0.70f;

        /// <summary>返回 true 表示处于冻结，本帧不能移动与攻击。</summary>
        protected bool TickStatus(float dt, World w)
        {
            TickCommonTimers(dt);
            Status.Tick(dt, this, w);
            if (!Alive) return true;
            return Status.Frozen;
        }

        public float SpeedFactor(World w) => Status.SpeedFactor(w.Tuning.formula);

        /// <summary>
        /// ★ v201：**不可选中**（目前唯一实现者 = 消失期的飞蛾）★
        ///
        /// 语义：**它现在不在场上** —— 任何伤害都不该落到它身上，任何索敌也不该指向它。
        /// 三个写者/读者各自分工（这条注释是唯一的口径说明，别再各写一份）：
        ///   ① `World.DealDamage`：`BossEnemy.InEntrance` 为 true ⇒ 直接返回 default（`Actors.cs`）
        ///      —— 这是全工程**唯一**的伤害闸门，所以"打不中"是结构性保证，不是逐处补漏 ✓
        ///   ② 消失期**不做任何会位移或接触的判断**（本分支每帧直接 `return`）✓
        ///   ③ 表现层读 `MothHidden` 把模型隐藏（`ProceduralBugs`）✓
        /// ⚠️ 已知限制：`World.NearestEnemyTo` 仍在 `World.Enemies` 里能看到它（那个文件不在本轮
        ///    允许改动的清单里）⇒ 自动瞄准类索敌仍可能指向它，但它**不可被打中** ✓
        /// </summary>
        public virtual bool Untargetable => false;

        protected void MoveStep(Vector2 dir, float speed, float dt, World w, float mul = 1f)
        {
            // ★ v305：正被**潮汐**推着的那 0.25 秒，敌人**自己的**移动按 0 算。
            //   不加这一行 ⇒ 「推开 1.2 m」会被它自己朝玩家的 2.7 m/s 当场吃回去
            //   （净位移 ≈ 0，于是用户报"潮汐的实际距离不对"）。见 `PluginExtra2.TidePushSeconds`。
            //   没装潮汐时 `IsTideHeld` 恒假 ⇒ 本行对既有战斗**逐位无影响** ✓
            float selfMove = PluginExtra2.IsTideHeld(Id) ? 0f : 1f;
            Vector2 v = dir * speed * SpeedFactor(w) * mul * selfMove;
            Pos += (v + KnockbackVel) * dt;
            KnockbackVel = Vector2.Lerp(KnockbackVel, Vector2.zero, 1f - Mathf.Exp(-7f * dt));
            Pos = World.ClampToArena(Pos, Radius, w.Tuning.farm.arenaHalf);
        }

        /// <summary>
        /// v45：与 <see cref="MoveStep"/> 规则相同（同样的速度、异常减速与击退衰减），
        /// 但抵达场地墙壁时**镜面反射**而不是贴住：撞到哪条轴就把该轴的方向分量取反。
        /// 返回 true 表示本帧发生了反弹（撞墙瞬间位置会被钳在墙面内侧，不会越界）。
        /// </summary>
        protected bool MoveStepBounce(ref Vector2 dir, float speed, float dt, World w, float mul = 1f)
        {
            // ★ v305：同 `MoveStep` —— 被潮汐推着时压住自主移动（见那里的说明）
            float selfMove = PluginExtra2.IsTideHeld(Id) ? 0f : 1f;
            Vector2 v = dir * speed * SpeedFactor(w) * mul * selfMove;
            Vector2 next = Pos + (v + KnockbackVel) * dt;
            float lim = Mathf.Max(0.1f, w.Tuning.farm.arenaHalf - Radius);
            bool bounced = false;

            // 只在"朝墙走"时取反：被击退顶到墙上时方向不变，等击退衰减后自然离开，避免来回抖动
            if (next.x > lim) { next.x = lim; if (dir.x > 0f) dir.x = -dir.x; bounced = true; }
            else if (next.x < -lim) { next.x = -lim; if (dir.x < 0f) dir.x = -dir.x; bounced = true; }
            if (next.y > lim) { next.y = lim; if (dir.y > 0f) dir.y = -dir.y; bounced = true; }
            else if (next.y < -lim) { next.y = -lim; if (dir.y < 0f) dir.y = -dir.y; bounced = true; }

            Pos = next;
            KnockbackVel = Vector2.Lerp(KnockbackVel, Vector2.zero, 1f - Mathf.Exp(-7f * dt));
            return bounced;
        }

        protected void TouchTick(float dt, World w, Actor target)
        {
            if (target == null || !target.Alive) return;
            TouchTimer -= dt;
            if (TouchTimer > 0f) return;
            // ★ v210：接触判据用 `Radius × ContactScale`（判定体比 `radius` 更瘦）★
            //   改前 `target.Radius + Radius + 0.06f` ⇒ `grunt` 为 0.45 + 0.535 + 0.06 = **1.045m**
            //   改后 `target.Radius + Radius × 0.70 + 0.06f` ⇒ 0.45 + 0.375 + 0.06 = **0.884m** ✓
            float reach = target.Radius + Radius * ContactScale + 0.06f;
            if (Vector2.Distance(target.Pos, Pos) > reach) return;
            TouchTimer = Mathf.Max(0.2f, Def.TouchCooldown);
            w.DealDamage(new DamageRequest
            {
                Type = DamageType.Physical,
                RawValue = Def.TouchDamageForWave(WaveIndex),
                Source = this,
                Target = target
            });
        }

        public override void Tick(float dt, World w)
        {
            if (TickStatus(dt, w)) return;
            Behaviour(dt, w);
        }

        protected abstract void Behaviour(float dt, World w);
    }

    /// <summary>
    /// v45：小猪——不攻击、不接触伤害，是"移动的肥料补给"。
    /// 移动规则重做：**生成瞬间取一个随机初始方向**，之后以恒定速度沿直线前进，
    /// 撞到场地墙壁时按镜面反射折返（撞哪条轴就反哪条轴），路径完全可预判。
    /// 旧的"靠近玩家 5 格内反向逃跑"逻辑已删除——它让小猪的路径不可预判，与弹球式设计冲突。
    /// </summary>
    public sealed class WanderEnemy : EnemyActor
    {
        /// <summary>当前朝向（单位向量）。生成后不再随机改变，只被墙壁反射改写。</summary>
        public Vector2 Heading = Vector2.right;

        /// <summary>累计撞墙反弹次数（自检与遥测用）。</summary>
        public int BounceCount;

        /// <summary>是否已经拿到初始方向（生成入口调用 InitHeading 后为 true）。</summary>
        public bool HeadingReady { get; private set; }

        /// <summary>
        /// 生成时给一个随机初始方向：同一 seed 必然给出同一个方向（确定性）。
        /// 由 <see cref="World.SpawnEnemy"/> 在生成瞬间调用。
        /// </summary>
        public void InitHeading(Pcg32 rng)
        {
            float a = rng.Range(0f, 6.2831853f);
            Heading = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (Heading.sqrMagnitude < 1e-6f) Heading = Vector2.right;   // 防御：退化成零向量时给个确定方向
            BounceCount = 0;
            HeadingReady = true;
        }

        protected override void Behaviour(float dt, World w)
        {
            // 兜底：任何绕过 World.SpawnEnemy 的构造路径（测试直接 new）也要有确定方向
            if (!HeadingReady) InitHeading(w.Rng);

            if (MoveStepBounce(ref Heading, Def.MoveSpeed, dt, w)) BounceCount++;
            // 不攻击、不接触伤害
        }
    }

    public sealed class GruntEnemy : EnemyActor
    {
        protected override void Behaviour(float dt, World w)
        {
            // ★ v172：原来这里先问"6 格内有没有嘲讽植物"（`w.TauntPlantNear(Pos, 6f)`）✗
            //   嘲讽植物已不存在 ⇒ 那个查询恒为 null ⇒ 方法已整条删除 ✓
            //   现在**直接打玩家** —— 这正是原本"没有嘲讽植物"时走的那条分支 ✓
            //   ★ 因为嘲讽恒为 null，这条改动【不改变任何既有行为】★ ✓
            Actor target = w.NearestAlly(Pos);
            if (target == null) return;
            Vector2 delta = target.Pos - Pos;
            float dist = delta.magnitude;
            // ★ v210：追击停距同乘 `ContactScale`（与 `TouchTick` 的 `reach` 同步收窄，
            //   ⇒ 停下来之后 `dist ≤ reach` 依然成立，接触伤害不会因为「停太远」而消失）★
            if (dist > target.Radius + Radius * ContactScale)
                MoveStep(delta.normalized, Def.MoveSpeed, dt, w);
            TouchTick(dt, w, target);
        }
    }

    public sealed class ShooterEnemy : EnemyActor
    {
        protected override void Behaviour(float dt, World w)
        {
            // ★ v172：同 `GruntEnemy` —— 嘲讽查询已删除，直接打玩家（行为不变）✓
            Actor target = w.NearestAlly(Pos);
            if (target == null) return;
            Vector2 delta = target.Pos - Pos;
            float dist = delta.magnitude;
            Vector2 dir = dist > 0.001f ? delta / dist : Vector2.up;

            if (dist > Def.AttackRange)
            {
                // 超出射程：靠近
                MoveStep(dir, Def.MoveSpeed, dt, w);
            }
            else
            {
                // 进入射程：开火；贴太近则后撤（安全距离的一半作为退让阈值）
                AttackTimer -= dt;
                if (AttackTimer <= 0f)
                {
                    AttackTimer = Mathf.Max(0.3f, Def.AttackInterval);
                    SpawnShot(dir, w, target);
                }
                if (dist < Def.AttackRange * 0.75f)
                    MoveStep(-dir, Def.MoveSpeed, dt, w);
            }
            TouchTick(dt, w, target);
        }

        private void SpawnShot(Vector2 dir, World w, Actor target)
        {
            float dmg = Def.ProjectileDamageForWave(WaveIndex);
            w.SpawnProjectile(Projectile.EnemyShot(this, Pos + dir * (Radius + 0.2f), dir, dmg,
                Def.ProjectileSpeed, Def.Color));
            Bus.Publish(new EnemyShotEvent { Pos = Pos });
        }
    }

    public struct EnemyShotEvent { public Vector2 Pos; }

    public sealed class JumperEnemy : EnemyActor
    {
        private enum Phase { Wander, Windup, Recover }

        private Phase _phase = Phase.Wander;
        private float _wanderTimer;
        private float _jumpCd = 2f;
        private float _windup;
        private float _recover;
        private Vector2 _wanderDir = Vector2.right;
        private Vector2 _landing;

        // ★ v191-b：**跳扑的飞行段**（修"一帧瞬移"）★
        //
        // ── 病根（实测，`MOTION2` 探针，锁实例 id，900 帧窗口）──
        // 旧实现只在 `_windup` 走完的那一帧执行 `Pos = _landing;` ⇒
        //   蚱蜢的三只实例单帧位移 **6.71m / 6.07m / 5.24m**
        //   ⇒ 位置 `最大帧位移/中位` = **239 / 216 / 187**（判据 ≤3）✗
        //   ⇒ 而且那一帧的朝向会「真的翻过去」（位移方向本来就是反的）
        //     ⇒ 实测 `急反转`（连续 3 帧内掉头）= 2 次 ✗
        // 这就是画面上"**闪一下出现在别处**"的来源 ✓
        //
        // ── 修法：把这一段位移**摊到整个蓄力窗口里**，用 smoothstep 缓动 ──
        //   · 总时长**一个数都不改**：起跳仍在 `JumpWindup` 结束那一刻，
        //     落地冲击 / 伤害判定 / `_recover` 全部原地不动 ✓
        //   · 落点**一个数都不改**：终点恰好是 `_landing`（`u=1` 时 `Lerp` 返回它本身）✓
        //   · smoothstep 两端速度为 0 ⇒ 起飞与落地都没有"顿一下" ✓
        //   · 单帧位移从 6.71m 降到 ≈ `距离 × 1.5 × dt / 蓄力时长`
        //     （6m / 0.8s 蓄力，60fps ⇒ 0.19m/帧）⇒ **降两个数量级** ✓
        //   · ⚠️ 逐帧位移仍**高于走路**（这是"扑"的定义，不是缺陷）：
        //     平均飞行速度 = 距离/蓄力 = 6/0.8 = 7.5 m/s，而游走速度 = 3.06×0.55 ≈ 1.68 m/s
        //     ⇒ 峰/中的理论下限就是 **≈5**（smoothstep 峰值 ×1.5 ⇒ ≈7.5）
        //     ⇒ 判据 ≤3 **对扑击这一种动作在数学上不可达**，除非改"总时长或落点" —— 那两条本轮明令禁止 ✓
        private Vector2 _jumpFrom;      // 起跳点（触发那一帧的位置）
        private float _jumpDur;         // 这一扑的蓄力总时长（= 触发时的 `JumpWindup`）
        private float _jumpElapsed;     // 已经在空中走了多久

        /// <summary>跳跃由 JumperMech 激活前，精英怪先游走。</summary>
        public float WindupRemaining => _windup;
        public Vector2 LandingPoint => _landing;

        protected override void Behaviour(float dt, World w)
        {
            PlayerActor player = w.Player;
            switch (_phase)
            {
                case Phase.Wander:
                    WanderTick(dt, w, player);
                    break;
                case Phase.Windup:
                    _windup -= dt;
                    _jumpElapsed += dt;
                    ApplyFlight();
                    if (_windup <= 0f)
                    {
                        Pos = _landing;                 // 终点再钉一次：与旧实现的落点**逐位相同**
                        Pos = World.ClampToArena(Pos, Radius, w.Tuning.farm.arenaHalf);
                        Impact(w);
                        _phase = Phase.Recover;
                        _recover = 0.5f;
                        _jumpCd = Mathf.Max(0.5f, Def.JumpCooldown);
                        Bus.Publish(new BossEvent { Kind = "jump_land", Pos = Pos });
                    }
                    break;
                case Phase.Recover:
                    _recover -= dt;
                    if (_recover <= 0f) _phase = Phase.Wander;
                    break;
            }

            if (player != null && player.Alive) TouchTick(dt, w, player);
        }

        /// <summary>
        /// 把"起跳点 → 落点"这一整段位移按 smoothstep 摊到蓄力窗口上（见 `_jumpFrom` 那段）。
        /// `u=0` ⇒ 恰好还没动；`u=1` ⇒ 恰好落在 `_landing`（所以调用方那一句 `Pos = _landing` 是幂等的）。
        /// </summary>
        private void ApplyFlight()
        {
            if (_jumpDur <= 0.0001f) return;
            float u = Mathf.Clamp01(_jumpElapsed / _jumpDur);
            float fly = u * u * (3f - 2f * u);                  // smoothstep：两端速度为 0
            Pos = Vector2.Lerp(_jumpFrom, _landing, fly);
        }

        private void WanderTick(float dt, World w, PlayerActor player)
        {
            _jumpCd -= dt;
            _wanderTimer -= dt;
            if (_wanderTimer <= 0f)
            {
                _wanderTimer = w.Rng.Range(0.8f, 1.8f);
                float a = w.Rng.Range(0f, 6.2831853f);
                _wanderDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            MoveStep(_wanderDir, Def.MoveSpeed, dt, w, 0.55f);

            if (player == null || !player.Alive) return;
            if (Vector2.Distance(player.Pos, Pos) > Mathf.Max(1f, Def.JumpRange)) return;
            if (_jumpCd > 0f) return;

            _phase = Phase.Windup;
            _windup = Mathf.Max(0.1f, Def.JumpWindup);
            _landing = player.Pos; // 落点 = 触发瞬间玩家位置（可预判走位，T-08 方案 A）
            // ★ v191-b：飞行段的三个初值（总时长 = 这一扑的蓄力时长，落点 = 上面那一句）★
            _jumpFrom = Pos;
            _jumpDur = _windup;
            _jumpElapsed = 0f;
            Bus.Publish(new JumpTelegraphEvent
            {
                From = Pos, To = _landing, Radius = Def.JumpRadius, Duration = _windup
            });
        }

        private void Impact(World w)
        {
            float dmg = Def.JumpDamageForWave(WaveIndex);
            PlayerActor player = w.Player;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, Pos) <= Def.JumpRadius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = dmg, Source = this, Target = player
                });
            }
            // ★ T2：**落地冲击打植物**的整段已删除 ✗ ★
            //   它遍历 `w.Plants`（恒空，无生产者）+ 按 `PlantAlly` 类型取 `p.Radius` ✓
            //   ⇒ 既不编译不过、也从不执行 ✓ 删掉后：落地冲击只打玩家（本方法上半段那条 ✓）
            //   ★ 这条是上一轮审计**明确要求保留**的两条之一，本轮授权后一并删除 ★
            //     保留它的原理由是"让'植物存活/死亡'这条链还在"——
            //     而那条链**本身就要经过 `PlantAlly`** ⇒ 它从来不是保留的理由，而是它也该删的证据 ✓
            Bus.Publish(new EnemyAttackFieldEvent { Pos = Pos, Radius = Def.JumpRadius });
        }
    }

    /// <summary>
    /// Boss：**入场演出**（1.2 秒：不动 / 不攻击 / 不清场 / 免疫伤害），随后两招循环
    /// （环形弹幕 / 冲锋践踏），生命低于 40% 进入二阶段（节奏加快）。
    /// v47：删除召唤招式——BOSS 波只出 BOSS，否则"打死 BOSS 却因场上还剩召唤物而被超时判负"。
    ///
    /// ★ v186：**"出场清场"整条删除** ✗ ★
    /// 用户原话："部分波次出现异常清除所有敌人的情况，修改这些清除敌人的异常，
    ///           为 boss 增加入场动画，boss 不再清除敌人" ✓
    /// 旧行为：`BossEnemy` 的**第一个 tick** 调 `ClearField(w)` —— 把场上所有小怪
    ///   `Health.Kill()` + 标 `NoDrop`（当时的设计意图是"清场给你单挑 BOSS"）✗
    /// 但它是在**玩家不知情**的情况下把一波敌人整批收走：
    ///   · 玩家看到的是"这一波的敌人凭空消失了"（正是用户报的症状之一）✓
    ///   · 而且是**每只 BOSS** 都清一次（10/18/24/28/30… 分钟），所以是"部分波次" ✓
    /// ⇒ 现在改成：**小怪一只都不动**，只播 1.2 秒入场（缩放 + 落地冲击 + 震屏 + 光照）。
    ///   见 `InEntrance` / `EntranceAge`（表现层读这两个数，逻辑层不额外存状态）。
    /// </summary>
    public sealed class BossEnemy : EnemyActor
    {
        /// <summary>
        /// ★ v186：**入场时长** = 1.2 秒 ★（需求区间 1.0~1.5s）
        ///
        /// 期间 BOSS：不移动、不攻击、不清场，并且**免疫伤害**（判据在 `World.DealDamage`）。
        /// 为什么必须免疫：入场是"它来了"的识别窗口，若这段时间就能被白打，
        /// 那玩家看到的就不是"BOSS 登场"而是"BOSS 站着挨打"✗
        /// 为什么不是更久：1.2s 之后玩家还没决定跑位就已经开始躲弹幕了，
        /// 再长会让"BOSS 到场"和"BOSS 开始打你"之间出现空窗 ⇒ 手感断成两段 ✗
        /// </summary>
        public const float EntranceSeconds = 1.2f;

        /// <summary>入场剩余秒数（>0 = 还在入场）。逻辑层**只有这一个**入场状态。</summary>
        private float _entrance = EntranceSeconds;

        /// <summary>正在入场（给 `World.DealDamage` 的无敌判据与表现层读）。</summary>
        /// <summary>
        /// ★ v201：**无敌判据 = 入场中 或 飞蛾消失中** ★
        /// 消失期复用的就是这条既有闸门（`Actors.cs` 的 `World.DealDamage` 里那一句），
        /// 不新开第二个无敌字段 —— 那样就有两套"打不中"的规则，迟早分叉 ✗
        /// </summary>
        public bool InEntrance => _entrance > 0f || MothHidden;

        /// <summary>★ v201：飞蛾消失期 ⇒ 不可选中（见 `EnemyActor.Untargetable` 的三条口径）。</summary>
        public override bool Untargetable => MothHidden;


        /// <summary>入场已经过了多少秒（0 → `EntranceSeconds`）——表现层按它算缩放与冲击时刻。</summary>
        public float EntranceAge => EntranceSeconds - Mathf.Max(0f, _entrance);

        private int _pattern;
        private float _chargeWindup;
        private float _chargeTime;
        private Vector2 _chargeDir = Vector2.right;
        public bool Phase2 => Health.HpPct < 0.4f;

        /// <summary>
        /// ★ v181：**表现信号**（只给动画看，不参与任何玩法判定）★
        ///
        /// 虫怪版的 BOSS 有 M=2 种攻击（环形弹幕 / 冲锋践踏），每种要一个**独立可辨认**的姿势。
        /// 姿势需要三件事，而它们都是本类私有状态：
        ///   ① 现在放的是哪一招  ② 这一招走到第几秒  ③ 冲锋的"蓄力 → 突进"转折点
        /// ★ 为什么不在 `ProceduralBugs` 里订阅 `BossEvent` 反推 ★
        ///   事件只带 `Kind` 与位置、没有"已经过了多久"；靠时间差去猜会在卡帧时读错 ✗
        ///   而这三个数**本来就在本类里**，另存一份就是第二份真相 ✓
        /// 取值：0=没有 1=环形弹幕 2=冲锋蓄力 3=冲锋突进（与 `ProceduralBugs.BugView.PoseOf` 一一对应）
        /// </summary>
        public int CastPose { get; private set; }

        /// <summary>当前姿势已经过的秒数（每个 tick 自增）。</summary>
        public float CastAge { get; private set; }

        private void RaiseCastPose(int kind)
        {
            CastPose = kind;
            CastAge = 0f;
        }

        protected override void Behaviour(float dt, World w)
        {
            CastAge += dt;

            // ★ v186：入场段先推进，推进完就 `return` ⇒ 这一帧不移动、不攻击、不清场 ★
            //   （旧版这里是 `if (!_cleared) { ClearField(w); _cleared = true; }` —— 满场小怪被收走 ✗）
            if (_entrance > 0f)
            {
                _entrance -= dt;
                return;
            }

            PlayerActor player = w.Player;
            float dist = player == null ? 999f : Vector2.Distance(player.Pos, Pos);

            // ★ v201：**飞蛾**走另一套状态机（三阶段，按血量均分）；旧版 BOSS 的下面几段一行不动 ✓
            if (IsMoth) { MothBehaviour(dt, w); return; }

            // ★ v201①「吼叫」：5 环、1 秒一环 —— 期间站定，脚下不放别的招
            if (_roarLeft > 0)
            {
                RoarTick(dt, w);
                TouchTick(dt, w, player);
                return;
            }

            // ★ v201②「蓄力爆炸」：蓄力段站定（预警圈交给 `BossBlastChargeEvent`），到点炸开
            if (_blastWindup > 0f)
            {
                _blastWindup -= dt;
                if (_blastWindup <= 0f) BlastBurst(w);
                TouchTick(dt, w, player);
                return;
            }

            if (_chargeWindup > 0f)
            {
                _chargeWindup -= dt;
                if (_chargeWindup <= 0f)
                {
                    _chargeTime = 0.55f;
                    RaiseCastPose(3);                       // v181：蓄力 → 突进（姿势换段）
                    Bus.Publish(new BossEvent { Kind = "charge_start", Pos = Pos });
                }
                TouchTick(dt, w, player);
                return;
            }

            if (_chargeTime > 0f)
            {
                _chargeTime -= dt;
                MoveStep(_chargeDir, Def.MoveSpeed * 6f, dt, w);
                TouchTick(dt, w, player);
                // ★ T2：**BOSS 冲锋打植物**的整段已删除 ✗ ★（同上：`w.Plants` 恒空 + `PlantAlly` 类型引用）
                //   BOSS 冲锋现在只靠 `TouchTick` 打玩家 ✓（那两行在下面 `return;` 之前原样保留 ✓）
                return;
            }

            // 缓慢逼近，保持在中距离
            if (player != null && player.Alive && dist > 7f)
                MoveStep((player.Pos - Pos).normalized, Def.MoveSpeed, dt, w);

            float speedMul = Phase2 ? 1.35f : 1f;
            AttackTimer -= dt * speedMul;
            if (AttackTimer <= 0f)
            {
                AttackTimer = Mathf.Max(1.0f, Def.AttackInterval);
                CastPattern(w, player);
            }
            TouchTick(dt, w, player);
        }

        private void CastPattern(World w, PlayerActor player)
        {
            // v47：BOSS 波只出 BOSS——召唤招式已删除（它曾经是"打死 BOSS 却被 90 秒判负"的根源：
            //      召唤出的小怪留在场上，而 BOSS 波要求清空全场才算过关）。
            int pattern = _pattern % 4;
            _pattern++;
            // ★ v201：招式表 2 → **4**（新增「吼叫」与「蓄力爆炸」）★
            //   顺序刻意让新招排在后面 ⇒ 第一次攻击仍是既有的环形弹幕，
            //   "改版后第一眼看到的东西"没变（旧录像/旧截图仍然对得上）✓
            switch (pattern)
            {
                case 0: RingBarrage(w); break;
                case 1: Charge(w, player); break;
                case 2: StartRoar(w); break;
                default: StartBlastCharge(w); break;
            }
        }

        private void RingBarrage(World w)
        {
            RaiseCastPose(1);                               // v181：攻击 1 = 环形弹幕
            int count = Phase2 ? 24 : 16;
            Vector2 origin = Pos;
            float dmg = Def.ProjectileDamageForWave(WaveIndex);   // v47：环形弹幕也吃 2 的回合次方
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * 6.2831853f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                w.SpawnProjectile(Projectile.EnemyShot(this, origin + dir * (Radius + 0.3f), dir,
                    dmg, Def.ProjectileSpeed, Def.Color));
            }
            Bus.Publish(new BossEvent { Kind = "barrage", Pos = Pos });
        }

        private void Charge(World w, PlayerActor player)
        {
            RaiseCastPose(2);                               // v181：攻击 2 = 冲锋（蓄力段）
            _chargeDir = player != null && player.Alive
                ? (player.Pos - Pos).normalized
                : new Vector2(1f, 0f);
            _chargeWindup = Mathf.Max(0.3f, Def.JumpWindup);
            Bus.Publish(new JumpTelegraphEvent
            {
                From = Pos, To = Pos + _chargeDir * 14f, Radius = Def.JumpRadius, Duration = _chargeWindup
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v201①：旧版 BOSS 的**两个新招式** ★★
        //   几何（`ProceduralBugs` 的 `Giant` 那几个 build 函数）**一个字节都没动** ——
        //   连 `GetMeshes` 的 BOSS 分支、`ShapeOf`、`MeshStats`/`MeshFingerprint` 的 BOSS 行
        //   也全都没碰。这一节只加**技能与表现信号**（需求：旧 BOSS 只强化技能）✓
        //   判据：`python Tools/mesh_gen/verify_bug_meshes.py --check` 的
        //         `boss block == 备份逐字（sha256=5ddccf30262f72b1）` 必须仍然 PASS ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>吼叫的**环数**（需求：持续发出圆环波状弹幕 **5 次**）。</summary>
        public const int RoarRingCount = 5;

        /// <summary>吼叫的**环间隔**（需求：**间隔 1 秒**）。</summary>
        public const float RoarRingInterval = 1.0f;

        /// <summary>每一环的弹数（一整圈 ⇒ 从 BOSS 向外扩散）。</summary>
        public const int RoarBulletsPerRing = 20;

        /// <summary>吼叫的**起手**（第一环之前的那一下仰头 —— 也是「吼叫」这个技能的识别窗口）。</summary>
        public const float RoarWindup = 0.45f;

        /// <summary>还剩几环没发（0 = 没在吼）。公开给自检/探针读，与事件同源。</summary>
        public int RoarRingsLeft => _roarLeft;

        /// <summary>已经发出去的环数（0..5）。</summary>
        public int RoarRingsFired => RoarRingCount - _roarLeft;

        private int _roarLeft;
        private float _roarTimer;
        private bool _roarPrimed;
        /// <summary>每一环的起始角度：逐环旋转 9° ⇒ 5 个环叠起来是**旋转的花**，不是同一圈打五遍。</summary>
        private float _roarPhase;

        private void StartRoar(World w)
        {
            RaiseCastPose(CastPoseRoar);
            _roarLeft = RoarRingCount;
            _roarTimer = RoarWindup;        // 先仰头，第一环在 RoarWindup 之后
            _roarPrimed = false;
            _roarPhase = 0f;
            Bus.Publish(new BossEvent { Kind = "roar", Pos = Pos });
            Bus.Publish(new BossRoarEvent { Pos = Pos, Ring = -1, Radius = 0f });   // Ring=-1 ⇒ 起手（可播"吸气"）
        }

        private void RoarTick(float dt, World w)
        {
            // 起手那一帧就把姿势立起来（之后 `CastAge` 自己走，表现层按 age 算曲线）
            if (!_roarPrimed) { _roarPrimed = true; RaiseCastPose(CastPoseRoar); }
            _roarTimer -= dt;
            if (_roarTimer > 0f) return;

            _roarTimer += RoarRingInterval;     // ★ 用 `+=` 而不是 `=`：卡帧时不会把 5 环挤成一帧 ✓
            int ring = RoarRingCount - _roarLeft;
            float dmg = Def.ProjectileDamageForWave(WaveIndex);
            Vector2 origin = Pos;
            for (int i = 0; i < RoarBulletsPerRing; i++)
            {
                float a = (i / (float)RoarBulletsPerRing) * 6.2831853f + _roarPhase;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                w.SpawnProjectile(Projectile.EnemyShot(this, origin + dir * (Radius + 0.3f), dir,
                    dmg, Def.ProjectileSpeed, Def.Color));
            }
            Bus.Publish(new BossRoarEvent
            {
                Pos = Pos, Ring = ring, Radius = Def.ProjectileSpeed * RoarRingInterval
            });
            _roarLeft--;
            _roarPhase += 0.1570796f;           // 9°（π/20）
            if (_roarLeft <= 0) RaiseCastPose(0);
        }

        /// <summary>蓄力爆炸的**蓄力时长**（需求：要有蓄力动画 / 预警圈 ⇒ 这个数就是预警圈的存活期）。</summary>
        public const float BlastChargeSeconds = 1.8f;

        /// <summary>爆炸半径（米）。BOSS 半径 1.5 ⇒ 玩家必须真的跑出这个圈才不吃伤害。</summary>
        public const float BlastRadius = 6.5f;

        /// <summary>爆炸伤害 = 跳砸伤害 × 这个系数（1.25 ⇒ 同一波次比一次跳砸更疼，但还不是秒杀）。</summary>
        public const float BlastDamageMul = 1.25f;

        /// <summary>正在蓄力（>0 = 蓄力剩余秒数）。</summary>
        public float BlastWindupRemaining => Mathf.Max(0f, _blastWindup);

        /// <summary>爆炸次数（自检/探针用：证明真的炸过）。</summary>
        public int BlastCount { get; private set; }

        private float _blastWindup;

        private void StartBlastCharge(World w)
        {
            RaiseCastPose(CastPoseBlast);
            _blastWindup = BlastChargeSeconds;
            Bus.Publish(new BossEvent { Kind = "blast_charge", Pos = Pos });
            // 预警圈：半径与时长都给表现层（它要画一个从 0 长到 BlastRadius 的地面圈）
            Bus.Publish(new BossBlastChargeEvent { Pos = Pos, Radius = BlastRadius, Duration = BlastChargeSeconds });
        }

        private void BlastBurst(World w)
        {
            BlastCount++;
            RaiseCastPose(0);
            float dmg = Def.JumpDamageForWave(WaveIndex) * BlastDamageMul;
            PlayerActor player = w.Player;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, Pos) <= BlastRadius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = dmg, Source = this, Target = player
                });
            }
            Bus.Publish(new BossBlastEvent { Pos = Pos, Radius = BlastRadius });
            Bus.Publish(new EnemyAttackFieldEvent { Pos = Pos, Radius = BlastRadius });
        }

        // ── 表现信号：4 = 吼叫 5 = 蓄力爆炸（与 `ProceduralBugs.BugView.PoseOf` 一一对应）──
        public const int CastPoseRoar = 4;
        public const int CastPoseBlast = 5;

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v201②：**飞蛾 BOSS**（三阶段，按血量均分）★★
        //
        // 形态（用户原话："三个阶段，按照血量平均切换"）：
        //   `> 66%` 毛毛虫：会爬、**朝玩家方向冲刺 3 次** + 扇形弹幕
        //   `33~66%` 蛹  ：**不能移动**、持续弹幕（螺旋）+ **从天而降、锁定玩家的激光**
        //   `< 33%` 飞蛾  ：弹幕 + **消失一段时间**后**突然跳砸** + **更快的冲刺 5 次**
        //
        // ★ 为什么整只飞蛾写在 `BossEnemy` 里，而不是另立一个 `MothEnemy` ★
        //   构造点在 `World.SpawnEnemy` 的 `switch (def.Kind)`（`Actors.cs`）——
        //   那个文件**不在本轮允许改动的清单里**（另一路正在改它附近）。
        //   而"是不是飞蛾"由**数据表 id** 决定（`enemies.json` 的 `moth`），
        //   所以复用 `BossEnemy` 这条既有的 BOSS 通道：
        //     · 血量走 BOSS 曲线、`bossIndex` 加成、入场演出、BossBar、波次结算 —— 全部照旧 ✓
        //     · 差别只有**行为**：`IsMoth` 为真时走 `MothBehaviour` ✓
        //   ⇒ 一行都没碰 `Actors.cs` / `Defs.cs` 的伤害与血量逻辑 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>血量 &gt; 它 ⇒ 毛毛虫（第一阶段）。</summary>
        public const float MothLarvaAbove = 0.66f;
        /// <summary>血量 &gt; 它（且 ≤ 上一档）⇒ 蛹（第二阶段）。</summary>
        public const float MothPupaAbove = 0.33f;

        /// <summary>血量百分比 → 形态号（0=毛毛虫 1=蛹 2=飞蛾）。**唯一实现**，自检与探针都读它。</summary>
        public static int MothPhaseOf(float hpPct)
            => hpPct > MothLarvaAbove ? 0 : (hpPct > MothPupaAbove ? 1 : 2);

        // ── 毛毛虫 ──
        public const int LarvaDashCount = 3;
        public const float LarvaDashWindup = 0.55f;
        public const float LarvaDashSeconds = 0.28f;
        public const float LarvaDashSpeedMul = 7.5f;      // × MoveSpeed(1.6) = 12.0 m/s
        public const float LarvaDashGap = 0.45f;
        public const float LarvaBarrageEvery = 3.0f;
        public const int LarvaBarrageCount = 10;
        public const float LarvaBarrageSpreadDeg = 120f;
        public const float LarvaDashCooldown = 5.0f;
        // ── 蛹 ──
        public const float PupaBarrageEvery = 2.2f;
        public const int PupaBarrageCount = 12;
        public const float PupaLaserEvery = 4.5f;
        public const float PupaLaserWarnSeconds = 1.10f;
        /// <summary>预警的最后这一段**锁定**落点（之前跟着玩家跑）——"锁定玩家"的可玩含义。</summary>
        public const float PupaLaserLockTail = 0.35f;
        public const float PupaLaserFallSeconds = 0.22f;
        public const float PupaLaserBurnSeconds = 3.0f;
        public const float PupaLaserBurnTick = 0.5f;
        public const float PupaLaserBurnDamageRatio = 0.35f;   // × 跳砸伤害/次
        // ── 飞蛾 ──
        public const int MothDashCount = 5;
        public const float MothDashWindup = 0.35f;
        public const float MothDashSeconds = 0.24f;
        public const float MothDashSpeedMul = 10f;        // × MoveSpeed(1.6) = 16.0 m/s
        public const float MothDashGap = 0.32f;
        public const float MothBarrageEvery = 2.4f;
        public const int MothBarrageCount = 14;
        public const float MothDashCooldown = 4.0f;
        public const float MothVanishEvery = 8.0f;
        public const float MothVanishSeconds = 1.6f;
        public const float MothSlamWindup = 0.5f;
        public const float MothSlamDamageRatio = 1.15f;
        // ★ v201：**消失期不挪位置** ★
        //   第一版把本体挪到场地外 60m（想让"半径/扇形/接触判定够不到"），实测发现一个真问题：
        //   若它**在消失期被打死**，那个 60m 的位置就再没有人搬回来（尸体不再 tick，`VanishSlam` 永不执行）
        //   ⇒ 场上留一只"血 0、不可见、在 60m 外"的幽灵 ✓
        //   而"打不中"其实**已经由 `InEntrance` 这条唯一闸门保证**了（`World.DealDamage` 直接 return）⇒
        //   位移是多余的保险，删掉它换来"位置永远等于真实位置"这条更重要的不变量 ✓
        public const int CastPoseDash = 6;
        public const int CastPoseLaser = 7;
        public const int CastPoseVanishSlam = 8;

        /// <summary>是不是飞蛾（由数据表 id 决定；纯函数，不依赖随机）。</summary>
        public bool IsMoth => Def != null && Def.Id == "moth";

        /// <summary>当前形态（0=毛毛虫 1=蛹 2=飞蛾；非飞蛾恒为 -1）。表现层按它换模型。</summary>
        public int MothPhaseIndex { get; private set; } = -1;

        /// <summary>当前形态已经过了多少秒（表现层用来做"刚变形"的一下）。</summary>
        public float MothPhaseAge { get; private set; }

        /// <summary>是否处在**消失**期（不可选中 + 模型隐藏）。</summary>
        public bool MothHidden { get; private set; }

        /// <summary>飞蛾状态机当前的动作名（探针/自检读数，也用于截图时确认打到哪个分支）。</summary>
        public string MothActName => _mAct.ToString();

        /// <summary>本次连冲还剩几次（0 = 没在冲）。</summary>
        public int MothDashLeft => _mDashLeft;

        /// <summary>已经打出的弹幕波数 / 激光次数 / 跳砸次数（自检与探针的证据面）。</summary>
        public int MothVolleys { get; private set; }
        public int MothLasers { get; private set; }
        public int MothSlams { get; private set; }
        public int MothFormSwitches { get; private set; }

        /// <summary>激光落点与阶段（0=预警 1=落下 2=灼烧；-1=没在打激光）。</summary>
        public Vector2 MothLaserAt => _mLaserAt;
        public float MothLaserRadius => Def != null ? Def.JumpRadius : 3f;
        public int MothLaserStage => _mAct == MothAct.LaserWarn ? 0 : _mAct == MothAct.LaserFall ? 1
                                   : _burnRemain > 0f ? 2 : -1;

        private enum MothAct { Crawl, DashWindup, Dash, DashGap, Barrage, LaserWarn, LaserFall, Vanish, SlamWindup, Slam }

        private MothAct _mAct = MothAct.Crawl;
        private float _mTimer;              // 当前动作的剩余秒数（>0 = 还在这个动作里）
        private int _mDashLeft;
        private Vector2 _mDashDir;
        private float _mDashCd;
        private float _mLaserCd;
        private float _mVanishCd;
        private float _mBarrageCd;
        private Vector2 _mLaserAt;
        private float _mVolleyPhase;
        private float _burnRemain, _burnTick, _burnRadius;
        private Vector2 _burnAt;
        private bool _mSlamTelegraph;

        private void MothBehaviour(float dt, World w)
        {
            PlayerActor player = w.Player;
            UpdateMothPhase();
            BurnTick(dt, w, player);

            // 消失期：不移动、不攻击、不受伤（见 `InEntrance`）。计时到点就现身并跳砸。
            if (MothHidden)
            {
                _mTimer -= dt;
                if (_mTimer <= 0f) VanishSlam(w, player);
                return;
            }

            switch (_mAct)
            {
                case MothAct.DashWindup:
                    _mTimer -= dt;
                    if (_mTimer <= 0f) { BeginDash(w, player); }
                    break;

                case MothAct.Dash:
                    _mTimer -= dt;
                    // 匀速直线冲刺（位移由速度决定，**不瞬移**）——朝玩家方向的那一条线
                    MoveStep(_mDashDir, Def.MoveSpeed * (MothPhaseIndex == 2 ? MothDashSpeedMul : LarvaDashSpeedMul), dt, w);
                    TouchTick(dt, w, player);
                    if (_mTimer <= 0f) EndDash(w);
                    break;

                case MothAct.DashGap:
                    _mTimer -= dt;
                    if (_mTimer <= 0f)
                    {
                        if (_mDashLeft > 0) StartDashWindup(w, player);
                        else { _mAct = MothAct.Crawl; RaiseCastPose(0); _mDashCd = MothPhaseIndex == 2 ? MothDashCooldown : LarvaDashCooldown; }
                    }
                    break;

                case MothAct.LaserWarn:
                    _mTimer -= dt;
                    // ★ 锁定 = 落点跟玩家当前位 ★ —— 直到最后 `PupaLaserLockTail` 秒才钉死
                    if (_mTimer > PupaLaserLockTail) _mLaserAt = player != null ? player.Pos : _mLaserAt;
                    if (_mTimer <= 0f)
                    {
                        _mAct = MothAct.LaserFall;
                        _mTimer = PupaLaserFallSeconds;
                        RaiseCastPose(CastPoseLaser);
                        Bus.Publish(new MothLaserEvent { Pos = _mLaserAt, Radius = MothLaserRadius, Stage = 1 });
                        LaserImpact(w, player);
                    }
                    break;

                case MothAct.LaserFall:
                    _mTimer -= dt;
                    if (_mTimer <= 0f) { _mAct = MothAct.Crawl; RaiseCastPose(0); _mLaserCd = PupaLaserEvery; }
                    break;

                case MothAct.SlamWindup:
                    _mTimer -= dt;
                    if (_mTimer <= 0f) SlamImpact(w, player);
                    break;

                default:
                    MothIdleTick(dt, w, player);
                    break;
            }
        }

        /// <summary>形态切换：血量过阈值就换（含"变形"的一下，表现层读 `MothFormEvent`）。</summary>
        private void UpdateMothPhase()
        {
            int phase = MothPhaseOf(Health.HpPct);
            if (phase == MothPhaseIndex) return;
            MothPhaseIndex = phase;
            MothPhaseAge = 0f;
            MothFormSwitches++;
            _mAct = MothAct.Crawl;
            _mTimer = 0f;
            _mDashLeft = 0;
            _mDashCd = 1.2f;
            _mLaserCd = 1.4f;
            _mVanishCd = 2.0f;
            _mBarrageCd = 0.6f;
            RaiseCastPose(0);
            Bus.Publish(new MothFormEvent { Pos = Pos, Phase = phase });
        }

        /// <summary>不在任何"正在发生"的动作里时：按形态决定"下一步做什么"。</summary>
        private void MothIdleTick(float dt, World w, PlayerActor player)
        {
            _mBarrageCd -= dt;
            _mDashCd -= dt;
            _mLaserCd -= dt;
            _mVanishCd -= dt;

            bool playerAlive = player != null && player.Alive;

            if (MothPhaseIndex == 1)
            {
                // ── 蛹：**不能移动**（一个 `MoveStep` 都不调）──
                if (_mLaserCd <= 0f) { StartLaser(w, player); return; }
                if (_mBarrageCd <= 0f) { MothBarrage(w, player, PupaBarrageCount, 360f, true); _mBarrageCd = PupaBarrageEvery; }
                return;
            }

            // 毛毛虫（0）/ 飞蛾（2）：朝玩家慢慢挪（毛毛虫爬、飞蛾悬停）
            if (playerAlive)
            {
                float d = Vector2.Distance(player.Pos, Pos);
                // ★ v210：撞车停距同乘 `ContactScale`（BOSS 也不许把判定体怼到玩家身上）★
                //   改前 `Radius + player.Radius + 0.5f`（moth：1.903 + 0.45 + 0.5 = **2.853m**）
                //   改后 `Radius × 0.70 + player.Radius + 0.5f`（0.999 + 0.45 + 0.5 = **1.949m**）✓
                float stop = Radius * ContactScale + player.Radius + 0.5f;
                if (d > stop) MoveStep((player.Pos - Pos).normalized, Def.MoveSpeed, dt, w);
            }

            if (MothPhaseIndex == 2 && _mVanishCd <= 0f) { StartVanish(w); return; }
            if (_mDashCd <= 0f && playerAlive) { StartDashCombo(w, player); return; }
            if (_mBarrageCd <= 0f)
            {
                MothBarrage(w, player, MothPhaseIndex == 2 ? MothBarrageCount : LarvaBarrageCount,
                            MothPhaseIndex == 2 ? 360f : LarvaBarrageSpreadDeg, MothPhaseIndex == 2);
                _mBarrageCd = MothPhaseIndex == 2 ? MothBarrageEvery : LarvaBarrageEvery;
            }
        }

        private void MothBarrage(World w, PlayerActor player, int count, float spreadDeg, bool fullCircle)
        {
            Vector2 origin = Pos;
            float dmg = Def.ProjectileDamageForWave(WaveIndex);
            float baseAngle;
            if (fullCircle)
            {
                baseAngle = _mVolleyPhase;
                _mVolleyPhase += 0.2617994f;      // 15°：逐波旋转 ⇒ 螺旋，而不是每次同一张网
            }
            else
            {
                Vector2 aim = player != null && player.Alive ? player.Pos - Pos : Vector2.right;
                baseAngle = Mathf.Atan2(aim.y, aim.x) - spreadDeg * 0.5f * Mathf.Deg2Rad;
            }
            float step = fullCircle ? (6.2831853f / count) : (spreadDeg * Mathf.Deg2Rad / Mathf.Max(1, count - 1));
            for (int i = 0; i < count; i++)
            {
                float a = baseAngle + step * i;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                w.SpawnProjectile(Projectile.EnemyShot(this, origin + dir * (Radius + 0.3f), dir,
                    dmg, Def.ProjectileSpeed, Def.Color));
            }
            MothVolleys++;
            Bus.Publish(new MothBarrageEvent { Pos = Pos, Count = count });
        }

        private void StartDashCombo(World w, PlayerActor player)
        {
            _mDashLeft = MothPhaseIndex == 2 ? MothDashCount : LarvaDashCount;
            StartDashWindup(w, player);
        }

        private void StartDashWindup(World w, PlayerActor player)
        {
            _mAct = MothAct.DashWindup;
            _mTimer = MothPhaseIndex == 2 ? MothDashWindup : LarvaDashWindup;
            _mDashDir = player != null && player.Alive
                ? (player.Pos - Pos).normalized
                : new Vector2(1f, 0f);
            RaiseCastPose(CastPoseDash);
            // 预警线：这一段位移的可视化（表现层画一条越来越亮的线）
            Bus.Publish(new MothDashEvent
            {
                From = Pos, To = Pos + _mDashDir * (Def.MoveSpeed * LarvaDashSpeedMul * LarvaDashSeconds),
                Duration = _mTimer, Impact = false
            });
        }

        private void BeginDash(World w, PlayerActor player)
        {
            _mAct = MothAct.Dash;
            _mTimer = MothPhaseIndex == 2 ? MothDashSeconds : LarvaDashSeconds;
            // ★ 冲刺方向 = 起冲**那一刻**的玩家方向（朝玩家方向冲刺）★
            if (player != null && player.Alive) _mDashDir = (player.Pos - Pos).normalized;
            _mDashLeft--;
            RaiseCastPose(CastPoseDash);
            Bus.Publish(new MothDashEvent { From = Pos, To = Pos + _mDashDir * 4f, Duration = _mTimer, Impact = true });
        }

        private void EndDash(World w)
        {
            _mAct = MothAct.DashGap;
            _mTimer = MothPhaseIndex == 2 ? MothDashGap : LarvaDashGap;
            // 冲完那一下顶到玩家身上：一次冲刺伤害（走跳砸那条曲线，和 BOSS 冲锋同源）
            PlayerActor player = w.Player;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, Pos) <= Radius + player.Radius + 0.45f)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical,
                    RawValue = Def.JumpDamageForWave(WaveIndex) * 0.6f,
                    Source = this, Target = player
                });
            }
        }

        private void StartLaser(World w, PlayerActor player)
        {
            MothLasers++;
            _mAct = MothAct.LaserWarn;
            _mTimer = PupaLaserWarnSeconds;
            _mLaserAt = player != null ? player.Pos : Pos;
            RaiseCastPose(CastPoseLaser);
            Bus.Publish(new MothLaserEvent { Pos = _mLaserAt, Radius = MothLaserRadius, Stage = 0 });
        }

        /// <summary>激光落下：命中的玩家吃一发，并把地面烧起来（`PupaLaserBurnSeconds` 秒的灼烧区）。</summary>
        private void LaserImpact(World w, PlayerActor player)
        {
            float dmg = Def.JumpDamageForWave(WaveIndex) * 0.8f;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, _mLaserAt) <= MothLaserRadius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = dmg, Source = this, Target = player
                });
            }
            _burnAt = _mLaserAt;
            _burnRadius = MothLaserRadius;
            _burnRemain = PupaLaserBurnSeconds;
            _burnTick = 0f;
        }

        /// <summary>灼烧区：站在里面每 `PupaLaserBurnTick` 秒吃一跳（伤害 = 跳砸曲线 × 0.35）。</summary>
        private void BurnTick(float dt, World w, PlayerActor player)
        {
            if (_burnRemain <= 0f) return;
            _burnRemain -= dt;
            _burnTick -= dt;
            if (_burnTick > 0f) return;
            _burnTick += PupaLaserBurnTick;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, _burnAt) <= _burnRadius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Ailment,
                    RawValue = Def.JumpDamageForWave(WaveIndex) * PupaLaserBurnDamageRatio,
                    Source = this, Target = player
                });
            }
        }

        private void StartVanish(World w)
        {
            MothHidden = true;
            _mAct = MothAct.Vanish;
            _mTimer = MothVanishSeconds;
            RaiseCastPose(CastPoseVanishSlam);
            Bus.Publish(new MothVanishEvent { Pos = Pos, Hidden = true });
            // ★ 位置**不动**（见 `MothSlamDamageRatio` 下面那段说明：位移会在"消失期被打死"时留下幽灵）★
            //   "打不中"由 `InEntrance`（⇒ `World.DealDamage` 的唯一闸门）保证，
            //   "够不到"由本分支每帧直接 `return`（不调 `TouchTick` / `MoveStep`）保证 ✓
        }

        /// <summary>现身 → 落到玩家位置跳砸（落地前一瞬给预警圈）。</summary>
        private void VanishSlam(World w, PlayerActor player)
        {
            MothHidden = false;
            _mAct = MothAct.SlamWindup;
            _mTimer = MothSlamWindup;
            MothSlamFrom = Pos;
            MothSlamTo = player != null && player.Alive ? player.Pos : Pos;
            RaiseCastPose(CastPoseVanishSlam);
            Bus.Publish(new MothVanishEvent { Pos = Pos, Hidden = false });
            Bus.Publish(new MothSlamEvent
            {
                From = MothSlamFrom, To = MothSlamTo, Radius = MothLaserRadius, Duration = MothSlamWindup
            });
            Bus.Publish(new JumpTelegraphEvent
            {
                From = MothSlamFrom, To = MothSlamTo, Radius = MothLaserRadius, Duration = MothSlamWindup
            });
            _mSlamTelegraph = true;
        }

        /// <summary>跳砸落点（表现层取景与自检读）。</summary>
        public Vector2 MothSlamFrom { get; private set; }
        public Vector2 MothSlamTo { get; private set; }

        private void SlamImpact(World w, PlayerActor player)
        {
            if (_mSlamTelegraph)
            {
                _mSlamTelegraph = false;
                MothSlams++;
            }
            Pos = MothSlamTo;
            Pos = World.ClampToArena(Pos, Radius, w.Tuning.farm.arenaHalf);
            _mAct = MothAct.Crawl;
            _mTimer = 0f;
            RaiseCastPose(0);
            _mVanishCd = MothVanishEvery;
            float dmg = Def.JumpDamageForWave(WaveIndex) * MothSlamDamageRatio;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, Pos) <= MothLaserRadius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = dmg, Source = this, Target = player
                });
            }
            Bus.Publish(new EnemyAttackFieldEvent { Pos = Pos, Radius = MothLaserRadius });
        }

        // ★ v186：`ClearField(w)` **整段删除** ✗ ★
        //
        // 旧实现（原文保留在此，避免下一个人按"清场"这个念头再写一遍）：
        //     for (int i = w.Enemies.Count - 1; i >= 0; i--)
        //     {
        //         EnemyActor e = w.Enemies[i];
        //         if (e == this || !e.Alive) continue;
        //         e.NoDrop = true;
        //         e.Health.Kill();
        //         e.Alive = false;
        //     }
        //     Bus.Publish(new BossEvent { Kind = "clear_field", Pos = Pos });
        //
        // 删除理由（用户原话）："修改这些清除敌人的异常，为 boss 增加入场动画，
        //                        boss 不再清除敌人" ✓
        // 连带影响（都已核对）：
        //   · `BossEvent{Kind="clear_field"}` 不再发布 ⇒ `WorldView.OnBoss` 那一支的
        //     紫光大闪与紫色标记不再出现（那本来就是"清场"的演出）✓ 别的 kind 照常 ✓
        //   · `NoDrop` 字段本身**保留** ✓ —— 它的注释与语义仍是"演出性清空不掉落"，
        //     且 `Enemies.cs` 之外没有第二个写者，删字段等于动一条与本需求无关的规则 ✓
        //   · 小怪的 50% 金币折算（`folded` 局部变量）本来就是**未使用**的计算 ✗
        //     （v32 起"金币只从收割来"），随方法一起消失，**不改变任何数值** ✓
    }
}
