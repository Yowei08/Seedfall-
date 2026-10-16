using UnityEngine;

namespace Seedfall
{
    public enum BotStrategy { Idle = 0, Farmer = 1, Balanced = 2, Rush = 3 }

    /// <summary>
    /// ★ v168：**策略档位** ★ —— 四个策略的行为差异**只允许落在这一张表里**。
    ///
    /// 为什么必须集中：
    /// v26 删掉耕作动作流程、v38 又写明"所有策略都应当战斗"之后，
    /// 四个策略的决策树里只剩"打" ⇒ 矩阵跑出的是**同一组数字**（实测 Farmer/Balanced/Rush
    /// 三行的击杀/金币/等级逐字段相同）⇒ 策略矩阵失去意义 ✗
    /// 差异散在 if 里也会漂移，所以收敛成一张表：**改行为就是改这张表** ✓
    /// </summary>
    public struct BotProfile
    {
        /// <summary>目标植株数（= 占几条列；一列 = 一株）。</summary>
        public int TargetPlants;
        /// <summary>每次进店最多买几份种子。</summary>
        public int SeedsPerVisit;
        /// <summary>蓄水低于上限的这个比例就补水。</summary>
        public float WaterBelow;
        /// <summary>射线覆盖的格子都补到这个比例以上才停手。</summary>
        public float WaterStop;
        /// <summary>是否施肥（生长 -50%，代价是定身与"期间不能攻击"）。</summary>
        public bool Fertilize;
        /// <summary>是否去商店车买种子（"播种"在 v23 之后由 RunSession 自动完成，Bot 只能补种子）。</summary>
        public bool VisitTruck;
        /// <summary>是否买道具/插件（种子买不到时的兜底）。</summary>
        public bool BuyItems;
        /// <summary>主动接敌距离（米）；超出就不追，回田边待命。</summary>
        public float FightRange;
        /// <summary>保留给战斗的最低弹药，低于它就是"不再浇水"。</summary>
        public int AmmoReserve;
    }

    /// <summary>
    /// 无头/自动跑局机器人：只通过 RunSession 的公共输入结构操作，与人类走同一条路径，
    /// 因此"Bot 跑局"本身就是可复现的回放（架构案 §确定性与存档）。
    ///
    /// ★ v168：补上**种地**这条线 ★
    ///
    /// 旧版（v26~v167）里 Bot 没有种地的代码：`CombatTick` 只会"走向成熟地块用锄头打"
    /// 与"打敌人"，注释还写着"耕作已自动化（种下即生长），所有策略都应当战斗" ✗
    /// 而新玩法是 **播种 + 浇水 + 收割** 三件事，且**金币与经验只从收割来** ⇒
    /// 实测四个策略全是 `耕作占比 0.0% / 存活植物 0` ✓ 那些数字不是"平衡结果"，
    /// 是"机器人根本没种过地"的读数 ✓
    ///
    /// 本版每帧决策（优先级理由见 <see cref="CombatTick"/> 的注释）：
    ///   危险规避 → **买入种子（停靠窗内，硬资源）** → 收割 → 浇水 → 施肥 → 捡掉落 → 战斗/待命
    ///   ⚠️ 上面这一行必须与 `CombatTick` 里的 `if/else` **顺序一致** ✗
    ///      v168.1 就是"注释写买种优先、代码里收割在前"⇒ 停靠窗一次都没用上 ✗
    ///
    /// 全部行为都走既有输入：`Move` / `AimWorld` / `Fire` / `WeaponSelectSet` /
    /// `Interact` / `ShopOpen` / `Skip` / `BuyOffer` —— **不改 RunSession 的任何玩法逻辑** ✓
    /// </summary>
    public sealed class RunBot
    {
        private readonly RunSession _s;

        private float _shopTimer;
        private int _shopVisits;
        private int _shopBuys;
        private int _shopSeeds;

        private float _repathTimer;
        private float _switchTimer;
        private float _fertilizeTimer;
        private int _fertilizePlot = -1;

        private float _stuckTimer;
        private float _detourTimer;
        private Vector2 _lastPos;
        private bool _haveLastPos;

        /// <summary>
        /// ★ v168.3：**去商店车的承诺期**（`Clock.GameTime` 之前不算被打断）★
        ///
        /// 实测缺口：不死 Farmer 活了 300 秒（车来 **5 次**）而**进店 0 次** ✗
        /// 根因不是门槛算错，而是**"危险规避"把整帧抢走了** ✗：
        ///   走过去要 2~3 秒，而 `acute`（敌人 < 2.6 米）几乎每时每刻成立 ⇒
        ///   刚迈两步就被判"危险"⇒ 退避方向又把它顶回田里 ⇒ **永远到不了停靠点** ✓
        /// ⇒ 一旦决定去，就得**承诺**这段路；否则"每 60 秒 10 秒的窗口"是够不着的 ✓
        /// 安全阀：血量 < 35% 立刻放弃承诺（命比种子重要 ✓）
        /// </summary>
        private float _shopCommitUntil;
        /// <summary>当前这个停靠窗是否已经记过一次（避免每帧累加）。</summary>
        private bool _windowCounted;

        /// <summary>本帧选中的"要收割的成熟格"（-1 = 没有）。</summary>
        private int _ripePlot = -1;
        /// <summary>本帧的浇水射线规划（站位 + 方向）。</summary>
        private WaterPlan _waterPlan;

        // ── 度量快照：耕作产出全部由 Bot 侧观测，不往 RunSession 里加字段 ──
        private PlotState[] _seenState = System.Array.Empty<PlotState>();
        private int[] _seenWater = System.Array.Empty<int>();
        private int _seenPlants = -1;

        public BotStrategy Strategy { get; set; }
        public string LastDecision { get; private set; } = "";

