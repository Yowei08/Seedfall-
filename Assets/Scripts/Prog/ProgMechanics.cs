using System;
using UnityEngine;

namespace Seedfall
{
    /// <summary>★ v197：场景级天气（植物机制 4 / 5）。`None` = 常态。</summary>
    public enum WeatherKind { None, Sunny, Rain }

    /// <summary>
    /// ★★ v197：**新玩法机制的运行时**（植物 6 条 + 道具 10 条里"会动的那一半"）★★
    ///
    /// ── 为什么它与 `PlantMechanics` / `ItemRules` 分家 ──
    ///   那两个是**解释器 / 参数表**：只回答"这个名字是什么意思、系数是多少"，
    ///   一律纯函数、无状态（`PlantMechanics.GrowSpeedMult(def)` 这类）✓
    ///   而本文件装的是**会随时间变的东西**：剩余层数、爬升计时、冷却、天气倒计时
    ///   ⇒ 混进解释器就等于让纯函数带上状态（本工程反复吃亏的"函数里藏旋钮"）✗
    ///
    /// ── 为什么是 `static`（而不是 `RunSession` 上的一个实例字段）──
    ///   三个消费点离 `RunSession` 很远：
    ///     · `DamageResolver.Resolve`（`Combat.cs`）要问"这一下能不能被盾牌挡掉"、"怪物打没打到我"
    ///     · `World.Simulate`（`Actors.cs`）要问"现在是不是大晴天"
    ///     · `WorldView`（表现层）要问"胡萝卜在哪、兔子在哪、要不要出太阳/下雨"
    ///   让它们各自持有 `RunSession` 引用 = 三条新耦合边；而本作**同时只有一个玩家**
    ///   （`EnemyDef.PlayerHpUnit` 也是同一个前提）⇒ 静态状态 + **每局 `Reset()`** 是诚实的做法 ✓
    ///   ⚠️ 代价写清楚：自检夹具会在同一进程里 `new RunSession()` 很多次 ⇒
    ///      隔离**只能**靠 `Start()` 里那一处 `Reset()`；漏了它就是"上一局的格挡冷却带到下一局" ✓
    ///
    /// ── 每条机制的三件事（触发 / 效果 / 消费点）都写在下面各自的注释里 ──
    /// </summary>
    public static class ProgMechanics
    {
        // ═══════════════════════════════════════════════════════════════════
        // 一、天气（植物机制 4 大晴天 / 5 雨天）
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>当前天气。表现层只读它（太阳 / 下雨两段由 `WorldView` 画）。</summary>
        public static WeatherKind Weather { get; private set; } = WeatherKind.None;
        /// <summary>当前天气还剩几秒（0 = 没有天气）。</summary>
        public static float WeatherRemain { get; private set; }
        /// <summary>本局触发过几次天气（探针 / 自检读数用）。</summary>
        public static int WeatherCount { get; private set; }
        /// <summary>最近一次天气是哪个品种触发的（诊断用）。</summary>
        public static string WeatherSourceId { get; private set; } = "";

        /// <summary>大晴天时全场生长速度的倍率（`World.Simulate` 里的**唯一**乘点）。</summary>
        public static float GrowMult => Weather == WeatherKind.Sunny ? PlantMechanics.SunnyGrowMult : 1f;

        /// <summary>天气已经持续了几秒（表现层淡入用；`DebugForceWeather` 也靠它算强度）。</summary>
        private static float _weatherAge;

        /// <summary>天气强度 0..1（表现层用它淡入淡出太阳 / 雨幕）。</summary>
        public static float WeatherIntensity01
        {
            get
            {
                if (Weather == WeatherKind.None) return 0f;
                // ★ 用 `_weatherAge` 而不是 `total − Remain` ★
                //   旧写法在"调试强制一个比标准时长更长的天气"时会算出**负的 age**
                //   ⇒ `fadeIn = 0` ⇒ 强度恒 0 ⇒ 太阳与雨幕**一个像素都不画** ✗
                //   （实测：`DebugForceWeather(Sunny, 600)` 抓出来是一张晴天但**没有太阳**的图）
                float fadeIn = Mathf.Clamp01(_weatherAge / 0.8f);
                float fadeOut = Mathf.Clamp01(WeatherRemain / 0.8f);
                return Mathf.Min(fadeIn, fadeOut);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // 一·五、v253 两条**有记性**的植物机制（连收递增 / 猎杀亢奋）
        //
        // 为什么它们在本文件而不在 `PlantMechanics`：
        //   那两个"账本"（这一格上次什么时候收的 / 上一次击杀是几秒前）是**随时间变的**，
        //   而 `PlantMechanics` 的契约是**纯函数**（见本文件头的分工说明）✓
        //   算式仍在 `PlantMechanics`（`StreakGoldMult` / `KillRushAtkMult`），这里只记账 ✓
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>`StatId.AtkMult` 上那条修正的来源键（"先撤再写"按它撤）。</summary>
        public const string KillRushSrc = "prog:killrush";

        /// <summary>猎杀亢奋剩余秒数（0 = 没在生效）。</summary>
        public static float KillRushRemain { get; private set; }
        /// <summary>本局触发过几轮猎杀亢奋（探针读数；同一轮里连杀不重复计数）。</summary>
        public static int KillRushCount { get; private set; }

        /// <summary>每格的连收层数（key = `FarmPlot.Index`）。</summary>
        private static readonly System.Collections.Generic.Dictionary<int, int> _streakStacks
            = new System.Collections.Generic.Dictionary<int, int>();
        /// <summary>每格上一次被收获的**世界时刻**（`World.Time`，秒）。</summary>
        private static readonly System.Collections.Generic.Dictionary<int, float> _streakTime
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>
        /// ★ v253 机制 2：**连收递增的账本**（`World.HarvestPlot` 里每收一格调一次）★
        ///
        /// 规则：两次收获间隔 ≤ `该品种成熟时间 × StreakWindowMult` ⇒ 层数 +1（封顶 6），
        ///       超出即从 1 层重算。倍率交给 `PlantMechanics.StreakGoldMult(层数)` ✓
        ///
        /// ★ 为什么"按格记账 + 按时间判连续"，而不是"按次数" ★
        ///   按次数的话，玩家隔十分钟回来收一次也能续上 —— 那不是"连收"，是"收得慢" ✓
        ///   按时间判才能表达玩家看到的那件事：**熟了就收、一直收，收益才涨** ✓
        /// ★ 为什么没有这条机制的株要顺手 `Remove` 记账 ★
        ///   同一格换种（含换成有这条机制的株）时，旧层的时长必须作废，
        ///   否则"上一株留下的时间戳"会让新株一落地就带着旧层数 ✓
        /// </summary>
        public static float PlantStreakGoldMult(FarmPlot plot, SeedDef def, float worldTime)
        {
            if (plot == null || def == null) return 1f;
            int idx = plot.Index;
            if (!PlantMechanics.Has(def, PlantMechanics.StreakHarvest))
            {
                _streakStacks.Remove(idx);
                _streakTime.Remove(idx);
                return 1f;
            }
            int stacks = 1;
            if (_streakStacks.TryGetValue(idx, out int prev)
                && _streakTime.TryGetValue(idx, out float last)
                && worldTime - last <= def.GrowTime * PlantMechanics.StreakWindowMult)
                stacks = Mathf.Min(PlantMechanics.StreakMaxStacks, prev + 1);
            _streakStacks[idx] = stacks;
            _streakTime[idx] = worldTime;
            return PlantMechanics.StreakGoldMult(stacks);
        }

        /// <summary>场上有没有带某条机制的植物（有 `CropDef` 的格才算）。</summary>
        public static bool HasFarmMechanic(World w, string mechanic)
        {
            if (w == null || w.Farm == null) return false;
            FarmPlot[] slots = w.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || p.CropDef == null) continue;
                if (PlantMechanics.Has(p.CropDef, mechanic)) return true;
            }
            return false;
        }

