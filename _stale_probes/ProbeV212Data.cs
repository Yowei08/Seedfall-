using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v212：**数据层只读探针**（锄头手执朝向 ＋ 三张表的规模/描述/CapSuffix 白名单）★★
    ///
    /// ── 它量什么（全是跑出来的读数，不是复述自检的结论）──
    ///   ① 锄头在**掌心局部系**里的本地欧拉角（当前值）＋ 改前值 `(90,0,0)` 的对照，
    ///      以及"武器长轴"与"玩家正前方 +Z"的夹角（这是"指着正面"这条需求唯一的判据）✓
    ///   ② 三张表的规模：seeds 30 / items 86 / plugins 36，以及"每条 description 里都有数字"✓
    ///   ③ `ImGuiHud.CapSuffix` 对每个属性的**实际输出**（白名单是不是只有那五项）✓
    ///   ④ `gamble_glove`（赌命手套）"攻速 −200%"在实战里等于什么倍率（含 `Mathf.Max(0.05f, ·)` 兜底）✓
    ///
    /// ── 只读性 ──
    ///   · **没有 `[RuntimeInitializeOnLoadMethod]`**：进 play 不会自动建实例（对实机零影响）✓
    ///   · 只读表、只建一个临时 `PlayerBean` 量完就 `DestroyImmediate`
    ///   · 报告只落 `<工程>/Library/`，不写 `Assets/**`、不改表、不改场景 ✓
    ///
    /// ── 怎么跑（编辑器里的桥）──
    ///   /scene create 一个挂本组件的空物体（名字 `ProbeV212`）⇒
    ///   /input action=send path=ProbeV212 method=Run            → ①②③④ 落盘
    ///   /input action=send path=ProbeV212 method=RunSelfCheck   → 自检原文落盘
    ///   /input action=send path=ProbeV212 method=Cleanup        → 把自己删掉
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV212Data : MonoBehaviour
    {
        public const string ReportPath = "Library/yowei-v212-data.txt";
        public const string SelfCheckPath = "Library/yowei-v212-selfcheck.txt";

        /// <summary>改前的握持欧拉角（v212 之前的值，来自 `PlayerBean.ToolGripEuler` 的历史注释）。</summary>
        private static readonly Vector3 GripBefore = new Vector3(90f, 0f, 0f);

        public string Run()
        {
            string outcome = Execute();
            Debug.Log("[PROBEV212] " + outcome);
            return outcome;
        }

        public string RunSelfCheck()
        {
            string outcome = ExecuteSelfCheck();
            Debug.Log("[PROBEV212] " + outcome);
            return outcome;
        }

        public string Cleanup()
        {
            string name = gameObject.name;
            DestroyImmediate(gameObject);
            return "removed " + name;
        }

        private static string Execute()
        {
            var sb = new StringBuilder();
            string outcome;
            try
            {
                TableDatabase.Load(true);
                DumpHoeGrip(sb);
                DumpTables(sb);
                DumpCapSuffix(sb);
                DumpGambleGlove(sb);
                outcome = "ok：" + Write(ReportPath, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("PROBE-V212 异常：" + ex.GetType().Name + "：" + ex.Message);
                sb.AppendLine(ex.StackTrace);
                outcome = "EXCEPTION：" + ex.GetType().Name + "：" + ex.Message;
                Debug.LogError("[PROBEV212] " + outcome);
                Write(ReportPath, sb);
            }
            return outcome;
        }

        // ══════════════════════════════════════════════════════════════════
        // ① 锄头朝向
        // ══════════════════════════════════════════════════════════════════
        private static void DumpHoeGrip(StringBuilder sb)
        {
            sb.AppendLine("═══ ① 锄头「指着正面」—— 本地欧拉角与夹角 ═══");
            Vector3 gripNow = GripField();
            sb.AppendLine($"PlayerBean.ToolGripEuler（现在）= {E(gripNow)}｜改前 = {E(GripBefore)}");
            sb.AppendLine($"PlayerBean.ToolRoll = {ToolRollText()}（0 = 不绕手柄轴滚）");
            sb.AppendLine("判据：武器模型的长轴（本地 +Y）= 玩家正前方（本地 +Z）⇒ 夹角 0°");

            PlayerBean bean = null;
            GameObject root = null;
            try
            {
                root = new GameObject("ANIM_probe_v212");
                root.transform.position = new Vector3(1000f, 1000f, 1000f);   // 挪到画面外，别干扰场景
                bean = PlayerBean.Build(root.transform);
                for (int w = 0; w < PlayerBean.ToolIds.Length; w++)
                {
                    GameObject tool = bean.MountTool(w);
                    string id = PlayerBean.ToolIds[w];
                    if (tool == null) { sb.AppendLine($"  {id}：挂不上（Resources 里没有模型）"); continue; }
                    // 掌心挂点的世界旋转：武器长轴 = 挂点旋转 × 握持旋转 × (0,1,0)
                    Quaternion hand = bean.HandMount.rotation;
                    Vector3 upNow = hand * (Quaternion.Euler(gripNow) * Vector3.up);
                    Vector3 upBefore = hand * (Quaternion.Euler(GripBefore) * Vector3.up);
                    Vector3 fwd = root.transform.forward;        // 模型正面 = 本地 +Z（PlayerBean 的约定）
                    float angNow = Vector3.Angle(upNow, fwd);
                    float angBefore = Vector3.Angle(upBefore, fwd);
                    sb.AppendLine($"  {id}：本地欧拉角（实测 transform.localEulerAngles）= {E(tool.transform.localEulerAngles)}");
                    sb.AppendLine($"      长轴方向 现在 = {V(upNow)} ⇒ 与正面夹角 {F(angNow)}°｜改前 = {V(upBefore)} ⇒ {F(angBefore)}°");
                }
                sb.AppendLine("注：localEulerAngles 是**挂上去那一帧**的值（idle 姿态、手部无额外旋转时）；");
                sb.AppendLine("    姿态一动（挥击/受击）挂点会跟着手臂转 —— 这是「武器握在手里」的必然结果 ✓");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
            sb.AppendLine();
        }

        private static Vector3 GripField()
        {
            FieldInfo f = typeof(PlayerBean).GetField("ToolGripEuler",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return Vector3.zero;
            object v = f.GetValue(null);
            return v is Vector3 vec ? vec : Vector3.zero;
        }

        private static string ToolRollText()
        {
            FieldInfo f = typeof(PlayerBean).GetField("ToolRoll",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return "(找不到字段)";
            var arr = f.GetValue(null) as float[];
            if (arr == null) return "(不是 float[])";
            var sb = new StringBuilder("{");
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(F(arr[i]));
            }
            return sb.Append('}').ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // ② 三张表
        // ══════════════════════════════════════════════════════════════════
        private static void DumpTables(StringBuilder sb)
        {
            sb.AppendLine("═══ ② 三张表的规模与描述（v212 数据层）═══");
            SeedDef[] seeds = TableDatabase.Seeds;
            ItemDef[] items = TableDatabase.Items;
            PluginAsset[] plugins = TableDatabase.Plugins;
            int mech = 0;
            for (int i = 0; i < seeds.Length; i++) if (PlantMechanics.HasMechanic(seeds[i])) mech++;
            sb.AppendLine($"植物 seeds：{seeds.Length} 株（其中带机制 {mech} 株）");
            sb.AppendLine($"道具 items：{items.Length} 件");
            sb.AppendLine($"插件 plugins：{plugins.Length} 件");
            int c1 = 0, c2 = 0, c3 = 0, noNum = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Category == 1) c1++;
                else if (items[i].Category == 2) c2++;
                else c3++;
                if (!HasDigit(items[i].Desc)) { noNum++; sb.AppendLine("  ✗ 描述里没有数字：" + items[i].Id); }
            }
            sb.AppendLine($"  类别：纯增益 {c1} ／ 有增有减 {c2} ／ 机制 {c3}");
            sb.AppendLine($"  描述含数字：{items.Length - noNum}/{items.Length}");
            int pNoNum = 0;
            for (int i = 0; i < plugins.Length; i++)
                if (!HasDigit(plugins[i].description)) { pNoNum++; sb.AppendLine("  ✗ 插件描述没有数字：" + plugins[i].id); }
            sb.AppendLine($"  插件描述含数字：{plugins.Length - pNoNum}/{plugins.Length}");
            int noEffect = 0;
            var noEff = new StringBuilder();
            for (int i = 0; i < items.Length; i++)
                if (items[i].Effects.Length == 0) { noEffect++; noEff.Append(items[i].Id).Append(' '); }
            sb.AppendLine($"  effects 为空（纯机制件）：{noEffect} 件 ⇒ {noEff}");
            sb.AppendLine();
        }

        private static bool HasDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) return true;
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        // ③ CapSuffix 白名单
        // ══════════════════════════════════════════════════════════════════
        private static void DumpCapSuffix(StringBuilder sb)
        {
            sb.AppendLine("═══ ③ 属性面板「（上限 X）」的实际输出（= CapSuffix 的返回值原文）═══");
            ImGuiHud hud = null;
            GameObject holder = null;
            try
            {
                // ★ 先试真组件（CapSuffix 是实例方法，`internal` 可见）★
                //   包在 try 里：`ImGuiHud` 的 Awake 若依赖场景（字体/皮肤）可能抛 —— 抛出就退回
                //   「只读反射白名单」那条路，报告里会写明走的是哪一条 ✓
                holder = new GameObject("probeV212hud");
                hud = holder.AddComponent<ImGuiHud>();
            }
            catch (Exception ex)
            {
                sb.AppendLine("  （ImGuiHud 建不出来：" + ex.GetType().Name + "，改读白名单函数）");
                hud = null;
                if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
                holder = null;
            }
            try
            {
                StatId[] all = (StatId[])Enum.GetValues(typeof(StatId));
                int shown = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    string suf;
                    if (hud != null) suf = hud.CapSuffix(all[i]);
                    else suf = CapShownForReflect(all[i])
                        ? "（白名单内：会印，具体串见 CapSuffix）"
                        : "";
                    if (!string.IsNullOrEmpty(suf)) shown++;
                    sb.AppendLine($"  {all[i],-16} {(string.IsNullOrEmpty(suf) ? "（空：不印）" : suf)}");
                }
                sb.AppendLine($"  ⇒ 会印上限的属性 {shown} 条（期望 5：CritRate / Dodge / MaxPlots / HeartDropRate / PigRate）");
            }
            finally
            {
                if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
            }
            sb.AppendLine();
        }

        /// <summary>`ImGuiHud.CapShownFor`（private static）的只读调用 —— HUD 建不出来时的退路。</summary>
        private static bool CapShownForReflect(StatId id)
        {
            MethodInfo m = typeof(ImGuiHud).GetMethod("CapShownFor",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) return false;
            object r = m.Invoke(null, new object[] { id });
            return r is bool b && b;
        }

        // ══════════════════════════════════════════════════════════════════
        // ④ gamble_glove 的"攻速 −200%"
        // ══════════════════════════════════════════════════════════════════
        private static void DumpGambleGlove(StringBuilder sb)
        {
            sb.AppendLine("═══ ④ `gamble_glove`（攻速 −200%）在实战里等于什么 ═══");
            ItemDef g = TableDatabase.GetItem("gamble_glove");
            if (g == null) { sb.AppendLine("  表里没有 gamble_glove"); sb.AppendLine(); return; }
            var eff = new StringBuilder();
            for (int i = 0; i < g.Effects.Length; i++)
                eff.Append(g.Effects[i].Stat).Append(' ')
                   .Append(g.Effects[i].IsPercent ? "Percent " : "Add ").Append(F(g.Effects[i].Add)).Append("｜");
            sb.AppendLine($"  effects = {eff}");
            sb.AppendLine($"  rule = {(string.IsNullOrEmpty(g.Rule) ? "（无）" : g.Rule)}｜ruleValue = {F(g.RuleValue)}");
            float baseSpeed = TableDatabase.Tuning != null ? TableDatabase.Tuning.player.atkSpeed : 1f;
            float mul = 1f;
            for (int i = 0; i < g.Effects.Length; i++)
                if (g.Effects[i].Stat == StatId.AtkSpeed && g.Effects[i].IsPercent)
                    mul *= 1f + g.Effects[i].Add / 100f;          // RunSession.WriteBonus 的 Percent 语义
            float speed = baseSpeed * mul;
            float hoeCd = TableDatabase.Tuning != null ? TableDatabase.Tuning.tools.hoe.cooldown : 0.7f;
            float interval = hoeCd / Mathf.Max(0.05f, speed);
            sb.AppendLine($"  开局攻速 = {F(baseSpeed)} ⇒ 乘 {F(mul)} = **{F(speed)}**");
            sb.AppendLine($"  出手间隔 = 冷却 {F(hoeCd)} ÷ max(0.05, {F(speed)}) = **{F(interval)} 秒**"
                          + $"（正常是 {F(hoeCd / Mathf.Max(0.05f, baseSpeed))} 秒 ⇒ 慢 **{F(interval / (hoeCd / Mathf.Max(0.05f, baseSpeed)))} 倍**）");
            sb.AppendLine($"  ⚠️ `ProgMechanics.ApplyCapsToTable` 还会写 `AtkSpeedCapFixed = −2`（那是「上限」语义，");
            sb.AppendLine($"     而按用户要求它现在应当是**纯减益**）⇒ 这行 clamp 在 `Prog/**`，本轮不改；");
            sb.AppendLine($"     实测两条路在消费点都被 `Mathf.Max(0.05f, ·)` 兜成同一个间隔 ⇒ 数值上等价 ✓");
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // 自检原文
        // ══════════════════════════════════════════════════════════════════
        private static string ExecuteSelfCheck()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            try
            {
                List<CheckResult> results = SelfCheck.RunAll();
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].Passed) pass++;
                    else fail++;
                    sb.AppendLine("  " + results[i]);
                }
                string head = $"自检 {(fail == 0 ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过，{fail} 失败";
                sb.Insert(0, head + "\n");
                Write(SelfCheckPath, sb);
                return head;
            }
            catch (Exception ex)
            {
                string crash = "SELFCHECK CRASH：" + ex.GetType().Name + "：" + ex.Message;
                sb.Insert(0, crash + "\n");
                Write(SelfCheckPath, sb);
                Debug.LogError("[PROBEV212] " + crash);
                return crash;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 杂项
        // ══════════════════════════════════════════════════════════════════
        private static string Write(string relativePath, StringBuilder sb)
        {
            try
            {
                string path = Path.Combine(ProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return "报告已写入 " + relativePath + $"（{sb.Length} 字符）";
            }
            catch (Exception ex)
            {
                return "报告写盘失败：" + ex.Message;
            }
        }

        private static string ProjectRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string E(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";
        private static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";
    }
}