        // ── 报告用的度量 ──
        /// <summary>本局 Bot 把时间花在耕作意图上的秒数（收割 / 浇水 / 施肥 / 去车买种）。</summary>
        public float FarmIntentSeconds { get; private set; }
        /// <summary>本局 Bot 把时间花在战斗意图上的秒数。</summary>
        public float FightIntentSeconds { get; private set; }
        /// <summary>Bot 触发的新增植株数（= 新增列数）。</summary>
        public int SownColumns { get; private set; }
        /// <summary>收割次数（成熟格 → 重新生长）。</summary>
        public int Harvests { get; private set; }
        /// <summary>实际浇进地里的水（按每格蓄水增量累计）。</summary>
        public int WaterDelivered { get; private set; }
        /// <summary>施肥完成次数。</summary>
        public int Fertilizes { get; private set; }
        /// <summary>进商店次数。</summary>
        public int ShopVisitsLog { get; private set; }
        /// <summary>★ v168.3：**可行动的停靠窗次数**（三个门槛都过了的窗口）——用来区分
        /// "没看到窗口" 与 "看到了但没走到" ✗（这两种修法完全不同）</summary>
        public int ShopWindows { get; private set; }
        /// <summary>★ v168.5：**进店时发现货架为空的次数** ★ —— 判据是**恒为 0** ✓
        /// 不为 0 ⇒ "车那条路又不刷新货架了" ⇒ 那是真信号，必须报出来而不是自己补上 ✓</summary>
        public int EmptyShelfVisits { get; private set; }
        /// <summary>买到的种子数。</summary>
        public int SeedsBought { get; private set; }
        /// <summary>收割累计金币（与 World.HarvestPlot 同一条算式 price/5）。</summary>
        public int HarvestGold { get; private set; }
        /// <summary>收割累计经验（与 World.HarvestPlot 同一条算式 price/10）。</summary>
        public int HarvestExp { get; private set; }
        /// <summary>被释放/死亡的耕地格数（成熟或生长中直接回到空地）——用来区分"收割"与"丢失"。</summary>
        public int PlotsLost { get; private set; }

        public RunBot(RunSession session, BotStrategy strategy)
        {
            _s = session;
            Strategy = strategy;
        }

        /// <summary>四个策略的唯一差异来源。</summary>
        public static BotProfile ProfileOf(BotStrategy s)
        {
            switch (s)
            {
                case BotStrategy.Farmer:
                    // 最多种地：目标满 6 列、每次进店最多买 3 份、水一掉就补
                    return new BotProfile
                    {
                        TargetPlants = 6, SeedsPerVisit = 3, WaterBelow = 0.90f, WaterStop = 0.95f,
                        Fertilize = true, VisitTruck = true, BuyItems = false,
                        FightRange = 8.0f, AmmoReserve = 10
                    };
                case BotStrategy.Balanced:
                    // 3~4 株：种得少一点、水才够用；仍然进店，但每次只买 2 份
                    return new BotProfile
                    {
                        TargetPlants = 4, SeedsPerVisit = 2, WaterBelow = 0.65f, WaterStop = 0.90f,
                        Fertilize = true, VisitTruck = true, BuyItems = true,
                        FightRange = 9.0f, AmmoReserve = 20
                    };
                case BotStrategy.Rush:
                    // 0~1 株：只留开局那株，不买种、几乎不管水，专攻打
                    return new BotProfile
                    {
                        TargetPlants = 1, SeedsPerVisit = 0, WaterBelow = 0.20f, WaterStop = 0.60f,
                        Fertilize = false, VisitTruck = false, BuyItems = true,
                        FightRange = 9.5f, AmmoReserve = 30
                    };
                default: // Idle
                    return new BotProfile
                    {
                        TargetPlants = 0, SeedsPerVisit = 0, WaterBelow = 0f, WaterStop = 0f,
                        Fertilize = false, VisitTruck = false, BuyItems = false,
                        FightRange = 0f, AmmoReserve = 0
                    };
            }
        }

        public BotProfile Profile => ProfileOf(Strategy);

        // ------------------------------------------------------------ 每帧入口
        public FrameInput Tick(float dt)
        {
            var input = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
            };
            if (_s == null) return input;

            switch (_s.Phase)
            {
                case PhaseState.Prepare:
                    input.InitialChoice = 0;
                    break;
                case PhaseState.Shop:
                    ShopTick(dt, ref input);
                    break;
                case PhaseState.LevelReward:
                    // v12：升级抽卡是"选中 → 确认"两步，Bot 也必须走这条路径，否则会永久卡住
                    if (_s.RewardCards.Count > 0)
                    {
                        if (_s.RewardSelected < 0) { input.RewardSelectSet = true; input.RewardSelect = 0; }
                        else { input.RewardPick = _s.RewardSelected; input.RewardPickConfirmed = true; }
                    }
                    break;
                case PhaseState.Combat:
                    if (Strategy == BotStrategy.Idle) return input;
                    CombatTick(dt, ref input);
                    break;
            }
            return input;
        }

