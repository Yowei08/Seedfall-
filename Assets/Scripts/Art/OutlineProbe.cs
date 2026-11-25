#if UNITY_EDITOR
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v222：**物体描边的运行期实验台**（只在编辑器里存在，由 `OutlineAudit` 自动生成）★
    ///
    /// ── 为什么必须有它 ──
    /// 用户报"进入战斗后，敌人和玩家的描边又没了"。查到的事实是：
    ///   玩家/敌人的材质 `M_Toon_xxxx_11mm` 上 `_OutlineWidth = 0.011 m`、`_OutlinePixels = 2.20 px`
    ///   —— **不是 0**（`ToonLook.Get` 是全工程唯一写这两个属性的地方，已全库核对）。
    ///   但 1600×900 的 1:1 截图里，玩家身上**一条线都没有**；而同一张图上
    ///   树/石头（`Seedfall/ToonSimple`）**有明显的黑边**。
    /// ⇒ 问题不在数值，在"这个 Pass 到底跑没跑"。
    /// 而这个区别**只能靠实验**：把宽度推到极端，看画面变不变 ——
    ///   变了 ⇒ Pass 在跑，只是数值太小；
    ///   不变 ⇒ Pass 从来没被画过（那就得改 shader 的 Pass 结构）。
    ///
    /// ── 为什么做成 MonoBehaviour 而不做成静态类 ──
    /// 桥的 `unity_drive send` 只能调**某个 GameObject 上的公开方法**。
    /// 把它挂在一个固定名字 `OUTLINE_PROBE` 的对象上，就能一条命令往返一次实验（不用重编译）✓
    ///
    /// 用法（play 中）：
    ///   unity_drive send OUTLINE_PROBE OutlineProbe Info
    ///   unity_drive send OUTLINE_PROBE OutlineProbe Width 0.25
    ///   unity_drive send OUTLINE_PROBE OutlineProbe Pixels 4
    ///   unity_drive send OUTLINE_PROBE OutlineProbe Both 0.25:4
    /// </summary>
    public class OutlineProbe : MonoBehaviour
    {
        public const string ProbeName = "OUTLINE_PROBE";
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int PixelsId = Shader.PropertyToID("_OutlinePixels");

        /// <summary>把**所有** `Seedfall/Toon` 材质的描边宽度设成 <paramref name="w"/> 米，返回改了哪几份。</summary>
        public string Width(float w) => Apply(w, float.NaN);

        /// <summary>把**所有** `Seedfall/Toon` 材质的像素下限设成 <paramref name="px"/>。</summary>
        public string Pixels(float px) => Apply(float.NaN, px);

        /// <summary>"宽:像素"，例如 `0.25:4`。留空表示不改那一项。</summary>
        public string Both(string csv)
        {
            float w = float.NaN, px = float.NaN;
            if (!string.IsNullOrEmpty(csv))
            {
                string[] p = csv.Split(':');
                if (p.Length > 0 && !string.IsNullOrEmpty(p[0])) float.TryParse(p[0], out w);
                if (p.Length > 1 && !string.IsNullOrEmpty(p[1])) float.TryParse(p[1], out px);
            }
            return Apply(w, px);
        }

        private static string Apply(float w, float px)
        {
            Material[] all = Resources.FindObjectsOfTypeAll<Material>();
            var sb = new System.Text.StringBuilder();
            int n = 0;
            sb.Append("[OUTLINE_PROBE] 改 ").Append("Seedfall/Toon").Append(" 材质：");
            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i];
                if (m == null || m.shader == null || m.shader.name != "Seedfall/Toon") continue;
                if (!float.IsNaN(w)) m.SetFloat(WidthId, w);
                if (!float.IsNaN(px)) m.SetFloat(PixelsId, px);
                n++;
                if (n <= 6) sb.Append(m.name).Append(' ');
            }
            sb.Append("（共 ").Append(n).Append(" 份）");
            if (!float.IsNaN(w)) sb.Append(" ｜_OutlineWidth=").Append(w.ToString("0.000")).Append(" m");
            if (!float.IsNaN(px)) sb.Append(" ｜_OutlinePixels=").Append(px.ToString("0.00")).Append(" px");
            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>★ 交付①的那份读数：玩家 / 敌人各自用的是哪份材质、描边三件套是多少 ★</summary>
        public string Info()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[OUTLINE_PROBE] ═══ 玩家/敌人 的物体描边读数 ═══\n");
            Report(sb, "玩家 MDL_farmer", "MDL_farmer");
            Report(sb, "小猪 MDL_pig", "MDL_pig");
            Report(sb, "敌人 MDL_imp", "MDL_imp");
            Report(sb, "敌人 MDL_brute", "MDL_brute");
            Report(sb, "敌人 BUG_", "BUG_");
            Report(sb, "作物 MDL_（其它）", "MDL_");
            sb.Append("   ── 运行时 Shader 的 Pass / LightMode（确认编辑器里的改动真的生效）──\n");
            AppendPassTags(sb, "Seedfall/Toon");
            AppendPassTags(sb, "Seedfall/ToonSimple");
            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>把一个 shader 的每个 Pass 名字与它当前的 `LightMode` 打出来。</summary>
        private static void AppendPassTags(System.Text.StringBuilder sb, string shaderName)
        {
            Shader sh = Shader.Find(shaderName);
            if (sh == null) { sb.Append("     ").Append(shaderName).Append("：**找不到**\n"); return; }
            var tag = new UnityEngine.Rendering.ShaderTagId("LightMode");
            sb.Append("     ").Append(shaderName).Append("（passCount=").Append(sh.passCount).Append("）：");
            for (int i = 0; i < sh.passCount; i++)
            {
                UnityEngine.Rendering.ShaderTagId v = sh.FindPassTagValue(i, tag);
                // Pass 名字没有**运行期** API（`ShaderUtil` 这版也没有 `GetPassName`），
                // 所以这里只报序号与 LightMode —— 要判断的东西恰好就是 LightMode ✓
                sb.Append('[').Append(i).Append("]LightMode=").Append(v.name).Append("  ");
            }
            sb.Append('\n');
        }

        /// <summary>
        /// ★ 屏幕描边的**运行期 A/B**（只影响本次 play，不写盘）★
        /// `on` = 强度 1（正常），`off` = 强度 0（等价于关掉，用于对照"这一道边是不是它"）。
        /// ★ v222 起 `disableInScenes` 已删除（战斗不再按场景关屏幕描边），
        ///   所以这里改成直接推 `strength` —— 不改渲染器资产，关掉 play 就复原 ✓
        /// </summary>
        public string Screen(string mode)
        {
            bool on = string.IsNullOrEmpty(mode) || mode.Trim().ToLowerInvariant() != "off";
            ScreenOutlineFeature[] feats = Resources.FindObjectsOfTypeAll<ScreenOutlineFeature>();
            var sb = new System.Text.StringBuilder();
            sb.Append("[OUTLINE_PROBE] 屏幕描边 → **").Append(on ? "开（strength=1）" : "关（strength=0，仅本次 play）").Append("**");
            int n = 0;
            for (int i = 0; i < feats.Length; i++)
            {
                if (feats[i] == null) continue;
                feats[i].strength = on ? 1f : 0f;
                n++;
            }
            sb.Append("（改了 ").Append(n).Append(" 个 Feature 实例）");
            Log.Info(sb.ToString());
            return sb.ToString();
        }

        /// <summary>
        /// ★ v223：屏幕描边的**法线阈值**运行期 A/B（只影响本次 play）★
        /// 判据：草地/木纹上的假边消失、人和敌人仍有一道清晰的边。
        /// 改的是 **Feature 的字段**而不是材质的 float —— `AddRenderPasses` 每帧都会
        /// 把字段写进材质，直接改材质会在下一帧被覆盖掉 ✓
        /// </summary>
        public string Normal(float v)
        {
            ScreenOutlineFeature[] feats = Resources.FindObjectsOfTypeAll<ScreenOutlineFeature>();
            var sb = new System.Text.StringBuilder();
            sb.Append("[OUTLINE_PROBE] 法线阈值 → **").Append(v.ToString("0.00")).Append("**");
            int n = 0;
            for (int i = 0; i < feats.Length; i++)
            {
                if (feats[i] == null) continue;
                feats[i].normalThreshold = v;
                n++;
            }
            sb.Append("（改了 ").Append(n).Append(" 个 Feature 实例；下一帧生效）");
            Log.Info(sb.ToString());
            return sb.ToString();
        }

        private static void Report(System.Text.StringBuilder sb, string label, string prefix)
        {
            Renderer[] all = Object.FindObjectsByType<Renderer>();
            int count = 0;
            string matName = "—", shaderName = "—";
            float w = -1f, px = -1f;
            Color col = Color.white;
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                if (!OwnerStartsWith(r.transform, prefix)) continue;
                Material m = r.sharedMaterial;
                if (m == null) continue;
                count++;
                if (matName != "—") continue;
                matName = m.name;
                shaderName = m.shader != null ? m.shader.name : "null";
                if (m.HasProperty("_OutlineWidth")) w = m.GetFloat("_OutlineWidth");
                if (m.HasProperty("_OutlinePixels")) px = m.GetFloat("_OutlinePixels");
                if (m.HasProperty("_OutlineColor")) col = m.GetColor("_OutlineColor");
            }
            if (count == 0) { sb.Append("   ").Append(label).Append("：**场上没有**\n"); return; }
            sb.Append("   ").Append(label).Append("：渲染器 ").Append(count)
              .Append(" 个 ｜样本材质 ").Append(matName)
              .Append("（").Append(shaderName).Append("）")
              .Append(" ｜_OutlineWidth=").Append(w.ToString("0.000")).Append(" m（")
              .Append((w * 100f).ToString("0.0")).Append(" 厘米）")
              .Append(" ｜_OutlinePixels=").Append(px.ToString("0.00")).Append(" px")
              .Append(" ｜_OutlineColor=RGB(").Append(col.r.ToString("0.00")).Append(',')
              .Append(col.g.ToString("0.00")).Append(',').Append(col.b.ToString("0.00")).Append(")\n");
        }

        /// <summary>往上找 8 层，看名字是否以 <paramref name="prefix"/> 开头。</summary>
        private static bool OwnerStartsWith(Transform t, string prefix)
        {
            Transform c = t;
            for (int d = 0; d < 8 && c != null; d++)
            {
                if (c.name.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
                c = c.parent;
            }
            return false;
        }
    }
}
#endif