        /// <summary>
        /// ★ v253 机制 4：**击杀即亢奋**（`World.Cleanup` 的击杀分支里**唯一**调用点）★
        ///
        /// 场上没有 `KillRush` 植物时**连计时都不开**（不是"开了再乘 1"）——
        /// 否则探针会读到"没种这株却有一轮 buff"，而玩家看到的是没有效果，两者会互相矛盾 ✓
        /// </summary>
        public static void OnEnemyKilled(World w)
        {
            if (!HasFarmMechanic(w, PlantMechanics.KillRush)) return;
            bool fresh = KillRushRemain <= 0f;
            KillRushRemain = PlantMechanics.KillRushSeconds;
            if (fresh) KillRushCount++;
        }

        // ═══════════════════════════════════════════════════════════════════
        // 二、胡萝卜环绕（道具 1）—— 上限 3、等边三角形、挡敌人飞行物
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>最多几根（= 这件道具的 `maxStack`，也是"上限 3"的唯一出处）。</summary>
        public const int MaxCarrots = 3;
        /// <summary>环绕半径（米）。</summary>
        public const float CarrotRadius = 1.55f;
        /// <summary>每根胡萝卜的拦截半径（米）。</summary>
        public const float CarrotBlockRadius = 0.60f;
        /// <summary>环绕角速度（度/秒）—— 等边三角形整体转动，间距永远是 120°。</summary>
        public const float CarrotSpinDegPerSec = 110f;
        /// <summary>本局被胡萝卜拦下的飞行物数量（探针读数）。</summary>
        public static int CarrotBlockedCount { get; private set; }

        private static float _carrotAngle;

        /// <summary>玩家身上有几根胡萝卜。</summary>
        public static int CarrotCount(int stacks) => Mathf.Clamp(stacks, 0, MaxCarrots);

        /// <summary>第 i 根胡萝卜的世界坐标（`count` 根时按 360°/count 均分 = 等边三角形）。</summary>
        public static Vector2 CarrotPos(Vector2 player, int i, int count)
        {
            if (count <= 0) return player;
            float step = 360f / count;
            float rad = (_carrotAngle + step * i) * Mathf.Deg2Rad;
            return player + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * CarrotRadius;
        }

        // ═══════════════════════════════════════════════════════════════════
        // 三、兔子宠物（道具 2）—— 自动追最近目标，攻击力 = 玩家最大生命 10%
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>兔子当前世界坐标（没有兔子时等于玩家位置）。</summary>
        public static Vector2 RabbitPos { get; private set; }
        /// <summary>兔子是否在场上。</summary>
        public static bool RabbitActive { get; private set; }
        /// <summary>出手间隔（秒）。</summary>
        public const float RabbitInterval = 0.9f;
        /// <summary>移动速度（米/秒）——比玩家快一点，否则永远追不上。</summary>
        public const float RabbitSpeed = 7.2f;
        /// <summary>一口能够到的距离（米）。</summary>
        public const float RabbitReach = 1.9f;
        /// <summary>攻击力 = 玩家**最大生命**的 10%（用户原话）。</summary>
        public const float RabbitDamagePctOfMaxHp = 0.10f;
        /// <summary>本局兔子咬过几口（探针读数）。</summary>
        public static int RabbitHits { get; private set; }

        private static float _rabbitCd;

        // ═══════════════════════════════════════════════════════════════════
        // 四、两条"每秒爬升、受伤重置"（道具 5 攻速 / 6 伤害倍率）
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>每秒爬升的百分点（用户原话"+2%"）。</summary>
        public const float RampPerSecond = 2f;
        /// <summary>爬升封顶秒数（60 秒 = +120%；再高没有意义，攻速本身有 caps）。</summary>
        public const float RampCapSeconds = 60f;

        /// <summary>攻速爬升攒了几秒（受伤清零）。</summary>
        public static float AtkSpeedRampSeconds { get; private set; }
        /// <summary>伤害倍率爬升攒了几秒（受伤清零）。</summary>
        public static float DamageRampSeconds { get; private set; }

        /// <summary>
        /// ★★ v198：**爬升的唯一算式** —— "+2%/秒，硬顶 `RampCapSeconds`" ★★
        ///
        /// 抽成纯函数是为了让"5 / 10 / 20 分钟到底多少"这个问题**可被探针直接问**，
        /// 而不是由探针再写一遍 `min(60, t) × 2`（那就是第二份实现 ——
        /// 本工程反复吃过"两处各写一份、改了一处"的亏）✓
        /// 玩法侧的调用点仍是下面两个属性，它们只是把**当前秒数**喂进来 ✓
        ///
        /// ★ 这条硬顶就是"上限从 1 变无限"之后数值**不会爆**的原因 ★
        ///   `maxStack` 只影响 `Tick` 里的 `stacks > 0` 这一个布尔 ⇒
        ///   爬升的**速率与终点与层数无关**，任何时刻都是 `min(60, t) × 2` ⇒ 最大 +120% ✓
        ///   （真正的副作用在 `costs` 一侧，见 `tuning.json` 的 `_note_v198_unlimited`）
        /// </summary>
        public static float RampPctAt(float seconds)
            => Mathf.Min(RampCapSeconds, Mathf.Max(0f, seconds)) * RampPerSecond;

        /// <summary>同一个算式，换算成 `ModOp.Mul` 的**乘子**（1 + pct/100）。</summary>
        public static float RampMultAt(float seconds) => 1f + RampPctAt(seconds) / 100f;

        public static float AtkSpeedRampPct => RampPctAt(AtkSpeedRampSeconds);
        public static float DamageRampPct => RampPctAt(DamageRampSeconds);

        // ═══════════════════════════════════════════════════════════════════
        // 五、盾牌格挡（道具 7）—— 挡一次伤害，触发后 30 秒 CD
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>格挡冷却剩余（秒）。0 = 可用。</summary>
        public static float BlockShieldCd { get; private set; }
        /// <summary>本局格挡掉过几次伤害（探针读数）。</summary>
        public static int BlockShieldUsed { get; private set; }
        /// <summary>格挡表现脉冲剩余（秒）——表现层画一圈扩散的盾环，逻辑层不读它做判断。</summary>
        public static float BlockShieldPulse;

