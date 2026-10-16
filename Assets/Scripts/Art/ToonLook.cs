using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 模型 → 卡通描边材质的转换口。
    ///
    /// 为什么需要它：FBX 导入时 Unity 会给模型挂上它自己生成的 URP/Lit 材质，
    /// 那样渲染出来是"塑料感"而不是参考美术的漫画感。这里做两件事：
    ///   ① 按原材质的底色生成 Seedfall/Toon 材质（颜色沿用 Blender 侧写的调色板）
    ///   ② 描边粗细按模型尺寸自动换算——固定毫米数在 0.7m 的小恶魔上糊成一坨、在 1.55m 的大恶魔上又看不见
    /// 转换是幂等的，可以重复调用。
    /// </summary>
    public static class ToonLook
    {
        public const string ShaderName = "Seedfall/Toon";
        /// <summary>装饰专用的轻量着色器（只有本体 Pass）。</summary>
        public const string SimpleShaderName = "Seedfall/ToonSimple";

        private static Shader _shader;
        private static readonly Dictionary<int, Material> Cache = new Dictionary<int, Material>();

        public static Shader ToonShader
        {
            get
            {
                if (_shader == null) _shader = Shader.Find(ShaderName);
                return _shader;
            }
        }

        /// <summary>
        /// 把一个物体（含子物体）的所有材质槽换成卡通材质，返回替换的槽数。
        /// simple=true 时用 Seedfall/ToonSimple（只有本体 Pass，无墨线无霓虹）——**给场景装饰用**。
        ///
        /// 为什么装饰要走简化版：它们常驻 400+ 个，每个都跑三个 Pass 会把 drawCall 推到 ~2000；
        /// 而描边对装饰本来就没意义——每根草勾一圈黑边，近看是一团黑线。
        /// 这是**性能与观感同时受益**，不是取舍。
        /// </summary>
        public static int Apply(GameObject root, bool simple = false)
        {
            if (root == null || ToonShader == null) return 0;
            int replaced = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                // 描边粗细：取该渲染器最大边长的 1.6%，并夹在 4mm ~ 5cm
                float maxDim = Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z));
                float outline = Mathf.Clamp(maxDim * 0.016f, 0.004f, 0.05f);

                for (int m = 0; m < mats.Length; m++)
                {
                    mats[m] = Get(SourceColor(mats[m]), outline, simple);
                    replaced++;
                }
                r.sharedMaterials = mats;
            }
            return replaced;
        }

        private static Color SourceColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        private static Material Get(Color c, float outlineWidth, bool simple = false)
        {
            int rgb = (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16)
                    | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8)
                    | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
            int widthKey = Mathf.RoundToInt(outlineWidth * 10000f);
            // 简化版与完整版必须落在不同的缓存键上，否则装饰会拿到角色的材质（或反过来）
            int key = ((rgb * 397) ^ widthKey) * (simple ? 2 : 1);

            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Shader sh = ToonShader;
            if (simple)
            {
                if (_simpleShader == null) _simpleShader = Shader.Find(SimpleShaderName);
                if (_simpleShader != null) sh = _simpleShader;
            }

            Material mat = new Material(sh)
            {
                name = simple ? $"M_ToonS_{rgb:X6}" : $"M_Toon_{rgb:X6}_{outlineWidth * 1000f:0.#}mm"
            };
            mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            mat.SetColor("_ShadeColor", Shade(c));
            mat.SetColor("_RimColor", new Color(1f, 0.98f, 0.92f, 0.26f));
            mat.SetFloat("_ShadeThreshold", 0.44f);
            mat.SetFloat("_ShadeSoft", 0.035f);
            mat.SetFloat("_RimPower", 3.4f);
            if (!simple)
            {
                // 只有完整版才有这两个属性
                mat.SetColor("_OutlineColor", new Color(0.15f, 0.12f, 0.10f, 1f));
                mat.SetFloat("_OutlineWidth", outlineWidth);
                mat.SetFloat("_OutlineDepthFade", 26f);
            }

            Cache[key] = mat;
            return mat;
        }

        private static Shader _simpleShader;

        /// <summary>
        /// 暗部颜色：往冷紫方向压一点再降明度。
        /// 纯按比例变暗会显得脏；参考美术的阴影都带一点冷色，这是"通透"的来源。
        /// </summary>
        private static Color Shade(Color c)
        {
            Color cold = new Color(0.44f, 0.42f, 0.56f, 1f);
            Color s = Color.Lerp(c, cold, 0.40f) * 0.88f;
            s.a = 1f;
            return s;
        }

        /// <summary>把场景里所有模型（含已生成的预览）都刷一遍，供美术批量验收。</summary>
        public static int ApplyAll()
        {
            int n = 0;
            Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject.name.StartsWith("PREVIEW_")) continue;   // 预览已单独刷过
                Material[] mats = all[i].sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                float maxDim = Mathf.Max(all[i].bounds.size.x,
                                Mathf.Max(all[i].bounds.size.y, all[i].bounds.size.z));
                float outline = Mathf.Clamp(maxDim * 0.016f, 0.004f, 0.05f);
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].shader == ToonShader) continue;
                    mats[m] = Get(SourceColor(mats[m]), outline);
                    changed = true;
                }
                if (changed) { all[i].sharedMaterials = mats; n++; }
            }
            return n;
        }
    }
}
