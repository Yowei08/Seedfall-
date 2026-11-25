using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v253：**六个新机制里需要"跨帧状态"的那两个**（回响 / 收束）★★
    ///
    /// ── 为什么它们不能像灼烧/减速那样塞进 `PluginDotTable` ──
    ///   `PluginDotTable` 的语义是"**每秒**结算一次的持续伤害"，而这两者的语义是
    ///   "**一次性、在某个时刻/第几次**结算"：
    ///     · 回响 = 命中后 `EchoDelay` 秒**再结算一次**（不是持续掉血）
    ///     · 收束 = 对**同一个敌人**数到第 N 次时**追加一次**（不是每秒）
    ///   塞进 DoT 表会让"回响 3 秒"这种读法出现，而玩家看到的是"同一刀疼两次" ✓
    ///
    /// ── 确定性 ──
    ///   本类**不消耗任何随机数**，只用"帧时间 + 目标 id" ⇒ 不影响 `Pcg32` 序列，
    ///   钉种子的自检/回放读数不会因为装了这两件插件而整体漂移 ✓
    ///
    /// ── 生命周期 ──
    ///   `Reset()` 在每个会话开始（`RunSession.Start`）清空 —— 静态表跨局残留是这类
    ///   "延迟结算"最容易踩的坑（上一局的回响打到这一局的怪身上）✓
    /// </summary>
    public static class PluginMech
    {
        // ─────────────────────────── 回响 ───────────────────────────
        private struct Echo
        {
            public float Delay;
            public int EnemyId;
            public float Damage;
            public Vector2 Pos;
        }

        private static readonly List<Echo> Echoes = new List<Echo>(32);

        /// <summary>排一条回响：`delay` 秒后在 `enemyId` 身上再结算 `damage` 点伤害。</summary>
        public static void QueueEcho(int enemyId, float damage, float delay, Vector2 pos)
        {
            if (damage <= 0f) return;
            Echoes.Add(new Echo
            {
                Delay = Mathf.Max(0.05f, delay),
                EnemyId = enemyId,
                Damage = damage,
                Pos = pos,
            });
        }

        /// <summary>回响的条数（探针/自检读数用）。</summary>
        public static int PendingEchoes => Echoes.Count;

        // ─────────────────────────── 收束 ───────────────────────────
        /// <summary>每个敌人身上的连续命中次数 + 最后一次命中的时刻。</summary>
        private static readonly Dictionary<int, int> FocusCount = new Dictionary<int, int>(32);
        private static readonly Dictionary<int, float> FocusAt = new Dictionary<int, float>(32);

        /// <summary>连续命中多久没续上就清零（秒）。</summary>
        public const float FocusWindow = 4f;

        /// <summary>
        /// 记一次命中，返回"这一次是不是该触发处决"。
        /// 到达 <paramref name="every"/> 次 ⇒ 触发并把计数清零 ✓
        /// </summary>
        public static bool BumpFocus(int enemyId, int every, float now)
        {
            if (every <= 0) return false;
            FocusCount.TryGetValue(enemyId, out int n);
            // 上一次命中离现在太久 ⇒ 从头数（"连续"才有意义）
            if (FocusAt.TryGetValue(enemyId, out float last) && now - last > FocusWindow) n = 0;
            n++;
            FocusAt[enemyId] = now;
            bool fire = n >= every;
            FocusCount[enemyId] = fire ? 0 : n;
            return fire;
        }

        /// <summary>清空全部跨帧状态（每个会话开始时调）。</summary>
        public static void Reset()
        {
            Echoes.Clear();
            FocusCount.Clear();
            FocusAt.Clear();
        }

        /// <summary>
        /// 每帧推进（由 `RunSession.FixedTick` 调，与 `Dots.Tick` 同一批）。
        /// 到点的回响**当场结算**：目标还活着才疼 ✓（死了就不补刀 —— 与"吸血只看当下这一刀"同一条洁癖）
        /// </summary>
        public static void Tick(float dt, World world, PlayerActor source)
        {
            if (world == null || Echoes.Count == 0) return;
            for (int i = Echoes.Count - 1; i >= 0; i--)
            {
                Echo e = Echoes[i];
                e.Delay -= dt;
                if (e.Delay > 0f) { Echoes[i] = e; continue; }
                Echoes.RemoveAt(i);
                EnemyActor target = world.FindEnemyById(e.EnemyId);
                if (target == null || target.IsDead) continue;
                // ★★ v308：「回响」弹字要带**实际造成的伤害**（用户判据）★★
                //   数字取 `DamageResult.Dealt`（这一下半量伤害**真正扣掉的血/盾**），
                //   不是 `e.Damage`（那是 40% 的算值：残血截断 / 闪避归零都会让它偏大）✓
                DamageResult echoHit = world.DealDamage(new DamageRequest
                {
                    Type = DamageType.Magic,          // 回响不是"物理第二刀"：它绕过物理抗性那套语义
                    RawValue = e.Damage,
                    Source = source,
                    Target = target,
                    Knockback = 0f,
                    Crit = false,
                    // ★★ v286：**回响吃灼印倍率，但不摘印** ★★
                    //   它不是"下一次受到的伤害"，而是**同一次命中延迟入账的那一部分**（40%）。
                    //   让它摘印的后果与子爆/毒区 tick 完全同形：一次旧的锄头命中，
                    //   回响在 0.3 秒后才到 ⇒ 若中途肥料投掷刚挂上印，就被这 40% 的余响吃掉 ✗
                    //   ⇒ 四处豁免用同一条判据：**派生伤害只吃倍率，直击本体才摘印** ✓
                    NoMarkConsume = true,
                });
                // ★ v308：`Dealt < 0.05`（被闪避 / 免伤 / 目标在那 0.3 秒里被别的东西打死）
                //   ⇒ `DamageLabel` 返回 null ⇒ **不发这条弹字** ✓
                string echoText = PluginHitFx.DamageLabel("回响", echoHit.Dealt);
                if (echoText == null) continue;
                Bus.Publish(new FloatingTextEvent
                {
                    Pos = target.Pos,
                    Text = echoText,
                    Kind = PopupKind.DamageToEnemy,
                    Scale = 0.85f,
                });
            }
        }
    }
}
