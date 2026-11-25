using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v191·fx3 取证探针（只读 · 默认静默）★
    ///
    /// 三套新表现各自"看得见 / 到点真的消失"必须留下**图 + 读数**，而不是"我看过了"：
    ///   ① 全屏回血      —— 触发帧一张 + 0.9 秒后一张（后者必须什么都没有）
    ///   ② 移动灰尘      —— 动 2 秒一张（读数 alive&gt;0）+ 停下 1 秒一张（读数 alive=0）
    ///   ③ 死亡绿色痕迹  —— 普通 / boss 各一张（比分大小）+ 3.2 秒后一张（必须没了）
    ///
    /// ★ 为什么抓图不用 `ScreenCapture.CaptureScreenshot(路径)` ✗ ★
    ///   它把写盘排到**帧末**，而"排到了没有 / 写成功没有"没有任何返回值 —— 实测连调 4 次，
    ///   日志 4 行都打出来了，磁盘上只有第 3 张。这里沿用 `ProbeV191Shots` 已经验过的做法：
    ///   `CaptureScreenshotAsTexture()` + `EncodeToPNG()` + `File.WriteAllBytes`，
    ///   同步拿像素、自己写、自己报字节数，顺手做**黑图判定**（亮度统计）✓
    ///
    /// ★ 怎么开 ★
    ///   在 `<工程>/Library/yowei_v191_fx3.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动跑一遍。
    ///   删掉它 ⇒ 彻底静默（不改代码、不重编译）✓  图写到 `<工程>/Library/yowei-fx3-shots/` ✓
    ///
    /// ★ 它改了什么 ★
    ///   全是**局内**状态：切相位、开/关机器人、推进一波、打死场上的怪、投递两个事件。
    ///   不写任何工程文件、不碰表 ✓
    ///
    /// ★ 为什么 `Boot()` 用 `AfterSceneLoad` ★
    ///   与 `ProbeV191Shots` 一致：进入 Play 时才建会话，`BeforeSceneLoad` 那一刻还没有
    ///   `GameBootstrap.Live` ⇒ 抓不到会话。开着的判据只有"那个小文件在不在" ✓
    /// </summary>
    public sealed class ProbeV191Fx3 : MonoBehaviour
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v191_fx3.on");
        private static string OutDir => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-fx3-shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV191Fx3");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV191Fx3>();
            Log.Info("PROBEV191FX3 armed（Library/yowei_v191_fx3.on 存在）");
        }

        private RunSession _s;
        private GameBootstrap _boot;
        private WorldView _view;

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
                if (tex == null) { Log.Error("PROBEV191FX3 抓图为空：" + name); yield break; }
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
                Log.Error("PROBEV191FX3 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
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
                Log.Info($"PROBEV191FX3 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV191FX3 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        private IEnumerator Sequence()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (!Grab()) { Log.Warn("PROBEV191FX3 取不到当前局会话 ⇒ 什么都不做"); yield break; }

            var sb = new StringBuilder("PROBEV191FX3 BEGIN\n");
            _s.SetPhasePublic(PhaseState.Combat);
            yield return null; yield return null;
            sb.Append("  开场读数：").Append(Report()).Append('\n');

            // ══════════════════ ② 移动轨迹：动 2 秒 / 停 1 秒 ══════════════════
            //
            // ★ 为什么这里**自己**推玩家坐标，而不是开机器人 ★
            //   实测过机器人那一版：2 秒里它大部分时间在打怪/种地，**真正在走的时间不到 0.4 秒**
            //   ⇒ 只产出 3 块灰尘，图上看不出"轨迹" ✗ 那不是特效的问题，是驱动没在走 ✓
            //   所以这里按 2.5 m/s 沿半径 3m 的圆推进 `Player.Pos` ——
            //   每帧位移约 0.04m、速度 2.5 m/s，与真人走路落在**同一条判据**上
            //   （`Moved()`：这一帧位移 >5mm 且速度 >0.05 m/s）⇒ 走的仍然是产品代码那条路 ✓
            _boot.SetBot(-1);
            yield return null;
            // ★ 先站到**空地**上，再**走直线** ★
            //   两次实测各踩过一个坑，都值得写下来：
            //   ① 第一次是在玩家原地（耕地床面）绕圈：床面抬起来，灰尘被床沿挡住 ⇒
            //      读数 `emitted=17` 而图上什么也没有 ✗ 取样点选错了 ✓
            //   ② 第二次改成绕 r=2.5m 的圆：灰尘是**沿圆周撒开**的，任意时刻只有 1~2 块
            //      在玩家附近 ⇒ 读作"几个白点"，读不出"轨迹" ✗ 路径选错了 ✓
            //   ⇒ 现在是：站到空地 (0,-10) 上**沿 +X 直线走 2 秒**（5m），
            //      于是那 17 块排成一条带子跟在他身后 ✓
            _s.World.Player.Pos = new Vector2(0f, -12f);
            yield return new WaitForSecondsRealtime(0.35f);      // 等镜头跟过去
            Vector2 p0 = _s.World.Player.Pos;
            float t = 0f;
            int peakAlive = 0, emitStart = _view.DustEmitted;
            const float speed = 2.5f;
            while (t < 2.0f)
            {
                yield return null;
                float step = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                t += step;
                // **单调**走 5m（不折返）：镜头跟着玩家 ⇒ 轨迹留在他身后、正好在画面里 ✓
                // （折返会把带子踩掉，第一版就是这么把证据弄丢的）
                _s.World.Player.Pos = new Vector2(p0.x + t * speed, p0.y);
                if (_view.DustAlive > peakAlive) peakAlive = _view.DustAlive;
            }
            int emittedMoving = _view.DustEmitted - emitStart;
            sb.Append($"  ② 移动 2.0 秒（{speed} m/s，直线来回 5m）：{Report()}\n")
              .Append($"     这一段的产出 = {emittedMoving} 块｜同屏峰值 alive = {peakAlive}\n");
            yield return Shot("fx3-2-dust-moving.png");

            // 停 1 秒（真实时间）：不再写 `Pos` ⇒ `Moved()` 判"没动" ⇒ 一块都不产，
            // 且寿命 0.55s 的旧块必须**全部**到点
            yield return new WaitForSecondsRealtime(1.0f);
            int aliveAfterStop = _view.DustAlive;
            sb.Append($"  ② 停 1.0 秒：{Report()}\n")
              .Append($"     停下后 alive = {aliveAfterStop}（判据 0）\n");
            yield return Shot("fx3-2-dust-stopped.png");

            // ══════════════════ ① 全屏回血：触发帧 / 0.9 秒后 ══════════════════
            //
            // 走**真实链路**：`StartWave` → `ResetPlayerForNewWave` → `Health.Heal` → `Bus.Publish(MinuteHealEvent)`
            // （不是直接投递事件 —— 那条链路才是"每分钟整点回血"本身）
            int waveBefore = _s.WaveIndex;
            bool healBefore = _view.HealFxPlaying;
            _s.StartWave(waveBefore + 1);
            yield return null;                       // 让 Sync 跑一帧
            sb.Append($"  ① 触发（StartWave {waveBefore}→{_s.WaveIndex}）：before playing={healBefore}\n")
              .Append("     触发后：").Append(Report()).Append('\n');
            yield return Shot("fx3-1-heal-trigger.png");
            yield return new WaitForSecondsRealtime(0.9f);
            sb.Append("  ① 0.9 秒后：").Append(Report()).Append('\n');
            yield return Shot("fx3-1-heal-after-09s.png");

            // ══════════════════ ③ 死亡痕迹：普通 / boss / 3.2 秒后 ══════════════════
            //
            // 普通怪走**真实死亡**：`KillAll()` 把它们 `Health.Kill()`，
            // 由 `Actors.cs:1975` 那条 `Bus.Publish(new EnemyKilledEvent{...})` 发出来
            int enemiesBefore = _s.World.Enemies.Count;
            int shardBefore = _view.SplatShardsEmitted;
            // ★ 先等场上有怪 ★
            //   第一次实测时场上是 0 只 ⇒ `KillAll` 什么也没杀 ⇒ "普通痕迹"那张图上什么都没有 ✗
            //   （上一版之所以有 8 只，是因为另一路探针已经跑了几分钟把波次推起来了）
            //   ⇒ 最多等 10 秒让本波把怪刷出来；仍然没有就**退回直接投递同一个事件**并如实说明 ✓
            float waited = 0f;
            while (_s.World.Enemies.Count == 0 && waited < 10f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                waited += 0.5f;
            }
            enemiesBefore = _s.World.Enemies.Count;
            bool realKill = enemiesBefore > 0;
            // 与 `DebugApi.KillAll()` 同一句：`Health.Kill()` 只写 `Hp = 0`，
            // 真正的出队与 `Bus.Publish(new EnemyKilledEvent{...})` 在 `Actors.cs:1975` 那条清理循环里 ✓
            for (int i = _s.World.Enemies.Count - 1; i >= 0; i--)
                if (_s.World.Enemies[i] != null) _s.World.Enemies[i].Health.Kill();
            yield return null; yield return null;
            if (!realKill)
            {
                // 空场兜底：投递与真实死亡完全相同的事件（同一个 handler、同一个 `Pos` 字段）
                Vector2 g = _s.World.Player.Pos + new Vector2(-2.0f, 0f);
                Bus.Publish(new EnemyKilledEvent { EnemyId = "grunt", Pos = g, Gold = 0, Exp = 0f });
                yield return null;
            }
            sb.Append($"  ③ 普通：场上 {enemiesBefore} 只（{(realKill ? "真实死亡" : "空场 ⇒ 投递同一事件")}）")
              .Append($" → KillAll 之后 {_s.World.Enemies.Count} 只")
              .Append($"｜喷出 {_view.SplatShardsEmitted - shardBefore} 片\n")
              .Append("     ").Append(Report()).Append('\n');
            yield return Shot("fx3-3-splat-normal.png");

            // boss：**直接投递与真实 boss 死亡完全相同的事件**（只差 `EnemyId`）
            // 为什么不把真 boss 拉进来：它要走入场演出 + 波次，代价不成比例；
            // 而"大小更大"的判据落在同一个 handler 的同一条分支上（`e.EnemyId == "boss"`）✓
            Vector2 at = _s.World.Player.Pos + new Vector2(2.2f, 0f);
            Bus.Publish(new EnemyKilledEvent { EnemyId = "boss", Pos = at, Gold = 0, Exp = 0f });
            yield return null;
            sb.Append("  ③ boss：").Append(Report()).Append('\n');
            // ★ 留渍"到底画没画出来"的逐项取证（`isVisible` 是引擎自己的判断）★
            sb.Append("     ").Append(_view.StainDebug()).Append('\n');
            yield return Shot("fx3-3-splat-boss.png");
            float normalR = _view.SplatLastRadius;
            // 再补一记普通痕迹，读一次普通半径（boss 那次已经把 lastR 覆盖成 boss 了）
            Bus.Publish(new EnemyKilledEvent { EnemyId = "grunt", Pos = at + new Vector2(-2.2f, 0f), Gold = 0, Exp = 0f });
            yield return null;
            float gruntR = _view.SplatLastRadius;
            sb.Append($"     半径：普通 {gruntR:0.00}m ／ boss {normalR:0.00}m ⇒ 比值 {(gruntR > 0.01f ? normalR / gruntR : 0f):0.00}×\n");

            // 3.2 秒后：留渍寿命 3.0s ⇒ 必须一片不剩
            yield return new WaitForSecondsRealtime(3.2f);
            sb.Append("  ③ 3.2 秒后：").Append(Report()).Append('\n');
            yield return Shot("fx3-3-splat-after-32s.png");

            // 上限与复用：一口气投 6 次 boss 事件 ⇒ 碎片远少于 6×16，且不超上限
            int before = _view.SplatShardsEmitted;
            for (int i = 0; i < 6; i++)
                Bus.Publish(new EnemyKilledEvent { EnemyId = "boss", Pos = at + new Vector2(i * 0.3f, 0f), Gold = 0, Exp = 0f });
            yield return null;
            sb.Append($"  ③ 上限：连发 6 次 boss（每次 {WorldView.SplatShardsBoss} 片 = {6 * WorldView.SplatShardsBoss} 片）")
              .Append($" ⇒ 实际喷 {_view.SplatShardsEmitted - before} 片，池上限 {WorldView.SplatShardMax}｜")
              .Append(Report()).Append('\n');

            sb.Append("PROBEV191FX3 DONE\n");

            // ══════════════════ ①（证据）两条图标断言的原文本 ══════════════════
            //
            // `SelfCheck.Report` 只打 FAIL 行 ⇒ 通过的断言"看不见"，
            // 而本轮必须证明 `ITEM-ICON-MASK` / `ITEM-ICON-UNIQUE` 覆盖到了 39 件。
            // 所以直接读 `RunAll()` 的结果，把这两条**原文**打出来（只读，不改任何断言）✓
            try
            {
                var results = SelfCheck.RunAll();
                int pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                sb.Append($"  自检合计 {pass}/{results.Count} 通过（本轮只负责图标那两条）\n");
                for (int i = 0; i < results.Count; i++)
                    if (results[i].Name != null && results[i].Name.StartsWith("ITEM-ICON", StringComparison.Ordinal))
                        sb.Append("    ").Append(results[i]).Append('\n');
            }
            catch (Exception e)
            {
                sb.Append("  自检读取失败：").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
            }

            Log.Info(sb.ToString());
        }
    }
}
