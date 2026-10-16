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
        /// <summary>报告里时间线的行间隔（秒）。采样本身是每固定步一次，打印时按它抽稀。</summary>
        public const float RowInterval = 1.0f;

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
            public int Gold;
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
        /// <summary>最后一次观测到的游戏时刻（秒）—— 用来把"车不在场上"折算成占比。</summary>
        public static float ObservedSeconds;

        // ── ★ 商店车【实测】★ ──
        //   为什么要实测而不是推导：`CanShop` 为真的**时长**是这件事的唯一判据 ✗
        //   这里用**引擎自己的** `ShopTruck.CanShop(d)` 判定，距离也按 `TickShopTruck`
        //   的同一条算式算 ⇒ 量的是引擎的真条件，不是设计值 ✓
        /// <summary>`CanShop` 为真的累计秒数（整局）。</summary>
        public static float ShopOpenSeconds;
        /// <summary>`CanShop` 由假变真的次数（= 真正可交互的窗口个数）。</summary>
        public static int ShopOpenWindows;
        /// <summary>最长的一段连续可交互时长（秒）。</summary>
        public static float ShopOpenLongest;
        /// <summary>整局 `Dock` 阶段的总时长（对照：设计值 = 10 秒 × 窗口数）。</summary>
        public static float ShopDockSeconds;
        /// <summary>
        /// ★ 机制判据 ★ 停靠期"引擎实际摆车的点"离 `DockPoint` 的最远距离（米）。
        /// 停车正确时应 ≈ 0；位置错配时它会在 10 秒里跑满整圈 ⇒ 十几米 ✗
        /// </summary>
        public static float ShopTruckOffDockMax;
        /// <summary>同一帧里按**旧算法**（`PointAt(PhaseT)`）算的同一量 —— 受控对照 ✓</summary>
        public static float ShopTruckOffDockMaxOld;
        /// <summary>★ 用户判据 ★ 车**停在停靠位**（离停靠位 ≤ <see cref="AtDockEpsilon"/> 米）的最长连续时长。</summary>
        public static float ShopTruckAtDockLongest;
        /// <summary>车**不在场上**（`Idle`，模型被隐藏）的累计秒数 —— 回答"是不是常驻"。</summary>
        public static float ShopAwaySeconds;
        /// <summary>其中**轮与轮之间**的那部分（不含开局等待）—— 这才是"开走之后消失多久"。</summary>
        public static float ShopAwayBetweenSeconds;
        /// <summary>走完的完整轮数（`Idle→Entering` 到 `Leaving→Idle`）。</summary>
        public static int ShopCycles;
        /// <summary>各轮总时长之和（秒）；除以 <see cref="ShopCycles"/> 得一轮均值。</summary>
        public static float ShopCycleSecondsSum;
        /// <summary>一轮最短 / 最长（秒）。</summary>
        public static float ShopCycleShortest = float.MaxValue;
        public static float ShopCycleLongest;
        /// <summary>逐阶段实测累计（秒）：入场 / 绕场 / 停靠 / 离场。</summary>
        public static float ShopEnterMeasured, ShopLoopMeasured, ShopDockMeasured, ShopLeaveMeasured;

        /// <summary>"停在停靠位"的判定容差（米）。</summary>
        public const float AtDockEpsilon = 0.5f;

        private static float _shopOpenRun;
        private static bool _shopOpenPrev;
        private static float _atDockRun;
        private static ShopTruck.Phase _shopPhasePrev = ShopTruck.Phase.Idle;
        private static float _cycleStartAt = -1f;
        private static bool _cycleTimingArmed;
        private static bool _truckSeenOnce;

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
            ObservedSeconds = 0f;
            ShopOpenSeconds = 0f;
            ShopOpenWindows = 0;
            ShopOpenLongest = 0f;
            ShopDockSeconds = 0f;
            ShopTruckOffDockMax = 0f;
            ShopTruckOffDockMaxOld = 0f;
            ShopTruckAtDockLongest = 0f;
            ShopAwaySeconds = 0f;
            ShopAwayBetweenSeconds = 0f;
            ShopCycles = 0;
            ShopCycleSecondsSum = 0f;
            ShopCycleShortest = float.MaxValue;
            ShopCycleLongest = 0f;
            ShopEnterMeasured = 0f;
            ShopLoopMeasured = 0f;
            ShopDockMeasured = 0f;
            ShopLeaveMeasured = 0f;
            _shopOpenRun = 0f;
            _shopOpenPrev = false;
            _atDockRun = 0f;
            _shopPhasePrev = ShopTruck.Phase.Idle;
            _cycleStartAt = -1f;
            _cycleTimingArmed = false;
            _truckSeenOnce = false;
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
            ObservedSeconds = t;

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
                Gold = s.Gold,
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

            ObserveShopTruck(s, t);
        }

        /// <summary>
        /// ★ 实测商店停靠窗 ★ —— 每固定步记录"玩家此刻按 E 会不会真的进店"，
        /// 以及"车到底停在哪、停了多久、一轮多长"。
        ///
        /// 判定用**引擎自己的** `Truck.CanShop(d)`，`d` 用 `TickShopTruck` 里那同一条算式 ✓
        /// ⇒ 量出来的是"可交互时长"，不是"设计时长" ✓
        ///
        /// ⚠️ 它必须跟着**引擎实际用的取位入口**走 ✗ —— 所以修 bug 时这里要一起改，
        ///    否则"修好了"只是观测在自我安慰 ✓（本会话反复出现的形状）
        /// </summary>
        private static void ObserveShopTruck(RunSession s, float t)
        {
            ShopTruck truck = s.Truck;
            World w = s.World;
            if (truck == null || w == null || w.Player == null) return;
            float dt = GameClock.FixedDt;
            float half = w.Tuning.farm.arenaHalf;
            ShopTruck.Phase ph = truck.Current;

            // ── ① 轮时长：Idle→Entering 起算，Leaving→Idle 收尾 ──
            if (ph != _shopPhasePrev)
            {
                if (ph == ShopTruck.Phase.Entering && _shopPhasePrev == ShopTruck.Phase.Idle)
                {
                    _cycleStartAt = t;
                    _cycleTimingArmed = true;
                }
                else if (ph == ShopTruck.Phase.Idle && _shopPhasePrev == ShopTruck.Phase.Leaving && _cycleTimingArmed)
                {
                    float dur = t - _cycleStartAt;
                    ShopCycles++;
                    ShopCycleSecondsSum += dur;
                    if (dur < ShopCycleShortest) ShopCycleShortest = dur;
                    if (dur > ShopCycleLongest) ShopCycleLongest = dur;
                    _cycleTimingArmed = false;
                }
                _shopPhasePrev = ph;
            }

            // ── ② 逐阶段实测累计 ──
            switch (ph)
            {
                case ShopTruck.Phase.Entering: ShopEnterMeasured += dt; break;
                case ShopTruck.Phase.Looping: ShopLoopMeasured += dt; break;
                case ShopTruck.Phase.Dock: ShopDockMeasured += dt; break;
                case ShopTruck.Phase.Leaving: ShopLeaveMeasured += dt; break;
                default: ShopAwaySeconds += dt; break;      // Idle = 不在场上（模型被隐藏）
            }
            // ★ "车不在场上"要拆成两段 ★ —— 否则它会骗人：
            //   · **开局等待**（设计：第一分钟没有车，`Reset` 把首个周期锚在 +60s）✓
            //   · **轮与轮之间的间隙**（"开走之后消失了多久"= 用户说的"不要常驻"）✓
            //   两者混在一个数里时，"不在场 60 秒"看着像实现了间歇，
            //   而它其实**全部**来自开局那一次 ✗（第一版实测 60.10s / 4 轮 ⇒ 间隙 ≈ 0.1s ✓）
            if (ph == ShopTruck.Phase.Entering) _truckSeenOnce = true;
            if (ph == ShopTruck.Phase.Idle && _truckSeenOnce) ShopAwayBetweenSeconds += dt;

            if (ph == ShopTruck.Phase.Dock) ShopDockSeconds += dt;

            // ── ③ 车距停靠位：同一帧同时算"引擎现在摆在哪"与"旧算法会摆在哪" ──
            Vector2 atDock = ShopTruck.DockPoint * half;
            Vector2 truckPos = TruckPosForInteraction(truck, s) * half;
            Vector2 truckPosOld = ShopTruck.PointAt(truck.PhaseT(s.Clock.GameTime)) * half;
            if (ph == ShopTruck.Phase.Dock)
            {
                float offNow = (truckPos - atDock).magnitude;
                float offOld = (truckPosOld - atDock).magnitude;
                if (offNow > ShopTruckOffDockMax) ShopTruckOffDockMax = offNow;
                if (offOld > ShopTruckOffDockMaxOld) ShopTruckOffDockMaxOld = offOld;
            }

            // ── ④ "车停在停靠位"的连续时长（不管阶段，只要真的贴在那儿就算） ──
            //
            // ⚠️ **Idle 必须排除** ✗ —— `PhaseT` 在 Idle 下走的是"时长为 0 ⇒ 返回 1f"的分支
            //    （那是给动画采样用的保护值）⇒ `PointAt(1f)` 恰好 = `DockPoint` ✓
            //    于是"不在场的 60 秒"会被算成"停在停靠位 60 秒"✗
            //    （第一版就是这么翻车的：实测值 60.03s，正好是一整个周期长度 ✓）
            //    而 Idle 时模型是**隐藏**的 ⇒ 它根本不在场上，谈不上"停在哪" ✓
            if (ph != ShopTruck.Phase.Idle && (truckPos - atDock).magnitude <= AtDockEpsilon)
            {
                _atDockRun += dt;
                if (_atDockRun > ShopTruckAtDockLongest) ShopTruckAtDockLongest = _atDockRun;
            }
            else
            {
                _atDockRun = 0f;
            }

            // ── ⑤ 玩家可交互窗（引擎判定） ──
            bool open = ph == ShopTruck.Phase.Dock
                        && truck.CanShop((w.Player.Pos - truckPos).magnitude);
            if (open)
            {
                ShopOpenSeconds += dt;
                _shopOpenRun += dt;
                if (_shopOpenRun > ShopOpenLongest) ShopOpenLongest = _shopOpenRun;
                if (!_shopOpenPrev) ShopOpenWindows++;
            }
            else
            {
                _shopOpenRun = 0f;
            }
            _shopOpenPrev = open;
        }

        /// <summary>
        /// 车在"可交互判定"里用的位置（归一化）。
        /// ★ ★ 这是**唯一**一处观测侧的取位 ⇒ 引擎改什么入口，这里改一处就能跟上 ✓
        /// ⚠️ 它【必须】跟引擎用同一个入口 ✗ —— 否则"修好了"只是观测在自我安慰 ✓
        /// </summary>
        private static Vector2 TruckPosForInteraction(ShopTruck truck, RunSession s)
            => truck.SamplePoint(s.Clock);

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
            // ★ 必须抽稀 ★：`Observe` 每个固定步都被调用（60 次/秒），
            //   不抽稀的话这 30 秒就是 1800 行，把报告淹掉（第一版就是这么翻车的）。
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "## A · 死前 {0:0} 秒时间线（每秒 1 行，由 {1:0} 秒/步的采样抽稀；敌人按 近战/远程/跳跃/头目/小猪）",
                win, GameClock.FixedDt));
            sb.AppendLine("     t     血/上限    盾/上限   近 远 跳 头 猪  累计杀  弹药   金币  植株  玩家(x,y)         Bot决策");
            float nextRowAt = float.MinValue;
            for (int i = 0; i < Timeline.Count; i++)
            {
                Sample s = Timeline[i];
                if (s.T < from) continue;
                bool last = i == Timeline.Count - 1;
                if (!last && s.T < nextRowAt) continue;
                nextRowAt = s.T + RowInterval;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:0.0}  {1,5:0.0}/{2,-5:0.0} {3,4:0.0}/{4,-4:0.0}  {5,2} {6,2} {7,2} {8,2} {9,2}   {10,5}  {11,4}  {12,5}  {13,4}  ({14,5:0.0},{15,5:0.0})  {16}",
                    s.T, s.Hp, s.MaxHp, s.Shield, s.MaxShield,
                    s.Grunt, s.Shooter, s.Jumper, s.Boss, s.Pig,
                    s.Kills, s.WaterAmmo, s.Gold, s.Plants, s.X, s.Y, s.Decision));
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
            sb.AppendLine("   分钟   样本  平均等级  平均在场敌  平均血/上限   该分钟击杀  该分钟承伤   耕作决策占比    命中次数   单次均伤");
            sb.Append(PerMinute());
            sb.AppendLine();

            // ── D2. 承伤增长的来源分解 ──
            sb.AppendLine("## D2 · 承伤增长到底来自【挨得更多】还是【每下更疼】");
            sb.Append(GrowthSplit());
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

            // ── G. 商店车【实测】──
            sb.Append(ShopSummary());
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "──── 下游 ────  进店 {0} 次 ｜ 买种 {1} 份 ｜ 存活植株 {2} ｜ 收割 {3} 次 ｜ 金币 {4} ｜ 等级 {5} ｜ 死亡 {6:0.0}s",
                r.ShopVisits, r.SeedsBought, r.PlantsAlive, r.Harvests, r.Gold, r.Level, r.Duration));
            sb.AppendLine();

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  观测调用 {0} 次（采样 {1} 次）", ObserveCalls, Timeline.Count));
            return sb.ToString();
        }

        /// <summary>
        /// ★ 商店车【实测】摘要 ★ —— 与报告 G 段**同一份文本**（唯一实现），
        /// 供 `DebugApi.TruckReport` 直接打进控制台（长日志会被 Unity 截断，所以它是短的）✓
        /// </summary>
        public static string ShopSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## G · 商店车【实测】（用引擎自己的 `CanShop` / 取位入口量的，不是设计值）");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   一轮时长            均值 {0,6:0.00}s   最短 {1,6:0.00}s   最长 {2,6:0.00}s   （跑完 {3} 轮）",
                ShopCycles > 0 ? ShopCycleSecondsSum / ShopCycles : 0f,
                ShopCycleShortest == float.MaxValue ? 0f : ShopCycleShortest, ShopCycleLongest, ShopCycles));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   逐阶段实测累计      入场 {0,6:0.00}s  绕场 {1,6:0.00}s  停靠 {2,6:0.00}s  离场 {3,6:0.00}s",
                ShopEnterMeasured, ShopLoopMeasured, ShopDockMeasured, ShopLeaveMeasured));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   车【不在场上】累计  {0,7:0.00}s   占整局 {1,5:0.0}%   ← 0 = 常驻，>0 = 真的会开走",
                ShopAwaySeconds, ObservedSeconds > 0f ? ShopAwaySeconds / ObservedSeconds * 100f : 0f));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "     ├ 开局等待（首个周期锚在 +60s）  {0,6:0.00}s   （设计如此，不是间隙）",
                ShopAwaySeconds - ShopAwayBetweenSeconds));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "     └ 轮与轮之间的间隙  {0,6:0.00}s ÷ {1} 轮 = 每轮 {2:0.00}s   ← ★「不要常驻」的判据 ★",
                ShopAwayBetweenSeconds, ShopCycles,
                ShopCycles > 0 ? ShopAwayBetweenSeconds / ShopCycles : 0f));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   车停在停靠位(≤{0:0.0}m) 最长连续 {1,6:0.00}s   （设计值 {2:0.00}s）",
                AtDockEpsilon, ShopTruckAtDockLongest, ShopTruck.DockSeconds));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   停靠期车离停靠位最远 {0,7:0.00}m  ← 引擎现在的取位入口", ShopTruckOffDockMax));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "                        {0,7:0.00}m  ← 旧算法 `PointAt(PhaseT)` 同一帧的对照", ShopTruckOffDockMaxOld));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   `CanShop` 为真累计   {0,7:0.00}s   占 Dock 期的 {1,5:0.0}%",
                ShopOpenSeconds, ShopDockSeconds > 0f ? ShopOpenSeconds / ShopDockSeconds * 100f : 0f));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   可交互窗口个数       {0,7}", ShopOpenWindows));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   单窗最长连续可交互   {0,7:0.00}s   （设计值 {1:0.00}s）", ShopOpenLongest, ShopTruck.DockSeconds));
            sb.AppendLine("   ★ 判据：车停在停靠位的连续时长应 ≈ 10.00s（用户要求「停留 10 秒」）；");
            sb.AppendLine("           「车不在场上」应 > 0（用户要求「不要常驻」）；");
            sb.AppendLine("           「玩家可交互」是另一个量 —— 它同时要求玩家站在车旁 ✓");
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
                // ★ 这一分钟挨了几下、每下多大 ★ ——
                //   这是"承伤增长里【数量】占多少、【单次数值】占多少"的**唯一实测依据** ✗
                //   只报总伤害的话，两种成因在数字上长得一模一样 ✓
                int hits = 0; float hitSum = 0f;
                for (int i = 0; i < Hits.Count; i++)
                {
                    Hit h = Hits[i];
                    if (h.T < lo || h.T > hi) continue;
                    hits++; hitSum += h.Amount;
                }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,5}  {1,5}   {2,8:0.0}  {3,10:0.0}  {4,7:0.0}/{5,-5:0.0}  {6,9}  {7,10:0.0}  {8,11:0.0}%  {9,6}  {10,8:0.00}",
                    wv, n, lvl / (float)n, en / (float)n, hp / (float)n, mhp / (float)n,
                    killsLast - killsFirst, dmg, farmDec / n * 100f, hits,
                    hits > 0 ? hitSum / hits : 0f));
            }
            return sb.ToString();
        }

        /// <summary>
        /// ★ 把"承伤随分钟增长"拆成两个因子 ★：`承伤 = 命中次数 × 单次均伤`。
        /// 用对数份额分解（两个因子都是乘性的），得到各占多少百分比 ✓
        /// 它决定修法：次数占大头 ⇒ 该削**数量**曲线；单次伤害占大头 ⇒ 该削**伤害**曲线 ✓
        /// 两者对应的旋钮完全不同，而只看总伤害是分不出来的 ✗
        /// </summary>
        private static string GrowthSplit()
        {
            var sb = new StringBuilder();
            int maxWave = 0;
            for (int i = 0; i < Timeline.Count; i++) maxWave = Mathf.Max(maxWave, Timeline[i].Wave);

            // 取第一个完整分钟与最后一个有数据的分钟
            int first = -1, last = -1;
            for (int wv = 1; wv <= maxWave; wv++)
            {
                float lo = (wv - 1) * 60f, hi = wv * 60f;
                int c = 0;
                for (int i = 0; i < Hits.Count; i++) if (Hits[i].T >= lo && Hits[i].T <= hi) c++;
                if (c > 0) { if (first < 0) first = wv; last = wv; }
            }
            if (first < 0 || last == first)
            {
                sb.AppendLine("  （数据不足以分解）");
                return sb.ToString();
            }

            float d1 = DamageBetween((first - 1) * 60f, first * 60f);
            float dN = DamageBetween((last - 1) * 60f, last * 60f);
            int h1 = 0, hN = 0;
            for (int i = 0; i < Hits.Count; i++)
            {
                Hit h = Hits[i];
                if (h.T >= (first - 1) * 60f && h.T <= first * 60f) h1++;
                if (h.T >= (last - 1) * 60f && h.T <= last * 60f) hN++;
            }
            if (h1 <= 0 || hN <= 0 || d1 <= 0f || dN <= 0f) { sb.AppendLine("  （数据不足）"); return sb.ToString(); }

            float avg1 = d1 / h1, avgN = dN / hN;
            float rHits = hN / (float)h1, rAvg = avgN / avg1, rAll = dN / d1;

            double lHits = System.Math.Log(rHits), lAvg = System.Math.Log(rAvg), lAll = System.Math.Log(rAll);
            double shareHits = lAll != 0 ? lHits / lAll * 100.0 : 0.0;
            double shareAvg = lAll != 0 ? lAvg / lAll * 100.0 : 0.0;

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   第 {0} 分钟 → 第 {1} 分钟：承伤 {2:0.0} → {3:0.0}（×{4:0.00}）", first, last, d1, dN, rAll));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "     · 命中次数  {0} → {1} 下（×{2:0.00}）      ← 贡献 {3:0.0}% 的增长", h1, hN, rHits, shareHits));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "     · 单次均伤  {0:0.00} → {1:0.00}（×{2:0.00}）   ← 贡献 {3:0.0}% 的增长", avg1, avgN, rAvg, shareAvg));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "   ★ 判定：{0}",
                shareHits >= 60.0
                    ? "【数量】是主因 ⇒ 该削的是「同时在场上限 / 每拍刷新量」，不是伤害曲线"
                    : (shareAvg >= 60.0
                        ? "【单次伤害】是主因 ⇒ 该削的是 enemies.dmgStepA/B"
                        : "【数量】与【单次伤害】各占一半 ⇒ 单独动哪一个都只解决一半")));
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