        /// <summary>
        /// ★ 伤害漏斗里问的唯一一句话：**这一下能不能被盾牌挡掉** ★
        /// 返回 true = 已挡掉（调用方必须当作"打空"：不再扣血/盾、不再弹红蓝字）。
        ///
        /// `Stacks` / `CdMax` 由 `Tick` 每帧从 `ItemPanel` 写进来 —— 之所以不在这里
        /// 现查道具表，是因为 `DamageResolver.Resolve` 拿着的是 `PlayerActor`，
        /// 而**道具层数挂在 `RunSession` 上**（`PlayerActor` 不持有会话）。
        /// 与其给 `PlayerActor` 加一条"回指会话"的反向引用，不如每帧缓存两个数 ✓
        /// </summary>
        public static int BlockShieldStacks;
        public static float BlockShieldCdMax;

        public static bool TryBlockPlayerHit()
        {
            if (BlockShieldStacks <= 0) return false;
            if (BlockShieldCd > 0f) return false;
            BlockShieldCd = Mathf.Max(1f, BlockShieldCdMax);
            BlockShieldUsed++;
            BlockShieldPulse = 0.45f;
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // 六、富豪（道具 10）—— 1% 金币取整转额外伤害
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ 额外伤害公式（用户原话"每次攻击把 1% 金币取整转为额外伤害"，并追加"**增加上限 29.9 点伤害**"）★
        ///     `extra = min(29.9, Round(Gold × 0.01))`
        /// 例：金币 25 ⇒ 0.25 ⇒ **0**（早期 1% 还不到 1 点，这是对的）；
        ///     150 ⇒ 1.5 ⇒ **2**；2000 ⇒ 20 ⇒ 20；**3000 ⇒ 30 ⇒ 钳到 29.9** ✓
        /// ★ 只钳**这一项**，不钳总伤害 ★ —— 用户明确要求；
        ///   总伤害仍走 `(基础+伤害)×倍率×暴击`，这里只是往 `RawValue` 上加一个**有上限**的加数 ✓
        /// ★ 为什么是加法不是乘法 ★
        ///   乘法会叠进已有的三层乘区 ⇒ 后期指数级放大 ✗ 加法只加一个点数，上限可读 ✓
        /// </summary>
        public const float GoldExtraDamageCap = 29.9f;

        public static float GoldExtraDamage(int gold, int stacks)
        {
            if (stacks <= 0 || gold <= 0) return 0f;
            return Mathf.Min(GoldExtraDamageCap, Mathf.Max(0, Mathf.RoundToInt(gold * 0.01f)));
        }

        // ═══════════════════════════════════════════════════════════════════
        // 重置 / 每帧推进
        // ═══════════════════════════════════════════════════════════════════

        private static int _lastMinute = -1;
        private static float _rainTopUpAcc;
        /// <summary>`caps.dodge` 的**基值**（第一次写覆盖之前抓住；另一路把它设成 0.6）。</summary>
        private static float _dodgeCapBase = -1f;

        /// <summary>
        /// ★★ 把两件"改上限"的道具写进**它们真正住的地方** ★★
        ///
        /// · **闪避上限 70%** → **全局** `Tuning.caps.dodge`（用户纠正后确认的口径：
        ///   "闪避修改就是全局，拥有道具的时候就锁在 70%"）⇒ 拥有写 `ruleValue/100`，
        ///   没有则**回落基值** ✓
        ///   ⚠️ 基值（0.6）由另一路设在 `tuning.json`，本文件**不写死** ——
        ///     第一次调用时抓一次，`Reset()` 时把基值写回去 ⇒ 不会把覆盖值当基值抓进来 ✓
        /// · **攻速上限 −200%** → `StatBlock.AtkSpeedCapFixed*`（不是下限；见 `Stats.cs` 的注释）
        ///
        /// 每帧调用（幂等，写同一个值不会产生垃圾）✓
        /// </summary>
        public static void ApplyCapsToTable(RunSession s)
        {
            if (s == null) return;
            ItemPanel p = ItemPanel.Of(s);

            CapsDef caps = TableDatabase.Tuning != null ? TableDatabase.Tuning.caps : null;
            if (caps != null)
            {
                if (_dodgeCapBase < 0f) _dodgeCapBase = caps.dodge;
                caps.dodge = p.OwnsDodgeCapUp ? p.DodgeCapTarget : _dodgeCapBase;
            }

            // ★ v212（用户原话："那个修改攻速上限为-200%的道具改为攻速-200%而不是上限，
            //   同时附加效果攻击范围+99%"）★★
            //
            // 旧行为：`gamble_glove`（`crit_trade`）把**攻速的上限**压到 −200% ⇒
            //   这是"改规则"，玩家看到的是「攻速上限 −200%」，而**实际攻速并没有被改** ✗
            //   更糟的是：上限一旦压死，**其它所有攻速增益都彻底无效**（这也是它变成"废卡"的原因）
            //
            // 新行为：**不再动上限** ⇒ 攻速 −200% 作为**真实效果**写进 `items.json` 的 `effects`
            //   （`AtkSpeed Percent -200`），与攻击范围 +99%（`AtkRange Percent 99`）并列 ✓
            //   · 好处 ① 玩家看到的就是它真正做的事 ✓
            //   · 好处 ② 上限不再被绑死 ⇒ 别的攻速道具仍能生效、两者可叠加 ✓
            //   · 消费点仍有 `Mathf.Max(0.05f, atkSpeed)` 兜底 ⇒ 负攻速不会让游戏卡死 ✓
            // ★ v3xx 更新（只改数值，机制口径不变）：上面那套 −200%/+99% 的数值已被用户整条替换
            //   ⇒ 现在的四条是 `MoveSpeed Percent −9.9` / `AtkSpeed Percent −9.9` /
            //     `CritRate Add 0.099` / `CritDmg Percent 99`（见 `items.json` 的 gamble_glove）✓
            //   攻速 −9.9% 于是不再走"下限兜底"那条路（1.0 × 0.901 = 0.901，间隔 ×1.110）✓
            //
            // ⚠️ 只删"设上限"这两行；`ResetRuntimeOverrides()` 在 else 分支保留 ——
            //   它是给别的规则用的，不是这条的专属收尾 ✓
            // 若日后要恢复"改规则"那种设计，请作为**新机制**另建，不要再复用 `crit_trade` ✓
            StatBlock.ResetRuntimeOverrides();
        }

        /// <summary>
        /// 新一局开始（`RunSession.Start` 里**唯一**调用点）。
        /// 不清的话上一局的格挡冷却 / 爬升 / 兔子位置会带到下一局 ✓
        /// </summary>
        public static void Reset()
        {
            // 先把全局闪避上限还原成基值（否则下一次抓基值会抓到上一局的 0.7）
            if (_dodgeCapBase > 0f && TableDatabase.Tuning != null && TableDatabase.Tuning.caps != null)
                TableDatabase.Tuning.caps.dodge = _dodgeCapBase;
            StatBlock.ResetRuntimeOverrides();

            Weather = WeatherKind.None;
            WeatherRemain = 0f;
            WeatherCount = 0;
            WeatherSourceId = "";
            _carrotAngle = 0f;
            CarrotBlockedCount = 0;
            RabbitPos = Vector2.zero;
            RabbitActive = false;
            RabbitHits = 0;
            _rabbitCd = 0f;
            AtkSpeedRampSeconds = 0f;
            DamageRampSeconds = 0f;
            BlockShieldCd = 0f;
            BlockShieldUsed = 0;
            BlockShieldPulse = 0f;
            _lastMinute = -1;
            _rainTopUpAcc = 0f;
            // v253：两条有记性的机制也必须清（否则"上一局的连收层数/亢奋倒计时"会带进下一局）
            KillRushRemain = 0f;
            KillRushCount = 0;
            _streakStacks.Clear();
            _streakTime.Clear();
        }

        /// <summary>
        /// 每帧推进（`RunSession.FixedTick` 里**唯一**调用点，紧跟 `RunTime += dt`）。
        /// 顺序有讲究：先推进计时器、再结算"到点该发生什么" ——
        /// 反过来会让第 1 帧就多掷一次天气骰 ✓
        /// </summary>
        public static void Tick(RunSession s, float dt)
        {
            if (s == null || s.World == null || dt <= 0f) return;
            ItemPanel p = ItemPanel.Of(s);
            PlayerActor player = s.World.Player;

            // ① 天气倒计时
            if (Weather != WeatherKind.None)
            {
                _weatherAge += dt;
                WeatherRemain -= dt;
                if (WeatherRemain <= 0f) { Weather = WeatherKind.None; WeatherRemain = 0f; _weatherAge = 0f; }
            }

            // ② 每分钟整点掷一次天气（植物机制 4 / 5）
            int minute = s.Clock.MinuteIndex;
            if (minute != _lastMinute)
            {
                _lastMinute = minute;
                if (minute > 0) RollWeather(s);
            }

            // ③ 雨天：每 0.5 秒把全场耕地补满（让"下雨"在 30 秒里持续成立）
            if (Weather == WeatherKind.Rain)
            {
                _rainTopUpAcc += dt;
                if (_rainTopUpAcc >= 0.5f) { _rainTopUpAcc = 0f; RefillAllPlots(s); }
            }
            else _rainTopUpAcc = 0f;

            // ④ 胡萝卜环绕 + 拦截敌弹
            int carrots = CarrotCount(p.CarrotStacks);
            _carrotAngle = Mathf.Repeat(_carrotAngle + CarrotSpinDegPerSec * dt, 360f);
            if (carrots > 0 && player != null) BlockProjectiles(s, player.Pos, carrots);

            // ⑤ 兔子
            TickRabbit(s, dt);

            // ⑥ 两条爬升（受伤已在 `OnPlayerDamaged` 清零）
            AtkSpeedRampSeconds = p.AtkSpeedRampStacks > 0 ? AtkSpeedRampSeconds + dt : 0f;
            DamageRampSeconds = p.DamageRampStacks > 0 ? DamageRampSeconds + dt : 0f;

            // ⑦ 格挡冷却 / 脉冲
            BlockShieldStacks = p.BlockShieldStacks;
            BlockShieldCdMax = p.BlockShieldCdSeconds;
            if (BlockShieldCd > 0f) BlockShieldCd = Mathf.Max(0f, BlockShieldCd - dt);
            if (BlockShieldPulse > 0f) BlockShieldPulse = Mathf.Max(0f, BlockShieldPulse - dt);

            // ⑧ 两件"改上限"的道具 → 写进 `caps.dodge` / `StatBlock.AtkSpeedCapFixed`
            ApplyCapsToTable(s);

            // ⑧b v253：猎杀亢奋倒计时（`OnEnemyKilled` 点火、这里只递减）
            if (KillRushRemain > 0f) KillRushRemain = Mathf.Max(0f, KillRushRemain - dt);

            // ⑨ 把"随时间 / 随存在变化"的三条写进属性（幂等：先撤再写）
            SyncRuntimeStats(s);
        }

        /// <summary>玩家受伤（`DamageResolver.Resolve` 里的唯一调用点）：两条爬升清零。</summary>
        public static void OnPlayerDamaged()
        {
            AtkSpeedRampSeconds = 0f;
            DamageRampSeconds = 0f;
        }

        // ═══════════════════════════════════════════════════════════════════
        // 天气掷骰
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每分钟整点：场上每有一株"天气类"植物各掷一次。
        /// 触发率 = `10% + 5% × (等级−1)`（用户原话"每分钟整点 10% 触发…每级 +5%"）。
        ///
        /// ★ 为什么用 `Rng.Fork` 而不是 `World.Rng` ★
        ///   直接消费主流会让每帧的敌人 AI / 刷怪序列整体错位 ——
        ///   "加了一个机制，顺手改了整局的随机序列"，无法归因 ✗
        ///   `Fork` 的契约就是"派生独立流、不影响本流"（见 `Pcg32.Fork`）✓
        /// </summary>
        private static void RollWeather(RunSession s)
        {
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot plot = slots[i];
                if (plot == null || plot.CropDef == null) continue;
                SeedDef def = plot.CropDef;
                bool sunny = PlantMechanics.Has(def, PlantMechanics.SunnyBloom);
                bool rain = PlantMechanics.Has(def, PlantMechanics.RainCloud);
                if (!sunny && !rain) continue;

                float chance = PlantMechanics.WeatherChance(plot.Level);
                Pcg32 roll = s.World.Rng.Fork((ulong)(0x7000 + i));
                if (!roll.Chance(chance)) continue;

                Weather = sunny ? WeatherKind.Sunny : WeatherKind.Rain;
                WeatherRemain = PlantMechanics.WeatherSeconds;
                _weatherAge = 0f;      // 每次触发都从头淡入（与 `DebugForceWeather` 同一口径）
                WeatherCount++;
                WeatherSourceId = def.Id;
                if (Weather == WeatherKind.Rain) RefillAllPlots(s);
                Bus.Publish(new FloatingTextEvent
                {
                    Pos = plot.World,
                    Text = sunny ? "大晴天！" : "下雨了！",
                    Kind = PopupKind.PlantMature,
                    // ★ 本轮（拉平字号）：1.2 → **1.0** ⇒ 20px ★
                    //   ── 这一处是"隐藏的 30px 档"，必须一起收 ──
                    //   它借的是 `PopupKind.PlantMature`，而上屏乘积 = 26 × 0.769 × 该档 × 本值：
                    //     改前 = 1.25 × 1.2 = **1.50 ⇒ 30px**，与 `PopupKind.Bomb` 的 30px **并列最大**
                    //       （用户："bomb 唯一最大"被它悄悄破掉了 —— 它不是暴击、也不是 bomb）✗
                    //     只把 `PlantMature` 降到 1.00 而不动这里 = 1.00 × 1.2 = 1.20 ⇒ **24px**
                    //       仍然 > 20px ⇒ 还是反超"暴击以外一律 ≤ 20"这条约束 ✗
                    //   ⇒ 两处都要收：档位 1.25→1.00（`Actors.PopupStyle`）、本值 1.2→1.0 ✓
                    //   可接受性：天气弹字与「成熟」**同号 20px** —— 它俩本来就是一个 kind，
                    //     文案（「大晴天！/下雨了！」）与发布时间（一分钟至多一次）足以区分 ✓
                    Scale = 1.0f,
                });
                return;     // 一分钟只触发一种天气
            }
        }

