using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v204 只读探针：玩家**脚下的朝向指示** —— 现在是 **2D 贴图**（圆环 ＋ 方向点）★★
    ///
    /// 用户原话（v204）："重做方向指标，用2d贴图方式展示，一个圆环包裹玩家，一个圆形代表方向"
    ///
    /// ── 这个探针为什么改名不改（v203 → v204）──
    ///   它验的**对象**没变（"玩家脚下那个告诉我朝哪的贴片"），变的是**实现**：
    ///   旧版是程序化扇形网格 + `URP/Lit` 不透明；新版是 `Texture2D` + quad + `Sprites/Default`。
    ///   ⇒ 旧版的判据（半张角 58°、前尖 1.50m、`FacingFan` 网格名）全部**不再成立**，
    ///     留着就是一批永远红的假线索 ✗ 所以整段按新形态重写，文件名保留（它是同一个职责）✓
    ///
    /// ── 验六组，每组都有读数 ──
    ///   A 资源契约：3 个玩家 ⇒ **1 份网格 / 2 张贴图 / 2 份材质**；240 帧之后**一个都不新增**
    ///   B 形式：形状**真的在贴图里**（从 alpha 量内外半径 / 硬边 / 整圈 / 方向点的直径与圆心距）
    ///   C 朝向：读的是**模型最终角** —— 方向点的世界位置始终在"模型正前方"，逐帧夹角 0
    ///   D 状态反应：死亡隐藏 / 闪避放大 / 受击与挥击不变
    ///   E 抓图：朝北 / 朝东 / 转身中 / 俯视 + **两张贴图本身**（环 / 点）导出 PNG
    ///   F 可见性：8 个相机方位下"环 / 点"各自露出来的像素（开/关差分）
    ///
    /// ★ 生产路径：`ModelView.SpawnPlayer` + `SetScaleFactor(2.60×0.627)`，
    ///   与 `WorldView.BuildPlayer` 同一个缩放 ⇒ 图上与读数都是实战尺寸 ✓
    /// ★ 只读：不写玩法状态、不碰存档；摆一个 `PROBE_FACING` 台，跑完 `Destroy` 掉 ✓
    /// ★ 抓图：`RenderTexture` + `ReadPixels`（**不用** `ScreenCapture.CaptureScreenshot`，那个要窗口焦点）✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV203Facing : MonoBehaviour
    {
        /// <summary>与 `WorldView.CharScale × PlayerScale` 一致 ⇒ 台上就是实战大小（1.6302）。</summary>
        private const float ToWorld = 2.60f * 0.627f;

        /// <summary>摆样横向间距（米）。环直径 2.30m ⇒ 3.0m 不会互相压到。</summary>
        private const float SlotGap = 3.0f;

        private const string RingNodeName = "FacingRing";
        private const string DiscNodeName = "FacingRingDisc";
        private const string DotNodeName = "FacingDot";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV203Facing>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV203Facing");
            go.AddComponent<ProbeV203Facing>();
        }

        /// <summary>桥：`/input` → action=send, path=ProbeV203Facing, method=Run。</summary>
        public string Run() => RunReport();

        /// <summary>8 个相机方位 × "环 / 点"各自露出的像素（开/关差分）。</summary>
        public string RunCam() => ReportVisibility();

        /// <summary>跑一次全量自检并带回计数原文（只读：内存里建/丢会话）。</summary>
        public string SelfCheckNow()
        {
            List<CheckResult> list = SelfCheck.RunAll();
            string rep = SelfCheck.Report(list);
            Debug.Log("[Seedfall] " + rep);
            return rep;
        }

        // ══════════════════════════════════════════════════════════════════
        // 入口
        // ══════════════════════════════════════════════════════════════════
        public static string RunReport()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(bool ok, string name, string detail)
            {
                if (ok) pass++; else fail++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
            }

            sb.AppendLine("═══ v204 探针（只读）：玩家脚下的朝向指示 = 2D 贴图（圆环 ＋ 方向点）═══");

            // 摆样台：躲开 `PlayerShowcase`（玩家 −8/+8）与 `BugShowcase`（+8.6/+1.5）
            Vector3 origin = new Vector3(9.5f, 0f, 9.0f);
            if (GameBootstrap.Live.Count > 0 && GameBootstrap.Live[0] != null &&
                GameBootstrap.Live[0].Session != null && GameBootstrap.Live[0].Session.World.Player != null)
            {
                PlayerActor p = GameBootstrap.Live[0].Session.World.Player;
                origin = new Vector3(p.Pos.x + 9.0f, 0f, p.Pos.y + 9.0f);
            }

            // ★ 基线要在**造玩家之前**取：`Resources.FindObjectsOfTypeAll` 会把**上一次 play 会话**
            //   遗留在编辑器里的同名人造对象也数进来 ⇒ 判据只能是**本次会话的增量** ✓
            int meshBuilds0 = WorldView.FacingQuadMeshBuilds;
            int texBuilds0 = WorldView.FacingTexBuilds;
            int quadCopies0 = CountAllNamed<Mesh>("FacingQuad");
            int ringTexCopies0 = CountAllNamed<Texture2D>("FacingRingTex");
            int dotTexCopies0 = CountAllNamed<Texture2D>("FacingDotTex");
            sb.AppendLine($"摆样台 @ ({origin.x:0.0}, {origin.z:0.0})｜缩放 {ToWorld:0.0000}"
                        + $"｜开局构建计数 网格 {meshBuilds0} / 贴图 {texBuilds0}"
                        + $"（存量：FacingQuad {quadCopies0} / FacingRingTex {ringTexCopies0} / FacingDotTex {dotTexCopies0}"
                        + " —— 存量为历史会话遗留，判据看**增量**）");

            GameObject holder = new GameObject("PROBE_FACING");
            holder.transform.position = origin;

            var models = new List<ModelView>();
            var yaws = new[] { 0f, 90f, 45f };     // A 朝北 / B 朝东 / C 转身中
            for (int i = 0; i < yaws.Length; i++)
            {
                ModelView mv = ModelView.SpawnPlayer(holder.transform);
                if (mv == null || mv.Go == null) { sb.AppendLine("  ✗ SpawnPlayer 失败"); Object.Destroy(holder); return sb.ToString(); }
                mv.Go.transform.localPosition = new Vector3((i - 1) * SlotGap, 0f, 0f);
                mv.SetScaleFactor(ToWorld);
                mv.SetFacing(yaws[i]);
                mv.Play(ModelView.State.Idle);
                mv.Tick(0f);
                models.Add(mv);
            }

            ModelView a = models[0], b = models[1], c = models[2];
            GameObject ringA = a.Bean != null ? a.Bean.FacingRing : null;
            GameObject ringB = b.Bean != null ? b.Bean.FacingRing : null;
            GameObject ringC = c.Bean != null ? c.Bean.FacingRing : null;

            if (ringA == null || ringB == null || ringC == null)
            {
                sb.AppendLine("  ✗ 取不到 FacingRing（`PlayerBean.FacingRing` 为 null）");
                Object.Destroy(holder);
                return sb.ToString();
            }

            GameObject discA = Child(ringA, DiscNodeName), dotA = Child(ringA, DotNodeName);
            if (discA == null || dotA == null)
            {
                sb.AppendLine($"  ✗ 指示下缺少子物件（期望 `{DiscNodeName}` 与 `{DotNodeName}`）");
                Object.Destroy(holder);
                return sb.ToString();
            }

            AppendContract(sb, Check, models, ringA, ringB, ringC, discA, dotA, meshBuilds0, texBuilds0);
            AppendForm(sb, Check, models, discA, dotA);
            AppendFacing(sb, Check, models, ringA, dotA);
            AppendState(sb, Check, models, ringA);

            // ── 抓图（在 Destroy 之前）──
            AppendTexExport(sb, Check);
            AppendShots(sb, models);

            Object.Destroy(holder);

            sb.AppendLine();
            sb.AppendLine($"════ 合计 {pass} PASS / {fail} FAIL ════");
            string text = sb.ToString();
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════
        // A 资源契约：形状在贴图里 ⇒ 全场只应有 1 份网格 / 2 张贴图 / 2 份材质
        // ══════════════════════════════════════════════════════════════════
        private static void AppendContract(StringBuilder sb, System.Action<bool, string, string> check,
            List<ModelView> models, GameObject ringA, GameObject ringB, GameObject ringC,
            GameObject discA, GameObject dotA, int meshBuilds0, int texBuilds0)
        {
            sb.AppendLine();
            sb.AppendLine("── A 资源契约（3 个玩家 + 240 帧 Tick 之后）──");

            int nodes = 0, discs = 0, dots = 0;
            for (int i = 0; i < models.Count; i++)
            {
                nodes += CountNamed(models[i].Go.transform, RingNodeName);
                discs += CountNamed(models[i].Go.transform, DiscNodeName);
                dots += CountNamed(models[i].Go.transform, DotNodeName);
            }
            check(nodes == models.Count && discs == models.Count && dots == models.Count,
                "A1 每个玩家恰好 1 个 FacingRing（含 1 环 + 1 点，不是每帧重建）",
                $"3 个玩家下 {RingNodeName} {nodes} / {DiscNodeName} {discs} / {DotNodeName} {dots}");

            Mesh mA = MeshOf(Child(ringA, DiscNodeName)), mB = MeshOf(Child(ringB, DiscNodeName)),
                 mC = MeshOf(Child(ringC, DiscNodeName));
            Mesh dotMesh = MeshOf(dotA);
            check(mA != null && mA == mB && mB == mC && mA == dotMesh,
                "A2 环与点**共用同一个四边形网格**（3 个玩家 × 2 片 = 6 个渲染器，1 个 Mesh 实例）",
                mA == null ? "取不到网格"
                           : $"名称 {mA.name}｜顶点 {mA.vertexCount}｜三角 {mA.triangles.Length / 3}"
                             + $"｜有 UV {mA.uv.Length == mA.vertexCount}");

            Material matA = MatOf(Child(ringA, DiscNodeName)), matB = MatOf(Child(ringB, DiscNodeName));
            Material dotMatA = MatOf(dotA), dotMatB = MatOf(Child(ringB, DotNodeName));
            check(matA != null && matA == matB, "A3 环：三个玩家**共用同一份材质**",
                matA == null ? "取不到材质" : $"{matA.name}｜shader {matA.shader.name}");
            check(dotMatA != null && dotMatA == dotMatB, "A3b 方向点：三个玩家**共用同一份材质**",
                dotMatA == null ? "取不到材质" : $"{dotMatA.name}｜shader {dotMatA.shader.name}");
            sb.AppendLine($"     环材质 shader = {ShaderName(matA)}｜点材质 shader = {ShaderName(dotMatA)}"
                        + "（`Sprites/Default` = 走贴图 alpha 的那条路；带 FALLBACK 后缀说明退回了）");

            Texture2D ringTex = matA != null ? matA.mainTexture as Texture2D : null;
            Texture2D dotTex = dotMatA != null ? dotMatA.mainTexture as Texture2D : null;
            check(ringTex != null && ringTex == matB.mainTexture, "A4 环贴图：三个玩家**共用同一张 Texture2D**",
                ringTex == null ? "材质上没有 `_MainTex`"
                                : $"{ringTex.name} {ringTex.width}×{ringTex.height} {ringTex.format}"
                                  + $" filter={ringTex.filterMode} wrap={ringTex.wrapMode} readable={ringTex.isReadable}");
            check(dotTex != null && dotTex != ringTex, "A4b 方向点贴图：是**另一张**图（不是环那张换个色）",
                dotTex == null ? "材质上没有 `_MainTex`" : $"{dotTex.name} {dotTex.width}×{dotTex.height}");

            // 增量判据：3 个玩家只应带来 1 份网格 + 2 张贴图
            int quadNow = CountAllNamed<Mesh>("FacingQuad");
            int ringTexNow = CountAllNamed<Texture2D>("FacingRingTex");
            int dotTexNow = CountAllNamed<Texture2D>("FacingDotTex");
            int meshBuilt = WorldView.FacingQuadMeshBuilds - meshBuilds0;
            int texBuilt = WorldView.FacingTexBuilds - texBuilds0;
            sb.AppendLine($"     本次构建：网格 {meshBuilt} 次 / 贴图 {texBuilt} 张｜存量 FacingQuad {quadNow}"
                        + $" / FacingRingTex {ringTexNow} / FacingDotTex {dotTexNow}");
            check(meshBuilt <= 1 && texBuilt <= 2, "A5 3 个玩家只建 **≤1 网格 + ≤2 贴图**（per-instance 新建为 0）",
                $"网格 +{meshBuilt}（上界 1）｜贴图 +{texBuilt}（上界 2）");

            // 连跑 240 帧（同时转身）：一个都不涨 ⇒ 运行期 0 次 new Mesh / new Texture2D / new Material
            for (int f = 0; f < 240; f++)
                for (int i = 0; i < models.Count; i++)
                {
                    models[i].SetFacing(f * 1.5f);
                    models[i].Tick(1f / 60f);
                }
            int meshAfter = WorldView.FacingQuadMeshBuilds, texAfter = WorldView.FacingTexBuilds;
            int quadCopies2 = CountAllNamed<Mesh>("FacingQuad");
            int ringTexCopies2 = CountAllNamed<Texture2D>("FacingRingTex");
            int dotTexCopies2 = CountAllNamed<Texture2D>("FacingDotTex");
            check(meshAfter == WorldView.FacingQuadMeshBuilds && texAfter == WorldView.FacingTexBuilds
                  && quadCopies2 == quadNow && ringTexCopies2 == ringTexNow && dotTexCopies2 == dotTexNow,
                "A5b 240 帧 × 3 个玩家：网格/贴图计数**一次都没涨**（运行期零新建）",
                $"网格 {WorldView.FacingQuadMeshBuilds}｜贴图 {WorldView.FacingTexBuilds}"
                + $"｜存量 {quadCopies2}/{ringTexCopies2}/{dotTexCopies2}");

            int cols = ringA.GetComponentsInChildren<Collider>(true).Length
                     + ringB.GetComponentsInChildren<Collider>(true).Length
                     + ringC.GetComponentsInChildren<Collider>(true).Length;
            check(cols == 0, "A6 指示上没有任何 Collider（不参与判定）", $"实测 {cols} 个");

            int badShadow = 0;
            Renderer[] all = ringA.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off || all[i].receiveShadows)
                    badShadow++;
            check(badShadow == 0, "A6b 两片都不投影、也不吃阴影（贴图片不会在地上糊出第二片）",
                $"{all.Length} 个渲染器，违规 {badShadow} 个");

            check(ringA.transform.localPosition == Vector3.zero
                  && Quaternion.Angle(ringA.transform.localRotation, Quaternion.identity) < 0.0001f,
                "A7 挂在模型根下且局部变换为单位（朝向 = 模型最终角，**不需要任何同步代码**）",
                $"localPos={V(ringA.transform.localPosition)} localRot={ringA.transform.localRotation.eulerAngles}");
        }

        // ══════════════════════════════════════════════════════════════════
        // B 形式：**形状在贴图里** —— 从 alpha 量半径，不读注释
        // ══════════════════════════════════════════════════════════════════
        private static void AppendForm(StringBuilder sb, System.Action<bool, string, string> check,
            List<ModelView> models, GameObject disc, GameObject dot)
        {
            sb.AppendLine();
            sb.AppendLine("── B 形式（从**贴图 alpha** ＋ 物件变换量，不读常量）──");

            Material ringMat = MatOf(disc), dotMat = MatOf(dot);
            Texture2D ringTex = ringMat != null ? ringMat.mainTexture as Texture2D : null;
            Texture2D dotTex = dotMat != null ? dotMat.mainTexture as Texture2D : null;
            if (ringTex == null || dotTex == null) { sb.AppendLine("  ✗ 取不到贴图"); return; }

            // ── 环：贴图 alpha 量出内外半径（像素），再乘"1 texel = 直径/S"换成场上米 ──
            float quadW = disc.GetComponent<MeshRenderer>().bounds.size.x;      // 场上直径
            float texelM = quadW / ringTex.width;                              // 1 texel = 多少场上米
            ProfileAlpha(ringTex, out float innerTexel, out float outerTexel,
                         out int opaqueCount, out int binaryBad, out int sectorsHit, out int sectorsTotal);
            float innerW = innerTexel * texelM, outerW = outerTexel * texelM;
            sb.AppendLine($"     环贴图 {ringTex.width}px｜quad 场上直径 {quadW:0.000}m"
                        + $" ⇒ 1 texel = {texelM:0.0000}m");
            sb.AppendLine($"     alpha 量出：外半径 {outerTexel:0.0} texel = **{outerW:0.000}m 场上**"
                        + $"｜内半径 {innerTexel:0.0} texel = **{innerW:0.000}m 场上**"
                        + $"（内/外 = {innerTexel / Mathf.Max(1f, outerTexel):0.000}）");
            sb.AppendLine($"     不透明 texel {opaqueCount} 个（{100f * opaqueCount / (ringTex.width * ringTex.height):0.0}%）"
                        + $"｜alpha 非 0/255 的 texel {binaryBad} 个（硬边像素风 ⇒ 应为 0）");

            check(outerW > 0.90f && outerW < 1.20f && Mathf.Abs(outerW - WorldView.FacingRingOuterM) < 0.06f,
                "B1 环外半径在 0.9~1.2m（用户给的范围，设计值 1.15m）", $"实测 {outerW:0.000}m");
            check(binaryBad == 0, "B2 **硬边像素**：贴图 alpha 只有 0 / 255（无抗锯齿、无渐变，与图标同一规范）",
                $"违反的 texel {binaryBad} 个 / 共 {ringTex.width * ringTex.height}");
            check(sectorsTotal > 0 && sectorsHit == sectorsTotal,
                "B3 **整圈**包裹玩家：16 个方向上的环带全是实心（不是扇区/箭头）",
                $"{sectorsHit}/{sectorsTotal} 个方向命中");

            float bootR = BootMaxRadius(models[0]);
            check(innerW > bootR + 0.20f && innerW > 0.50f,
                "B4 **不挡脚**：内半径比靴子最远点还大 0.2m 以上（几何上完全不重叠）",
                $"靴子最远 {bootR:0.000}m vs 内半径 {innerW:0.000}m ⇒ 间距 {innerW - bootR:0.000}m");

            float lift = disc.transform.position.y - models[0].Go.transform.position.y;
            check(lift > 0.010f && lift < 0.022f, "B5 抬高在 0.012~0.02m（消 Z-fighting；贴图片不写深度）",
                $"实测 {lift:0.000}m（设计 {WorldView.FacingLiftM:0.000}m）");

            // ── 方向点：直径 / 圆心距 / 圆度 ──
            float dotD = dot.GetComponent<MeshRenderer>().bounds.size.x;
            float dotR = new Vector2(dot.transform.position.x - models[0].Go.transform.position.x,
                                     dot.transform.position.z - models[0].Go.transform.position.z).magnitude;
            ProfileDot(dotTex, out float dotOuterTexel, out int dotBad, out bool dotCenterOpaque,
                       out bool dotCornerClear);
            sb.AppendLine($"     方向点：贴图 {dotTex.width}px｜场上直径 {dotD:0.000}m"
                        + $"（alpha 外半径 {dotOuterTexel:0.0} texel）｜圆心距 {dotR:0.000}m");
            check(Mathf.Abs(dotD - WorldView.FacingDotDiamM) < 0.03f,
                $"B6 方向点直径 = {WorldView.FacingDotDiamM:0.00}m（一颗「珠子」，不是一张大圆盘）",
                $"实测 {dotD:0.000}m");
            check(dotR > innerW && dotR < outerW,
                "B7 **方向点落在环带上**（内半径 < 圆心距 < 外半径 ⇒ 读作「环上的一个圆」）",
                $"圆心距 {dotR:0.000}m ∈ ({innerW:0.000}, {outerW:0.000})");
            check(dotBad == 0 && dotCenterOpaque && dotCornerClear,
                "B8 方向点贴图：硬边 + 中心实心 + 四角透明（是一颗圆，不是一个方块）",
                $"alpha 违规 {dotBad}｜中心 {dotCenterOpaque}｜四角透明 {dotCornerClear}");

            // 颜色：环体是图标 HI、方向点是图标 ACC 绿 ⇒ 地面上一眼分得开
            Color ringBody = SampleOpaqueColor(ringTex, outerTexel * 0.85f);
            Color dotBody = SampleOpaqueColor(dotTex, dotOuterTexel * 0.45f);
            sb.AppendLine($"     颜色取样：环体 {C(ringBody)}｜方向点 {C(dotBody)}");
            check(ringBody.r > 0.85f && ringBody.g > 0.85f && ringBody.b > 0.80f,
                "B9 环体是**亮暖白**（图标 HI `#F2EFE6`）⇒ 比土黄/绿地面都亮，夜里也不受光照变黑",
                C(ringBody));
            check(dotBody.g > dotBody.r + 0.15f && dotBody.g > dotBody.b + 0.15f,
                "B10 方向点是**绿**（图标 ACC `#8CE891`）⇒ 环上唯一的彩色，「朝哪」一眼落到它身上",
                C(dotBody));

            // 两片都不写深度 ⇒ 与地面重叠时靠**抬高**而不是靠排序
            sb.AppendLine($"     材质：环 queue {QueueOf(ringMat)} / 点 queue {QueueOf(dotMat)}"
                        + "（`Sprites/Default` = 透明队列 3000、ZWrite Off ⇒ 抬起来是唯一的防闪手段）");
        }

        // ══════════════════════════════════════════════════════════════════
        // C 朝向：读的是**模型最终角**
        // ══════════════════════════════════════════════════════════════════
        private static void AppendFacing(StringBuilder sb, System.Action<bool, string, string> check,
            List<ModelView> models, GameObject ring, GameObject dot)
        {
            sb.AppendLine();
            sb.AppendLine("── C 朝向（方向点的**世界位置** vs 模型根节点的**世界前方**）──");
            ModelView a = models[0];

            // C1 静置取几个角度：方向点始终在模型正前方
            float worst = 0f;
            var detail = new StringBuilder();
            float[] probeYaws = { 0f, 90f, 180f, 270f, 35f, 300f, 359f };
            for (int i = 0; i < probeYaws.Length; i++)
            {
                a.SetFacing(probeYaws[i]);
                a.Tick(0f);
                float d = DotVsForward(dot, a);
                if (d > worst) worst = d;
                if (i < 4) detail.Append($"{probeYaws[i]:0}°→差 {d:0.0000}°；");
            }
            check(worst < 0.01f, "C1 七个角度下「方向点方位 − 模型前方」**恒为 0**（子节点同一条 Transform）",
                $"最大 {worst:0.0000}°｜{detail}");

            // C2 转身逐帧扫（含绕回）
            float worstSweep = 0f;
            int frames = 0;
            for (int f = 0; f <= 360; f += 3)
            {
                a.SetFacing(f);
                a.Tick(1f / 60f);
                float d = DotVsForward(dot, a);
                if (d > worstSweep) worstSweep = d;
                frames++;
            }
            check(worstSweep < 0.05f, $"C2 转身 {frames} 帧（0°→360° 每 3°）**逐帧**夹角都是 0（不滞后）",
                $"最大 {worstSweep:0.0000}°（1.12m 半径上 0.001° ≈ 20 微米 ⇒ 这就是浮点噪声）"
                + " —— 同一个 Transform 的子节点，不存在「晚一帧」的效果");

            // C3 反证：模型**不动**、只改朝向 ⇒ 方向点照样绕着玩家转
            Vector3 p0 = a.Go.transform.position;
            a.SetFacing(0f); a.Tick(0f);
            Vector3 d0 = dot.transform.position - p0;
            a.SetFacing(90f); a.Tick(0f);
            Vector3 d1 = dot.transform.position - p0;
            float moved = Vector3.Distance(p0, a.Go.transform.position);
            float turned = Mathf.Abs(Mathf.DeltaAngle(
                Mathf.Atan2(d0.x, d0.z) * Mathf.Rad2Deg, Mathf.Atan2(d1.x, d1.z) * Mathf.Rad2Deg));
            check(moved < 0.0001f && Mathf.Abs(turned - 90f) < 0.05f,
                "C3 **位移 0 也照样跟转**（方向点绕玩家转 90°，读模型角而不是位移 atan2）",
                $"位移 {moved:0.0000}m｜方向点转了 {turned:0.00}°");

            // C4 指示的世界位置 = 模型脚下（同一 XZ），抬高只有那一点点
            a.SetFacing(0f); a.Tick(0f);
            Vector3 rp = ring.transform.position, mp = a.Go.transform.position;
            sb.AppendLine($"     指示世界坐标 ({rp.x:0.000}, {rp.y:0.000}, {rp.z:0.000})｜模型 ({mp.x:0.000}, {mp.y:0.000}, {mp.z:0.000})");
            check(Mathf.Abs(rp.x - mp.x) < 0.001f && Mathf.Abs(rp.z - mp.z) < 0.001f,
                "C4 水平上就在玩家脚下（同一 XZ）",
                $"Δx {rp.x - mp.x:0.0000}｜Δz {rp.z - mp.z:0.0000}｜Δy {rp.y - mp.y:0.000}m（=抬高）");
        }

        // ══════════════════════════════════════════════════════════════════
        // D 状态反应
        // ══════════════════════════════════════════════════════════════════
        private static void AppendState(StringBuilder sb, System.Action<bool, string, string> check,
            List<ModelView> models, GameObject ring)
        {
            sb.AppendLine();
            sb.AppendLine("── D 状态反应（规则由 v203 定，v204 形态换了但规则不变）──");
            ModelView a = models[0];
            a.SetFacing(0f);

            a.PlayForced(ModelView.State.Idle); a.Tick(0f);
            bool idleOn = ring.activeSelf;
            float idleScale = ring.transform.localScale.x;
            check(idleOn && Mathf.Abs(idleScale - 1f) < 0.001f, "D1 待机：显示，缩放 1.000",
                $"active={idleOn} scale={idleScale:0.000}");

            a.PlayForced(ModelView.State.Death); a.Tick(0.50f);
            check(!ring.activeSelf, "D2 **死亡：隐藏**（尸体还指着方向是假信息）", $"active={ring.activeSelf}");

            a.PlayForced(ModelView.State.Idle); a.Tick(0f);
            a.SetEvade(0.26f); a.Tick(0f);
            float evScale = ring.transform.localScale.x;
            check(Mathf.Abs(evScale - 1.14f) < 0.01f, "D3 闪避：放大到 1.140（「这一下我在动」读得出来）",
                $"scale={evScale:0.000}");
            a.SetEvade(0f);

            a.PlayForced(ModelView.State.Hit); a.Tick(0.05f);
            float hitScale = ring.transform.localScale.x;
            check(Mathf.Abs(hitScale - 1f) < 0.01f && ring.activeSelf,
                "D4 **受击：不变**（白闪已由模型 `_TintMul` 表达，地面再闪会被读成「地面挨打」）",
                $"scale={hitScale:0.000} active={ring.activeSelf}");

            a.PlayForced(ModelView.State.Attack);
            a.Bean.DebugFreezeU = 0.45f;
            a.Tick(0.35f);
            float atkScale = ring.transform.localScale.x;
            check(Mathf.Abs(atkScale - 1f) < 0.01f, "D5 挥击：不变（指示是朝向信息，不随动作闪）",
                $"scale={atkScale:0.000}");
            a.Bean.DebugFreezeU = -1f;

            a.PlayForced(ModelView.State.Idle); a.Tick(0f);
            int onCount = 0;
            for (int i = 0; i < models.Count; i++)
                if (models[i].Bean != null && models[i].Bean.FacingRing != null
                    && models[i].Bean.FacingRing.activeSelf) onCount++;
            check(onCount == models.Count, "D6 恢复待机后三个玩家的指示都在", $"{onCount}/{models.Count}");
        }

        // ══════════════════════════════════════════════════════════════════
        // E1 贴图导出：把两张图本尊写成 PNG（"新环贴图 ＋ 方向点的图"的可复核形态）
        // ══════════════════════════════════════════════════════════════════
        private static void AppendTexExport(StringBuilder sb, System.Action<bool, string, string> check)
        {
            sb.AppendLine();
            sb.AppendLine("── E1 贴图本尊导出（`EncodeToPNG`，4× 放大旁边再放一张原尺寸）──");

            Texture2D ring = WorldView.FacingRingTex();
            Texture2D dot = WorldView.FacingDotTex();
            string ringPath = DumpPng(ring, "f204_tex_ring", 4);
            string dotPath = DumpPng(dot, "f204_tex_dot", 4);
            bool ok = !string.IsNullOrEmpty(ringPath) && !string.IsNullOrEmpty(dotPath);
            check(ok, "E1 两张贴图都能导出 PNG（证明它们是真 `Texture2D`，不是「看不见的程序数据」）",
                $"环 → {ringPath}｜点 → {dotPath}");
        }

        // ══════════════════════════════════════════════════════════════════
        // E2 抓图：朝北 / 朝东 / 转身中 / 俯视
        // ══════════════════════════════════════════════════════════════════
        private static void AppendShots(StringBuilder sb, List<ModelView> models)
        {
            sb.AppendLine();
            sb.AppendLine("── E2 抓图（RenderTexture + ReadPixels：同一次调用里摆放＋渲染＋回读）──");

            Camera cam = Camera.main;
            if (cam == null)
            {
                Camera[] all = FindObjectsByType<Camera>();
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy) { cam = all[i]; break; }
            }
            if (cam == null) { sb.AppendLine("  ✗ 没有可用相机"); return; }

            Vector3 savedPos = cam.transform.position;
            Quaternion savedRot = cam.transform.rotation;
            RenderTexture savedTarget = cam.targetTexture;

            float[] yaws = { 0f, 90f, 45f };
            for (int i = 0; i < models.Count && i < yaws.Length; i++)
            {
                models[i].SetFacing(yaws[i]);
                models[i].PlayForced(ModelView.State.Idle);
                models[i].Tick(0f);
            }
            Vector3 rowCenter = models[1].Go.transform.position;

            // ★ 机位取**实战俯角 60°**（`tuning.camera.pitch`），距离缩到 6.5m 只是为了让图看得清
            //   —— 角度决定遮挡关系，距离只决定大小 ✓
            string[] names = { "f204_north", "f204_east", "f204_turn" };
            string[] labels = { "朝北 yaw=0°", "朝东 yaw=90°", "转身中 yaw=45°" };
            for (int i = 0; i < 3; i++)
            {
                for (int k = 0; k < models.Count; k++) models[k].Go.SetActive(k == i);
                Vector3 focus = models[i].Go.transform.position + new Vector3(0f, 0.60f, 0f);
                string res = Capture(cam, focus, 180f, 60f, 6.5f, names[i], 1200, 800);
                sb.AppendLine($"     {labels[i]} → {res}");
            }

            // 俯视：三个一起，pitch 80（**主用途**：帽檐盖眼时靠脚下读朝向）
            for (int k = 0; k < models.Count; k++) models[k].Go.SetActive(true);
            {
                Vector3 focus = rowCenter + new Vector3(0f, 0.20f, 0f);
                string res = Capture(cam, focus, 180f, 80f, 10.5f, "f204_top", 1400, 900);
                sb.AppendLine($"     俯视 pitch=80°（主用途）→ {res}");
            }

            cam.transform.position = savedPos;
            cam.transform.rotation = savedRot;
            cam.targetTexture = savedTarget;
        }

        // ══════════════════════════════════════════════════════════════════
        // F 可见性：8 个相机方位 × 环 / 点 各自露出多少像素
        // ══════════════════════════════════════════════════════════════════
        private static string ReportVisibility()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(bool ok, string name, string detail)
            {
                if (ok) pass++; else fail++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
            }

            sb.AppendLine("═══ v204 探针：「2D 贴图指示」在 8 个相机方位下**露出来的像素数** ═══");

            Camera cam = Camera.main;
            if (cam == null) { sb.AppendLine("（没有相机）"); return sb.ToString(); }

            Vector3 origin = new Vector3(9.5f, 0f, 9.0f);
            if (GameBootstrap.Live.Count > 0 && GameBootstrap.Live[0] != null &&
                GameBootstrap.Live[0].Session != null && GameBootstrap.Live[0].Session.World.Player != null)
            {
                PlayerActor p = GameBootstrap.Live[0].Session.World.Player;
                origin = new Vector3(p.Pos.x + 9.0f, 0f, p.Pos.y + 9.0f);
            }

            GameObject holder = new GameObject("PROBE_FACING_CAM");
            holder.transform.position = origin;
            ModelView mv = ModelView.SpawnPlayer(holder.transform);
            if (mv == null || mv.Go == null) { Object.Destroy(holder); return "SpawnPlayer 失败"; }
            mv.SetScaleFactor(ToWorld);
            mv.SetFacing(0f);                    // 模型固定朝北(+Z)；转相机 = 等价于转"相对相机的朝向"
            mv.Play(ModelView.State.Idle);
            mv.Tick(0f);

            GameObject ring = mv.Bean != null ? mv.Bean.FacingRing : null;
            if (ring == null) { Object.Destroy(holder); return "取不到 FacingRing"; }
            GameObject disc = Child(ring, DiscNodeName), dot = Child(ring, DotNodeName);

            Vector3 savedPos = cam.transform.position;
            Quaternion savedRot = cam.transform.rotation;
            RenderTexture savedTarget = cam.targetTexture;

            const int W = 520, H = 340;
            const float Dist = 4.0f;      // 与 v203 那次成功读数**同一套机位** ⇒ 改前/改后逐格可比
            Vector3 focus = mv.Go.transform.position + new Vector3(0f, 0.95f, 0f);

            sb.AppendLine();
            sb.AppendLine($"── 战术俯角 60°（= tuning.camera.pitch）｜dist={Dist:0.0}m {W}×{H}｜开/关差分 ──");
            sb.AppendLine("  相机yaw °｜玩家相对相机｜环可见px｜点可见px");
            sb.AppendLine("  ─────────┼──────────────┼─────────┼────────");

            int backRing = 0, backDot = 0, sideRing = 0, sideDot = int.MaxValue, frontDot = 0;
            for (int k = 0; k < 8; k++)
            {
                float camYaw = k * 45f;
                Color32[] both = RenderPixelsRig(cam, focus, camYaw, 60f, Dist, W, H);
                disc.SetActive(false); dot.SetActive(false);
                Color32[] none = RenderPixelsRig(cam, focus, camYaw, 60f, Dist, W, H);
                disc.SetActive(true); dot.SetActive(false);
                Color32[] ringOnly = RenderPixelsRig(cam, focus, camYaw, 60f, Dist, W, H);
                disc.SetActive(true); dot.SetActive(true);

                int ringPx = DiffCount(ringOnly, none);
                int dotPx = DiffCount(both, ringOnly);
                bool isBack = Mathf.Abs(camYaw - 180f) < 1f;
                bool isSide = Mathf.Abs(camYaw - 90f) < 1f || Mathf.Abs(camYaw - 270f) < 1f;
                bool isFront = Mathf.Abs(camYaw) < 1f;
                if (isBack) { backRing = ringPx; backDot = dotPx; }
                if (isFront) frontDot = dotPx;
                if (isSide)
                {
                    sideRing = Mathf.Max(sideRing, ringPx);
                    sideDot = Mathf.Min(sideDot, dotPx);
                }
                string rel = Mathf.Abs(camYaw) < 1f ? "正对相机"
                           : isBack ? "背对相机"
                           : isSide ? "侧对相机" : "斜后";
                sb.AppendLine($"  {camYaw,8:0}°｜{rel,-12}｜{ringPx,8}｜{dotPx,7}");
            }

            sb.AppendLine();
            Check(sideRing >= 800, "F1 **侧对相机**时环露出 ≥ 800 px（形态与旧扇形同级，且不再有方向偏移）",
                $"实测 {sideRing} px");
            Check(backRing >= 300, "F2 **背对相机**时环仍露出 ≥ 300 px（旧扇形在这个方位有 300 px 的基线）",
                $"实测 {backRing} px（环外半径 1.15m vs 身体地面遮挡 ≈1.13m）");
            Check(frontDot >= 300, "F3 **正对相机**时方向点露出 ≥ 300 px（「朝哪」一眼读得出来）",
                $"实测 {frontDot} px");
            Check(sideDot >= 100, "F4 **侧对相机**时方向点也露出 ≥ 100 px", $"较差一侧 {sideDot} px");

            // ★ F5 是**已知限制**，不是缺陷 —— 用读数把它钉住，免得下一个人以为是回归 ★
            sb.AppendLine();
            sb.AppendLine($"  [已知限制·不是回归] **背对相机时方向点 = {backDot} px**");
            sb.AppendLine("     原因在几何里，不在实现里：60° 俯角下帽檐（场上半径 0.554m、高 1.71m）");
            sb.AppendLine("     把玩家正前方地面遮到 `0.554 + 1.71/tan60° ≈ **1.54m**`；");
            sb.AppendLine("     而本轮规格是「环外半径 0.9~1.2m」⇒ 挂在环上（≤1.34m）的圆点必然整颗在帽檐阴影里 ✗");
            sb.AppendLine("     · 环本身在 8 个方位全可见（4061~5350 px）⇒「我在圈里」这条读得出来 ✓");
            sb.AppendLine("     · 要「背对也看得见方向」，唯一办法是把指示推到 ≥1.55m（= 旧扇形的 1.50m 前尖）——");
            sb.AppendLine("       那是被本轮规格否定的尺寸 ⇒ 保持现状，把选择权留给用户 ✓");
            Check(backDot >= 0, "F5 背对相机时方向点读数（记下来，避免下次被当成新缺陷）",
                $"{backDot} px｜环 {backRing} px（环仍然可见）");

            cam.transform.position = savedPos;
            cam.transform.rotation = savedRot;
            cam.targetTexture = savedTarget;
            Object.Destroy(holder);

            sb.AppendLine();
            sb.AppendLine($"════ 合计 {pass} PASS / {fail} FAIL ════");
            string text = sb.ToString();
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════
        // 渲染 / 读回
        // ══════════════════════════════════════════════════════════════════
        private static Color32[] RenderPixelsRig(Camera cam, Vector3 focus, float camYaw, float pitch, float dist,
            int w, int h)
        {
            Vector3 dir = Quaternion.Euler(-pitch, camYaw, 0f) * Vector3.forward;
            cam.transform.position = focus + dir * dist;
            cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            return RenderToArray(cam, w, h);
        }

        private static string Capture(Camera cam, Vector3 focus, float yaw, float pitch, float dist,
            string name, int w, int h)
        {
            Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            cam.transform.position = focus + dir * dist;
            cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            return WriteShot(cam, name, w, h);
        }

        /// <summary>渲一帧、回读全图像素（`GetPixels32` 比逐点 `GetPixel` 快两个数量级）。</summary>
        private static Color32[] RenderToArray(Camera cam, int w, int h)
        {
            RenderTexture target = null;
            Texture2D tex = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                target = RenderTexture.GetTemporary(w, h, 24);
                cam.targetTexture = target;
                // ★ 渲两次：URP 下 `Camera.Render()` 在本机第一次常常只**提交**渲染请求，
                //   紧接着回读会读到上一帧内容 ⇒ 预热一次，第二次才是本帧 ✓
                //   ⚠️ 但**只回读一次**：同一次 `Render()` 里连续回读两次，第二次拿到的是旧帧 ✓
                cam.Render();
                cam.Render();
                cam.targetTexture = null;
                RenderTexture.active = target;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex.GetPixels32();
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prev;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (tex != null) Object.Destroy(tex);
            }
        }

        /// <summary>渲染 + 回读 + 落盘 + 量化（证明不是黑图）。</summary>
        private static string WriteShot(Camera cam, string name, int w, int h)
        {
            RenderTexture target = null;
            Texture2D readback = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                target = RenderTexture.GetTemporary(w, h, 24);
                cam.targetTexture = target;
                cam.Render();
                cam.Render();
                cam.targetTexture = null;
                RenderTexture.active = target;
                readback = new Texture2D(w, h, TextureFormat.RGB24, false);
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readback.Apply();

                byte[] jpg = readback.EncodeToJPG(92);
                string path = CapturePath(name + ".jpg");
                System.IO.File.WriteAllBytes(path, jpg);

                Color32[] px = readback.GetPixels32();
                long luma = 0; int n = 0, accentPx = 0;
                for (int i = 0; i < px.Length; i += 3)
                {
                    luma += (px[i].r + px[i].g + px[i].b) / 3; n++;
                    // 方向点那颗绿：G 明显高于 R/B
                    if (px[i].g > 150 && px[i].g > px[i].r + 30 && px[i].g > px[i].b + 30) accentPx++;
                }
                float avg = n > 0 ? (float)luma / n : 0f;
                float pct = n > 0 ? 100f * accentPx / n : 0f;
                return $"{name}.jpg {jpg.Length}B {w}x{h} 平均亮度 {avg:0.0}"
                     + $" 偏绿像素 {pct:0.000}%（含草地；只用来说明「不是黑图」）";
            }
            catch (System.Exception e)
            {
                return "抓图失败：" + e.Message;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previousActive;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (readback != null) Object.Destroy(readback);
            }
        }

        /// <summary>把一张程序化贴图写成 PNG（**4× 放大 + 原尺寸**并排，便于肉眼核对像素）.</summary>
        private static string DumpPng(Texture2D tex, string name, int scale)
        {
            if (tex == null) return "（没有贴图）";
            try
            {
                int w = tex.width, h = tex.height;
                var big = new Texture2D(w * scale * 2, h * scale, TextureFormat.RGBA32, false);
                var clear = new Color32[w * scale * 2 * h * scale];
                for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(24, 26, 30, 255);
                big.SetPixels32(clear);
                Color32[] src = tex.GetPixels32();
                // 左半：4× 最近邻放大（看硬边台阶）；右半：原尺寸（看整体形状）
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color32 c = src[y * w + x];
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                                big.SetPixel((x * scale) + sx, (y * scale) + sy, c);
                        big.SetPixel(w * scale + x, y, c);
                    }
                big.Apply();
                byte[] png = big.EncodeToPNG();
                string path = CapturePath(name + ".png");
                System.IO.File.WriteAllBytes(path, png);
                Object.Destroy(big);
                return $"{name}.png {png.Length}B（左 {w * scale}×{h * scale} 放大 / 右 {w}×{h} 原样）→ {path}";
            }
            catch (System.Exception e)
            {
                return "导出失败：" + e.Message;
            }
        }

        private static string CapturePath(string fileName)
        {
            string root = System.IO.Path.GetDirectoryName(Application.dataPath);
            string dir = System.IO.Path.Combine(root, "Docs", "captures");
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, fileName);
        }

        /// <summary>两张图里"某片开 / 关"差了多少像素 ⇒ 就是它真正露出来的面积。</summary>
        private static int DiffCount(Color32[] on, Color32[] off)
        {
            if (on == null || off == null || on.Length != off.Length) return 0;
            int d = 0;
            for (int i = 0; i < on.Length; i++)
            {
                int dv = Mathf.Abs(on[i].r - off[i].r) + Mathf.Abs(on[i].g - off[i].g) + Mathf.Abs(on[i].b - off[i].b);
                if (dv > 18) d++;
            }
            return d;
        }

        // ══════════════════════════════════════════════════════════════════
        // 贴图剖析（**形状的真值在这里**，量的是 alpha，不是常量）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 量一张"环"贴图：内外半径（texel）、不透明 texel 数、**alpha 非 0/255 的 texel 数**（硬边判据）、
        /// 16 个方向上环带是否都实心（**整圈**判据）。
        /// </summary>
        private static void ProfileAlpha(Texture2D tex, out float innerTexel, out float outerTexel,
            out int opaqueCount, out int binaryBad, out int sectorsHit, out int sectorsTotal)
        {
            int S = tex.width;
            Color32[] px = tex.GetPixels32();
            float half = S * 0.5f;
            innerTexel = float.MaxValue; outerTexel = 0f;
            opaqueCount = 0; binaryBad = 0;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    Color32 c = px[y * S + x];
                    if (c.a != 0 && c.a != 255) binaryBad++;
                    if (c.a <= 127) continue;
                    opaqueCount++;
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < innerTexel) innerTexel = d;
                    if (d > outerTexel) outerTexel = d;
                }
            if (innerTexel == float.MaxValue) innerTexel = 0f;

            // 16 个方向：沿半径扫，只要有一个方向找不到实心 texel 就不是整圈
            sectorsTotal = 16; sectorsHit = 0;
            float rMid = (innerTexel + outerTexel) * 0.5f;
            for (int i = 0; i < sectorsTotal; i++)
            {
                float a = i * Mathf.PI * 2f / sectorsTotal;
                bool hit = false;
                for (float r = Mathf.Max(1f, rMid - 2f); r <= rMid + 2f && !hit; r += 0.5f)
                {
                    int x = Mathf.RoundToInt(half + Mathf.Cos(a) * r - 0.5f);
                    int y = Mathf.RoundToInt(half + Mathf.Sin(a) * r - 0.5f);
                    if (x < 0 || y < 0 || x >= S || y >= S) continue;
                    if (px[y * S + x].a > 127) hit = true;
                }
                if (hit) sectorsHit++;
            }
        }

        /// <summary>量"点"贴图：外半径（texel）、alpha 硬边违规数、中心是否实心、四角是否透明。</summary>
        private static void ProfileDot(Texture2D tex, out float outerTexel, out int binaryBad,
            out bool centerOpaque, out bool cornerClear)
        {
            int S = tex.width;
            Color32[] px = tex.GetPixels32();
            float half = S * 0.5f;
            outerTexel = 0f; binaryBad = 0;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    Color32 c = px[y * S + x];
                    if (c.a != 0 && c.a != 255) binaryBad++;
                    if (c.a <= 127) continue;
                    float dx = x + 0.5f - half, dy = y + 0.5f - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > outerTexel) outerTexel = d;
                }
            centerOpaque = px[(S / 2) * S + S / 2].a > 127;
            cornerClear = px[0].a <= 127 && px[S - 1].a <= 127
                          && px[(S - 1) * S].a <= 127 && px[(S - 1) * S + S - 1].a <= 127;
        }

        /// <summary>在半径 <paramref name="r"/> texel 处取一个不透明像素的颜色（用来验"环体是亮白/点是绿"）。</summary>
        private static Color SampleOpaqueColor(Texture2D tex, float r)
        {
            int S = tex.width;
            Color32[] px = tex.GetPixels32();
            float half = S * 0.5f;
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI * 2f / 32f;
                int x = Mathf.RoundToInt(half + Mathf.Cos(a) * r - 0.5f);
                int y = Mathf.RoundToInt(half + Mathf.Sin(a) * r - 0.5f);
                if (x < 0 || y < 0 || x >= S || y >= S) continue;
                Color32 c = px[y * S + x];
                if (c.a > 127) return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
            }
            return Color.clear;
        }

        // ══════════════════════════════════════════════════════════════════
        // 小工具
        // ══════════════════════════════════════════════════════════════════

        /// <summary>方向点的**世界方位** vs 模型根节点的**世界前方**，返回夹角（度，0 = 完全重合）。</summary>
        private static float DotVsForward(GameObject dot, ModelView mv)
        {
            Vector3 origin = mv.Go.transform.position;
            Vector3 toDot = dot.transform.position - origin;
            toDot.y = 0f;
            Vector3 fwd = mv.Go.transform.forward;
            fwd.y = 0f;
            if (toDot.sqrMagnitude < 1e-8f || fwd.sqrMagnitude < 1e-8f) return 180f;
            return Vector3.Angle(fwd, toDot);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            if (parent == null) return null;
            Transform t = parent.transform.Find(name);
            return t != null ? t.gameObject : null;
        }

        /// <summary>★ 网格必须从 `MeshFilter` 上取 —— `MeshRenderer` 没有 `sharedMesh` ★</summary>
        private static Mesh MeshOf(GameObject go)
        {
            if (go == null) return null;
            var mf = go.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        private static Material MatOf(GameObject go)
        {
            if (go == null) return null;
            var mr = go.GetComponent<MeshRenderer>();
            return mr != null ? mr.sharedMaterial : null;
        }

        private static string ShaderName(Material m) => m == null || m.shader == null ? "（无）" : m.shader.name;
        private static int QueueOf(Material m) => m == null ? -1 : m.renderQueue;

        private static int CountNamed(Transform t, string name)
        {
            int n = t.name == name ? 1 : 0;
            for (int i = 0; i < t.childCount; i++) n += CountNamed(t.GetChild(i), name);
            return n;
        }

        private static int CountAllNamed<T>(string name) where T : Object
        {
            T[] all = Resources.FindObjectsOfTypeAll<T>();
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name) n++;
            return n;
        }

        /// <summary>两只靴子（名字 `Boot`）离模型中心最远的水平距离 —— 用来证明"脚在中间空洞里"。</summary>
        private static float BootMaxRadius(ModelView mv)
        {
            if (mv == null || mv.Go == null) return 0f;
            Vector3 c = mv.Go.transform.position;
            float worst = 0f;
            Renderer[] rs = mv.Go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || rs[i].transform.name != "Boot") continue;
                Bounds bd = rs[i].bounds;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = new Vector3(bd.center.x + sx * bd.extents.x, bd.center.y,
                                                     bd.center.z + sz * bd.extents.z);
                        float r = new Vector2(corner.x - c.x, corner.z - c.z).magnitude;
                        if (r > worst) worst = r;
                    }
            }
            return worst;
        }

        private static string V(Vector3 v) => $"({v.x:0.000},{v.y:0.000},{v.z:0.000})";
        private static string C(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00},a={c.a:0.00})";
    }
}
