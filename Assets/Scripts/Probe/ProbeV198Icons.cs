using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v198 图标 / 植物卡 / 道具详情的**取证探针**（默认静默 · 只在当前这一局里造景）★
    ///
    /// 本轮要证明三件事，每一件都是"屏幕上的字/图"，代码读得再对也不算数：
    ///   ① **62 张 64×64 PNG 真的被 Unity 导入、真的被 UI 用上**
    ///      —— 判据不只是 `Resources.Load` 非空（那是 `ITEM-ICON-PNG` 断言的事），
    ///         还要**看见**商店货架/道具格里不再是 11×11 掩码放大出来的色块 ✓
    ///   ② **植物卡底部有「特殊功能」、而且带竖向滚动条**
    ///      —— 机制植物那一行在折线以下（内容 119px &gt; 视口 73px）⇒ 必须**滚到底**才看得见，
    ///         所以这一张图分两张拍：滚到顶（证明滚动条在）/ 滚到底（证明特殊功能在）✓
    ///   ③ **道具详情的数值栏在功能栏之前**（用户第 4 张截图那句"功能：力气都用在刃上，推不开人了。"）
    ///      —— 商店商品行的详情 + 菜单道具详情两处都拍 ✓
    ///
    /// ★ 抓图为什么必须走 `ScreenCapture` ✗ 不是 `camera.Render()` ★
    ///   整个界面是 `ImGuiHud`（`OnGUI`）⇒ **不在相机渲染里** ✗
    ///   `ScreenCapture.CaptureScreenshotAsTexture()` 抓的是后缓冲，**含 OnGUI** ✓
    ///   而 `CaptureScreenshot(路径)` 把写盘排到帧末且没有返回值 ⇒ 实测会静默丢图 ✗
    ///   ⇒ 同步取像素 + `EncodeToPNG()` + `File.WriteAllBytes` + 报字节数 + 黑图判定 ✓
    ///   黑图标尺（本项目既有记录）：`254,994` / `29,690` 字节 —— 低于这个量级就是黑图 ✓
    ///
    /// ★ 怎么开 ★
    ///   在 `<工程>/Library/yowei_v198_icons.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动跑一遍。
    ///   删掉它 ⇒ 彻底静默（不改代码、不重编译）✓
    ///
    /// ★ 它写了什么 ★
    ///   `Library/yowei-shots/v198-*.png`     —— 四张图
    ///   `Library/yowei-selfcheck-v198.txt`   —— 自检计数 + 62 个 id 的贴图读数 + 22 株植物的卡片原文
    ///   全部落在 `Library/`（Unity 的临时目录，不是工程数据）✓ 不碰 `Assets/**` ✓
    ///
    /// ★ 它改了什么 ★
    ///   只在**当前这一局**里：清/填种子袋、给几件道具、换掉货架上 4 个货位、切相位、滚一下植物格、
    ///   选中一件道具看详情 —— 全是**局内 UI 状态**，不写任何工程文件、不碰配置表、不改任何数值规则 ✓
    /// </summary>
    public sealed class ProbeV198Icons : MonoBehaviour
    {
        private static string Root => Path.Combine(Application.dataPath, "..");

        private static string FlagPath => Path.Combine(Root, "Library", "yowei_v198_icons.on");

        private static string OutDir => Path.Combine(Root, "Library", "yowei-shots");

        private static string ReportPath => Path.Combine(Root, "Library", "yowei-selfcheck-v198.txt");

        /// <summary>6 株机制植物（顺序即入袋顺序 ⇒ 截图上第一格就是战镰花）。</summary>
        private static readonly string[] MechSeeds =
        {
            "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily", "shelter_ivy", "lucky_monarch",
        };

        /// <summary>菜单里要展示的道具（`hardwood_haft` 就是用户第 4 张截图那一件）。</summary>
        private static readonly string[] ShowItems =
        {
            "hardwood_haft", "grind_stone", "whet_oil", "twin_blade", "lucky_stone", "snail_shell",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV198Icons");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV198Icons>();
            Log.Info("PROBEV198ICONS armed（Library/yowei_v198_icons.on 存在）");
        }

        private RunSession _s;

        private void Start() => StartCoroutine(Sequence());

        private bool Grab(out RunSession s)
        {
            s = null;
            if (GameBootstrap.Live == null || GameBootstrap.Live.Count == 0) return false;
            s = GameBootstrap.Live[0].Session;
            return s != null && s.World != null && s.World.Player != null;
        }

        // ─────────────────────────────────────────────────────── ① 文字 / 贴图读数
        /// <summary>62 张 PNG 的**贴图读数** + 22 株植物的卡片原文，落到报告里。</summary>
        private void TextProof(StringBuilder sb)
        {
            sb.AppendLine("══ 一、62 张图标 PNG 的贴图读数（`Resources.Load` 真加载）══");
            sb.AppendLine("   判据：icon_item_&lt;id&gt; 非空且 64×64；6 株机制植物另有 icon_plant_&lt;id&gt; 一张");
            int hits = 0, miss = 0, badSize = 0, plantHits = 0;
            string[] ids = new string[62];
            int n = 0;
            for (int i = 0; i < TableDatabase.ItemIconMasks.Count && n < ids.Length; i++)
            {
                string key = TableDatabase.ItemIconMaskKeyAt(i);
                if (string.IsNullOrEmpty(key)) continue;
                ids[n++] = key;
            }
            for (int i = 0; i < n; i++)
            {
                string id = ids[i];
                Texture2D t = Resources.Load<Texture2D>("Art/UI/icon_item_" + id);
                Texture2D p = Resources.Load<Texture2D>("Art/UI/icon_plant_" + id);
                if (t == null) { miss++; sb.AppendLine($"   缺 icon_item_{id}.png"); }
                else
                {
                    hits++;
                    if (t.width != 64 || t.height != 64) { badSize++; sb.AppendLine($"   {id} 尺寸 {t.width}×{t.height}"); }
                }
                if (p != null) plantHits++;
            }
            sb.AppendLine($"   合计：有图 {hits}/{n}｜缺 {miss}｜尺寸不对 {badSize}｜其中另有 icon_plant_* 的 {plantHits} 张");
            sb.AppendLine();

            sb.AppendLine("══ 二、22 株植物的卡片正文（`ImGuiHud.PlantCardLines` 的原文 = 屏幕上那 5 行）══");
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                var slot = new SeedSlot { Def = d, Quality = d.RarityTier, Level = SeedSlot.StartingLevel };
                string[] rows = ImGuiHud.PlantCardLines(slot);
                sb.Append("   ").Append(d.Id.PadRight(20)).Append(" 共 ").Append(rows.Length).Append(" 行：");
                for (int r = 0; r < rows.Length; r++)
                {
                    if (r > 0) sb.Append(" ｜ ");
                    sb.Append(rows[r]);
                }
                sb.AppendLine();
            }
            sb.AppendLine();

            sb.AppendLine("══ 三、道具详情的四行（`ImGuiHud.ItemDetailLines` 的原文 = 屏幕上的顺序）══");
            sb.AppendLine("   改前：① 功能 ② 道具类型 ③ 效果 ④ 稀有度…持有上限");
            sb.AppendLine("   改后：① **数值**（属性 ＋ 持有上限）② 功能 ③ 道具类型 ④ 稀有度");
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                if (it == null) continue;
                var o = new ShopOffer { Kind = OfferKind.Item, Item = it, Quality = it.RarityTier, Price = it.Price };
                string[] rows = ImGuiHud.ItemDetailLines(o);
                sb.Append("   ").Append(it.Id.PadRight(18)).Append(" ①").Append(rows.Length > 0 ? rows[0] : "—");
                if (rows.Length > 1) sb.Append(" ②").Append(rows[1]);
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private void SelfCheckReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("★ Seedfall v198 图标 / 植物卡 / 道具详情 —— 只读探针报告 ★");
            sb.AppendLine("生成时刻：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            try
            {
                var results = SelfCheck.RunAll();
                int failed = 0;
                for (int i = 0; i < results.Count; i++) if (!results[i].Passed) failed++;
                sb.AppendLine($"══ 自检：{results.Count - failed}/{results.Count} 通过，{failed} 失败 ══");
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].Passed) continue;
                    sb.AppendLine("   FAIL  " + results[i].Name + "  " + results[i].Detail);
                }
                sb.AppendLine();
                Log.Info($"PROBEV198ICONS 自检 {results.Count - failed}/{results.Count} 通过（失败 {failed}）");
            }
            catch (Exception e)
            {
                sb.AppendLine("自检抛异常：" + e.GetType().Name + ": " + e.Message);
                Log.Error("PROBEV198ICONS 自检抛异常：" + e.GetType().Name + ": " + e.Message);
            }
            TextProof(sb);
            try
            {
                File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
                Log.Info("PROBEV198ICONS 报告 " + Path.GetFullPath(ReportPath).Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Error("PROBEV198ICONS 写报告失败：" + e.GetType().Name + ": " + e.Message);
            }
        }

        // ─────────────────────────────────────────────────────── ② 画面取证
        /// <summary>帧末抓后缓冲（含 OnGUI）→ 写盘 → 报字节数 + 亮度统计（黑图判定）。</summary>
        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            byte[] png = null;
            string stat = "";
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { Log.Error("PROBEV198ICONS 抓图为空：" + name); yield break; }
                Color32[] px = tex.GetPixels32();
                int lit = 0, samples = 0, lumaMin = 255, lumaMax = 0;
                long lumaSum = 0;
                for (int i = 0; i < px.Length; i += 37)
                {
                    Color32 c = px[i];
                    int luma = (c.r * 30 + c.g * 59 + c.b * 11) / 100;
                    if (luma < lumaMin) lumaMin = luma;
                    if (luma > lumaMax) lumaMax = luma;
                    if (luma > 40) lit++;
                    lumaSum += luma;
                    samples++;
                }
                png = tex.EncodeToPNG();
                stat = $"｜{tex.width}×{tex.height}｜{png.Length} 字节｜亮度 min {lumaMin} max {lumaMax}"
                       + $" 均值 {(samples > 0 ? lumaSum / samples : 0)}｜亮像素 {lit}/{samples}";
            }
            catch (Exception e)
            {
                Log.Error("PROBEV198ICONS 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
                yield break;
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
            try
            {
                if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
                string path = Path.GetFullPath(Path.Combine(OutDir, name));
                File.WriteAllBytes(path, png);
                Log.Info($"PROBEV198ICONS 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV198ICONS 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>把种子袋换成**恰好 6 株机制植物**（先清空，再走 `AddSeed` 的真规则入口）。</summary>
        private void FillBagWithMechPlants()
        {
            for (int i = 0; i < _s.Bag.SlotCount; i++)
            {
                SeedSlot slot = _s.Bag[i];
                slot.Def = null;
                slot.MutationId = null;
                slot.Level = SeedSlot.StartingLevel;
            }
            var sb = new StringBuilder("PROBEV198ICONS 入袋：");
            for (int i = 0; i < MechSeeds.Length; i++)
            {
                SeedDef d = TableDatabase.GetSeed(MechSeeds[i]);
                if (d == null) { sb.Append(" 缺表 ").Append(MechSeeds[i]); continue; }
                if (!_s.Bag.AddSeed(d, null, d.RarityTier, out string why, SeedSlot.StartingLevel))
                    sb.Append(" 失败 ").Append(MechSeeds[i]).Append('(').Append(why).Append(')');
                else sb.Append(' ').Append(d.Name);
            }
            _s.SyncSeedSlots();
            sb.Append("｜占 ").Append(_s.Bag.OccupiedCount).Append('/').Append(_s.Bag.SlotCount).Append(" 格");
            Log.Info(sb.ToString());
        }

        private IEnumerator Sequence()
        {
            // ── ① 自检 + 文字/贴图取证（不需要画面，先把这份铁证落盘）──
            SelfCheckReport();
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Grab(out _s)) { Log.Warn("PROBEV198ICONS 取不到当前局会话 ⇒ 只出了文字报告"); yield break; }

            // ── ② 造景：6 株机制植物 + 6 件道具 + 金币 ──
            FillBagWithMechPlants();
            for (int i = 0; i < ShowItems.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(ShowItems[i]);
                if (it != null) _s.ApplyItem(it);
            }
            _s.AddGoldDirect(999);
            Log.Info($"PROBEV198ICONS 道具 {_s.ItemStacks.Count} 种｜金币 {_s.Gold}");

            // ── ③ 商店：四件商品（三件道具 + 一株机制植物）⇒ 图标 + 商品详情的数值栏 ──
            _s.Paused = false;
            _s.SetPhasePublic(PhaseState.Shop);
            yield return null;
            _s.Shop.Offers.Clear();
            var shelf = new StringBuilder("PROBEV198ICONS 货架：");
            AddItemOffer(shelf, "hardwood_haft");
            AddItemOffer(shelf, "grind_stone");
            AddItemOffer(shelf, "snail_shell");
            AddSeedOffer(shelf, "warblade_bloom");
            Log.Info(shelf.ToString());
            yield return null; yield return null; yield return null;
            yield return Shot("v198-a-shop-icons.png");

            // ── ④ 菜单右列植物格：滚到顶 / 滚到底（特殊功能在折线以下）──
            _s.Paused = true;                       // Esc ⇒ DrawMenu
            yield return null; yield return null; yield return null;
            yield return Shot("v198-b-plant-card-top.png");

            // `ImGuiHud` 是**普通类**（`GameBootstrap.Awake` 里 `Hud = new ImGuiHud()`，
            // 它不挂在 GameObject 上）⇒ 只能从 `GameBootstrap.Live[0].Hud` 拿，不能 Find ✓
            ImGuiHud hud = GameBootstrap.Live != null && GameBootstrap.Live.Count > 0
                ? GameBootstrap.Live[0].Hud : null;
            if (hud == null) { Log.Warn("PROBEV198ICONS 拿不到 ImGuiHud ⇒ 滚不动植物格"); yield break; }
            for (int i = 0; i < 6; i++) hud.DebugScrollPlantCell(i, 9999f);   // 会被钳到底部
            yield return null; yield return null; yield return null;
            yield return Shot("v198-c-plant-card-bottom.png");

            // ── ⑤ 菜单道具详情：**数值栏在功能栏之前** ──
            hud.DebugSelectItem("hardwood_haft");
            yield return null; yield return null; yield return null;
            yield return Shot("v198-d-menu-item-detail.png");

            Log.Info("PROBEV198ICONS DONE");
        }

        private void AddItemOffer(StringBuilder log, string id)
        {
            ItemDef it = TableDatabase.GetItem(id);
            if (it == null) { log.Append(" 缺表 ").Append(id); return; }
            var o = new ShopOffer
            {
                Kind = OfferKind.Item, Item = it, Quality = it.RarityTier, Price = it.Price,
            };
            o.Title = it.Name;
            _s.Shop.Offers.Add(o);
            string[] rows = ImGuiHud.ItemDetailLines(o);
            log.Append('「').Append(it.Name).Append(" 价").Append(it.Price).Append(" → ")
               .Append(rows.Length > 0 ? rows[0] : "—").Append(" ／ ")
               .Append(rows.Length > 1 ? rows[1] : "—").Append('」');
        }

        private void AddSeedOffer(StringBuilder log, string id)
        {
            SeedDef d = TableDatabase.GetSeed(id);
            if (d == null) { log.Append(" 缺表 ").Append(id); return; }
            var o = new ShopOffer
            {
                Kind = OfferKind.Seed, Seed = d, Quality = d.RarityTier, Level = SeedSlot.StartingLevel,
            };
            o.Price = ShopSystem.PriceOf(o, _s.WaveIndex);
            o.Title = d.Name;
            _s.Shop.Offers.Add(o);
            log.Append('「').Append(d.Name).Append(" 价").Append(o.Price).Append('」');
        }
    }
}