        /// <summary>
        /// ★ 调试/验收用：直接把天气切到指定档位（探针与抓图用，**不进任何玩法路径**）。
        /// 存在的理由：天气本来由"每分钟整点掷 10%×等级"决定，抓一张"雨天"的图
        /// 平均要等 10 分钟 ⇒ 没有这个入口，第五条证据根本无法稳定复现 ✓
        /// </summary>
        public static void DebugForceWeather(WeatherKind kind, float seconds = -1f)
        {
            Weather = kind;
            WeatherRemain = kind == WeatherKind.None ? 0f
                          : (seconds > 0f ? seconds : PlantMechanics.WeatherSeconds);
            _weatherAge = 0f;      // ★ 从 0 起算 ⇒ 淡入从 0 开始（不再是"负 age ⇒ 强度恒 0"）
            if (kind != WeatherKind.None) WeatherCount++;
            WeatherSourceId = "debug";
        }

        /// <summary>把全场耕地灌满水（雨天效果；`FarmPlot.AddWater` 是唯一入口）。</summary>
        private static void RefillAllPlots(RunSession s)
        {
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || !p.Unlocked) continue;
                if (p.State != PlotState.Growing && p.State != PlotState.Seeded) continue;
                int need = Mathf.Max(0, p.WaterMax - p.Water);
                if (need > 0) p.AddWater(need);
                p.NeedFullRefill = false;
                p.ThirstyNotified = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // 胡萝卜拦截
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 胡萝卜挡敌人飞行物：任何 `OwnerTeam == Enemy` 的弹丸进入任一根胡萝卜的
        /// `CarrotBlockRadius` 就被消掉（`Dead = true`，由 `World` 的常规清扫回收）。
        /// ★ 为什么只挡**敌方**飞行物 ★ 玩家自己的弹道（水球插件）也走同一个列表，
        ///   挡自己的弹等于给玩家上减益 ✗
        /// </summary>
        private static void BlockProjectiles(RunSession s, Vector2 playerPos, int carrots)
        {
            System.Collections.Generic.List<Projectile> list = s.World.Projectiles;
            for (int i = 0; i < list.Count; i++)
            {
                Projectile pr = list[i];
                if (pr == null || pr.Dead || pr.OwnerTeam != Team.Enemy) continue;
                for (int k = 0; k < carrots; k++)
                {
                    Vector2 cp = CarrotPos(playerPos, k, carrots);
                    float rr = CarrotBlockRadius + pr.Radius;
                    if ((pr.Pos - cp).sqrMagnitude > rr * rr) continue;
                    pr.Dead = true;
                    CarrotBlockedCount++;
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = pr.Pos, Text = "格挡", Kind = PopupKind.Evade, Scale = 0.9f
                    });
                    break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // 兔子
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 兔子：**自动追最近目标**，够得着就每 `RabbitInterval` 秒咬一口，
        /// 伤害 = 玩家**最大生命**的 10%。
        ///
        /// ★ 为什么伤害走 `DamageResolver.Resolve` 而不是直接 `Health.ApplyDamage` ★
        ///   直扣会绕过闪避 / 无敌帧 / 弹字 / 击杀结算四条规则 ⇒
        ///   "一只兔子绕过了整个伤害漏斗" ✗ 走漏斗之后弹字、击杀计数、Boss 结算自动跟上 ✓
        /// </summary>
        private static void TickRabbit(RunSession s, float dt)
        {
            World w = s.World;
            PlayerActor player = w.Player;
            RabbitActive = ItemPanel.Of(s).RabbitStacks > 0;
            if (player == null) { RabbitActive = false; return; }
            if (!RabbitActive) return;

            if (RabbitPos.sqrMagnitude < 0.0001f) RabbitPos = player.Pos;

            EnemyActor target = null;
            float best = float.MaxValue;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead) continue;
                float d = (e.Pos - RabbitPos).sqrMagnitude;
                if (d < best) { best = d; target = e; }
            }

