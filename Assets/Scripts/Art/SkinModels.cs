using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v213：皮肤的**真模型** —— 每件装饰品的几何、尺寸、挂法都在这里 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么几何写在代码里，而不是做成 FBX / 塞进 JSON
    /// ══════════════════════════════════════════════════════════════════════
    ///   · **不需要美术资产**：这八件全是"几个内建图元按算出来的相对位置摆一摆"，
    ///     没有一个零件需要手绘/雕刻 ⇒ 走 `PlayerBean` 那条程序化路线是同一条路 ✓
    ///   · **可复现**：形状是算式，改一个数就是改一件商品的剪影，没有"导出口径变了"这种事；
    ///   · **贴得住骨骼**：所有零件都写在**骨骼局部系**里（`HEAD` / `NECK` / `TORSO` / `SHOULDER_*`），
    ///     ⇒ 挂上去之后跟着骨骼一起转、一起压扁、一起走 —— 这是"加在角色身上"而不是"贴在屏幕上" ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、坐标系与量纲（**照抄 `PlayerBean` 的口径**，不然会飘）
    /// ══════════════════════════════════════════════════════════════════════
    ///   模型正面 = 本地 **+Z**，上 = +Y，右 = +X；总高 ≈ 1.20 米；所有数字单位是**米**。
    ///   头部（`HEAD` 局部系，原点 = 头心）：头是半径 ≈ 0.22 的球（x 半轴 0.2318 / y 0.2205 / z 0.216），
    ///     草帽檐在 y = 0.175、半径 0.340；眼睛在 z = 0.19 附近（**前表面到 z ≈ 0.23**）。
    ///     所以：任何要盖住眼睛的东西，前表面必须 > 0.23；任何要露在头侧的东西，x 必须 > 0.22。
    ///   躯干（`TORSO` 局部系，原点 = 髋）：身体是蛋形（x 半轴 0.21 / y 0.1995 / z 0.185，中心 y = 0.19），
    ///     肩在 y = 0.345、x = ±0.228；颈 `NECK` 在 y = 0.410；头底在 y ≈ 0.405。
    ///     ⇒ **脖子一圈是看不见的**（头比脖子宽得多），围巾必须做成"领圈 + 垂到胸口的尾巴"才读得出来 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、共享资源契约
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 网格：内建图元走 `TownKit.Prim`（静态缓存）；自建的**只有两个**（环面 / 圆锥），
    ///     按"形状键"在 `MeshCache` 里各**只建一次** ⇒ 与"穿了几件""换了几次"无关 ✓
    ///   · 材质：一律 `ToonLook.Get(色, 描边)`（共享池）。本文件**没有一处 `new Material`** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、骨骼找不到怎么办
    /// ══════════════════════════════════════════════════════════════════════
    /// 只记一条 Warning 并**跳过这一件**，不抛 —— 一件装饰消失不该让玩家连主城都进不去。
    /// 同一个（商品, 骨骼）组合只报一次（`WarnedMissing`），避免每帧刷屏 ✓
    /// </summary>
    public static class SkinModels
    {
        /// <summary>描边粗细（与 `PlayerBean.Outline` 同一个数，让装饰和身体是同一套线宽）。</summary>
        private const float Outline = 0.011f;
        private const float OutlineTiny = 0.004f;

        /// <summary>三份**全局共用**的辅色 —— 8 件商品的主/点缀色都在 JSON 里，这三个只做"叶/暗部/金属点"。</summary>
        private static readonly Color Leaf = new Color(0.30f, 0.52f, 0.26f);
        private static readonly Color Dark = new Color(0.13f, 0.11f, 0.10f);
        private static readonly Color Silver = new Color(0.92f, 0.94f, 0.95f);

        // ── 共享网格缓存（全工程一份）──
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();

        // ── 骨骼查找缓存（同一根玩家根节点只走一次递归）──
        private static Transform _cachedRoot;
        private static readonly Dictionary<string, Transform> BoneCache = new Dictionary<string, Transform>(8);

        // ── 缺骨骼只报一次 ──
        private static readonly HashSet<string> WarnedMissing = new HashSet<string>();

        // ══════════════════════════════════════════════════════════════════
        // 入口
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把 <paramref name="def"/> 这件装饰建到 <paramref name="playerRoot"/> 的骨骼上。
        /// 返回建出来的**每个挂点一个**容器物体（键风甲两肩 ⇒ 两个）；取不到骨骼时返回空表并记 Warning。
        /// </summary>
        public static List<GameObject> Build(TownSkinDef def, Transform playerRoot)
        {
            var made = new List<GameObject>(2);
            if (def == null || playerRoot == null) return made;

            string[] bones = def.BoneNames();
            if (bones.Length == 0)
            {
                Log.Warn($"皮肤 {def.id}：bones 为空，跳过");
                return made;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = Bone(playerRoot, bones[i]);
                if (bone == null)
                {
                    if (WarnedMissing.Add(def.id + "@" + bones[i]))
                        Log.Warn($"皮肤 {def.id}：玩家模型上找不到骨骼「{bones[i]}」，这件不会出现");
                    continue;
                }

                var group = new GameObject("SKIN_" + def.id);
                group.transform.SetParent(bone, false);
                group.transform.localPosition = Vector3.zero;
                group.transform.localRotation = Quaternion.identity;
                group.transform.localScale = Vector3.one;

                bool ok;
                try
                {
                    ok = BuildInto(def.id, group.transform, def);
                }
                catch (Exception e)
                {
                    ok = false;
                    Log.Error($"皮肤 {def.id} 建模异常：{e.GetType().Name}：{e.Message}");
                }

                if (!ok)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(group);
                    else UnityEngine.Object.DestroyImmediate(group);
                    if (WarnedMissing.Add(def.id + "!shape"))
                        Log.Warn($"皮肤 {def.id}：没有对应的形状实现（SkinModels.BuildInto 里没有这个 id），跳过");
                    continue;
                }
                made.Add(group);
            }
            return made;
        }

        /// <summary>按名字取骨骼（递归；结果按根节点缓存）。找不到返回 null。</summary>
        public static Transform Bone(Transform playerRoot, string name)
        {
            if (playerRoot == null || string.IsNullOrEmpty(name)) return null;
            if (!ReferenceEquals(_cachedRoot, playerRoot))
            {
                _cachedRoot = playerRoot;
                BoneCache.Clear();
            }
            if (BoneCache.TryGetValue(name, out Transform cached) && cached != null) return cached;

            Transform found = FindDeep(playerRoot, name);
            BoneCache[name] = found;      // null 也缓存：找不到就是找不到，不需要每帧再走一遍
            return found;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            int n = t.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // 八件商品的形状（★ 改剪影就改这几段；数字单位是米 ★）
        // ══════════════════════════════════════════════════════════════════

        private static bool BuildInto(string id, Transform P, TownSkinDef def)
        {
            Color c = def.Tint;
            Color c2 = def.Tint2;
            switch (id)
            {
                case "skin_flower_crown": BuildFlowerCrown(P, c, c2); return true;
                case "skin_red_scarf": BuildRedScarf(P, c, c2); return true;
                case "skin_seed_pack": BuildSeedPack(P, c, c2); return true;
                case "skin_rope_belt": BuildRopeBelt(P, c, c2); return true;
                case "skin_leaf_cape": BuildLeafCape(P, c, c2); return true;
                case "skin_owl_mask": BuildOwlMask(P, c, c2); return true;
                case "skin_moth_glasses": BuildMothGlasses(P, c, c2); return true;
                case "skin_iron_pauldron": BuildIronPauldron(P, c, c2); return true;
                default: return false;
            }
        }

        /// <summary>
        /// ① 稻草花冠（HEAD）—— 一圈压扁的草绳环套在帽檐上，环上插 8 朵花 + 4 片叶。
        /// 环在 y = 0.190（帽檐 y = 0.175、半径 0.340 ⇒ 环半径 0.300 正好落在檐面上），
        /// 压扁 y 缩放让"绳"变成扁带（0.042 宽 × 0.021 厚），不然像套了个轮胎。
        /// </summary>
        private static void BuildFlowerCrown(Transform P, Color c, Color c2)
        {
            Part(P, "Ring", Torus(), c, Outline,
                new Vector3(0f, 0.190f, 0.004f), new Vector3(0.300f, 0.150f, 0.300f));

            const int Flowers = 8;
            for (int i = 0; i < Flowers; i++)
            {
                float a = i * Mathf.PI * 2f / Flowers;
                float rad = 0.300f;
                float y = 0.200f + (i % 2 == 0 ? 0.018f : 0.006f);
                float size = i % 2 == 0 ? 0.082f : 0.066f;
                Part(P, "Flower", Sphere(), i % 2 == 0 ? c2 : c, Outline,
                    new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad),
                    new Vector3(size, size * 0.72f, size));
            }

            // 4 片叶：错开 22.5°，长轴沿 Z 并绕 Y 转到"径向朝外"
            for (int i = 0; i < 4; i++)
            {
                float a = (i * Mathf.PI * 2f / 4f) + Mathf.PI / 8f;
                float rad = 0.318f;
                Part(P, "Leaf", Sphere(), Leaf, OutlineTiny,
                    new Vector3(Mathf.Sin(a) * rad, 0.184f, Mathf.Cos(a) * rad),
                    new Vector3(0.055f, 0.026f, 0.088f),
                    new Vector3(0f, a * Mathf.Rad2Deg, 0f));
            }
        }

        /// <summary>
        /// ② 红围巾（NECK）—— 领圈 + 金色滚边 + 活扣 + 两条垂到胸口的尾巴。
        /// 领圈半径 0.205 放在 NECK 局部 y = 0.030（= 躯干 y 0.440）：
        /// 那里头的半宽只有 0.126（头心在 0.625），所以整圈露在**头的外面**✓
        /// </summary>
        private static void BuildRedScarf(Transform P, Color c, Color c2)
        {
            Part(P, "Collar", Torus(), c, Outline,
                new Vector3(0f, 0.030f, 0f), new Vector3(0.205f, 0.140f, 0.205f));
            Part(P, "Trim", Torus(), c2, OutlineTiny,
                new Vector3(0f, 0.050f, 0f), new Vector3(0.194f, 0.090f, 0.194f));
            Part(P, "Knot", Sphere(), c2, Outline,
                new Vector3(0.058f, -0.012f, 0.170f), Vector3.one * 0.090f);

            // 两条尾巴：贴在胸口（躯干 y 0.295 处身体前表面约在 z 0.157）
            Part(P, "TailA", Cube(), c, Outline,
                new Vector3(0.062f, -0.115f, 0.150f), new Vector3(0.105f, 0.255f, 0.042f),
                new Vector3(6f, 0f, -7f));
            Part(P, "TailB", Cube(), c, Outline,
                new Vector3(-0.052f, -0.135f, 0.142f), new Vector3(0.092f, 0.300f, 0.038f),
                new Vector3(4f, 0f, 8f));
        }

        /// <summary>
        /// ③ 种子背包（TORSO）—— 包体 + 包盖 + 两卷铺盖 + 两条过肩带。
        /// 包在背后 z = -0.255（身体背后表面约 -0.185）⇒ 从正面看不见、从背面一整块 ✓
        /// </summary>
        private static void BuildSeedPack(Transform P, Color c, Color c2)
        {
            Part(P, "Body", Cube(), c, Outline,
                new Vector3(0f, 0.205f, -0.255f), new Vector3(0.290f, 0.300f, 0.170f),
                new Vector3(-4f, 0f, 0f));
            Part(P, "Lid", Cube(), c2, Outline,
                new Vector3(0f, 0.330f, -0.243f), new Vector3(0.300f, 0.085f, 0.190f),
                new Vector3(-8f, 0f, 0f));

            // 两卷铺盖：圆柱的轴是 Y，绕 Z 转 90° ⇒ 横躺在包顶（沿 X 长 0.30）
            Part(P, "RollA", Cylinder(), c2, OutlineTiny,
                new Vector3(0f, 0.400f, -0.215f), new Vector3(0.058f, 0.150f, 0.058f),
                new Vector3(0f, 0f, 90f));
            Part(P, "RollB", Cylinder(), c2, OutlineTiny,
                new Vector3(0f, 0.400f, -0.300f), new Vector3(0.058f, 0.150f, 0.058f),
                new Vector3(0f, 0f, 90f));

            for (int s = -1; s <= 1; s += 2)
            {
                Part(P, "Strap", Cube(), c2, OutlineTiny,
                    new Vector3(s * 0.105f, 0.330f, 0.055f), new Vector3(0.055f, 0.035f, 0.290f),
                    new Vector3(24f, 0f, 0f));
                Part(P, "Buckle", Cube(), c, OutlineTiny,
                    new Vector3(s * 0.105f, 0.288f, 0.182f), new Vector3(0.048f, 0.028f, 0.030f));
            }
        }

        /// <summary>
        /// ④ 麻绳腰带（TORSO）—— 两圈绳 + 铜扣 + 右侧垂一段带头。
        /// 环用**非等比缩放**做出椭圆（x 0.192 / z 0.172）：身体在这一高度是 x 0.1787 / z 0.157 的椭圆，
        /// 等比圆环会在两侧浮空、前后扎进身体 ✗
        /// </summary>
        private static void BuildRopeBelt(Transform P, Color c, Color c2)
        {
            Part(P, "Rope", Torus(), c, Outline,
                new Vector3(0f, 0.085f, 0f), new Vector3(0.192f, 0.052f, 0.172f));
            Part(P, "RopeHi", Torus(), c2, OutlineTiny,
                new Vector3(0f, 0.110f, 0f), new Vector3(0.183f, 0.034f, 0.164f));
            Part(P, "Buckle", Cube(), c2, Outline,
                new Vector3(0f, 0.090f, 0.172f), new Vector3(0.072f, 0.066f, 0.032f));

            Part(P, "Knot", Sphere(), c2, OutlineTiny,
                new Vector3(0.105f, 0.072f, 0.160f), Vector3.one * 0.062f);
            Part(P, "TailEnd", Cube(), c, OutlineTiny,
                new Vector3(0.108f, 0.000f, 0.150f), new Vector3(0.042f, 0.150f, 0.036f),
                new Vector3(0f, 0f, 9f));
        }

        /// <summary>
        /// ⑤ 叶纹斗篷（TORSO）—— 三片逐层收窄的披风 + 领圈 + 一对肩别扣 + 叶纹。
        /// 三片分别落在 y 0.300 / 0.180 / 0.058，z 依次 -0.185 / -0.215 / -0.180：
        /// 越往下越窄、越往下越贴身 ⇒ 从背后看是"一片会收口的布"，不是三块板 ✓
        /// </summary>
        private static void BuildLeafCape(Transform P, Color c, Color c2)
        {
            Part(P, "Collar", Torus(), c2, Outline,
                new Vector3(0f, 0.372f, -0.010f), new Vector3(0.168f, 0.060f, 0.168f));
            Part(P, "PlateA", Cube(), c, Outline,
                new Vector3(0f, 0.300f, -0.185f), new Vector3(0.400f, 0.135f, 0.030f),
                new Vector3(9f, 0f, 0f));
            Part(P, "PlateB", Cube(), c, Outline,
                new Vector3(0f, 0.180f, -0.215f), new Vector3(0.345f, 0.145f, 0.030f),
                new Vector3(11f, 0f, 0f));
            Part(P, "PlateC", Cube(), c, Outline,
                new Vector3(0f, 0.058f, -0.180f), new Vector3(0.265f, 0.130f, 0.028f),
                new Vector3(13f, 0f, 0f));

            for (int s = -1; s <= 1; s += 2)
            {
                Part(P, "Clasp", Sphere(), c2, Outline,
                    new Vector3(s * 0.190f, 0.350f, 0.020f), Vector3.one * 0.078f);
                Part(P, "LeafMark", Sphere(), c2, OutlineTiny,
                    new Vector3(s * 0.100f, 0.178f, -0.232f), new Vector3(0.055f, 0.090f, 0.016f),
                    new Vector3(0f, 0f, s * 14f));
            }
        }

        /// <summary>
        /// ⑥ 守望面具（HEAD）—— 木面具 + 两只凹眼 + 亮银眼点 + 尖喙 + 一对耳簇。
        /// ★ 面片必须**盖过现有眼睛**：现有眼珠的前表面到 z ≈ 0.23，所以面具前表面做到 0.243 ✓
        /// </summary>
        private static void BuildOwlMask(Transform P, Color c, Color c2)
        {
            Part(P, "Plate", Sphere(), c, Outline,
                new Vector3(0f, 0.020f, 0.198f), new Vector3(0.310f, 0.245f, 0.090f));

            for (int s = -1; s <= 1; s += 2)
            {
                Part(P, "Socket", Sphere(), c2, OutlineTiny,
                    new Vector3(s * 0.082f, 0.055f, 0.228f), new Vector3(0.090f, 0.090f, 0.036f));
                Part(P, "Glint", Sphere(), Silver, OutlineTiny,
                    new Vector3(s * 0.090f, 0.064f, 0.250f), Vector3.one * 0.030f);
                // 耳簇：圆锥轴 +Y，绕 Z 往外倒 —— s 的正负决定往哪边倒
                Part(P, "EarTuft", Cone(), c, Outline,
                    new Vector3(s * 0.135f, 0.180f, 0.020f), new Vector3(0.078f, 0.170f, 0.078f),
                    new Vector3(0f, 0f, -s * 20f));
            }

            // 喙：圆锥 +Y 绕 X 转 -90° ⇒ 尖朝 +Z（身前）
            Part(P, "Beak", Cone(), c2, Outline,
                new Vector3(0f, -0.055f, 0.225f), new Vector3(0.080f, 0.105f, 0.080f),
                new Vector3(-90f, 0f, 0f));
        }

        /// <summary>
        /// ⑦ 飞蛾镜（HEAD）—— 两枚圆框 + 镜片 + 梁 + 两条镜腿 + 镜腿外端各挂一片小蛾翅。
        /// 圆框是环面绕 X 转 90°（环面本来躺在 X-Z 平面，转过来就**正对 +Z**）
        /// </summary>
        private static void BuildMothGlasses(Transform P, Color c, Color c2)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Part(P, "Rim", Torus(), c2, OutlineTiny,
                    new Vector3(s * 0.088f, 0.045f, 0.238f), Vector3.one * 0.078f,
                    new Vector3(90f, 0f, 0f));
                Part(P, "Lens", Sphere(), c, OutlineTiny,
                    new Vector3(s * 0.088f, 0.045f, 0.236f), new Vector3(0.128f, 0.128f, 0.022f));
                // 镜腿：从镜框外缘往后走，绕 Y 转到 (0.09, 0, -0.098) 这个方向（≈137°）
                Part(P, "Temple", Cube(), c2, OutlineTiny,
                    new Vector3(s * 0.145f, 0.046f, 0.189f), new Vector3(0.014f, 0.014f, 0.140f),
                    new Vector3(0f, s * 137f, 0f));
                // 蛾翅：挂在镜腿外端（那里头的半宽只有 0.185 ⇒ 0.208 一定露出来）
                Part(P, "MothWing", Sphere(), c, OutlineTiny,
                    new Vector3(s * 0.208f, 0.088f, 0.098f), new Vector3(0.100f, 0.068f, 0.024f),
                    new Vector3(0f, s * 40f, s * 18f));
            }
            Part(P, "Bridge", Cube(), c2, OutlineTiny,
                new Vector3(0f, 0.045f, 0.240f), new Vector3(0.048f, 0.018f, 0.018f));
        }

        /// <summary>
        /// ⑧ 铁肩甲（每个 SHOULDER 一根，两肩各一份）—— 主甲片 + 下叠甲 + 顶刺 + 两枚铜钉。
        /// 挂在**肩关节**上 ⇒ 挥锄/走路时跟着手臂一起抬（这正是"挂在骨骼上"与"挂在世界坐标上"的区别）
        /// </summary>
        private static void BuildIronPauldron(Transform P, Color c, Color c2)
        {
            Part(P, "Plate", Sphere(), c, Outline,
                new Vector3(0f, 0.012f, 0f), new Vector3(0.190f, 0.120f, 0.190f));
            Part(P, "Lame", Sphere(), c, Outline,
                new Vector3(0f, -0.058f, 0.006f), new Vector3(0.170f, 0.080f, 0.170f));
            Part(P, "Spike", Cone(), c, OutlineTiny,
                new Vector3(0f, 0.070f, 0f), new Vector3(0.046f, 0.115f, 0.046f));
            for (int s = -1; s <= 1; s += 2)
                Part(P, "Rivet", Sphere(), c2, OutlineTiny,
                    new Vector3(s * 0.088f, 0.000f, 0.062f), Vector3.one * 0.032f);
        }

        // ══════════════════════════════════════════════════════════════════
        // 几何工具（与 `PlayerBean` 同一手法：共享网格 + 共享材质）
        // ══════════════════════════════════════════════════════════════════

        private static GameObject Part(Transform parent, string name, Mesh mesh, Color color, float outline,
                                       Vector3 pos, Vector3 scale)
            => Part(parent, name, mesh, color, outline, pos, scale, Vector3.zero);

        /// <summary>造一个带网格的部件。**网格与材质都是共享的**（见类注释第三节）。</summary>
        private static GameObject Part(Transform parent, string name, Mesh mesh, Color color, float outline,
                                       Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler != Vector3.zero) go.transform.localRotation = Quaternion.Euler(euler);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ToonLook.Get(color, outline);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }

        private static Mesh Sphere() => TownKit.Prim(PrimitiveType.Sphere);
        private static Mesh Cube() => TownKit.Prim(PrimitiveType.Cube);
        private static Mesh Cylinder() => TownKit.Prim(PrimitiveType.Cylinder);

        /// <summary>
        /// 环面（甜甜圈）：**主半径 1.0、管半径 0.14**，躺在 X-Z 平面（轴向 = +Y）。
        /// ⇒ 挂上去用 `scale = (R, R, R)` 时主半径就是 R 米、管子是 0.14R。
        /// 围巾/腰带要"扁绳"效果 ⇒ 传非等比 scale（如 `(0.20, 0.14, 0.20)`）把管子压扁 ✓
        /// 绕向：本引擎是"顺时针为正面"，所以三角形按 `(a, b, c) / (a, c, d)` 的**外法线取反**顺序缝
        ///（见 `Torus` 里的注释），否则整圈会被背面剔除、只剩描边外壳。
        /// </summary>
        private static Mesh Torus()
        {
            const string Key = "torus24x10";
            if (MeshCache.TryGetValue(Key, out Mesh cached) && cached != null) return cached;

            const int Major = 24, Minor = 10;
            const float R = 1.0f, r = 0.14f;

            var verts = new List<Vector3>(Major * Minor);
            for (int i = 0; i < Major; i++)
            {
                float a = i * Mathf.PI * 2f / Major;
                float sa = Mathf.Sin(a), ca = Mathf.Cos(a);
                for (int j = 0; j < Minor; j++)
                {
                    float b = j * Mathf.PI * 2f / Minor;
                    float cb = Mathf.Cos(b), sb = Mathf.Sin(b);
                    // 径向 = (sa, 0, ca)，上 = (0,1,0)
                    verts.Add(new Vector3(sa * (R + r * cb), r * sb, ca * (R + r * cb)));
                }
            }

            var tris = new List<int>(Major * Minor * 6);
            for (int i = 0; i < Major; i++)
            {
                int inx = (i + 1) % Major;
                for (int j = 0; j < Minor; j++)
                {
                    int jnx = (j + 1) % Minor;
                    int a = i * Minor + j;
                    int b = i * Minor + jnx;
                    int c = inx * Minor + jnx;
                    int d = inx * Minor + j;
                    // ★ 绕向：`cross(v1-v0, v2-v0)` 必须指向**内侧**才算正面
                    //   （本引擎顺时针为正面；推导见 `PlayerBean.ConeMesh` 的 (b0, tip, b1) 顺序）
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = NewMesh("skin_torus", verts, tris);
            MeshCache[Key] = m;
            return m;
        }

        /// <summary>
        /// 圆锥：**底半径 0.5、高 1**，底面在 y = 0、尖在 y = 1，闭口（含底盖）。
        /// 与 `PlayerBean.ConeMesh` 同一个绕向约定（侧面 `(b0, tip, b1)`、底盖 `(center, b0, b1)`）。
        /// </summary>
        private static Mesh Cone()
        {
            const string Key = "cone12";
            if (MeshCache.TryGetValue(Key, out Mesh cached) && cached != null) return cached;

            const int Seg = 12;
            const float R = 0.5f;
            var verts = new List<Vector3>((Seg + 1) * 3 + 1);
            var tris = new List<int>(Seg * 6);

            for (int i = 0; i < Seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / Seg, a1 = (i + 1) * Mathf.PI * 2f / Seg;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * R, 0f, Mathf.Cos(a0) * R));
                verts.Add(new Vector3(Mathf.Sin(a1) * R, 0f, Mathf.Cos(a1) * R));
                verts.Add(new Vector3(0f, 1f, 0f));
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
            }
            int capCenter = verts.Count;
            verts.Add(Vector3.zero);
            for (int i = 0; i < Seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / Seg, a1 = (i + 1) * Mathf.PI * 2f / Seg;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * R, 0f, Mathf.Cos(a0) * R));
                verts.Add(new Vector3(Mathf.Sin(a1) * R, 0f, Mathf.Cos(a1) * R));
                tris.Add(capCenter); tris.Add(b + 0); tris.Add(b + 1);
            }

            Mesh m = NewMesh("skin_cone", verts, tris);
            MeshCache[Key] = m;
            return m;
        }

        private static Mesh NewMesh(string name, List<Vector3> verts, List<int> tris)
        {
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>自建网格的份数（探针/报告用：证明"不随实例增长"）。</summary>
        public static int CustomMeshCount => MeshCache.Count;

        /// <summary>已缓存的自建网格清单（报告用）。</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder(160);
            sb.Append("SkinModels 自建网格：");
            if (MeshCache.Count == 0) sb.Append("（还没建过）");
            foreach (var kv in MeshCache)
            {
                Mesh m = kv.Value;
                sb.Append($" {kv.Key}[v={(m != null ? m.vertexCount : 0)}");
                sb.Append($",t={(m != null ? m.triangles.Length / 3 : 0)}]");
            }
            sb.Append($"｜共用材质 {ToonLook.MaterialPoolCount} 份");
            return sb.ToString();
        }
    }
}
