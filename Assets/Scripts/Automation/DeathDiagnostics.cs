using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 只读死亡诊断（本轮新增）★ —— 回答「它到底是怎么死的」，
    /// **不改变任何玩法数值、不改变任何分支、不掷任何随机数**。
    ///
    /// ────────────────────────────────────────────────────────────
    /// 为什么需要它
    /// ────────────────────────────────────────────────────────────
    /// `SimResult` 只有**结局量**（活了多久 / 击杀多少 / 收割多少）✗
    /// 而「Balanced 死在 294.7 秒」这个数字本身**不可解释**：
    ///   · 敌人**变多了**？      ⇒ 该动刷新曲线（spawnCountGrowth / maxAlive*）
    ///   · 敌人**变强了**？      ⇒ 该动成长曲线（hpStepA/B、dmgStepA/B）
    ///   · 玩家那一刻**站错了地方** / **决策卡住了**？ ⇒ 该动的是 Bot ✗ 不是数值
    ///   · 玩家**成长跟不上**？  ⇒ 该动经验/收割收益
    /// 四者的修法完全不同 ⇒ **在动任何数值之前，先把死因变成数字** ✓
    ///
    /// ────────────────────────────────────────────────────────────
    /// ★ 关键：它怎么做到"零改动"就拿到承伤归因 ★
    /// ────────────────────────────────────────────────────────────
    /// 原本承伤归因看起来必须往 `World.DealDamage` 里插一行埋点（动玩法文件）✗
    /// 但 `DamageResolver.Resolve` 末尾**已经有**一句：
    ///
    ///     else if (req.Source is EnemyActor e) e.DamageDone += dealt;
    ///
    /// ⇒ **每个敌人身上本来就记着"它一共打出了多少伤害"** ✓（Actors.cs:280）
    /// 所以只要**逐步对每个敌人的 `DamageDone` 取增量**，就得到
    /// 「哪一只敌人、在什么时刻、打出了多少」——**完全只读** ✓
    /// 再按它的 `Kind` + 三条通道的期望值（接触 / 弹幕 / 跳砸）分类，
    /// 就得到"哪一类敌人的哪一条通道各造成多少伤害" ✓
    ///
    /// 代价：`DamageDone` 是 `dealt`（**已过无敌帧、已被护盾吸收之后**的实际值）⇒
    ///   · 好处：它就是"真正掉的血"，正是要问的问题 ✓
    ///   · 代价：**拿不到"被无敌帧挡掉的次数"** ✗ —— 报告里不编这个数，只标注未测 ✓
    ///
    /// ────────────────────────────────────────────────────────────
    /// ★ 它不会改变跑局结果 ★
    /// ────────────────────────────────────────────────────────────
    /// 只读 `World` / `RunSession` / `RunBot` 的**公共字段**，不写回任何一处 ✓
    /// 不调用 `Rng` ⇒ 不移动随机流 ⇒ 同种子结果不变 ✓
    /// （可验证：`Run` 带不带 observe 回调，`SimResult` 应逐字段相同）
    /// </summary>
    public static class DeathDiagnostics
    {
        /// <summary>采样间隔（游戏秒）。0.5 秒足够看清"一路掉"还是"突然崩"。</summary>
        public const float SampleInterval = 0.5f;

        /// <summary>一次承伤事件（按敌人增量归因）。</summary>
        public struct Hit
        {
            public float T;
            public string Kind;      // 如 "grunt·接触"
            public float Amount;     // 实际掉的血（护盾 + 生命合计）
            public float HpAfter;
            public int EnemyKind;    // EnemyKind
        }

        /// <summary>一次时间线采样。</summary>
        public struct Sample
        {
            public float T;
            public int Wave;
            public int Level;
            public float Hp, MaxHp, Shield, MaxShield;
            public int Grunt, Shooter, Jumper, Boss, Pig;
            public int Kills;
            public int WaterAmmo;
            public int Plants;
            public float X, Y;
            public string Decision;
            public int Enemies => Grunt + Shooter + Jumper + Boss;
        }

        public static readonly List<Hit> Hits = new List<Hit>();
        public static readonly List<Sample> Timeline = new List<Sample>();
        /// <summary>每次分钟边界（`StartWave` 会重置玩家满血）的记录。</summary>
        public static readonly List<string> MinuteMarks = new List<string>();
        /// <summary>性能计数器。</summary>
        public static float ObserveCalls;

        private static readonly Dictionary<int, float> PrevDamageDone = new Dictionary<int, float>();
        private static int _lastWave = -1;

        public static void Reset()
        {
            Hits.Clear();
            Timeline.Clear();
            MinuteMarks.Clear();
            PrevDamageDone.Clear();
            _lastWave = -1;
            ObserveCalls = 0;
        }

        /// <summary>
        /// 每个固定步调用一次（由 <see cref="RunSimulator.Run"/> 的 observe 回调驱动）。
        /// 必须在 `FixedTick` **之前**调用：这样"本步开始时还在场上的敌人"全都看得见 ✓
        /// </summary>
        public static void Observe(float t, RunSession s, RunBot bot)
        {
            if (s == null) return;
            World w = s.World;
            if (w == null || w.Player == null) return;
            ObserveCalls++;

            PlayerActor p = w.Player;

            // ── ① 承伤归因：逐敌人取 DamageDone 增量 ──
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.Def == null) continue;
                float now = e.DamageDone;
                if (PrevDamageDone.TryGetValue(e.Id, out float was))
                {
                    float d = now - was;
                    if (d > 0.0001f)
                    {
                        Hits.Add(new Hit
                        {
                            T = t,
                            Amount = d,
                            Kind = Classify(e, d),
                            HpAfter = p.Health.Hp,
                            EnemyKind = (int)e.Def.Kind
                        });
                    }
                }
                PrevDamageDone[e.Id] = now;
            }

            // ── ② 时间线 ──
            int g = 0, sh = 0, j = 0, b = 0, pig = 0;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead || e.Def == null) continue;
                switch (e.Def.Kind)
                {
                    case EnemyKind.Grunt: g++; break;
                    case EnemyKind.Shooter: sh++; break;
                    case EnemyKind.Jumper: j++; break;
                    case EnemyKind.Boss: b++; break;
                    default: pig++; break;          // Wander = 小猪
                }
            }

            Timeline.Add(new Sample
            {
                T = t,
                Wave = s.WaveIndex,
                Level = s.Progress.Level,
                Hp = p.Health.Hp,
                MaxHp = p.Health.MaxHp,
                Shield = p.Health.Shield,
                MaxShield = p.Health.MaxShield,
                Grunt = g, Shooter = sh, Jumper = j, Boss = b, Pig = pig,
                Kills = w.KillCount,
                WaterAmmo = s.WaterAmmo,
                Plants = w.Farm != null ? w.Farm.PlantCount : 0,
                X = p.Pos.x, Y = p.Pos.y,
                Decision = bot != null ? bot.LastDecision : ""
            });

            if (s.WaveIndex != _lastWave)
            {
                _lastWave = s.WaveIndex;
                MinuteMarks.Add(string.Format(CultureInfo.InvariantCulture,
                    "  t={0,7:0.0}s  第 {1} 分钟开始（StartWave ⇒ 满血 + 满盾 + 传送回 (0,0) + 水 +50）",
                    t, s.WaveIndex));
            }
        }

        /// <summary>
        /// 把一次伤害归因到「哪一类敌人的哪一条通道」。
        /// 三条通道的期望值都不同（grunt 只有接触 1.0；shooter 接触 0.4 / 弹幕 0.5；
        /// jumper 接触 0.8 / 跳砸 1.6）⇒ 按最接近的一条判定，不会歧义 ✓
        /// </summary>
        private static string Classify(EnemyActor e, float amount)
        {
            EnemyDef d = e.Def;
            int w = e.WaveIndex;
            float touch = d.TouchDamageForWave(w);
            float shot = d.ProjectileDamageForWave(w);
            float jump = d.JumpDamageForWave(w);

            string ch = "?";
            float best = float.MaxValue;
            if (touch > 0f) { best = Mathf.Abs(touch - amount); ch = "接触"; }
            if (shot > 0f)
            {
                float dd = Mathf.Abs(shot - amount);
                if (dd < best - 0.0001f) { best = dd; ch = "弹幕"; }
            }
            if (jump > 0f)
            {
                float dd = Mathf.Abs(jump - amount);
                if (dd < best - 0.0001f) { best = dd; ch = "跳砸"; }
            }
            return d.Id + "·" + ch;
        }

        // ================================================================
        //                              报告
        // ================================================================

        /// <summary>
        /// 生成死亡诊断报告。<paramref name="deathTime"/> 来自 `SimResult.Duration`；
        /// <paramref name="damagedByEnemies"/> 是 `World.DamagedByEnemies`（权威总承伤），
        /// 用来核对"归因之和"是否等于"总量" ✓
        /// </summary>
        public static string BuildReport(SimResult r, float damagedByEnemies)
        {
            var sb = new StringBuilder();
            float t0 = r.Duration;
            float win = 30f;
            float from = t0 - win;

            sb.AppendLine("# Seedfall 守园者 · 死亡诊断（只读观测，未改任何玩法数值）");
            sb.AppendLine();
            sb.AppendLine("## 结论摘要");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  策略 {0}｜种子 {1}｜死亡时刻 **{2:0.0}s**（第 {3} 分钟第 {4:0.0} 秒）｜等级 {5}｜击杀 {6}｜金币 {7}",
                r.Strategy, r.Seed, t0, r.Wave, t0 - (r.Wave - 1) * 60f, r.Level, r.Kills, r.Gold));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  死亡时场上敌人 {0} 只｜整局承伤 {1:0.0}｜死前 {2:0} 秒承伤 {3:0.0}",
                LastSample().Enemies, damagedByEnemies, win, DamageBetween(from, t0)));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  ★ 一句话：{0}", OneLineCause(t0, damagedByEnemies)));
            sb.AppendLine();

            // ── A. 死前 30 秒时间线 ──
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "## A · 死前 {0:0} 秒时间线（每 {1:0.0} 秒一行；敌人按 近战/远程/跳跃/头目/小猪）", win, SampleInterval * 2f));
            sb.AppendLine("     t     血/上限    盾/上限   近 远 跳 头 猪  累计杀  弹药  植株  玩家(x,y)         Bot决策");
            for (int i = 0; i < Timeline.Count; i++)
            {
                Sample s = Timeline[i];
                if (s.T < from) continue;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:0.0}  {1,5:0.0}/{2,-5:0.0} {3,4:0.0}/{4,-4:0.0}  {5,2} {6,2} {7,2} {8,2} {9,2}   {10,5}  {11,4}  {12,4}  ({13,5:0.0},{14,5:0.0})  {15}",
                    s.T, s.Hp, s.MaxHp, s.Shield, s.MaxShield,
                    s.Grunt, s.Shooter, s.Jumper, s.Boss, s.Pig,
                    s.Kills, s.WaterAmmo, s.Plants, s.X, s.Y, s.Decision));
            }
            sb.AppendLine();

            // ── B. 承伤归因 ──
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "## B · 承伤归因（死前 {0:0} 秒）", win));
            sb.Append(Attribution(from, t0));
            sb.AppendLine();
            sb.AppendLine("## C · 承伤归因（整局）");
            sb.Append(Attribution(0f, float.MaxValue));
            sb.AppendLine();

            // ── D. 成长 vs 敌人（分钟级）──
            sb.AppendLine("## D · 每分钟对照（玩家成长 vs 敌人成长）");
            sb.AppendLine("   分钟   样本  平均等级  平均在场敌  平均血/上限   该分钟击杀  该分钟承伤   耕作决策占比");
            sb.Append(PerMinute());
            sb.AppendLine();

            // ── E. 分钟边界 ──
            sb.AppendLine("## E · 分钟边界（每分钟 `StartWave` 会把玩家补满 —— 所以「活到第 N 分钟」是分段成立的）");
            if (MinuteMarks.Count == 0) sb.AppendLine("  （无）");
            for (int i = 0; i < MinuteMarks.Count; i++) sb.AppendLine(MinuteMarks[i]);
            sb.AppendLine();

            // ── F. Bot 决策分布 ──
            sb.AppendLine("## F · Bot 决策分布（死前 30 秒 vs 整局）—— 用来分辨「玩家在种地」还是「玩家在被追」");
            sb.Append(DecisionMix());
            sb.AppendLine();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  观测调用 {0} 次（采样 {1} 次）", ObserveCalls, Timeline.Count));
            return sb.ToString();
        }

        private static Sample LastSample()
            => Timeline.Count > 0 ? Timeline[Timeline.Count - 1] : default;

        private static float DamageBetween(float from, float to)
        {
            float sum = 0f;
            for (int i = 0; i < Hits.Count; i++)
            {
                Hit h = Hits[i];
                if (h.T >= from && h.T <= to) sum += h.Amount;
            }
            return sum;
        }

        /// <summary>归因表：按 (来源·通道) 汇总。</summary>
        private static string Attribution(float from, float to)
        {
            var total = new Dictionary<string, float>();
            var count = new Dictionary<string, int>();
            var maxHit = new Dictionary<string, float>();
            float grand = 0f;
            int grandCount = 0;

            for (int i = 0; i < Hits.Count; i++)
            {
                Hit h = Hits[i];
                if (h.T < from || h.T > to) continue;
                total.TryGetValue(h.Kind, out float cur);
                total[h.Kind] = cur + h.Amount;
                count.TryGetValue(h.Kind, out int c);
                count[h.Kind] = c + 1;
                maxHit.TryGetValue(h.Kind, out float mx);
                maxHit[h.Kind] = Mathf.Max(mx, h.Amount);
                grand += h.Amount;
                grandCount++;
            }

            var sb = new StringBuilder();
            if (grand <= 0f)
            {
                sb.AppendLine("  （这段窗口内没有归因到任何承伤）");
                return sb.ToString();
            }

            // 按总量降序（数据量小，直接排序）
            var keys = new List<string>(total.Keys);
            keys.Sort((a, b) => total[b].CompareTo(total[a]));

            sb.AppendLine("    来源·通道          命中次数   合计伤害   占比    平均     最大");
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                float v = total[k];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,-16}  {1,6}   {2,8:0.0}  {3,5:0.0}%  {4,6:0.00}  {5,6:0.00}",
                    k, count[k], v, v / grand * 100f, v / count[k], maxHit[k]));
            }
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    {0,-16}  {1,6}   {2,8:0.0}  100.0%", "合计", grandCount, grand));
            return sb.ToString();
        }

        private static string PerMinute()
        {
            if (Timeline.Count == 0) return "  （无采样）\n";
            var sb = new StringBuilder();
            int maxWave = 0;
            for (int i = 0; i < Timeline.Count; i++) maxWave = Mathf.Max(maxWave, Timeline[i].Wave);

            for (int wv = 1; wv <= maxWave; wv++)
            {
                float lo = (wv - 1) * 60f, hi = wv * 60f;
                int n = 0, lvl = 0, en = 0, hp = 0, mhp = 0;
                int killsFirst = -1, killsLast = 0;
                float farmDec = 0f;
                for (int i = 0; i < Timeline.Count; i++)
                {
                    Sample s = Timeline[i];
                    if (s.T < lo || s.T > hi) continue;
                    n++; lvl += s.Level; en += s.Enemies; hp += (int)s.Hp; mhp += (int)s.MaxHp;
                    if (killsFirst < 0) killsFirst = s.Kills;
                    killsLast = s.Kills;
                    if (IsFarmDecision(s.Decision)) farmDec += 1f;
                }
                if (n == 0) continue;
                float dmg = DamageBetween(lo, hi);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,5}  {1,5}   {2,8:0.0}  {3,10:0.0}  {4,7:0.0}/{5,-5:0.0}  {6,9}  {7,10:0.0}  {8,11:0.0}%",
                    wv, n, lvl / (float)n, en / (float)n, hp / (float)n, mhp / (float)n,
                    killsLast - killsFirst, dmg, farmDec / n * 100f));
            }
            return sb.ToString();
        }

        /// <summary>这些决策算"玩家在种地"（与 `RunBot.FarmIntentSeconds` 的口径一致）。</summary>
        private static bool IsFarmDecision(string d)
        {
            if (string.IsNullOrEmpty(d)) return false;
            return d.StartsWith("harvest") || d.StartsWith("water") || d.StartsWith("fert")
                || d.StartsWith("collect") || d.StartsWith("shop");
        }

        private static string DecisionMix()
        {
            var window = new Dictionary<string, int>();
            var whole = new Dictionary<string, int>();
            float from = LastSample().T - 30f;
            for (int i = 0; i < Timeline.Count; i++)
            {
                Sample s = Timeline[i];
                string key = string.IsNullOrEmpty(s.Decision) ? "(空)" : s.Decision;
                whole.TryGetValue(key, out int c1);
                whole[key] = c1 + 1;
                if (s.T >= from)
                {
                    window.TryGetValue(key, out int c2);
                    window[key] = c2 + 1;
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("    决策                   死前30秒   整局");
            var keys = new List<string>(whole.Keys);
            keys.Sort((a, b) => whole[b].CompareTo(whole[a]));
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                window.TryGetValue(k, out int wc);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,-22} {1,7}  {2,6}", k, wc, whole[k]));
            }
            return sb.ToString();
        }

        /// <summary>一句话死因 —— 由数字推出来，不是猜的。</summary>
        private static string OneLineCause(float t0, float damagedByEnemies)
        {
            float from = t0 - 30f;
            float last30 = DamageBetween(from, t0);
            Sample last = LastSample();

            // 最大的那个来源
            var total = new Dictionary<string, float>();
            for (int i = 0; i < Hits.Count; i++)
            {
                Hit h = Hits[i];
                if (h.T < from) continue;
                total.TryGetValue(h.Kind, out float cur);
                total[h.Kind] = cur + h.Amount;
            }
            string top = "（无）";
            float topV = 0f;
            foreach (var kv in total)
                if (kv.Value > topV) { topV = kv.Value; top = kv.Key; }

            float enemies = last.Enemies;
            return string.Format(CultureInfo.InvariantCulture,
                "死前 30 秒承伤 {0:0.0}（累计 {1:0.0}）中，{2} 占 {3:0.0}%；死亡瞬间场上 {4} 只敌人",
                last30, damagedByEnemies, top, last30 > 0f ? topV / last30 * 100f : 0f, enemies);
        }
    }
}
