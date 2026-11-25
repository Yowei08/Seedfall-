using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v196·fx4 取证探针（**只读** · 默认静默）★
    ///
    /// 本轮三件事，每件都必须留下**图 + 读数**，而不是"我看过了"：
    ///   ① 血液痕迹 —— 普通 / boss 各一张（同一帧里两片都在，直接可比）+ **尺寸比**
    ///      + 贴图与顶点色的**深浅取证** + 3.2 秒后归零 + 连发 6 次 boss 不超上限
    ///   ② 回血四段 —— 十字下落 / 炸开 / 光圈扩散 / 光圈到边 / 玩家回血 各一张
    ///      + 光圈最大半径 vs 场地半对角线 + 1.8 秒后四段全灭
    ///   ③ 道具图标 46/46 —— 两条断言的**原文**（`ITEM-ICON-MASK` / `ITEM-ICON-UNIQUE`）
    ///      + 46 件"到底能不能出图"的逐件结论（PNG 优先、掩码兜底，与 HUD 同一条判据）
    ///
    /// ★ 为什么 ③ 放在**最后**（第一版放最前面，图全废了）★
    ///   `SelfCheck.RunAll()` 会把玩家升好几级 ⇒ 走完会留下一张**升级抽卡弹窗**。
    ///   第一版在抓血渍图那一刻弹窗正盖在画面中央，两张血渍图里只有卡片、没有血 ✗
    ///   ⇒ 现在先抓表现图、最后才跑自检（自检本来也不需要画面）✓
    ///
    /// ★ 为什么抓图不用 `ScreenCapture.CaptureScreenshot(路径)` ✗ ★
    ///   它把写盘排到**帧末**，而"排到了没有 / 写成功没有"没有任何返回值 —— 上一轮实测连调 4 次，
    ///   日志 4 行都打出来了，磁盘上只有第 3 张。这里沿用已经验过的做法：
    ///   `CaptureScreenshotAsTexture()` + `EncodeToPNG()` + `File.WriteAllBytes`，
    ///   同步拿像素、自己写、自己报字节数，顺手做**黑图判定**（亮度统计）✓
    ///
    /// ★ 怎么开 ★
    ///   在 `<工程>/Library/yowei_v196_fx4.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动跑一遍。
    ///   删掉它 ⇒ 彻底静默（不改代码、不重编译）✓  图写到 `<工程>/Library/yowei-fx4-shots/` ✓
    ///
    /// ★ 它改了什么 ★
    ///   全是**局内**状态：切相位、关机器人、把玩家挪到空地、投递击杀/回血事件、
    ///   把 `WorldView.HealFxSlow` 临时设成 6 再设回 1（**时长常量一个都没改**）。
    ///   不写任何工程文件、不碰表 ✓
    /// </summary>
    public sealed class ProbeV196Fx4 : MonoBehaviour
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v196_fx4.on");
        private static string OutDir => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-fx4-shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV196Fx4");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV196Fx4>();
            Log.Info("PROBEV196FX4 armed（Library/yowei_v196_fx4.on 存在）");
        }

        private RunSession _s;
        private GameBootstrap _boot;
        private WorldView _view;
        private readonly StringBuilder _sb = new StringBuilder("PROBEV196FX4 BEGIN\n");

        private void Start() => StartCoroutine(Sequence());

        private bool Grab()
        {
            _s = null; _boot = null; _view = null;
            if (GameBootstrap.Live == null || GameBootstrap.Live.Count == 0) return false;
            _boot = GameBootstrap.Live[0];
            if (_boot == null) return false;
            _s = _boot.Session;
            _view = _boot.View;
            return _s != null && _s.World != null && _s.World.Player != null && _view != null;
        }

        private string Report()
        {
            try { return _view != null ? _view.Fx3Report() : "(无 WorldView)"; }
            catch (Exception e) { return "(Fx3Report 抛异常 " + e.GetType().Name + ": " + e.Message + ")"; }
        }

        private void Line(string s) => _sb.Append(s).Append('\n');

        /// <summary>把局面摆回"能拍表现"的状态：无弹窗、机器人关、玩家在空地。</summary>
        private void ResetView(Vector2 playerAt)
        {
            if (_boot != null) _boot.SetBot(-1);
            if (_s != null)
            {
                _s.SetPhasePublic(PhaseState.Combat);      // 关掉升级抽卡之类的弹窗
                if (_s.World != null && _s.World.Player != null) _s.World.Player.Pos = playerAt;
            }
        }

        /// <summary>
        /// 清场 + 补满：让回血那几张图里没有"围上来打的敌人"与"红色 -1 伤害数字"。
        /// 只动**局内**状态（与 `DebugApi.KillAll` 同一句 `Health.Kill()`），不碰表、不写盘 ✓
        /// </summary>
        private void KillAllAndHeal()
        {
            if (_s == null || _s.World == null) return;
            for (int i = _s.World.Enemies.Count - 1; i >= 0; i--)
                if (_s.World.Enemies[i] != null) _s.World.Enemies[i].Health.Kill();
            if (_s.World.Player != null)
            {
                _s.World.Player.Health.Heal(9999f);
                _s.World.Player.Health.RefillShield();
            }
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
                if (tex == null) { Log.Error("PROBEV196FX4 抓图为空：" + name); yield break; }
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
                Log.Error("PROBEV196FX4 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
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
                Log.Info($"PROBEV196FX4 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV196FX4 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        private IEnumerator Sequence()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Grab()) { Log.Warn("PROBEV196FX4 取不到当前局会话 ⇒ 什么都不做"); yield break; }

            // 站到空地（耕地床面会啃掉血渍的一半），等镜头跟过去
            ResetView(new Vector2(0f, -10f));
            yield return new WaitForSecondsRealtime(0.8f);
            Line("  开场读数：" + Report());

            // ══════════════════ ① 血液痕迹 ══════════════════
            Line("\n══ ① 敌人死亡血液痕迹 ══");

            // ── 普通：先试**真实死亡**（`Health.Kill()` → `Actors` 那条 `Bus.Publish`）──
            float waited = 0f;
            while (_s.World.Enemies.Count == 0 && waited < 3f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                waited += 0.5f;
            }
            int enemiesBefore = _s.World.Enemies.Count;
            int shardBefore = _view.SplatShardsEmitted;
            ResetView(new Vector2(0f, -10f));
            for (int i = _s.World.Enemies.Count - 1; i >= 0; i--)
                if (_s.World.Enemies[i] != null) _s.World.Enemies[i].Health.Kill();
            yield return null; yield return null;
            bool realKill = enemiesBefore > 0;
            if (!realKill)
            {
                // 空场兜底：投递与真实死亡完全相同的事件（同一个 handler、同一个 `Pos` 字段）
                Bus.Publish(new EnemyKilledEvent
                { EnemyId = "grunt", Pos = _s.World.Player.Pos + new Vector2(2.4f, 0f), Gold = 0, Exp = 0f });
                yield return null;
            }
            float normalR = _view.SplatLastRadius;
            float normalS = _view.SplatLastShardSize;
            Line($"  普通：场上 {enemiesBefore} 只（{(realKill ? "真实死亡" : "空场 ⇒ 投递同一事件")}）"
               + $"｜喷出 {_view.SplatShardsEmitted - shardBefore} 片｜血渍半径 {normalR:0.000}m｜片边长 {normalS:0.000}m");
            Line("     " + Report());
            // 等一下让碎片落下来（0.62s 里最像"溅射"的是开头 0.2s）
            yield return new WaitForSecondsRealtime(0.18f);
            yield return Shot("fx4-2-blood-normal.png");

            // ── boss：投递与真实 boss 死亡相同的事件（只差 `EnemyId`），放在**另一侧** ──
            //   ⇒ 这一张图里左边 boss、右边普通，**同一帧直接比大小** ✓
            Vector2 bossAt = _s.World.Player.Pos + new Vector2(-2.4f, 0f);
            Bus.Publish(new EnemyKilledEvent { EnemyId = "boss", Pos = bossAt, Gold = 0, Exp = 0f });
            yield return null;
            float bossR = _view.SplatLastRadius;
            float bossS = _view.SplatLastShardSize;
            Line($"  boss：血渍半径 {bossR:0.000}m｜片边长 {bossS:0.000}m"
               + $"｜单次尺寸比 半径 {bossR / Mathf.Max(0.0001f, normalR):0.00}× 片 {bossS / Mathf.Max(0.0001f, normalS):0.00}×");
            Line("     " + Report());
            yield return new WaitForSecondsRealtime(0.10f);
            yield return Shot("fx4-2-blood-boss.png");

            // ── "有颜色纹理（深浅分明）"的机器取证 ──
            Line(_view.BloodEvidence());
            Line("     " + _view.StainDebug());

            // ── 3.2 秒后必须归零（血渍寿命 3.0s）──
            yield return new WaitForSecondsRealtime(3.2f);
            Line($"  3.2 秒后：shard={_view.SplatShardsAlive} stain={_view.SplatStainsAlive}（判据都是 0）");
            yield return Shot("fx4-2-blood-after-32s.png");

            // ── 尺寸比的**统计口径**：各打 5 次，取均值（单次含 ±15% 随机抖动）──
            float sumN = 0f, sumB = 0f;
            const int samples = 5;
            for (int i = 0; i < samples; i++)
            {
                Bus.Publish(new EnemyKilledEvent
                { EnemyId = "grunt", Pos = _s.World.Player.Pos + new Vector2(3.0f + i * 0.2f, 1.5f), Gold = 0, Exp = 0f });
                yield return null;
                sumN += _view.SplatLastRadius;
                Bus.Publish(new EnemyKilledEvent
                { EnemyId = "boss", Pos = _s.World.Player.Pos + new Vector2(-3.0f - i * 0.2f, 1.5f), Gold = 0, Exp = 0f });
                yield return null;
                sumB += _view.SplatLastRadius;
            }
            Line($"  尺寸比（各 {samples} 次取均值）：普通 R {sumN / samples:0.000}m ／ boss R {sumB / samples:0.000}m"
               + $" ⇒ **{(sumB / samples) / Mathf.Max(0.0001f, sumN / samples):0.00}×**"
               + $"（常量 `BossSplatScale` = {WorldView.BossSplatScale:0.00}，单次带 ±15% 抖动 ⇒ 预期落在 1.9~2.5）");

            // ── 上限与复用：连发 6 次 boss ⇒ 同屏恒 ≤ 40 / 8 ──
            yield return new WaitForSecondsRealtime(3.2f);       // 先清场
            int emitBefore = _view.SplatShardsEmitted;
            int peakShard = 0, peakStain = 0;
            for (int i = 0; i < 6; i++)
            {
                Bus.Publish(new EnemyKilledEvent
                { EnemyId = "boss", Pos = _s.World.Player.Pos + new Vector2(i * 0.35f, 3.0f), Gold = 0, Exp = 0f });
                yield return null;
                if (_view.SplatShardsAlive > peakShard) peakShard = _view.SplatShardsAlive;
                if (_view.SplatStainsAlive > peakStain) peakStain = _view.SplatStainsAlive;
            }
            Line($"  连发 6 次 boss（每次 {WorldView.SplatShardsBoss} 片 = {6 * WorldView.SplatShardsBoss} 片）："
               + $"实际喷 {_view.SplatShardsEmitted - emitBefore} 片 ⇒ 同屏峰值 片 {peakShard} ≤ {WorldView.SplatShardMax}、"
               + $"渍 {peakStain} ≤ {WorldView.SplatStainMax}");
            yield return new WaitForSecondsRealtime(3.2f);       // 别把血渍带进回血那几张图

            // ══════════════════ ② 回血四段（世界空间）══════════════════
            Line("\n══ ② 整点回血四段 ══");
            ResetView(new Vector2(0f, -6f));
            // ★ 先把场清干净再拍 ★
            //   第一版没清场：慢放 6 倍一共要 9 秒真实时间，这期间敌人围上来打玩家，
            //   第 ④ 张图上生命符被一排红色 "-1" 伤害数字盖住，读不出"玩家在回血" ✗
            //   现在：清场 → 补满血 → **等 3.3 秒让清场时喷出来的血渍散尽**（寿命 3.0s）→ 再开
            KillAllAndHeal();
            yield return new WaitForSecondsRealtime(3.3f);
            KillAllAndHeal();
            yield return new WaitForSecondsRealtime(0.3f);
            Line($"  触发前：playing={_view.HealFxPlaying} stage={_view.HealFxStage}"
               + $" visible={_view.HealFxVisible}（判据：四段都不播）");

            // ★ 慢放 3 倍抓图 ★
            //   最短的 ② 只有 0.30s，正常速度下一帧（33ms）就过去 1/9 ⇒ 抓不准是哪一段；
            //   慢放 3 倍后每段 0.9~1.3s（约 30 帧），抓图稳，而**总暴露时间只有 4.5 秒**
            //   （慢放 6 倍要 9 秒，敌人又围上来了）✓
            //   `HealFxSlow` 只缩放**推进速度**，四段的时长常量与比例一个字都没改 ✓
            WorldView.HealFxSlow = 3f;
            int waveBefore = _s.WaveIndex;
            // 走**真实链路**：StartWave → ResetPlayerForNewWave → Health.Heal → Bus.Publish(MinuteHealEvent)
            _s.StartWave(waveBefore + 1);
            yield return null; yield return null;
            Line($"  触发（StartWave {waveBefore}→{_s.WaveIndex}）：stage={_view.HealFxStage}｜" + Report());

            // ① 十字下落：等它落到一半
            yield return WaitCrossFrac(_view, 0.55f, 6f);
            Line($"  ① 十字下落：stage={_view.HealFxStage} 十字高度 {_view.HealCrossY:0.00}m（从 7.20 落到 1.35）｜" + Report());
            yield return Shot("fx4-3-heal-1-fall.png");

            // ② 炸开
            yield return WaitStage(_view, 2, 8f);
            Line($"  ② 炸开：stage={_view.HealFxStage}｜" + Report());
            yield return Shot("fx4-3-heal-2-burst.png");

            // ③ 光圈扩散（中段：整圈都在画面里）
            yield return WaitRing(_view, 8.0f, 10f);
            Line($"  ③ 光圈扩散：stage={_view.HealFxStage} 半径 {_view.HealRingRadius:0.00}m｜" + Report());
            yield return Shot("fx4-3-heal-3-ring.png");

            // ③ 晚段：光圈接近场地半对角线（画面上是两段大弧从左右切入）
            yield return WaitRing(_view, 21.0f, 10f);
            Line($"  ③ 光圈到边：stage={_view.HealFxStage} 半径 {_view.HealRingRadius:0.00}m"
               + $"／最大 {WorldView.HealRingRadiusMax:0.00}m"
               + $"（场地半对角线 = arenaHalf {TableDatabase.Tuning.farm.arenaHalf:0.0} × √2 = {WorldView.HealRingRadiusMax:0.00}m）");
            yield return Shot("fx4-3-heal-3-ring-edge.png");

            // ④ 玩家回血
            // ★ 等年龄到 1.30s 再抓，不能"stage 一到 4 就抓" ★
            //   第一版就是那样抓的：stage 刚变 4 时 k4≈0.02，生命符的包络还在 0.08 的起步处
            //   ⇒ 图上**什么都没有**（而读数里 stage=4、visible=True，看着像"通过了"）✗
            //   1.30s ⇒ k4≈0.41，正处在"起亮度峰值之后"的位置，图上立得住 ✓
            //   这一段把慢放降到 1.5 倍：0.34s 的窗口在 2 秒内走完，敌人来不及围上来 ✓
            WorldView.HealFxSlow = 1.5f;
            KillAllAndHeal();
            yield return WaitStage(_view, 4, 12f);
            yield return WaitHealAge(_view, 1.30f, 8f);
            Line($"  ④ 玩家回血：stage={_view.HealFxStage} age={_view.HealFxAge:0.00}s｜" + Report());
            yield return Shot("fx4-3-heal-4-pulse.png");

            float maxSeen = _view.HealRingRadius;
            Line($"  光圈实际到达的最大半径 {maxSeen:0.00}m ⇒ 覆盖全场判据："
               + $"{maxSeen:0.00} ≥ {WorldView.HealRingRadiusMax:0.00} = {maxSeen >= WorldView.HealRingRadiusMax - 0.01f}");

            // 恢复正常速度，等它自然播完 ⇒ 1.8 秒后必须一段都不剩
            WorldView.HealFxSlow = 1f;
            yield return new WaitForSecondsRealtime(1.8f);
            Line($"  1.8 秒后：playing={_view.HealFxPlaying} stage={_view.HealFxStage}"
               + $" visible={_view.HealFxVisible}（判据：全灭）");
            Line("     " + Report());
            yield return Shot("fx4-3-heal-5-after-18s.png");

            // ══════════════════ ③ 道具图标 46/46（**最后跑**，见类注释）══════════════════
            Line("\n══ ③ 道具图标 ══");
            try
            {
                var results = SelfCheck.RunAll();
                int pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                Line($"  自检合计 {pass}/{results.Count} 通过");
                for (int i = 0; i < results.Count; i++)
                    if (results[i].Name != null && results[i].Name.StartsWith("ITEM-ICON", StringComparison.Ordinal))
                        Line("    " + results[i]);
            }
            catch (Exception e)
            {
                Line("  自检读取失败：" + e.GetType().Name + ": " + e.Message);
            }
            int iconOk = 0, iconPng = 0, iconMask = 0, iconNone = 0;
            var miss = new StringBuilder();
            var all = TableDatabase.Items;
            for (int i = 0; i < all.Length; i++)
            {
                string id = all[i].Id;
                bool hasPng = Resources.Load<Texture2D>("Art/UI/icon_item_" + id) != null;
                bool hasMask = TableDatabase.ItemIconMaskOf(id) != null;
                if (hasPng) iconPng++;
                if (hasMask) iconMask++;
                if (hasPng || hasMask) iconOk++;
                else { iconNone++; miss.Append(id).Append(' '); }
            }
            Line($"  表内 {all.Length} 件逐件：能出图 {iconOk}/{all.Length}"
               + $"（其中老 PNG {iconPng} 件、有程序化掩码 {iconMask} 件、两者都缺 {iconNone} 件）");
            if (iconNone > 0) Line("    缺图：" + miss);

            Line("PROBEV196FX4 DONE");
            Log.Info(_sb.ToString());
        }

        /// <summary>等到"十字落到下落段的某个比例"（`frac` 0..1）。</summary>
        private IEnumerator WaitCrossFrac(WorldView v, float frac, float timeout)
        {
            float t = 0f;
            float target = Mathf.Lerp(7.2f, 1.35f, frac);
            while (t < timeout)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                if (!v.HealFxPlaying) continue;
                if (v.HealCrossY <= target) yield break;
                if (v.HealFxStage >= 2) yield break;      // 已经炸开了就别等
            }
        }

        private IEnumerator WaitStage(WorldView v, int stage, float timeout)
        {
            float t = 0f;
            while (t < timeout)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                if (!v.HealFxPlaying) yield break;
                if (v.HealFxStage == stage) yield break;
            }
        }

        private IEnumerator WaitRing(WorldView v, float radius, float timeout)
        {
            float t = 0f;
            while (t < timeout)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                if (!v.HealFxPlaying) yield break;
                if (v.HealRingRadius >= radius) yield break;
                if (v.HealFxStage >= 4) yield break;
            }
        }

        /// <summary>等到"回血特效播到 `age` 秒"（慢放时也只是多等几帧）。</summary>
        private IEnumerator WaitHealAge(WorldView v, float age, float timeout)
        {
            float t = 0f;
            while (t < timeout)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                if (!v.HealFxPlaying) yield break;
                if (v.HealFxAge >= age) yield break;
            }
        }
    }
}