            Vector2 want = target != null ? target.Pos : player.Pos + new Vector2(-0.9f, 0.9f);
            Vector2 delta = want - RabbitPos;
            float dist = delta.magnitude;
            if (dist > 0.001f) RabbitPos += delta / dist * Mathf.Min(dist, RabbitSpeed * dt);

            if (_rabbitCd > 0f) _rabbitCd = Mathf.Max(0f, _rabbitCd - dt);
            if (target == null || _rabbitCd > 0f) return;
            if ((target.Pos - RabbitPos).magnitude > RabbitReach + target.Radius) return;

            _rabbitCd = RabbitInterval;
            RabbitHits++;
            DamageResolver.Resolve(new DamageRequest
            {
                Type = DamageType.Physical,
                RawValue = Mathf.Max(1f, player.Health.MaxHp * RabbitDamagePctOfMaxHp),
                Source = player,
                Target = target,
            }, w);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 每帧要从"道具层数"读到的问句 —— 集中成一张快照
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把"10 件机制道具现在各几层"读成一个**值快照**。
        /// ★ 为什么要有这一层 ★ `ItemStacks` 是 `Dictionary&lt;string,int&gt;`，
        ///   每帧查 10 次会把"这次改动到底读了哪几件"散到十个地方；
        ///   集中之后"漏读一件"在**一个地方**看得见 ✓
        /// </summary>
        public struct ItemPanel
        {
            public int CarrotStacks, RabbitStacks, BlockShieldStacks, WaterChargeStacks;
            public int AtkSpeedRampStacks, DamageRampStacks, CritTradeStacks;
            public int SharpWaterStacks, GoldDamageStacks, DodgeCapStacks;
            /// <summary>盾牌格挡的冷却秒数（表里 `ruleValue`，**不随层数变**）。</summary>
            public float BlockShieldCdSeconds;
            /// <summary>水壶充能上限的每层点数（表里 `ruleValue`，兜底 20）。</summary>
            public int WaterChargePerLayer;
            /// <summary>是否拥有"闪避上限 70%"那件（拥有 ⇒ 全局 `caps.dodge` 写 0.7，见 `ApplyDodgeCapToTable`）。</summary>
            public bool OwnsDodgeCapUp;
            /// <summary>闪避上限的目标值（0.7 = 70%）。</summary>
            public float DodgeCapTarget;
            /// <summary>
            /// 是否拥有「赌命手套」（`rule: crit_trade`）。
            ///
            /// ⚠️ **它当前没有任何消费点，这是有意的** —— v212 起这件道具的效果**全部**写在
            ///   `items.json` 的 `effects` 里，由 `RunSession.WriteBonus` 统一写入。
            ///   ★ v3xx 的数值（用户原话："移动速度降低 9.9%，攻速降低 9.9%，暴击率上涨 9.9%，
            ///     暴击伤害增加 99%"）⇒ 四条是 `MoveSpeed Percent −9.9` / `AtkSpeed Percent −9.9` /
            ///     `CritRate Add 0.099`（表里 `CritRate` 是 0..1 单位 ⇒ +9.9 个百分点）/ `CritDmg Percent 99`。
            ///   ⚠️ 旧的 `CritRate +0.99` / `CritDmg +999%` / `AtkSpeed −200%` / `AtkRange +99%` 已全部删除
            ///     —— 若在别处看到那组数字，那是历史注释，不是现行数据 ✗
            ///   `rule` 只剩一个**登记**作用：类别 3 必须 `HasRule` 才能过 `TableDatabase.Validate`
            ///   （见 `TableDatabase.cs:517`），而 `ItemRules.IsKnown` 读的是 `ItemRules.All`。
            /// ⇒ 别照旧注释去接线"攻速上限"：`ApplyCapsToTable` 已经**不再**写
            ///   `StatBlock.AtkSpeedCapFixed*`，谁把上限压到 −200%，谁就会连带废掉其它所有攻速增益。
            /// </summary>
            public bool OwnsCritTrade;

