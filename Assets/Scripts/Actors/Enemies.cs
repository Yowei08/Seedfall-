using System;
using UnityEngine;

namespace Seedfall
{
    public struct JumpTelegraphEvent { public Vector2 From, To; public float Radius, Duration; }
    public struct BossEvent { public string Kind; public Vector2 Pos; }
    public struct EnemyAttackFieldEvent { public Vector2 Pos; public float Radius; }

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

        /// <summary>返回 true 表示处于冻结，本帧不能移动与攻击。</summary>
        protected bool TickStatus(float dt, World w)
        {
            TickCommonTimers(dt);
            Status.Tick(dt, this, w);
            if (!Alive) return true;
            return Status.Frozen;
        }

        protected float SpeedFactor(World w) => Status.SpeedFactor(w.Tuning.formula);

        protected void MoveStep(Vector2 dir, float speed, float dt, World w, float mul = 1f)
        {
            Vector2 v = dir * speed * SpeedFactor(w) * mul;
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
            Vector2 v = dir * speed * SpeedFactor(w) * mul;
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
            float reach = target.Radius + Radius + 0.06f;
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
            if (dist > target.Radius + Radius)
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
                    if (_windup <= 0f)
                    {
                        Pos = _landing;
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
    /// Boss：出场清场（其余怪物金币 50% 直接入账），两招循环（环形弹幕 / 冲锋践踏），
    /// 生命低于 40% 进入二阶段（节奏加快）。
    /// v47：删除召唤招式——BOSS 波只出 BOSS，否则"打死 BOSS 却因场上还剩召唤物而被超时判负"。
    /// </summary>
    public sealed class BossEnemy : EnemyActor
    {
        private bool _cleared;
        private int _pattern;
        private float _chargeWindup;
        private float _chargeTime;
        private Vector2 _chargeDir = Vector2.right;
        public bool Phase2 => Health.HpPct < 0.4f;

        protected override void Behaviour(float dt, World w)
        {
            if (!_cleared)
            {
                ClearField(w);
                _cleared = true;
            }

            PlayerActor player = w.Player;
            float dist = player == null ? 999f : Vector2.Distance(player.Pos, Pos);

            if (_chargeWindup > 0f)
            {
                _chargeWindup -= dt;
                if (_chargeWindup <= 0f)
                {
                    _chargeTime = 0.55f;
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
            int pattern = _pattern % 2;
            _pattern++;
            if (pattern == 0) RingBarrage(w);
            else Charge(w, player);
        }

        private void RingBarrage(World w)
        {
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
            _chargeDir = player != null && player.Alive
                ? (player.Pos - Pos).normalized
                : new Vector2(1f, 0f);
            _chargeWindup = Mathf.Max(0.3f, Def.JumpWindup);
            Bus.Publish(new JumpTelegraphEvent
            {
                From = Pos, To = Pos + _chargeDir * 14f, Radius = Def.JumpRadius, Duration = _chargeWindup
            });
        }

        /// <summary>出场清场：其余怪物立即死亡，金币按 50% 折算直接入账（T-09 方案 B）。</summary>
        private void ClearField(World w)
        {
            int folded = 0;
            for (int i = w.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor e = w.Enemies[i];
                if (e == this || !e.Alive) continue;
                e.NoDrop = true;
                e.Health.Kill();
                e.Alive = false;
                folded += e.Def.Gold;
            }
            // v32：金币/经验只能来自击杀掉落的经验球（删除凭空给钱）
            Bus.Publish(new BossEvent { Kind = "clear_field", Pos = Pos });
        }
    }
}
