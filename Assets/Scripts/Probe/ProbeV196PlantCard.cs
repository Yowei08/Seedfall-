using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v196 植物信息卡探针（只读 · 默认静默）★
    ///
    /// 为什么要它：本轮把植物上屏的属性收敛到 **6 项**
    /// （名称 / 品质 / 等级 / 成熟时间 / 金币提供量 / 经验值提供量）⇒ 要证明的事有两件：
    ///   ① **屏幕上的字**确实只剩这 6 项（导出 `ImGuiHud.PlantCellInfoLines` 的真字符串）
    ///   ② **画面**确实排得下、不留空洞（抓 where 图，不是靠肉眼看代码）
    ///
    /// ★ 抓图为什么必须走 `ScreenCapture` ✗ 不是 `camera.Render()` ★
    ///   整个界面是 `ImGuiHud`（`OnGUI`）⇒ **不在相机渲染里** ✗
    ///   `ScreenCapture.CaptureScreenshotAsTexture()` 抓的是后缓冲，**含 OnGUI** ✓
    ///   而 `CaptureScreenshot(路径)` 把写盘排到帧末且没有返回值 ⇒ 实测会静默丢图 ✗
    ///   ⇒ 同步取像素 + `EncodeToPNG()` + `File.WriteAllBytes` + 报字节数 + 黑图判定 ✓
    ///
    /// ★ 怎么开 ★
    ///   在 `<工程>/Library/yowei_v196_plantcard.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动跑一遍。
    ///   删掉它 ⇒ 彻底静默（不改代码、不重编译）✓
    ///
    /// ★ 它写了什么文件 ★
    ///   `Library/yowei-shots/v196-*.png`     —— 三张图（选种卡 / 菜单植物格 / 商店种子详情）
    ///   `Library/yowei-selfcheck-v196.txt`   —— 自检计数 + 16 品种 × 3 级的**逐行原文**
    ///   全部落在 `Library/`（Unity 的临时目录，不是工程数据）✓ 不碰 `Assets/**` ✓
    ///
    /// ★ 它改了什么 ★
    ///   只在**当前这一局**里切相位 / 往种子袋里塞 4 颗种 / 换掉货架 4 个货位 —— 全是局内状态，
    ///   不写任何工程文件、不碰配置表 ✓
    /// </summary>
    public sealed class ProbeV196PlantCard : MonoBehaviour
    {
        private static string Root => Path.Combine(Application.dataPath, "..");

        private static string FlagPath => Path.Combine(Root, "Library", "yowei_v196_plantcard.on");

        private static string OutDir => Path.Combine(Root, "Library", "yowei-shots");

        private static string ReportPath => Path.Combine(Root, "Library", "yowei-selfcheck-v196.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV196PlantCard");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV196PlantCard>();
            Log.Info("PROBEV196 armed（Library/yowei_v196_plantcard.on 存在）");
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

        // ─────────────────────────────────────────────────────────── ① 文字取证
        /// <summary>
        /// 把"屏幕上到底会印哪几行"原样导出。
        ///
        /// ★ 为什么读 `ImGuiHud.PlantCellInfoLines` 而不是照着它再拼一遍 ★
        ///   再拼一遍的那天，就是两份文案开始分家的那天 ✓
        ///   这个函数是本轮唯一的生产者，15 处调用（主界面 / 菜单 / 商店卡 / 商店详情 / 开局选种）
        ///   全指向它 ⇒ 读到它就是读到屏幕 ✓
        /// </summary>
        private void TextProof(StringBuilder sb)
        {
            sb.AppendLine("══ 一、植物信息卡正文（ImGuiHud.PlantCellInfoLines 的原文）══");
            sb.AppendLine("   抬头 ① 名称 / ② 品质 由各卡片自己画（DrawPlantCell / SeedCardPanel / 商店行）");
            sb.AppendLine("   正文 = ③ 等级 / ④ 成熟时间 / ⑤ 金币 / ⑥ 经验 —— 合计 6 项");
            sb.AppendLine("   窄版 4 行：植物格 / 种子卡 / 开局选种    ｜    宽版 2 行：商店商品行（视口只有 68px）");

            int rowsBad = 0, seeds = TableDatabase.Seeds.Length;
            for (int i = 0; i < seeds; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                for (int lv = SeedSlot.StartingLevel; lv <= SeedSlot.MaxLevel; lv++)
                {
                    var slot = new SeedSlot { Def = d, Quality = d.RarityTier, Level = lv };
                    string[] narrow = ImGuiHud.PlantCellInfoLines(slot, false);
                    string[] wide = ImGuiHud.PlantCellInfoLines(slot, true);
                    if (narrow.Length != 4 || wide.Length != 2) rowsBad++;
                    sb.Append("   ").Append(d.Id.PadRight(18)).Append(' ')
                      .Append(Rarity.NameOf(d.RarityTier)).Append("  Lv").Append(lv).Append("  →  ");
                    for (int r = 0; r < narrow.Length; r++)
                    {
                        if (r > 0) sb.Append(" ｜ ");
                        sb.Append(narrow[r]);
                    }
                    sb.Append("   ‖ 宽版：");
                    for (int r = 0; r < wide.Length; r++) sb.Append(r > 0 ? " ／ " : "").Append(wide[r]);
                    sb.AppendLine();
                }
            }
            sb.AppendLine("   行数不合规的卡片数 = " + rowsBad + "（窄版 4 / 宽版 2，应为 0）");
            sb.AppendLine();
        }

        /// <summary>自检计数 + 上面那份文字的原文，落到 `Library/yowei-selfcheck-v196.txt`。</summary>
        private void SelfCheckReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("★ Seedfall v196 植物信息卡 —— 只读探针报告 ★");
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
                Log.Info($"PROBEV196 自检 {results.Count - failed}/{results.Count} 通过（失败 {failed}）");
            }
            catch (Exception e)
            {
                sb.AppendLine("自检抛异常：" + e.GetType().Name + ": " + e.Message);
                Log.Error("PROBEV196 自检抛异常：" + e.GetType().Name + ": " + e.Message);
            }

            TextProof(sb);

            try
            {
                File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
                Log.Info("PROBEV196 报告 " + Path.GetFullPath(ReportPath).Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Error("PROBEV196 写报告失败：" + e.GetType().Name + ": " + e.Message);
            }
        }

        // ─────────────────────────────────────────────────────────── ② 画面取证
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
                if (tex == null) { Log.Error("PROBEV196 抓图为空：" + name); yield break; }
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
                Log.Error("PROBEV196 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
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
                Log.Info($"PROBEV196 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV196 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>往种子袋里塞 4 颗（1/2/3 级混排）—— 走 `RunSession` 的公共入口，不直接写槽位。</summary>
        private void FillBag()
        {
            string[] ids = { "radish_guard", "pea_shooter", "toxic_mushroom", "frost_ivy" };
            int[] levels = { SeedSlot.StartingLevel, 2, SeedSlot.MaxLevel, SeedSlot.StartingLevel };
            for (int i = 0; i < ids.Length; i++)
            {
                SeedDef d = TableDatabase.GetSeed(ids[i]);
                if (d == null) continue;
                if (!_s.Bag.AddSeed(d, null, d.RarityTier, out string why, levels[i]))
                    Log.Warn($"PROBEV196 入袋失败 {ids[i]} Lv{levels[i]}：{why}");
            }
            _s.SyncSeedSlots();
            var sb = new StringBuilder("PROBEV196 入袋 " + _s.Bag.OccupiedCount + " 格：");
            for (int i = 0; i < _s.Bag.SlotCount && i < 6; i++)
            {
                SeedSlot slot = _s.Bag[i];
                if (slot.IsEmpty) continue;
                string[] rows = ImGuiHud.PlantCellInfoLines(slot);
                sb.Append('「').Append(slot.Label).Append(' ').Append(Rarity.NameOf(slot.Quality)).Append(' ');
                for (int r = 0; r < rows.Length; r++)
                {
                    if (r > 0) sb.Append(" ｜ ");
                    sb.Append(rows[r]);
                }
                sb.Append('」');
            }
            Log.Info(sb.ToString());
        }

        private IEnumerator Sequence()
        {
            // ── ① 自检 + 文字取证（不需要画面，先把这份铁证落盘）──
            SelfCheckReport();
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Grab(out _s)) { Log.Warn("PROBEV196 取不到当前局会话 ⇒ 只出了文字报告"); yield break; }

            // ── ② 开局选种卡（自然状态：Prepare + 弹窗打开）──
            Log.Info($"PROBEV196 ② 相位 {_s.Phase}，初始三选一 {_s.InitialChoices.Length} 张");
            if (_s.Phase == PhaseState.Prepare && !_s.Paused)
            {
                yield return null; yield return null;
                yield return Shot("v196-a-seedchoice.png");
            }

            // ── ③ 菜单右列植物格（2×3，与主界面 `DrawPlantCell` 同一个函数）──
            FillBag();
            _s.Paused = true;
            yield return null; yield return null; yield return null;
            yield return Shot("v196-b-menu-plants.png");

            // ── ④ 商店：货架摆 1/2/3 级种子 ⇒ 右侧详情的 4 行正文 + 左下种子卡的 4 行 ──
            _s.Paused = false;
            _s.SetPhasePublic(PhaseState.Shop);
            yield return null;
            _s.Shop.Offers.Clear();
            string[] shelf = { "radish_guard", "pea_shooter", "thorn_pumpkin" };
            int[] shelfLevels = { SeedSlot.StartingLevel, 2, SeedSlot.MaxLevel };
            for (int i = 0; i < shelf.Length; i++)
            {
                SeedDef d = TableDatabase.GetSeed(shelf[i]);
                if (d == null) continue;
                var o = new ShopOffer
                {
                    Kind = OfferKind.Seed,
                    Seed = d,
                    Quality = d.RarityTier,
                    Level = shelfLevels[i],
                };
                o.Price = ShopSystem.PriceOf(o, _s.WaveIndex);
                o.Title = d.Name;
                _s.Shop.Offers.Add(o);
            }
            var shelfLog = new StringBuilder("PROBEV196 ④ 货架 = ");
            for (int i = 0; i < _s.Shop.Offers.Count; i++)
            {
                ShopOffer o = _s.Shop.Offers[i];
                shelfLog.Append('「').Append(o.Label()).Append(' ').Append(o.Kind)
                        .Append(" Lv").Append(o.Level).Append(" 价 ").Append(o.Price).Append('」');
                // 商店商品行走的是**宽版**（2 行）—— 与 `OfferDetailLines` 的调用一致 ✓
                string[] rows = ImGuiHud.PlantCellInfoLines(
                    new SeedSlot { Def = o.Seed, Quality = o.Quality, Level = o.Level }, true);
                shelfLog.Append(" 商店详情(宽版 ").Append(rows.Length).Append(" 行)：");
                for (int r = 0; r < rows.Length; r++) shelfLog.Append(r > 0 ? " ／ " : "").Append(rows[r]);
            }
            Log.Info(shelfLog.ToString());
            yield return null; yield return null; yield return null;
            yield return Shot("v196-c-shop-seed.png");

            Log.Info("PROBEV196 DONE");
        }
    }
}