            public static ItemPanel Of(RunSession s)
            {
                var p = default(ItemPanel);
                if (s == null) return p;
                p.CarrotStacks = StacksOf(s, ProgRules.CarrotOrbit);
                p.RabbitStacks = StacksOf(s, ProgRules.RabbitPet);
                p.BlockShieldStacks = StacksOf(s, ProgRules.BlockShield);
                p.WaterChargeStacks = StacksOf(s, ProgRules.WaterChargeUp);
                p.AtkSpeedRampStacks = StacksOf(s, ProgRules.AtkSpeedRamp);
                p.DamageRampStacks = StacksOf(s, ProgRules.DamageRamp);
                p.CritTradeStacks = StacksOf(s, ProgRules.CritTrade);
                p.SharpWaterStacks = StacksOf(s, ProgRules.SharpWater);
                p.GoldDamageStacks = StacksOf(s, ProgRules.GoldDamage);
                p.DodgeCapStacks = StacksOf(s, ProgRules.DodgeCapUp);

                // ★ 这两条读的是**表里那一行的 ruleValue**，不是 `ItemRule()` 的累加值 ★
                //   `ItemRule` = Σ(ruleValue × 层数) —— 对"刷新折扣"那种线性机制是对的，
                //   而"冷却 30 秒"叠 6 层变成 180 秒就是错的（层数只该提高**覆盖率**）✓
                p.BlockShieldCdSeconds = Mathf.Max(1f, RowRuleValue(ProgRules.BlockShield, 30f));
                float perLayer = RowRuleValue(ProgRules.WaterChargeUp, WaterChargePerLayerDefault);
                p.WaterChargePerLayer = perLayer > 0f ? Mathf.RoundToInt(perLayer) : WaterChargePerLayerDefault;

                // ★ 闪避上限 70%：**就是改全局 `caps.dodge`**（用户纠正后的口径）★
                //   拥有 ⇒ 写 0.7；没有 ⇒ 回落基值（基值 0.6 由另一路设在 tuning.json）
                p.OwnsDodgeCapUp = p.DodgeCapStacks > 0;
                p.DodgeCapTarget = Mathf.Clamp01(RowRuleValue(ProgRules.DodgeCapUp, 70f) / 100f);

                // ★ v212：`crit_trade` 的真实效果**不在这里**，也不在上限上 ★
                //   它只是"拥有/不拥有"的一个读数，而效果由 `items.json` 的 `effects` 承担
                //   ★ v3xx：现在是 `AtkSpeed Percent −9.9` ⇒ `WriteBonus` 写一条 `Mul(0.901)`
                //     ⇒ 攻速 1.0 × 0.901 = 0.901 ⇒ 出手间隔 = cooldown / max(0.05, 0.901) = ×1.110
                //     （旧的 `−200%` 才需要"下限兜底"那条路；现在离 0.05 的下限还很远）✓
                //   ⇒ 本字段只回答"持有几层"，没有写入方 ✓
                p.OwnsCritTrade = p.CritTradeStacks > 0;
                return p;
            }

            /// <summary>水壶充能上限的兜底每层点数（表里 `ruleValue` 缺失时用）。</summary>
            public const int WaterChargePerLayerDefault = 20;

            private static int StacksOf(RunSession s, string rule)
            {
                string id = ProgRules.ItemOf(rule);
                if (string.IsNullOrEmpty(id)) return 0;
                return s.ItemStacks.TryGetValue(id, out int n) ? n : 0;
            }

            /// <summary>某条规则在表里的 `ruleValue`（找不到 = 兜底值）。</summary>
            public static float RowRuleValue(string rule, float fallback)
            {
                string id = ProgRules.ItemOf(rule);
                ItemDef def = string.IsNullOrEmpty(id) ? null : TableDatabase.GetItem(id);
                return def != null && def.RuleValue > 0f ? def.RuleValue : fallback;
            }

            /// <summary>水壶弹药上限的**运行时**值（基础 `RunSession.WaterAmmoMax` + 道具加成）。</summary>
            public int WaterAmmoCap => RunSession.WaterAmmoMax + WaterChargePerLayer * WaterChargeStacks;
        }

        public const string BuffSrcPrefix = "buff:";
        public const string PlantSrc = "prog:plant";
        public const string RampAtkSpeedSrc = "prog:ramp_atkspeed";
        public const string RampDamageSrc = "prog:ramp_damage";

        /// <summary>
        /// ★ 机制 3 的**判定余量**（米）：「站在这株植物的田地上」= 玩家位置落在地块矩形内，
        /// 半边长 = `farm.tileSize / 2 + 本值`（`tileSize = 2.5` ⇒ 1.25 + 0.15 = **1.40**）★
        ///
        /// ── 为什么按"米"写，而不是按"格的比例"（旧口径是 `tileSize × 0.55` = 1.375）──
        ///   比例口径下那个 0.55 **不是**一个能解释的数：它是"半格 0.5 加一点"凑出来的，
        ///   换 `farm.tileSize`（表里可调）时会跟着漂，而且看上去永远差 0.125。
        ///   同样的宽容度按米写 ⇒ "半格 ＋ 15 厘米" 一句话读得懂，也经得起调表 ✓
        ///   为什么恰好 0.15：玩家脚下是一个约 0.6 米宽的圆，压在格沿上时身体有一半在格里、
        ///   一半在格外的**视觉**上仍然"站在田里" —— 不给这点宽容，玩家会觉得"贴着格子站没用"✓
        ///
        /// ⚠️ 余量只放宽**边界**，不放宽"必须在这株植物的地块上"这条本体：
        ///   没有这块地的地块（未解锁 / 空地 / 别种植物）照样不算 ✓
        /// </summary>
        public const float PlotStandMargin = 0.15f;

