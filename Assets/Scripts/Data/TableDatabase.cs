using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 配置表加载与校验。数值的唯一来源是 Assets/Resources/Data/*.json。
    /// 任何非法配置都在这里失败并报错，绝不静默取默认值。
    /// </summary>
    public static class TableDatabase
    {
        public static TuningDef Tuning { get; private set; }
        public static SeedDef[] Seeds { get; private set; } = Array.Empty<SeedDef>();
        public static MutationDef[] Mutations { get; private set; } = Array.Empty<MutationDef>();
        public static EnemyDef[] Enemies { get; private set; } = Array.Empty<EnemyDef>();
        public static WaveDef[] Waves { get; private set; } = Array.Empty<WaveDef>();
        public static ItemDef[] Items { get; private set; } = Array.Empty<ItemDef>();

        /// <summary>
        /// ★★ v314：**能被随机到的道具**（= `Items` 里 `NoDrop == false` 的那些）★★
        ///
        /// 用户原话（关于「陨落天使的羽毛」）：「此道具只有复活后产生（不会刷新）」。
        /// ⇒ 它必须在**每一条**随机路上都抽不到：商店掷档、`RandomItemOfGrade` 的回退、
        ///   任何按 grade 抽道具的地方。一个数组就是"能不能抽到"的唯一真相 ✓
        ///
        /// ★ 三种读法（都指向同一份数据）★
        ///   · `Items`         —— **全表**（图鉴 / 道具栏 / 训练场仓库 / 自检统计要用全部）
        ///   · `RollableItems` —— **能抽到的**（本属性）
        ///   · `RollableItems == Items`（引用相等）⇒ 全表都能抽，下游可以走"不过滤"的快路
        /// ⚠️ 只读：别往返回的数组里写（它可能就是要出货的那一份）✓
        /// </summary>
        public static ItemDef[] RollableItems { get; private set; } = Array.Empty<ItemDef>();
        /// <summary>
        /// 插件商品的**静态定义**（商店第三类）。
        /// 来源是 `Assets/Resources/Data/plugins.json`，实例化成 `PluginAsset` 运行时实例 ✓
        /// （为什么不是 `.asset` SO 资产：见 `PluginDefRaw` 的说明）
        /// </summary>
        public static PluginAsset[] Plugins { get; private set; } = Array.Empty<PluginAsset>();
        /// <summary>v47：敌人成长（生命/伤害的 2 的回合次方）。EnemyDef 的公式读它，兜底非空。</summary>
        public static EnemyGrowthDef EnemyGrowth { get; private set; } = new EnemyGrowthDef();
        public static bool Loaded { get; private set; }
        public static string LastError { get; private set; } = "";

        private static readonly Dictionary<string, SeedDef> SeedById = new Dictionary<string, SeedDef>();
        private static readonly Dictionary<string, MutationDef> MutationById = new Dictionary<string, MutationDef>();
        private static readonly Dictionary<string, EnemyDef> EnemyById = new Dictionary<string, EnemyDef>();
        private static readonly Dictionary<int, WaveDef> WaveByIndex = new Dictionary<int, WaveDef>();
        /// <summary>v165：越界复用最后一条时只播报一次（见 <see cref="GetWave"/>）。</summary>
        private static bool _beyondTableLogged;
        private static readonly Dictionary<string, ItemDef> ItemById = new Dictionary<string, ItemDef>();
        private static readonly List<SeedDef>[] ByGrade = new List<SeedDef>[5];

        public static void Load(bool force = false)
        {
            if (Loaded && !force) return;
            SeedById.Clear();
            MutationById.Clear();
            EnemyById.Clear();
            WaveByIndex.Clear();
            _beyondTableLogged = false;
            ItemById.Clear();
            for (int i = 0; i < ByGrade.Length; i++) ByGrade[i] = new List<SeedDef>();

            Tuning = LoadJson<TuningDef>("Data/tuning");
            NormalizeTuning(Tuning);

            SeedTableRaw seedTable = LoadJson<SeedTableRaw>("Data/seeds");
            EnemyTableRaw enemyTable = LoadJson<EnemyTableRaw>("Data/enemies");
            WaveTableRaw waveTable = LoadJson<WaveTableRaw>("Data/waves");
            ItemTableRaw itemTable = LoadJson<ItemTableRaw>("Data/items");

            var seedList = new List<SeedDef>();
            foreach (SeedDefRaw raw in seedTable.seeds)
            {
                var d = new SeedDef
                {
                    Id = raw.id,
                    Name = raw.name,
                    Desc = raw.desc,
                    Special = string.IsNullOrEmpty(raw.special) ? "None" : raw.special,
                    Grade = DefParser.ParseEnum(raw.grade, SeedGrade.D),

                    Mechanic = string.IsNullOrEmpty(raw.mechanic) ? "None" : raw.mechanic,
                    WaterMax = raw.waterMax > 0 ? raw.waterMax : 20,
                    WaterDrain = raw.waterDrain,
                    AttackType = DefParser.ParseEnum(raw.attackType, AttackType.Physical),
                    AttackKind = DefParser.ParseEnum(raw.attackKind, AttackKind.Melee),
                    Support = DefParser.ParseEnum(raw.supportMode, SupportMode.None),
                    BaseValue = raw.baseValue,
                    InheritMult = raw.inheritMult,
                    GrowTime = raw.growTime,
                    BaseHp = raw.baseHp,
                    BaseShield = raw.baseShield,
                    AttackInterval = raw.attackInterval,
                    Range = raw.range,
                    MoveSpeed = raw.moveSpeed,
                    Price = raw.price,
                    // v179：收益基数与售价脱钩（见 SeedDefRaw.goldYield 的说明）
                    GoldYield = raw.goldYield,
                    ExpYield = raw.expYield
                };
                seedList.Add(d);
            }
            Seeds = seedList.ToArray();

            var mutList = new List<MutationDef>();
            foreach (MutationDefRaw raw in seedTable.mutations)
            {
                mutList.Add(new MutationDef
                {
                    Id = raw.id, Name = raw.name, Desc = raw.desc, InheritMultBonus = raw.inheritMultBonus
                });
            }
            Mutations = mutList.ToArray();

            var enemyList = new List<EnemyDef>();
            foreach (EnemyDefRaw raw in enemyTable.enemies)
            {
                enemyList.Add(new EnemyDef
                {
                    Id = raw.id,
                    Name = raw.name,
                    Kind = DefParser.ParseEnum(raw.kind, EnemyKind.Grunt),
                    ColorHex = raw.color,
                    Color = DefParser.ParseColor(raw.color, Color.red),
                    BaseHp = raw.baseHp,
                    MoveSpeed = raw.moveSpeed,
                    TouchDamage = raw.touchDamage,
                    TouchCooldown = raw.touchCooldown,
                    AttackRange = raw.attackRange,
                    SafeRange = raw.safeRange,
                    ProjectileDamage = raw.projectileDamage,
                    ProjectileSpeed = raw.projectileSpeed,
                    AttackInterval = raw.attackInterval,
                    Radius = raw.radius,
                    JumpDamage = raw.jumpDamage,
                    JumpRadius = raw.jumpRadius,
                    JumpCooldown = raw.jumpCooldown,
                    JumpRange = raw.jumpRange,
                    JumpWindup = raw.jumpWindup,
                    Gold = raw.gold,
                    Exp = raw.exp
                });
            }
            Enemies = enemyList.ToArray();

            var waveList = new List<WaveDef>();
            foreach (WaveDefRaw raw in waveTable.waves)
            {
                var batches = new List<WaveBatchDef>();
                foreach (WaveBatchDefRaw b in raw.batches)
                {
                    batches.Add(new WaveBatchDef
                    {
                        EnemyId = b.enemyId, Count = b.count, StartAt = b.startAt, Interval = b.interval
                    });
                }
                waveList.Add(new WaveDef
                {
                    Index = raw.index, IsBoss = raw.isBoss, Tip = raw.tip,
                    Batches = batches.ToArray()
                });
            }
            Waves = waveList.ToArray();

            var itemList = new List<ItemDef>();
            foreach (ItemDefRaw raw in itemTable.items)
            {
                itemList.Add(new ItemDef
                {
                    Id = raw.id, Name = raw.name, Desc = raw.desc,
                    Price = raw.price, MaxStack = raw.maxStack,
                    Grade = DefParser.ParseEnum(raw.grade, SeedGrade.D),
                    // v168：三类别道具
                    Category = raw.category,
                    Effects = ParseBonuses(raw.effects),
                    Costs = ParseBonuses(raw.costs),
                    Rule = raw.rule ?? "",
                    RuleValue = raw.ruleValue,
                    GrowPerWave = raw.growPerWave,
                    // ★ v314：**不进随机池**标记（缺键 = false ⇒ 老行逐位不变）
                    NoDrop = raw.noDrop
                });
            }
            Items = itemList.ToArray();

            // ★★ v314：**可抽取道具 = 表里 `noDrop == false` 的那些** —— 唯一实现 ★★
            //
            // 为什么要单独存一个数组，而不是在每次抽取时判 `NoDrop`：
            //   ① `RandomItemOfGrade` 的掷法是"先数该档位有几个、再取第 k 个"——
            //      如果只在取的那一刻跳过 `NoDrop`，数量与序号就对不上（会取到 null 或错位）✗
            //   ② 商店侧 `RunSession.ItemCandidates()` 需要"过滤后的整份数组"（它可能**直接返回**
            //      原引用以避免分配）⇒ 一个预过滤好的数组天然满足这两条 ✓
            // 语义：`Items` 仍是**全表**（图鉴 / 道具栏 / 训练场仓库要看到全部）；
            //       `RollableItems` 是**能抽到的**（商店 / 任何 grade 抽取 / 掉落）。
            if (itemList.Exists(it => it.NoDrop))
            {
                var roll = new List<ItemDef>(itemList.Count);
                for (int i = 0; i < itemList.Count; i++) if (!itemList[i].NoDrop) roll.Add(itemList[i]);
                RollableItems = roll.ToArray();
            }
            else
            {
                // 全表都能抽 ⇒ **直接共用同一个数组引用**（零分配，且下游的 `ReferenceEquals`
                // 判断（"池没被过滤"的快路）照旧成立）✓
                RollableItems = Items;
            }

            // ── 插件商品（商店第三类）──
            // 表里是原始行，这里实例化成 `PluginAsset` ⇒ 下游（ShopOffer / PlayerGoods /
            // WeaponSystem）看到的仍然是同一个类型，不需要知道它从哪来 ✓
            PluginTableRaw pluginTable = LoadJson<PluginTableRaw>("Data/plugins");
            var pluginList = new List<PluginAsset>();
            foreach (PluginDefRaw raw in pluginTable.plugins)
            {
                if (raw == null || string.IsNullOrEmpty(raw.id)) continue;
                var p = ScriptableObject.CreateInstance<PluginAsset>();
                p.id = raw.id;
                p.displayName = string.IsNullOrEmpty(raw.displayName) ? raw.id : raw.displayName;
                p.description = raw.description ?? "";
                p.quality = DefParser.ParseEnum(raw.quality, GoodsQuality.D);
                // ★ 阶段 3：把**表里的字面值**留在资产上 ★
                // 理由不是"方便"，而是"可诊断"：`effect` 解析失败会回落到 `None`，
                // 而报告里必须说清"表里写的是哪一个名字"（自检 `PLUGIN-NO-STAT` 就靠它点名）✗
                // 只留枚举的话，一个写错的效果在报告里只剩下 `None`，等于告诉人"这里没写" ✓
                p.rawEffect = raw.effect ?? "";
                // 空数组 = 任意武器（`PluginAsset.Fits` 的判据）✓
                if (raw.allowedWeapons == null || raw.allowedWeapons.Length == 0)
                {
                    p.allowedWeapons = Array.Empty<WeaponSlot>();
                }
                else
                {
                    var slots = new WeaponSlot[raw.allowedWeapons.Length];
                    for (int i = 0; i < raw.allowedWeapons.Length; i++)
                        slots[i] = (WeaponSlot)Mathf.Clamp(raw.allowedWeapons[i], (int)WeaponSlot.Hoe, (int)WeaponSlot.Fertilizer);
                    p.allowedWeapons = slots;
                }
                p.effect = DefParser.ParseEnum(raw.effect, PluginEffectKind.None);
                p.amount = raw.amount;
                p.amount2 = raw.amount2;
                p.stackRule = DefParser.ParseEnum(raw.stackRule, StackRule.Stackable);
                p.maxStack = Mathf.Max(1, raw.maxStack);
                p.exclusivePerWeapon = raw.exclusivePerWeapon;
                p.basePrice = Mathf.Max(1, raw.basePrice);
                pluginList.Add(p);
            }
            Plugins = pluginList.ToArray();

            Validate();
            Loaded = true;
            Log.Info($"配置表载入完成：种子 {Seeds.Length}｜变异 {Mutations.Length}｜敌人 {Enemies.Length}｜波次 {Waves.Length}｜道具 {Items.Length}｜插件 {Plugins.Length}");
        }

        /// <summary>
        /// 把表里的效果行解析成运行时效果（v168）。
        ///
        /// ★ 属性名解析失败**不静默兜底** ★
        /// `StatIds.TryParse` 失败时旧代码是 `out StatId stat` = `MaxHp`（枚举默认值），
        /// 于是一个把属性名写错的道具会**悄悄变成"最大生命 +N"** ✗
        /// 那正是"看着活着、实际死的"最坏形态：不报错、不崩溃，只是效果完全不是设计的那条。
        /// 现在：解析不了就 `Log.Error` 并跳过该条 —— 表校验会因此看到"效果数为 0"而拦下来 ✓
        /// </summary>
        private static ItemBonus[] ParseBonuses(ItemBonusRaw[] raw)
        {
            if (raw == null || raw.Length == 0) return Array.Empty<ItemBonus>();
            var list = new List<ItemBonus>(raw.Length);
            foreach (ItemBonusRaw b in raw)
            {
                if (b == null) continue;
                if (!StatIds.TryParse(b.stat, out StatId stat))
                {
                    Log.Error($"道具效果引用了不存在的属性：{b.stat}（该条已丢弃）");
                    continue;
                }
                list.Add(new ItemBonus
                {
                    Stat = stat,
                    Add = b.add,
                    IsPercent = string.Equals(b.mode, "Percent", StringComparison.OrdinalIgnoreCase)
                });
            }
            return list.ToArray();
        }

        private static void NormalizeTuning(TuningDef t)
        {
            if (t == null) throw new InvalidOperationException("Data/tuning.json 解析失败");
            t.player ??= new PlayerBaseDef();
            t.growth ??= new GrowthDef();
            t.caps ??= new CapsDef();
            t.farm ??= new FarmDef();
            t.formula ??= new FormulaDef();
            // ★ v282：吸血上限段缺失 ⇒ 补一份（`LifestealDef` 的字段初始值就是设计值 5% / 3%）
            //   不补的话 `JsonUtility.FromJson` 会把它留成 null ⇒ 两条上限都读成 0 = **关闭**
            //   ⇒ 老表跑起来会静默回到"群怪线性回血"那个 bug。补一份才是"老表不改也能跑对" ✓
            t.lifesteal ??= new LifestealDef();
            t.tools ??= new ToolsDef();
            t.tools.hoe ??= new ToolDef();
            t.tools.can ??= new ToolDef();
            t.tools.fertilizer ??= new ToolDef();
            t.shop ??= new ShopDef();
            t.seedBag ??= new SeedBagDef();
            t.quality ??= new QualityDef();
            t.waves ??= new WavesCfg();
            t.enemies ??= new EnemyGrowthDef();
            EnemyGrowth = t.enemies;
            if (t.enemies.expCap <= 0f) t.enemies.expCap = 64f;
            if (t.enemies.hpGrowth < 0f) t.enemies.hpGrowth = 0f;
            if (t.enemies.dmgGrowth < 0f) t.enemies.dmgGrowth = 0f;
            t.camera ??= new CameraDef();
            t.ui ??= new UiDef();
            t.meta ??= new MetaDef();
            if (t.camera.orthoSize <= 0f) t.camera.orthoSize = 7.5f;
            if (t.camera.pitch <= 0f) t.camera.pitch = 60f;
            if (t.camera.followLerp <= 0f) t.camera.followLerp = 10f;
            if (t.ui.scale <= 0f) t.ui.scale = 1f;
            if (t.ui.popupRadius <= 0f) t.ui.popupRadius = 0.6f;
            if (t.ui.popupFontSize <= 0f) t.ui.popupFontSize = 26f;
            if (t.tools.switchWindup <= 0f) t.tools.switchWindup = 0.5f;
            // v45：攻击倍率是伤害的乘区，缺表/填 0 会让所有伤害归零——必须兜底到 100%
            if (t.player.atkMult <= 0f) t.player.atkMult = 1f;
            // v162：三种伤害类型各自非负。注意这里**不用**兜底到 1 ——
            //       0 是合法设计（"这件武器暂时不吃玩家成长"），而乘区 atkMult 的 0 是致命的，两者性质不同。
            if (t.player.meleeDamage < 0f) t.player.meleeDamage = 0f;
            if (t.player.rangedDamage < 0f) t.player.rangedDamage = 0f;
            if (t.player.blastDamage < 0f) t.player.blastDamage = 0f;
            if (t.formula.plantProjectileSpeed <= 0f) t.formula.plantProjectileSpeed = 18f;
            if (t.formula.ailmentDelayPoison <= 0f) t.formula.ailmentDelayPoison = 2f;
            if (t.toolUpgrades == null) t.toolUpgrades = Array.Empty<ToolUpgradeDef>();
            if (t.levelReward.stats == null || t.levelReward.stats.Length == 0)
                t.levelReward.stats = Array.Empty<RewardStatDef>();
            if (t.levelReward.patterns == null || t.levelReward.patterns.Length == 0)
                t.levelReward.patterns = new[]
                {
                    new RewardPatternDef { kind = "AllDiff", weight = 60 },
                    new RewardPatternDef { kind = "TwoSame", weight = 30 },
                    new RewardPatternDef { kind = "AllSame", weight = 10 }
                };
            if (t.levelReward.drawsPerLevel <= 0) t.levelReward.drawsPerLevel = 1;
            if (t.levelReward.rerollBasePrice <= 0) t.levelReward.rerollBasePrice = 3;
            if (t.levelReward.rerollStepPrice <= 0) t.levelReward.rerollStepPrice = 2;
            if (t.shop.itemGradeWeights == null || t.shop.itemGradeWeights.Length == 0)
                t.shop.itemGradeWeights = new[]
                {
                    new GradeWeightDef { grade = "D", weight = 50 },
                    new GradeWeightDef { grade = "C", weight = 28 },
                    new GradeWeightDef { grade = "B", weight = 15 },
                    new GradeWeightDef { grade = "A", weight = 6 },
                    new GradeWeightDef { grade = "S", weight = 1 }
                };
            // ★ v253：前期高品质抑制的两个旋钮（缺键/非法值一律回落到"与改前一致"）
            //   `floor <= 0` 会让高档权重直接变 0 ⇒ 后期也抽不到 ⇒ 必须兜底到 0.15 ✓
            //   `rampWaves <= 1` 会让 t 的分母为 0 ⇒ 兜底到 10 ✓
            if (t.shop.highTierEarlyFloor <= 0f || t.shop.highTierEarlyFloor > 1f) t.shop.highTierEarlyFloor = 0.15f;
            if (t.shop.highTierRampWaves < 2) t.shop.highTierRampWaves = 10;
            // ★★ v277：`shop.rarityCurve` 的兜底（高品质概率的时间膨胀公式）★★
            //   ① 整块缺键 ⇒ 用字段初值那套（0.6% / 4.6% / 10% / gamma 3.5 / kA 2.5 / kB 5）✓
            //   ② 单个键缺 ⇒ JsonUtility 给 0 ⇒ 这里按"是不是明显没填"回落到同一个默认值。
            //      为什么 `s0 <= 0` 要回落而不是"就用 0"：开局 S 恒 0 ⇒ 前几分钟一件 S 都没有，
            //      而 `WeatherSeedPitySlots` 之外再没有别的高档来源 ⇒ 等于悄悄改了出货口 ✗
            //   ③ `sMax` 夹到 (0, 1]；`s10` 夹到 [s0, sMax]（s10 > sMax 会让"10 分钟 < 5%"与
            //      "最高 10%" 互相打架，两条件同时成立才算数 ⇒ 以 sMax 为准）✓
            //   ④ `gamma / kneeMinutes` 必须 > 0（除法在 `RarityCurveDef.SAt` 里，0 会算出 NaN）✓
            {
                if (t.shop.rarityCurve == null) t.shop.rarityCurve = new RarityCurveDef();
                RarityCurveDef rc = t.shop.rarityCurve;
                if (rc.sMax <= 0f || rc.sMax > 1f) rc.sMax = 0.10f;
                if (rc.s0 <= 0f || rc.s0 > rc.sMax) rc.s0 = 0.006f;
                if (rc.s10 <= 0f || rc.s10 > rc.sMax) rc.s10 = 0.046f;
                if (rc.s10 < rc.s0) rc.s10 = rc.s0;
                if (rc.gamma <= 0f) rc.gamma = 3.5f;
                if (rc.kneeMinutes <= 0.01f) rc.kneeMinutes = 10f;
                if (rc.postKneePerMinute < 0f) rc.postKneePerMinute = 0f;
                if (rc.kA < 0f) rc.kA = 2.5f;
                if (rc.kB < 0f) rc.kB = 5f;
                if (rc.lowCap <= 0f || rc.lowCap > 1f) rc.lowCap = 0.90f;
            }
            if (t.shop.itemGradePriceStep <= 0f) t.shop.itemGradePriceStep = 0.45f;
            if (t.shop.priceWaveStep <= 0f) t.shop.priceWaveStep = 0.03f;
            // ★ v195：刷新/重抽价有自己的波次斜率（见 `ShopDef.RerollWaveMult`）。
            //   缺键回落 0.0682 = 表里写的默认值（第 20 波 3.50×、第 30 波 10.16×）✓
            if (t.shop.rerollWaveStep <= 0f) t.shop.rerollWaveStep = 0.0682f;
            // v166：田地形状 = gridSize 列 × [initialRows, maxRows] 行
            if (t.farm.gridSize <= 0) t.farm.gridSize = 6;
            if (t.farm.maxRows <= 0) t.farm.maxRows = t.farm.gridSize;
            if (t.farm.initialRows <= 0) t.farm.initialRows = 3;
            if (t.farm.initialRows > t.farm.maxRows) t.farm.initialRows = t.farm.maxRows;
            if (t.farm.rowGrowthSeconds <= 0f) t.farm.rowGrowthSeconds = 300f;   // 5 分钟
            // ★ v195：收割金币全局系数（见 `FarmDef.harvestGoldScale` 的实算）——
            //   缺键/≤0 一律回落 1（乘 0 会把收割变成零收益 ✗）
            if (t.farm.harvestGoldScale <= 0f) t.farm.harvestGoldScale = 1f;
            // ★ v195：爆炸收割收益 1/4（用户原话"改为获取 1/4 金币和经验"）——
            //   `≤ 0` 或 `> 1` 兜底到同一个 1/4（表里漏键时 JsonUtility 给 0，乘 0 = 爆炸收割零收益 ✗）
            if (t.farm.blastHarvestRatio <= 0f || t.farm.blastHarvestRatio > 1f) t.farm.blastHarvestRatio = 0.25f;
            if (t.tools.workCapMax <= 1) t.tools.workCapMax = 4;
            // ★ v282：`shop.gradeWeights`（旧种子表）已**整体退役** —— 品阶只剩
            //   `shop.itemGradeWeights` 一张（三类共用），见上面那段兜底与 `ShopDef.itemGradeWeights` ✓
            // 品质表兜底：白/绿/蓝/紫/金
            if (t.quality.names == null || t.quality.names.Length < 5)
                t.quality.names = new[] { "白", "绿", "蓝", "紫", "金" };
            if (t.quality.mult == null || t.quality.mult.Length != t.quality.names.Length)
                t.quality.mult = new[] { 1.0f, 1.15f, 1.32f, 1.52f, 1.75f };
            if (t.quality.shopWeights == null || t.quality.shopWeights.Length != t.quality.names.Length)
                t.quality.shopWeights = new[] { 50, 27, 14, 7, 2 };
        }

        private static T LoadJson<T>(string resourcePath) where T : class
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                string msg = $"缺少配置表 Assets/Resources/{resourcePath}.json";
                LastError = msg;
                Log.Error(msg);
                throw new InvalidOperationException(msg);
            }
            try
            {
                T obj = JsonUtility.FromJson<T>(asset.text);
                if (obj == null) throw new InvalidOperationException("解析结果为空");
                return obj;
            }
            catch (Exception e)
            {
                string msg = $"{resourcePath}.json 解析失败：{e.Message}";
                LastError = msg;
                Log.Error(msg);
                throw;
            }
        }

        public static void Validate()
        {
            var errors = new StringBuilder();

            if (Seeds.Length == 0) errors.AppendLine("seeds.json 为空");
            foreach (SeedDef s in Seeds)
            {
                if (string.IsNullOrEmpty(s.Id)) errors.AppendLine("种子缺少 id");
                else if (SeedById.ContainsKey(s.Id)) errors.AppendLine($"种子 id 重复：{s.Id}");
                else SeedById[s.Id] = s;
                // v23：支援型植物（不攻击）允许 baseValue = 0
                if (s.BaseValue < 0f) errors.AppendLine($"种子 {s.Id} baseValue 不能为负");
                if (s.GrowTime <= 0f) errors.AppendLine($"种子 {s.Id} growTime 必须 > 0");
                if (s.InheritMult <= 0f) errors.AppendLine($"种子 {s.Id} inheritMult 必须 > 0");
                if (s.BaseHp <= 0f) errors.AppendLine($"种子 {s.Id} baseHp 必须 > 0");
                if (s.Price <= 0) errors.AppendLine($"种子 {s.Id} price 必须 > 0");
                // v179：收益基数必须显式给出 —— 缺字段会被反序列化成 0，
                // 于是收割恒为保底 1 金 1 验（不报错、只是全表收益塌掉）⇒ 这里判红，宁可不启动
                if (s.GoldYield <= 0) errors.AppendLine($"种子 {s.Id} goldYield 必须 > 0（v179 起收益不再由 price 推导）");
                if (s.ExpYield <= 0) errors.AppendLine($"种子 {s.Id} expYield 必须 > 0（v179 起收益不再由 price 推导）");
                if (s.AttackType != AttackType.Support && s.AttackInterval <= 0f)
                    errors.AppendLine($"种子 {s.Id} 攻击类必须有 attackInterval > 0");
                ByGrade[(int)s.Grade].Add(s);
            }

            foreach (MutationDef m in Mutations)
            {
                if (string.IsNullOrEmpty(m.Id)) errors.AppendLine("变异缺少 id");
                else if (MutationById.ContainsKey(m.Id)) errors.AppendLine($"变异 id 重复：{m.Id}");
                else MutationById[m.Id] = m;
            }

            if (Enemies.Length == 0) errors.AppendLine("enemies.json 为空");
            foreach (EnemyDef e in Enemies)
            {
                if (string.IsNullOrEmpty(e.Id)) errors.AppendLine("敌人缺少 id");
                else if (EnemyById.ContainsKey(e.Id)) errors.AppendLine($"敌人 id 重复：{e.Id}");
                else EnemyById[e.Id] = e;
                if (e.BaseHp <= 0f) errors.AppendLine($"敌人 {e.Id} baseHp 必须 > 0");
                if (e.Radius <= 0f) errors.AppendLine($"敌人 {e.Id} radius 必须 > 0");
            }

            if (Waves.Length == 0) errors.AppendLine("waves.json 为空");
            foreach (WaveDef w in Waves)
            {
                if (WaveByIndex.ContainsKey(w.Index)) errors.AppendLine($"波次 index 重复：{w.Index}");
                else WaveByIndex[w.Index] = w;
                // v165：删掉 `!w.IsBoss && w.Duration <= 0f` 校验 ✗ —— duration 字段已随旧节奏一起删除
                foreach (WaveBatchDef b in w.Batches)
                {
                    if (!EnemyById.ContainsKey(b.EnemyId))
                        errors.AppendLine($"波次 {w.Index} 引用了不存在的敌人 id：{b.EnemyId}");
                    if (b.Count <= 0) errors.AppendLine($"波次 {w.Index} 的 {b.EnemyId} count 必须 > 0");
                    if (b.Interval <= 0f) errors.AppendLine($"波次 {w.Index} 的 {b.EnemyId} interval 必须 > 0");
                }
            }

            foreach (ItemDef it in Items)
            {
                if (string.IsNullOrEmpty(it.Id)) errors.AppendLine("道具缺少 id");
                else if (ItemById.ContainsKey(it.Id)) errors.AppendLine($"道具 id 重复：{it.Id}");
                else ItemById[it.Id] = it;

                // ── v168：三类别的结构约束 ──
                // ★ 这些不是"格式洁癖"，每一条都拦掉一种"买了没反应"的道具 ★
                if (it.Category < 1 || it.Category > 3)
                    errors.AppendLine($"道具 {it.Id} 的 category 必须是 1/2/3（当前 {it.Category}）");
                if (it.Price <= 0) errors.AppendLine($"道具 {it.Id} 的 price 必须 > 0");
                if (it.MaxStack <= 0) errors.AppendLine($"道具 {it.Id} 的 maxStack 必须 > 0");
                if (it.Effects.Length == 0 && !it.HasRule)
                    errors.AppendLine($"道具 {it.Id} 既没有 effects 也没有可解释的 rule ⇒ 买了不会有任何效果");
                if (!string.IsNullOrEmpty(it.Rule) && !ItemRules.IsKnown(it.Rule))
                    errors.AppendLine($"道具 {it.Id} 的 rule「{it.Rule}」没有解释器（ItemRules.All 里没有它）");

                // v11 约定：数值增减必须是整数；倍率修改必须是整数%
                //
                // ★ v179：唯一的例外是 `CritRate`（见 `IsFractionStat`）★
                // ★ v3xx：`gamble_glove` 的两条 `Percent −9.9`（移速/攻速）需要一个**更窄**的例外
                //   —— `AllowsFraction`（= `IsFractionStat` ∪ {MoveSpeed, AtkSpeed}）✓
                //   为什么不用 `IsFractionStat` 见那个函数的注释（会改掉抽卡的单位语义）✓
                foreach (ItemBonus b in it.Effects)
                    if (!AllowsFraction(b.Stat) && Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f)
                        errors.AppendLine($"道具 {it.Id} 的增益 {b.Stat} 必须为整数（当前 {b.Add}）");
                foreach (ItemBonus b in it.Costs)
                    if (!AllowsFraction(b.Stat) && Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f)
                        errors.AppendLine($"道具 {it.Id} 的减益 {b.Stat} 必须为整数（当前 {b.Add}）");

                // ★ 类别 2 的两条硬约束（用户标准："必须有增益与减益两半，且增益大于减益"）★
                if (it.Category == 2)
                {
                    if (it.Costs.Length == 0)
                        errors.AppendLine($"道具 {it.Id} 是类别 2（有牺牲的增加）却没有 costs ⇒ 它是件纯增益");
                    if (it.Effects.Length == 0)
                        errors.AppendLine($"道具 {it.Id} 是类别 2 却没有 effects ⇒ 只有牺牲没有收益");

                    // 逐属性比大小：同一条属性上，增益必须严格大于减益（跨属性无法比较，不做判断）
                    foreach (ItemBonus cost in it.Costs)
                    {
                        float gain = 0f;
                        bool has = false;
                        foreach (ItemBonus e in it.Effects)
                            if (e.Stat == cost.Stat) { gain += Mathf.Abs(e.Add); has = true; }
                        if (!has) continue;
                        if (gain <= Mathf.Abs(cost.Add))
                            errors.AppendLine($"道具 {it.Id} 在 {cost.Stat} 上增益 {gain} 不大于减益 {Mathf.Abs(cost.Add)}");
                    }
                }
                // 类别 1 / 3 不该有减益（"有牺牲"是类别 2 的定义）
                //
                // ★★ v197：**类别 3 放开 costs**（用户本轮 10 件机制道具里的 4 件带真代价）★★
                //   旧规则把 cat3 与"无代价"绑在一起 ✗ 而本轮有 4 件是"机制 + 真代价"：
                //     `windup_gear` 攻速 −10% / `charge_coil` 伤害倍率 −10%
                //     / `aegis_lamp` 护盾 −1 / `sharp_spray` 远程伤害 −5 / `tycoon_coin` 伤害倍率 −20%
                //   两条路都不通：
                //     ① 硬塞进类别 2 ⇒ 得给它们编一个**不存在的增益**（那是造假）✗
                //     ② 把代价挪进代码 ⇒ 表里看不到的减益 = **第二份真相**（改表改不动它）✗
                //   ⇒ 只放开 cat3；**cat1 仍然一律无代价**（纯增益的定义没变）✓
                //   判据也跟着变清楚：**"有代价"不再等于"类别 2"**，
                //   类别 2 = 有代价**且**没有机制；类别 3 = 有机制（可以有代价）✓
                if (it.Category == 1 && it.Costs.Length > 0)
                    errors.AppendLine($"道具 {it.Id} 是类别 1（纯增益）却带了 costs ⇒ 应归入类别 2 或 3");
                // 类别 1 必须有数值；类别 3 必须有机制（否则这个分类是空标签）
                if (it.Category == 1 && it.Effects.Length == 0)
                    errors.AppendLine($"道具 {it.Id} 是类别 1（纯数值）却没有 effects");
                if (it.Category == 3 && !it.HasRule)
                    errors.AppendLine($"道具 {it.Id} 是类别 3（机制拓展）却没有可解释的 rule");
            }

        // v166：新模型下 player.maxPlots = **可种植株数 = 列数**，硬上限就是列数本身。
            if (Tuning.player.maxPlots > Tuning.farm.gridSize)
                errors.AppendLine($"player.maxPlots ({Tuning.player.maxPlots}) 超过列数 ({Tuning.farm.gridSize})");
            if (Tuning.farm.initialRows > Tuning.farm.maxRows)
                errors.AppendLine($"farm.initialRows ({Tuning.farm.initialRows}) 超过 farm.maxRows ({Tuning.farm.maxRows})");
            if (Tuning.farm.maxRows > Tuning.farm.gridSize)
                errors.AppendLine($"farm.maxRows ({Tuning.farm.maxRows}) 超过列数 ({Tuning.farm.gridSize})——满格必须是正方形");

            if (errors.Length > 0)
            {
                LastError = errors.ToString();
                Log.Error("配置表校验失败：\n" + LastError);
                throw new InvalidOperationException("配置表校验失败：\n" + LastError);
            }
            LastError = "";
        }

        /// <summary>
        /// ★ v179：值是 **0..1 小数**的属性（v179 只有 `CritRate`；v191 起还有 `Luck`）★
        ///
        /// ★ 为什么它必须被整数约定排除 ★
        /// `CritRate` 既不是点数、也不是百分点：
        ///   `tuning.player.critRate = 0`、`caps.critRate = 1.0`、
        ///   消费点是 `World.Rng.Chance(st.Get(StatId.CritRate))` ⇒ **0.06 就是 6%** ✓
        /// 于是两条"合法"写法都是错的：
        ///   · `Add 1`    ⇒ +100%（一件道具直接顶满上限，档位失去意义）✗
        ///   · `Percent`  ⇒ `0 × (1 + p)` = **恒 0**（基底是 0 时乘法加不上去）⇒ 死道具 ✗
        /// ⇒ 只有小数 `Add`（0.06）能表达"暴击率 +6%" ✓
        ///
        /// ★ 为什么不干脆把整数约定废掉 ★
        /// 那条约定防的是"面板上出现 +0.4 点近战伤害"这种读不出来的数 ✗
        /// 而小数在这里**恰恰是读得出来的那个单位** ✓ ⇒ 只放开这一个属性，
        /// 其余属性继续整数硬约束 ✓
        /// ★ `CritDmg` **不在豁免里**：它基础 2.0 = 200%，用 `Percent` 乘（×1.20 = 240%），
        ///   Add 用整数也读得出来 —— 两条件都成立，所以它不需要例外 ✓
        /// ★ 公开（而不是 private）：`SelfCheck` 里两条"整数约定"断言要读**同一份**判定，
        ///   各写一份 `== StatId.CritRate` 就会出现"表校验放过、自检判红"的分歧 ✓
        /// </summary>
        /// ★ v191：**`Luck` 也进来了**（用户需求①："幸运值，百分比形式，初始值 5%"）★
        ///
        /// 为什么它必须在这一边，而不是"用 `Percent` 乘"：
        ///   `Luck` 的单位就是 **0..1 小数**（基底 0.05 = 5%、抽卡 `amount: 3` = +3 个百分点），
        ///   与 `CritRate` 逐位同形 ⇒ 两条"合法"写法里只有一种能表达"幸运值 +3%"：
        ///     · `Percent` ⇒ 走 `PickRewardCard` 的 `Mul(1 + amount/100)` ⇒ `0.05 × 1.03`，
        ///       面板按百分点差印出来是 **+0%**（0.05 → 0.0515）⇒ 玩家抽了 20 张还是 +0% ✗
        ///       （更糟的是：若基底真是 0，`0 × (1+p)` 恒为 0 ⇒ 卡彻底是死的 ✗）
        ///     · `Add 3`   ⇒ `/100` 后 `0.05 + 0.03 = 0.08` ⇒ 面板 **+3%** ✓ 这才是需求要的
        ///   ⇒ 判据必须与 `CritRate` 一致，否则表校验会**拒绝**一张本来就该这么写的卡 ✓
        ///
        /// ⚠️ 进这一边的**代价**必须认清：这两条属性从此**允许写小数** ✓
        ///   那正是它们的单位（0.06 = 6%、3 级 = 3 级），所以"面板上出现 +0.4 点幸运值"
        ///   这种事不会发生 —— 面板对它们走 `AddPoints`（×100 后取整）✓
        ///   而其余属性（含 `CritDmg` / 全部 `Percent`）的整数硬约束一个字没松 ✓
        ///
        /// ★ 仍然是**一处判定**：表校验（`Validate`）、自检（两条 `ITEM-INT`）、
        ///   抽卡（`PickRewardCard` / `IsPercentPoints`）、面板（`AddPoints` 还是 `AddRelative`）
        ///   全部读这一个函数 ⇒ 不会出现"表校验放过、自检判红"的分歧 ✓
        /// </summary>
        public static bool IsFractionStat(StatId s) => s == StatId.CritRate || s == StatId.Luck;

        /// <summary>
        /// ★★ 表里**允许写小数**的属性 —— 只管"整数约定"这一件事，**不是** `IsFractionStat` 的扩大 ★★
        ///
        /// ── 为什么必须另开一条，不能把这两个 StatId 塞进 `IsFractionStat` ──
        ///   `IsFractionStat` 除了"允许小数"，还**决定了写入与显示的单位语义**：
        ///     · `RunSession.PickRewardCard`：为真 ⇒ `Add(amount/100)`（百分点），为假 ⇒ `Mul(1+amount/100)`
        ///     · `ImGuiHud.AddPoints` / `IsPercentPoints`：为真 ⇒ 面板印"百分点差"
        ///   而 `tuning.levelReward.stats` 里**本来就有** `AtkSpeed Percent 5` 与 `MoveSpeed Percent 3`
        ///   两张卡（`Assets/Resources/Data/tuning.json`）⇒ 把这两个属性塞进 `IsFractionStat`
        ///   会让这两张卡从"×1.05 / ×1.03"静默变成"Add +0.05 / +0.03"（攻速卡直接失效）✗
        ///   ⇒ 小数的"合法性"与"单位是否 0..1"是**两件事**，必须是两条判定 ✓
        ///
        /// ── 谁在用（本轮唯一消费者）──
        ///   `gamble_glove` 的两条代价写成了 `Percent −9.9`（用户原话"移速降低 9.9%、攻速降低 9.9%"）
        ///   ⇒ 表校验（`Validate`）与自检（`ITEM-INT` 两条）若只认 `IsFractionStat`，
        ///     会把 −9.9 判成"必须为整数" ⇒ **整张表载入失败**（`Validate` 抛异常，见 `:529`）✗
        ///   所以那三处整数判定读本函数；`IsFractionStat` 一个字符没改 ✓
        ///
        /// ⚠️ 界面上不会因此出现"+0.4 点移速"这类读不出来的数：
        ///   小数只被允许出现在 **Percent 模式**（本工程只有 `gamble_glove` 这么写），
        ///   而 Percent 显示走 `AddRelative`（印 `−9.9%`），与 `Add` 那条 `AddPoints` 无关 ✓
        /// </summary>
        public static bool AllowsFraction(StatId s)
            => IsFractionStat(s) || s == StatId.MoveSpeed || s == StatId.AtkSpeed;

        /// <summary>
        /// 取种子定义。
        ///
        /// ★ v166：**空 id 返回 null，不再抛异常** ★（与下面的 <see cref="GetMutation"/> 同一约定）
        ///
        /// 为什么改：新模型下"一列 = 一株植物"，**作物身份在 `FarmPlot.CropSeedId`**，
        /// 而 `FarmPlot.SeedId` 只有**列首**才有（那是 `RunSession.SyncPlotsToSeeds`
        /// 的 "1 种子 : 1 地块" 契约要求的）✓
        ///
        /// 于是任何"读错字段"的调用方都会传进 **null**，而
        /// `Dictionary.TryGetValue(null)` 抛的是 `ArgumentNullException` ✗ ——
        /// 堆栈指向字典内部，**完全看不出是"读错了字段"**，
        /// 而且它发生在**收割路径**上 ⇒ 玩家挥一次锄头崩一次 ✓（本会话真实发生过，
        /// 见 `RunSession.HarvestPlot` 的修复注释）
        ///
        /// 所以：**空 id = "这一格没有作物身份"，是合法状态 ⇒ 返回 null** ✓
        /// 调用方一律用 `def?.` 形式（既有写法）✓
        ///
        /// ⚠️ 但**非空却查不到**仍然抛 `KeyNotFoundException` ✗
        /// 那是真·配置错误（id 写错 / 表里漏了），必须炸出来，不能被这里掩盖 ✓
        /// </summary>
        public static SeedDef GetSeed(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (SeedById.TryGetValue(id, out SeedDef s)) return s;
            Log.Error($"未找到种子定义：{id}");
            throw new KeyNotFoundException($"未找到种子定义：{id}");
        }

        /// <summary>
        /// ★ v168：**"表里有没有这个种子" —— 一个不抛的入口** ★
        ///
        /// ★ 为什么需要它，而 `GetSeed` 不能改成"找不到返回 null" ★
        /// `GetSeed` 对**非空但不存在的 id 抛异常**是**刻意的** ✓ —— 那是真·配置错误
        /// （表里漏了一行 / id 打错），必须炸出来，不能静默 ✗
        ///
        /// 但"表里有没有 X"**本身是一个正经问题** ✓ 而且有一个必须能问出来的场景：
        /// **自检要断言"测试夹具的 id 不在真表里"**（防测试数据污染真表）✗
        /// 用 `GetSeed` 去问它，问的方式就是"先抛一个异常" ⇒ 断言**永远执行不到** ✗
        /// ⇒ 于是把"一个漂亮的红"变成"整轮自检崩掉"，而崩掉时**一条判据都跑不完** ✓
        /// （这不是假设：v168 期间真的这么崩过一次 ✓）
        ///
        /// 所以两个入口各司其职：**要定义用 `GetSeed`（会炸）；问存在用 `HasSeed`（不会炸）** ✓
        /// </summary>
        public static bool HasSeed(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return SeedById.ContainsKey(id);
        }

        public static MutationDef GetMutation(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return MutationById.TryGetValue(id, out MutationDef m) ? m : null;
        }

        public static EnemyDef GetEnemy(string id)
        {
            if (EnemyById.TryGetValue(id, out EnemyDef e)) return e;
            Log.Error($"未找到敌人定义：{id}");
            throw new KeyNotFoundException($"未找到敌人定义：{id}");
        }

        /// <summary>
        /// 取第 <paramref name="index"/> 回合的波次参数（index 语义 = **第几分钟**）。
        ///
        /// ★ v165：越界时**复用最后一条**，不再返回 null ★
        ///
        /// 这是本次修掉的 P0。旧实现越界返回 null，于是：
        ///   `WaveDirector.IsComplete` 首行 `if (Wave == null) return true;` 当帧成立 ⇒ 进商店
        ///   `WaveDirector.Tick`      首行 `if (Wave == null) return;`      ⇒ 从此不再刷怪
        ///   玩家点"继续" → `StartWave(WaveIndex + 1)` → 又是 null ⇒ **Shop↔Combat 每帧乒乓**
        /// 触发点是 RunTime = 1200s（第 21 分钟），而 waves.json 只有 20 条。
        ///
        /// 为什么是"复用最后一条"而不是"补表到 200 条"：
        ///   成长曲线**已经是公式化的**（`1 + 0.12t + 0.006t²`，见 <see cref="EnemyDef.WaveGrowth"/>），
        ///   表里的 20 条只是"前 20 分钟的参数" ⇒ 越界复用最后一条在数值上是【连续的】✓
        ///   而补表到 200 条仍然是【有限】的 —— 那只是把悬崖推远，违背"无限模式" ✗
        ///
        /// 取"Index 最大的那一条"而不是 `Waves[Length - 1]`：不假设数组已按 index 排序。
        /// 表为空时仍返回 null —— 那种情况只能靠调用方保留的防御性兜底拦住。
        /// </summary>
        public static WaveDef GetWave(int index)
        {
            if (WaveByIndex.TryGetValue(index, out WaveDef w)) return w;

            WaveDef[] all = Waves;
            if (all.Length == 0) return null;

            WaveDef last = null;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && (last == null || all[i].Index > last.Index)) last = all[i];

            // 只播报一次：这条每 60 秒就会被调用一次，每帧/每分钟都打日志会把有用的信息淹掉，
            // 而"第一次越界"正是需要知道的那一刻。
            if (!_beyondTableLogged && last != null)
            {
                _beyondTableLogged = true;
                Log.Info($"第 {index} 分钟越出 waves.json（{all.Length} 条，最大 index {last.Index}）"
                         + "⇒ 复用最后一条（无限模式，数值连续）");
            }
            return last;
        }

        public static ItemDef GetItem(string id)
        {
            return ItemById.TryGetValue(id, out ItemDef it) ? it : null;
        }

        public static SeedDef[] SeedsOfGrade(SeedGrade grade) => ByGrade[(int)grade].ToArray();

        public static SeedDef RandomSeed(ref Pcg32 rng, SeedGrade grade)
        {
            List<SeedDef> pool = ByGrade[(int)grade];
            if (pool.Count == 0) pool = ByGrade[0];
            if (pool.Count == 0) throw new InvalidOperationException("没有任何种子可用于抽卡");
            return pool[rng.RangeInt(0, pool.Count)];
        }

        public static MutationDef RandomMutation(ref Pcg32 rng)
        {
            // v31：变异词条系统已彻底删除，永远返回 null
            return null;
        }

        public static ItemDef RandomItem(ref Pcg32 rng)
        {
            // ★ v314：从 `RollableItems`（不含 `noDrop`）里抽 —— 见那个属性的注释
            ItemDef[] pool = RollableItems;
            if (pool.Length == 0) throw new InvalidOperationException("没有任何道具可抽");
            return pool[rng.RangeInt(0, pool.Length)];
        }

        /// <summary>v9：指定等级随机一个道具（找不到该等级的则退回全表）。</summary>
        public static ItemDef RandomItemOfGrade(ref Pcg32 rng, SeedGrade grade)
        {
            // ★★ v314：**池子从 `Items` 换成 `RollableItems`** ★★
            //   算法一个字没动（先数、再取第 k 个），只是"哪些能抽"换成了预过滤好的那份：
            //   「陨落天使的羽毛」`noDrop = true` ⇒ 不属于任何档位的候选 ⇒ 永远抽不到 ✓
            //   ⚠️ 这也保证了**老道具的抽取序列不受影响**：那件羽毛被排除在"计数"之外，
            //      所以 D 档的 count 与序号与"它根本不存在"时**逐位相同** ✓
            ItemDef[] pool = RollableItems;
            if (pool.Length == 0) throw new InvalidOperationException("没有任何道具可抽");
            int count = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i].Grade == grade) count++;
            if (count == 0) return pool[rng.RangeInt(0, pool.Length)];
            int pick = rng.RangeInt(0, count);
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].Grade != grade) continue;
                if (pick-- == 0) return pool[i];
            }
            return pool[0];
        }

        // ════════════════════════════════════════════════════════════════════════
        // ★ v191：**道具的程序化图标（11×11 位掩码）** ★
        //
        // 用户需求："设计大概 20 种道具…制作图标"。本工程**已经有一整套**程序化图标的先例：
        // `ImGuiHud.PopupIconTex`（v188 的火焰 / 水滴 / 骷髅）—— 11×11 字符画 → `Texture2D`
        // （Point 采样）→ `GUI.DrawTexture` ✓ 这里做同一件事，只是对象从"状态弹字"换成"道具卡" ✓
        //
        // ★ 为什么道具图标不走 PNG（本轮的第一个决定）★
        // `Resources/Art/UI/icon_item_*.png` 只有 15 张，24 件老道具里 **17 件没有图**
        // ⇒ 商店货架上是那块品质色 ▭（`DrawOfferTexture` 的缺图退路）✓
        // 而本轮的道具是"表里加 22 行"就完成的东西 ⇒ 图标的生产方式必须与它同量级，
        // 否则下一个人加第 23 件时又会缺图 ✗ 所以位掩码写在 C# 里：加一件道具 = 加一段字符画，
        // 编译即生效、零资源管线、不产生任何美术依赖 ✓
        //
        // ★ 为什么不能用 emoji / 字体字形（已验过，不是猜的）★
        // HUD 字体是 `CreateDynamicFontFromOSFont("Microsoft YaHei")`；emoji 在**辅助平面**，
        // 该字体没有那些字形，而 IMGUI 的动态字形器也不认彩色字形表（COLR/CBDT）
        // ⇒ 界面上只会出现"豆腐块" □ ✗ 位掩码不依赖任何字体 ✓
        //
        // ★ 色调 = 正 / 负两类（用户要求"正负色调区分"）★
        // `category` 1（纯增益）绿、2（有代价）橙红 —— 颜色**烘进纹理**，而不是画的时候染色：
        // 商店 / 菜单道具栏 / 卡片详情三处都走 `DrawOfferTexture`，靠 `GUI.color` 就得改三处，
        // 而"这张图是什么颜色"本来就是图的属性 ⇒ 烘在图里最省事，也不可能有一处忘染 ✓
        //
        // ★ 缓存 ★ 每件一张纹理（11×11 = 121 像素），首次用到时生成、之后只读
        // ⇒ 运行期零分配，**不会每帧 `new Texture2D`**（唯一的一份字典在 `ImGuiHud.ItemMaskTex`）✓
        // ════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 掩码边长（11）。生成纹理、自检校验、工具出图**共用这一个数** ✓
        /// </summary>
        public const int ItemIconSize = 11;

        /// <summary>
        /// 道具 id → 11×11 位掩码。`#` = 主体、`+` = 高光、其余字符 = 透明。
        ///
        /// ★ 一行 = 图像**顶部**（与 `PopupIconTex` 同一约定：生成纹理时上下翻转）★
        /// ★ 每行必须等宽 `ItemIconSize` ★ 不等宽时 `ImGuiHud.ItemMaskTex` 返回 null
        /// （宁可退回品质色块，也不画一张糊图）✓
        /// </summary>
        public static readonly Dictionary<string, string[]> ItemIconMasks =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // ── ① 纯增·单属性（绿） ─────────────────────────────────────────────
            { "whet_oil", new[]          // 磨刃油：油瓶
                {
                    "...####....",
                    "....##.....",
                    "....##.....",
                    "...####....",
                    "..######...",
                    "..######...",
                    "..######...",
                    "..######...",
                    "..######...",
                    "...####....",
                    "....++.....",
                } },
            { "wind_bell", new[]         // 风铃：铃身 + 铃舌
                {
                    ".....##....",
                    "....####...",
                    "...######..",
                    "..########.",
                    ".##########",
                    "###########",
                    ".#########.",
                    "...........",
                    ".....##....",
                    "....+++....",
                    "...........",
                } },
            { "long_handle", new[]       // 长柄：柄头 + 长杆
                {
                    "....######.",
                    "...#######.",
                    "....######.",
                    ".....##....",
                    ".....##....",
                    ".....##....",
                    ".....##....",
                    ".....##....",
                    ".....##....",
                    ".....##....",
                    "...+++++...",
                } },
            { "lucky_bead", new[]        // 幸运珠：串起来的两颗珠
                {
                    ".....#.....",
                    "....#+#....",
                    "...#####...",
                    "...#####...",
                    "....#+#....",
                    ".....#.....",
                    "...#####...",
                    "..#######..",
                    "..#######..",
                    "...#####...",
                    ".....#.....",
                } },
            { "grind_stone", new[]       // 磨石：一块带亮带的石砖
                {
                    "..#######..",
                    ".#########.",
                    "###########",
                    "###########",
                    "+++++++++++",
                    "###########",
                    "###########",
                    "###########",
                    "###########",
                    ".#########.",
                    "..#######..",
                } },
            { "blast_grain", new[]       // 爆裂粒：中心炸开 + 四向碎粒
                {
                    ".....#.....",
                    "..#..#..#..",
                    "...#####...",
                    ".#.#####.#.",
                    "###########",
                    "###########",
                    ".#.#####.#.",
                    "...#####...",
                    "..#..#..#..",
                    ".....#.....",
                    "...........",
                } },
            { "wide_funnel", new[]       // 广口漏斗：上宽下窄 + 出料口
                {
                    "###########",
                    "###########",
                    ".#########.",
                    "..#######..",
                    "...#####...",
                    "....###....",
                    "....###....",
                    "....###....",
                    "....+++....",
                    "...+++++...",
                    "....+++....",
                } },
            { "healing_herb", new[]      // 疗心草：草叶 + 茎
                {
                    "......#....",
                    ".....##....",
                    "..#..##....",
                    "..###.#....",
                    "...#.###...",
                    "....##.#...",
                    "..#.##.....",
                    "..###......",
                    "...##......",
                    "....#......",
                    "....#......",
                } },
            { "lucky_stone", new[]       // 幸运石：切面宝石（v191 补卡，Luck）
                {
                    "....###....",
                    "...#####...",
                    "..##+++##..",
                    ".###+++###.",
                    ".#########.",
                    "..#######..",
                    "..#######..",
                    "...#####...",
                    "....###....",
                    ".....#.....",
                    "...........",
                } },
            { "pig_bell", new[]          // 猪铃：猪鼻（两个鼻孔，v191 补卡，PigRate）
                {
                    "...........",
                    "..#######..",
                    ".#########.",
                    "###########",
                    "##..###..##",
                    "##..###..##",
                    "###########",
                    "###########",
                    ".#########.",
                    "..#######..",
                    "...........",
                } },

            // ── ② 纯增·多属性（绿） ─────────────────────────────────────────────
            { "dual_straps", new[]       // 双臂束带：两条并列的束带
                {
                    "..###...###",
                    "..###...###",
                    "..###...###",
                    "..+++...+++",
                    "..###...###",
                    "..###...###",
                    "..###...###",
                    "..+++...+++",
                    "..###...###",
                    "..###...###",
                    "..###...###",
                } },
            { "spring_basket", new[]     // 春收篓：提手 + 编篓
                {
                    "....###....",
                    "...#...#...",
                    "..#.....#..",
                    "..#.....#..",
                    "###########",
                    "###########",
                    "##+#+#+#+##",
                    "###+#+#+###",
                    "###########",
                    "...#####...",
                    "....###....",
                } },
            { "heart_lantern", new[]     // 心灯：一颗心
                {
                    "..##...##..",
                    ".####.####.",
                    "###########",
                    "###########",
                    "###########",
                    ".#########.",
                    "..#######..",
                    "...#####...",
                    "....###....",
                    ".....#.....",
                    "...........",
                } },
            { "fertile_core", new[]      // 沃土核：土堆 + 冒出来的芽
                {
                    "...........",
                    "......#....",
                    ".....#.#...",
                    "...#..#....",
                    "..#...#....",
                    ".##...#....",
                    "###########",
                    "###########",
                    ".#########.",
                    "..#######..",
                    "...#####...",
                } },

            // ── ③ 有弊端（橙红） ───────────────────────────────────────────────
            { "berserk_mask", new[]      // 狂战面：带角的面具 + 眼缝
                {
                    "+.........+",
                    "##.......##",
                    ".#########.",
                    "###########",
                    "#.##...##.#",
                    "###########",
                    "##.#####.##",
                    "#.#######.#",
                    ".#########.",
                    "..#######..",
                    "...#####...",
                } },
            { "longbow_arm", new[]       // 长弓臂：弓身 + 弓弦
                {
                    "....#.....#",
                    "...##.....#",
                    "..#.#.....#",
                    ".#..#.....#",
                    ".#..#.....#",
                    ".#..#.....#",
                    ".#..#.....#",
                    ".#..#.....#",
                    "..#.#.....#",
                    "...##.....#",
                    "....#.....#",
                } },
            { "thorn_whip", new[]        // 荆鞭：带刺的藤条（刺 = 推得开人，见 desc）
                {
                    "...........",
                    ".....#.....",
                    "....###....",
                    "...#.#.#...",
                    "....###....",
                    ".....#.....",
                    "...###.....",
                    "..#.#.#....",
                    "...###.....",
                    "...+++.....",
                    "..+++++....",
                } },
            { "big_bore", new[]          // 大孔径：中心一个大孔
                {
                    "..#######..",
                    ".#########.",
                    "###########",
                    "####...####",
                    "###.....###",
                    "###.....###",
                    "###.....###",
                    "####...####",
                    "###########",
                    ".#########.",
                    "..#######..",
                } },
            { "stone_draught", new[]     // 石肤饮：一杯药（与「广口漏斗」的倒三角区分开）
                {
                    "...........",
                    "###########",
                    "#.........#",
                    "#.........#",
                    ".#.......#.",
                    "..#######..",
                    "...#####...",
                    "....###....",
                    "....###....",
                    "...+++++...",
                    "...........",
                } },
            { "night_lamp", new[]        // 夜读灯：灯罩 + 灯座 + 光晕
                {
                    "..+..#..+..",
                    "...+.#.+...",
                    "....###....",
                    "...#####...",
                    "..#######..",
                    "..#######..",
                    "...#####...",
                    "....###....",
                    "..#######..",
                    "..#######..",
                    "...........",
                } },

            // ── ④ 条件型（橙红：代价写在另一条属性上） ──────────────────────────
            { "frenzy_band", new[]       // 狂热带：带亮环的腕带
                {
                    "....++.....",
                    "....###....",
                    "..#######..",
                    ".#########.",
                    "###########",
                    "##+++++++##",
                    "###########",
                    ".#########.",
                    "..#######..",
                    "....###....",
                    "....++.....",
                } },
            { "tailwind_sash", new[]     // 顺风带：飘起来的绶带
                {
                    "...###.....",
                    "..#####....",
                    "...####....",
                    "....####...",
                    "....#####..",
                    "...#####...",
                    "..#####....",
                    "..####.....",
                    "...###.....",
                    "....##.....",
                    ".....#.....",
                } },

            // ════════════════════════════════════════════════════════════════════
            // ⑤ v191·fx3：**17 件老道具补图**（用户原话："部分物品图标没有制作"）
            //
            // ★ 为什么是这 17 件 ★
            // `Resources/Art/UI/icon_item_*.png` 只有 15 张，而 24 件老道具里有 17 件没有图
            // ⇒ 它们此前在商店货架上只是那块品质色 ▭（`DrawOfferTexture` 的缺图退路）。
            // 名单是**实测比对**出来的（表里 id ↔ PNG 文件名取差集），不是凭印象列的 ✓
            //
            // ★ 判据（与已有 22 张同一条）★
            //   每张 11×11、字符域只有 `#` `+` `.`、**39 张两两不同**
            //   ⇒ `SelfCheck` 的 `ITEM-ICON-MASK` / `ITEM-ICON-UNIQUE` 现在覆盖 39 件
            //     （期望值从 22 提到 39，断言一条没删）✓
            // ★ 形状要能认出来是什么 ★
            //   水囊是水滴、玻璃大炮是炮身（+ 炮口透光）、赌徒骰是有眼儿的一面骰子、
            //   血契是**中间裂开**的血滴（与实心水滴的水囊成对）、传家宝是链子 + 宝石牌 ✓
            //   配色不在这里定：`ItemIconPalette(category)` 仍是**唯一一处**定义
            //   （这 17 件里只有水囊是 category 1 绿，其余 16 件 category 2 橙红）✓
            // ════════════════════════════════════════════════════════════════════

            // ── 老道具·纯增益（绿，category 1） ────────────────────────────────
            { "water_pouch", new[]       // 水囊：一颗水滴（与「血契」的裂滴成对）
                {
                    "....##.....",
                    "....##.....",
                    "...####....",
                    "...####....",
                    "..######...",
                    "..######...",
                    ".########..",
                    ".###+++###.",
                    "###########",
                    ".#########.",
                    "..#######..",
                } },

            // ── 老道具·有代价（橙红，category 2） ──────────────────────────────
            { "glass_cannon", new[]      // 玻璃大炮：炮身（右端透光的炮口）+ 炮架与两轮
                {
                    "...........",
                    "..###......",
                    "..########+",
                    "..########+",
                    "..########+",
                    "..###......",
                    "....####...",
                    "...######..",
                    "..##....##.",
                    "...........",
                    "...........",
                } },
            { "plow_weight", new[]       // 配重犁：上面一块配重铁，下面张成 V 的两片犁刃
                {
                    "...........",
                    "..+++++++..",
                    "..#######..",
                    "..#######..",
                    ".#########.",
                    "....###....",
                    "....###....",
                    "...#####...",
                    "..##...##..",
                    ".##.....##.",
                    "##.......##",
                } },
            { "hardwood_haft", new[]     // 硬木柄：一根斜置的木杆，两道箍
                {
                    ".........##",
                    "........##.",
                    ".......##..",
                    "......##...",
                    ".....++....",
                    "....##.....",
                    "...##......",
                    "..++.......",
                    ".##........",
                    "##.........",
                    "...........",
                } },
            { "twin_blade", new[]        // 双刃护腕：两刃交叉成 X + 中间那道护腕
                {
                    "##.......##",
                    ".##.....##.",
                    "..##...##..",
                    "...##.##...",
                    "....###....",
                    "+++++++++++",
                    "....###....",
                    "...##.##...",
                    "..##...##..",
                    ".##.....##.",
                    "##.......##",
                } },
            { "ferment_jar", new[]       // 发酵罐：带盖的罐子 + 里面冒泡的液面
                {
                    ".....##....",
                    "...#####...",
                    "..#######..",
                    ".#########.",
                    ".#+++++++#.",
                    ".#########.",
                    ".#########.",
                    ".#+#+#+#+#.",
                    ".#########.",
                    "..#######..",
                    "...........",
                } },
            { "overclock_core", new[]    // 超载核心：一圈齿轮 + 烧红的芯
                {
                    "...........",
                    "..#.###.#..",
                    "..#######..",
                    ".#########.",
                    ".####+####.",
                    ".###+++###.",
                    ".####+####.",
                    ".#########.",
                    "..#######..",
                    "..#.###.#..",
                    "...........",
                } },
            { "thorn_collar", new[]      // 荆棘项圈：中空的项圈，内外都扎刺
                {
                    "..#...#...#",
                    "...#.#.#...",
                    "..#######..",
                    ".##.....##.",
                    "##.......##",
                    "##.......##",
                    "##.......##",
                    ".##.....##.",
                    "..#######..",
                    "...#.#.#...",
                    "..#...#...#",
                } },
            { "iron_bark", new[]         // 铁树皮：竖纹木皮（裂纹贯穿）+ 一道铁箍上的两颗铆钉
                {
                    "..#.####...",
                    ".####.####.",
                    "####..#####",
                    "###.####.##",
                    "##+#####+##",
                    "##.####.###",
                    "###########",
                    "###.#####.#",
                    "###########",
                    ".###.#####.",
                    "..#...###..",
                } },
            { "sniper_scope", new[]      // 狙击镜：镜筒 + 筒内十字准星 + 下方支座
                {
                    "...........",
                    ".########..",
                    "###.....###",
                    "##...++..##",
                    "###.....###",
                    ".########..",
                    "....##.....",
                    "...####....",
                    "..######...",
                    "...........",
                    "...........",
                } },
            { "barbed_tip", new[]        // 倒刺箭镞：箭头 + 两侧倒钩 + 箭杆
                {
                    ".....#.....",
                    "....+++....",
                    "....###....",
                    "...#####...",
                    "..#..#..#..",
                    ".#...#...#.",
                    "#....#....#",
                    ".....#.....",
                    ".....#.....",
                    ".....#.....",
                    ".....#.....",
                } },
            { "gambler_dice", new[]      // 赌徒骰：一面朝上的骰子，四点 + 中心一点
                {
                    "...........",
                    ".#########.",
                    ".#+#####+#.",
                    ".####.####.",
                    ".#########.",
                    ".####+####.",
                    ".#########.",
                    ".####.####.",
                    ".#+#####+#.",
                    ".#########.",
                    "...........",
                } },
            { "wax_seal", new[]          // 蜡封符：圆蜡印 + 里面方形的印记
                {
                    "...........",
                    "...#####...",
                    "..#######..",
                    ".##+++++##.",
                    "###+###+###",
                    "###+++++###",
                    "###+###+###",
                    ".##+++++##.",
                    "..#######..",
                    "...#####...",
                    "...........",
                } },
            { "blood_pact", new[]        // 血契：中间**裂开**的血滴（与实心的「水囊」成对）
                {
                    "....#.#....",
                    "....#.#....",
                    "...##.##...",
                    "...##.##...",
                    "..###.###..",
                    "..###.###..",
                    ".####.####.",
                    ".####.####.",
                    "###########",
                    ".#########.",
                    "..#######..",
                } },
            { "greedy_sickle", new[]     // 贪婪镰：弯下去的刃 + 右下角的握柄
                {
                    ".......###.",
                    ".....#####.",
                    "....###....",
                    "...###.....",
                    "..###......",
                    "..##.......",
                    ".###.......",
                    ".###....++.",
                    "..##...++##",
                    "...###++##.",
                    ".....####..",
                } },
            { "hollow_fruit", new[]      // 空心果：带叶的果子，中间是空的（只剩一枚核）
                {
                    "...........",
                    ".....##....",
                    "....####...",
                    "..+++++++..",
                    ".#########.",
                    "###.....###",
                    "##...##..##",
                    "###.....###",
                    ".#########.",
                    "..#######..",
                    "...........",
                } },
            { "heirloom", new[]          // 传家宝：挂链 + 牌位里嵌一颗亮宝石
                {
                    "....###....",
                    "...#...#...",
                    "..#.....#..",
                    ".#########.",
                    "#####+#####",
                    "####+++####",
                    "#####+#####",
                    "###########",
                    ".#########.",
                    "..#######..",
                    "...........",
                } },

            // ── ④ v196：**最后 7 件**（本轮补齐 ⇒ 表内 46 件道具 **46/46** 有图标）───────
            //  这 7 件是"24 件老道具"里上一轮**没轮到**的那批（`iron_hoe` 等），
            //  它们的 `id` 与显示名不同名（`iron_hoe` = 磨刀石、`ash_pouch` = 火药袋），
            //  而**形状按 id 画**（锄头 / 口袋 / 稻草人 / 蜗牛壳 / 靴子 / 放大镜 / 四叶草）——
            //  `id` 是这件东西的身份，名字是文案，图标画的是"它是什么东西" ✓
            { "iron_hoe", new[]          // 铁锄头：斜柄 + 左下宽平刃（刃口一行 `+`）
                {
                    ".........#.",
                    "........##.",
                    ".......##..",
                    "......##...",
                    ".....##....",
                    "....##.....",
                    "...##......",
                    "..##.......",
                    "..#####....",
                    ".#######...",
                    "..+++++....",
                } },
            { "ash_pouch", new[]         // 火药袋：袋口两根绳 + 束颈 + 鼓身
                {
                    "..+....+...",
                    "...####....",
                    "....##.....",
                    "...####....",
                    "..######...",
                    ".########..",
                    ".########..",
                    ".########..",
                    "..######...",
                    "...####....",
                    "....++.....",
                } },
            { "scarecrow", new[]         // 稻草人：草帽 + 横杆手臂 + 垂下草羽 + 插在土里的立柱
                {
                    "...+++.....",
                    "..#####....",
                    "...###.....",
                    "###########",
                    "..+.....+..",
                    "....###....",
                    "....###....",
                    "....###....",
                    "....###....",
                    "...#####...",
                    "..#######..",
                } },
            { "snail_shell", new[]       // 蜗牛壳：外壳一圈 + 里面的螺纹（右下角是螺口的缺口）
                {
                    "...####....",
                    "..##..##...",
                    ".##....##..",
                    "##......##.",
                    "#...##...#.",
                    "#..####..#.",
                    "#..####..#.",
                    "##..##..##.",
                    ".##....##+.",
                    "..##..##+..",
                    "...####+...",
                } },
            { "lens", new[]              // 放大镜：镜圈 + 玻璃上两枚反光点 + 右下斜柄
                {
                    "...........",
                    "...###.....",
                    "..#...#....",
                    ".#..++.#...",
                    ".#.+.+.#...",
                    ".#.....#...",
                    "..#...#....",
                    "...###.....",
                    "......##...",
                    ".......##..",
                    "........##.",
                } },
            { "sprinter_boots", new[]    // 疾行靴：靴筒 + 脚踝收窄 + 前伸鞋头 + 鞋底下一道速度线
                {
                    "...###.....",
                    "...###.....",
                    "...###.....",
                    "...####....",
                    "...#####...",
                    "...######..",
                    "...#####...",
                    "...######..",
                    "..########.",
                    "###########",
                    ".+..++..+..",
                } },
            { "lucky_clover", new[]      // 幸运四叶：四片叶被十字缝分开 + 一根茎
                {
                    "...#...#...",
                    "..###.###..",
                    ".#########.",
                    "####...####",
                    "###..#..###",
                    "####...####",
                    ".#########.",
                    "..###.###..",
                    "...#...#...",
                    "....##.....",
                    "....+......",
                } },

            // ══════════════════════════════════════════════════════════════════
            // ★★ v197：**16 张新掩码**（10 件机制道具 + 6 株机制植物）⇒ 46 → 62 ★★
            //
            // ★ 为什么这张表里会有**种子 id** ★
            //   用户要求"6 植物 ＋ 10 道具 ＝ 16 张新掩码，总数 46 → 62"，而 `SeedDef`
            //   **没有图标字段**（查证：`Defs.cs` 的 `SeedDef` 只有 id/name/grade/mechanic/
            //   数值那几项）⇒ 两条路：
            //     ① 植物共用某张道具掩码 —— 少 6 张，总数只有 56，与本轮要求不符 ✗
            //     ② 把这张表扩成"**11×11 位掩码表**"（道具 + 种子共用一套画法与色调）✓
            //   选 ② ⇒ 植物也有自己的图标，且**不新建第二套图** ✓
            //   随之改的判据只有一条：`ITEM-ICON-MASK` 的"键名必须是真实道具"放宽为
            //   "**真实道具或真实种子**"（见 `SelfCheck`，并新增 `IconMaskKeyValid` 作为唯一实现）✓
            //
            // ★ 色调为什么不在这里定 ★
            //   仍是 `ItemIconPalette(category)` 那一处：类别 1/其它 = 绿、2 = 橙红。
            //   种子按"它的档位"取同一套 palette 里的**正**色（绿）⇒ 不需要第三套配色 ✓
            // ══════════════════════════════════════════════════════════════════

            // ── ② 机制道具（10 件） ──
            { "carrot_ring", new[]        // 胡萝卜环：三根胡萝卜围成一圈
                {
                    "...#...#...",
                    "...#...#...",
                    "..###.###..",
                    ".#.......#.",
                    ".#.......#.",
                    "#.........#",
                    ".#.......#.",
                    ".#.......#.",
                    "..###.###..",
                    "...#...#...",
                    "...#...#...",
                } },
            { "bunny_pet", new[]          // 兔子宠物：两只长耳 + 圆脸
                {
                    "...#...#...",
                    "...#...#...",
                    "..##...##..",
                    "..#..#..#..",
                    ".#######...",
                    "#########..",
                    "#########..",
                    ".#######...",
                    "..##...##..",
                    ".###...###.",
                    "...........",
                } },
            { "evade_helm", new[]         // 闪避头盔：盔顶 + 亮边 + 面窗
                {
                    "...........",
                    "..#######..",
                    "..#######..",
                    ".+++++++++.",
                    ".#########.",
                    ".#########.",
                    "...#####...",
                    "...#####...",
                    "...#...#...",
                    "...#...#...",
                    "...........",
                } },
            { "big_can", new[]            // 丰水壶：壶身 + 提手 + 壶嘴
                {
                    ".....##....",
                    "....#..#...",
                    "..######...",
                    ".#+#+##.#..",
                    ".#.####..#.",
                    ".#.####..#.",
                    ".#.####..#.",
                    ".#.####..#.",
                    "..######...",
                    "...#..#....",
                    "...........",
                } },
            { "windup_gear", new[]        // 蓄势齿轮：八齿 + 轴心
                {
                    "...#...#...",
                    "..#.###.#..",
                    ".###...###.",
                    "##.......##",
                    "##.......##",
                    ".#..+++..#.",
                    "##.......##",
                    "##.......##",
                    ".###...###.",
                    "..#.###.#..",
                    "...#...#...",
                } },
            { "charge_coil", new[]        // 蓄力线圈：一道折下来的电
                {
                    ".....#.....",
                    "....##.....",
                    "...##......",
                    "..##.......",
                    ".#####.....",
                    "....##.....",
                    "....##.....",
                    "...##......",
                    "..##.......",
                    "...........",
                    "...........",
                } },
            { "aegis_lamp", new[]         // 护佑灯：灯罩 + 灯芯 + 底座
                {
                    "...#####...",
                    "..#.....#..",
                    ".#..+++..#.",
                    ".#..+++..#.",
                    ".#..+++..#.",
                    "..#.....#..",
                    "...#####...",
                    "....###....",
                    "....###....",
                    "..#######..",
                    "...........",
                } },
            { "gamble_glove", new[]       // 赌命手套：一枚骰子的三点面
                {
                    "..#######..",
                    "..#+#+#+#..",
                    "..#.....#..",
                    "..#+#+#+#..",
                    "..#.....#..",
                    "..#+#+#+#..",
                    "..#######..",
                    "...#####...",
                    "...........",
                    "...........",
                    "...........",
                } },
            { "sharp_spray", new[]        // 锋利水枪：一滴水被刃横切
                {
                    ".....#.....",
                    "....###....",
                    "...#####...",
                    "...#####...",
                    "..#######..",
                    "..#######..",
                    ".+++++++++.",
                    ".#########.",
                    "..#######..",
                    "...#####...",
                    "....###....",
                } },
            { "tycoon_coin", new[]        // 富豪钱袋：袋身 + 亮面 + 钱孔
                {
                    "....###....",
                    "...#####...",
                    "..#######..",
                    ".##+++++##.",
                    ".#+++++++#.",
                    ".#+++#+++#.",
                    ".#+++++++#.",
                    ".##+++++##.",
                    "..#######..",
                    "...#####...",
                    "....###....",
                } },

            // ── ③ 机制植物（6 株，键是**种子 id**） ──
            { "warblade_bloom", new[]     // 战镰花：一把弯下来的镰刀
                {
                    "......#....",
                    ".....##....",
                    "....##.....",
                    "...##......",
                    "..##.......",
                    ".##........",
                    "##########.",
                    "...........",
                    "..####.....",
                    "..####.....",
                    "...........",
                } },
            { "gale_vine", new[]          // 疾风藤：一道风卷起叶子
                {
                    "...#.......",
                    "..#.#......",
                    ".#...#.....",
                    "#..#..#....",
                    ".#...#.....",
                    "..#.#......",
                    "...#.......",
                    "....####...",
                    "...#####...",
                    "....####...",
                    "...........",
                } },
            { "sunflower_crown", new[]    // 日光葵：太阳花盘 + 八向花瓣
                {
                    "....###....",
                    ".#..#.#..#.",
                    "..#...#..#.",
                    "...#####...",
                    "##.#+++#.##",
                    "##.+++++.##",
                    "##.#+++#.##",
                    "...#####...",
                    "..#...#..#.",
                    ".#..#.#..#.",
                    "....###....",
                } },
            { "rainlily", new[]           // 雨语莲：一滴雨落进莲叶
                {
                    ".....#.....",
                    "....###....",
                    "...#####...",
                    "...#####...",
                    "..#######..",
                    ".+++++++++.",
                    ".###+++###.",
                    "###########",
                    "..#######..",
                    "...#####...",
                    "...........",
                } },
            { "shelter_ivy", new[]        // 庇护藤：藤编的盾
                {
                    ".+++++++++.",
                    ".#########.",
                    ".#########.",
                    ".##.###.##.",
                    ".##.###.##.",
                    ".#########.",
                    "..#######..",
                    "..#######..",
                    "...#####...",
                    "....###....",
                    ".....#.....",
                } },
            { "lucky_monarch", new[]      // 四叶王：四片叶 + 一根主轴
                {
                    ".....#.....",
                    "....###....",
                    "...#.#.#...",
                    "..##.#.##..",
                    ".###.#.###.",
                    ".....#.....",
                    ".###.#.###.",
                    "..##.#.##..",
                    "...#.#.#...",
                    "....###....",
                    ".....#.....",
                } },
        };

        /// <summary>
        /// 取某件道具的位掩码（没登记 ⇒ null；调用方退回品质色块，不抛异常）✓
        /// </summary>
        public static string[] ItemIconMaskOf(string itemId)
            => string.IsNullOrEmpty(itemId) ? null
             : (ItemIconMasks.TryGetValue(itemId, out string[] m) ? m : null);

        /// <summary>
        /// ★ v197：取任意 id（**道具或种子**）的位掩码。v197 起这张表也装了 6 株机制植物的图标
        /// （理由与判据改动见 `ItemIconMasks` 里那两段注释）✓
        /// </summary>
        public static string[] IconMaskOf(string id) => ItemIconMaskOf(id);

        /// <summary>v197：取某株植物的位掩码（没有 ⇒ null）。</summary>
        public static string[] SeedIconMaskOf(string seedId) => ItemIconMaskOf(seedId);

        /// <summary>
        /// ★ v197：**一个掩码键是不是合法的** —— 唯一实现。
        ///
        /// 判据：表里真有这件道具，**或**真有这个种子 ✓
        /// ★ 为什么必须是一个函数而不是两处各写一遍 ★
        ///   表校验（`Validate` 不查这条）与自检 `ITEM-ICON-MASK` 的"野键"判定读的是同一个问题；
        ///   两处各写一份就会出现"自检放过、运行时查不到"或反向的分歧 ✓
        /// ★ 为什么用 `HasSeed` 而不是 `GetSeed` ★
        ///   `GetSeed` 对**不存在的 id 抛异常**（只有空 id 返回 null）⇒ 拿它做判定会让
        ///   一个野键把校验流程炸掉，而不是报一行红字 ✓
        /// </summary>
        public static bool IconMaskKeyValid(string key)
            => !string.IsNullOrEmpty(key) && (GetItem(key) != null || HasSeed(key));

        /// <summary>
        /// ★ v198：**机制植物的"卡片短版"说明 —— 唯一实现** ★
        ///
        /// 用户原话："植物增加滑动条展示，**在底部显示拥有的特殊功能**"。
        ///
        /// ── 为什么不能直接用 `PlantMechanics.Describe` ✗（取证，不是猜）──
        ///   `Describe` 是**完整说明书**（"收割狂热：收割它 ⇒ 3 秒内攻击倍率每层 +3%（每级 +1%，
        ///   最多 12 层，叠层刷新时长）"，50 多字）。它服务的是**商店详情 / 自检报告**那种宽面板 ✓
        ///   而植物格的文字区只有 163px、正文视口 73px（4 行数值就占掉 68px）⇒
        ///   50 字折成 5 行 85px，滚到底也只能看到**半句话的中间几截**
        ///   （v198 第一版抓图 `Library/yowei-shots/v198-c-plant-card-bottom.png` 就是这样）✗
        ///   短版 20 字左右 ⇒ 2 行 34px ⇒ **滚到底时整行读得完** ✓
        ///
        /// ── 短版里为什么必须保留数字，又不许在这里另写一份 ──
        ///   六个机制的数值**随等级变**（每层百分点 / 闪避 / 天气概率 / 幸运都各有 +每级 的常量）
        ///   ⇒ 短版必须按**这张卡当前的等级**算，否则 1 级卡与 3 级卡会印出同一个数 = 撒谎 ✗
        ///   而数值全部来自 `PlantMechanics` 的常量与函数 ⇒ 这里只用它、不复制它 ✓
        ///   （本工程吃过一次"UI 自己另写一份名字表 ⇒ 传奇被画成紫色"的教训）
        ///
        /// ── 没写短版的机制怎么办 ──
        ///   退回 `Describe`（宁可长，不可空）。目前 22 株里只有那 6 条走短版；
        ///   其余机制（Haste / 丰收 / 凝露…）**没有任何种子在用** ⇒ 这条退路只为将来加种子不踩空 ✓
        ///
        /// 返回**空串**表示"这株没有机制"（调用方 `ImGuiHud.PlantSpecialFeatureLine` 会写成「无」）✓
        /// </summary>
        public static string MechShortDesc(SeedDef def, int level)
        {
            if (def == null || string.IsNullOrEmpty(def.Mechanic)) return "";
            string mech = def.Mechanic;
            if (string.Equals(mech, PlantMechanics.None, StringComparison.OrdinalIgnoreCase)) return "";
            if (string.Equals(mech, PlantMechanics.HarvestAtkMult, StringComparison.OrdinalIgnoreCase))
                return $"收割狂热：攻击倍率 +{PlantMechanics.HarvestBuffPerStack(level):0}%/层";
            if (string.Equals(mech, PlantMechanics.HarvestAtkSpeed, StringComparison.OrdinalIgnoreCase))
                return $"收割疾风：攻速 +{PlantMechanics.HarvestBuffPerStack(level):0}%/层";
            if (string.Equals(mech, PlantMechanics.StandingDodge, StringComparison.OrdinalIgnoreCase))
                return $"站田：站在它的田里闪避 +{PlantMechanics.StandingDodgeValue(level) * 100f:0}%";
            if (string.Equals(mech, PlantMechanics.SunnyBloom, StringComparison.OrdinalIgnoreCase))
                return $"日照：定时 {PlantMechanics.WeatherChance(level) * 100f:0}% 大晴天";
            if (string.Equals(mech, PlantMechanics.RainCloud, StringComparison.OrdinalIgnoreCase))
                return $"降雨：定时 {PlantMechanics.WeatherChance(level) * 100f:0}% 补满水";
            if (string.Equals(mech, PlantMechanics.FarmLuck, StringComparison.OrdinalIgnoreCase))
                return $"祥瑞：幸运 +{PlantMechanics.FarmLuckValue(level) * 100f:0}%";
            // 其余机制：短版没写 ⇒ 退回长版（宁可长，不可空）✓
            //
            // ★★ v212：v212 的 8 株新植物让这里**必须补 8 条**（否则会红）★★
            //   那 8 株用的是 `PlantMechanics` 里**原本没人用**的 8 条机制
            //   （Haste / NeighborBoost / WaterSave / ElderGrowth / ThirstTolerant /
            //     DewDrop / RichHarvest / ChainHarvest）—— 它们只有长版 `Describe`。
            //   而 `SelfCheck` 有一条判据（`v198：植物卡底部有「特殊功能」行`）要求
            //   **短版必须真的比长版短**（`want.Length < full.Length`）。
            //   退回长版 ⇒ 两者长度相等 ⇒ 那条必红（实测首例 `dusk_berry` 16 ≥ 16）✓
            //   ⇒ 这 8 条是"短版表"的补全，不是新增文案风格：
            //     每条都**保留机制里的那个数**，只是把解释性的话去掉 ✓
            if (string.Equals(mech, PlantMechanics.Haste, StringComparison.OrdinalIgnoreCase))
                return $"提速：成熟 ×{PlantMechanics.HasteMult:0.##}";
            if (string.Equals(mech, PlantMechanics.NeighborBoost, StringComparison.OrdinalIgnoreCase))
                return $"邻接：邻列成熟 ×{PlantMechanics.NeighborMult:0.##}";
            if (string.Equals(mech, PlantMechanics.WaterSave, StringComparison.OrdinalIgnoreCase))
                return $"省水：漏水 ×{PlantMechanics.WaterSaveScale:0.##}";
            if (string.Equals(mech, PlantMechanics.ElderGrowth, StringComparison.OrdinalIgnoreCase))
                return $"古木：每 {PlantMechanics.ElderStepSeconds:0}s 生长 +{PlantMechanics.ElderStepBonus * 100f:0}%";
            if (string.Equals(mech, PlantMechanics.ThirstTolerant, StringComparison.OrdinalIgnoreCase))
                return $"耐旱：缺水仍 {PlantMechanics.ThirstGrowMult * 100f:0}%";
            if (string.Equals(mech, PlantMechanics.DewDrop, StringComparison.OrdinalIgnoreCase))
                return $"凝露：收获回 {PlantMechanics.DewDropWater} 水";
            if (string.Equals(mech, PlantMechanics.RichHarvest, StringComparison.OrdinalIgnoreCase))
                return $"丰收：金币 ×{PlantMechanics.RichHarvestMult:0.##}";
            if (string.Equals(mech, PlantMechanics.ChainHarvest, StringComparison.OrdinalIgnoreCase))
                return "连锁：同列一并收";
            // ★ v253：表尾 12 株里有 6 条**新机制** ⇒ 短版必须补 6 条 ★
            //   理由与上面 v212 那批完全相同（`SelfCheck` 判"短版 < 长版"）：
            //   不补就会退回 `Describe` ⇒ 两者长度相等 ⇒ 那条判据必红 ✗
            //   写法沿用本表的习惯：**保留机制里的那个数**，只把解释性的话去掉（16 字左右）✓
            if (string.Equals(mech, PlantMechanics.FullWaterGold, StringComparison.OrdinalIgnoreCase))
                return $"满水厚赏：满水收金 ×{PlantMechanics.FullWaterGoldMult:0.##}";
            if (string.Equals(mech, PlantMechanics.StreakHarvest, StringComparison.OrdinalIgnoreCase))
                return $"连收递增：准时连收 +{PlantMechanics.StreakStepPct:0}%/层";
            if (string.Equals(mech, PlantMechanics.NightGold, StringComparison.OrdinalIgnoreCase))
                return $"夜露：夜间收金 ×{PlantMechanics.NightGoldMult:0.##}";
            if (string.Equals(mech, PlantMechanics.KillRush, StringComparison.OrdinalIgnoreCase))
                return $"猎杀亢奋：击杀后攻倍 ×{PlantMechanics.KillRushAtkMult:0.##}";
            if (string.Equals(mech, PlantMechanics.PlotCountGold, StringComparison.OrdinalIgnoreCase))
                return $"田园成片：每块地 +{PlantMechanics.PlotCountStepPct:0}% 金币";
            if (string.Equals(mech, PlantMechanics.LowHpGrow, StringComparison.OrdinalIgnoreCase))
                return $"血勇：低血生长 ×{PlantMechanics.LowHpGrowMultValue:0.##}";
            return PlantMechanics.Describe(mech);
        }

        /// <summary>
        /// ★ v197：**按下标**取道具 id（增益栏画图标用）★
        ///
        /// ── 为什么需要它 ✗ ──
        ///   `ItemIconMasks` 是 `Dictionary&lt;string, string[]&gt;`（键是**道具 id**），
        ///   而 `BuffInstance.IconMaskIdx` 是**下标** ⇒ 增益栏拿到的是一个 int，
        ///   中间必须有人把 int 翻回 "是哪一件道具的掩码" 才能交给 `ItemMaskTex` ✓
        ///
        /// ── 为什么顺序必须与 `PlayerBuffs.IconIndexOf` 完全一致 ★ ──
        ///   那一侧也是遍历本字典取第 n 个 ⇒ 两边**同源**（Dictionary 的枚举顺序 = 表里的书写顺序）。
        ///   若这里换一种顺序（比如按字母排序），增益栏画的图标就会与"设计指定的那一件"不是同一张 ——
        ///   而**这种错不会报任何异常** ✓ ⇒ 所以顺序只有这一处定义，两边都走它 ✓
        ///
        /// 越界 / 负数 ⇒ null（调用方退回色块，与 `ItemIconMaskOf` 同一条退路）✓
        /// </summary>
        public static string ItemIconMaskKeyAt(int index)
        {
            if (index < 0) return null;
            int i = 0;
            foreach (KeyValuePair<string, string[]> kv in ItemIconMasks)
            {
                if (i == index) return kv.Key;
                i++;
            }
            return null;
        }

        /// <summary>
        /// `category` → 图标配色。**正负两类的唯一一处定义**（UI 与离线出图工具读同一份）✓
        /// 1 = 纯增益（绿）／2 = 有代价（橙红）／其它 = 按纯增益处理。
        /// </summary>
        public static void ItemIconPalette(int category, out Color main, out Color accent)
        {
            if (category == 2)
            {
                main = new Color(0.96f, 0.50f, 0.26f, 1f);    // 橙红：有代价
                accent = new Color(1f, 0.86f, 0.46f, 1f);
            }
            else
            {
                main = new Color(0.55f, 0.91f, 0.57f, 1f);    // 绿：纯增益
                accent = new Color(0.90f, 1f, 0.82f, 1f);
            }
        }
    }
}
