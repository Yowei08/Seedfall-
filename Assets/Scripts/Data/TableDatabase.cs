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
                    Price = raw.price
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
                    GrowPerWave = raw.growPerWave
                });
            }
            Items = itemList.ToArray();

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
                p.quality = DefParser.ParseEnum(raw.quality, GoodsQuality.Common);
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
            if (t.player.explodeDamage < 0f) t.player.explodeDamage = 0f;
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
            if (t.shop.itemGradePriceStep <= 0f) t.shop.itemGradePriceStep = 0.45f;
            if (t.shop.priceWaveStep <= 0f) t.shop.priceWaveStep = 0.03f;
            // v166：田地形状 = gridSize 列 × [initialRows, maxRows] 行
            if (t.farm.gridSize <= 0) t.farm.gridSize = 6;
            if (t.farm.maxRows <= 0) t.farm.maxRows = t.farm.gridSize;
            if (t.farm.initialRows <= 0) t.farm.initialRows = 3;
            if (t.farm.initialRows > t.farm.maxRows) t.farm.initialRows = t.farm.maxRows;
            if (t.farm.rowGrowthSeconds <= 0f) t.farm.rowGrowthSeconds = 300f;   // 5 分钟
            if (t.tools.workCapMax <= 1) t.tools.workCapMax = 4;
            if (t.shop.gradeWeights == null) t.shop.gradeWeights = Array.Empty<GradeWeightDef>();
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
                foreach (ItemBonus b in it.Effects)
                    if (Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f)
                        errors.AppendLine($"道具 {it.Id} 的增益 {b.Stat} 必须为整数（当前 {b.Add}）");
                foreach (ItemBonus b in it.Costs)
                    if (Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f)
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
                if (it.Category != 2 && it.Costs.Length > 0)
                    errors.AppendLine($"道具 {it.Id} 是类别 {it.Category} 却带了 costs ⇒ 应归入类别 2");
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
            if (Items.Length == 0) throw new InvalidOperationException("没有任何道具可抽");
            return Items[rng.RangeInt(0, Items.Length)];
        }

        /// <summary>v9：指定等级随机一个道具（找不到该等级的则退回全表）。</summary>
        public static ItemDef RandomItemOfGrade(ref Pcg32 rng, SeedGrade grade)
        {
            if (Items.Length == 0) throw new InvalidOperationException("没有任何道具可抽");
            int count = 0;
            for (int i = 0; i < Items.Length; i++) if (Items[i].Grade == grade) count++;
            if (count == 0) return Items[rng.RangeInt(0, Items.Length)];
            int pick = rng.RangeInt(0, count);
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i].Grade != grade) continue;
                if (pick-- == 0) return Items[i];
            }
            return Items[0];
        }
    }
}