        /// <summary>
        /// ★ 把"随时间 / 随存在变化"的三条写进 `StatBlock` ★
        ///
        ///   `buff:<id>`  —— 临时增益的层数（植物机制 1 / 2）
        ///   `prog:plant` —— **站在该植物的田地上**：闪避；植物在场：幸运
        ///   `prog:ramp*` —— 道具的攻速 / 伤害倍率爬升
        ///
        /// ★ 为什么"先撤再写"而不是"算增量加进去" ★
        ///   增量法一旦有一帧漏算（暂停 / 切阶段 / 面板打开）就永久漂移 ✗
        ///   撤销—重写是幂等的：任何一帧算完都等于"从基底重来一遍" ✓
        ///   代价是每帧几次零分配的 `List.RemoveAll` ⇒ 可接受 ✓
        /// </summary>
        public static void SyncRuntimeStats(RunSession s)
        {
            if (s == null || s.Progress == null) return;
            StatBlock st = s.Progress.Stats;
            ItemPanel p = ItemPanel.Of(s);

            // ① 临时增益（植物 1 / 2）
            st.RemoveModifiersFrom(BuffSrcPrefix + BuffIds.PlantAtkMult);
            st.RemoveModifiersFrom(BuffSrcPrefix + BuffIds.PlantAtkSpeed);
            if (s.Buffs != null)
            {
                WriteBuffMult(s, st, BuffIds.PlantAtkMult, StatId.AtkMult, PlantMechanics.HarvestAtkMult);
                WriteBuffMult(s, st, BuffIds.PlantAtkSpeed, StatId.AtkSpeed, PlantMechanics.HarvestAtkSpeed);
            }

            // ② 站田闪避 / 在场幸运
            st.RemoveModifiersFrom(PlantSrc);
            float dodge = PlantPresenceDodge(s);
            if (dodge > 0f) st.AddModifier(new StatModifier(StatId.Dodge, dodge, ModOp.Add, PlantSrc));
            float luck = PlantPresenceLuck(s);
            if (luck > 0f) st.AddModifier(new StatModifier(StatId.Luck, luck, ModOp.Add, PlantSrc));

            // ③ 两条爬升
            st.RemoveModifiersFrom(RampAtkSpeedSrc);
            st.RemoveModifiersFrom(RampDamageSrc);
            if (p.AtkSpeedRampStacks > 0 && AtkSpeedRampPct > 0f)
                st.AddModifier(new StatModifier(StatId.AtkSpeed, RampMultAt(AtkSpeedRampSeconds), ModOp.Mul, RampAtkSpeedSrc));
            if (p.DamageRampStacks > 0 && DamageRampPct > 0f)
                st.AddModifier(new StatModifier(StatId.AtkMult, RampMultAt(DamageRampSeconds), ModOp.Mul, RampDamageSrc));

            // ④ v253 猎杀亢奋（植物机制 4）：击杀后 4 秒内攻击倍率 ×1.12
            //   ★ 走 `StatId.AtkMult` 的**乘法**修正，与攻速爬升(`RampDamageSrc`)同一处、同一口径 ★
            //   植物被拔掉 / 换种后 `HasFarmMechanic` 变 false ⇒ 修正被撤掉（不会"buff 留在身上"）✓
            st.RemoveModifiersFrom(KillRushSrc);
            if (KillRushRemain > 0f && HasFarmMechanic(s.World, PlantMechanics.KillRush))
                st.AddModifier(new StatModifier(StatId.AtkMult, PlantMechanics.KillRushAtkMult, ModOp.Mul, KillRushSrc));
        }

        /// <summary>
        /// 一条层叠增益 → 一次乘法修正。
        /// 每层百分点 = **场上该机制植物的最高等级**换算出来的值
        /// （`PlantMechanics.HarvestBuffPerStack`：3% + 1%×（等级−1））⇒
        /// 与"叠层先后顺序"无关，任何一帧算出来都相同 ✓
        /// </summary>
        private static void WriteBuffMult(RunSession s, StatBlock st, string buffId, StatId stat, string mechanic)
        {
            int stacks = s.Buffs.StacksOf(buffId);
            if (stacks <= 0) return;
            float per = HarvestBuffPerStackInFarm(s, mechanic);
            st.AddModifier(new StatModifier(stat, 1f + stacks * per / 100f, ModOp.Mul, BuffSrcPrefix + buffId));
        }

        /// <summary>
        /// ★ 收割触发的两条机制（植物 1 / 2）—— **写侧的唯一入口** ★
        ///
        /// 用户原话："收割此植物 ⇒ 3 秒内攻击倍率 +3%，最多 12 层，每次叠加刷新时长，每升 1 级 +1%"
        /// ⇒ `Apply(..., refresh: true)`：**刷新时长** 且 **层数 +1（封顶 12）** ✓
        ///   （`refresh:false` 的那条语义留给"只是续时长、不叠层"的机制，本轮没有用到）
        ///
        /// ★ 为什么在这里而不在 `World.HarvestPlot` ★
        ///   `World` 不知道自己属于哪个 `RunSession`（它只有 `Player` 引用）；
        ///   而增益表挂在会话上 ⇒ 由调用方（`RunSession.HarvestPlot`，收割成功的那一处）转进来 ✓
        /// </summary>
        public static void OnHarvest(RunSession s, SeedDef def, int level)
        {
            if (s == null || def == null || s.Buffs == null) return;
            if (PlantMechanics.Has(def, PlantMechanics.HarvestAtkMult))
                s.Buffs.Apply(BuffIds.PlantAtkMult,
                    $"收割狂热 ({PlantMechanics.HarvestBuffPerStack(level):0}%/层)",
                    1, PlantMechanics.HarvestBuffSeconds, true);
            if (PlantMechanics.Has(def, PlantMechanics.HarvestAtkSpeed))
                s.Buffs.Apply(BuffIds.PlantAtkSpeed,
                    $"收割疾风 ({PlantMechanics.HarvestBuffPerStack(level):0}%/层)",
                    1, PlantMechanics.HarvestBuffSeconds, true);
        }