        // ------------------------------------------------------------ 商店
        private void ShopTick(float dt, ref FrameInput input)
        {
            _shopTimer += dt;
            if (_shopTimer < 0.35f) return;
            _shopTimer = 0f;
            _shopVisits++;

            BotProfile cfg = Profile;
            if (_shopVisits == 1) { ShopVisitsLog++; LastDecision = "shop-enter"; }

            // ★ v168.5：**空货架不再被"悄悄补上"，改成一条会报警的计数** ★
            //
            // 事故记录（值得留在代码里）：
            // 从**商店车**进店时，`RunSession.TickShopTruck` 曾经只调 `SetPhase(PhaseState.Shop)`
            // —— 没有 `Shop.ResetForPhase()`、也没有 `Shop.Refresh(this)` ✗
            // 而 `ShopSystem.Refresh` 是 `Offers` 的**唯一填充者** ⇒ **货架是空的** ⇒ 一份种子也买不到 ✓
            // （实测：不死 Farmer 进店 4 次 / 买种 0 份 ✓ 真人也一样：按 E 进店看到空货架 ✗）
            //
            // 我当时的变通是"发现空货架就先重抽一次"——它让数字好看了 ✗
            // ★ 而那正是这个 bug 能藏那么久的原因：**异常被静默处理 = 异常消失** ★
            //   （"自动那条路会填"也是同一类遮蔽 ✓）
            // 真修复已落在 `TickShopTruck`（ResetForPhase + Refresh + 再 SetPhase ✓）
            // ⇒ 这里只**观察**，不修补 ✓：
            if (_shopVisits == 1 && _s.Shop.Offers.Count == 0) EmptyShelfVisits++;
            // ★ 判据：`EmptyShelfVisits` **应恒为 0** ★ —— 不为 0 就是"货架又空了"的真信号 ✓
            //   而不是一个被我用重抽悄悄绕过去的异常 ✓

            int plants = _s.World != null && _s.World.Farm != null ? _s.World.Farm.PlantCount : 0;
            int wantSeeds = Mathf.Max(0, cfg.TargetPlants - plants);

            // ① 种子优先：**"播种"的唯一入口**（买到即由 RunSession.AutoPlantSeed 种到第一条空列）
            if (wantSeeds > 0 && _shopSeeds < cfg.SeedsPerVisit)
            {
                int best = -1;
                int bestPrice = int.MaxValue;
                for (int i = 0; i < _s.Shop.Offers.Count; i++)
                {
                    ShopOffer o = _s.Shop.Offers[i];
                    if (o.Kind != OfferKind.Seed || o.Price > _s.Gold) continue;
                    if (o.Price < bestPrice) { bestPrice = o.Price; best = i; }
                }
                if (best >= 0)
                {
                    input.BuyOffer = best;
                    _shopSeeds++;
                    _shopBuys++;
                    SeedsBought++;
                    LastDecision = "buy-seed";
                    return;
                }
            }

            // ② 兜底：插件 / 道具，每回合最多一件。
            //    顺序有意是"插件先于道具"——插件是永久强化、不占种子位 ✓（沿用旧版的判断）
            if (cfg.BuyItems && _shopBuys < 1)
            {
                int best = -1;
                for (int i = 0; i < _s.Shop.Offers.Count; i++)
                {
                    ShopOffer o = _s.Shop.Offers[i];
                    if (o.Price > _s.Gold) continue;
                    if (o.Kind == OfferKind.Plugin) { best = i; break; }
                    if (o.Kind == OfferKind.Item && best < 0) best = i;
                }
                if (best >= 0)
                {
                    input.BuyOffer = best;
                    _shopBuys++;
                    LastDecision = "buy-other";
                    return;
                }
            }

            // ③ 买不动了 / 买够了 → 走。v5：商店不限时，不走就是软锁
            bool done = wantSeeds <= 0 || _shopSeeds >= cfg.SeedsPerVisit || _shopVisits > 10;
            if (!done && _shopVisits <= 6) return;      // 再等两拍，金币可能马上就够
            input.Skip = true;
            _shopBuys = 0;
            _shopSeeds = 0;
            _shopVisits = 0;
            LastDecision = "skip-shop";
        }

