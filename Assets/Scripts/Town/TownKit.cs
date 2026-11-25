using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城的"积木"（程序化几何 + 共享材质 + 复用现有装饰资源）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、共享材质是**硬要求**，不是优化
    /// ══════════════════════════════════════════════════════════════════════
    /// 主城有 36 块耕地 + 十几栋房子 + 8 根路灯 + 一整个公园，如果有任何一处
    /// `new Material(...)`，主城就会比战斗场景多出几百份材质 ⇒ 合批全废。
    /// 唯一入口是 <see cref="ToonLook.Get"/>（那是**带缓存的材质池**，键 = 底色 + 描边 + simple）：
    ///   · 36 块耕地读的是**同一个** `Material` 对象（同一个 `Dirt` 常量）✓
    ///   · 8 种作物只有 8 份材质（色号来自 `town_seeds.json` 的 `color`）✓
    /// 验收方式：进城前后打印 `ToonLook.MaterialPoolCount`，**只允许按"用到的颜色数"增长**，
    /// 不允许按"物体数"增长（`TownBootstrap` 的自动化入口 `TownStatus()` 会把这两个数都报出来）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、网格也共享
    /// ══════════════════════════════════════════════════════════════════════
    /// 与 `PlayerBean.PrimMesh` 同一手法：建一个临时图元 → 抄走 `sharedMesh` → 立刻销毁 GameObject。
    /// 内建网格在默认资源里，销毁实例不会带走它 ⇒ 全场 300 个方块共用**一个** Cube 网格 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、碰撞体只加在**建筑本体**上
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户要求「玩家在主城加入射线检测碰撞，禁止穿透主城所有建筑」＋
    /// 「碰撞体要贴合模型，不许一个大方块糊过去」。
    /// ⇒ 本类的每个造物体方法都有一个 `solid` 开关，**只有建筑零件**传 true：
    ///    · 方块零件 → `BoxCollider`（尺寸 = 世界尺寸，逐面贴合）✓
    ///    · 球零件   → `SphereCollider`（等比缩放 ⇒ 精确）✓
    ///    · 圆柱/锥/圆顶零件 → **真尺寸网格 + `MeshCollider(convex = true)`**（逐顶点贴合）✓
    /// 而 `ground / 道路 / 田圃 / 作物 / 装饰模型` 一律**没有** Collider ⇒ 玩家照常走上去 ✓
    /// 交互判定仍然是「玩家到锚点的距离」（见 `TownBootstrap`），碰撞只负责"别穿墙"，两者不混用。
    /// `GameObject.CreatePrimitive` 会自带一个 Collider ⇒ 这里只借它的网格再销毁临时物体，不继承那个 Collider。
    /// </summary>
    public static class TownKit
    {
        // ── 共享网格缓存（全工程一份）──
        private static readonly Dictionary<PrimitiveType, Mesh> PrimCache = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>Decoration 资源缺失时只报一次，不要每个物体刷一行。</summary>
        private static readonly HashSet<string> ReportedMissing = new HashSet<string>();

        /// <summary>取内建图元的共享网格（见类注释第二节）。</summary>
        public static Mesh Prim(PrimitiveType type)
        {
            if (PrimCache.TryGetValue(type, out Mesh cached) && cached != null) return cached;
            GameObject tmp = GameObject.CreatePrimitive(type);
            MeshFilter mf = tmp.GetComponent<MeshFilter>();
            Mesh m = mf != null ? mf.sharedMesh : null;
            if (Application.isPlaying) Object.Destroy(tmp); else Object.DestroyImmediate(tmp);
            if (m != null) PrimCache[type] = m;
            return m;
        }

        /// <summary>
        /// 取一份**共享**材质。`simple = true` 走 `Seedfall/ToonSimple`（只有本体 Pass、无墨迹无霓虹）——
        /// `ToonLook` 的注释里已经写明这是**给场景装饰用**的那一档（主城几百个积木全在装饰范畴）。
        /// </summary>
        public static Material Mat(Color c, bool simple = true, float outline = 0f)
            => ToonLook.Get(c, outline, simple);

        // ══════════════════════════════════════════════════════════════════
        // 造物体
        // ══════════════════════════════════════════════════════════════════

        /// <summary>一个盒子（方块）。<paramref name="center"/> 是世界坐标，<paramref name="size"/> 是**世界尺寸**。</summary>
        public static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            GameObject go = MeshObject(parent, name, Prim(PrimitiveType.Cube), center);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>圆柱。<paramref name="radius"/> 世界米、<paramref name="height"/> 世界米（Unity 圆柱原始高 2 ⇒ 缩放 y 取半）。</summary>
        public static GameObject Cyl(Transform parent, string name, Vector3 center, float radius, float height, Material mat)
        {
            GameObject go = MeshObject(parent, name, Prim(PrimitiveType.Cylinder), center);
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>球（`size` 是直径的三轴分量）。</summary>
        public static GameObject Ball(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            GameObject go = MeshObject(parent, name, Prim(PrimitiveType.Sphere), center);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>把一个已有 GameObject 拉到世界坐标（不改缩放）。</summary>
        public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Vector3 center)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }

        // ══════════════════════════════════════════════════════════════════
        // 复用现有装饰资源（Resources/Art/Environment/*）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 从 `Resources/Art/Environment/` 里取一个已有装饰模型并实例化（要求 2「复用现有装饰资源」）。
        /// 找不到时**不崩**：返回 null 并只记一条警告 —— 主城的玩法不依赖这些模型。
        /// </summary>
        public static GameObject Env(Transform parent, string envId, Vector3 pos, float yawDeg, float scale = 1f)
        {
            GameObject prefab = Resources.Load<GameObject>("Art/Environment/" + envId);
            if (prefab == null)
            {
                if (ReportedMissing.Add(envId))
                    Log.Warn($"主城：装饰资源 Art/Environment/{envId} 缺失（该位置改为程序化积木）");
                return null;
            }
            GameObject go = Object.Instantiate(prefab, parent);
            go.name = "ENV_" + envId;
            go.transform.position = pos;
            // ★ 上轴修正要**乘**上去，不能取代 ★
            //   `Art/Environment/*.fbx` 是 Blender 导出的 Z-up 资产，**根节点自带修正旋转**
            //   （`Rx(270)` / `Rx(180)`）。旧写法只写 yaw ⇒ 把那条修正整个盖掉 ⇒ 树干水平、木箱侧躺
            //   （= "模型倒下"，`TownArtDresser` 的 ①.5 节记的就是这一条）。
            //   ⚠️ 不能只靠 `TownArtDresser.FixUpright` 事后兜底：`TownLife.TickSway`
            //      每帧回写树/灌木的 rotation，兜底会被下一帧抹掉 ⇒ 必须在**摆的时候**就对。
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f) * prefab.transform.rotation;
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;

            // ★ 换材质：**不走 `ToonLook.Apply`**，而是自己按"底色 + 固定描边 0"取共享材质 ★
            //   为什么：`ToonLook.Apply` 会按**每个渲染器的包围盒**算描边粗细，
            //   于是同一种颜色只要尺寸不同就是**不同的缓存键** ⇒ 每件装饰各得一份材质。
            //   实测代价：主城装饰只用到十几个颜色，材质池却被推到 48 份（=按物体数增长）。
            //   而 `Seedfall/ToonSimple` **根本没有描边 Pass** ⇒ 描边参数对 simple 版毫无意义，
            //   取 0 即可 ⇒ 材质键退化成"只有底色"，装饰共用材质池 ✓
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Material[] mats = rs[i].sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                for (int m = 0; m < mats.Length; m++)
                    mats[m] = ToonLook.Get(BaseColorOf(mats[m]), 0f, true);
                rs[i].sharedMaterials = mats;
            }
            return go;
        }

        /// <summary>读一份材质的底色（`_BaseColor` 优先，退回 `_Color`），与 `ToonLook.SourceColor` 同口径。</summary>
        private static Color BaseColorOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        /// <summary>
        /// ★ 装饰资源的**定尺寸实例化** —— 主城复用 `Resources/Art/Environment/*` 的入口 ★
        ///
        /// 为什么不能直接 `scale = 1`：那批 FBX 是 Blender 导出的，单位/原点/父级缩放
        /// 各不相同（`tree` 可能是 6 米、`flower_a` 可能是 40 厘米）。
        /// 直接摆会把公园变成"巨树压平一切"或"花是看不见的一个点" ✗
        /// ⇒ 实例化后**量一次包围盒**，再按 `targetHeight` 等比缩 —— 参数是"这棵东西应该多高（米）"，
        ///   与 FBX 内部单位无关 ⇒ 换资源、换导出口径都不用改这里的数字 ✓
        /// 缩放后把**底部对齐到 pos.y**：资源原点不一定在脚下，不对齐会出现"树浮在空中"。
        /// </summary>
        public static GameObject EnvFit(Transform parent, string envId, Vector3 pos, float yawDeg,
                                        float targetHeight, float targetRadius = 0f)
        {
            GameObject go = Env(parent, envId, pos, yawDeg);
            if (go == null) return null;

            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return go;

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

            float k = 1f;
            if (targetHeight > 0f && b.size.y > 1e-4f) k = targetHeight / b.size.y;
            if (targetRadius > 0f)
            {
                float r = Mathf.Max(b.size.x, b.size.z) * 0.5f;
                if (r > 1e-4f) k = Mathf.Min(k, targetRadius / r);
            }
            if (!Mathf.Approximately(k, 1f)) go.transform.localScale *= k;

            // 底部对齐（重新量一次：缩放后 bounds 变了）
            var rs2 = go.GetComponentsInChildren<Renderer>(true);
            Bounds b2 = rs2[0].bounds;
            for (int i = 1; i < rs2.Length; i++) b2.Encapsulate(rs2[i].bounds);
            Vector3 p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y - b2.min.y, p.z);
            return go;
        }

        /// <summary>
        /// 在物体下挂一盏真实的点光（路灯、传送门、商店灯）。
        /// `range` 给足、`intensity` 克制 —— 主城白天为主，光只用于"点出氛围"。
        /// </summary>
        public static Light Glow(Transform parent, string name, Vector3 pos, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        // ══════════════════════════════════════════════════════════════════
        // 四、圆形布局要用的几何（★ 真尺寸网格，缓存共享 ★）
        // ══════════════════════════════════════════════════════════════════
        //
        // 为什么新做一批网格，而不是继续用内建图元 + `localScale`：
        //   ① **环形道路 / 圆形广场 / 圆顶** 不是内建图元能表达的（Cube / Cylinder / Sphere 三个而已）✓
        //   ② **碰撞体贴合模型**是这一轮的硬要求。内建图元靠 `localScale` 变形后：
        //      圆柱体只能退化成 `CapsuleCollider`（两头是球面 ⇒ 四角会漏）或非均匀缩放的
        //      `MeshCollider`（缩放烘焙结果随版本变）；而**真尺寸网格 + `MeshCollider(convex)`**
        //      是逐顶点贴合的 ⇒ 圆顶是圆的、锥顶是尖的，不是一个方块糊过去 ✓
        //   ③ 全部**按 (形状, 尺寸) 缓存**：8 根路灯柱共用一份圆柱网格、36 块地共用一份方块网格。
        //      `ShapeCacheCount` 会出现在 `TownStatus()` 里，验收口径与材质池同一条：
        //      **只允许按"用到的尺寸数"增长，不允许按"物体数"增长** ✓

        private static readonly Dictionary<string, Mesh> ShapeCache = new Dictionary<string, Mesh>();

        /// <summary>已生成的形状网格数（验收用：应远小于物体数）。</summary>
        public static int ShapeCacheCount => ShapeCache.Count;

        /// <summary>
        /// 圆柱（**真尺寸**：半径 <paramref name="radius"/> 米、高 <paramref name="height"/> 米），
        /// 原点在几何中心，轴向 +Y。侧面 + 上下两个盖。
        /// </summary>
        public static Mesh Cylinder(float radius, float height, int seg = 16)
        {
            string key = "cyl|" + radius.ToString("0.000") + "|" + height.ToString("0.000") + "|" + seg;
            if (ShapeCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float half = height * 0.5f;

            // 侧面：θ 增大 ⇒ 从外面看是顺时针（见 `Disc` 注释里的推导）
            for (int i = 0; i < seg; i++)
            {
                float a0 = (i / (float)seg) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)seg) * Mathf.PI * 2f;
                var b0 = new Vector3(Mathf.Cos(a0) * radius, -half, Mathf.Sin(a0) * radius);
                var b1 = new Vector3(Mathf.Cos(a1) * radius, -half, Mathf.Sin(a1) * radius);
                var t0 = new Vector3(b0.x, half, b0.z);
                var t1 = new Vector3(b1.x, half, b1.z);
                int at = verts.Count;
                verts.Add(b0); verts.Add(b1); verts.Add(t1); verts.Add(t0);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
                tris.Add(at); tris.Add(at + 2); tris.Add(at + 3);
            }

            // 上盖（从上往下看顺时针）+ 下盖（从下往上看顺时针）
            AddCap(verts, uvs, tris, radius, half, seg, true);
            AddCap(verts, uvs, tris, radius, -half, seg, false);

            Mesh m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            ShapeCache[key] = m;
            return m;
        }

        /// <summary>
        /// 圆锥（**真尺寸**）：底面圆半径 <paramref name="radius"/>、总高 <paramref name="height"/>，
        /// 底面在 y = −height/2、尖在 y = +height/2。`seg = 4` 就是一个四棱锥帐篷顶。
        /// </summary>
        public static Mesh Cone(float radius, float height, int seg = 16)
        {
            string key = "cone|" + radius.ToString("0.000") + "|" + height.ToString("0.000") + "|" + seg;
            if (ShapeCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float half = height * 0.5f;
            var apex = new Vector3(0f, half, 0f);
            int apexIdx = -1;

            for (int i = 0; i < seg; i++)
            {
                float a0 = (i / (float)seg) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)seg) * Mathf.PI * 2f;
                var b0 = new Vector3(Mathf.Cos(a0) * radius, -half, Mathf.Sin(a0) * radius);
                var b1 = new Vector3(Mathf.Cos(a1) * radius, -half, Mathf.Sin(a1) * radius);
                // 每个三角单独立顶点 ⇒ 侧面保持硬边（低多边形的"棱"要看得见）
                if (apexIdx < 0) { apexIdx = verts.Count; verts.Add(apex); uvs.Add(new Vector2(0.5f, 1f)); }
                int at = verts.Count;
                verts.Add(b0); verts.Add(b1);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                tris.Add(at); tris.Add(at + 1); tris.Add(apexIdx);
            }

            AddCap(verts, uvs, tris, radius, -half, seg, false);

            Mesh m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            ShapeCache[key] = m;
            return m;
        }

        /// <summary>平面圆盘（半径 <paramref name="radius"/>，法线 +Y，在 y = 0 平面）。广场 / 水池水面用。</summary>
        public static Mesh Disc(float radius, int seg = 32)
        {
            string key = "disc|" + radius.ToString("0.000") + "|" + seg;
            if (ShapeCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            verts.Add(Vector3.zero); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            // 顺时针（从 +Y 往下看）：center → 下一个 → 当前
            for (int i = 0; i < seg; i++)
            {
                int a = 1 + i;
                int b = 1 + (i + 1) % seg;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }

            Mesh m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            ShapeCache[key] = m;
            return m;
        }

        /// <summary>
        /// 平面圆环（内半径 <paramref name="inner"/>、外半径 <paramref name="outer"/>，法线 +Y）。
        /// **环形道路 / 田圃土环 / 广场边石**都靠它 —— 这才是"圆形结构"的表达方式。
        /// </summary>
        public static Mesh Ring(float inner, float outer, int seg = 48)
        {
            string key = "ring|" + inner.ToString("0.000") + "|" + outer.ToString("0.000") + "|" + seg;
            if (ShapeCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                verts.Add(new Vector3(ca * inner, 0f, sa * inner));
                verts.Add(new Vector3(ca * outer, 0f, sa * outer));
                uvs.Add(new Vector2(ca * 0.5f + 0.5f, sa * 0.5f + 0.5f));
                uvs.Add(new Vector2(ca * 0.5f + 0.5f, sa * 0.5f + 0.5f));
            }
            // 顺时针（从 +Y 往下看）：inner_i → inner_i+1 → outer_i+1 → outer_i
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                int b = ((i + 1) % seg) * 2;
                tris.Add(a); tris.Add(b); tris.Add(b + 1);
                tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
            }

            Mesh m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            ShapeCache[key] = m;
            return m;
        }

        /// <summary>上半球（半径 <paramref name="radius"/>，赤道在 y = 0，顶在 y = radius）。**圆顶**用。</summary>
        public static Mesh Dome(float radius, int seg = 14, int rings = 5)
        {
            string key = "dome|" + radius.ToString("0.000") + "|" + seg + "|" + rings;
            if (ShapeCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int j = 0; j < rings; j++)
            {
                // 纬度：a = 0 在赤道，a → π/2 到顶
                float a0 = (j / (float)rings) * Mathf.PI * 0.5f;
                float a1 = ((j + 1) / (float)rings) * Mathf.PI * 0.5f;
                float r0 = Mathf.Cos(a0) * radius, y0 = Mathf.Sin(a0) * radius;
                float r1 = Mathf.Cos(a1) * radius, y1 = Mathf.Sin(a1) * radius;

                for (int i = 0; i < seg; i++)
                {
                    float t0 = (i / (float)seg) * Mathf.PI * 2f;
                    float t1 = ((i + 1) / (float)seg) * Mathf.PI * 2f;
                    var b0 = new Vector3(Mathf.Cos(t0) * r0, y0, Mathf.Sin(t0) * r0);
                    var b1 = new Vector3(Mathf.Cos(t1) * r0, y0, Mathf.Sin(t1) * r0);
                    var t01 = new Vector3(Mathf.Cos(t0) * r1, y1, Mathf.Sin(t0) * r1);
                    var t11 = new Vector3(Mathf.Cos(t1) * r1, y1, Mathf.Sin(t1) * r1);

                    if (r1 < 1e-4f)
                    {
                        // 最后一圈：退化成扇形（顶点在同一条"尖"上）
                        int at = verts.Count;
                        verts.Add(b0); verts.Add(b1); verts.Add(t01);
                        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0.5f, 1f));
                        tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
                    }
                    else
                    {
                        int at = verts.Count;
                        verts.Add(b0); verts.Add(b1); verts.Add(t11); verts.Add(t01);
                        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                        uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                        tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
                        tris.Add(at); tris.Add(at + 2); tris.Add(at + 3);
                    }
                }
            }

            Mesh m = new Mesh { name = key };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            ShapeCache[key] = m;
            return m;
        }

        /// <summary>圆盘/圆环的盖（<paramref name="up"/>=true 上盖，法线 +Y；false 下盖，法线 −Y）。</summary>
        private static void AddCap(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                   float radius, float y, int seg, bool up)
        {
            int center = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            int baseIdx = verts.Count;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = baseIdx + i;
                int b = baseIdx + (i + 1) % seg;
                if (up) { tris.Add(center); tris.Add(b); tris.Add(a); }
                else { tris.Add(center); tris.Add(a); tris.Add(b); }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 五、**局部坐标**造物体（建筑都是"父物体摆到方位角上、零件在局部坐标里搭"）
        // ══════════════════════════════════════════════════════════════════
        //
        // `Box` / `Cyl` / `Ball` 写的是**世界坐标**（它们只适合摆在未旋转的 `_root` 下）。
        // 一栋楼会被父物体旋转到"正面朝圆心"，它的零件就必须按**局部坐标**给，否则每块砖
        // 都要自己算一次旋转 —— 那是把 12 行代码变成 200 行三角函数，且改一个尺寸就全错。
        // ⇒ 下面这一组是建筑专用：`localPos` / `size` 都在楼的本地坐标系里（+Z = 正面朝圆心）。

        /// <summary>把一份网格放到 <paramref name="parent"/> 的局部坐标 <paramref name="localPos"/>（不缩放）。</summary>
        public static GameObject Local(Transform parent, string name, Mesh mesh, Vector3 localPos, Material mat,
                                       bool solid = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            if (solid)
            {
                // ★ 真尺寸网格 + convex MeshCollider = **逐顶点贴合**（不是一个大方块）★
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = true;
            }
            return go;
        }

        /// <summary>局部坐标的方块。<paramref name="size"/> 是世界尺寸（父物体不缩放）。`solid` ⇒ BoxCollider 逐面贴合。</summary>
        public static GameObject BoxL(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat,
                                      bool solid = false)
        {
            GameObject go = Local(parent, name, Prim(PrimitiveType.Cube), localPos, mat);
            go.transform.localScale = size;
            if (solid) go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>局部坐标的球（<paramref name="diameter"/> 世界直径，三轴等比 ⇒ SphereCollider 精确贴合）。</summary>
        public static GameObject BallL(Transform parent, string name, Vector3 localPos, float diameter, Material mat,
                                       bool solid = false)
        {
            GameObject go = Local(parent, name, Prim(PrimitiveType.Sphere), localPos, mat);
            go.transform.localScale = Vector3.one * diameter;
            if (solid) go.AddComponent<SphereCollider>();
            return go;
        }

        /// <summary>局部坐标的圆柱（真尺寸网格，底面中心 **y = localPos.y**，向 +Y 长 <paramref name="height"/>）。</summary>
        public static GameObject CylL(Transform parent, string name, Vector3 localPos, float radius, float height,
                                      Material mat, bool solid = false)
        {
            return Local(parent, name, Cylinder(radius, height), localPos + new Vector3(0f, height * 0.5f, 0f),
                         mat, solid);
        }

        /// <summary>局部坐标的锥（底面中心 <paramref name="localPos"/>，尖朝 +Y，总高 <paramref name="height"/>）。</summary>
        public static GameObject ConeL(Transform parent, string name, Vector3 localPos, float radius, float height,
                                       Material mat, int seg = 16, bool solid = false)
        {
            return Local(parent, name, Cone(radius, height, seg), localPos + new Vector3(0f, height * 0.5f, 0f),
                         mat, solid);
        }

        /// <summary>局部坐标的圆顶（赤道平面在 <paramref name="localPos"/>，向上鼓 <paramref name="radius"/>）。</summary>
        public static GameObject DomeL(Transform parent, string name, Vector3 localPos, float radius, Material mat,
                                       bool solid = false)
        {
            return Local(parent, name, Dome(radius), localPos, mat, solid);
        }

        /// <summary>世界坐标的水平圆盘 / 圆环薄板（路面、广场、田圃土环）。</summary>
        public static GameObject Plate(Transform parent, string name, Mesh flatMesh, Vector3 center, Material mat)
        {
            GameObject go = MeshObject(parent, name, flatMesh, center);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