        /// <summary>场上带某条收割增益机制的植物里，最高等级那一株的"每层百分点"。</summary>
        private static float HarvestBuffPerStackInFarm(RunSession s, string mechanic)
        {
            int bestLevel = 0;
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || p.CropDef == null) continue;
                if (!PlantMechanics.Has(p.CropDef, mechanic)) continue;
                if (p.Level > bestLevel) bestLevel = p.Level;
            }
            if (bestLevel <= 0) bestLevel = 1;
            return PlantMechanics.HarvestBuffPerStack(bestLevel);
        }

        /// <summary>
        /// ★★ 机制 3：**站在这株植物的田地上 ⇒ 闪避** ★★
        ///
        /// 用户原话（本轮）：「植物（站着时获得增益），这种植物修改的效果说明和实际效果为
        /// **站在它的田地上**生效，而不是站着」——
        ///   判据只有一条：**玩家位置是否落在该植株所在地块的矩形内**（见 <see cref="PlotStandMargin">），
        ///   与"在不在走路"**完全无关**：跑着穿过、站着不动、边打边退，只要人在格子里就生效 ✓
        ///
        /// ★ 为什么遍历全场、而不是只问"脚下那一块"（改动点）★
        ///   一株植物占的是**一整列**（`FarmGrid.FillRows` 把同列每格都补上同一个 `CropDef`），
        ///   而相邻两块的判定框在边沿上有 ±0.15m 的重叠带 ⇒ 只取"第一块命中的地"时，
        ///   站在重叠带里的玩家可能拿到**隔壁那株（没有这条机制的）**的地块，
        ///   于是"明明站在庇护藤的田里却没有闪避"✗ 用户要的是
        ///   「**任意一株**所在地块内即可」⇒ 遍历所有命中地块，取其中最大的一份 ✓
        ///   （多株同为该机制时不叠加，与"取最高级那一株"的既有口径一致；数值一个没动）
        /// </summary>
        public static float PlantPresenceDodge(RunSession s)
        {
            if (s == null || s.World == null || s.World.Player == null) return 0f;
            FarmGrid farm = s.World.Farm;
            if (farm == null) return 0f;
            FarmPlot[] slots = farm.Slots;
            if (slots == null) return 0f;
            float half = farm.TileSize * 0.5f + PlotStandMargin;
            Vector2 pp = s.World.Player.Pos;
            float best = 0f;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot plot = slots[i];
                if (plot == null || !plot.Unlocked || plot.CropDef == null) continue;
                if (!InsidePlot(plot, pp, half)) continue;
                if (!PlantMechanics.Has(plot.CropDef, PlantMechanics.StandingDodge)) continue;
                float v = PlantMechanics.StandingDodgeValue(plot.Level);
                if (v > best) best = v;
            }
            return best;
        }

        /// <summary>场上"幸运"植物给的幸运（机制 6；取最高一株，不叠）。</summary>
        public static float PlantPresenceLuck(RunSession s)
        {
            float best = 0f;
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || p.CropDef == null) continue;
                if (!PlantMechanics.Has(p.CropDef, PlantMechanics.FarmLuck)) continue;
                float v = PlantMechanics.FarmLuckValue(p.Level);
                if (v > best) best = v;
            }
            return best;
        }

        /// <summary>
        /// 玩家站在哪块**田地**上（没有 = null）。判据：地块中心 ±（`tileSize / 2` ＋ <see cref="PlotStandMargin"/>）；
        /// 多块同时命中时取**中心最近**的那块（重叠带里不会随便挑一个）。
        /// ⚠️ 机制 3 的闪避**不走**这个函数（它要"任意一株命中即可"，见 `PlantPresenceDodge`），
        ///    这里保留的是"脚下是哪块地"这个通用问题（别的系统问的时候用同一条判定）✓
        /// </summary>
        public static FarmPlot PlotUnderPlayer(RunSession s)
        {
            if (s == null || s.World == null || s.World.Player == null) return null;
            FarmGrid farm = s.World.Farm;
            if (farm == null) return null;
            FarmPlot[] slots = farm.Slots;
            if (slots == null) return null;
            float half = farm.TileSize * 0.5f + PlotStandMargin;
            Vector2 pp = s.World.Player.Pos;
            FarmPlot best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || !p.Unlocked) continue;
                if (!InsidePlot(p, pp, half)) continue;
                float sqr = (p.World - pp).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return best;
        }

        /// <summary>`pos` 是否落在 `plot` 那块地的矩形内（半边长由调用方算好，见 `PlotStandMargin`）。</summary>
        private static bool InsidePlot(FarmPlot plot, Vector2 pos, float half)
        {
            Vector2 d = plot.World - pos;
            return Mathf.Abs(d.x) <= half && Mathf.Abs(d.y) <= half;
        }
    }

    /// <summary>
    /// ★ v197：**这 10 件机制道具的规则键**（与 `items.json` 的 `rule` 字段逐字对应）。
    ///
    /// 与 `ItemRules`（`Defs.cs`）的关系：那边是"解释器登记处 + 中文说明"，
    /// 这边是"本批的键名常量 + 键↔道具 id 反查表"。
    /// `ItemRules.All` 仍必须包含这 10 个键（`TableDatabase.Validate` 与自检都会查），
    /// 由 `Defs.cs` 里**只追加**完成 —— 那两个文件另两路也在改，所以写得尽量小 ✓
    /// </summary>
    public static class ProgRules
    {
        public const string CarrotOrbit = "carrot_orbit";
        public const string RabbitPet = "rabbit_pet";
        public const string DodgeCapUp = "dodge_cap_up";
        public const string WaterChargeUp = "water_charge_up";
        public const string AtkSpeedRamp = "atk_speed_ramp";
        public const string DamageRamp = "damage_ramp";
        public const string BlockShield = "block_shield";
        public const string CritTrade = "crit_trade";
        public const string SharpWater = "sharp_water";
        public const string GoldDamage = "gold_damage";

        /// <summary>规则键 → 对应的道具 id（**表里真有的 id**；空 = 没有对应件）。</summary>
        public static string ItemOf(string rule)
        {
            if (string.IsNullOrEmpty(rule)) return null;
            if (string.Equals(rule, CarrotOrbit, StringComparison.OrdinalIgnoreCase)) return "carrot_ring";
            if (string.Equals(rule, RabbitPet, StringComparison.OrdinalIgnoreCase)) return "bunny_pet";
            if (string.Equals(rule, DodgeCapUp, StringComparison.OrdinalIgnoreCase)) return "evade_helm";
            if (string.Equals(rule, WaterChargeUp, StringComparison.OrdinalIgnoreCase)) return "big_can";
            if (string.Equals(rule, AtkSpeedRamp, StringComparison.OrdinalIgnoreCase)) return "windup_gear";
            if (string.Equals(rule, DamageRamp, StringComparison.OrdinalIgnoreCase)) return "charge_coil";
            if (string.Equals(rule, BlockShield, StringComparison.OrdinalIgnoreCase)) return "aegis_lamp";
            if (string.Equals(rule, CritTrade, StringComparison.OrdinalIgnoreCase)) return "gamble_glove";
            if (string.Equals(rule, SharpWater, StringComparison.OrdinalIgnoreCase)) return "sharp_spray";
            if (string.Equals(rule, GoldDamage, StringComparison.OrdinalIgnoreCase)) return "tycoon_coin";
            return null;
        }
    }
}