        // ------------------------------------------------------------ 战斗/耕作
        /// <summary>
        /// ★ v168 的优先级（每一档都写清"为什么在这一档"）★
        ///
        ///   ① **危险规避**：贴身 / 残血时先打。理由：死了就没有后半局；
        ///      而且 `FarmingThisFrame`（耕作动作）会**禁止一切攻击** ⇒ 在敌人脸上种地是纯亏 ✓
        ///   ② **收割**：有成熟格 → 走过去锄头收。理由：成熟地块**不再生长**
        ///      （`TickGrowth2` 对 Grown 直接返回），不收它就一直是 0 产出；
        ///      而收割是全游戏**唯一的金币/经验来源** ✓
        ///      为什么排在浇水之前：收割是"立刻进账 + 让这株重新开始长"，浇水只是"让正在长的继续长"；
        ///      而且收割会把地块拨回 Growing ⇒ 收完马上就会用到水（两件事天然一前一后）✓
        ///   ③ **买入种子（= 播种）**：停靠窗是**硬资源**（每 60 秒只有 10 秒），
        ///      错过一次就整整晚一分钟才可能多一株；水是软资源（晚几秒补只是少长几秒）✓
        ///      所以窗口开着时它排在浇水前面 ✓
        ///   ④ **浇水**：先救"停长"的格，再补低于档位阈值的格。理由：`GrowthPaused`
        ///      意味着这株**此刻完全不长**（永久损失），轻微缺水只是慢一点 ✓
        ///      激光同一发既浇水又打敌人 ⇒ 边打边浇是这套玩法的设计意图，不必二选一 ✓
        ///   ⑤ **施肥**：生长 -50%，但要定身且期间不能攻击 ⇒ 只在身边没敌人时做 ✓
        ///   ⑥ **战斗 / 待命**：其余时间打最近的敌人（击杀 → 金币 + 雨滴 → 弹药）；
        ///      没有目标就回田边待命，**不空放激光**（起手费 10 弹药 + 每 0.5 秒 10，是全场最贵的动作）✓
        /// </summary>
        private void CombatTick(float dt, ref FrameInput input)
        {
            World w = _s.World;
            PlayerActor p = w.Player;
            if (p == null || !p.Alive) return;

            Observe();   // 先结算上一帧的产出（本 Tick 早于 FixedTick）

            BotProfile cfg = Profile;
            EnemyActor nearest = w.NearestEnemyTo(p.Pos, 99f);
            float nearestDist = nearest == null ? 99f : Vector2.Distance(p.Pos, nearest.Pos);
            bool lowHp = p.Health.HpPct < 0.45f;
            bool acute = nearestDist < 2.6f || (lowHp && nearestDist < 6.5f);

            _repathTimer -= dt;
            if (_repathTimer <= 0f)
            {
                _repathTimer = 0.25f;
                _ripePlot = PickRipePlot(w, p.Pos);
                _waterPlan = PickWaterPlan(w, p.Pos, cfg);
            }

            Vector2 move = Vector2.zero;
            Vector2 aim = Vector2.zero;
            bool fire = false;
            bool interact = false;
            bool shopOpen = false;
            int weapon = p.WeaponIndex;
            bool farming = false;

            // ★ v168.3：**去商店车的承诺期**可以压过危险规避 ★
            // 理由见 `_shopCommitUntil` 的注释：没有承诺，"10 秒窗口"就是够不着的 ✗
            bool shopCommitted = _s.Clock.GameTime < _shopCommitUntil;

            if (acute && !shopCommitted)
            {
                Fight(w, p, nearest, nearestDist, cfg, true, ref move, ref aim, ref fire, ref weapon);
                LastDecision = "fight:acute";
            }
            // ★ v168.2：**买种子（硬资源窗口）排在收割之前** ★
            //
            // 实测教训：v168.1 里它排在收割之后 ⇒ 两块地、5 秒熟一次 ⇒ **每帧都有熟地可收**
            // ⇒ 收割永远抢先 ⇒ 停靠窗（每 60 秒只有 10 秒）**一次都没被用上** ✗
            // 数据：Farmer 活 226 秒、收割 24 次、但「进店 0 次 / 买种 0 份」✓✓
            //
            // 判据：窗口是**会过期**的资源（错过就整整晚一分钟），熟地**不会过期**（它就在那儿等）✓
            // ⇒ 窗口开着时先抢窗口，回来再收 ✓
            // 而 `TryShopTrip` 自己带门槛（只在 Dock / 停靠前 3.5 秒 / 赶得上 / 买得起）⇒ 平时是空操作 ✓
            else if (TryShopTrip(w, p, cfg, ref move, ref shopOpen))
            {
                farming = true;
                // 承诺期里"边走边打"：**移动仍然去停靠点**，但瞄准与开火照旧 ✗→✓
                // （原来 acute 会把移动也抢走 ⇒ 一走就被顶回田里 ⇒ 6 次窗口一次也到不了）
                if (acute && nearest != null)
                {
                    Vector2 toE = nearest.Pos - p.Pos;
                    if (toE.sqrMagnitude > 0.0001f) aim = p.Pos + toE.normalized * 3f;
                    bool canShoot = _s.WaterAmmo >= 10;
                    fire = canShoot;
                    weapon = canShoot ? PlayerActor.WeaponCan : PlayerActor.WeaponHoe;
                    LastDecision = "shop:walk+fight";
                }
            }
            else if (TryHarvest(w, p, ref move, ref aim, ref fire, ref weapon))
            {
                farming = true;
            }
            else if (TryWater(w, p, cfg, ref move, ref aim, ref fire, ref weapon))
            {
                farming = true;
            }
            else if (TryFertilize(w, p, nearestDist, ref move, ref interact))
            {
                farming = true;
            }
            else if (TryCollect(w, p, nearestDist, ref move))
            {
                farming = true;      // 捡雨滴 = 攒弹药 = 浇水的前提，算在耕作这一侧
            }
            else
            {
                Fight(w, p, nearest, nearestDist, cfg, false, ref move, ref aim, ref fire, ref weapon);
            }

            if (farming) FarmIntentSeconds += dt; else FightIntentSeconds += dt;

            // 卡住检测：直线上有障碍时侧移一下（无随机数 ⇒ 仍然确定）
            move = Unstick(p, move, farming, dt);

            input.Move = move;
            input.Fire = fire;
            input.Interact = interact;
            input.ShopOpen = shopOpen;

            Vector2 aimPoint = aim.sqrMagnitude > 0.0001f
                ? aim
                : p.Pos + (move.sqrMagnitude > 0.001f ? move : new Vector2(0f, 1f)) * 3f;
            input.AimWorld = aimPoint;

            // 武器：走"数字键"这条直达路径（滚轮是相对增量，还要跨过中间那把）
            _switchTimer -= dt;
            if (weapon != p.WeaponIndex && p.SwitchWindup <= 0f && _switchTimer <= 0f)
            {
                input.WeaponSelectSet = true;
                input.SelectWeapon = weapon;
                _switchTimer = 0.45f;
            }
        }

        // ------------------------------------------------------------ 收割
        private bool TryHarvest(World w, PlayerActor p,
            ref Vector2 move, ref Vector2 aim, ref bool fire, ref int weapon)
        {
            FarmPlot plot = _ripePlot >= 0 && _ripePlot < w.Farm.Slots.Length ? w.Farm.Slots[_ripePlot] : null;
            if (plot == null || !plot.IsRipe) return false;

            weapon = PlayerActor.WeaponHoe;
            Vector2 to = plot.World - p.Pos;
            float dist = to.magnitude;
            // 锄头收割判定：距离 ≤ 射程(3.2×攻击范围) + 1.2，且方向在 ±arc/2 = ±50° 内
            float reach = Mathf.Max(1.5f, w.Tuning.tools.hoe.range * p.RangeMult + 0.8f);
            if (dist > reach)
            {
                move = to.normalized;
                LastDecision = "harvest:walk";
                return true;
            }
            move = Vector2.zero;
            aim = p.Pos + to.normalized * 3f;
            fire = true;                 // 挥锄 = HarvestRipeInMeleeBox（扇形内的成熟格一次全收）
            LastDecision = "harvest:swing";
            return true;
        }

