using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v199：**只读探针** —— 本轮五件事的读数与画面取证 ★★
    ///
    /// 它回答的四组问题（第 ⑤ 条只确认、不修）：
    ///   ① 雨滴掉落概率的**现值** + 雨滴的**寿命**（5 秒后是不是真的从 `Pickups` 里消失、怎么淡出）
    ///   ② 菜单里 6 个植物格**滚到底**之后，最后一行（特殊功能）是不是**整行字形**都在
    ///   ③ 滚轮转武器的**门控矩阵**：战斗转、商店/抽卡/菜单不转（逐格给 before → after）
    ///   ④ 属性面板上「（上限 X）」的**完整清单**（印的就是屏幕上那两个函数吐的串）
    ///   ⑤ `gamble_glove`（赌命手套）的"攻速上限 −200%"到底改到了谁 —— 生效／不生效
    ///
    /// ★ 只读 ★ 不写 `Assets/**`（`Library/` 是 Unity 的临时目录：不进版本库、不参与打包），
    ///   不改表、不改场景；`new RunSession()` 用完立刻 `Dispose()`（与自检同一套夹具用法）✓
    ///
    /// 触发方式：`Library/yowei_v199_quick.on` 存在 ⇒ 进 Play 时自建（与 v198 的探针同一套约定）。
    /// </summary>
    public sealed class ProbeV199Quick : MonoBehaviour
    {
        public const string ReportPath = "Library/yowei-v199-quick.txt";

        private static string Root => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Root, "Library", "yowei_v199_quick.on");
        private static string OutDir => Path.Combine(Root, "Library", "yowei-shots");

        /// <summary>6 株机制植物（与 v198 同一组 ⇒ 每格都有"特殊功能"那一条长句）。</summary>
        private static readonly string[] MechSeeds =
        {
            "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily", "shelter_ivy", "lucky_monarch",
        };

        private static readonly StatId[] PanelStats =
        {
            StatId.MeleeDamage, StatId.RangedDamage, StatId.BlastDamage, StatId.AtkMult, StatId.ExplodeRange,
            StatId.AtkSpeed, StatId.AtkRange, StatId.MoveSpeed, StatId.CritRate, StatId.CritDmg, StatId.Dodge,
            StatId.Luck, StatId.Knockback, StatId.ExpMult, StatId.GoldMult, StatId.HeartDropRate,
            StatId.HeartHeal, StatId.PigRate, StatId.MaxPlots,
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV199Quick");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV199Quick>();
            Log.Info("PROBEV199QUICK armed（Library/yowei_v199_quick.on 存在）");
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

        private IEnumerator Sequence()
        {
            var sb = new StringBuilder();
            sb.AppendLine("★ Seedfall v199 五件事 —— 只读探针报告 ★");
            sb.AppendLine("生成时刻：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            try { TextProof(sb); }
            catch (Exception e) { sb.AppendLine("文字读数抛异常：" + e.GetType().Name + ": " + e.Message); }
            TryWrite(sb);                                  // 先落盘：画面那一步失败也留证据

            yield return new WaitForSecondsRealtime(1f);
            if (!Grab(out _s)) { Log.Warn("PROBEV199QUICK 取不到当前局会话 ⇒ 只出了文字报告"); yield break; }

            // ── 画面：菜单（右栏属性 / 中栏植物格）+ 植物格滚到底 ──
            FillBagWithMechPlants();
            _s.Paused = true;                              // Esc ⇒ DrawMenu
            yield return null; yield return null; yield return null;
            yield return Shot("v199-a-menu-caps-top.png");  // ④ 属性面板（上限 X）那一列

            ImGuiHud hud = GameBootstrap.Live != null && GameBootstrap.Live.Count > 0
                ? GameBootstrap.Live[0].Hud : null;
            if (hud == null) { Log.Warn("PROBEV199QUICK 拿不到 ImGuiHud ⇒ 滚不动植物格"); yield break; }
            for (int i = 0; i < 6; i++) hud.DebugScrollPlantCell(i, 9999f);   // 会被钳到底部
            sb.AppendLine($"植物格正文行距（本帧实际用的）= {hud.PlantCellLineHeight:0.##}px（改前写死 17px）");
            yield return null; yield return null; yield return null;
            for (int i = 0; i < ImGuiHud.PlantCellLayoutLog.Length; i++)
                if (!string.IsNullOrEmpty(ImGuiHud.PlantCellLayoutLog[i]))
                    sb.AppendLine("   " + ImGuiHud.PlantCellLayoutLog[i]);
            yield return Shot("v199-b-plant-card-bottom.png");  // ② 滚到底：最后一行完整

            TryWrite(sb);
            Log.Info("PROBEV199QUICK DONE → " + ReportPath);
        }

        // ═══════════════════════════════════════════════════ 文字读数
        private void TextProof(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;

            // ── ① 雨滴 ───────────────────────────────────────────────
            sb.AppendLine("══ ① 雨滴：掉落概率 ＋ 5 秒寿命 ══");
            sb.AppendLine($"   `World.RainDropChancePerKill` = **{World.RainDropChancePerKill:0.###}**（= {World.RainDropChancePerKill * 100f:0.#}%）");
            sb.AppendLine($"   `Pickup.RainLifeSeconds` = {Pickup.RainLifeSeconds:0.##}s｜淡出窗口 {Pickup.RainFadeSeconds:0.##}s");
            {
                var w = MakeWorld(out PlayerActor pw);
                if (w != null)
                {
                    Pickup pk = SpawnRainFar(w, pw);
                    float tt = 0f, goneAt = -1f;
                    float sAt3 = 0f, sAt42 = 0f, sAt49 = 0f;
                    while (tt < 8f)
                    {
                        tt += GameClock.FixedDt;
                        pk.Tick(GameClock.FixedDt, w);
                        if (Mathf.Abs(tt - 3f) < GameClock.FixedDt * 0.5f) sAt3 = pk.RainFadeScale;
                        if (Mathf.Abs(tt - 4.2f) < GameClock.FixedDt * 0.5f) sAt42 = pk.RainFadeScale;
                        if (Mathf.Abs(tt - 4.9f) < GameClock.FixedDt * 0.5f) sAt49 = pk.RainFadeScale;
                        if (pk.Dead) { goneAt = tt; break; }
                    }
                    sb.AppendLine($"   单颗雨滴逐帧 Tick 的读数（60Hz）：Dead 发生在 t = **{goneAt:0.000}s**"
                                  + $"（{GameClock.FixedDt * 60f:0}Hz 下一个刻度 = 5.017s）");
                    sb.AppendLine($"   淡出缩放（只乘 scale，不动材质 alpha）：t=3.0s → {sAt3:0.###}｜t=4.2s → {sAt42:0.###}｜t=4.9s → {sAt49:0.###}");

                    // 端到端：`World.Simulate` 里会被 `Cleanup()` 真正摘掉
                    Pickup pk2 = SpawnRainFar(w, pw);
                    w.Pickups.Add(pk2);
                    int rain0 = CountRain(w);
                    float te = 0f;
                    int rainAt45 = -1;
                    while (te < 7f)
                    {
                        w.Simulate(GameClock.FixedDt);
                        te += GameClock.FixedDt;
                        if (rainAt45 < 0 && te >= 4.5f) rainAt45 = CountRain(w);
                        if (CountRain(w) == 0) break;
                    }
                    sb.AppendLine($"   端到端（`World.Simulate` → `Cleanup()`）：放进去 {rain0} 滴 ⇒ t≈4.5s 场上 {rainAt45} 滴"
                                  + $" ⇒ t≈{te:0.00}s 场上 {CountRain(w)} 滴（列表里真的没了 = 消失，不是只置了个标记）");
                }
                else sb.AppendLine("   （造不出 World 夹具 ⇒ 这一条没测到）");
            }
            sb.AppendLine();

            // ── ③ 滚轮门控矩阵 ────────────────────────────────────────
            sb.AppendLine("══ ③ 滚轮转武器：门控矩阵（`RunSession.ConsumeFrameEdges`）══");
            sb.AppendLine("   改后门控条件：`Phase == Combat && !Paused && World != null && Player != null && Player.Alive`");
            sb.AppendLine("   " + WheelRow("战斗（Combat，未暂停）", PhaseState.Combat, false));
            sb.AppendLine("   " + WheelRow("商店（Shop）", PhaseState.Shop, false));
            sb.AppendLine("   " + WheelRow("抽卡（LevelReward）", PhaseState.LevelReward, false));
            sb.AppendLine("   " + WheelRow("菜单（Combat + Paused）", PhaseState.Combat, true));
            sb.AppendLine("   " + WheelRow("选种（Prepare）", PhaseState.Prepare, false));
            sb.AppendLine();

            // ── ④ 「（上限 X）」清单 ───────────────────────────────────
            sb.AppendLine("══ ④ 属性面板「（上限 X）」清单（印的就是 `CapSuffix` 吐的串）══");
            ImGuiHud hud = GameBootstrap.Live != null && GameBootstrap.Live.Count > 0 ? GameBootstrap.Live[0].Hud : null;
            if (hud != null)
            {
                for (int i = 0; i < PanelStats.Length; i++)
                {
                    string suf = hud.CapSuffix(PanelStats[i]);
                    sb.AppendLine($"   {PanelStats[i],-16} {(string.IsNullOrEmpty(suf) ? "（无上限 ⇒ 不印）" : suf)}");
                }
            }
            else sb.AppendLine("   （拿不到 ImGuiHud ⇒ 这一条没测到）");
            sb.AppendLine();

            // ── ⑤ gamble_glove 的"改上限"是否生效 ─────────────────────
            sb.AppendLine("══ ⑤ `gamble_glove`（赌命手套）「改攻速上限」是否生效 ══");
            // ★ v3xx：这一行原来是**写死的旧表**（CritRate 0.99 / CritDmg 999 / AtkSpeed −200 / AtkRange 99）。
            //   用户那一轮把赌命手套整条换掉了 ⇒ 写死的字符串会变成"探针在撒谎" ✗ ⇒ 改成**读表**：
            //   表里现在是什么就印什么（`effects` 四条 + rule），一行都不用再维护 ✓
            var gloveTable = TableDatabase.GetItem("gamble_glove");
            string gloveFx = "表里没有 gamble_glove";
            if (gloveTable != null)
            {
                var fx = new System.Text.StringBuilder();
                foreach (ItemBonus b in gloveTable.Effects)
                {
                    if (fx.Length > 0) fx.Append(" ＋ ");
                    fx.Append(b.IsPercent ? $"Percent {b.Stat} {b.Add}" : $"Add {b.Stat} {b.Add}");
                }
                gloveFx = $"effects = {fx}｜rule = {gloveTable.Rule} {gloveTable.RuleValue}";
            }
            sb.AppendLine($"   表：{gloveFx}");
            sb.AppendLine("   ⚠️ 下面这两条读数来自 v199 那次的旧数值（AtkSpeed −200%）；现表已改为 −9.9%");
            sb.AppendLine("       ⇒ 本段只剩「机制是否改写上限」这一问仍然有效，绝对数值不再可比 ✓");
            sb.AppendLine($"   写侧：`ProgMechanics.ApplyCapsToTable` ⇒ `StatBlock.AtkSpeedCapFixedActive = true` / `AtkSpeedCapFixed = −2`");
            sb.AppendLine($"   读侧：`StatBlock.CapOf(AtkSpeed)` ⇒ `min(AtkSpeedCapFixed, caps.atkSpeed)`");
            sb.AppendLine($"   表级 `caps.atkSpeed` = {t.caps.atkSpeed:0.###}｜`ProgMechanics.Tick` → `ApplyCapsToTable`（每帧，见 ProgMechanics.cs:334）");
            {
                var s = new RunSession();
                s.Start(9101897UL);
                s.StartWave(1);
                StatBlock.ResetRuntimeOverrides();
                s.Progress.Stats.Evaluate();
                float atk0 = s.Progress.Stats.Get(StatId.AtkSpeed);
                float cd0 = t.tools.hoe.cooldown / Mathf.Max(0.05f, atk0);
                sb.AppendLine($"   改前：AtkSpeed = {atk0:0.###} ⇒ 锄头每击 {cd0:0.###}s");

                ItemDef glove = TableDatabase.GetItem("gamble_glove");
                if (glove == null) sb.AppendLine("   表里没有 gamble_glove ✗");
                else
                {
                    s.AddGoldDirect(999);
                    s.ApplyItem(glove);
                    ProgMechanics.ApplyCapsToTable(s);                   // 与每帧那条调用同一个函数
                    s.Progress.Stats.Evaluate();
                    float atk1 = s.Progress.Stats.Get(StatId.AtkSpeed);
                    float cd1 = t.tools.hoe.cooldown / Mathf.Max(0.05f, atk1);
                    sb.AppendLine($"   改后：AtkSpeed = **{atk1:0.###}** ⇒ 锄头每击 **{cd1:0.###}s**（×{cd1 / cd0:0.##}）");
                    sb.AppendLine($"   静态位：AtkSpeedCapFixedActive = {StatBlock.AtkSpeedCapFixedActive}"
                                  + $"｜AtkSpeedCapFixed = {StatBlock.AtkSpeedCapFixed:0.###}");
                    var probe = new StatBlock(t.caps, t.player);
                    probe.SetBase(StatId.AtkSpeed, 1e7f);
                    sb.AppendLine($"   `CapOf` 的实际读数（把基底顶到 1e7 再读回，与 UI 同一个办法）= **{probe.Get(StatId.AtkSpeed):0.###}**");
                    if (hud != null) sb.AppendLine($"   UI 上那一行会印：攻击速度 …… {hud.CapSuffix(StatId.AtkSpeed)}");
                    // 分开两条：把"上限改写"关掉，只留 effects 里的 Percent AtkSpeed −200
                    StatBlock.ResetRuntimeOverrides();
                    s.Progress.Stats.Evaluate();
                    float atkEffectOnly = s.Progress.Stats.Get(StatId.AtkSpeed);
                    float cdEffectOnly = t.tools.hoe.cooldown / Mathf.Max(0.05f, atkEffectOnly);
                    sb.AppendLine($"   对照（**只留 effects、把上限改写关掉**）：AtkSpeed = {atkEffectOnly:0.###}"
                                  + $" ⇒ 锄头每击 {cdEffectOnly:0.###}s（×{cdEffectOnly / cd0:0.##}）");
                    sb.AppendLine("   ⇒ 两条**各自都让攻击慢 20 倍**：`Mathf.Max(0.05f, atkSpeed)` 把 −1 与 −2 兜成同一个下限");
                    if (s.ItemStacks != null && s.ItemStacks.TryGetValue("gamble_glove", out int n))
                        sb.AppendLine($"   `ItemStacks[\"gamble_glove\"]` = {n}（`ItemPanel.StacksOf` 读的就是它）");
                    // 撤销：一条都不改，只把静态位复位，免得污染后面的会话
                    StatBlock.ResetRuntimeOverrides();
                }
                s.Dispose();
            }
            sb.AppendLine();

            // ── 自检 ────────────────────────────────────────────────
            sb.AppendLine("══ 自检（`SelfCheck.RunAll`）══");
            try
            {
                var results = SelfCheck.RunAll();
                int failed = 0;
                for (int i = 0; i < results.Count; i++) if (!results[i].Passed) failed++;
                sb.AppendLine($"   {results.Count - failed}/{results.Count} 通过，{failed} 失败");
                for (int i = 0; i < results.Count; i++)
                    if (!results[i].Passed) sb.AppendLine("   FAIL  " + results[i].Name + "  " + results[i].Detail);
            }
            catch (Exception e)
            {
                sb.AppendLine("   自检抛异常：" + e.GetType().Name + ": " + e.Message);
            }
            sb.AppendLine();
        }

        // ── 小工具 ───────────────────────────────────────────────
        /// <summary>造一个自检同款的最小世界（不建视觉）。</summary>
        private static World MakeWorld(out PlayerActor player)
        {
            TuningDef t = TableDatabase.Tuning;
            var w = new World(t, 12345UL);
            player = new PlayerActor
            {
                Id = w.NextActorId(),
                Team = Team.Player,
                Progress = new PlayerProgress(t),
                Pos = Vector2.zero,
                Radius = 0.5f,
            };
            player.Health.Setup(player.Progress.Stats.Get(StatId.MaxHp), player.Progress.Stats.Get(StatId.Shield));
            w.Player = player;
            return w;
        }

        /// <summary>在离玩家很远的地方放一颗雨滴（躲开 `dropAttractRadius`，否则会被吸过去捡掉）。</summary>
        private static Pickup SpawnRainFar(World w, PlayerActor pw)
        {
            Vector2 pos = pw.Pos + new Vector2(6f, 6f);
            return new Pickup { IsRain = true, IsHeart = true, Pos = pos };
        }

        private static int CountRain(World w)
        {
            int n = 0;
            for (int i = 0; i < w.Pickups.Count; i++) if (w.Pickups[i].IsRain) n++;
            return n;
        }

        /// <summary>滚轮一格：给 before → after，并判定转没转。</summary>
        private static string WheelRow(string label, PhaseState phase, bool paused)
        {
            var s = new RunSession();
            s.Start(5353UL);
            s.StartWave(1);
            if (phase != PhaseState.Combat) s.SetPhasePublic(phase);
            s.Paused = paused;
            int before = s.World.Player.WeaponIndex;
            var input = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, WheelScroll = 1
            };
            s.ConsumeFrameEdges(ref input);
            int after = s.World.Player.WeaponIndex;
            string verdict = after != before ? "**转**（武器 " + before + " → " + after + "）" : "不转（仍是 " + before + "）";
            s.Dispose();
            return $"{label,-22} {verdict}";
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
            var sb = new StringBuilder("PROBEV199QUICK 入袋：");
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
                if (tex == null) { Log.Error("PROBEV199QUICK 抓图为空：" + name); yield break; }
                Color32[] px = tex.GetPixels32();
                int lit = 0, samples = 0;
                for (int i = 0; i < px.Length; i += 37)
                {
                    Color32 c = px[i];
                    int luma = (c.r * 30 + c.g * 59 + c.b * 11) / 100;
                    if (luma > 40) lit++;
                    samples++;
                }
                png = tex.EncodeToPNG();
                stat = $"｜{tex.width}×{tex.height}｜{png.Length} 字节｜亮像素 {lit}/{samples}";
            }
            catch (Exception e)
            {
                Log.Error("PROBEV199QUICK 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
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
                Log.Info($"PROBEV199QUICK 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEV199QUICK 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void TryWrite(StringBuilder sb)
        {
            try
            {
                File.WriteAllText(Path.GetFullPath(ReportPath), sb.ToString(), new UTF8Encoding(false));
                Log.Info("PROBEV199QUICK 报告 " + Path.GetFullPath(ReportPath).Replace('\\', '/'));
            }
            catch (Exception e)
            {
                Log.Error("PROBEV199QUICK 写报告失败：" + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
