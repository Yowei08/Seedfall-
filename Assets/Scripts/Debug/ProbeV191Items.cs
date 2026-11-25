using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v191 道具 / 图标 只读探针 + 取图开关 ★
    ///
    /// 两个开关文件（都在 &lt;工程&gt;/Library/ 下），**都不在资产管理里**、删掉即静默：
    ///   · `yowei_v191_items_probe.on` → 进 Play 时跑一次**只读报告**（自检逐条 + 运行时表 + 图标生成）
    ///   · `yowei_v191_items_show.on`  → 抓到会话后**发 22 件道具 + 打开菜单 + 截图**
    ///
    /// ★ 为什么报告不能只靠 `SelfCheck.Report()` ★
    ///   那个函数**只打印失败项** ⇒ 全过时只有一句 "PASS：N/N"，
    ///   于是"我新加的那几条断言真的跑了、真的读到了表"**没有证据** ✗
    ///   所以这里把 `ITEM-*` 与 v191 那两条逐条打出来（PASS 也打）✓
    ///
    /// ★ 图标为什么要反射调 `ImGuiHud.ItemMaskTex` ★
    ///   那是**游戏真正在用的**那个函数（菜单道具格 / 商店货架 / 卡片详情都走它）✗
    ///   在探针里另写一份"同样的算法"就等于测了个假东西 ✓ 反射调它能得到三件真事：
    ///     ① 纹理真的建出来了（尺寸 / 采样方式）
    ///     ② 连续两次是**同一个对象** ⇒ 缓存生效 ⇒ 不是每帧 `new Texture2D` ✓
    ///     ③ 类别 1 / 2 的中心像素颜色不同 ⇒ 正负色调是**烘在图里**的 ✓
    ///
    /// ★ `_show` 一档会改运行期状态（发道具 + 暂停开菜单）★
    ///   它只改**内存里的这一局**，不写任何工程文件；暂停用的就是 `TogglePause`（游戏的菜单），
    ///   而且**先判状态再切**（toggle 不盲发）✓
    /// </summary>
    public sealed class ProbeV191Items : MonoBehaviour
    {
        private static string FlagReport
            => Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", "yowei_v191_items_probe.on");

        private static string FlagShow
            => Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", "yowei_v191_items_show.on");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            bool report = File.Exists(FlagReport);
            bool show = File.Exists(FlagShow);
            if (!report && !show) return;
            var go = new GameObject("ProbeV191Items");
            var c = go.AddComponent<ProbeV191Items>();
            c._report = report;
            c._show = show;
        }

        private bool _report, _show, _reported, _shown, _captured;
        private float _t;

        private void Start()
        {
            if (!_report || _reported) return;
            _reported = true;
            Log.Info("PROBEV191ITEMS BEGIN");
            try { Log.Info("\n" + Report()); }
            catch (Exception e)
            {
                // 探针自己崩了也要看得见 —— 否则"没有输出"会被读成"没问题"
                Log.Error("PROBEV191ITEMS CRASH " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
            Log.Info("PROBEV191ITEMS END");
        }

        private void Update()
        {
            if (!_show) return;
            _t += Time.unscaledDeltaTime;
            if (_t > 30f) return;                      // 30 秒还找不到会话就放弃（不无限刷日志）
            RunSession s = LiveSession();
            if (s == null) return;
            if (!_shown)
            {
                _shown = true;
                Log.Info("PROBEV191ITEMS SHOW BEGIN");
                foreach (string id in NewIds)
                {
                    ItemDef it = TableDatabase.GetItem(id);
                    if (it == null) { Log.Error("PROBEV191ITEMS 未知道具 " + id); continue; }
                    s.ApplyItem(it);
                }
                if (!s.Paused) s.TogglePause();        // 菜单 = 暂停界面（toggle：先判后切）
                Log.Info($"PROBEV191ITEMS 已发 {s.ItemStacks.Count} 种道具｜paused={s.Paused}");
                return;
            }
            // 菜单画出来之后再截（等 2 帧，避开"刚置 paused 那一帧还没重绘"）
            if (!_captured && _t > 0.5f)
            {
                _captured = true;
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "captures"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "v191-items-menu-icons.png");
                ScreenCapture.CaptureScreenshot(path);
                Log.Info("PROBEV191ITEMS CAPTURE " + path);
            }
        }

        private static RunSession LiveSession()
        {
            var live = GameBootstrap.Live;
            if (live == null) return null;
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i].Session != null) return live[i].Session;
            return null;
        }

        public static string Report()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.AppendLine("═══ v191 道具 + 图标探针（只读）═══");
            AppendSelfCheck(sb);
            AppendTable(sb);
            AppendIcons(sb);
            return sb.ToString();
        }

        // ────────────────────────────────────────────── 自检逐条
        private static void AppendSelfCheck(StringBuilder sb)
        {
            sb.AppendLine("── 自检（逐条打印道具/图标相关断言，PASS 也打）──");
            List<CheckResult> results;
            try { results = SelfCheck.RunAll(); }
            catch (Exception e)
            {
                sb.AppendLine("自检 CRASH：" + e.GetType().Name + " " + e.Message);
                return;
            }
            int pass = 0;
            foreach (CheckResult r in results) if (r.Passed) pass++;
            sb.AppendLine($"自检 {(pass == results.Count ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过");
            foreach (CheckResult r in results)
            {
                bool mine = r.Name.StartsWith("ITEM-", StringComparison.Ordinal)
                         || r.Name.StartsWith("v191：", StringComparison.Ordinal)
                         || r.Name.StartsWith("v168：类别 2", StringComparison.Ordinal)
                         || r.Name.StartsWith("v168：没有", StringComparison.Ordinal)
                         || r.Name.StartsWith("v168：类别 2 有", StringComparison.Ordinal)
                         || r.Name.StartsWith("所有道具的数值都是整数", StringComparison.Ordinal)
                         || r.Name.StartsWith("道具分为整数加法", StringComparison.Ordinal);
                if (!mine) continue;
                sb.AppendLine("  " + r);
            }
        }

        // ────────────────────────────────────────────── 22 件道具的**运行时**读数
        private static readonly string[] NewIds =
        {
            "whet_oil", "wind_bell", "long_handle", "lucky_bead", "grind_stone", "blast_grain",
            "wide_funnel", "healing_herb", "lucky_stone", "pig_bell",
            "dual_straps", "spring_basket", "heart_lantern", "fertile_core",
            "berserk_mask", "longbow_arm", "thorn_whip", "big_bore", "stone_draught",
            "night_lamp", "frenzy_band", "tailwind_sash"
        };

        private static void AppendTable(StringBuilder sb)
        {
            sb.AppendLine("── 22 件新道具（从 JSON 解析出的运行时值，不是文件里的字）──");
            sb.AppendLine($"表内道具 {TableDatabase.Items.Length} 件"
                        + $"（纯增益 {CountCat(1)}／有增有减 {CountCat(2)}／机制类 {CountCat(3)}）");
            sb.AppendLine("  id               档 类 基础价 实付@1波 叠  增益 / 代价");
            foreach (string id in NewIds)
            {
                ItemDef it = TableDatabase.GetItem(id);
                if (it == null) { sb.AppendLine("  " + id + "  ✗ 不在表里"); continue; }
                int actual = SeedSlot.ShopPriceFrom(it.Price, it.RarityTier, 1);
                sb.AppendLine("  " + it.Id.PadRight(16)
                    + " " + it.Grade
                    + "  " + it.Category
                    + " " + it.Price.ToString().PadLeft(6)
                    + " " + actual.ToString().PadLeft(8)
                    + " " + it.MaxStack.ToString().PadLeft(2)
                    + "  " + Bonuses(it.Effects) + " / " + Bonuses(it.Costs));
            }
        }

        private static int CountCat(int cat)
        {
            int n = 0;
            for (int i = 0; i < TableDatabase.Items.Length; i++)
                if (TableDatabase.Items[i].Category == cat) n++;
            return n;
        }

        private static string Bonuses(ItemBonus[] list)
        {
            if (list == null || list.Length == 0) return "—";
            var sb = new StringBuilder();
            foreach (ItemBonus b in list)
            {
                if (sb.Length > 0) sb.Append('、');
                sb.Append(b.Stat.ToString());
                sb.Append(b.IsPercent ? " +" + b.Add.ToString("0.#") + "%" : " +" + b.Add.ToString("0.###"));
            }
            return sb.ToString();
        }

        // ────────────────────────────────────────────── 图标：反射调用游戏真正的生成函数
        private static void AppendIcons(StringBuilder sb)
        {
            sb.AppendLine("── 图标（反射调用 `ImGuiHud.ItemMaskTex` —— 菜单/商店/详情走的就是它）──");
            MethodInfo mi = typeof(ImGuiHud).GetMethod("ItemMaskTex",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null)
            {
                sb.AppendLine("  ✗ 找不到 ImGuiHud.ItemMaskTex（签名变了？）");
                return;
            }
            sb.AppendLine($"  掩码登记 {TableDatabase.ItemIconMasks.Count} 张｜边长 {TableDatabase.ItemIconSize}");
            int made = 0, cachedOk = 0, nullTex = 0;
            Color pure = Color.clear, cost = Color.clear;
            bool gotPure = false, gotCost = false;
            foreach (string id in NewIds)
            {
                var t = mi.Invoke(null, new object[] { id }) as Texture2D;
                var again = mi.Invoke(null, new object[] { id }) as Texture2D;
                if (t == null) { nullTex++; sb.AppendLine("  " + id.PadRight(16) + " ✗ 没生成纹理"); continue; }
                made++;
                if (ReferenceEquals(t, again)) cachedOk++;
                ItemDef it = TableDatabase.GetItem(id);
                // ★ 采样必须落在**实体像素**上 ★
                //   直接取中心 (5,5) 会在"中心刚好是透明"的图案上读到白色
                //   （`dual_straps` 的中间就是一列空隙）⇒ 那会让人误判"色调没区别" ✗
                //   所以扫一遍找第一个不透明像素 ✓
                Color c = FirstBodyColor(t);
                if (it != null && it.Category == 1 && !gotPure) { pure = c; gotPure = true; }
                if (it != null && it.Category == 2 && !gotCost) { cost = c; gotCost = true; }
                sb.AppendLine("  " + id.PadRight(16)
                    + " " + t.width + "×" + t.height
                    + " 采样=" + t.filterMode
                    + " 类=" + (it != null ? it.Category : 0)
                    + " 实体像素=rgb(" + c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00") + ")"
                    + (ReferenceEquals(t, again) ? "" : "  ⚠ 第二次调用是新对象（缓存没生效）"));
            }
            sb.AppendLine($"  纹理生成 {made}/{NewIds.Length}｜缓存命中 {cachedOk}/{made}｜未生成 {nullTex}");
            sb.AppendLine("  正负色调：纯增益 rgb(" + pure.r.ToString("0.00") + "," + pure.g.ToString("0.00") + "," + pure.b.ToString("0.00")
                        + ") vs 有代价 rgb(" + cost.r.ToString("0.00") + "," + cost.g.ToString("0.00") + "," + cost.b.ToString("0.00") + ")");
            sb.AppendLine("  反证：ItemMaskTex(\"not_an_item\") = "
                        + (mi.Invoke(null, new object[] { "not_an_item" }) == null ? "null ✓" : "非 null ✗"));
        }

        /// <summary>第一个不透明像素的颜色（透明像素的 RGB 也是白，取它等于没读）。</summary>
        private static Color FirstBodyColor(Texture2D t)
        {
            for (int y = 0; y < t.height; y++)
                for (int x = 0; x < t.width; x++)
                {
                    Color c = t.GetPixel(x, y);
                    if (c.a > 0.5f) return c;
                }
            return Color.clear;
        }
    }
}
