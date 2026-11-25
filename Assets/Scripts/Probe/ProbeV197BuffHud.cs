using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v197 增益栏 + 闪避虚化/灰字 —— 只读探针（默认静默）★
    ///
    /// 要证明的事：
    ///   ① 增益区**画得出来**：1 格 / 多格（含 `×12` 角标与时长条）/ 悬停浮框 三张图
    ///   ② 增益区**坐标对不对**：导出 `ImGuiHud.BuffChipRects` 的真实矩形（y 必须落在血量下方）
    ///   ③ 悬停浮框**到底写了哪三行**：导出 `ImGuiHud.LastBuffTip` 的原文
    ///   ④ 闪避：`caps.dodge` 现值 + 虚化时长 + 灰色「闪避」弹字的**原文与色值**
    ///
    /// ★ 抓图为什么必须走 `ScreenCapture` ✗ 不是 `camera.Render()` ★
    ///   整个界面是 `ImGuiHud`（`OnGUI`）⇒ **不在相机渲染里** ✗
    ///   `CaptureScreenshotAsTexture()` 抓的是后缓冲、**含 OnGUI** ✓
    ///   （与 `ProbeV196PlantCard` 同一条路，同样报字节数 + 黑图判定）
    ///
    /// ★ 怎么开 ★
    ///   在 `<工程>/Library/yowei_v197_buffhud.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动跑一遍；
    ///   删掉它 ⇒ 彻底静默（不改代码、不重编译）✓
    ///
    /// ★ 它写了什么 ★
    ///   `Library/yowei-shots/v197-*.png`     —— 四张图
    ///   `Library/yowei-selfcheck-v197.txt`   —— 全部读数（自检计数 / 增益表 / 坐标 / 浮框三行 / 闪避）
    ///   全部落在 `Library/`（Unity 的临时目录，不是工程数据）✓ 不碰 `Assets/**` ✓
    ///
    /// ★ 它改了什么（都只是**局内状态**，与 `ProbeV196PlantCard` 同一尺度）★
    ///   · 往这一局的 `PlayerBuffs` 里塞几条测试增益（不写任何工程文件）
    ///   · 给玩家临时加一条闪避修正、把 `InvulnRemaining` 清零以便反复掷闪避
    ///   ⇒ 退出 Play 全部消失 ✓
    /// </summary>
    public sealed class ProbeV197BuffHud : MonoBehaviour
    {
        private static string Root => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Root, "Library", "yowei_v197_buffhud.on");
        private static string OutDir => Path.Combine(Root, "Library", "yowei-shots");
        private static string ReportPath => Path.Combine(Root, "Library", "yowei-selfcheck-v197.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV197BuffHud");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV197BuffHud>();
            Log.Info("PROBEV197 armed（Library/yowei_v197_buffhud.on 存在）");
        }

        private RunSession _s;
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly StringBuilder _shots = new StringBuilder();

        private void Start() => StartCoroutine(Sequence());

        private bool Grab(out RunSession s)
        {
            s = null;
            if (GameBootstrap.Live == null || GameBootstrap.Live.Count == 0) return false;
            s = GameBootstrap.Live[0].Session;
            return s != null && s.World != null && s.World.Player != null;
        }

        /// <summary>报告里的一行（`text` 省略 = 空行）。</summary>
        private void Line(string text = "") => _sb.AppendLine(text);

        // ─────────────────────────────────────────────────────── ① 自检 + 契约
        private void Head()
        {
            _sb.Clear();
            Line("★ Seedfall v197 增益栏 / 闪避 —— 只读探针报告 ★");
            Line("生成时刻：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line();

            try
            {
                var results = SelfCheck.RunAll();
                int failed = 0;
                for (int i = 0; i < results.Count; i++) if (!results[i].Passed) failed++;
                Line($"══ 自检：{results.Count - failed}/{results.Count} 通过，{failed} 失败 ══");
                for (int i = 0; i < results.Count; i++)
                    if (!results[i].Passed) Line("   FAIL  " + results[i].Name + "  " + results[i].Detail);
                Line();
                Log.Info($"PROBEV197 自检 {results.Count - failed}/{results.Count} 通过（失败 {failed}）");
            }
            catch (Exception e)
            {
                Line("自检抛异常：" + e.GetType().Name + ": " + e.Message);
                Log.Error("PROBEV197 自检抛异常：" + e.GetType().Name + ": " + e.Message);
            }

            // 排布常量（需求：血量下方的 y 到底是多少）
            Line("══ 一、增益区排布常量（ImGuiHud 的公开常量）══");
            Line($"   格子 {ImGuiHud.BuffChipSize:0.#}px ｜ 间距 {ImGuiHud.BuffChipGap:0.#}px ｜ 一行最多 {ImGuiHud.BuffRowMax} 格");
            Line($"   行顶 y = {ImGuiHud.BuffRowY:0.#} ｜ 左基准 x = {ImGuiHud.BuffRowX:0.#}");
            Line("   ↑ 血量行 20..44、护盾 48..70、经验 74..96、金币 98..122 ⇒ 本区 124..146（不覆盖上面任何一条）");
            Line("   按键说明字框从 146 起、字形实际从 ~153 才有像素 ⇒ 两块字形之间仍留 ~7px");
            Line("   前 8 格的矩形：");
            for (int i = 0; i < ImGuiHud.BuffRowMax; i++)
            {
                Rect r = ImGuiHud.BuffChipAt(i);
                Line($"     #{i}  x {r.x:0.#}..{r.xMax:0.#}   y {r.y:0.#}..{r.yMax:0.#}");
            }
            Line();

            // 闪避的两个定值
            Line("══ 二、闪避的三条口径 ══");
            TuningDef t = TableDatabase.Tuning;
            Line($"   ① 上限 caps.dodge = {t.caps.dodge:0.###}（需求 60% = 0.6 ⇒ {(Mathf.Abs(t.caps.dodge - 0.6f) < 0.001f ? "已达标，无需改动" : "未达标")}）");
            Line($"   ② 虚化时长 PlayerActor.EvadeGhostSeconds = {PlayerActor.EvadeGhostSeconds:0.##}s");
            Color gray = PopupStyle.ColorOf(PopupKind.Evade);
            Line($"   ③ 灰色弹字 PopupStyle.ColorOf(Evade) = rgb({gray.r:0.000},{gray.g:0.000},{gray.b:0.000})"
                 + $" ｜ 文字型 = {PopupStyle.IsTextKind(PopupKind.Evade)}"
                 + $" ｜ 错位 = {PopupStyle.StackOffsetOf(PopupKind.Evade)}");
            Line();
        }

        private void Flush()
        {
            try
            {
                if (_shots.Length > 0) { Line("══ 六、抓图清单 ══"); Line(_shots.ToString()); }
                File.WriteAllText(ReportPath, _sb.ToString(), new UTF8Encoding(false));
                Log.Info("PROBEV197 报告 " + Path.GetFullPath(ReportPath).Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Error("PROBEV197 写报告失败：" + e.GetType().Name + ": " + e.Message);
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
                if (tex == null) { Log.Error("PROBEV197 抓图为空：" + name); yield break; }
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
                Log.Error("PROBEV197 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
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
                _shots.AppendLine("   " + name + stat);
                Log.Info($"PROBEV197 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV197 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        // ─────────────────────────────────────────────────────── ③ 读数
        private void DumpBuffs(string tag, PlayerBuffs buffs)
        {
            Line($"══ 增益区 {tag} ══");
            if (buffs == null) { Line("   PlayerBuffs = null（另一路还没接线 ⇒ 本区不画，这是契约兜底路径）"); Line(); return; }
            var list = buffs.Active;
            Line($"   Active.Count = {(list == null ? -1 : list.Count)} ｜ 实际画出的格子 = {ImGuiHud.BuffChipRects.Count}");
            for (int i = 0; i < (list == null ? 0 : list.Count); i++)
            {
                BuffInstance b = list[i];
                string key = TableDatabase.ItemIconMaskKeyAt(b.IconMaskIdx);
                Line($"   [{i}] Id={b.Id}  Label={b.Label}  ×{b.Stacks}  Remain={b.Remain:0.00}s"
                     + $"  IconMaskIdx={b.IconMaskIdx} → 掩码键 {key ?? "(null ⇒ 暖色块兜底)"}");
            }
            for (int i = 0; i < ImGuiHud.BuffChipRects.Count; i++)
            {
                Rect r = ImGuiHud.BuffChipRects[i];
                Line($"   格 #{i}: x {r.x:0.#}..{r.xMax:0.#}  y {r.y:0.#}..{r.yMax:0.#}");
            }
            Line($"   悬停浮框最近三行 = 「{ImGuiHud.LastBuffTip[0] ?? ""}」/「{ImGuiHud.LastBuffTip[1] ?? ""}」/「{ImGuiHud.LastBuffTip[2] ?? ""}」");
            Line();
        }

        private IEnumerator Sequence()
        {
            Head();
            Flush();
            yield return new WaitForSecondsRealtime(1.0f);

            if (!Grab(out _s)) { Line("取不到当前局会话 ⇒ 只出文字报告"); Flush(); yield break; }

            // 到战斗相位（草地底最接近实际观感）
            if (_s.Phase == PhaseState.Prepare) _s.SetPhasePublic(PhaseState.Combat);
            _s.Paused = false;
            yield return new WaitForSecondsRealtime(0.6f);

            ImGuiHud.ResetBuffsProbe();
            PlayerBuffs buffs = ImGuiHud.BuffsOf(_s);
            if (buffs == null)
            {
                Line("★★ PlayerBuffs 尚未挂到 RunSession ⇒ 增益区按契约**不画**（不占位/不报错）★★");
                Log.Warn("PROBEV197 PlayerBuffs 未接线：增益区走 null 兜底");
                DumpBuffs("（未接线）", null);
                Flush();
            }
            else
            {
                // ── A：1 格 ──
                buffs.Clear();
                buffs.Apply(BuffIds.PlantAtkMult, "收割·攻击 +3%", 1, 3f, false);
                yield return null; yield return null;
                DumpBuffs("A：只有 1 条（×1）", buffs);
                yield return Shot("v197-a-buff-1.png");

                // ── B：多格 + 层数 + 溢出边界 ──
                buffs.Clear();
                buffs.Apply(BuffIds.PlantAtkMult, "收割·攻击 +3%", 12, 3f, false);
                buffs.Apply(BuffIds.PlantAtkSpeed, "收割·攻速 +3%", 8, 3f, false);
                buffs.Apply(BuffIds.ItemAtkSpeedRamp, "爬升·攻速", 60, 60f, false);
                buffs.Apply(BuffIds.ItemDamageRamp, "爬升·伤害", 5, 60f, false);
                buffs.Apply("probe_buff_a", "测试增益·甲", 3, 6f, false);
                buffs.Apply("probe_buff_b", "测试增益·乙", 2, 10f, false);
                yield return null; yield return null;
                DumpBuffs("B：6 条（含 ×12 / ×60 角标）", buffs);
                yield return Shot("v197-b-buff-many.png");

                // ── B2：溢出（9 条 ⇒ 7 格 + `+2`）──
                buffs.Apply("probe_buff_c", "测试增益·丙", 4, 10f, false);
                buffs.Apply("probe_buff_d", "测试增益·丁", 6, 10f, false);
                buffs.Apply("probe_buff_e", "测试增益·戊", 7, 10f, false);
                yield return null; yield return null;
                DumpBuffs("B2：9 条（应 7 格 + 「+2」）", buffs);
                yield return Shot("v197-b2-buff-overflow.png");

                // ── C：悬停浮框（探针钩子只换坐标，判据仍是真实 Contains）──
                ImGuiHud.ForceBuffTipIndex = 0;
                yield return null; yield return null;
                DumpBuffs("C：悬停第 0 格", buffs);
                yield return Shot("v197-c-buff-hover.png");
                ImGuiHud.ForceBuffTipIndex = -1;
            }

            // ── D：闪避（虚化 + 灰色「闪避」）──
            yield return EvadePhase();

            Line("══ 五、抓图清单见文末 ══");
            Flush();
            Log.Info("PROBEV197 DONE");
        }

        /// <summary>给玩家拉满闪避 → 反复打自己直到掷出一次闪避 → 立刻抓「虚化 + 灰字」。</summary>
        private IEnumerator EvadePhase()
        {
            Line("══ 四、闪避成功 = 虚化 + 灰色「闪避」══");
            PlayerActor p = _s.World.Player;
            if (_s.Phase != PhaseState.Combat) { Line("   当前不在 Combat ⇒ 跳过闪避取证"); Line(); yield break; }

            // 闪避是"概率"，靠加修正把它推到上限 0.6；每轮清掉无敌帧，否则 Miss 不会掷闪避
            p.Progress.Stats.AddModifier(new StatModifier(StatId.Dodge, 1.0f, ModOp.Add, "probe_v197"));
            float dodge = p.Progress.Stats.Get(StatId.Dodge);
            Line($"   掷闪避用的闪避率 = {dodge:0.###}（上限 {TableDatabase.Tuning.caps.dodge:0.###}）");

            int tries = 0;
            bool evaded = false;
            for (; tries < 40 && !evaded; tries++)
            {
                p.InvulnRemaining = 0f;      // 否则这一下直接 Miss，不会走到掷闪避
                _s.World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical,
                    RawValue = 0.1f,          // ×10 血点 ⇒ 1 点，40 次也打不死
                    Target = p,
                });
                if (p.EvadeGhost > 0f) evaded = true;
                yield return null;
            }

            if (!evaded)
            {
                Line($"   ✗ {tries} 次都没掷出闪避（闪避率 {dodge:0.###}）⇒ 这一项没验到");
                Line();
                yield break;
            }

            Line($"   ✓ 第 {tries} 次掷出闪避");
            Line($"   · PlayerActor.EvadeGhost = {p.EvadeGhost:0.000}s（满值 {PlayerActor.EvadeGhostSeconds:0.##}s）");
            Line($"   · 虚化是否在画 = {p.EvadeGhost > 0f}（ImGuiHud.DrawEvadeGhost 只读这一个数）");

            // 弹字：在 Popups 里找 Evade 那一条（原文 + 色值 + 锚点）
            bool found = false;
            for (int i = 0; i < _s.Popups.Count; i++)
            {
                RunSession.FloatingText ft = _s.Popups[i];
                if (ft.Kind != PopupKind.Evade) continue;
                found = true;
                Line($"   · 弹字原文「{ft.Text}」  色 rgb({ft.Color.r:0.000},{ft.Color.g:0.000},{ft.Color.b:0.000})"
                     + $"  缩放 {ft.Scale:0.##}  Ttl {ft.Ttl:0.00}/{ft.Total:0.00}s");
                Line($"   · 锚点 Pos=({ft.Pos.x:0.00},{ft.Pos.y:0.00}) ｜ 玩家 Pos=({p.Pos.x:0.00},{p.Pos.y:0.00})"
                     + $" ⇒ 错位 Δ=({ft.Pos.x - p.Pos.x:0.00},{ft.Pos.y - p.Pos.y:0.00})");
            }
            if (!found) Line("   ✗ Popups 里没有 Evade 那一条（弹字没上屏）");
            Line();

            // ⚠️ 抓图的时刻是有讲究的（第一次抓到的是「几乎看不见」的那一帧）：
            //   弹字有三段动画：age<9% 爆开（起手 0.15×、alpha 从 0 爬到 1）、
            //   9%~26% 回落、26%~68% **稳定（1.0×、alpha=1）**、68% 后缩小淡出。
            //   虚化只有 0.40s，而且 `fade = k²`（后段掉得极快）。
            //   ⇒ 两段窗口几乎不重叠，所以**分两张抓**：
            //     ① t+0.05s：虚化最实（alpha≈0.35）、弹字已经满 alpha 且放大到 1.5×
            //     ② t+0.20s：弹字进入稳定段（1.0×、alpha=1），虚化已淡成薄影
            yield return new WaitForSecondsRealtime(0.05f);
            yield return Shot("v197-d1-evade-ghost.png");
            yield return new WaitForSecondsRealtime(0.15f);
            yield return Shot("v197-d2-evade-popup.png");
        }
    }
}
