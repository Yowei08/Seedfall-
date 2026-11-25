using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v197：**六株机制植物 ＋ 十件机制道具 ＋ 天气的"看得见"那一半** ★★
    ///
    /// ── 为什么单独一个 partial 文件，而不是塞进 `WorldView.cs` ──
    ///   `WorldView.cs` 已经 6500 行，而它同时在**两个方向**上被改
    ///   （表现层这条线在改各种 fx）。把 500 行新内容插进去，冲突面会大到不可合并。
    ///   所以：`WorldView` 改成 `partial`，本轮新增的表现全部落在这个文件里；
    ///   原文件只多**一行**调用（`SyncProgVisuals`），见那里的注释 ✓
    ///   ⇒ `Sync` 的调用顺序保证我的覆盖生效：`SyncPlots`（占位方块/模型）→ 本文件（植株/道具/天气）✓
    ///
    /// ── 硬约束（用户点名，逐条对号）──
    ///   ① **不新建资源**：网格全部来自 `_fxMeshCache`（`FxIcoShellMesh` / `FxFrustumMesh` /
    ///      `FxDiscMesh`，都是既有工厂），材质全部来自 `MatCached` / `VfxMatCached` 的缓存 ⇒
    ///      运行期 `new Mesh` = 0、`new Material` = 0（只有 `Light` 组件是新的，那是 Unity 的组件）✓
    ///   ② **不每实例 new Mesh / new Material** ✓（同上，建一次就留住）
    ///   ③ **淡出只用缩放 / 位移 / 颜色**，**不逐实例改 alpha** ✓
    ///      （盾环与雨丝都靠缩放与位移；颜色只在缓存材质上设一次，不在每帧改）
    /// </summary>
    public sealed partial class WorldView
    {
        // ══════════════════════════════════════════════════════════════════
        // 一、六种机制植物的程序化形状
        //
        // ★ 它们为什么没有模型 ★
        //   `ModelView.Spawn("Plants", <id>)` 会去 `Resources/Art/Plants/<id>.fbx` 找资源；
        //   本轮 6 株是新品种，**没有任何 fbx**（也不许新建资源）
        //   ⇒ `EnsurePlotCropModels` 三档全取不到 ⇒ `SyncPlots` 会退回"占位方块"。
        //   那个方块能看见、但**六株长得一模一样**，读不出"这是机制植物"✗
        //   ⇒ 本文件给 6 株各建一个"最小形状"：3 个以内零件、全部来自既有网格工厂 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>六株机制植物的 id（顺序 = 形状号）。</summary>
        private static readonly string[] ProgSeedIds =
        {
            "warblade_bloom",   // 0 战镰花：两把交叉的刃
            "gale_vine",        // 1 疾风藤：一根茎 + 一片叶盘
            "sunflower_crown",  // 2 日光葵：扁球花盘 + 一圈花瓣
            "rainlily",         // 3 雨语莲：宽浅的水盆 + 一颗水珠
            "shelter_ivy",      // 4 庇护藤：一座编成弧顶的藤罩
            "lucky_monarch",    // 5 四叶王：四片叶 + 一根主轴
            // ★ v252：4 株新植物（无机制）。它们同样没有 fbx ⇒ 走同一条程序化形状路线，
            //   理由与上面那 6 株逐字相同：不新建任何资源，但**每一株长得不一样** ✓
            "mint_sprout",      // 6 薄荷苗：三片竖叶 + 细茎
            "cinder_pepper",    // 7 灰烬椒：一颗锥椒 + 蒂 + 一片碎叶
            "glass_melon",      // 8 琉璃瓜：一个大瓜 + 一圈箍 + 蒂
            "frost_reed",       // 9 霜纹芦：一根细高柱 + 尖端 + 两片鳍叶
            // ★ v252 第三批：5 株（同样没有 fbx ⇒ 同一条程序化形状路线）
            "glow_moss",        // 10 辉苔：低矮的苔垫 + 三颗亮点
            "salt_melon",       // 11 盐霜瓜：一颗瓜 + 一圈盐霜环
            "cloud_orchid",     // 12 云兰：三片竖瓣 + 一朵云团
            "mirror_bloom",     // 13 镜花：一片镜盘 + 一块斜插的碎片
            "ember_reed",       // 14 烬苇：一根细苇 + 顶端的余烬
        };

        private GameObject[] _progPlotRoots;   // 每格一个根（没有 = null）
        private int[] _progPlotShape;          // 每格当前的形状号（-1 = 不是机制植物）
        private int[] _progPlotStage;          // v253：每格当前激活的生长阶段（-1 = 还没摆过）
        private float[] _progSwayPhase;        // v253：每格的摇摆相位（只影响视觉）
        private float _progMatureHeight;       // v255：成熟基准高（= tile × 0.72），统一曲线的量纲
        private GameObject[] _progCarrots;
        private GameObject _progRabbit;
        private GameObject _progShieldRing;
        private GameObject _progSun;
        private Light _progSunLight;
        // ★★ v308：**常驻雨幕**（用户：「雨天增加常驻下雨效果」）★★
        //   整套只有 **1 个渲染体**（一个 `ParticleSystem` + 它的 `ParticleSystemRenderer`），
        //   粒子由引擎自己循环复用 ⇒ 运行期零 `Instantiate` / 零 `new`、每帧只写 2 个字段。
        //   旧实现的 `_progRain` / `_progRainPhase` / `_progRainStart` / `_progRainLaunched` /
        //   `_progRainWaveCount` / `_progRainAirborne` / `_progRainLanded` 已全部**删除**
        //   （那是"每场雨只落一波 84 条"的定长池，与"常驻"直接冲突）。
        private GameObject _progRainGo;
        private ParticleSystem _progRainPs;
        private ParticleSystemRenderer _progRainRend;
        /// <summary>当前整体不透明度（0..1，用于雨停后淡出；只写到材质上，不改粒子本身）。</summary>
        private float _progRainAlpha;
        /// <summary>累计下雨时长（读数用：证明它是**常驻**的，不是一波）。</summary>
        private float _progRainOnT;
        private int _progRainStartedTimes;     // 本次进了几次雨天（读数）
        private float _progSunT;

        /// <summary>某株植物的形状号（不是机制植物 ⇒ −1）。</summary>
        private static int ProgShapeOf(string seedId)
        {
            if (string.IsNullOrEmpty(seedId)) return -1;
            for (int i = 0; i < ProgSeedIds.Length; i++)
                if (string.Equals(ProgSeedIds[i], seedId, System.StringComparison.Ordinal)) return i;
            return -1;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v253：**生长阶段**（幼苗 / 小苗 / 抽穗·花苞 / 成熟）
        //
        // ★ 为什么要有这一段 ★
        //   上面那 15 株是程序化形状，**一株只有一个外形**：从前一版起，唯一的"生长"
        //   是 `localScale.y` 随 `GrowthProgress01` 从 0.20 拉到 1.00 ⇒
        //   整段生长期里它是**同一株草被拉高**，看不出"长出了什么" ✗
        //   24 株有 fbx 的品种本来就有 young / grown / mature 三档模型（见 `SyncPlots`），
        //   缺生长历程的正是这 15 株程序化品种 ✓
        //
        // ★ 阶段划分（按 `FarmPlot.GrowthProgress01`，不新造时间源）★
        //   阶段 0  `[0.00, 0.25)`  两片小叶 + 极短茎       顶点高 ≈ 0.13 m
        //   阶段 1  `[0.25, 0.55)`  4~6 片叶 + 茎           顶点高 ≈ 0.33 m
        //   阶段 2  `[0.55, 0.85)`  叶更多 + 花苞 / 穗 / 幼果 顶点高 ≈ 0.55 m
        //   阶段 3  `[0.85, 1.00]`  成熟体 = 该品种原有外形  顶点高 = tile × 0.72（1.80 m @ tile 2.5）
        //
        // ★ 三件"必须这么写"的事 ★
        //   ① **不每帧建网格**：阶段 0~2 的零件全部来自 `_fxMeshCache` 的既有网格工厂，
        //      而且键位只取决于（形状类别 × 阶段）⇒ **全部 15 株共用 ≤ 4 份网格、1 份叶片网格** ✓
        //   ② **切换不销毁重建**：四个阶段在 `BuildProgPlant` 时**一次建齐**，之后只 `SetActive` ✓
        //   ③ **身高首尾相接**：每段从"上一段的顶点高度"起长 ⇒ 切换那一下不跳高度（只换外形）✓
        // ══════════════════════════════════════════════════════════════════

        private const int ProgStageCount = 4;

        /// <summary>四个阶段的**进度起点**（`GrowthProgress01` 口径）。</summary>
        private static readonly float[] ProgStageStart = { 0.00f, 0.25f, 0.55f, 0.85f };

        /// <summary>阶段 0~2 的**顶点高度（米）** —— 几何就是照这三个数建的，见 `BuildProgSproutStage`。</summary>
        private static readonly float[] ProgStageTopM = { 0.13f, 0.33f, 0.55f };

        /// <summary>阶段 0 的起长比例（种子刚破土时只有目标高度的 75%）。</summary>
        private const float ProgStageFirstK = 0.75f;

        /// <summary>叶片数：幼苗 2 片、小苗 5 片、抽穗期 7 片（藤/瓜果类各减 1 —— 叶片更大更少）。</summary>
        private static readonly int[] ProgLeafN = { 2, 5, 7 };

        /// <summary>叶片半长（米）：`FxDiscMesh` 是半径 1 的盘 ⇒ `localScale.z` 就是半长。</summary>
        private static readonly float[] ProgLeafHalf = { 0.045f, 0.070f, 0.090f };

        /// <summary>叶片外张角（度）：越成熟越外张。</summary>
        private static readonly float[] ProgLeafTiltDeg = { 52f, 60f, 64f };

        /// <summary>茎长（米）与茎半径（米）。</summary>
        private static readonly float[] ProgStemLen = { 0.105f, 0.285f, 0.455f };
        private static readonly float[] ProgStemR = { 0.010f, 0.019f, 0.026f };

        /// <summary>
        /// 叶色：**同一条绿**（色相不动），只把明度/饱和度往上/下微调 ——
        /// 幼苗最嫩（亮、偏黄绿），抽穗期更沉，成熟体交给品种自己的颜色 ✓
        /// </summary>
        private static readonly Color[] ProgLeafStageColor =
        {
            new Color(0.66f, 0.94f, 0.56f),   // 阶段 0 幼苗：嫩绿
            new Color(0.56f, 0.87f, 0.45f),   // 阶段 1 小苗
            new Color(0.46f, 0.79f, 0.36f),   // 阶段 2 抽穗：深一档
        };

        /// <summary>阶段 2 顶端装饰的颜色（按形状类别取，绿色系之外只加这一颗）。</summary>
        private static readonly Color[] ProgAccentColor =
        {
            new Color(0.96f, 0.90f, 0.62f),   // Leafy    花苞：乳白偏黄
            new Color(0.86f, 0.92f, 0.56f),   // Vine     卷须：嫩黄绿
            new Color(0.92f, 0.66f, 0.30f),   // Fruiting 幼果：暖橙
            new Color(0.96f, 0.84f, 0.40f),   // Spike    穗：麦黄
        };

        /// <summary>品种**形状类别** —— 阶段 0~2 的外形按类别做四套（不是按品种做 15 套）。</summary>
        private enum ProgShapeClass { Leafy = 0, Vine = 1, Fruiting = 2, Spike = 3 }

        /// <summary>
        /// 品种 → 形状类别。判据是**成熟形态**（与 `seeds.json` 的 `_note_名字与形态` 同一套读法）：
        /// 穗/花/刃 → Spike，藤 → Vine，瓜果/椒 → Fruiting，叶菜/苔/莲 → Leafy。
        /// </summary>
        private static ProgShapeClass ProgClassOf(int shape)
        {
            switch (shape)
            {
                case 0:    // 战镰花：两把刃（穗/刃类）
                case 2:    // 日光葵：花盘 + 花瓣
                case 9:    // 霜纹芦：细高柱
                case 12:   // 云兰：竖瓣 + 云团
                case 14:   // 烬苇：细苇 + 余烬
                    return ProgShapeClass.Spike;
                case 1:    // 疾风藤
                case 4:    // 庇护藤
                    return ProgShapeClass.Vine;
                case 7:    // 灰烬椒：锥椒
                case 8:    // 琉璃瓜：大瓜
                case 11:   // 盐霜瓜
                    return ProgShapeClass.Fruiting;
                default:   // 3 雨语莲 / 5 四叶王 / 6 薄荷苗 / 10 辉苔 / 13 镜花
                    return ProgShapeClass.Leafy;
            }
        }

        /// <summary>`GrowthProgress01` → 阶段号（0..3）。</summary>
        private static int ProgStageOf01(float gp)
        {
            if (gp >= ProgStageStart[3]) return 3;
            if (gp >= ProgStageStart[2]) return 2;
            if (gp >= ProgStageStart[1]) return 1;
            return 0;
        }

        /// <summary>本阶段内的进度 0..1（用于"阶段内继续长大"）。</summary>
        private static float ProgStageLocal01(float gp, int stage)
        {
            float a = ProgStageStart[stage];
            float b = stage + 1 < ProgStageCount ? ProgStageStart[stage + 1] : 1f;
            return Mathf.Clamp01((gp - a) / Mathf.Max(0.01f, b - a));
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v255：**统一生长高度曲线** —— 两条渲染链共用的唯一一把尺子 ★
        //
        // ★ 为什么必须加这一节（用户原话："统一植物生长大小，存在部分植物的部分
        //   阶段大小异常"）★
        //   改之前，**同一个 `GrowthProgress01` 的世界高度差到 6.2 倍**：
        //     · fbx 链（24 品种）走 `ScaleToHeight`，目标 = tile×0.72×baseK
        //       ⇒ gp=0.55 时 1.40 m，平顺 ✓
        //     · 程序化链（15 品种）阶段 0~2 用的是**绝对常量** 0.13/0.33/0.55 m
        //       （`ProgStageTopM`），阶段 3 却换成 `tile×0.72 = 1.80 m` 的**盒子**
        //       ⇒ 同 gp 下 fbx 1.40 m vs 程序化 0.42~0.59 m（**矮 2.4~3.3 倍**）；
        //       而 gp=0.85 那一档，阶段 3 的盒子乘的是**各品种不同的本地几何**
        //       （顶点 0.26~1.61 m，见 `AddProgMatureParts`），
        //       k01 起点又只有 0.55/1.80 = 0.306
        //       ⇒ 成熟那一刻的顶点高 = 0.14 m（辉苔）… 0.89 m（日光葵），
        //          **同进度差 6.2 倍**，而且从阶段 2 的 0.55 m **缩回去**到 0.14 m
        //          ⇒ 玩家看到的是"成熟时植株突然缩小"，正是报的那条 ✗
        //   现在：**只有一条曲线** `CropHeightFrac01(gp)`，两条链都从它取高度
        //   ⇒ 同一进度同一高度，残差只剩"类别带 ±6%"与呼吸 ±3.5% ✓
        //
        // ★ 曲线数值的依据（量纲来自**代码里已有的那把尺子**，比例按"看得出生长"定）★
        //   成熟段的世界高度早已被 fbx 链钉死在 `tile × 0.72`
        //   （`WorldView.cs:1728`；`FarmGrid.TileSize` = 2.5，见 `WorldView.cs:1049`
        //   ⇒ 成熟 **1.80 m**）—— 这一档**不动**，它是格子定出来的唯一硬约束。
        //   要统一的是**比例**：早期两档该有多矮。见 `CropHeightAnchorFrac` 的说明：
        //   0.16 / 0.34 / 0.62 / 0.92 / 1.00 ⇒ 0.29 / 0.61 / 1.12 / 1.66 / 1.80 m，
        //   相邻两段 1.8~2.1 倍落差 ⇒ 生长历程读得出来 ✓
        //   旧 fbx 链的早期档是 0.45 / 0.75 / 0.78 / 1.00 ⇒ 幼苗 0.81 m、
        //   与成熟体几乎等高，四段在画面上没有落差 —— 那才是要修的病，
        //   所以这里**不迁就它的历史数值**，两条链一起改到新曲线上 ✓
        //   ⚠️ 这一改会让 24 个 fbx 品种的幼苗/小苗明显变小（0.81→0.29、1.06→0.61），
        //     成熟段 1.80 m 不变。这是刻意的代价，见下面的"未验证项"。
        //
        // ★ 物种类别带（成熟处倍率，≤ ±6%）★
        //   统一 ≠ 一模一样：留一条窄带保住"穗类最高、叶菜最矮"的读感，
        //   同时任意两品种在同进度下相差 ≤ 6%（用户要的"基本一致"）✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>世界高度曲线的锚点（`GrowthProgress01`），与四个阶段起点对齐。</summary>
        private static readonly float[] CropHeightAnchorGp = { 0.00f, 0.25f, 0.55f, 0.85f, 1.00f };

        /// <summary>
        /// 锚点处的目标高度，**单位 = 成熟基准高 `tile × 0.72`**（1.80 m @ tile 2.5）。
        /// 对应米数：0.29 / 0.61 / 1.12 / 1.66 / 1.80 m —— 分段线性、全程单调不减。
        /// </summary>
        // ★ v279：锚点从 {0.45,0.60,0.78,1.00,1.00} 改成下面这组 ★
        //   原因：原锚点让**幼苗高达成熟体的 45%**（1.80m × 0.45 = 0.81m），四段生长在画面上几乎没有落差，
        //   而耕地格子只有 2.5m ⇒ 一株"幼苗"占格子三分之一高，视觉上完全不像幼苗。
        //   现锚点 = 成熟体的 16% / 34% / 62% / 92% / 100%（× 1.80m ⇒ 0.29 / 0.61 / 1.12 / 1.66 / 1.80 m），
        //   相邻阶段仍有 **2 倍以上落差**，"统一"的是"同进度同高度"（跨品种一致），不是"把苗放大"。
        private static readonly float[] CropHeightAnchorFrac = { 0.16f, 0.34f, 0.62f, 0.92f, 1.00f };

        /// <summary>各锚点的目标高度（米，tile = 2.5）—— 只给注释与诊断看，运行期从比例算。</summary>
        private const string CropHeightAnchorNote =
            "gp 0.00→0.29m  0.25→0.61m  0.55→1.12m  0.85→1.66m  1.00→1.80m （tile 2.5）";

        /// <summary>
        /// `GrowthProgress01` → **目标高度比例**（0.16 … 1.00，分段线性、单调不减）。
        /// ★ 两条渲染链**只准从这里取高度**，不许再各写一份 ★
        /// </summary>
        private static float CropHeightFrac01(float gp)
        {
            gp = Mathf.Clamp01(gp);
            for (int i = 1; i < CropHeightAnchorGp.Length; i++)
            {
                if (gp > CropHeightAnchorGp[i]) continue;
                float a = CropHeightAnchorGp[i - 1];
                float b = CropHeightAnchorGp[i];
                float u = Mathf.Clamp01((gp - a) / Mathf.Max(0.0001f, b - a));
                return Mathf.Lerp(CropHeightAnchorFrac[i - 1], CropHeightAnchorFrac[i], u);
            }
            return CropHeightAnchorFrac[CropHeightAnchorFrac.Length - 1];
        }

        /// <summary>
        /// 按**物种类别**给成熟高度的窄带倍率（±6%，见上面那段说明）。
        /// 穗/花/刃最高 → 藤 → 瓜果 → 叶菜/苔/莲最矮。
        /// 米数（tile 2.5，成熟 1.80 m 基准）：穗 1.85 / 藤 1.75 / 瓜果 1.80 / 叶菜 1.69。
        /// </summary>
        private static float ProgClassBand(ProgShapeClass cls)
        {
            switch (cls)
            {
                case ProgShapeClass.Spike:    return 1.03f;   // 穗类：1.85 m
                case ProgShapeClass.Fruiting: return 1.00f;   // 瓜果：1.80 m（与 fbx 链中性带同一档）
                case ProgShapeClass.Vine:     return 0.97f;   // 藤类：1.75 m
                default:                      return 0.94f;   // 叶菜：1.69 m
            }
        }

        // ── 几何实测缓存（"包围盒归一化"的分母，见 `SyncProgPlants` 的缩放那一段）──
        //
        // ★ 为什么必须**实测**，不能用常量 ★
        //   同一个 `ProgStageTopM[stage]` 对四个类别是**同一个数**，但 15 株的
        //   真实顶点高并不相同：类别乘子（`lenK` 穗 1.06 / 藤 0.88、`leafHalf`
        //   叶菜 1.20 / 瓜果 1.35 / 穗 0.62）会**叠加到高度上**
        //   ⇒ 藤类阶段 2 的顶点只有 0.42 m，穗类 0.59 m，与常量 0.55 各偏 24% / 7%
        //      —— 这就是"部分植物的部分阶段大小异常"里的那 24% ✓
        //   改成量包围盒 ⇒ 乘子再怎么叠加，除以自己的实测高之后都归到同一个目标高 ✓

        /// <summary>阶段 0~2 的实测几何顶点高（米），按 `[类别, 阶段]` 缓存 —— 同类别同阶段共用一份几何。</summary>
        private static readonly float[,] ProgGeomTopCache = new float[4, 3];

        /// <summary>阶段 3（成熟体）的实测几何顶点高 / 最大水平半径（米），按品种号缓存。</summary>
        private static float[] _progMatureGeomTop;
        private static float[] _progMatureGeomHalf;

        /// <summary>
        /// 量一个分组（植株的某一段）的**几何包围盒**：所有零件 `Mesh.bounds` 的八个角
        /// 经各自的局部变换后取并集 ⇒ `top` = 地面之上的最高点，`half` = 最大水平半径。
        /// 单位米（分组建在根下、根缩放在 `SyncProgPlants` 里另算，这里量的是"缩放 = 1"）。 ✓
        /// </summary>
        private static void ProgGroupExtentM(GameObject group, out float top, out float half)
        {
            top = 0f;
            half = 0f;
            for (int i = 0; i < group.transform.childCount; i++)
            {
                Transform c = group.transform.GetChild(i);
                MeshFilter mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = Matrix4x4.TRS(c.localPosition, c.localRotation, c.localScale);
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = new Vector3(
                        (k & 1) == 0 ? b.min.x : b.max.x,
                        (k & 2) == 0 ? b.min.y : b.max.y,
                        (k & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (p.y > top) top = p.y;
                    float r = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                    if (r > half) half = r;
                }
            }
        }

        /// <summary>
        /// 每帧的总入口（`Sync` 里**唯一**调用点，紧跟在 `SyncPlots` 之后）。
        /// 顺序很重要：`SyncPlots` 每帧都会把占位方块打开 ⇒ 这里必须**之后**跑，才能把它压下去 ✓
        /// </summary>
        public void SyncProgVisuals(RunSession s, float dt)
        {
            if (_root == null || s == null || s.World == null) return;
            SyncProgPlants(s, dt);
            SyncCarrots(s, dt);
            SyncRabbit(s, dt);
            SyncShieldRing(s, dt);
            SyncWeatherFx(s, dt);
        }

        // ────────────────────────── 植株 ──────────────────────────

        private void SyncProgPlants(RunSession s, float dt)
        {
            FarmGrid grid = s.World.Farm;
            int want = grid.Slots.Length;
            if (_progPlotRoots == null || _progPlotRoots.Length != want)
            {
                // 耕地扩建（行数随存活时间长）⇒ 数组跟着长，已有的根原样搬过去
                var roots = new GameObject[want];
                var shapes = new int[want];
                var stages = new int[want];
                var sway = new float[want];
                for (int i = 0; i < want; i++)
                {
                    roots[i] = _progPlotRoots != null && i < _progPlotRoots.Length ? _progPlotRoots[i] : null;
                    shapes[i] = _progPlotShape != null && i < _progPlotShape.Length ? _progPlotShape[i] : -1;
                    stages[i] = _progPlotStage != null && i < _progPlotStage.Length ? _progPlotStage[i] : -1;
                    sway[i] = _progSwayPhase != null && i < _progSwayPhase.Length ? _progSwayPhase[i] : 0f;
                }
                _progPlotRoots = roots;
                _progPlotShape = shapes;
                _progPlotStage = stages;
                _progSwayPhase = sway;
            }

            // 成熟段的基准尺寸：与 `SyncPlots` 里 fbx 那条路**同一口径**（tile × 0.72 高）
            float tile = grid.TileSize;
            _progMatureHeight = tile * 0.72f;   // v255：只作统一曲线的量纲，不再直接当缩放用

            for (int i = 0; i < want; i++)
            {
                FarmPlot plot = grid.Slots[i];
                bool live = plot != null && (plot.State == PlotState.Growing || plot.State == PlotState.Grown);
                int shape = live ? ProgShapeOf(plot.CropSeedId) : -1;

                if (shape < 0)
                {
                    if (_progPlotRoots[i] != null && _progPlotRoots[i].activeSelf) _progPlotRoots[i].SetActive(false);
                    _progPlotShape[i] = -1;
                    _progPlotStage[i] = -1;
                    continue;
                }

                if (_progPlotRoots[i] == null || _progPlotShape[i] != shape)
                {
                    if (_progPlotRoots[i] != null) Object.Destroy(_progPlotRoots[i]);
                    _progPlotRoots[i] = BuildProgPlant(shape, i, tile);
                    _progPlotShape[i] = shape;
                    _progPlotStage[i] = -1;
                }

                // 占位方块（`SyncPlots` 每帧会把它打开）必须压下去，否则一个方块套着一株草
                if (_plotTrees != null && i < _plotTrees.Length && _plotTrees[i] != null && _plotTrees[i].activeSelf)
                    _plotTrees[i].SetActive(false);

                GameObject root = _progPlotRoots[i];
                if (!root.activeSelf) root.SetActive(true);

                // ── 阶段：按 `GrowthProgress01` 选档，只在**变化的那一帧**切 SetActive ──
                float gp = Mathf.Clamp01(plot.GrowthProgress01);
                int stage = ProgStageOf01(gp);
                if (_progPlotStage[i] != stage)
                {
                    for (int k = 0; k < ProgStageCount; k++)
                    {
                        Transform ch = root.transform.GetChild(k);
                        bool on = k == stage;
                        if (ch.gameObject.activeSelf != on) ch.gameObject.SetActive(on);
                    }
                    _progPlotStage[i] = stage;
                }

                // ── v255：**按目标高度归一化**（包围盒归一化），不再按常量顶点高拉伸 ──
                //
                //   目标高 = 统一曲线 `CropHeightFrac01(gp)` × 成熟基准高 × 类别带
                //   实测高 = 该段零件包围盒的顶点（`ProgGroupExtentM`）
                //   缩放   = 目标 / 实测
                //   ⇒ 不管这一段的零件原本多高、类别乘子叠了多少，**出来都是同一个高** ✓
                //   ⇒ 段与段之间不需要再做"起长 = 上一段顶点"的对齐：
                //     两段读的是**同一个 gp 的同一个目标高**，交界处必然相等（C0 连续）✓
                ProgShapeClass cls = ProgClassOf(shape);
                Transform seg = root.transform.GetChild(stage);
                float wantH = CropHeightFrac01(gp) * _progMatureHeight * ProgClassBand(cls);

                if (stage == 3)
                {
                    float gTop = (_progMatureGeomTop != null && shape >= 0 && shape < _progMatureGeomTop.Length)
                        ? _progMatureGeomTop[shape] : 0f;
                    float gHalf = (_progMatureGeomHalf != null && shape >= 0 && shape < _progMatureGeomHalf.Length)
                        ? _progMatureGeomHalf[shape] : 0f;
                    if (gTop <= 0.001f) gTop = ProgStageTopM[2];          // 量不到就退回常量，绝不除 0
                    float ky = wantH / gTop;                              // 纵向：高度**精确**等于目标
                    // 横向：等比跟随，但**不让宽型品种溢出格子**
                    // （葵/瓜/莲的水平半径 ≈ 1.05，等比会到 2.5 米开外；旧代码 x/z 固定 1.55 也溢，
                    //   这里顺带收一格：上限 = 半格 × 0.52 ≈ 1.30 米半径）
                    float halfCap = Mathf.Max(0.20f, tile * 0.52f);
                    float kxz = gHalf > 0.001f ? Mathf.Min(ky, halfCap / gHalf) : ky;
                    seg.localScale = new Vector3(kxz, ky, kxz);
                }
                else
                {
                    float gTop = ProgGeomTopCache[(int)cls, stage];
                    if (gTop <= 0.001f) gTop = ProgStageTopM[stage];
                    float k = wantH / gTop;
                    seg.localScale = new Vector3(k, k, k);
                }

                // ── 摆位 + 轻微摇摆（±3°，仅视觉；只在根上转，不动任何零件）──
                Transform tr = root.transform;
                tr.position = new Vector3(plot.World.x, 0f, plot.World.y);
                tr.rotation = Quaternion.Euler(0f, 0f, 3f * Mathf.Sin(_progSwayPhase[i]));
                _progSwayPhase[i] += 1.9f * dt;
            }
        }

        /// <summary>
        /// 第 `shape` 号植株的**四个生长阶段**：一次建齐（各段先 `SetActive(false)`），
        /// 之后由 `SyncProgPlants` 按进度切换 ⇒ 运行期**不建网格、不销毁重建** ✓
        /// 子物体顺序固定为 `ProgStage0..3`，`SyncProgPlants` 用 `GetChild(stage)` 取 ✓
        /// 根物体在局部 y = 0（贴地）。
        /// </summary>
        private GameObject BuildProgPlant(int shape, int plotIndex, float tile)
        {
            var root = new GameObject("ProgPlant_" + shape + "_" + plotIndex);
            root.transform.SetParent(_root, false);
            root.transform.localPosition = Vector3.zero;

            // ★ v253：阶段 0~2 —— 幼苗 / 小苗 / 抽穗（按**形状类别**做，同类别品种共用一套）
            ProgShapeClass cls = ProgClassOf(shape);
            for (int st = 0; st < 3; st++)
            {
                var g = new GameObject("ProgStage" + st);
                g.transform.SetParent(root.transform, false);
                BuildProgSproutStage(g, cls, st);
                // v255：建成后量一次几何顶点高（同类别同阶段全场共用一份网格 ⇒ 只量一次）
                if (ProgGeomTopCache[(int)cls, st] <= 0.001f)
                {
                    ProgGroupExtentM(g, out float gTop, out float _);
                    ProgGeomTopCache[(int)cls, st] = gTop;
                }
                g.SetActive(false);
            }

            // ★ v253：阶段 3 —— 成熟体 = 该品种原有外形（原 switch 整体搬进 `ProgStage3`）
            var mature = new GameObject("ProgStage3");
            mature.transform.SetParent(root.transform, false);
            AddProgMatureParts(mature, shape);
            // v255：成熟体按**品种**量包围盒 —— 15 株的本地顶点高差到 6 倍（0.26~1.61 m，
            //       日光葵的竖起的环 1.61 / 辉苔的苔垫 0.26），归一化系数必须逐品种各算一个 ✓
            if (shape >= 0 && shape < ProgSeedIds.Length)
            {
                if (_progMatureGeomTop == null || _progMatureGeomTop.Length != ProgSeedIds.Length)
                {
                    _progMatureGeomTop = new float[ProgSeedIds.Length];
                    _progMatureGeomHalf = new float[ProgSeedIds.Length];
                }
                if (_progMatureGeomTop[shape] <= 0.001f)
                {
                    ProgGroupExtentM(mature, out float mTop, out float mHalf);
                    _progMatureGeomTop[shape] = mTop;
                    _progMatureGeomHalf[shape] = mHalf;
                }
            }
            mature.SetActive(false);

            return root;
        }

        /// <summary>
        /// v253：**阶段 0~2** 的程序化外形 —— 只按「形状类别 × 阶段」建，
        /// 所以 15 株只有 4 类 × 3 段 = 12 套零件，且网格全部来自既有工厂缓存。
        ///
        /// ★ 几何硬指标（与 `ProgStageTopM` 一一对应，单位米）★
        ///   阶段 0：茎 0.105 / 叶 2 片（半长 0.045，外张 52°）→ 顶点 0.126  ✓ [0.10, 0.16]
        ///   阶段 1：茎 0.285 / 叶 5 片（半长 0.070，外张 60°）→ 顶点 0.323  ✓ [0.28, 0.38]
        ///   阶段 2：茎 0.455 / 叶 7 片（半长 0.090，外张 64°）+ 顶端装饰
        ///           穗类 → 锥穗（顶 0.565）｜瓜果 → 幼果球（顶 0.557）
        ///           叶菜 → 花苞（顶 0.520）｜藤类 → 卷须环 + 小芽（顶 ≈ 0.53）  ✓ [0.45, 0.60]
        ///   类别只改**宽度/张角/叶片数**，不改高度档 ⇒ 三档身高对 15 株都成立 ✓
        /// </summary>
        private void BuildProgSproutStage(GameObject group, ProgShapeClass cls, int stage)
        {
            Color leaf = ProgLeafStageColor[stage];
            // 茎比叶深一档（同色相，只压明度）—— 沿用"幼苗更嫩、成熟更饱和"的同一条规则
            Color stem = Color.Lerp(leaf, new Color(0.30f, 0.50f, 0.24f), 0.45f);

            float widthK = cls == ProgShapeClass.Vine ? 0.80f
                         : (cls == ProgShapeClass.Fruiting ? 1.25f : 1f);
            float lenK = cls == ProgShapeClass.Spike ? 1.06f
                       : (cls == ProgShapeClass.Vine ? 0.88f : 1f);
            float stemLen = ProgStemLen[stage] * lenK;
            float stemR = ProgStemR[stage] * widthK;

            // 茎：棱台网格长轴在局部 +Z，`Euler(90,0,0)` 把它立到世界 +Y；长度靠 `localScale.z`
            // 键位 = 6200 + 阶段×8 + 类别 ⇒ 同一 (阶段,类别) 全场共用一份网格
            AddProgPart(group, FxFrustumMesh(6200 + stage * 8 + (int)cls, 6, stemR * 0.70f, stemR),
                new Vector3(0f, stemLen * 0.5f, 0f), new Vector3(1f, 1f, stemLen),
                new Vector3(90f, 0f, 0f), stem);

            // 叶：一张圆盘（半径 1）⇒ `localScale = (半宽, 1, 半长)`，绕 X 负角把叶尖抬起来再绕 Y 分向
            int n = ProgLeafN[stage];
            if (cls == ProgShapeClass.Vine || cls == ProgShapeClass.Fruiting) n = Mathf.Max(2, n - 1);
            float leafHalf = ProgLeafHalf[stage]
                           * (cls == ProgShapeClass.Leafy ? 1.20f
                            : cls == ProgShapeClass.Fruiting ? 1.35f
                            : cls == ProgShapeClass.Spike ? 0.62f : 1f);
            float halfW = leafHalf * 0.55f;
            float tilt = ProgLeafTiltDeg[stage];
            float tiltRad = tilt * Mathf.Deg2Rad;
            for (int k = 0; k < n; k++)
            {
                float yaw = k * (360f / n);
                float yawRad = yaw * Mathf.Deg2Rad;
                float y = Mathf.Lerp(stemLen * 0.30f, stemLen * 0.92f, n <= 1 ? 0.5f : k / (float)(n - 1));
                // 叶根贴着茎、叶身向外：沿朝向往外挪"半长 × cos(张角)"
                float out2 = leafHalf * Mathf.Cos(tiltRad);
                AddProgPart(group, FxDiscMesh(6240, 8),
                    new Vector3(Mathf.Sin(yawRad) * out2, y, Mathf.Cos(yawRad) * out2),
                    new Vector3(halfW, 1f, leafHalf),
                    new Vector3(-tilt, yaw, 0f), leaf);
            }

            // 顶端装饰：只有阶段 2 有（"出现花苞 / 穗"）—— 阶段 0/1 的差别是叶片数与高度
            if (stage != 2) return;
            Color accent = ProgAccentColor[(int)cls];
            switch (cls)
            {
                case ProgShapeClass.Spike:      // 麦穗/花头：一根窄尖锥，顶在茎尖上
                    AddProgPart(group, FxFrustumMesh(6260 + (int)cls, 6, 0.018f, 0.055f),
                        new Vector3(0f, stemLen + 0.045f, 0f), new Vector3(1f, 1f, 0.13f),
                        new Vector3(90f, 0f, 0f), accent);
                    break;
                case ProgShapeClass.Fruiting:   // 幼果：一颗小球，挂在茎尖下方
                    AddProgPart(group, FxIcoShellMesh(1),
                        new Vector3(0f, stemLen + 0.030f, 0f), new Vector3(0.072f, 0.072f, 0.072f),
                        Vector3.zero, accent);
                    break;
                case ProgShapeClass.Vine:       // 卷须：一圈细环 + 一颗小芽
                    AddProgPart(group, FxRingMesh(6270 + (int)cls, 10, 0.72f),
                        new Vector3(0f, stemLen + 0.015f, 0f), new Vector3(0.062f, 1f, 0.062f),
                        new Vector3(58f, 0f, 0f), accent);
                    AddProgPart(group, FxIcoShellMesh(1),
                        new Vector3(0f, stemLen + 0.055f, 0f), new Vector3(0.026f, 0.026f, 0.026f),
                        Vector3.zero, accent);
                    break;
                default:                        // 叶菜：一颗压扁的花苞
                    AddProgPart(group, FxIcoShellMesh(1, 0.70f),
                        new Vector3(0f, stemLen + 0.020f, 0f), new Vector3(0.065f, 0.065f, 0.065f),
                        Vector3.zero, accent);
                    break;
            }
        }

        /// <summary>
        /// 阶段 3（成熟体）：该品种的原有外形。**参数名仍是 `root`** ——
        /// 这一段是原 `BuildProgPlant` 的 switch 逐行搬过来的，一个零件的数都没有改 ✓
        /// </summary>
        private void AddProgMatureParts(GameObject root, int shape)
        {
            switch (shape)
            {
                case 0:   // 战镰花：两把交叉的刃（四棱台当刃身）
                    AddProgPart(root, FxFrustumMesh(6100, 4, 0.05f, 0.34f),
                        new Vector3(-0.10f, 0.42f, 0f), new Vector3(1.1f, 1f, 0.45f),
                        new Vector3(0f, -55f, 62f), new Color(0.96f, 0.72f, 0.30f));
                    AddProgPart(root, FxFrustumMesh(6101, 4, 0.05f, 0.30f),
                        new Vector3(0.12f, 0.42f, 0f), new Vector3(1.0f, 1f, 0.42f),
                        new Vector3(0f, 55f, -62f), new Color(0.99f, 0.55f, 0.42f));
                    AddProgPart(root, FxFrustumMesh(6102, 6, 0.10f, 0.09f),
                        new Vector3(0f, 0.16f, 0f), new Vector3(1f, 0.5f, 1f),
                        new Vector3(90f, 0f, 0f), new Color(0.36f, 0.55f, 0.28f));
                    break;

                case 1:   // 疾风藤：茎 + 叶盘
                    AddProgPart(root, FxFrustumMesh(6103, 6, 0.13f, 0.06f),
                        new Vector3(0f, 0.34f, 0f), new Vector3(1f, 1f, 0.9f),
                        new Vector3(90f, 0f, 0f), new Color(0.40f, 0.82f, 0.52f));
                    AddProgPart(root, FxDiscMesh(6104, 10),
                        new Vector3(0.12f, 0.62f, 0f), new Vector3(0.62f, 1f, 0.62f),
                        new Vector3(0f, 0f, 24f), new Color(0.66f, 0.95f, 0.72f));
                    break;

                case 2:   // 日光葵：扁球花盘 + 外圈花瓣（同一份环网格）
                    AddProgPart(root, FxIcoShellMesh(1, 0.55f),
                        new Vector3(0f, 0.56f, 0f), new Vector3(0.52f, 1f, 0.52f),
                        Vector3.zero, new Color(1.00f, 0.80f, 0.22f));
                    AddProgPart(root, FxRingMesh(6105, 12, 0.62f),
                        new Vector3(0f, 0.56f, 0f), new Vector3(1.05f, 1f, 1.05f),
                        new Vector3(90f, 0f, 0f), new Color(1.00f, 0.62f, 0.14f));
                    AddProgPart(root, FxFrustumMesh(6106, 6, 0.09f, 0.07f),
                        new Vector3(0f, 0.22f, 0f), new Vector3(1f, 1f, 0.8f),
                        new Vector3(90f, 0f, 0f), new Color(0.38f, 0.60f, 0.26f));
                    break;

                case 3:   // 雨语莲：宽浅水盆 + 水珠
                    AddProgPart(root, FxDiscMesh(6107, 12),
                        new Vector3(0f, 0.18f, 0f), new Vector3(1.05f, 1f, 1.05f),
                        Vector3.zero, new Color(0.32f, 0.62f, 0.86f));
                    AddProgPart(root, FxIcoShellMesh(1),
                        new Vector3(0.16f, 0.40f, 0.06f), new Vector3(0.20f, 0.34f, 0.20f),
                        Vector3.zero, new Color(0.70f, 0.90f, 1.00f));
                    break;

                case 4:   // 庇护藤：编成弧顶的藤罩（压扁的球壳）
                    AddProgPart(root, FxIcoShellMesh(1, 0.62f),
                        new Vector3(0f, 0.30f, 0f), new Vector3(0.78f, 1f, 0.78f),
                        Vector3.zero, new Color(0.46f, 0.78f, 0.44f));
                    AddProgPart(root, FxRingMesh(6108, 12, 0.80f),
                        new Vector3(0f, 0.06f, 0f), new Vector3(1.0f, 1f, 1.0f),
                        new Vector3(90f, 0f, 0f), new Color(0.72f, 0.94f, 0.58f));
                    break;

                case 5:   // 四叶王：四片叶 + 一根主轴
                    {
                        AddProgPart(root, FxFrustumMesh(6109, 6, 0.07f, 0.06f),
                            new Vector3(0f, 0.28f, 0f), new Vector3(1f, 1f, 0.8f),
                            new Vector3(90f, 0f, 0f), new Color(0.36f, 0.66f, 0.34f));
                        float[] yaw = { 0f, 90f, 180f, 270f };
                        for (int k = 0; k < 4; k++)
                        {
                            GameObject leaf = AddProgPart(root, FxDiscMesh(6110 + k, 8),
                                new Vector3(0f, 0.56f, 0f), new Vector3(0.46f, 1f, 0.46f),
                                new Vector3(0f, yaw[k], 0f), new Color(0.56f, 0.90f, 0.42f));
                            // 四片叶各往外挪一点 ⇒ 读成"四叶草"而不是"一个盘"
                            leaf.transform.localPosition = new Vector3(
                                Mathf.Sin(yaw[k] * Mathf.Deg2Rad) * 0.28f, 0.56f,
                                Mathf.Cos(yaw[k] * Mathf.Deg2Rad) * 0.28f);
                            leaf.transform.localRotation = Quaternion.Euler(90f, yaw[k], 0f);
                        }
                    }
                    break;

                // ══════════ ★ v252：6 株新植物（无机制）══════════

                case 6:   // 薄荷苗：一根细茎 + 三片竖立的叶
                    {
                        AddProgPart(root, FxFrustumMesh(6120, 6, 0.055f, 0.045f),
                            new Vector3(0f, 0.30f, 0f), new Vector3(1f, 1f, 0.9f),
                            new Vector3(90f, 0f, 0f), new Color(0.34f, 0.62f, 0.30f));
                        for (int k = 0; k < 3; k++)
                        {
                            float a = k * 120f;
                            GameObject leaf = AddProgPart(root, FxDiscMesh(6121 + k, 8),
                                new Vector3(0f, 0.56f, 0f), new Vector3(0.34f, 1f, 0.20f),
                                new Vector3(0f, a, 68f), new Color(0.50f, 0.90f, 0.52f));
                            leaf.transform.localPosition = new Vector3(
                                Mathf.Sin(a * Mathf.Deg2Rad) * 0.16f, 0.56f,
                                Mathf.Cos(a * Mathf.Deg2Rad) * 0.16f);
                        }
                    }
                    break;

                case 7:   // 灰烬椒：一颗下尖的锥椒 + 蒂 + 两片碎叶
                    {
                        AddProgPart(root, FxFrustumMesh(6124, 6, 0.10f, 0.24f),
                            new Vector3(0f, 0.34f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(180f, 0f, 0f), new Color(0.82f, 0.28f, 0.20f));
                        AddProgPart(root, FxFrustumMesh(6125, 6, 0.13f, 0.07f),
                            new Vector3(0f, 0.58f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.30f, 0.52f, 0.24f));
                        AddProgPart(root, FxDiscMesh(6126, 7),
                            new Vector3(0.14f, 0.60f, 0.05f), new Vector3(0.30f, 1f, 0.30f),
                            new Vector3(0f, 24f, 52f), new Color(0.44f, 0.68f, 0.30f));
                    }
                    break;

                case 8:   // 琉璃瓜：一个大球瓜 + 一圈箍 + 蒂
                    {
                        AddProgPart(root, FxIcoShellMesh(1, 0.88f),
                            new Vector3(0f, 0.30f, 0f), new Vector3(0.86f, 1f, 0.86f),
                            Vector3.zero, new Color(0.52f, 0.80f, 0.62f));
                        AddProgPart(root, FxRingMesh(6127, 14, 0.42f),
                            new Vector3(0f, 0.30f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.86f, 0.94f, 0.70f));
                        AddProgPart(root, FxFrustumMesh(6128, 5, 0.06f, 0.05f),
                            new Vector3(0f, 0.62f, 0f), new Vector3(1f, 0.6f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.34f, 0.50f, 0.26f));
                    }
                    break;

                case 9:   // 霜纹芦：一根细高柱 + 尖端 + 两片斜鳍
                    {
                        AddProgPart(root, FxFrustumMesh(6129, 5, 0.045f, 0.035f),
                            new Vector3(0f, 0.46f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.62f, 0.78f, 0.86f));
                        AddProgPart(root, FxFrustumMesh(6130, 4, 0.03f, 0.06f),
                            new Vector3(0f, 0.94f, 0f), new Vector3(1f, 1f, 1f),
                            Vector3.zero, new Color(0.86f, 0.96f, 1.00f));
                        for (int k = 0; k < 2; k++)
                        {
                            AddProgPart(root, FxDiscMesh(6131 + k, 6),
                                new Vector3(k == 0 ? -0.10f : 0.10f, 0.62f, 0f),
                                new Vector3(0.30f, 1f, 0.12f),
                                new Vector3(0f, k == 0 ? -40f : 40f, k == 0 ? 46f : -46f),
                                new Color(0.74f, 0.88f, 0.94f));
                        }
                    }
                    break;

                // ══════════ ★ v252 第三批：5 株新植物（无机制）══════════

                case 10:  // 辉苔：低矮苔垫 + 三颗亮点
                    {
                        AddProgPart(root, FxIcoShellMesh(1, 0.28f),
                            new Vector3(0f, 0.10f, 0f), new Vector3(0.72f, 1f, 0.72f),
                            Vector3.zero, new Color(0.42f, 0.78f, 0.56f));
                        for (int k = 0; k < 3; k++)
                        {
                            float a = k * 120f;
                            AddProgPart(root, FxIcoShellMesh(1),
                                new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.22f, 0.20f,
                                            Mathf.Cos(a * Mathf.Deg2Rad) * 0.22f),
                                new Vector3(0.06f, 0.06f, 0.06f), Vector3.zero,
                                new Color(0.86f, 1.00f, 0.72f));
                        }
                    }
                    break;

                case 11:  // 盐霜瓜：一颗瓜 + 一圈盐霜环
                    {
                        AddProgPart(root, FxIcoShellMesh(1, 0.82f),
                            new Vector3(0f, 0.28f, 0f), new Vector3(0.78f, 1f, 0.78f),
                            Vector3.zero, new Color(0.72f, 0.86f, 0.62f));
                        AddProgPart(root, FxRingMesh(6141, 14, 0.46f),
                            new Vector3(0f, 0.28f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.94f, 0.96f, 0.88f));
                        AddProgPart(root, FxFrustumMesh(6142, 5, 0.05f, 0.04f),
                            new Vector3(0f, 0.58f, 0f), new Vector3(1f, 0.6f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.40f, 0.54f, 0.30f));
                    }
                    break;

                case 12:  // 云兰：三片竖瓣 + 一朵云团
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            float a = k * 120f;
                            GameObject petal = AddProgPart(root, FxDiscMesh(6143 + k, 6),
                                new Vector3(0f, 0.52f, 0f), new Vector3(0.18f, 1f, 0.40f),
                                new Vector3(72f, a, 0f), new Color(0.86f, 0.92f, 1.00f));
                            petal.transform.localPosition = new Vector3(
                                Mathf.Sin(a * Mathf.Deg2Rad) * 0.16f, 0.52f,
                                Mathf.Cos(a * Mathf.Deg2Rad) * 0.16f);
                        }
                        AddProgPart(root, FxIcoShellMesh(1),
                            new Vector3(0f, 0.72f, 0f), new Vector3(0.26f, 1f, 0.26f),
                            Vector3.zero, new Color(0.96f, 0.98f, 1.00f));
                    }
                    break;

                case 13:  // 镜花：一片镜盘 + 一块斜插的碎片
                    {
                        AddProgPart(root, FxDiscMesh(6146, 12),
                            new Vector3(0f, 0.44f, 0f), new Vector3(0.62f, 1f, 0.62f),
                            new Vector3(74f, 0f, 0f), new Color(0.90f, 0.92f, 0.98f));
                        AddProgPart(root, FxFrustumMesh(6147, 4, 0.04f, 0.16f),
                            new Vector3(0.16f, 0.52f, 0.06f), new Vector3(1f, 1f, 0.18f),
                            new Vector3(0f, 30f, -58f), new Color(0.74f, 0.80f, 0.92f));
                        AddProgPart(root, FxFrustumMesh(6148, 6, 0.05f, 0.04f),
                            new Vector3(0f, 0.22f, 0f), new Vector3(1f, 1f, 0.8f),
                            new Vector3(90f, 0f, 0f), new Color(0.38f, 0.52f, 0.34f));
                    }
                    break;

                default:  // 14 烬苇：一根细苇 + 顶端余烬（也是兜底形状）
                    {
                        AddProgPart(root, FxFrustumMesh(6149, 5, 0.04f, 0.03f),
                            new Vector3(0f, 0.50f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.66f, 0.50f, 0.34f));
                        AddProgPart(root, FxIcoShellMesh(1),
                            new Vector3(0f, 1.00f, 0f), new Vector3(0.12f, 0.16f, 0.12f),
                            Vector3.zero, new Color(1.00f, 0.58f, 0.22f));
                        AddProgPart(root, FxRingMesh(6150, 10, 0.20f),
                            new Vector3(0f, 0.30f, 0f), new Vector3(1f, 1f, 1f),
                            new Vector3(90f, 0f, 0f), new Color(0.78f, 0.64f, 0.40f));
                    }
                    break;

            }
        }

        /// <summary>给植株加一个零件（网格/材质都来自缓存 ⇒ 不新建资源）。</summary>
        private GameObject AddProgPart(GameObject root, Mesh mesh, Vector3 localPos, Vector3 localScale,
                                       Vector3 euler, Color color)
        {
            // `MeshObj` 会挂到 `_root` 下；这里要挂到植株根下 ⇒ 建完再改父级
            GameObject go = MeshObj(mesh, MatCached(color, true), "ProgPart");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = Quaternion.Euler(euler);
            return go;
        }

        // ────────────────────────── 胡萝卜（道具 1） ──────────────────────────

        /// <summary>
        /// 胡萝卜环绕：**拥有几件就转几根**（封顶 3），按 360°/n 均分 = 等边三角形；
        /// 位置由逻辑层 `ProgMechanics.CarrotPos` 给出（表现层不自己算角度，
        /// 否则"挡弹的位置"与"画出来的位置"会差一个角速度 ✗）✓
        /// </summary>
        private void SyncCarrots(RunSession s, float dt)
        {
            PlayerActor p = s.World.Player;
            if (p == null) return;
            int count = ProgMechanics.CarrotCount(ProgMechanics.ItemPanel.Of(s).CarrotStacks);

            if (_progCarrots == null)
            {
                _progCarrots = new GameObject[ProgMechanics.MaxCarrots];
                for (int i = 0; i < _progCarrots.Length; i++)
                {
                    var body = MeshObj(FxIcoShellMesh(1), MatCached(new Color(1f, 0.55f, 0.16f), true), "CarrotBody");
                    MeshObj(FxFrustumMesh(6120 + i, 4, 0.06f, 0.02f),
                            MatCached(new Color(0.42f, 0.85f, 0.40f), true), "CarrotLeaf")
                        .transform.SetParent(body.transform, false);
                    var leaf = body.transform.Find("CarrotLeaf");
                    if (leaf != null)
                    {
                        leaf.localPosition = new Vector3(0f, 0.20f, 0f);
                        leaf.localScale = new Vector3(1f, 1f, 0.9f);
                        leaf.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    }
                    body.SetActive(false);
                    _progCarrots[i] = body;
                }
            }

            for (int i = 0; i < _progCarrots.Length; i++)
            {
                GameObject go = _progCarrots[i];
                if (go == null) continue;
                bool on = i < count;
                if (go.activeSelf != on) go.SetActive(on);
                if (!on) continue;
                Vector2 cp = ProgMechanics.CarrotPos(p.Pos, i, count);
                go.transform.position = new Vector3(cp.x, 0.85f, cp.y);
                go.transform.localScale = new Vector3(0.34f, 0.44f, 0.34f);
                go.transform.rotation = Quaternion.Euler(0f, (ProgMechanics.CarrotSpinDegPerSec * Time.time) % 360f, 12f);
            }
        }

        // ────────────────────────── 兔子（道具 2） ──────────────────────────

        private void SyncRabbit(RunSession s, float dt)
        {
            if (_progRabbit == null)
            {
                _progRabbit = MeshObj(FxIcoShellMesh(1), MatCached(new Color(0.96f, 0.95f, 0.92f), true), "BunnyBody");
                for (int k = 0; k < 2; k++)
                {
                    GameObject ear = MeshObj(FxFrustumMesh(6130 + k, 4, 0.075f, 0.02f),
                        MatCached(new Color(1f, 0.86f, 0.88f), true), "BunnyEar" + k);
                    ear.transform.SetParent(_progRabbit.transform, false);
                    ear.transform.localPosition = new Vector3(k == 0 ? -0.09f : 0.09f, 0.30f, 0f);
                    ear.transform.localScale = new Vector3(1f, 1f, 1.1f);
                    ear.transform.localRotation = Quaternion.Euler(k == 0 ? -8f : 8f, 0f, 90f);
                }
                _progRabbit.SetActive(false);
            }

            bool on = ProgMechanics.RabbitActive;
            if (_progRabbit.activeSelf != on) _progRabbit.SetActive(on);
            if (!on) return;
            Vector2 rp = ProgMechanics.RabbitPos;
            _progRabbit.transform.position = new Vector3(rp.x, 0.42f, rp.y);
            // 跑动时轻微上下跳（只用位移，不改 alpha）
            float hop = 1f + 0.06f * Mathf.Sin(Time.time * 12f);
            _progRabbit.transform.localScale = new Vector3(0.40f, 0.40f * hop, 0.46f);
        }

        // ────────────────────────── 盾牌脉冲（道具 7） ──────────────────────────

        /// <summary>
        /// 格挡触发后的一圈扩散盾环：**只靠缩放消失**（不逐实例改 alpha）✓
        /// 环的半径从 0.5 涨到 2.2，在 `ProgMechanics.BlockShieldPulse`（0.45s）里走完。
        /// </summary>
        private void SyncShieldRing(RunSession s, float dt)
        {
            if (_progShieldRing == null)
            {
                _progShieldRing = MeshObj(FxRingMesh(6140, 16, 0.86f),
                    MatCached(new Color(0.55f, 0.85f, 1f), true), "ShieldPulse");
                _progShieldRing.SetActive(false);
            }
            PlayerActor p = s.World.Player;
            float t = ProgMechanics.BlockShieldPulse;
            bool on = p != null && t > 0f;
            if (_progShieldRing.activeSelf != on) _progShieldRing.SetActive(on);
            if (!on) return;
            float k = Mathf.Clamp01(1f - t / 0.45f);                 // 0 → 1
            float r = Mathf.Lerp(0.55f, 2.20f, k);
            _progShieldRing.transform.position = new Vector3(p.Pos.x, 0.10f, p.Pos.y);
            _progShieldRing.transform.localScale = new Vector3(r, 1f, r);
            // 厚度随扩散变薄（只改缩放 ⇒ 不动 alpha）
            _progShieldRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // ────────────────────────── 天气（植物 4 / 5） ──────────────────────────

        // ── ★★ v308：常驻雨幕的参数（一个 ParticleSystem 全包）★★ ──
        //   ★ 旧版那四个常量（`ProgRainCount` 84 / `ProgRainTop` 13 / `ProgRainSpeed` 17 /
        //     `ProgRainLen` 2.4）**已随"一波 84 条定长池"一起删除**：它们的量纲是
        //     "手算每条雨丝的高度与长度"，而粒子系统自己管这两件事 ⇒ 留着只会误导下一次改动 ✓
        /// <summary>粒子上限（用户规格 200~400 ⇒ 取 **380**，给"风大时看着更密"留余量）。</summary>
        private const int RainMaxParticles = 380;
        /// <summary>每秒发射数：380 ÷ 1.05s 寿命 ≈ **362/s** 稳态满池（取 400/s 让池子保持填满）。</summary>
        private const float RainEmitRate = 400f;
        /// <summary>粒子寿命（秒）—— 决定单条雨丝能落多长（速度 × 寿命）。</summary>
        private const float RainParticleLife = 1.05f;
        /// <summary>下落速度（米/秒）。</summary>
        private const float RainFallSpeed = 16f;
        /// <summary>横向风（米/秒）—— 雨丝**斜向下**的来源（不是竖直的）。</summary>
        private const float RainWind = 2.6f;
        /// <summary>发射盒的宽 / 深（米）：跟着镜头走的一片"雨板"，比画面宽一圈。</summary>
        private const float RainBoxW = 26f;
        private const float RainBoxD = 26f;
        /// <summary>雨幕的整体 α（灰蓝、半透明）。</summary>
        private const float RainAlpha = 0.55f;
        /// <summary>雨停后的淡出时长（秒）。</summary>
        private const float RainFadeOutSeconds = 0.35f;
        /// <summary>拉伸渲染的"速度 → 长度"倍率：速度 16 × 0.10 = **1.6 米**长的雨线。</summary>
        private const float RainStretchScale = 0.10f;
        /// <summary>雨丝宽度（米）：细。</summary>
        private const float RainWidth = 0.045f;
        /// <summary>雨的颜色（偏灰蓝）。</summary>
        private static readonly Color RainColor = new Color(0.62f, 0.72f, 0.86f, 1f);
        /// <summary>代码生成雨丝贴图的尺寸（宽 × 高）。</summary>
        private const int RainTexW = 16, RainTexH = 64;
        /// <summary>雨幕材质（静态缓存：整局一份，**不**随 `Clear()` 销毁 —— 与贴图同一条纪律）。</summary>
        private static Material _rainMat;
        private static Texture2D _rainTex;

        /// <summary>
        /// **大晴天**：场上空一颗发光的太阳 + 一盏真灯光（"发光照亮"）；屏幕上的太阳本身由
        /// `CombatSunDisc` 负责（柔光幕 / 星芒 / 光线脉冲 / 遮挡降 50%）。
        /// **雨天**：★ v308：**常驻雨幕**（一个 `ParticleSystem`，1 个渲染体，380 条上限，
        /// 斜向下、偏灰蓝、雨停后 0.35s 淡出），并把环境光压暗一档。
        ///
        /// ★★ v308 推翻的两条 v198 结论（用户原话：「雨天增加常驻下雨效果」）★★
        /// ① v198 的"**只落一波**"（84 条从 13m 落一次就 `SetActive(false)`、绝不回卷）
        ///    是为了修当时"来回循环、看着假"—— 但代价是 30 秒的雨天里 0.76 秒之后天空就干净了 ✗
        ///    现在改成**引擎自己循环的粒子**：一直在吐新粒子、旧的按寿命回收 ⇒ 真正的常驻，
        ///    而且**没有逐条 transform 手算**（这正是"常驻"最便宜的写法）✓
        /// ② v198 的"**竖向、垂直地面**"也被推翻：现在雨丝**斜向下**（受 `RainWind` 2.6 m/s 的风），
        ///    因为"风吹着雨"才是下雨该有的样子；斜率由 `velocityOverLifetime` 决定，
        ///    由 `Stretch` 渲染模式沿速度方向拉长 ⇒ 不需要给每条雨丝摆旋转 ✓
        ///
        /// ★ 环境变暗为什么改成写 `NightLighting.AmbientDimMultiplier` ★
        ///   "全局下雨（环境变暗）"是**整场**的读法，只有环境光这一处能一次改到全部物体。
        ///   上一版**直接写** `RenderSettings.ambientLight`，而 `NightLighting.Apply`
        ///   每帧也写同一个值 ⇒ 两个写者抢一个旋钮，谁后写谁赢
        ///   ⇒ 实测雨幕出来了、"环境变暗"**没生效** ✗
        ///   现在改成给唯一的写者一个**倍率入口**（它原有逻辑一个数没改），
        ///   由它去乘 —— 一个旋钮只有一个写者 ✓
        /// </summary>
        /// <summary>
        /// ★ v308：**建一次雨幕**（一个 `ParticleSystem`，1 个渲染体）。
        ///
        /// 参数都写在常量段（`RainMaxParticles` / `RainEmitRate` / `RainFallSpeed` / `RainWind` …），
        /// 这里只负责把模块逐个填好：
        ///   · `main`：世界空间（粒子留在世界上，镜头动了雨不会跟着晃）、寿命 1.05s、380 上限
        ///   · `emission`：初始 0（由 `SyncWeatherFx` 在雨天里设成 `RainEmitRate`）
        ///   · `shape`：`Box` 26×0.2×26 —— 跟着镜头走的一片"雨板"
        ///   · `velocityOverLifetime`（世界空间）：`y = −16`（下落）+ `x = +2.6`（风）⇒ **斜向下**
        ///   · `renderer`：`Stretch` 模式 + `velocityScale` ⇒ 沿速度方向拉成**细长斜线**
        /// 全程**只建一次**：物件挂在 `_root` 下（`Clear()` 时随根一起销毁），
        /// 材质与贴图是静态缓存（不销毁，下一局直接复用）✓
        /// </summary>
        private void EnsureRainCurtain()
        {
            if (_progRainGo != null) return;

            var go = new GameObject("FX_RainCurtain");
            go.transform.SetParent(_root, false);
            go.transform.position = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            // 先停掉：模块在"运行中"改会有一些值被忽略（Unity 的既有行为）
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.prewarm = true;                       // 一开播就"已经在下"（不是从零慢慢积起来）
            main.startLifetime = RainParticleLife;
            main.startSpeed = 0f;                      // 速度全交给 velocityOverLifetime（世界空间）
            main.startSize = RainWidth;
            main.startRotation = 0f;
            main.maxParticles = RainMaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new Color(1f, 1f, 1f, 1f);   // 颜色/α 全由材质给（一份材质控全场）

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;                      // 只在雨天由 SyncWeatherFx 拉起来

            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(RainBoxW, 0.2f, RainBoxD);

            // 速度 = 下落 + 风（世界空间常量曲线 ⇒ 每条雨丝的斜率完全一致，读作"一片雨幕"）
            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = new ParticleSystem.MinMaxCurve(RainWind);
            vol.y = new ParticleSystem.MinMaxCurve(-RainFallSpeed);
            vol.z = new ParticleSystem.MinMaxCurve(0f);

            // 关掉用不到的重模块（省 CPU，也避免上一版模块默认值干扰）
            var col = ps.colorOverLifetime;
            col.enabled = false;
            var sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = false;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                rend.renderMode = ParticleSystemRenderMode.Stretch;
                rend.velocityScale = RainStretchScale;   // 16 m/s × 0.10 ⇒ 1.6 米长的雨线
                rend.lengthScale = 2f;
                rend.cameraVelocityScale = 0f;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;
                rend.sharedMaterial = RainMaterial();    // 用 sharedMaterial：不实例化、不泄漏
                rend.alignment = ParticleSystemRenderSpace.View;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _progRainGo = go;
            _progRainPs = ps;
            _progRainRend = rend;
        }

        /// <summary>整体不透明度：只改我们**自己那 1 份**材质的颜色（不碰粒子、不逐条设色）。</summary>
        private static void SetRainAlpha(float a)
        {
            if (_rainMat == null) return;
            Color c = new Color(RainColor.r, RainColor.g, RainColor.b, Mathf.Clamp01(a));
            // `_Color`（内置 / 旧粒子）与 `_BaseColor`（URP）各探一次 ⇒ 换 shader 不丢控制
            if (_rainMat.HasProperty("_Color")) _rainMat.SetColor("_Color", c);
            if (_rainMat.HasProperty("_BaseColor")) _rainMat.SetColor("_BaseColor", c);
        }

        /// <summary>
        /// ★ v308：雨幕材质（**整局一份**，静态缓存）。兜底链与 `CombatSunDisc.BuildMat` 同一套：
        /// `URP Particles/Unlit` → `URP Unlit` → `Sprites/Default` → 旧版粒子 ⇒ 任何一条在都能画 ✓
        /// 贴图同时挂到 `_MainTex` 与 `_BaseMap`（两边都先 `HasProperty` 探一遍）。
        /// </summary>
        private static Material RainMaterial()
        {
            if (_rainMat != null) return _rainMat;
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) return null;                 // 极端兜底：没有可用的透明 shader ⇒ 不画雨（不崩）

            var m = new Material(sh) { name = "M_ProgRainCurtain" };
            Texture2D tex = RainTexture();
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            Color c = new Color(RainColor.r, RainColor.g, RainColor.b, 0f);   // 开局透明（雨天再拉起）
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);        // URP：Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);            // URP：Alpha
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            _rainMat = m;
            return _rainMat;
        }

        /// <summary>
        /// ★ v308：雨丝贴图（**代码生成 16×64，白 + α**，与太阳那三张同一条纪律）。
        /// 形状：一条**竖直的软条** —— 横向 `exp(−(2x)²)` 收边（细）、纵向两端渐隐（不硬切头尾）。
        /// 拉长方向由 `Stretch` 渲染模式负责，所以贴图本身只需要"细 + 软" ✓
        /// </summary>
        private static Texture2D RainTexture()
        {
            if (_rainTex != null) return _rainTex;
            int w = RainTexW, h = RainTexH;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;                       // 0..1 沿长度
                float ends = Mathf.Sin(v * Mathf.PI);           // 两端 0、中间 1（软头尾）
                for (int x = 0; x < w; x++)
                {
                    float u = ((x + 0.5f) / w) * 2f - 1f;       // −1..1 横向
                    float core = Mathf.Exp(-(u * 4f) * (u * 4f));  // 细亮芯
                    float a = Mathf.Clamp01(core * Mathf.Pow(ends, 0.6f));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "T_ProgRainStreak",
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _rainTex = tex;
            return _rainTex;
        }

        private void SyncWeatherFx(RunSession s, float dt)
        {
            WeatherKind kind = ProgMechanics.Weather;
            float strength = ProgMechanics.WeatherIntensity01;

            // ── 太阳 ──
            if (_progSun == null)
            {
                _progSun = MeshObj(FxIcoShellMesh(1), MatCached(new Color(1f, 0.90f, 0.35f), true), "ProgSun");
                // ★ 关掉投影：一个悬在场上空的球会在草地上糊一大块黑影（实测抓图里那块深色椭圆
                //   就是它的影子）⇒ 太阳要"发光"，不要"挡光" ✓
                var sunRend = _progSun.GetComponent<MeshRenderer>();
                if (sunRend != null) sunRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var lightGo = new GameObject("ProgSunLight");
                lightGo.transform.SetParent(_progSun.transform, false);
                _progSunLight = lightGo.AddComponent<Light>();
                _progSunLight.type = LightType.Point;
                _progSunLight.color = new Color(1f, 0.88f, 0.55f);
                _progSunLight.range = 46f;
                _progSunLight.intensity = 0f;
                _progSunLight.shadows = LightShadows.None;
                _progSun.SetActive(false);
            }
            bool sunOn = kind == WeatherKind.Sunny && strength > 0.01f;
            if (_progSun.activeSelf != sunOn) _progSun.SetActive(sunOn);
            if (sunOn)
            {
                _progSunT += dt;
                float bob = 0.25f * Mathf.Sin(_progSunT * 1.6f);
                float half = s.T != null ? s.T.farm.arenaHalf : 20f;
                // ★ 摆在**场地上空、靠镜头这一侧**（z 取负）：俯视 60° 的相机往 -z 看，
                //   放进 +z（远离镜头）会被画到屏幕最上沿甚至出画 —— 实测第一次抓图就没拍到太阳 ✓
                _progSun.transform.position = new Vector3(-half * 0.30f, 13.5f + bob, -half * 0.30f);
                float k = 1.05f + 0.55f * strength;
                _progSun.transform.localScale = new Vector3(k, k, k);
                if (_progSunLight != null) _progSunLight.intensity = 2.6f * strength;
            }
            else if (_progSunLight != null) _progSunLight.intensity = 0f;

            // ══════════════════════════════════════════════════════════════════
            // ★★ v308：**常驻雨幕**（用户：「雨天增加常驻下雨效果」）★★
            //
            // ── 为什么从"84 条定长池"换成"一个 ParticleSystem" ──
            //   改前（v198）是 84 个独立 `GameObject` + 手写相位：每场雨**只落一波**、
            //   落完就 `SetActive(false)`、绝不补充 ⇒ 0.76 秒之后雨就没了，
            //   30 秒的雨天里 29 秒天空是干净的 ✗ 用户说的"常驻"正是这件事。
            //   要变成常驻，最直接的办法是"回卷相位"（每条落到底回天顶）—— 但那要
            //   **每帧遍历 84 个 transform 手算位置**，而且 84 条太稀（规格 200~400）。
            //   ⇒ 改用**一个** `ParticleSystem`：粒子由引擎自己分配/回收/循环，
            //     我们每帧只写 2 个字段（发射器位置 + 发射率），**零 Instantiate、零 new** ✓
            //
            // ── 性能账（用户要求"同屏渲染体不超过 8 个"）──
            //   · 渲染体：**1** 个（`ParticleSystemRenderer`，`Stretch` 模式一片顶一条雨丝）
            //   · 粒子上限：`RainMaxParticles` = 380（规格 200~400 内）
            //   · 每帧 CPU：2 次字段写 + 1 次 `SetColor`（只在 α 变化时）—— 没有逐粒子循环
            //   · 材质/贴图：各 **1 份**，代码生成（16×64 的软竖条），`HideFlags.DontSave`
            //   · 不投影（`shadowCastingMode = Off`）、无碰撞体、无物理
            //
            // ── 视觉 ──
            //   · `Stretch` + `velocityScale`：粒子沿**速度方向**拉成细长斜线（速度 = 下落 + 风）
            //     ⇒ 天然"斜向下"，不需要自己做旋转 ✓
            //   · 颜色偏灰蓝（`RainColor`），整体 α 由材质控制（常驻时 0.55，雨停后 0.35s 淡到 0）
            // ══════════════════════════════════════════════════════════════════
            bool rainOn = kind == WeatherKind.Rain && strength > 0.01f;
            if (rainOn && _progRainGo == null)
            {
                EnsureRainCurtain();
                _progRainStartedTimes++;
                _progRainOnT = 0f;
                _progRainAlpha = 0f;      // 淡入（不会"啪"一下出现一整片雨）
            }

            if (_progRainGo != null)
            {
                if (rainOn)
                {
                    _progRainOnT += dt;
                    // ① 整体不透明度：在 0.25s 内爬到 `RainAlpha × 强度`（与天气淡入同步）
                    float want = RainAlpha * Mathf.Clamp01(strength);
                    _progRainAlpha = Mathf.MoveTowards(_progRainAlpha, want, dt * (1f / 0.25f));
                    if (!_progRainPs.isPlaying) _progRainPs.Play(true);

                    // ② 发射器跟着镜头（雨是"无限远"的环境层 ⇒ 画面里永远在下）
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        Transform ct = cam.transform;
                        _progRainGo.transform.position = ct.position + ct.forward * 7f + Vector3.up * 7.5f;
                    }

                    // ③ 常驻发射：一直吐新粒子，旧的自己寿命到期被引擎回收
                    var em = _progRainPs.emission;
                    em.rateOverTime = RainEmitRate;
                }
                else
                {
                    // 雨停：停发射 + 淡出（剩下的粒子会自然落完，不需要逐个关）
                    var em = _progRainPs.emission;
                    em.rateOverTime = 0f;
                    _progRainAlpha = Mathf.MoveTowards(_progRainAlpha, 0f, dt / RainFadeOutSeconds);
                    if (_progRainAlpha <= 0.001f && _progRainPs.isPlaying)
                        _progRainPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    _progRainOnT = 0f;
                }
                SetRainAlpha(_progRainAlpha);
            }

            // ── 环境变暗（★ v198：改成给唯一写者一个**倍率入口**，不再自己写 ambientLight）──
            //   旧写法在这里直接写 `RenderSettings.ambientLight`，与 `NightLighting.Apply`
            //   每帧抢同一个旋钮 ⇒ 实测"环境变暗"被覆盖、**没生效** ✗
            NightLighting.AmbientDimMultiplier = rainOn ? Mathf.Lerp(1f, 0.45f, strength) : 1f;
        }

        /// <summary>
        /// 清掉本轮新建的表现（`WorldView.Clear()` 里**唯一**调用点）。
        /// ★ 只断引用、不销毁：物件都是 `_root` 的子物体，`Clear()` 里那句 `Destroy(_root)` 会带走它们；
        ///   材质全在 `MatCached` 的缓存里（**不能**销毁）✓
        /// </summary>
        private void ClearProgVisuals()
        {
            _progPlotRoots = null;
            _progPlotShape = null;
            _progPlotStage = null;      // v253：阶段号随根一起清（下一局从 -1 重摆）
            _progSwayPhase = null;      // v253
            _progCarrots = null;
            _progRabbit = null;
            _progShieldRing = null;
            _progSun = null;
            _progSunLight = null;
            _progRainGo = null;
            _progRainPs = null;
            _progRainRend = null;
            _progRainAlpha = 0f;
            _progRainOnT = 0f;
            _progRainStartedTimes = 0;
            _progSunT = 0f;
            // ★ v198：外部环境光倍率必须归 1 —— 否则"上一局停在雨里"会把下一局的环境光
            //   永久压到 0.45（而 `NightLighting` 那边的昼夜循环看起来完全正常 ⇒ 极难归因）✓
            NightLighting.AmbientDimMultiplier = 1f;
        }

        // ══════════════════════════════════════════════════════════════════
        // 只读探针（给 `ProbeV198` / 交付报告取证）
        //
        // ★ 为什么这些是**读方法**而不是字段 ★
        //   现场只有一个 `WorldView` 实例，写侧状态是它的私有字段；
        //   而取证方（探针组件）拿不到那个实例的内部状态 ⇒ 开两个**纯读**入口，
        //   让"雨丝到底竖不竖""这一波落完没有"变成可引用的读数，而不是肉眼签收 ✓
        //   它们**不改任何状态**、不参与任何玩法判定 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>只读：**雨幕**状态（★ v308：常驻粒子 ⇒ 报"当前有多少条在空中"而不是"这一波落完没有"）。</summary>
        public string RainWaveDump()
        {
            if (_progRainGo == null) return "雨幕：**还没建**（没进过雨天 / buildVisuals=false）";
            int alive = _progRainPs != null ? _progRainPs.particleCount : 0;
            bool playing = _progRainPs != null && _progRainPs.isPlaying;
            return $"天气={ProgMechanics.Weather}｜强度={ProgMechanics.WeatherIntensity01:0.000}"
                 + $"｜**常驻雨幕**：空中 {alive}/{RainMaxParticles} 条（上限 {RainMaxParticles}）"
                 + $"｜{(playing ? (alive > 0 ? "**正在下**" : "刚开播") : "已停")}"
                 + $"｜整体 α={_progRainAlpha:0.000}/{RainAlpha:0.00}（雨停后 {RainFadeOutSeconds:0.00}s 淡出）"
                 + $"｜已连续下 {_progRainOnT:0.0}s（本次雨天）· 进过 {_progRainStartedTimes} 次雨天"
                 + $"｜渲染体 **1** 个（ParticleSystem · Stretch · 发射 {RainEmitRate:0}/s · 风 {RainWind:0.0} m/s）"
                 + $"｜环境光外部倍率={NightLighting.AmbientDimMultiplier:0.000}";
        }

        /// <summary>
        /// 只读：雨幕的**朝向与位移**读数（★ v308 重写）。
        /// 旧版量的是单个 `GameObject` 的长轴与 +Y 夹角（"竖不竖"）；
        /// 现在雨是粒子，所以报的是**发射器与速度曲线**：粒子速度方向就是雨丝的朝向，
        /// 由"下落 −16 m/s + 风 +2.6 m/s"决定 ⇒ 斜向下的角度可以直接算出来印证 ✓
        /// </summary>
        public string RainDropDump(int index)
        {
            if (_progRainGo == null) return "雨幕：还没建（没进过雨天）";
            if (index != 0) return $"index 越界：雨幕只有 1 个渲染体（index 0）";
            var vol = _progRainPs.velocityOverLifetime;
            float vy = vol.y.constant;
            float vx = vol.x.constant;
            float ang = Mathf.Atan2(Mathf.Abs(vx), Mathf.Abs(vy)) * Mathf.Rad2Deg;
            Vector3 p = _progRainGo.transform.position;
            return $"雨幕[0]｜渲染体=ParticleSystem（Stretch，1 个）"
                 + $"｜速度=({vx:0.00},{vy:0.00}) ⇒ 与竖直夹角 **{ang:0.0}°**（>0 = 斜向下，受风）"
                 + $"｜单条长度≈{Mathf.Abs(vy) * RainStretchScale:0.00}m（速度×velocityScale）"
                 + $"｜发射器位置=({p.x:0.00},{p.y:0.00},{p.z:0.00})"
                 + $"｜在空中={(_progRainPs != null && _progRainPs.particleCount > 0)}"
                 + $"｜`shadowCastingMode`={(_progRainRend != null ? _progRainRend.shadowCastingMode.ToString() : "?")}";
        }
    }
}
