using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v201 只读探针：**两条伤害曲线（标准 / BOSS）的 1 / 10 / 20 / 30 分钟读数** ★★
    ///
    /// ── 它回答的三个问题 ──
    ///   ① 用户要的六个数在第 30 分钟是否**精确命中**（取整后）：普通 20 / 15 / 25、
    ///      BOSS 25 / 20 / 30 —— 逐条打 ✓ / ✗
    ///   ② 第 1 / 10 / 20 / 30 分钟各是多少（改前 → 改后），以及"几下致死"
    ///   ③ 『开局一刀死』是否仍成立（HP 侧，本轮一个字节没动 ⇒ 必须仍然成立）
    ///
    /// ── 为什么"改前"那一列是**探针自己算的**（而不是读表）──
    ///   表里只剩改后的系数（`tuning.enemies.dmgStepA/B`），改前的
    ///   `0.0242915 / 0.001214575 / 0.0347098 / 0.00173549` 已经没有第二个来源。
    ///   所以这里把四个**旧系数**写成 const `OldA / OldB / OldBossA / OldBossB`，
    ///   再用 `EnemyDef.WaveGrowth` / `EarlyNerfFactor` / `Post20Factor` 三个**唯一实现**拼出"改前"，
    ///   只用于对照 ✓（没有任何游戏代码读这四个 const ✓）
    ///
    /// ── 改后那一列**走真实代码路径** ──
    ///   `def.TouchDamageForWave / ProjectileDamageForWave / JumpDamageForWave`
    ///   = 游戏里真正打玩家用的那三个入口（同一份曲线、同一次取整）⇒ 读数不是"探针另算一遍" ✓
    ///
    /// ── 开关 ──
    ///   `<工程>/Library/yowei_v201_dmg.on` 存在才跑一次（`RuntimeInitializeOnLoadMethod`）；
    ///   常态对游戏零影响 ✓ 报告写 `Library/yowei-v201-dmg.txt` 并 `Log.Info` 一份 ✓
    ///
    /// ⚠️ 它是**只读**的：不改表、不改场景、不建物体、不碰存档 ✓
    /// </summary>
    public static class ProbeV201Dmg
    {
        /// <summary>改前基线系数（v196）—— **仅本探针对照用**，游戏代码不读它们。</summary>
        private const float OldA = 0.0242915f, OldB = 0.001214575f;
        private const float OldBossA = 0.0347098f, OldBossB = 0.00173549f;

        /// <summary>旗标文件（相对工程根）。</summary>
        public const string FlagPath = "Library/yowei_v201_dmg.on";
        /// <summary>报告路径（相对工程根）。</summary>
        public const string ReportPath = "Library/yowei-v201-dmg.txt";

        private enum Atk { Touch, Proj, Jump }

        /// <summary>六条（招式, 敌人 id, 三招里的哪一招, 改前 base, 第 30 分钟目标）。</summary>
        private struct Row
        {
            public string Label, Id; public Atk Kind; public float OldBase; public int Target;
            public Row(string label, string id, Atk kind, float oldBase, int target)
            { Label = label; Id = id; Kind = kind; OldBase = oldBase; Target = target; }
        }

        private static readonly Row[] Rows =
        {
            new Row("grunt.touch",  "grunt",   Atk.Touch, 0.7f,        20),
            new Row("shooter.proj", "shooter", Atk.Proj,  0.4666667f,  15),
            new Row("jumper.jump",  "jumper",  Atk.Jump,  0.9333333f,  25),
            new Row("boss.touch",   "boss",    Atk.Touch, 0.84f,       25),
            new Row("boss.proj",    "boss",    Atk.Proj,  0.56f,       20),
            new Row("boss.jump",    "boss",    Atk.Jump,  1.75f,       30),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                string flag = Path.Combine(root, FlagPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(flag)) return;          // ← 常态：直接返回
                File.Delete(flag);                        // ← 一次性

                string text = Run();
                string path = Path.Combine(root, ReportPath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(path, text, new UTF8Encoding(false));
                Log.Info("PROBEV201 结果已写入 " + ReportPath);
                Log.Info("\n" + text);
            }
            catch (Exception ex)
            {
                Debug.LogError("PROBEV201 异常：" + ex.GetType().Name + "：" + ex.Message + "\n" + ex.StackTrace);
            }
        }

        /// <summary>纯只读打表。</summary>
        public static string Run()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            TuningDef t = TableDatabase.Tuning;
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;

            sb.AppendLine("═══ v201 伤害曲线探针（只读）═══");
            sb.AppendLine($"  dmgStepA/B     = {g.dmgStepA:0.#######} / {g.dmgStepB:0.#######}（改前 {OldA} / {OldB}）");
            sb.AppendLine($"  bossDmgStepA/B = {g.bossDmgStepA:0.#######} / {g.bossDmgStepB:0.#######}（改前 {OldBossA} / {OldBossB}）");
            sb.AppendLine($"  earlyNerf = {g.earlyNerf:0.###} / 窗口 {g.earlyNerfEnd:0.#} 分钟｜post20Slope = {g.post20Slope:0.###}" +
                          $"｜hpUnit = {EnemyDef.PlayerHpUnit:0.##}｜玩家血 {t.player.maxHp:0.##} / 盾 {t.player.shield:0.##}");
            sb.AppendLine($"  血点 = max(1, round(曲线值 × hpUnit))；第 30 分钟 post20(29) = {EnemyDef.Post20Factor(30):0.####}" +
                          $"；曲线倍率 标准 {EnemyDef.WaveGrowth(30, g.dmgStepA, g.dmgStepB):0.#####} / BOSS {EnemyDef.WaveGrowth(30, g.bossDmgStepA, g.bossDmgStepB):0.#####}");

            AppendCurveTable(sb);
            AppendHitTable(sb, t);
            int pass, total;
            AppendTargets(sb, out pass, out total);
            AppendOneKnife(sb, t);
            sb.AppendLine();
            sb.AppendLine($"── 六目标判定：{pass}/{total} 命中 ──");
            return sb.ToString();
        }

        /// <summary>曲线值与整数血点（改前 → 改后）。</summary>
        private static void AppendCurveTable(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("────── ① 曲线值与整数血点（改前 → 改后）──────");
            int[] waves = { 1, 10, 20, 30 };
            for (int w = 0; w < waves.Length; w++)
            {
                int m = waves[w];
                sb.AppendLine($"  ── 第 {m,2} 分钟（t = {m - 1}，early = {EnemyDef.EarlyNerfFactor(m):0.####}，" +
                              $"post20 = {EnemyDef.Post20Factor(m):0.####}）");
                for (int i = 0; i < Rows.Length; i++)
                {
                    EnemyDef def = TableDatabase.GetEnemy(Rows[i].Id);
                    float after = BaseOf(def, Rows[i].Kind);
                    float c0 = OldCurved(Rows[i], m);
                    float c1 = NowCurved(def, Rows[i].Kind, m);
                    sb.AppendLine($"          {Rows[i].Label,-12} base {Rows[i].OldBase,-9:0.######}→{after,-8:0.######}" +
                                  $" 曲线 {c0,9:0.00000} → {c1,-9:0.00000}" +
                                  $" 血点 {EnemyDef.PlayerDamagePoints(c0),4} → {EnemyDef.PlayerDamagePoints(c1),4}");
                }
            }
        }

        /// <summary>几下致死（盾先吃，与 `HealthPool.ApplyDamageSplit` 同序）。</summary>
        private static void AppendHitTable(StringBuilder sb, TuningDef t)
        {
            float hp = t.player.maxHp, sh = t.player.shield;
            sb.AppendLine();
            sb.AppendLine($"────── ② 几下致死（血 {hp:0.##} + 盾 {sh:0.##}；盾先吃）──────");
            sb.AppendLine("  分钟｜招式        ｜血点 改前→改后｜含盾 改前→改后｜只算血 改前→改后");
            int[] waves = { 20, 30 };
            for (int w = 0; w < waves.Length; w++)
            {
                int m = waves[w];
                for (int i = 0; i < Rows.Length; i++)
                {
                    EnemyDef def = TableDatabase.GetEnemy(Rows[i].Id);
                    int p0 = EnemyDef.PlayerDamagePoints(OldCurved(Rows[i], m));
                    int p1 = EnemyDef.PlayerDamagePoints(NowCurved(def, Rows[i].Kind, m));
                    sb.AppendLine($"  {m,4} ｜{Rows[i].Label,-12}｜{p0,4} → {p1,-4}｜" +
                                  $"{Hits(p0, hp, sh),5} → {Hits(p1, hp, sh),-5}｜{Hits(p0, hp, 0f),5} → {Hits(p1, hp, 0f),-5}");
                }
            }
        }

        /// <summary>六个目标在第 30 分钟是否精确命中（取整后）。</summary>
        private static void AppendTargets(StringBuilder sb, out int pass, out int total)
        {
            sb.AppendLine();
            sb.AppendLine("────── ③ 第 30 分钟六目标（碰撞20 弹幕15 跳砸25 / boss 碰撞25 弹幕20 跳30）──────");
            pass = 0;
            total = Rows.Length;
            for (int i = 0; i < Rows.Length; i++)
            {
                EnemyDef def = TableDatabase.GetEnemy(Rows[i].Id);
                float after = BaseOf(def, Rows[i].Kind);
                float c = NowCurved(def, Rows[i].Kind, 30);
                int pts = EnemyDef.PlayerDamagePoints(c);
                bool ok = pts == Rows[i].Target;
                if (ok) pass++;
                sb.AppendLine($"  {Rows[i].Label,-12} 目标 {Rows[i].Target,3} ⇒ base {after:0.######} × 曲线 {c,9:0.00000}" +
                              $" ⇒ **{pts,3}** 点  {(ok ? "✓ 命中" : "✗ 未命中")}");
            }
        }

        /// <summary>『开局一刀死』复算（HP 侧，本轮未动 ⇒ 必须仍成立）。</summary>
        private static void AppendOneKnife(StringBuilder sb, TuningDef t)
        {
            var st = new StatBlock(t.caps, t.player);
            float perHit = DamageResolver.WeaponBlastDamage(t.tools.hoe.baseValue, st.Get(StatId.MeleeDamage),
                st.Get(StatId.AtkMult), false, st.Get(StatId.CritDmg), 0f);
            sb.AppendLine();
            sb.AppendLine("────── ④ 『开局一刀死』（HP 侧，本轮一个字节没动）──────");
            sb.AppendLine($"  玩家开局一击（锄头）= ({t.tools.hoe.baseValue:0.##} + {st.Get(StatId.MeleeDamage):0.##})" +
                          $" × 倍率 {st.Get(StatId.AtkMult):0.##} = **{perHit:0.##}**");
            string[] ids = { "pig", "grunt", "shooter", "jumper" };
            for (int i = 0; i < ids.Length; i++)
            {
                EnemyDef d = TableDatabase.GetEnemy(ids[i]);
                if (d == null) continue;
                float h1 = d.HpForWave(1, 1);
                int knives = Mathf.CeilToInt(h1 / perHit);
                sb.AppendLine($"  {ids[i],-8} 第 1 分钟血 {h1,8:0.####} ⇒ 开局 {knives} 刀 {(knives == 1 ? "✓" : "✗")}");
            }
        }

        // ── 小工具 ──

        /// <summary>表里的 base（真的从 def 读 —— 表是唯一真相）。</summary>
        private static float BaseOf(EnemyDef def, Atk kind)
        {
            switch (kind)
            {
                case Atk.Proj: return def.ProjectileDamage;
                case Atk.Jump: return def.JumpDamage;
                default: return def.TouchDamage;
            }
        }

        /// <summary>改后读数：**走游戏真正的入口**（同一份曲线、同一次取整）。</summary>
        private static float NowCurved(EnemyDef def, Atk kind, int wave)
        {
            switch (kind)
            {
                case Atk.Proj: return def.ProjectileDamageForWave(wave);
                case Atk.Jump: return def.JumpDamageForWave(wave);
                default: return def.TouchDamageForWave(wave);
            }
        }

        /// <summary>改前读数：旧系数 × 三个唯一因子（仅对照）。</summary>
        private static float OldCurved(Row row, int wave)
        {
            bool boss = row.Id == "boss";
            float a = boss ? OldBossA : OldA, b = boss ? OldBossB : OldB;
            return row.OldBase * EnemyDef.WaveGrowth(wave, a, b)
                 * (boss ? 1f : EnemyDef.EarlyNerfFactor(wave))
                 * EnemyDef.Post20Factor(wave);
        }

        /// <summary>几下打死：盾先吃满，余量进血（`HealthPool.ApplyDamageSplit` 的顺序）。</summary>
        private static int Hits(int per, float hp, float shield)
        {
            if (per <= 0) return 999;
            int n = 0;
            float sh = shield, h = hp;
            while (h > 0f && n < 999)
            {
                n++;
                float d = per;
                if (sh > 0f) { float a = Mathf.Min(sh, d); sh -= a; d -= a; }
                h -= d;
            }
            return n;
        }
    }
}