        private int PickRipePlot(World w, Vector2 from)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < w.Farm.Slots.Length; i++)
            {
                FarmPlot plot = w.Farm.Slots[i];
                if (plot == null || !plot.IsRipe) continue;
                float d = (plot.World - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------ 播种（去商店车买种子）
        /// <summary>
        /// v168：**"播种"在 v23 之后没有玩家动作** —— 种子进袋即由
        /// `RunSession.AutoPlantSeed` 种到第一条空列（`BeginPlant` 还会把整列补齐）✓
        /// 所以 Bot 能做、也必须做的只有一件事：**弄到种子** ✓
        /// 计时制里商店的唯一入口是**商店车停靠期按 E**（每 60 秒一个 10 秒窗口）✓
        /// </summary>
        private bool TryShopTrip(World w, PlayerActor p, BotProfile cfg, ref Vector2 move, ref bool shopOpen)
        {
            if (!cfg.VisitTruck) return false;
            if (w.Farm.PlantCount >= cfg.TargetPlants) return false;
            // 种子袋满了也买不进来（`Bag.AddSeed` 会失败）⇒ 别白跑一趟 ✗
            // 袋满但植株数不足是可能的：某个种子占着槽位、它的地块却被释放/死了 ✓
            if (_s.Bag != null && _s.Bag.OccupiedCount >= _s.Bag.SlotCount) return false;
            if (_s.Gold < MinSeedPrice()) return false;

            ShopTruck truck = _s.Truck;
            if (truck == null) return false;
            float half = w.Tuning.farm.arenaHalf;
            Vector2 dock = ShopTruck.DockPoint * half;
            float dist = Vector2.Distance(p.Pos, dock);

            ShopTruck.Phase ph = truck.Current;
            bool open = ph == ShopTruck.Phase.Dock;
            bool soon = ph == ShopTruck.Phase.Looping
                        && truck.PhaseElapsed(_s.Clock.GameTime) > ShopTruck.LoopSeconds - 3.5f;

            if (!open) _windowCounted = false;                     // 窗口关了就允许下次再记一次
            if (!open && !soon) return false;
            if (open && !_windowCounted) { _windowCounted = true; ShopWindows++; }

            if (open)
            {
                // 安全阀：残血立刻放弃承诺 —— 命比种子重要 ✓
                if (p.Health.HpPct < 0.35f) { _shopCommitUntil = 0f; LastDecision = "shop:abort"; return false; }
                float left = truck.DockSecondsLeft(_s.Clock);
                float eta = dist / Mathf.Max(0.5f, p.MoveSpeed);
                if (eta > left) return false;                    // 赶不上就不去了，别把田丢下
                _shopCommitUntil = _s.Clock.GameTime + 2.5f;      // ★ 承诺这段路，不被 acute 打断 ★
                if (dist <= ShopTruck.InteractRange)
                {
                    shopOpen = true;                             // 按 E 进店
                    LastDecision = "shop:e";
                    return true;
                }
            }
            else
            {
                _shopCommitUntil = _s.Clock.GameTime + 1.5f;      // 提前出发那一段也算承诺
            }

            move = (dock - p.Pos).normalized;
            LastDecision = open ? "shop:walk" : "shop:prewalk";
            return true;
        }

        /// <summary>
        /// 最便宜的种子价 —— ★ 走项目里**唯一的那份定价算式**（`SeedSlot.ShopPriceFrom`）★
        /// 而不是写个常数：价格带波次曲线（`shop.priceWaveStep = 0.13/波`）⇒
        /// 第 11 分钟时 D 级种子的实际价已是 ~18 金 ✓
        /// 用常数 8 会让 Bot 在第 10 分钟**白跑一趟**（到了发现买不起）✗
        /// </summary>
        private int MinSeedPrice() => SeedSlot.ShopPriceFrom(8, 0, _s.WaveIndex);

        // ------------------------------------------------------------ 浇水
        /// <summary>
        /// v168：**浇水 = 对着地块开激光**（水壶激光同时伤害敌人与补水）✓
        ///
        /// 数值（tools.can / farm）：
        ///   · 每次命中 +10 水（`farm.waterPerHit`）；送出去的水按 50 水 = 1 弹药**累计**扣 ✓
        ///   · 激光**起手费 10 弹药 + 每 0.5 秒 10** ⇒ 空放是全场最贵的浪费
        ///     ⇒ **只在射线上真有缺水格时开火** ✓
        ///   · 判定：射线（半宽 1.4 米）上的地块都补水 ⇒ 站到**两行之间**就能一条射线浇两行 ✓
        ///     （行距 2.5 ⇒ 行中点的垂直偏差 1.25 &lt; 1.4，正好在判定内；
        ///      再拉开会被 `aimJitterDeg`（1.5°）抖出去，所以固定取行中点而不是更大 ✓）
        /// </summary>
        private bool TryWater(World w, PlayerActor p, BotProfile cfg,
            ref Vector2 move, ref Vector2 aim, ref bool fire, ref int weapon)
        {
            if (cfg.WaterBelow <= 0f || !_waterPlan.Valid) return false;
            if (_s.WaterAmmo < cfg.AmmoReserve + 10) return false;    // 弹药见底：先保证打得动

            weapon = PlayerActor.WeaponCan;

            // 先对齐到射线所在的 y（x 不动，少走路）
            float dy = _waterPlan.Stand.y - p.Pos.y;
            if (Mathf.Abs(dy) > 0.35f)
            {
                move = new Vector2(0f, Mathf.Sign(dy));
                LastDecision = "water:align";
                return true;
            }

            move = Vector2.zero;
            aim = p.Pos + _waterPlan.Dir * 3f;
            if (FirstThirstyOnRay(w, p.Pos, _waterPlan.Dir, cfg) == null)
            {
                LastDecision = "water:full";
                return true;              // 站好了但这条射线已经浇满——不开火
            }
            fire = true;
            LastDecision = "water:fire";
            return true;
        }

        /// <summary>
        /// 射线"行半宽"容差：站两行中间时，几何偏移正是 **1.25**（行距 2.5 的一半）✓
        /// 而引擎自己的补水判定半宽是 **1.4**（`RunSession.FireBeam` 的 `perp > 1.4f`）✓
        /// ⇒ 这里取 **1.3**：比几何值大、比引擎判定小 ⇒ 两边都不会互相错过 ✓
        /// （远距离时 `aimJitterDeg=1.5°` 会让某一帧的 perp 越过 1.4 而**漏一帧**，
        ///   这不要紧：激光是连续的，下一帧抖动过去就补上了 ✓）
        /// </summary>
        private const float RowHalfTolerance = 1.3f;

        private struct WaterPlan
        {
            public bool Valid;
            /// <summary>站位（只用到 y：射线要落在两行中间）。</summary>
            public Vector2 Stand;
            /// <summary>射线方向（±X）。</summary>
            public Vector2 Dir;
            /// <summary>这条射线能覆盖的缺水格加权数（评分用）。</summary>
            public int Score;
        }

        /// <summary>这一格现在需要补水吗（"停长"算 3 倍紧急）。</summary>
        private static bool NeedsWater(FarmPlot plot, BotProfile cfg)
        {
            if (plot == null || plot.State != PlotState.Growing) return false;
            if (plot.GrowthPaused) return true;
            return plot.Water < plot.WaterMax * cfg.WaterBelow;
        }

        /// <summary>
        /// 选"从哪条水平射线浇水"：枚举（行对 × 左右方向），取覆盖缺水格最多的那条。
        /// 行对 = (r, r+1)：站在两行中间时一条射线同时覆盖两行 ✓（判定半宽 1.4 &gt; 行距的一半 1.25）
        ///
        /// ⚠️ **两处阈值必须是 1.3，不能是 1.2** ✗ —— 我第一版写成 1.2，而几何上的偏移正好是 1.25
        /// ⇒ `PickWaterPlan` 一格子都评不上、`FirstThirstyOnRay` 永远返回 null ⇒ **Bot 一滴水都不浇** ✓
        /// 1.3 = 几何偏移 1.25 + 一点余量；真正的判定（1.4）由引擎自己做，
        /// 而 `aimJitterDeg=1.5°` 让远距离的格子偶尔漏一帧**不要紧**：激光是连续的，
        /// 一帧没命中只是那一帧的 10 水没送到，下一帧抖动过去就补上了 ✓
        /// </summary>
        private WaterPlan PickWaterPlan(World w, Vector2 from, BotProfile cfg)
        {
            var plan = new WaterPlan { Valid = false };
            if (cfg.WaterBelow <= 0f) return plan;
            FarmGrid grid = w.Farm;
            int rows = grid.Rows;
            if (rows <= 0 || grid.Slots.Length == 0) return plan;

            float usable = Mathf.Max(3f, w.Tuning.tools.can.range - 1f);   // 留 1 米给散布
            int bestScore = 0;

            for (int r = 0; r < rows; r++)
            {
                for (int pair = 0; pair <= 1; pair++)
                {
                    int r2 = r + pair;
                    if (r2 >= rows) continue;
                    float y0 = grid.Slots[r * grid.Size].World.y;
                    float y1 = grid.Slots[r2 * grid.Size].World.y;
                    float y = (y0 + y1) * 0.5f;

                    for (int s = 0; s < 2; s++)
                    {
                        Vector2 dir = s == 0 ? Vector2.right : Vector2.left;
                        int score = 0;
                        for (int i = 0; i < grid.Slots.Length; i++)
                        {
                            FarmPlot plot = grid.Slots[i];
                            if (!NeedsWater(plot, cfg)) continue;
                            if (Mathf.Abs(plot.World.y - y) > RowHalfTolerance) continue;
                            float along = (plot.World.x - from.x) * dir.x;
                            if (along < 0f || along > usable) continue;
                            score += plot.GrowthPaused ? 3 : 1;
                        }
                        if (score > bestScore)
                        {
                            bestScore = score;
                            plan.Valid = true;
                            plan.Stand = new Vector2(from.x, y);
                            plan.Dir = dir;
                            plan.Score = score;
                        }
                    }
                }
            }
            return plan;
        }

        /// <summary>射线覆盖范围内第一个"还需要补水"的格子（决定这一帧到底开不开火）。</summary>
        private FarmPlot FirstThirstyOnRay(World w, Vector2 origin, Vector2 dir, BotProfile cfg)
        {
            FarmGrid grid = w.Farm;
            float usable = Mathf.Max(3f, w.Tuning.tools.can.range - 1f);
            for (int i = 0; i < grid.Slots.Length; i++)
            {
                FarmPlot plot = grid.Slots[i];
                if (!NeedsWater(plot, cfg)) continue;
                Vector2 rel = plot.World - origin;
                float along = rel.x * dir.x;
                if (along < 0f || along > usable) continue;
                if (Mathf.Abs(rel.y) > RowHalfTolerance) continue;
                if (plot.Water >= plot.WaterMax * cfg.WaterStop) continue;
                return plot;
            }
            return null;
        }

        // ------------------------------------------------------------ 施肥
        /// <summary>
        /// v168：施肥是**唯一还活着的 `Interact` 耕作动作**（状态机里 Growing 的
        /// NextAction = Fertilize）✓
        /// 它把本株生长时间 ×0.5（下限 `farm.growthFloor` = 0.35），代价是
        /// `rootWhileActing` 定身 + `FarmingThisFrame` 期间不能攻击 ⇒ 只在身边没敌人时做 ✓
        /// </summary>
        private bool TryFertilize(World w, PlayerActor p, float nearestDist, ref Vector2 move, ref bool interact)
        {
            if (!Profile.Fertilize) return false;
            if (nearestDist < 6.5f) return false;                 // 有敌人就别定身

            if (_fertilizePlot >= 0)
            {
                FarmPlot cur = w.Farm.Plot(_fertilizePlot);
                if (cur == null || cur.Fertilized || cur.State != PlotState.Growing)
                {
                    _fertilizePlot = -1;
                    _fertilizeTimer = 0f;
                }
            }

            if (_fertilizePlot < 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < w.Farm.Slots.Length; i++)
                {
                    FarmPlot plot = w.Farm.Slots[i];
                    if (plot == null || plot.State != PlotState.Growing || plot.Fertilized) continue;
                    if (plot.GrowthRemaining < 1.5f) continue;     // 快熟了，省这一下
                    float d = (plot.World - p.Pos).sqrMagnitude;
                    if (d < best) { best = d; _fertilizePlot = i; }
                }
                if (_fertilizePlot < 0) return false;
                _fertilizeTimer = 0f;
            }

            FarmPlot target = w.Farm.Plot(_fertilizePlot);
            if (target == null) { _fertilizePlot = -1; return false; }

            Vector2 to = target.World - p.Pos;
            float range = w.Tuning.farm.interactRange;
            if (to.magnitude > range * 0.8f)
            {
                move = to.normalized;
                LastDecision = "fert:walk";
                return true;
            }

            // `Interact` 作用在**最近**的已解锁地块上 ⇒ 必须确认最近的那块就是目标，
            // 否则会去挖空地（`Dig` 会把 Empty 变成 Dug ⇒ 反而挡住自动播种）✗
            int near = w.Farm.NearestUnlocked(p.Pos, range);
            if (near != _fertilizePlot) { _fertilizePlot = -1; return false; }

            _fertilizeTimer += GameClock.FixedDt;
            interact = true;
            LastDecision = "fert:hold";
            if (_fertilizeTimer > 1.6f) { _fertilizePlot = -1; _fertilizeTimer = 0f; }
            return true;
        }

        // ------------------------------------------------------------ 捡掉落物
        /// <summary>
        /// v168：捡掉落物。★**雨滴是唯一的水源**★ —— 击杀时 p=0.25 掉一滴，捡到 +20 弹药 ✓
        /// （`World.SpawnPickup` 已在 v167 删除 ⇒ 击杀**不再产出金币/经验**，
        ///   所以"金币与经验只从收割来"在代码层也是真的 ✓ 而雨滴是唯一的弹药补给 ✓）
        ///
        /// 掉落物在 `dropAttractRadius`（2.2 米）内会自己飞向玩家、`pickupRadius`（0.9 米）内入袋
        /// ⇒ Bot 只要**路过**就行，不需要精确站位 ✓
        /// 只在附近没有敌人时去捡，且只跑 8 米以内 ✓（为了一滴水冲进怪堆不划算）
        /// </summary>
        private bool TryCollect(World w, PlayerActor p, float nearestDist, ref Vector2 move)
        {
            if (nearestDist < 5.5f) return false;
            Pickup best = null;
            float bestD = 64f;                                  // 8 米内
            for (int i = 0; i < w.Pickups.Count; i++)
            {
                Pickup pk = w.Pickups[i];
                if (pk == null || pk.Dead) continue;
                float d = (pk.Pos - p.Pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = pk; }
            }
            if (best == null) return false;
            Vector2 to = best.Pos - p.Pos;
            if (to.magnitude < 0.3f) return false;
            move = to.normalized;
            LastDecision = "collect";
            return true;
        }

        // ------------------------------------------------------------ 战斗
        private void Fight(World w, PlayerActor p, EnemyActor nearest, float dist, BotProfile cfg,
            bool danger, ref Vector2 move, ref Vector2 aim, ref bool fire, ref int weapon)
        {
            if (nearest == null || dist > cfg.FightRange)
            {
                // 没有可打目标：回田边待命。**不开火**——激光空放 20 弹药/秒，是全场最贵的动作
                Vector2 post = w.Farm.Slots.Length > 0
                    ? w.Farm.Slots[(w.Farm.Slots.Length - 1) / 2].World
                    : Vector2.zero;
                Vector2 d = post - p.Pos;
                if (d.magnitude > 2.5f) move = d.normalized;
                weapon = PlayerActor.WeaponHoe;
                LastDecision = "guard";
                return;
            }

            Vector2 to = nearest.Pos - p.Pos;
            Vector2 dir = to.sqrMagnitude > 0.0001f ? to.normalized : new Vector2(0f, 1f);
            aim = p.Pos + dir * 3f;

            // ★ v168.1：**弹药决定打法**，这是"活多久"的关键 ★
            //
            // 水壶激光射程 9.5 米 > 远程兵的 6.5 米 ⇒ 有弹药时"边打边退"是严格优势 ✓
            // 而弹药见底时**绝不能冲上去** ✗ —— 锄头只有 3.2 米，冲进怪堆就是
            // 用 12 点血（10+2 护盾）去换一只 10 血的兵，而 grunt 每 0.8 秒 1 点 ⇒
            // 两只贴身 ≈ 5 秒就死完 ✓（玩家移速 5.5 > grunt 3.0 / jumper 3.4 ⇒ 跑是跑得掉的 ✓）
            bool canShoot = _s.WaterAmmo >= 10;
            if (canShoot)
            {
                weapon = PlayerActor.WeaponCan;
                fire = true;
                move = Retreat(w, p, dir, dist);
                LastDecision = "fight:beam";
                return;
            }

            weapon = PlayerActor.WeaponHoe;
            // 已经在锄头射程内才挥，**不主动靠近**（靠近是"追"不是"打"）
            fire = dist <= w.Tuning.tools.hoe.range;
            move = Retreat(w, p, dir, dist);
            LastDecision = "fight:flee-noammo";
        }

        /// <summary>
        /// 退避方向：躲开**所有 7 米内敌人的合力**（不是只躲最近那一个——那会被第二只堵住）✓
        /// 贴边时沿切线滑，而不是"往中心拉"：往中心拉会把 Bot **推回敌群里** ✗
        /// 站位好（离最近敌人 ≥ 3.4 米、且没有敌人贴脸）时原地站着打，不无谓走动 ✓
        /// </summary>
        private Vector2 Retreat(World w, PlayerActor p, Vector2 dirToNearest, float dist)
        {
            Vector2 repulse = Vector2.zero;
            float closest = 99f;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || !e.Alive) continue;
                Vector2 d = p.Pos - e.Pos;
                float d2 = d.sqrMagnitude;
                if (d2 > 49f) continue;                       // 7 米以外不参与
                float len = Mathf.Sqrt(d2);
                if (len < closest) closest = len;
                repulse += (len > 0.001f ? d / len : Vector2.right) / Mathf.Max(0.6f, len);
            }

            if (repulse.sqrMagnitude < 0.0001f)
                return dist > 3.4f ? Vector2.zero : -dirToNearest;     // 没有贴脸的：站着打

            Vector2 away = repulse.normalized;
            float lim = w.Tuning.farm.arenaHalf * 0.72f;
            if (p.Pos.magnitude > lim)
            {
                var tangent = new Vector2(-away.y, away.x);
                if (Vector2.Dot(tangent, -p.Pos) < 0f) tangent = -tangent;   // 选更靠场内的一侧
                away = (away * 0.35f + tangent).normalized;
            }
            return away;
        }

        // ------------------------------------------------------------ 卡住脱困
        private Vector2 Unstick(PlayerActor p, Vector2 move, bool farming, float dt)
        {
            if (_haveLastPos && !farming && move.sqrMagnitude > 0.001f)
            {
                if ((p.Pos - _lastPos).sqrMagnitude < 0.0004f) _stuckTimer += dt;
                else _stuckTimer = 0f;
            }
            else _stuckTimer = 0f;
            _lastPos = p.Pos;
            _haveLastPos = true;

            if (_stuckTimer > 0.7f) { _detourTimer = 0.9f; _stuckTimer = 0f; }
            if (_detourTimer > 0f)
            {
                _detourTimer -= dt;
                if (move.sqrMagnitude > 0.001f)
                {
                    var perp = new Vector2(-move.y, move.x);
                    if (p.Pos.x < 0f) perp = -perp;      // 贴左墙就向右绕（确定性，不用随机数）
                    move = (move + perp * 0.9f).normalized;
                }
            }
            return move;
        }

        // ------------------------------------------------------------ 度量：观测上一帧的产出
        /// <summary>
        /// 快照对比法：Bot 只读 World，不往 RunSession 加字段 ✓
        /// 逐格记录（状态, 蓄水），于是"收割了几次 / 浇了多少水"变成可数的整数：
        ///   · 成熟 → 重新生长 = 一次收割（Grown → Growing 只有 `Harvest` 这一条路）✓
        ///   · 成熟/生长中 → 空地 = 被释放或死亡（不是收割）✗ 分开计数，避免把丢失当成收益 ✓
        /// </summary>
        private void Observe()
        {
            World w = _s.World;
            if (w == null || w.Farm == null) return;
            FarmPlot[] slots = w.Farm.Slots;

            if (_seenState.Length != slots.Length)
            {
                var st = new PlotState[slots.Length];
                var wt = new int[slots.Length];
                int keep = Mathf.Min(_seenState.Length, slots.Length);
                for (int i = 0; i < keep; i++) { st[i] = _seenState[i]; wt[i] = _seenWater[i]; }
                for (int i = keep; i < slots.Length; i++) { st[i] = slots[i].State; wt[i] = slots[i].Water; }
                _seenState = st;
                _seenWater = wt;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot plot = slots[i];
                PlotState now = plot.State;
                PlotState was = _seenState[i];
                if (now == was && plot.Water == _seenWater[i]) continue;

                if (was == PlotState.Grown && now == PlotState.Growing)
                {
                    Harvests++;
                    SeedDef def = SeedOf(plot.CropSeedId);
                    HarvestGold += def != null ? Mathf.Max(1, def.Price / 5) : 1;
                    HarvestExp += def != null ? Mathf.Max(1, def.Price / 10) : 1;
                }
                else if ((was == PlotState.Grown || was == PlotState.Growing) && now == PlotState.Empty)
                {
                    PlotsLost++;
                }

                int dw = plot.Water - _seenWater[i];
                if (dw > 0) WaterDelivered += dw;

                _seenState[i] = now;
                _seenWater[i] = plot.Water;
            }

            int plants = w.Farm.PlantCount;
            if (_seenPlants < 0) _seenPlants = plants;
            else if (plants > _seenPlants) { SownColumns += plants - _seenPlants; _seenPlants = plants; }
            else if (plants < _seenPlants) _seenPlants = plants;
        }

        private static SeedDef SeedOf(string id)
            => string.IsNullOrEmpty(id) ? null : TableDatabase.GetSeed(id);
    }
}
