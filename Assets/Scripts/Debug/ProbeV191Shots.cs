using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v191 截图探针（只读 · 默认静默）★
    ///
    /// 为什么需要它：`/capture` 走的是 `camera.Render()` ⇒ **ImGui HUD 不进画面** ✗
    ///   而本工程的整个界面都是 `ImGuiHud`（OnGUI）⇒ 面板、抽卡、弹字一张都拍不到 ✓
    /// 后缓冲抓取（`ScreenCapture`）**含 OnGUI** ⇒ 能拍到 ✓ 桥没有这个入口 ⇒ 自己起一个 ✓
    ///
    /// ★ 为什么不用 `ScreenCapture.CaptureScreenshot(路径)` ✗（实测踩过）★
    ///   它把写盘排到**帧末**，而"排到了没有 / 写成功没有"没有任何返回值——
    ///   第一版连调 4 次，日志 4 行"抓图 → …"全打出来了，磁盘上只有**第 3 张** ✓
    ///   ⇒ 改成 `CaptureScreenshotAsTexture()` + `EncodeToPNG()` + `File.WriteAllBytes`：
    ///     同步拿到像素、自己写、自己报字节数，还能顺手做**黑图判定** ✓
    ///
    /// ★ 怎么开 ★
    ///   在 `<工程>/Library/yowei_v191_shots.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动跑一遍。
    ///   删掉它 ⇒ 彻底静默（不需要改代码、不需要重编译）✓
    ///   图写到 `<工程>/Library/yowei-shots/` ✓
    ///
    /// ★ 它改了什么 ★
    ///   只在**当前这一局**里切相位 / 掰玩家坐标 / 改幸运值 / 收一格地 —— 全是局内状态，
    ///   不写任何工程文件、不碰表 ✓ 用来把四件事摆到镜头前：
    ///     ① 菜单（`Paused = true` → `DrawMenu` → `DrawStatsColumns`）右栏「属性」那两行新属性
    ///     ② 商店（`PhaseState.Shop` → `DrawShop` → 同一个 `DrawStatsColumns`）右栏「角色属性」
    ///     ③ 抽卡面板（`PhaseState.LevelReward`）抽到「幸运值 +3%」那张卡（一直重抽到出现为止）
    ///     ④ 收割触发幸运时那两条带 `X2` 的弹字
    /// </summary>
    public sealed class ProbeV191Shots : MonoBehaviour
    {
        private static string FlagPath
            => Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", "yowei_v191_shots.on");

        private static string OutDir
            => Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", "yowei-shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV191Shots");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV191Shots>();
            Log.Info("PROBEV191SHOTS armed（Library/yowei_v191_shots.on 存在）");
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
                if (tex == null) { Log.Error("PROBEV191SHOTS 抓图为空：" + name); yield break; }
                // 亮度统计（抽样 1/37）：黑图判据 —— 全部接近 0 就是"什么都没画出来"
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
                stat = $"｜{tex.width}×{tex.height}｜{png.Length} 字节｜亮度 min {lumaMin} max {lumaMax} 均值 {(samples > 0 ? lumaSum / samples : 0)}"
                       + $"｜亮像素 {lit}/{samples}";
            }
            catch (Exception e)
            {
                Log.Error("PROBEV191SHOTS 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
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
                Log.Info($"PROBEV191SHOTS 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV191SHOTS 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        private IEnumerator Sequence()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Grab(out _s)) { Log.Warn("PROBEV191SHOTS 取不到当前局会话 ⇒ 什么都不做"); yield break; }

            // ── ① 菜单右栏「属性」（`Paused` 时画 DrawMenu，与相位无关）──
            _s.Paused = true;
            yield return null; yield return null; yield return null;
            Log.Info("PROBEV191SHOTS ① 菜单已打开（Paused = true）");
            yield return Shot("shot1-menu-stats.png");

            // ── ② 商店右栏「角色属性」（同一份 DrawStatsColumns 的另一个调用方）──
            _s.Paused = false;
            _s.SetPhasePublic(PhaseState.Shop);
            yield return null; yield return null; yield return null;
            Log.Info("PROBEV191SHOTS ② 进商店（Phase = " + _s.Phase + "｜货位 " + _s.Shop.Offers.Count + "）");
            yield return Shot("shot2-shop-stats.png");

            // ── ③ 抽卡：一直重抽到出现「幸运值」那张卡，再抓 ──
            _s.SetPhasePublic(PhaseState.LevelReward);
            int draws = 0;
            bool got = false;
            for (int i = 0; i < 400 && !got; i++)
            {
                _s.DrawRewardCards();
                draws++;
                for (int k = 0; k < _s.RewardCards.Count; k++)
                {
                    RunSession.LevelRewardCard cd = _s.RewardCards[k];
                    if (cd != null && cd.Def != null
                        && string.Equals(cd.Def.id, "Luck", StringComparison.OrdinalIgnoreCase)) got = true;
                }
            }
            var sb = new StringBuilder();
            sb.Append("PROBEV191SHOTS ③ 抽卡：重抽 ").Append(draws).Append(" 次后本屏 ").Append(_s.RewardCards.Count)
              .Append(" 张 ⇒ 含幸运卡=").Append(got).Append("：");
            for (int k = 0; k < _s.RewardCards.Count; k++) sb.Append('「').Append(_s.RewardCards[k].Label).Append('」');
            Log.Info(sb.ToString());
            yield return null; yield return null; yield return null;
            yield return Shot("shot3-gacha-luck.png");

            // ── ④ X2 弹字：先把玩家挪到那一格（等镜头跟上），再 100% 幸运收一格 ──
            //
            // ★ 为什么必须"先等镜头"★ 弹字是按**世界坐标投影**画的（`DrawPopups` 用 `Camera.main`）
            //   镜头是跟玩家的（`WorldView` 插值）⇒ 玩家瞬移后立刻收割，弹字可能还在视野外 ✗
            //   所以：挪人 → 等 0.9 秒让镜头追上 → 收 → 立刻抓 ✓
            _s.SetPhasePublic(PhaseState.Combat);
            TuningDef t = TableDatabase.Tuning;
            SeedDef seed = TableDatabase.GetSeed("radish_guard");
            FarmPlot plot = _s.World.Farm.Slot(0, 0);
            _s.World.Player.Pos = plot.World;
            _s.Progress.Stats.SetBase(StatId.Luck, 1f);       // 100% 必触发：截图不该赌 5%
            plot.BeginPlant(t.farm, seed, 1000, 0);
            plot.TickGrowth(999f);
            yield return new WaitForSecondsRealtime(0.9f);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(new Vector3(plot.World.x, 1.3f, plot.World.y));
                Log.Info($"PROBEV191SHOTS ④ 地块 {plot.Index} 在视口 ({vp.x:0.00},{vp.y:0.00})，z={vp.z:0.0}"
                         + (vp.z > 0f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f
                            ? " ⇒ 在画面内"
                            : " ⇒ **可能不在画面内**（弹字会看不见）"));
            }

            _s.Popups.Clear();
            _s.World.HarvestPlot(plot);
            var pop = new StringBuilder("PROBEV191SHOTS ④ 收割弹字：");
            for (int i = 0; i < _s.Popups.Count; i++)
                pop.Append('「').Append(_s.Popups[i].Text).Append('」');
            Log.Info(pop.ToString());
            yield return null;
            yield return Shot("shot4-x2-popup.png");
            Log.Info("PROBEV191SHOTS DONE");
        }
    }
}
