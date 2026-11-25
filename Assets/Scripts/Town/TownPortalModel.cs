using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v261：**大传送门 = 真正的 3D 模型**（程序化生成的网格，不是一堆方块拼的）★★
    ///
    /// ── 为什么推倒重来 ──
    ///   上一版是 30 多个 `BoxL` 方块拼的门楼：柱子是长方体、门楣是长方体、拱顶靠"再压一层方块"假装。
    ///   近看全是直角，且门前那几块**互相重叠的薄板**（台阶 7.2×1.2、月台 9.6×1.6、5 条 y=0.03 的光斑）
    ///   与广场地面几乎共面 ⇒ 实机上是**一条条闪面**（用户："传送门前的路面异常"）✗
    ///
    /// ── 这一版的做法 ──
    ///   ① 全部几何都是**生成的网格**（见下面 5 个 Mesh 构造器）：
    ///        门拱 = 真·拱带（内/外圆弧 + 前后端面封闭）  立柱 = 12 面锥台 + 柱础/柱头
    ///        台基 = **一块连续的三级圆台**（不再是互相压着的薄板）  拱心石 = 真·楔形块
    ///        隧道 = 拱形内壁沿 −Z 挤出（"门洞"有进深，不是一张片）
    ///   ② 门前**只留一层地面**（圆台台基），所有薄板（旧台阶/月台/光斑）**全部删除** ⇒ 闪面绝迹
    ///   ③ 特效走**真几何**：三条对数螺旋**管**（`Tube`）+ 门洞里的黑盘（`TownPortalFx` 照旧）
    ///   ④ 材质沿用 `TownColors`（Stone/StoneDark/SignGold/PortalGlow）⇒ 与主城同一套色
    ///
    /// ── 坐标 ──
    ///   局部原点 = `TownLayout.PortalPos`，+Z = 城内方向（`PortalYaw = 180`）。
    ///   门洞净宽 4.95 m（内半径 2.475）、净高到拱顶 ≈ 6.9 m（旧 `PORTAL_Field` 的尺寸，保持一致）。
    /// </summary>
    public static class TownPortalModel
    {
        // ── 尺寸（改这里就够了）──
        public const float InnerRadius = 2.43f;      // ★ v267：门洞内半径（净宽 4.86，与柱心 ±3.05、柱半径 0.62 自洽）
        public const float ArchThick = 1.24f;        // ★ v267：拱厚 = 柱直径 ⇒ 外半径 3.67 正好压在柱外沿上
        public const float ArchDepth = 1.24f;        // ★ v267：进深 = 柱直径 ⇒ 拱与柱前后齐平
        public const float ColumnX = 2.86f;          // 柱心到中线
        public const float ColumnR = 0.62f;          // 柱底半径
        public const float ColumnH = 7.10f;          // 柱高（柱头到拱起点的距离）
        public const float TunnelDepth = 3.40f;      // 门洞内壁进深
        public const float PlatformTop = 0.18f;      // 台基顶面高度

        // ────────────────────────────────────────────────────────────────
        // ① 网格构造器（全部返回**真尺寸**网格：1 单位 = 1 米）
        // ────────────────────────────────────────────────────────────────

        /// <summary>拱带：内半径 <paramref name="rIn"/> ~ 外半径 <paramref name="rOut"/> 的**实心**半环，
        /// 沿 z 从 <paramref name="z0"/> 拉到 <paramref name="z1"/>，两端与前后都封口。</summary>
        public static Mesh ArchBand(float rIn, float rOut, float z0, float z1, int seg, float a0deg, float a1deg)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            float a0 = a0deg * Mathf.Deg2Rad, a1 = a1deg * Mathf.Deg2Rad;
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)seg);
                float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                // 每个截面 4 个角：内/外 × 前/后
                v.Add(new Vector3(cx * rIn, cy * rIn, z0));
                v.Add(new Vector3(cx * rOut, cy * rOut, z0));
                v.Add(new Vector3(cx * rOut, cy * rOut, z1));
                v.Add(new Vector3(cx * rIn, cy * rIn, z1));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 4, b = (i + 1) * 4;
                Quad(t, a + 0, b + 0, b + 1, a + 1);   // 前端面（朝 +z）
                Quad(t, a + 3, b + 3, b + 2, a + 2);   // 后端面（朝 −z）
                Quad(t, a + 1, b + 1, b + 2, a + 2);   // 外弧面
                Quad(t, a + 0, a + 3, b + 3, b + 0);   // 内弧面
            }
            // 两端封口
            Quad(t, 0, 1, 2, 3);
            int e = seg * 4;
            Quad(t, e + 3, e + 2, e + 1, e + 0);
            return Finish("PortalArch", v, t);
        }

        /// <summary>
        /// ★ v268：**车削（lathe）** —— 把一条 (半径, 高度) 折线绕 Y 轴旋转成**一个**网格。
        /// 为什么新加它：柱础 / 柱身 / 柱头若用三个独立件叠，它们必然互相穿插，
        /// 而本工程的描边通道会把每条交线画成黑线 ✗
        /// 车削成**一个**网格后，内部没有"零件交线"，造型还更连贯 ✓
        /// </summary>
        public static Mesh Lathe(Vector2[] profile, int sides)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                float c = Mathf.Cos(a), s2 = Mathf.Sin(a);
                for (int p = 0; p < profile.Length; p++)
                    v.Add(new Vector3(c * profile[p].x, profile[p].y, s2 * profile[p].x));
            }
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                for (int p = 0; p < profile.Length - 1; p++)
                {
                    int a0 = i * profile.Length + p, b0 = n * profile.Length + p;
                    Quad(t, a0, b0, b0 + 1, a0 + 1);
                }
            }
            // 上下封口（折线首末点若半径 > 0，补一个风扇盖）
            int bottomC = v.Count; v.Add(new Vector3(0f, profile[0].y, 0f));
            int topC = v.Count; v.Add(new Vector3(0f, profile[profile.Length - 1].y, 0f));
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                t.Add(bottomC); t.Add(n * profile.Length); t.Add(i * profile.Length);
                t.Add(topC); t.Add(i * profile.Length + profile.Length - 1);
                t.Add(n * profile.Length + profile.Length - 1);
            }
            return Finish("PortalLathe", v, t);
        }

        /// <summary>锥台（立柱）：底半径 <paramref name="rBottom"/>、顶半径 <paramref name="rTop"/>、高 <paramref name="h"/>，
        /// 底面在 y=0。12 面即够（低多边形风格）。</summary>
        public static Mesh FacetColumn(float rBottom, float rTop, float h, int sides)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v.Add(new Vector3(c * rBottom, 0f, s * rBottom));
                v.Add(new Vector3(c * rTop, h, s * rTop));
            }
            for (int i = 0; i < sides; i++)
            {
                int n = (i + 1) % sides;
                Quad(t, i * 2, n * 2, n * 2 + 1, i * 2 + 1);          // 侧面
                t.Add(0); t.Add(i * 2); t.Add(n * 2);                  // 底盖（朝下）
                int topC = v.Count;
                v.Add(Vector3.zero + new Vector3(0f, h, 0f));
                t.Add(topC); t.Add(n * 2 + 1); t.Add(i * 2 + 1);        // 顶盖（朝上）
            }
            return Finish("PortalColumn", v, t);
        }

        /// <summary>
        /// 三级圆台台基 —— **一整块连续网格**。
        /// ★ 这是"路面异常"的根治点：旧版是 3 块互相压着的薄板（台阶/月台/光斑），
        ///   与广场地面共面 ⇒ 闪面。现在只有**这一块**，且每级侧面都有坡度，不会与地面重合 ✓
        /// </summary>
        public static Mesh SteppedPlatform(float[] radii, float[] heights, int seg)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            float y = 0f;
            for (int step = 0; step < radii.Length; step++)
            {
                int baseIdx = v.Count;
                // 上下两圈
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    v.Add(new Vector3(c * radii[step], y, s * radii[step]));
                    v.Add(new Vector3(c * radii[step], y + heights[step], s * radii[step]));
                }
                for (int i = 0; i < seg; i++)
                {
                    int n = (i + 1) % seg;
                    Quad(t, baseIdx + i * 2, baseIdx + n * 2, baseIdx + n * 2 + 1, baseIdx + i * 2 + 1);  // 侧面
                }
                y += heights[step];
            }
            // 顶面（最后一个半径的圆盘）
            int center = v.Count;
            v.Add(new Vector3(0f, y, 0f));
            int ring = v.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * radii[radii.Length - 1], y, Mathf.Sin(a) * radii[radii.Length - 1]));
            }
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                t.Add(center); t.Add(ring + i); t.Add(ring + n);
            }
            return Finish("PortalPlatform", v, t);
        }

        /// <summary>拱心石：上宽下窄的楔形块（真几何，不是方块）。</summary>
        public static Mesh Wedge(float wTop, float wBottom, float h, float depth)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            float hd = depth * 0.5f;
            // 前(z=+hd) 4 点：下左、下右、上右、上左
            v.Add(new Vector3(-wBottom * 0.5f, 0f, hd));
            v.Add(new Vector3(wBottom * 0.5f, 0f, hd));
            v.Add(new Vector3(wTop * 0.5f, h, hd));
            v.Add(new Vector3(-wTop * 0.5f, h, hd));
            // 后(z=−hd)
            v.Add(new Vector3(-wBottom * 0.5f, 0f, -hd));
            v.Add(new Vector3(wBottom * 0.5f, 0f, -hd));
            v.Add(new Vector3(wTop * 0.5f, h, -hd));
            v.Add(new Vector3(-wTop * 0.5f, h, -hd));
            Quad(t, 0, 1, 2, 3);          // 前
            Quad(t, 7, 6, 5, 4);          // 后
            Quad(t, 0, 4, 5, 1);          // 底
            Quad(t, 3, 2, 6, 7);          // 顶
            Quad(t, 0, 3, 7, 4);          // 左
            Quad(t, 1, 5, 6, 2);          // 右
            return Finish("PortalKeystone", v, t);
        }

        /// <summary>沿路径的圆管（旋臂 / 光丝用）。<paramref name="pts"/> 至少 2 点。</summary>
        public static Mesh Tube(IList<Vector3> pts, float radius, int sides)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                Vector3 dir = (i == pts.Count - 1 ? p - pts[i - 1] : pts[i + 1] - p).normalized;
                Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.forward)) > 0.95f ? Vector3.up : Vector3.forward;
                Vector3 right = Vector3.Cross(dir, up).normalized;
                up = Vector3.Cross(right, dir).normalized;
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    v.Add(p + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
                }
            }
            for (int i = 0; i < pts.Count - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int n = (k + 1) % sides;
                    Quad(t, i * sides + k, (i + 1) * sides + k, (i + 1) * sides + n, i * sides + n);
                }
            return Finish("PortalTube", v, t);
        }

        private static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        private static Mesh Finish(string name, List<Vector3> v, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
