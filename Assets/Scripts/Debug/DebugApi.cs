using System;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 自动化验证入口：给 unity_drive / 外部脚本提供"可调用的动作 + 可读的状态"。
    /// 所有方法都只走 RunSession 的公共 API，不绕过任何玩法规则。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DebugApi : MonoBehaviour
    {
        /// <summary>
        /// v131：**自举**（自诊断修正）。
        ///
        /// 问题：全工程**没有任何地方创建 DebugApi** —— 没有 `AddComponent`、也没有自举钩子。
        /// 于是它是否存在完全取决于场景里有没有手动摆一个 ✗
        /// 症状极具迷惑性：`unity_drive` 报"窗口被最小化/主线程没运行"，
        /// 而实际用 `IsIconic` 查过，窗口**根本没最小化** —— 真正原因是**组件不在，方法自然找不到** ✗
        ///
        /// 这类"工具本身悄悄失效"最费时间：你会以为是环境问题，然后去查窗口、查端口，
        /// 而问题在"对象不存在"。加一个自举钩子之后，play 模式下它必然存在 ✓
        ///
        /// 用 `AfterSceneLoad`（不是 `BeforeSceneLoad`）：GameBootstrap 在场景加载时才建好，
        /// 早于它创建会让 `Boot` 属性一开始就取不到实例。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<DebugApi>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("DebugApi");
            go.AddComponent<DebugApi>();

            // ★ v143：**不要 DontDestroyOnLoad** ★
            //
            // 我原本加它是想"让它活过场景切换"，但那正是 bug 的来源：
            // 实例活过了一次 play 会话 → 它的 `_auditAt` 已经是 float.MaxValue（审计只跑一次）
            // → 下一次 play 时 `AutoCreate` 因 `FindAnyObjectByType` 找到这个旧实例而**早退**
            // → **新会话里审计永远不跑** ✗
            //
            // 症状极具迷惑性：审计**有时**工作（第一次 play 时），之后再也不出现，
            // 让人以为是"环境不稳"而不是"对象是旧的" ✗
            // 实测证据：审计出现在 `play` 之后 **1 秒**（设计是 8 秒）——
            // 说明那个实例的计时是从**上一次**会话延续过来的 ✓
            //
            // 一个纯诊断组件不需要活过场景切换：本工程只有一个场景。
            // 去掉之后每次 play 都是新实例、新计时 ✓
            // go.AddComponent<DebugApi>() 之后不做 DontDestroyOnLoad
        }

        /// <summary>
        /// v133：改成 `Update` 里一次性触发（原来是 `Start()` + 协程等 6 秒）。
        ///
        /// 上一轮那个版本**没有触发** —— 控制台里只有 2 行启动日志，连"配置表载入完成"都没有，
        /// 说明那一局根本没有完整启动，协程自然也没跑完。
        /// 换成 `Update` 计数：只要游戏渲染过几帧就一定会执行，
        /// 不依赖"场景在 6 秒内建好"这个假设 ✗
        /// </summary>
        private float _auditAt = -1f;

        private void Update()
        {
            // v172：表现层探针必须放在**最前面** ✗
            // 下面那三段提前 return 会让本 Update 在绝大多数帧里什么都不做 ✓
            TickAnimProbe();

            if (_auditAt < 0f) { _auditAt = Time.realtimeSinceStartup + 8f; return; }
            if (Time.realtimeSinceStartup < _auditAt) return;
            _auditAt = float.MaxValue;      // 只跑一次
            try { AuditModelHeights(); }
            catch (Exception e) { Debug.LogWarning("[Seedfall] 模型审计失败：" + e.Message); }
            try { AuditRotations(); }
            catch (Exception e) { Debug.LogWarning("[Seedfall] 旋转审计失败：" + e.Message); }
        }

        private void AuditModelHeights()
        {
            // ★ v150：改成**扫描所有 Renderer + 按世界高度降序报前 10 名** ★
            //
            // 为什么推翻之前两版：
            //   第一版：只看 `MDL_` 前缀，且用 `h > 8f` 单一阈值 → 报了一堆假阳性
            //   第二版：加了"场地内"判据 → 假阳性少了，但**读数本身不可信** ✗
            //     实测：围栏报 110.00m / localScale 135.8，比值恒为 0.81（= 网格自身高度），
            //     说明 `Renderer.bounds.size.y` 没有反映最终世界尺寸 ✗
            //     我据此改了一处**本来正确**的代码，还改坏了（81m → 110m）✓ 已回退
            //
            // 所以这一版换思路：**不猜阈值、不猜对象类型，直接把最大的几个列出来** ✓
            // "谁挡住了画面"这个问题，答案一定在**高度最大的前几名**里 ✗
            // 让它自己排出来，比我逐个假设（围栏？豌豆射手？蒙皮网格？）快得多 ✓
            //
            // 读数改用**渲染器的世界 AABB 并集**（而不是单个 bounds.size.y）✓
            // 并且把 `lossyScale` 一起报出来，便于和 localScale 对照 ✓
            var all = new System.Collections.Generic.List<(string name, float h, float scale, Vector3 pos)>();
            Renderer[] rs = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || r.transform == null) continue;
                // 跳过纯 UI / 不可见的
                if (r is CanvasRenderer) continue;
                Bounds b = r.bounds;                          // 世界空间 AABB ✓
                if (b.size.y <= 0.001f && b.size.x <= 0.001f) continue;
                // 归到顶层命名对象（去掉子件名），避免同一模型的 20 个子件刷屏
                Transform t = r.transform;
                while (t.parent != null && !t.parent.name.StartsWith("MDL_")
                       && !t.parent.name.StartsWith("SCN_") && t.parent.parent != null) t = t.parent;
                all.Add((t.name, b.size.y, r.transform.lossyScale.x, b.center));
            }
            all.Sort((a, b) => b.h.CompareTo(a.h));

            var sb = new StringBuilder();
            sb.Append("[Seedfall] 高度排行（世界 AABB，前 10）：共 ").Append(all.Count).Append(" 个渲染器\n");
            for (int i = 0; i < all.Count && i < 10; i++)
                sb.Append("  ").Append(i + 1).Append(". ").Append(all[i].name)
                  .Append("  高=").Append(all[i].h.ToString("0.00")).Append("m")
                  .Append("  lossyScale=").Append(all[i].scale.ToString("0.0"))
                  .Append("  位置=").Append(all[i].pos.x.ToString("0.0")).Append(",")
                  .Append(all[i].pos.z.ToString("0.0")).Append('\n');

            GameBootstrap bootForTime = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            RunSession sess = null;
            try { sess = bootForTime != null ? bootForTime.Session : null; } catch { sess = null; }
            if (sess != null)
                sb.Append("  [时间] RunTime=").Append(sess.RunTime.ToString("0.0")).Append("s")
                  // ★ v173：这一栏原来写"回合="✗ —— 而"回合"这个词在本工程里已不存在 ✓
                  // 底层是 `WaveIndex` ✓ 计时制下【每分钟 +1】⇒ 叫"波次"✓
                  // （★ 同一条日志里"派生波"也是它 ✓ 所以原来那两个名字指同一个量 ✗）
                  .Append("  波次=").Append(sess.WaveIndex)
                  .Append("  暂停=").Append(sess.Paused)
                  .Append("  阶段=").Append(sess.Phase).Append('\n');
            Debug.Log(sb.ToString());
        }

        // ────────────────────────────────────────────────────────────────────────────
        // v167：批量旋转审计（只报告，**不修任何东西**）
        // ────────────────────────────────────────────────────────────────────────────

        private struct RotRow
        {
            public string Name, Path, Cat;
            public Vector3 Rot;
            public float Scale, Dev, Ratio;
        }

        /// <summary>
        /// v167：**批量旋转审计** —— 找出"倒下的模型"。
        ///
        /// 为什么需要它：报的是"模型存在倒下的 bug"，而场景里的模型有几十个
        /// （16 植物 × 3 阶段 + 敌人 5 + 玩家 + 商店车 + 工具 3 + 环境 26 …），
        /// **逐个 probe 名字不可行** ✗
        /// 仿 AuditModelHeights 的形状：不猜阈值、不猜对象，**分类判偏差 + 让它们自己排出来** ✓
        ///
        /// ★ 判据必须分类 ★（不同来源的"正确"标准不同，一刀切必然误报）
        ///   · `MDL_*`  —— FBX 外层。**资产里根节点就自带 -90°绕X**（euler 270）：
        ///       实测依据（FBX 二进制直读）：GlobalSettings 里 `UpAxis=1`（文件声明 Y-up），
        ///       根 Model `Armature` 的 `Lcl Rotation = [-90,0,0]`、`Lcl Scaling = [100,100,100]`。
        ///       ⇒ 那个 270 **不是 Unity 导入器加的轴转换**，而是 Blender 导出时留在根对象上的旋转 ✗
        ///       ⇒ 所以「pitch≈270」和「pitch≈0」（网格本来就是 Y-up/米制）**都合法**，roll 必须 ≈0，yaw 任意 ✓
        ///   · `ANIM_*` —— 内层。旋转由 `AnimationClip.SampleAnimation` 每帧写（ModelView.cs:526），
        ///       所以**不判它自己**；但"外层 × 内层"的净结果就写在它的**世界旋转**里，必须判 ✓
        ///       （全工程写内层旋转的只有 ModelView.cs:114 置 identity 和 :526 采样两处）
        ///   · 其余     —— 手写 GameObject。identity 或纯 yaw 之外一律算偏差 ✓
        ///
        /// ★ 为什么还要报世界 AABB 的"立姿比" ★
        /// 旋转读数是**原因**，AABB 是**结果**：姿势对的模型必然"高 > 宽/深"。
        /// 只报旋转会陷进"那 270 到底该不该有"的争论（本文件上面两版就绕进去了 ✗）；
        /// 立姿比一眼就能看出"谁躺下了"，而且**不依赖任何判据假设** ✓
        /// </summary>
        public string AuditRotations()
        {
            var sb = new StringBuilder();
            sb.Append("[Seedfall] 旋转审计 v167（分类判偏差 + 世界 AABB 立姿比）\n");

            // ── ① 场景实例 ──
            var rows = new System.Collections.Generic.List<RotRow>();
            Transform[] ts = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            int cModel = 0, cAnim = 0, cOther = 0;
            for (int i = 0; i < ts.Length; i++)
            {
                Transform t = ts[i];
                if (t == null || t.gameObject == null) continue;
                string nm = t.name;
                Vector3 e = t.localRotation.eulerAngles;
                float sc = t.localScale.x;

                if (nm.StartsWith("ANIM_"))
                {
                    cAnim++;
                    Transform outer = t.parent;
                    // ★ v170 修正判据：内层要看的是【它自己的贡献】，不是它的世界旋转 ★
                    //
                    // 上一版我拿"内层的世界旋转 vs 仅偏航"当判据 ✗ 结果**全假阳性**：
                    //   实测 1017 个 ANIM_ 全部被报成"偏差 90°" —— 因为外层本来就该带 -90 ✓
                    //   那个 -90 是【资产根的静态旋转】，摆正模型靠的就是它 ✓
                    //   ⇒ 拿"世界旋转"判内层，等于把外层的正确姿态当成内层的错误 ✗
                    //
                    // 正确的判据（也正是 v169 修复要建立的不变量）：
                    //   **内层 `localRotation` 必须是 identity 或纯 yaw** ✓
                    //   ——"根的整体姿态"是外层的职责，内层只该被动画驱动 ✓
                    //   修复前：玩家内层 local=Rx(270) ⇒ 偏差 90° ✗（而植物内层=identity ⇒ 0° ✓）
                    //   修复后：全部 0° ✓  ⇒ 这条判据**恰好能区分修复前后** ✓★
                    Vector3 le = t.localRotation.eulerAngles;
                    rows.Add(new RotRow
                    {
                        Name = nm, Path = PathOf(t), Cat = "ANIM_内层自身",
                        Rot = le, Scale = sc,
                        Dev = Quaternion.Angle(t.localRotation, Quaternion.Euler(0f, le.y, 0f)),
                        Ratio = outer != null ? UprightRatio(outer.gameObject) : 0f
                    });
                }
                else if (nm.StartsWith("MDL_"))
                {
                    cModel++;
                    float d270 = Quaternion.Angle(t.localRotation, Quaternion.Euler(270f, e.y, 0f));
                    float d0 = Quaternion.Angle(t.localRotation, Quaternion.Euler(0f, e.y, 0f));
                    rows.Add(new RotRow
                    {
                        Name = nm, Path = PathOf(t), Cat = d270 <= d0 ? "MDL_(270 合法)" : "MDL_(0 合法)",
                        Rot = e, Scale = sc, Dev = Mathf.Min(d270, d0), Ratio = UprightRatio(t.gameObject)
                    });
                }
                else
                {
                    if (IsUnderCanvas(t)) continue;
                    if (t.GetComponent<Renderer>() == null) continue;   // 只报看得见的
                    // ★ v170：跳过 FBX 木偶的部件 ★
                    //   实测：上一版前 10 名被 `Part_Foot_R`(180°) / `Part_Calf_L`(119°) /
                    //   `Part_Thigh_*` / `Part_UpperArm_*` 刷满 ✗
                    //   它们挂在 `MDL_*/ANIM_*` 底下，是**资产里的木偶部件**，
                    //   旋转由 FBX 层级与动画曲线决定，不属于"手写 GameObject" ✓
                    //   混进来就变成 29 条假阳性，把真正的"手写对象写错"淹掉 ✗
                    if (HasModelAncestor(t)) continue;
                    float dev = Quaternion.Angle(t.localRotation, Quaternion.Euler(0f, e.y, 0f));
                    if (dev <= 1f) continue;                            // 合格的不入榜，否则几百个正常对象刷屏
                    cOther++;
                    rows.Add(new RotRow
                    {
                        Name = nm, Path = PathOf(t), Cat = "手写", Rot = e, Scale = sc,
                        Dev = dev, Ratio = UprightRatio(t.gameObject)
                    });
                }
            }
            rows.Sort((a, b) => b.Dev.CompareTo(a.Dev));

            sb.Append("① 场景实例：MDL_ ").Append(cModel).Append(" 个｜ANIM_ ").Append(cAnim)
              .Append(" 个｜手写且偏差>1° ").Append(cOther).Append(" 个（合格的手写对象不入榜）\n");
            sb.Append("   偏差降序前 10（偏差 = 与该类正确姿势的最小夹角，度）：\n");
            for (int i = 0; i < rows.Count && i < 10; i++)
            {
                RotRow r = rows[i];
                sb.Append("   ").Append(i + 1).Append(". ").Append(r.Name)
                  .Append("  [").Append(r.Cat).Append(']')
                  .Append("  偏差=").Append(r.Dev.ToString("0.0")).Append("°")
                  .Append("  rot=(").Append(r.Rot.x.ToString("0.0")).Append(',')
                  .Append(r.Rot.y.ToString("0.0")).Append(',').Append(r.Rot.z.ToString("0.0")).Append(')')
                  .Append("  scale=").Append(r.Scale.ToString("0.###"))
                  .Append("  立姿比=").Append(r.Ratio.ToString("0.00"))
                  .Append(r.Dev > 5f ? "  << 超阈值" : "").Append('\n');
                sb.Append("       路径 ").Append(r.Path).Append('\n');
            }

            int fell = 0, wide = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Cat == "ANIM_内层自身" && rows[i].Dev > 45f) fell++;
                if (rows[i].Ratio > 0f && rows[i].Ratio < 0.5f) wide++;
            }
            sb.Append("   小计：内层自身仍有额外旋转(>45°)的 ").Append(fell).Append(" 个")
              .Append("（≈90° = 那个 -90 被叠加了两次 ⇒ 翻倒 ✗；v169 修复后应为 0）｜立姿比<0.5 的 ").Append(wide)
              .Append(" 个（宽扁的树/灌木本来就会低，要结合 ④ 一起看）\n");

            // ── ② 预制体清单（Resources 直读，**不实例化**，零副作用）──
            //
            // ★ v170 修正判据：**根节点的角度判不出"站/躺"** ★
            //
            // 上一版把"根 pitch ∉ {270, 0}"当成可疑 ✗ 实测报了 34 个 —— **全是假阳性** ✓
            //   因为那个值是**等价类**里的一种写法：
            //     Rx(270) 可写成 (270,0,0) 或 (270,180,180)
            //     Rx(180) 可写成 (180,0,0) 或 **(0,180,180)**   ← 实测这 34 个正是这个 ✓
            //   而装配后算 AABB：player 根 270 → Y 最高（站着）；imp/pig/brute 根 **180** → 也 Y 最高（站着）✓
            //   ⇒ 两族都可能是**正确**的 —— 取决于那套网格自己按哪个上轴建的 ✗
            //   ⇒ 能判"站/躺"的只有【装配后的 AABB】，那是 ④ 的活（对实例）✓
            //
            // 所以这一节只做它**真正能**做的事：**只挑出"根旋转不是绕 X 的 90° 整数倍"的怪值** ✓
            // （那种才是真的"导出参数写歪了"——比如 37.5° 之类 ✗）
            string[] folders = { "Art/Characters", "Art/Plants", "Art/Environment", "Art/Props", "Art/Tools", "Art/Anim" };
            int total = 0, atOther = 0, famYaw = 0;
            int[] fam = new int[4];                       // Rx(0) / Rx(90) / Rx(180) / Rx(270) 四族
            var odd = new System.Collections.Generic.List<string>();
            for (int f = 0; f < folders.Length; f++)
            {
                GameObject[] pfs = Resources.LoadAll<GameObject>(folders[f]);
                if (pfs == null) continue;
                for (int i = 0; i < pfs.Length; i++)
                {
                    GameObject pf = pfs[i];
                    if (pf == null || pf.transform == null) continue;
                    total++;
                    Vector3 e = pf.transform.localRotation.eulerAngles;
                    Quaternion q = pf.transform.localRotation;
                    // ★ v170b：判据是「pitch 与 roll 必须是 90° 的整数倍」（yaw 任意）★
                    //   上一版拿 euler 三元组逐分量去和 Euler(p, e.y, 0) 比 ✗
                    //   实测把 Rx(180) 误判成怪值 34 个 —— 因为 Unity 把它报成 (0,180,180)：
                    //   **那个 y=180 是等价表示的副产品，不是真偏航** ✗
                    //   而现在这个判据天然吃等价表示：180 和 0 都是 90 的整数倍 ✓
                    //   真正的怪值（导出参数写歪，如 37.5°）才会被它挑出来 ✓
                    float fx = DistTo90(e.x), fz = DistTo90(e.z);
                    string line = folders[f] + "/" + pf.name
                        + "  rot=(" + e.x.ToString("0.0") + "," + e.y.ToString("0.0") + "," + e.z.ToString("0.0") + ")"
                        + "  scale=" + pf.transform.localScale.x.ToString("0.####")
                        + "  pitch 距 90° 倍数 " + fx.ToString("0.0") + "°，roll 距 " + fz.ToString("0.0") + "°";
                    if (fx <= 2f && fz <= 2f)
                    {
                        // ★ 归类要按**等效**的绕 X 角度，不能按 euler 的 x 分量 ✗
                        //   实测：Rx(180) 被 Unity 报成 (0,180,180) ⇒ 照 e.x 归会数进"Rx(0)" ✗
                        //   改用轴角：纯 X 轴旋转才能读出唯一的有符号角 ✓
                        q.ToAngleAxis(out float ang, out Vector3 ax);
                        if (ang < 0.01f) fam[0]++;
                        else if (Mathf.Abs(ax.x) > 0.99f)
                            fam[Mathf.RoundToInt(Mathf.Repeat(ax.x > 0f ? ang : -ang, 360f) / 90f) % 4]++;
                        else famYaw++;                      // 带偏航的（合法，但要单独计数）
                    }
                    else { atOther++; odd.Add(line); }
                }
            }
            sb.Append("② 预制体根节点旋转（Resources 直读，未实例化）：共 ").Append(total).Append(" 个 FBX\n");
            sb.Append("   绕 X 的 90° 整数倍（= 正确的轴对齐写法，按**等效轴角**归类）：Rx(0) ").Append(fam[0])
              .Append("｜Rx(90) ").Append(fam[1]).Append("｜Rx(180) ").Append(fam[2])
              .Append("｜Rx(270) ").Append(fam[3])
              .Append("｜带偏航 Rx(yaw) ").Append(famYaw).Append('\n');
            sb.Append("   ★ 注意：**270 与 180 两族都可能是正确的** —— 取决于那套网格按哪个上轴建模。\n")
              .Append("     资产层面实测：根 270 的 player、根 180 的 imp/pig/brute，装配 AABB 全都是 Y 最高（站着）✓\n")
              .Append("     ⇒ 「站/躺」只能靠 ④ 的立姿断言判，根节点角度判不出来 ✗\n")
              .Append("   **不是轴对齐的怪值：").Append(atOther).Append(" 个**\n");
            for (int i = 0; i < odd.Count; i++) sb.Append("     !! ").Append(odd[i]).Append('\n');

            // ★ 已知的资产异类：**无条件**标出来，并写明【为什么它不构成 bug】★
            //   理由：将来有人改 brute 的动画时，这条例外会变成陷阱 —— 留一条说明比留一个"未解释的异常"安全 ✓
            sb.Append("     └ 已知的资产异类（不是 bug，但记一笔）：Art/Anim/brute_*.fbx 的根是 AxisFix_brute = Rx(180)，\n")
              .Append("        是 Anim 这批里唯一不是 Rx(270) 的一族 ✓ 它【不构成倒下的 bug】，两条理由：\n")
              .Append("        ① 子节点已补偿 —— 把层级装配起来算 AABB 仍是 Y 最高（88.6/122.1/64.2，站着）✓\n")
              .Append("        ② brute_* 片段【没有根曲线】⇒ 内层不会二次叠加 ⇒ 净姿态 = 预制体根本身 ✓\n")
              .Append("        ⚠ 陷阱：一旦 brute 的动画重导后带上根曲线，它就会和 -90 那族一样被叠加两次 ✗\n")
              .Append("          判据不是「根节点是多少度」，而是「内层自身贡献(①) + 立姿(④)」✓\n");

            // ── ③ 个案：ANIM_player 的 Z=180 到底是什么 ──
            Transform animP = FindTransform("ANIM_player");
            Transform mdlP = FindTransform("MDL_player");
            if (animP != null)
            {
                Transform outer = animP.parent;
                // ★ v170：判据换成【内层自身的贡献】★
                //   上一版用"内层世界旋转 vs 仅偏航" ⇒ 对一个**正确**的模型也报 90° ✗
                //   因为外层本来就该带那 -90（资产根的静态旋转）✓
                //   ⇒ 正确的问法是：**内层自己额外转了多少**（应为 0）✓
                float dev = Quaternion.Angle(animP.localRotation,
                                             Quaternion.Euler(0f, animP.localEulerAngles.y, 0f));
                sb.Append("③ 个案 ANIM_player（probe 读的是 transform.eulerAngles = **世界**旋转）:\n");
                sb.Append("   外层 ").Append(outer != null ? outer.name : "(无)")
                  .Append("  local=").Append(EulerStr(outer))
                  .Append("  世界=").Append(EulerWorldStr(outer)).Append('\n');
                sb.Append("   内层 ").Append(animP.name)
                  .Append("  local=").Append(EulerStr(animP))
                  .Append("  q=").Append(QuatStr(animP.localRotation))
                  .Append("  世界=").Append(EulerWorldStr(animP)).Append('\n');
                sb.Append("   内层【自身】额外贡献=").Append(dev.ToString("0.0")).Append("°")
                  .Append("（应为 0 = 只应用了一次）\n");
                if (dev < 5f)
                    sb.Append("   >> 判定：内层没有额外旋转 ⇒ 那个 -90 只被应用了**一次** ⇒ 姿态正确 ✓\n")
                      .Append("      这正是 ModelView.cs:558-564（v169）在起作用：采样后把根旋转的 x/z 清零、保留 y ✓\n");
                else if (dev > 45f)
                    sb.Append("   >> 判定：内层额外转了 ").Append(dev.ToString("0"))
                      .Append("° ⇒ 与外层叠加 ⇒ 模型翻倒 ✗（v169 修复未生效 / 被回退）\n")
                      .Append("      来源：动画片段里 path 为空的那条常量根曲线 quat=(-0.7071,0,0,0.7071) = 资产根那个 -90 ✓\n")
                      .Append("      写内层旋转的代码只有 ModelView.cs:114(置 identity) 与 :526(SampleAnimation) ⇒ 是动画数据，不是代码写错 ✗\n");
                sb.Append("   [给 probe 用户的提醒] 直接 probe ANIM_player 会看到**世界**旋转：\n")
                  .Append("      正确时 ≈ (270, 偏航, 0)；翻倒时 ≈ (0, 偏航+180, 180) —— 后者那个 180 在 **X 上不是 Z 上** ✗\n")
                  .Append("      因为 Unity 的 euler.x 按 asin 取、落在 [0,90]∪(270,360] ⇒ 取不到 180 ✓\n");
            }
            else
            {
                sb.Append("③ 场景里没有 ANIM_player（会话未启动或走的是图元回退）\n");
            }
            if (mdlP == null && animP == null)
                sb.Append("   提示：本审计要 play 且会话已启动才看得到 MDL_/ANIM_ 三层结构\n");

            // ── ④ 立姿断言（PASS/FAIL；**与根旋转无关**，专抓"翻转/放倒"）──
            //
            // ★ 这里必须用两条规则，只写"Y 是最高轴"是【抓不到 180° 翻转】的 ★
            //
            //   AABB 的**尺寸**在"绕 X 翻 180°"下【一模一样】✗
            //   ⇒ 只判"Y 跨度是不是最大"，上下颠倒的模型照样 PASS ✓（而这次事故正是这个形态 ✗）
            //   ⇒ 必须加第二条：**底面 min.y ≈ 0**（模型站在地面上）
            //       站着的几何是 y∈[0,h]；翻 180° 后变成 y∈[-h,0] ⇒ min.y 立刻变负 ✓
            //   ⇒ 分工（v170b：**只有人形才硬判**）：
            //       规则1（底面 min.y）—— 硬判**只对 MDL_player** ✓
            //         其余 MDL_ 只做**提示**：实测 fence 底面 -0.36m、farm_plot -0.12m，
            //         那是**设计性的下沉**（耕地要齐平地面、栅栏柱要埋进土里）✗
            //         上一版把它们算成 24 条 FAIL ⇒ 判定被淹没，真正的信号看不见了 ✓
            //       规则2（Y 是最高轴）—— 同样只对**人形**（围栏/耕地/池塘/小猪/树本来就"宽 > 高"）✓
            //   ⇒ 两条一起，才真正与"根节点 rotation.x 是 270 还是 0"解耦 ✓★
            int groundPass = 0, playerFail = 0, advisory = 0;
            bool playerFound = false, playerUpright = false;
            float playerMinY = 0f;
            var bad = new System.Collections.Generic.List<string>();
            var flat = new System.Collections.Generic.List<RotRow>();
            Transform[] allForStand = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < allForStand.Length; i++)
            {
                Transform t = allForStand[i];
                if (t == null || !t.name.StartsWith("MDL_")) continue;
                Bounds b;
                if (!TryWorldBounds(t.gameObject, out b)) continue;
                bool uprightSize = b.size.y >= b.size.x && b.size.y >= b.size.z;
                bool onGround = b.min.y > -0.25f;
                bool humanoid = t.name == "MDL_player";
                if (onGround) groundPass++;
                else if (!humanoid) advisory++;
                if (humanoid)
                {
                    playerFound = true; playerUpright = uprightSize; playerMinY = b.min.y;
                    if (!uprightSize || !onGround)
                    {
                        playerFail++;
                        bad.Add("MDL_player 立姿异常：Y " + (uprightSize ? "是" : "**不是**")
                                + "最高轴，底面 min.y=" + b.min.y.ToString("0.00")
                                + "m，尺寸 " + SizeStr(b.size));
                    }
                }
                flat.Add(new RotRow
                {
                    Name = t.name, Path = PathOf(t), Cat = "立姿",
                    Rot = b.size, Scale = b.min.y, Dev = 0f,
                    Ratio = b.size.y / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z))
                });
            }
            flat.Sort((x, y) => x.Ratio.CompareTo(y.Ratio));
            if (!playerFound) playerFail = -1;
            bool standOk = playerFail == 0;
            sb.Append("④ 立姿断言（与根旋转无关）：场景里 MDL_ 共 ").Append(flat.Count)
              .Append(" 个｜底面在地面以上的 ").Append(groundPass).Append('/').Append(flat.Count).Append('\n');
            sb.Append("   硬判只对【人形 MDL_player】：Y 必须是最高轴 **且** 底面 min.y > -0.25m\n")
              .Append("   （其余 MDL_ 只做提示：耕地要齐平地面、栅栏柱要埋进土里，底面为负是**设计**✗）\n");
            for (int i = 0; i < bad.Count; i++) sb.Append("   FAIL ").Append(bad[i]).Append('\n');
            sb.Append("   [提示] 底面低于地面 0.25m 以上的非人形模型 ").Append(advisory)
              .Append(" 个（fence/farm_plot 这类，属设计，不参与判定）\n");
            sb.Append("   立姿比最低的 5 个（宽扁道具本来就低，结合名字看）：\n");
            for (int i = 0; i < flat.Count && i < 5; i++)
                sb.Append("     ").Append(flat[i].Name)
                  .Append("  立姿比=").Append(flat[i].Ratio.ToString("0.00"))
                  .Append("  尺寸=").Append(SizeStr(flat[i].Rot))
                  .Append("  min.y=").Append(flat[i].Scale.ToString("0.00")).Append("m\n");
            sb.Append("   判定：").Append(standOk ? "PASS" : "FAIL")
              .Append("（player ").Append(playerFail < 0 ? "未找到（无会话）"
                      : (playerFail == 0 ? "立姿 PASS  尺寸最高轴=Y  底面 min.y=" + playerMinY.ToString("0.00") + "m"
                                         : "立姿 FAIL"))
              .Append("）\n");
            if (playerFound && playerFail > 0)
                sb.Append("   >> player 被判为【不是站着】⇒ 与 ③ 的内层贡献一起看：\n")
                  .Append("      ModelView.cs:558-564（v169）若已生效，③ 应为 0.0° 且这里应为 PASS ✓\n");

            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        private static string EulerStr(Transform t)
        {
            if (t == null) return "(无)";
            Vector3 e = t.localEulerAngles;      // ★ v170：这里要的是 **local** ✗
            return "(" + e.x.ToString("0.0") + "," + e.y.ToString("0.0") + "," + e.z.ToString("0.0") + ")";
        }

        /// <summary>世界旋转的 euler —— 与 `unity_probe` 报的同一个量（它读 t.eulerAngles）。</summary>
        private static string EulerWorldStr(Transform t)
        {
            if (t == null) return "(无)";
            Vector3 e = t.eulerAngles;
            return "(" + e.x.ToString("0.0") + "," + e.y.ToString("0.0") + "," + e.z.ToString("0.0") + ")";
        }

        /// <summary>祖先里有没有模型根（MDL_/ANIM_）—— 用来把 FBX 木偶部件与手写对象分开。</summary>
        private static bool HasModelAncestor(Transform t)
        {
            for (Transform p = t.parent; p != null; p = p.parent)
                if (p.name.StartsWith("MDL_") || p.name.StartsWith("ANIM_")) return true;
            return false;
        }

        private static string EulerStr(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            return "(" + e.x.ToString("0.0") + "," + e.y.ToString("0.0") + "," + e.z.ToString("0.0") + ")";
        }

        private static string QuatStr(Quaternion q)
        {
            return "(" + q.x.ToString("0.0000") + "," + q.y.ToString("0.0000") + ","
                 + q.z.ToString("0.0000") + "," + q.w.ToString("0.0000") + ")";
        }

        private static Transform FindTransform(string name)
        {
            Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == name) return all[i];
            return null;
        }

        private static string PathOf(Transform t)
        {
            if (t == null) return "(null)";
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            for (int guard = 0; p != null && guard < 8; guard++) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        private static bool IsUnderCanvas(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.GetComponent<Canvas>() != null) return true;
            return false;
        }

        /// <summary>世界 AABB 的"立姿比" = 高 ÷ max(宽,深)。站着的模型必然 &gt; 1；躺下的接近 0。</summary>
        private static float UprightRatio(GameObject go)
        {
            Bounds b;
            if (!TryWorldBounds(go, out b)) return 0f;
            float w = Mathf.Max(b.size.x, b.size.z);
            return w > 0.0001f ? b.size.y / w : 0f;
        }

        /// <summary>一组渲染器的世界 AABB 并集。返回 false = 一个渲染器都没有。</summary>
        private static bool TryWorldBounds(GameObject go, out Bounds b)
        {
            b = new Bounds();
            if (go == null) return false;
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || r.transform == null) continue;
                if (IsUnderCanvas(r.transform)) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>某个欧拉分量到最近的 90° 整数倍的距离（度）。判"轴对齐"用。</summary>
        private static float DistTo90(float deg)
        {
            float m = Mathf.Repeat(deg, 90f);
            return Mathf.Min(m, 90f - m);
        }

        private static string SizeStr(Vector3 s)
        {
            return s.x.ToString("0.00") + "×" + s.y.ToString("0.00") + "×" + s.z.ToString("0.00") + "m";
        }

        [SerializeField] private GameBootstrap boot;

        private GameBootstrap Boot
        {
            get
            {
                if (boot != null) return boot;
                for (int i = 0; i < GameBootstrap.Live.Count; i++)
                {
                    if (GameBootstrap.Live[i] != null) { boot = GameBootstrap.Live[i]; return boot; }
                }
                boot = GetComponent<GameBootstrap>();
                if (boot == null) boot = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
                return boot;
            }
        }

        private RunSession S => Boot != null ? Boot.Session : null;

        /// <summary>策略矩阵 + 确定性校验，结果写入 Docs/reports/ 并返回报告路径。</summary>
        public void RunRegression(string seedText)
        {
            ulong seed = (ulong)ParseInt(seedText, 20240607);
            TableDatabase.Load(true); // 回归必须读当前磁盘上的配置，而不是缓存的旧表
            string report = RunSimulator.BuildRegressionReport(seed, out bool det);
            string path = RunSimulator.WriteReport(report);
            Log.Info($"REGRESSION {(det ? "PASS" : "FAIL")} → {path}\n{report}");
        }

        public string Status()
        {
            RunSession s = S;
            if (s == null)
                return Fail($"会话未启动（Boot={(Boot == null ? "null" : Boot.name)} Live={GameBootstrap.Live.Count} " +
                            $"StartRun={GameBootstrap.StartRunCount} playing={Application.isPlaying}）");
            World w = s.World;
            var sb = new StringBuilder();
            sb.Append($"phase={s.Phase} minute={s.WaveIndex} t={s.RunTime:0.0} ");
            sb.Append($"hp={w.Player.Health.Hp:0}/{w.Player.Health.MaxHp:0} shield={w.Player.Health.Shield:0}/{w.Player.Health.MaxShield:0} ");
            sb.Append($"level={s.Progress.Level} exp={s.Progress.Exp:0}/{s.Progress.ExpToNext:0} gold={s.Gold} kills={w.KillCount} ");
            // ★ T2：`plants={w.Plants.Count}` 已删除 ✗ —— `World.Plants` 本身已不存在 ✓
            //   要看"场上有几株植物"请用 `Farm.PlantCount`（新玩法：一株 = 一列地块）✓
            sb.Append($"plots={w.Farm.UsedPlotCount}/{w.Farm.MaxPlots} enemies={w.Enemies.Count} ");
            sb.Append($"pickups={w.Pickups.Count} bag={BagSummary(s)} farm={s.FarmActionSeconds:0.0}s combat={s.CombatSeconds:0.0}s ");
            sb.Append($"farmRatio={(s.CombatSeconds <= 0f ? 0f : s.FarmActionSeconds / s.CombatSeconds) * 100f:0.0}% ");
            sb.Append($"pos=({w.Player.Pos.x:0.0},{w.Player.Pos.y:0.0}) prompt=\"{s.Prompt}\" ");
            sb.Append($"weapon={w.Player.WeaponName}({w.Player.WeaponIndex}) windup={w.Player.SwitchWindup:0.00} ");
            sb.Append($"locked={w.Player.IsLocked}({w.Player.LockTargetId}) invuln={w.Player.InvulnRemaining:0.00} ");
            sb.Append($"grace={(s.Waves.InGrace ? s.Waves.GraceLeft : 0f):0.0}s");
            string text = sb.ToString();
            Log.Info("STATUS " + text);
            return text;
        }

        /// <summary>
        /// v172：**表现层**状态快照 —— 武器显隐 / 动画状态 / 当前真正在播的片段 / 播放倍率。
        ///
        /// 为什么必须单独开一个而不是加进 `Status()`：
        /// `Status()` 报的全是**玩法量**（血量/金币/冷却/相位），
        /// 而缺陷①②③④**一条都不影响玩法** ⇒ 只看 `Status()` 会全部是绿的，缺陷依然在 ✗
        /// "手上换没换、播的是哪一段、倍率变了没有"只能靠这个读 ✓
        /// </summary>
        public string AnimState()
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动，读不到表现层状态");
            GameBootstrap b = Boot;
            // ★ v172：**这里原来把两种完全不同的原因合并成了一句话** ✗
            //
            // 原代码：`if (b == null || b.View == null) return Fail("WorldView 未建立（buildVisuals 关闭？）");`
            // ⇒ 而 `b == null`（找不到 GameBootstrap 实例）与 `b.View == null`（视图没建）
            //   是【两件无关的事】✓ 而提示只说了后者 ✓
            // ⇒ ★ 后果（真实发生过）★：排查者看到"buildVisuals 关闭？"
            //   ⇒ 去读 `GameBootstrap` ⇒ 看到 `View` 只在 `Awake` 建（`:57` ✓）
            //   ⇒ 得出"`Restart` 路径不重建 `View`"✗
            //   而真相是：★ `View = ` 全工程【只有那一处写入】★（grep 确认 ✓）
            //   所以它【永远不会变成 null】✓ ⇒ 那是 ①（`Boot` 找不到）而不是 ② ✓
            //   旁证：同一时段另一个 agent 的日志打了 `WHOAMI … 静态实例数=0` ✓
            //
            // ⇒ ★ 修法：把两种原因【分开报】★ ✓ —— 而这不是措辞问题 ✗
            //   它决定了下一个人【往哪个方向查】✓
            if (b == null)
                return Fail("找不到 GameBootstrap 实例（静态实例在一次域重载 / Restart 后为空？）"
                          + " —— ★ 这一条**不是** buildVisuals 的问题 ★，别去查视图");
            if (b.View == null)
                return Fail("GameBootstrap 在，但它持有的 View 为 null（buildVisuals 关闭？）");
            string text = b.View.ProbePlayerAnim(s);
            Log.Info("ANIMSTATE " + text);
            return text;
        }

        /// <summary>
        /// v172：表现层探针 —— 把"出手/换武器之后**真正被采样的是哪一段**"打到控制台。
        ///
        /// ★ 为什么不能"调完立刻读" ★
        /// 攻击信号在 `FixedTick` 里投递，由**下一帧**的 `View.Sync` 消费
        ///（`_pendingAttack` → `PlayForced(Attack)`）✗
        /// 所以同一次调用里读到的仍是上一帧的状态 —— 会读到假的"没生效" ✓
        /// 而且 `DebugApi.Update` 与 `GameBootstrap.Update` 的先后**没有保证**，
        /// 所以"隔一帧"本身也不稳 ⇒ 改成**连续回报几帧** ✓
        ///
        /// 尾巴那一次（`ProbeTailFrames` 帧之后）用来看"一次性动作**播完有没有回待机**"——
        /// 只看入口的话，"卡在攻击第 0 帧"与"正常播一遍"是分不出来的 ✗
        /// </summary>
        private const int ProbeBurstFrames = 3;
        private const int ProbeTailFrames = 90;      // ≈1.5s @60fps

        private int _animProbeBurst;
        private int _animProbeTail = -1;
        private string _animProbeTag;

        private void TickAnimProbe()
        {
            if (_animProbeBurst <= 0 && _animProbeTail < 0) return;
            if (_animProbeBurst > 0)
            {
                _animProbeBurst--;
                ReportAnimProbe($"now+{ProbeBurstFrames - _animProbeBurst}");
            }
            if (_animProbeTail >= 0)
            {
                if (_animProbeTail == 0) { _animProbeTail = -1; ReportAnimProbe("tail"); }
                else _animProbeTail--;
            }
        }

        private void ReportAnimProbe(string when)
        {
            string snap = Boot != null && Boot.View != null
                ? Boot.View.ProbePlayerAnim(S)
                : "(无视图，buildVisuals 关闭？)";
            Log.Info($"ANIMPROBE {_animProbeTag} [{when}] {snap}");
        }

        private void ArmAnimProbe(string tag)
        {
            _animProbeTag = tag;
            _animProbeBurst = ProbeBurstFrames;
            _animProbeTail = ProbeTailFrames;
        }

        /// <summary>v172：切武器 + 自动回报表现层状态（**验证缺陷①③**：手上换成哪把、有没有播 swap）。</summary>
        public void SwapProbe(string spec)
        {
            SetWeapon(spec);
            ArmAnimProbe($"SwapProbe({spec})");
        }

        /// <summary>
        /// v172：立刻走一次指定武器的**完整攻击结算** + 自动回报表现层状态
        /// （**验证缺陷②**：播的是攻击段还是待机）。
        ///
        /// 先 `SwitchWeapon` 再出手：否则"攻击片段随武器取"这条根本走不到 ✗
        /// 走的是 `RunSession.DebugAttackOnce` —— 它会走**玩家按键时那条路**的同一段判定，
        /// 不是我另抄一份 ✗（那条纪律写在那个方法的注释里）✓
        /// </summary>
        public void AttackProbe(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) { Log.Warn("DEBUG AttackProbe 会话未启动"); return; }
            string key = (spec ?? "hoe").Trim().ToLowerInvariant();
            WeaponSlot slot = key.StartsWith("can") || key == "1" ? WeaponSlot.Can
                : key.StartsWith("fert") || key == "2" ? WeaponSlot.Fertilizer
                : WeaponSlot.Hoe;
            s.World.Player.SwitchWeapon((int)slot, s.World, true);
            s.DebugAttackOnce(slot);
            ArmAnimProbe($"AttackProbe({key})");
        }

        /// <summary>切换武器："0|hoe" / "1|can" / "2|fert"。</summary>
        public void SetWeapon(string spec)
        {
            RunSession s = S;
            if (s == null) return;
            int index;
            string key = (spec ?? "0").Trim().ToLowerInvariant();
            if (key.StartsWith("hoe") || key == "0") index = PlayerActor.WeaponHoe;
            else if (key.StartsWith("can") || key == "1") index = PlayerActor.WeaponCan;
            else if (key.StartsWith("fert") || key == "2") index = PlayerActor.WeaponFertilizer;
            else index = ParseInt(spec, 0);
            s.World.Player.SwitchWeapon(index, s.World);
            Log.Info($"DEBUG SetWeapon {s.World.Player.WeaponName} 前摇={s.World.Player.SwitchWindup:0.00}s");
        }

        /// <summary>调试/自动化：直接给/升级一件工具。spec = "hoe|can|fert|glove[:品质]"。</summary>
        public void GrantTool(string spec)
        {
            RunSession s = S;
            if (s == null) return;
            string[] parts = (spec ?? "hoe:0").Split(':');
            string key = parts[0].Trim().ToLowerInvariant();
            int idx = key.StartsWith("can") || key == "1" ? RunSession.ToolCan
                : key.StartsWith("fert") || key == "2" ? RunSession.ToolFertilizer
                : key.StartsWith("glo") || key == "3" ? RunSession.ToolGlove
                : RunSession.ToolHoe;
            int q = parts.Length > 1 ? ParseInt(parts[1], 0) : 0;
            bool ok = s.UpgradeTool(idx, q, out string reason);
            Log.Info($"DEBUG GrantTool {s.ToolName(idx)} quality={q} ok={ok} → {s.T.quality.NameOf(s.ToolLevel[idx])} 同时耕作 {s.WorkCapability(idx)} 块 {reason}");
        }

        /// <summary>调试/自动化：立刻进入商店阶段，用于验证商店 UI（正常路径是打完一波）。</summary>
        public void ForceShop()
        {
            RunSession s = S;
            if (s == null) return;
            s.ForceShopForDebug();
            Log.Info($"DEBUG ForceShop → {s.Phase}");
        }

        /// <summary>
        /// ★ 调试 / 自动化：**按 E**（与真实按键走同一条分支）★
        ///
        /// 为什么需要它：E 键由 `GameBootstrap` 从 `Keyboard.current` 读 ✗
        /// 而键盘状态来自**操作系统** ⇒ 自动化点不到、无头回放也按不了 ✓
        /// 于是把"按 E"做成一个可调用的动作 ✓
        ///
        /// ⚠️ 它**不绕过任何条件** ✗ —— 只在"停靠期 + 玩家在 4 米内"时才会真的进店，
        ///    其余情况与真人按键一样什么都不发生 ✓（判定在 `TickShopTruck` 里）
        /// </summary>
        public void ShopKey()
        {
            RunSession s = S;
            if (s == null) { Log.Warn("DEBUG ShopKey 会话未启动"); return; }
            ShopTruck truck = s.Truck;
            float half = s.T.farm.arenaHalf;
            Vector2 dock = ShopTruck.DockPoint * half;
            float dist = s.World != null && s.World.Player != null
                ? Vector2.Distance(s.World.Player.Pos, dock) : -1f;
            s.RequestShopOpen();
            Log.Info($"DEBUG ShopKey（按 E）→ 阶段 {truck.Current}｜离停靠位 {dist:0.00}m"
                     + $"｜可交互 {truck.CanShop(dist)}｜进店前 Phase={s.Phase}");
        }

        /// <summary>
        /// ★ 调试 / 自动化：把商店车**摆到停靠位**（截图 / 复现那一帧）★
        ///
        /// 一轮 60 秒，而"车停在门口"只占 10 秒 ✗ 靠等待抓帧不可复现 ✓
        /// 所以给一个显式的舞台调度（只搬阶段指针，见 `ShopTruck.DebugForcePhase`）✓
        /// spec：`dock`（默认）/ `idle` / `loop` / `enter` / `leave`
        /// </summary>
        public void TruckDock(string spec)
        {
            RunSession s = S;
            if (s == null) { Log.Warn("DEBUG TruckDock 会话未启动"); return; }
            string key = (spec ?? "").Trim().ToLowerInvariant();
            ShopTruck.Phase p = key.StartsWith("idle") ? ShopTruck.Phase.Idle
                : key.StartsWith("loop") ? ShopTruck.Phase.Looping
                : key.StartsWith("enter") ? ShopTruck.Phase.Entering
                : key.StartsWith("leave") ? ShopTruck.Phase.Leaving
                : ShopTruck.Phase.Dock;
            s.Truck.DebugForcePhase(p, s.Clock);
            Vector2 np = s.Truck.SamplePoint(s.Clock);
            float half = s.T.farm.arenaHalf;
            Log.Info($"DEBUG TruckDock → {p}｜归一化 ({np.x:0.00},{np.y:0.00})"
                     + $"｜世界 ({np.x * half:0.0},{np.y * half:0.0})｜场地半宽 {half:0.0}"
                     + $"｜停靠位 {ShopTruck.DockPoint.x:0.00},{ShopTruck.DockPoint.y:0.00}"
                     + $"｜离停靠位 {(np * half - ShopTruck.DockPoint * half).magnitude:0.00}m"
                     + $"｜剩余 {s.Truck.DockSecondsLeft(s.Clock):0.0}s");
        }

        /// <summary>
        /// ★ 调试 / 自动化：**商店车的一轮行为实测**（只读观测，不改任何玩法数值）★
        ///
        /// 跑一局 Balanced 并挂上 `DeathDiagnostics.Observe`，
        /// 把"车离停靠位多远 / 停了多久 / 玩家能交互多久 / 一轮多长 / 有没有不在场"打成日志 ✓
        /// spec = 种子（可空 ⇒ 默认 20240607）✓
        /// 完整报告写到 `Docs/reports/`（长报告会被 Unity 日志截断，所以只打摘要）✓
        /// </summary>
        public void TruckReport(string spec)
        {
            int exit = 0;
            try
            {
                TableDatabase.Load();
                ulong seed = 20240607UL;
                if (!string.IsNullOrEmpty(spec) && ulong.TryParse(spec.Trim(), out ulong parsed)) seed = parsed;

                DeathDiagnostics.Reset();
                SimResult r = RunSimulator.Run(BotStrategy.Balanced, seed, 400f, false, DeathDiagnostics.Observe);

                string summary = DeathDiagnostics.ShopSummary();
                Log.Info($"[Seedfall] 商店车实测（seed {seed}｜活 {r.Duration:0.0}s｜进店 {r.ShopVisits} 次"
                         + $"｜买种 {r.SeedsBought} 份｜植株 {r.PlantsAlive}）\n" + summary);
                string path = RunSimulator.WriteReport(
                    $"# 商店车实测（DebugApi.TruckReport｜seed {seed}｜Balanced）\n"
                    + summary + $"\n  进店 {r.ShopVisits} 次｜买种 {r.SeedsBought} 份｜植株 {r.PlantsAlive}"
                    + $"｜收割 {r.Harvests}｜金币 {r.Gold}｜等级 {r.Level}｜存活 {r.Duration:0.0}s\n",
                    $"shop-truck-{System.DateTime.Now:yyyyMMdd-HHmmss}.txt");
                Log.Info("[Seedfall] 报告：" + path);
            }
            catch (Exception e)
            {
                exit = 1;
                Log.Error("DEBUG TruckReport 失败：" + e);
            }
            if (exit != 0) Log.Warn("DEBUG TruckReport 以失败结束");
        }
        /// <summary>模拟 Esc：打开/关闭菜单（暂停）。</summary>
        public void ToggleMenu()
        {
            RunSession s = S;
            if (s == null) return;
            s.TogglePause();
            Log.Info($"DEBUG ToggleMenu paused={s.Paused}");
        }

        /// <summary>模拟 G 键：切换锁定最近目标。</summary>
        public void ToggleLock()
        {
            RunSession s = S;
            if (s == null) return;
            PlayerActor p = s.World.Player;
            if (p.IsLocked)
            {
                p.LockTargetId = -1;
                Log.Info("DEBUG ToggleLock → 解除");
                return;
            }
            EnemyActor tgt = s.World.NearestEnemyTo(p.Pos, 0f);
            p.LockTargetId = tgt != null ? tgt.Id : -1;
            Log.Info($"DEBUG ToggleLock → {(tgt == null ? "无目标" : tgt.Def.Id + "#" + tgt.Id)}");
        }

        /// <summary>模拟滚轮选择武器（0 锄头 / 1 水壶 / 2 肥料），与滚轮走同一条切换路径。</summary>
        public void WheelSelect(string spec)
        {
            SetWeapon(spec);
        }

        /// <summary>
        /// 调试/自动化：整屏截图（含 IMGUI 覆盖层，能拍到 HUD 与武器轮盘），落到 Docs/captures/。
        /// </summary>
        public void Capture(string name)
        {
            string fileName = string.IsNullOrEmpty(name) ? $"capture-{System.DateTime.Now:HHmmss}.png" : name;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Docs", "captures"));
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, fileName);
            ScreenCapture.CaptureScreenshot(path);
            Log.Info("CAPTURE " + path);
        }

        /// <summary>模拟滚轮切换武器：spec = 滚轮增量（+1 下一把 / -1 上一把）。</summary>
        public void ScrollWeapon(string spec)
        {
            RunSession s = S;
            if (s == null) return;
            int delta = ParseInt(spec, 1);
            s.World.Player.CycleWeapon(delta >= 0 ? 1 : -1, s.World);
            Log.Info($"DEBUG ScrollWeapon {delta} → {s.World.Player.WeaponName} 前摇={s.World.Player.SwitchWindup:0.00}s");
        }

        private static string BagSummary(RunSession s)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot slot = s.Bag[i];
                if (i > 0) sb.Append('|');
                string suffix = "∞";   // v4：种子无限
                sb.Append(slot.IsEmpty ? "-" : $"{slot.Def.Id}{(slot.MutationId == null ? "" : ":" + slot.MutationId)}{suffix}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        public string PlotDump()
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动");
            var sb = new StringBuilder();
            for (int i = 0; i < s.World.Farm.Slots.Length; i++)
            {
                FarmPlot p = s.World.Farm.Slots[i];
                sb.Append($"#{i}({p.GridX},{p.GridY}):{p.State}");
                if (p.State == PlotState.Growing)
                    sb.Append($"(剩余{p.GrowthRemaining:0.0}s 水={p.Water}/{p.WaterMax} 肥={(p.Fertilized ? 1 : 0)})");
                // v166：作物身份在 CropSeedId；SeedId 只有列首才有（1:1 契约）
                if (!string.IsNullOrEmpty(p.CropSeedId))
                    sb.Append($"[{p.CropSeedId}{(p.IsColumnHead ? "*" : "")}]");
                sb.Append("  ");
            }
            string text = sb.ToString();
            Log.Info("PLOTS " + text);
            return text;
        }

        /// <summary>
        /// v166：**田地的形状与植物分配**（新玩法的验收入口）。
        /// 一次调用回答四个问题：几列几行？几格？几株植物？下一排还要多久？
        /// </summary>
        public string FarmDiag()
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动");
            FarmGrid g = s.World.Farm;
            var sb = new StringBuilder();
            sb.Append($"FARMDIAG 形状={g.Columns}列×{g.Rows}行（上限{g.MaxRows}行） 格数={g.PlotCount}/{g.Capacity} ");
            sb.Append($"植物={g.PlantCount}/{g.Columns}株 空列={g.FreeColumnCount} 第一空列={g.FirstFreeColumn()} ");
            sb.Append($"已种格={g.UsedPlotCount} 生长中={g.GrowingCount} ");
            sb.Append($"世界时间={s.World.Time:0.0}s 距下一排={g.SecondsToNextRow(s.World.Time):0.0}s ");
            // 每列：列首状态 + 该列几格有作物 + 品种
            for (int c = 0; c < g.Columns; c++)
            {
                FarmPlot head = g.ColumnHead(c);
                int crops = 0;
                for (int r = 0; r < g.Rows; r++)
                {
                    FarmPlot p = g.Slot(c, r);
                    if (p != null && p.State != PlotState.Empty) crops++;
                }
                sb.Append($"[列{c}: {(head != null ? head.State.ToString() : "-")} {crops}/{g.Rows}格");
                if (head != null && !string.IsNullOrEmpty(head.CropSeedId)) sb.Append($" {head.CropSeedId}");
                sb.Append("] ");
            }
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// v166：把世界时间推到指定秒数，用来**不用真等 5 分钟**就验证"加一排"。
        /// 注意它推的是 `World.Time`（存活时间），田地成长读的就是它。
        /// </summary>
        public string AdvanceFarmTime(string secondsText)
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动");
            float sec = ParseFloat(secondsText, 300f);
            s.World.Time = Mathf.Max(s.World.Time, sec);
            s.World.Farm.SyncSurvival(s.World.Time);
            Log.Info($"DEBUG AdvanceFarmTime → 世界时间={s.World.Time:0.0}s 行数={s.World.Farm.Rows} 格数={s.World.Farm.PlotCount}");
            return FarmDiag();
        }

        public string BootDiag()
        {
            var sb = new StringBuilder();
            GameBootstrap[] all = FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include);
            sb.Append($"bootstrap 实例数={all.Length} 字段boot={(boot == null ? "null" : boot.name)} ");
            foreach (GameBootstrap b in all)
            {
                sb.Append($"[{b.name} ready={b.Ready} enabled={b.enabled} session={(b.Session == null ? "null" : "ok")}] ");
            }
            string text = sb.ToString();
            Log.Info("BOOTDIAG " + text);
            return text;
        }

        public string PlantDiag()
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动");
            World w = s.World;
            var sb = new StringBuilder();
            sb.AppendLine($"t={s.RunTime:0.0} 阶段={s.Phase} 波={s.WaveIndex} 击杀={w.KillCount} 弹道={w.Projectiles.Count} 掉落={w.Pickups.Count}");
            // ★ T2：植物实体的逐株转储**整段删除** ✗ ★
            //   它读的是 `w.Plants` / `PlantAlly.{Growing,AttackValue,AttackInterval,RangeM,AttackTimer,DamageDone}`
            //   —— 这些属性随 `PlantAlly` 一起消失 ✓
            //   想看"植物"请用 `ShowPlants()`（它转储的是**耕地/作物**，与植物实体无关 ✓）
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                sb.AppendLine($"敌人#{e.Id} {e.Def.Id} hp={e.Health.Hp:0}/{e.Health.MaxHp:0} 位置=({e.Pos.x:0.0},{e.Pos.y:0.0}) " +
                              $"冻结={e.Status.Frozen} 毒={e.Status.PoisonTicksLeft}次 燃={e.Status.BurnTicksLeft}次 寒冷={e.Status.ChillStacks}层");
            }
            string text = sb.ToString();
            Log.Info("PLANTDIAG\n" + text);
            return text;
        }

        /// <summary>
        /// v45：小猪弹球诊断——位置 / 当前朝向 / 累计撞墙反弹次数。
        /// 这是"随机初始方向 + 撞墙镜面反射"这条规则的实机验证口：
        /// 连续采样应看到朝向在两面墙之间逐次反号，而位置始终落在 ±(arenaHalf − 半径) 之内。
        /// </summary>
        public string PigDiag()
        {
            RunSession s = S;
            if (s == null || s.World == null) return Fail("会话未启动");
            World w = s.World;
            var sb = new StringBuilder();
            float lim = TableDatabase.Tuning.farm.arenaHalf;
            sb.AppendLine($"t={s.RunTime:0.0} 阶段={s.Phase} 场地半宽={lim:0.#}");
            int n = 0;
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                if (!(w.Enemies[i] is WanderEnemy pig)) continue;
                n++;
                sb.AppendLine($"猪#{pig.Id} 位置=({pig.Pos.x:0.00},{pig.Pos.y:0.00}) " +
                              $"朝向=({pig.Heading.x:0.000},{pig.Heading.y:0.000}) 反弹={pig.BounceCount} " +
                              $"初始方向已定={pig.HeadingReady}");
            }
            if (n == 0) sb.AppendLine("场上没有小猪（用 SpawnEnemy pig:n 生成）");
            string text = sb.ToString();
            Log.Info("PIGDIAG\n" + text);
            return text;
        }

        /// <summary>
        /// v51：HUD 文本溢出审计——下一帧 OnGUI 会把每个信息区里放不下的行打到 Console（HUD-AUDIT）。
        /// 用法：切到想检查的界面（战斗 / 商店 / 菜单）各调用一次，再读 Console。
        /// </summary>
        public string HudAudit()
        {
            ImGuiHud.AuditFindings.Clear();
            ImGuiHud.AuditRequested = true;
            string text = $"HUD 审计已排队（当前阶段 {S?.Phase.ToString() ?? "无会话"}）→ 读 Console 的 HUD-AUDIT 行";
            Log.Info(text);
            return text;
        }

        /// <summary>v51：直接读上一次审计的结论（不必翻 Console）。</summary>
        public string HudAuditReport()
        {
            if (ImGuiHud.AuditRequested) return "HUD-AUDIT：尚未执行（HUD 这一帧还没画，等一帧再读）";
            if (ImGuiHud.AuditFindings.Count == 0) return "HUD-AUDIT 通过：上一帧画出的所有文本都放得下";
            var sb = new StringBuilder();
            sb.AppendLine($"HUD-AUDIT 共 {ImGuiHud.AuditFindings.Count} 条超宽：");
            for (int i = 0; i < ImGuiHud.AuditFindings.Count; i++) sb.AppendLine("  " + ImGuiHud.AuditFindings[i]);
            return sb.ToString();
        }

        private bool _artShot;
        private bool _artFollowPlayer;
        private Vector3 _artFocus = new Vector3(0f, 0.55f, 0f);
        private Vector3 _artCamOffset = new Vector3(0f, 0.30f, -4.2f);
        private Vector3 _artCamEuler = new Vector3(5f, 0f, 0f);

        /// <summary>
        /// ★ v174：摆样机位的**自动退出**秒数 ★
        ///
        /// 为什么必须有这个数：`LateUpdate` 每帧把 `Camera.main` 钉在 `_artFocus + _artCamOffset` 上 ✗
        /// 而 `_artFocus` 是**摆样那一刻**算好的 —— 摆完之后它不会跟着玩家走 ✓
        /// ⇒ 玩家一动，画面里就是一排植物悬在不该在的地方、而相机还在按老机位取景 ✗
        /// ⇒ 读起来就是"★ 有些植物没有种在耕地上，且到处移动 ★"（用户原话）✓
        ///
        /// 修法：机位只在"拍照"这段时间内接管，到点**自己交还**给 `GameBootstrap.UpdateCamera` ✓
        /// 为什么不用"靠人记得调 `ArtShotOff()`"：★ 那正是出事的原因 ✗ ——
        ///   没人会记得，于是 `_artShot` 就一直是 true ✓
        /// 为什么不做成"摆完立刻交还"：那 `BattleView()`/`FocusFarm()` 这类要靠眼睛看的机位
        ///   会连一帧都留不住，等于把它们废掉 ✗
        /// </summary>
        private const float ArtShotSeconds = 6f;

        private float _artShotRemaining;

        /// <summary>开启摆样机位并**开始倒计时**（每个置 `_artShot = true` 的入口都要走它）✓</summary>
        private void ArmArtShot() => _artShotRemaining = ArtShotSeconds;

        /// <summary>
        /// 退出摆样机位，并把 `ArtPreview` 底下**本次摆出来的东西一起清掉** ✓
        ///
        /// 清场是判据的一部分：不清的话，摆样过的植物会以 GameObject 的形式留在场景里 ✗
        /// ⇒ 镜头交还玩家之后，玩家看到的仍然是"走过路过都是不在地里的植物" ✓
        /// （这和 `ArtShot`/`ShowPlants` 自己开头那段清场是同一个理由 —— 那里清的是"上一次摆的"✓）
        /// </summary>
        public string ArtShotOff()
        {
            _artShot = false;
            _artFollowPlayer = false;
            _artShotRemaining = 0f;
            int cleared = ClearArtPreview();
            return "美术特写机位已关闭" + (cleared > 0 ? $"（清掉 {cleared} 个摆样对象）" : "");
        }

        /// <summary>销毁 `ArtPreview` 下的所有摆样对象，返回清掉的数量（无 holder 时返回 0）✓</summary>
        private static int ClearArtPreview()
        {
            GameObject holder = GameObject.Find("ArtPreview");
            if (holder == null) return 0;
            int n = holder.transform.childCount;
            for (int c = n - 1; c >= 0; c--)
                Destroy(holder.transform.GetChild(c).gameObject);
            return n;
        }

        private void LateUpdate()
        {
            if (!_artShot) return;

            // 到点自己交还机位 —— 不依赖任何人记得调 `ArtShotOff()`
            _artShotRemaining -= Time.unscaledDeltaTime;
            if (_artShotRemaining <= 0f)
            {
                ArtShotOff();
                return;
            }

            LateUpdateArtFollow();
            Camera cam = Camera.main;
            if (cam == null) return;
            // 机位由 _artCamOffset/_artCamEuler 决定（角色一排 / 植物阵列各有一套）
            cam.transform.position = _artFocus + _artCamOffset;
            cam.transform.rotation = Quaternion.Euler(_artCamEuler);
        }

        /// <summary>
        /// v58：环境摆样——用环境模块拼一小块场景，验收模块的尺度关系与拼接是否成立。
        /// 农田按 1.6m 格距铺、栅栏按 2.0m 段首尾相接，正是它们在玩法里的用法。
        /// </summary>
        public string ShowEnvironment()
        {
            Vector2 basePos = S != null && S.World != null && S.World.Player != null
                ? S.World.Player.Pos : Vector2.zero;
            float ox = basePos.x, oz = basePos.y + 9f;      // 摆样中心，避开玩家与已有耕地

            GameObject holder = GameObject.Find("ArtPreview");
            if (holder == null) holder = new GameObject("ArtPreview");
            for (int c = holder.transform.childCount - 1; c >= 0; c--)
                Destroy(holder.transform.GetChild(c).gameObject);

            // (模块, x 偏移, z 偏移, 绕 Y 旋转)
            var layout = new (string id, float x, float z, float yaw)[]
            {
                // 农田：3×2，格距 1.6m
                ("farm_plot", -2.4f, 0.0f,   0f), ("farm_plot", -0.8f, 0.0f, 0f), ("farm_plot", 0.8f, 0.0f, 0f),
                ("farm_plot", -2.4f, 1.6f,   0f), ("farm_plot", -0.8f, 1.6f, 0f), ("farm_plot", 0.8f, 1.6f, 0f),
                // 栅栏：沿农田下沿首尾相接（2.0m 一段）
                ("fence",     -3.2f, -1.0f,  0f), ("fence", -1.2f, -1.0f, 0f), ("fence", 0.8f, -1.0f, 0f),
                ("fence",     -3.2f, 2.6f, 180f), ("fence", -1.2f, 2.6f, 180f), ("fence", 0.8f, 2.6f, 180f),
                // 地标与植被
                ("windmill",  -6.2f, 3.4f,  25f),
                ("tree",       4.6f, 3.0f, -15f),
                ("pond",       4.4f, -1.6f,  0f),
                ("rock_a",     2.9f, 1.2f,  20f),
                ("rock_b",    -4.6f, -1.4f, 40f),
                ("rock_c",     3.4f, -0.4f,  0f),
                ("bush",       2.4f, 3.4f,   0f), ("bush", -5.0f, 0.6f, 60f),
                ("grass",     -1.6f, -1.9f,  0f), ("grass", 0.2f, -1.9f, 30f),
                ("grass",     -2.6f, 3.0f,  45f), ("grass", 1.6f, 2.9f, 10f),
            };

            var sb = new StringBuilder();
            int ok = 0, missing = 0;
            for (int i = 0; i < layout.Length; i++)
            {
                GameObject prefab = Resources.Load<GameObject>("Art/Environment/" + layout[i].id);
                if (prefab == null) { missing++; sb.AppendLine(layout[i].id + "：缺失"); continue; }
                GameObject go = Instantiate(prefab, holder.transform);
                go.name = "ENV_" + layout[i].id + "_" + i;
                go.transform.position = new Vector3(ox + layout[i].x, 0f, oz + layout[i].z);
                go.transform.rotation = Quaternion.Euler(0f, layout[i].yaw, 0f);
                ToonLook.Apply(go);
                ok++;
            }

            _artFocus = new Vector3(ox, 1.10f, oz + 1.0f);
            _artCamOffset = new Vector3(0f, 9.5f, -15.5f);
            _artCamEuler = new Vector3(30f, 0f, 0f);
            _artShot = true;
            ArmArtShot();

            string report = "环境摆样：" + ok + " 个模块已放置" + (missing > 0 ? "，缺 " + missing : "") + "\n" + sb;
            Log.Info("ENV-PREVIEW\n" + report);
            return report;
        }

        /// <summary>
        /// v57：植物全家福——16 个种子模型按 4×4 摆开，验收"轮廓能否读出玩法定位"。
        /// </summary>
        public string ShowPlants()
        {
            string[] ids =
            {
                "radish_guard", "pea_shooter", "toxic_mushroom", "frost_ivy",
                "thorn_pumpkin", "stone_fist", "chili_mage", "starvine",
                "moontree", "char_flower", "plague_mother", "moss_shield",
                "healing_clover", "life_spring", "holy_tree_guard", "ironroot_colossus"
            };

            Vector2 basePos = S != null && S.World != null && S.World.Player != null
                ? S.World.Player.Pos : Vector2.zero;

            GameObject holder = GameObject.Find("ArtPreview");
            if (holder == null) holder = new GameObject("ArtPreview");
            for (int c = holder.transform.childCount - 1; c >= 0; c--)
                Destroy(holder.transform.GetChild(c).gameObject);

            var sb = new StringBuilder();
            int ok = 0;
            const float gap = 1.35f;
            for (int i = 0; i < ids.Length; i++)
            {
                GameObject prefab = Resources.Load<GameObject>("Art/Plants/" + ids[i]);
                if (prefab == null) { sb.AppendLine(ids[i] + "：资源缺失"); continue; }
                GameObject go = Instantiate(prefab, holder.transform);
                go.name = "PLANT_" + ids[i];
                float gx = (i % 4) - 1.5f;
                float gz = (i / 4) - 1.5f;
                go.transform.position = new Vector3(basePos.x + gx * gap, 0f, basePos.y + 5.0f + gz * gap);
                go.transform.rotation = Quaternion.identity;
                ToonLook.Apply(go);
                ok++;
            }

            // 4×4 阵列约 4.05m × 4.05m（含 S 级 1.2m 的高个），
            // 机位要够高够远：上一版 3.1m 高 / 6.4m 距把最后一排切掉了。
            _artFocus = new Vector3(basePos.x, 0.40f, basePos.y + 5.0f);
            _artCamOffset = new Vector3(0f, 4.6f, -8.2f);
            _artCamEuler = new Vector3(28f, 0f, 0f);
            _artShot = true;
            ArmArtShot();

            string report = "植物预览：" + ok + "/" + ids.Length + " 个已放置\n" + sb;
            Log.Info("PLANT-PREVIEW\n" + report);
            return report;
        }

        /// <summary>
        /// v54：美术特写——把 4 个模型摆成一排，并在 LateUpdate 里接管相机贴近拍摄。
        /// 为什么要在 LateUpdate 接管：GameBootstrap 每帧都会重置跟随相机，
        /// 只有 LateUpdate 里写入的机位才能稳定压过它。
        /// v174：接管是**限时**的（见 `ArtShotSeconds`），到点自动交还 ✓
        /// </summary>
        public string ArtShot()
        {
            string placed = ShowModels();
            _artCamOffset = new Vector3(0f, 0.30f, -4.2f);
            _artCamEuler = new Vector3(5f, 0f, 0f);
            _artShot = true;
            ArmArtShot();
            return "美术特写机位已开启。\n" + placed;
        }

        public string ShowModels()
        {
            return ShowModelsFrom("player,imp,imp_archer,brute");
        }

        /// <summary>
        /// v54：美术预览——把 Resources/Art/Characters 下的角色模型摆到玩家附近，
        /// 统一换成 Seedfall/Toon 描边材质，用来肉眼验收模型比例与漫画观感。
        /// </summary>
        public string ShowModelsFrom(string ids)
        {
            string[] list = ids.Split(',');
            GameObject holder = GameObject.Find("ArtPreview");
            if (holder == null) holder = new GameObject("ArtPreview");

            // 清掉上一次的预览实例。
            // 不做这一步，每次调用都会在场景里再叠一层——上一轮就是因为几层新旧模型叠在一起，
            // 让我把"旧实例躺着"误判成"新模型躺着"，白追了一轮。
            for (int c = holder.transform.childCount - 1; c >= 0; c--)
                Destroy(holder.transform.GetChild(c).gameObject);

            Vector2 basePos = S != null && S.World != null && S.World.Player != null
                ? S.World.Player.Pos : Vector2.zero;
            // 特写机位的焦点 = 这排模型的中心，抬高到胸口高度
            _artFocus = new Vector3(basePos.x, 0.55f, basePos.y + 4.4f);

            var sb = new StringBuilder();
            int ok = 0;
            for (int i = 0; i < list.Length; i++)
            {
                string id = list[i].Trim();
                GameObject prefab = Resources.Load<GameObject>("Art/Characters/" + id);
                if (prefab == null) { sb.AppendLine(id + "：资源缺失"); continue; }

                GameObject go = Instantiate(prefab, holder.transform);
                go.name = "PREVIEW_" + id;
                go.transform.position = new Vector3(
                    basePos.x + (i - (list.Length - 1) * 0.5f) * 1.5f, 0f, basePos.y + 4.4f);

                // 朝向：模型正面 = Unity +Z（建模脚本已在导出前烘好 Y-up，见 seedfall_kit.export_fbx）。
                // 相机在 -Z 侧朝 +Z 看，所以转 180° 才是面向镜头。
                // 注意：这里只设 yaw；不要用 rotation 去"修正"直立性——直立性属于资产，不属于摆位。
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

                int slots = ToonLook.Apply(go);
                Renderer rend = go.GetComponentInChildren<Renderer>();
                float h = rend != null ? rend.bounds.size.y : 0f;
                Renderer[] all = go.GetComponentsInChildren<Renderer>(true);
                int matSlots = 0;
                for (int k = 0; k < all.Length; k++)
                    matSlots += all[k].sharedMaterials != null ? all[k].sharedMaterials.Length : 0;
                sb.AppendLine(id + "：换了 " + slots + "/" + matSlots + " 个材质槽｜渲染器 " + all.Length
                    + "｜高 " + h.ToString("0.00") + "m｜面数 " + CountTris(go));
                ok++;
            }

            string shaderState = ToonLook.ToonShader != null
                ? "Seedfall/Toon 已找到" : "**Shader.Find(\"Seedfall/Toon\") 返回 null**";
            string report = "艺术预览：" + ok + "/" + list.Length + " 个模型已放置｜" + shaderState + "\n" + sb;
            Log.Info("ART-PREVIEW\n" + report);
            return report;
        }

        private static int CountTris(GameObject go)
        {
            int n = 0;
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                Mesh m = mfs[i].sharedMesh;
                if (m != null) n += m.triangles.Length / 3;
            }
            return n;
        }

        /// <summary>
        /// v56：动画片段验收——把 Resources/Art/Anim 下的每个 FBX 里的 AnimationClip 列出来，
        /// 并核对片段时长是否与设计帧数一致（fps 采样率 × 帧数）。
        /// </summary>
        public string AnimReport()
        {
            string[] ids = { "player", "imp", "imp_archer", "brute" };
            string[] anims = { "idle", "walk", "attack", "hit", "death" };
            var sb = new StringBuilder();
            int found = 0, missing = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                sb.AppendLine("— " + ids[i] + " —");
                for (int a = 0; a < anims.Length; a++)
                {
                    string key = "Art/Anim/" + ids[i] + "_" + anims[a];
                    UnityEngine.Object[] all = Resources.LoadAll(key, typeof(AnimationClip));
                    if (all == null || all.Length == 0) { sb.AppendLine("  " + anims[a] + "：缺失"); missing++; continue; }
                    found++;
                    float total = 0f;
                    int looping = 0;
                    for (int c = 0; c < all.Length; c++)
                    {
                        AnimationClip clip = all[c] as AnimationClip;
                        if (clip == null) continue;
                        total += clip.length;
                        if (clip.isLooping) looping++;
                    }
                    AnimationClip first = all[0] as AnimationClip;
                    // 汇总而不是逐条列——上一版把几百条片段全打出来，把工具响应刷爆了
                    sb.AppendLine("  " + anims[a] + "：" + all.Length + " 条｜总时长 "
                        + total.ToString("0.00") + "s｜循环 " + looping + "｜首条 "
                        + (first != null ? first.name : "?"));
                }
            }
            string report = "动画片段：命中 " + found + "／缺失 " + missing + "\n" + sb;
            Log.Info("ANIM-REPORT\n" + report);
            return report;
        }

        /// <summary>
        /// v56：把角色摆到某个动画的某一时刻并特写——用来肉眼验证"整条动画是否驱动全身"。
        /// 用 AnimationClip.SampleAnimation 直接采样，不需要 Animator/Animation 组件，
        /// 因此这条验证路径不受控制器配置影响。
        /// </summary>
        public string PoseAnim()
        {
            return PoseAnimAt("player", "walk", 0.35f);
        }

        public string PoseAnimAt(string charId, string animName, float t)
        {
            AnimationClip clip = Resources.Load<AnimationClip>("Art/AnimMerged/" + charId + "_" + animName);
            if (clip == null) return "找不到合并片段：" + charId + "_" + animName;

            GameObject holder = GameObject.Find("ArtPreview");
            if (holder == null) holder = new GameObject("ArtPreview");
            for (int c = holder.transform.childCount - 1; c >= 0; c--)
                Destroy(holder.transform.GetChild(c).gameObject);

            GameObject prefab = Resources.Load<GameObject>("Art/Characters/" + charId);
            if (prefab == null) return "找不到模型：" + charId;

            GameObject go = Instantiate(prefab, holder.transform);
            go.name = "PREVIEW_" + charId;
            go.transform.position = new Vector3(0f, 0f, 0f);
            go.transform.rotation = Quaternion.identity;
            ToonLook.Apply(go);

            clip.SampleAnimation(go, Mathf.Clamp(t, 0f, clip.length));
            _artFocus = new Vector3(0f, 0.55f, 0f);
            _artShot = true;
            ArmArtShot();

            string report = charId + "/" + animName + " @ " + t.ToString("0.00") + "s"
                + "｜片段时长 " + clip.length.ToString("0.000") + "s";
            Log.Info("POSE-ANIM " + report);
            return report;
        }

        /// <summary>
        /// v59：染色链路验证——4 个角色走 **ModelView.Spawn（生产路径）** 并施加 0.72 灰染色。
        /// 判据：若各部件仍保留自己的颜色（草帽黄、围巾绿、皮肤白），说明 _TintMul 生效；
        /// 若整只变成一片灰，说明染色又把所有材质槽覆盖成同一个颜色了（旧 bug 复发）。
        /// </summary>
        public string TintTest()
        {
            string[] ids = { "player", "imp", "imp_archer", "brute" };
            GameObject holder = GameObject.Find("ArtPreview");
            if (holder == null) holder = new GameObject("ArtPreview");
            for (int c = holder.transform.childCount - 1; c >= 0; c--)
                Destroy(holder.transform.GetChild(c).gameObject);

            Vector2 basePos = S != null && S.World != null && S.World.Player != null
                ? S.World.Player.Pos : Vector2.zero;

            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                ModelView mv = ModelView.Spawn("Characters", ids[i], holder.transform, true);
                if (mv == null) { sb.AppendLine(ids[i] + "：缺失"); continue; }
                mv.Go.transform.position = new Vector3(
                    basePos.x + (i - (ids.Length - 1) * 0.5f) * 1.5f, 0f, basePos.y + 4.4f);
                mv.Go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                mv.SetTint(new Color(0.72f, 0.72f, 0.72f));   // 模拟"血量 50% 变暗"
                mv.ScaleToHeight(0f);                          // 保持原尺寸
                sb.AppendLine(ids[i] + "：已染色 0.72 | 高 " + mv.Height.ToString("0.00") + "m");
            }

            _artFocus = new Vector3(basePos.x, 0.55f, basePos.y + 4.4f);
            _artCamOffset = new Vector3(0f, 0.30f, -4.2f);
            _artCamEuler = new Vector3(5f, 0f, 0f);
            _artShot = true;
            ArmArtShot();
            _artFollowPlayer = false;

            string report = "染色验证：\n" + sb;
            Log.Info("TINT-TEST\n" + report);
            return report;
        }

        /// <summary>
        /// v59：场景体检——列出体积最大的若干渲染器。
        /// 用来定位"画面上莫名多出一大块东西"这类问题：直接看是谁、多大、什么材质。
        /// </summary>
        public string Diagnose()
        {
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var list = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].gameObject.activeInHierarchy) continue;
                if (all[i].gameObject.name == "Ground") continue;      // 地面是刻意的大
                list.Add(all[i]);
            }
            list.Sort((a, b) =>
            {
                Vector3 sa = a.bounds.size, sb = b.bounds.size;
                return (sb.x * sb.y * sb.z).CompareTo(sa.x * sa.y * sa.z);
            });

            var sb2 = new StringBuilder();
            sb2.AppendLine("渲染器总数 " + all.Length + "（已排除 Ground）");
            int n = Mathf.Min(12, list.Count);
            for (int i = 0; i < n; i++)
            {
                Renderer r = list[i];
                Vector3 s = r.bounds.size;
                Material m = r.sharedMaterial;
                string matName = m != null ? m.name : "(无)";
                string shader = m != null && m.shader != null ? m.shader.name : "(无)";
                sb2.AppendLine("  " + r.gameObject.name
                    + "｜尺寸 " + s.x.ToString("0.00") + "×" + s.y.ToString("0.00") + "×" + s.z.ToString("0.00")
                    + "｜槽 " + r.sharedMaterials.Length
                    + "｜材质 " + matName + "｜" + shader);
            }
            string report = sb2.ToString();
            Log.Info("DIAGNOSE\n" + report);
            return report;
        }

        /// <summary>
        /// v59：战斗特写机位——贴着玩家拍，用来验收"模型真的进玩法了"。
        /// 游戏默认跟随相机是俯视远机位，角色只有几十像素，看不清模型与动画。
        /// </summary>
        public string BattleView()
        {
            if (S == null || S.World == null || S.World.Player == null) return "无会话";
            Vector2 pp = S.World.Player.Pos;
            _artFocus = new Vector3(pp.x, 0.6f, pp.y);
            _artCamOffset = new Vector3(0f, 4.6f, -7.6f);
            _artCamEuler = new Vector3(24f, 0f, 0f);
            _artShot = true;
            ArmArtShot();
            _artFollowPlayer = true;
            return "战斗特写机位已开启（跟随玩家）";
        }

        /// <summary>v59：保持特写机位跟随玩家（每帧更新焦点）。</summary>
        private void LateUpdateArtFollow()
        {
            if (!_artShot || !_artFollowPlayer || S == null || S.World == null || S.World.Player == null) return;
            Vector2 pp = S.World.Player.Pos;
            _artFocus = new Vector3(pp.x, 0.6f, pp.y);
        }

        /// <summary>
        /// v64：网格探针——直接报出模型预制体的原始网格包围盒与根节点缩放/旋转。
        /// 用来一次性看清"导入后到底多大、朝向如何"，不再靠间接推断。
        /// </summary>
        public string MeshProbe()
        {
            string[] ids = { "Characters/player", "Characters/imp", "Environment/tree",
                             "Environment/grass", "Plants/pea_shooter", "Environment/farm_plot" };
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                GameObject prefab = Resources.Load<GameObject>("Art/" + ids[i]);
                if (prefab == null) { sb.AppendLine(ids[i] + "：缺失"); continue; }
                Transform root = prefab.transform;
                MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>(true);
                Bounds mb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds();
                sb.AppendLine(ids[i]
                    + "｜根 scale " + root.localScale.x.ToString("0.####")
                    + " rot " + root.localRotation.eulerAngles.ToString("0.#")
                    + "｜网格本地 bounds size " + mb.size.ToString("0.###")
                    + " center " + mb.center.ToString("0.###"));
                // 把网格节点的整条父链打出来——身高换算反复算错，就是因为我一直在猜这条链
                if (mf != null)
                {
                    Transform t = mf.transform;
                    var chain = new StringBuilder();
                    while (t != null)
                    {
                        chain.Append(t.name).Append("[scale ")
                             .Append(t.localScale.x.ToString("0.####")).Append("] ");
                        if (t == root) break;
                        t = t.parent;
                    }
                    sb.AppendLine("      链: " + chain);
                }
            }
            string report = sb.ToString();
            Log.Info("MESH-PROBE\n" + report);
            return report;
        }

        /// <summary>v66：立刻弹出升级抽卡（验收发牌动画）。</summary>
        public string ForceReward()
        {
            if (S == null) return "无会话";
            S.DebugShowReward();
            return "已弹出升级抽卡（阶段 " + S.Phase + "）";
        }

        /// <summary>v66：立刻放一次近战挥击特效（验收挥击弧）。会把弧拉到 4 秒慢放，便于截图。</summary>
        public string ForceSwing()
        {
            if (S == null) return "无会话";
            WorldView.DebugSwingHold = 4f;
            S.DebugSwing();
            return "已触发挥击弧（慢放 4 秒）";
        }

        /// <summary>v66：结束慢放，恢复正常的 0.20 秒挥击。</summary>
        public string SwingNormal()
        {
            WorldView.DebugSwingHold = 0f;
            return "挥击已恢复正常速度";
        }

        /// <summary>v69：一次性放出全部六种浮动文字，核对配色/字号/叹号。</summary>
        public string PopupDemo()
        {
            if (S == null) return "无会话";
            S.DebugPopupDemo();
            return "已放出：敌人受击(白) / 玩家受击(红) / 回血(绿) / 成熟(黄) / 缺水(红+！) / 暴击(黄+！) / bomb！！！";
        }

        /// <summary>
        /// v79：调试用——在相邻两块地里种下**两个体型差距最大的品种**，并推到同一生长进度。
        ///
        /// 为什么要专门造这个场景：验收"统一生长中植物大小"必须**两个品种并排、进度一致**，
        /// 才能一眼看出高度是否真的统一。自然对局里两块地很难长出不同品种，
        /// 而且生长是异步的，进度也凑不齐。
        /// 选豌豆射手（最小）和荆棘南瓜（最大）——体型差距最大，最能暴露问题。
        /// </summary>
        public string DebugPlantPair()
        {
            if (S == null || S.World == null || S.World.Farm == null) return "无会话";
            FarmGrid g = S.World.Farm;
            string[] ids = { "pea_shooter", "thorn_pumpkin" };
            // 开局只有 1 块地，对比组要 2 块——先解锁。
            // SetMaxPlots 是耕地的正规入口（商店买地也走它），不是调试后门。
            if (g.PlotCount < ids.Length) g.SetMaxPlots(ids.Length);
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length && i < g.PlotCount; i++)
            {
                FarmPlot p = g.Plot(i);
                if (p == null) continue;
                SeedDef def;
                try { def = TableDatabase.GetSeed(ids[i]); }
                catch (System.Exception) { sb.Append($"第{i}格：找不到种子 {ids[i]}；"); continue; }

                p.ResetToEmpty();
                p.BeginPlant(S.T.farm, def, 100, 1);
                // 推到 50% —— 正好在幼苗/成熟切换点附近，是最能看出大小差异的位置。
                // GrowthRemaining 是私有 setter，只能靠 TickGrowth 推进，不能直接赋值。
                for (int k = 0; k < 2000 && p.GrowthProgress01 < 0.5f; k++) p.TickGrowth(0.05f);
                sb.Append($"第{i}格 {ids[i]} → {p.GrowthProgress01:P0}；");
            }
            return "已种下对比组：" + sb;
        }

        /// <summary>
        /// v78：耕地特写机位——**近乎垂直俯视第一块有作物的地**。
        ///
        /// 为什么需要专门一个机位：验收"作物是否落在格子中心"和"不同品种在同一生长进度下是否一样高"，
        /// 用默认的斜视角判断不了——格子被透视压成梯形，中心点看不准，高度差也被视角掩盖。
        /// 这里压到 88°（留 2° 是为了还能看出高度），正对一块地拍。
        /// </summary>
        public string FocusFarm()
        {
            if (S == null || S.World == null || S.World.Farm == null) return "无会话";
            FarmGrid grid = S.World.Farm;
            for (int i = 0; i < grid.Slots.Length; i++)
            {
                FarmPlot p = grid.Slots[i];
                if (p.State != PlotState.Growing && p.State != PlotState.Grown) continue;
                // 焦距放在第一块地和**它的右邻的中点**上，一次能拍进两块——
                // 对比组的两个品种就是种在相邻格的，只框一格看不到另一个。
                Vector2 f = p.World;
                FarmPlot nb = grid.Plot(i + 1);
                if (nb != null && (nb.State == PlotState.Growing || nb.State == PlotState.Grown))
                    f = (p.World + nb.World) * 0.5f;

                _artFocus = new Vector3(f.x, 0f, f.y);
                _artCamOffset = new Vector3(0f, 9.0f, -0.6f);
                _artCamEuler = new Vector3(88f, 0f, 0f);
                _artShot = true;
                ArmArtShot();
                _artFollowPlayer = false;      // 固定在耕地上，不跟玩家跑
                return $"耕地特写 → 第 {i} 格｜作物 {p.CropSeedId}｜生长 {p.GrowthProgress01:P0}｜状态 {p.State}";
            }
            return "场上还没有正在生长的作物";
        }

        public string WhoAmI()
        {
            var sb = new StringBuilder();
            sb.Append($"调用对象={gameObject.name} playing={Application.isPlaying} 帧={Time.frameCount} ");
            sb.Append($"静态实例数={GameBootstrap.Live.Count} StartRun 次数={GameBootstrap.StartRunCount} 场景={gameObject.scene.name} ");
            foreach (GameBootstrap b in GameBootstrap.Live)
            {
                sb.Append($"[Live {b.name} ready={b.Ready} enabled={b.enabled} " +
                          $"session={(b.Session == null ? "null" : "wave" + b.Session.WaveIndex)}] ");
            }
            GameBootstrap[] found = FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include);
            sb.Append($"Find 到 {found.Length} 个：");
            foreach (GameBootstrap b in found)
            {
                sb.Append($"[Find {b.name} ready={b.Ready} " +
                          $"session={(b.Session == null ? "null" : "wave" + b.Session.WaveIndex)}] ");
            }
            string text = sb.ToString();
            Log.Info("WHOAMI " + text);
            return text;
        }

        /// <summary>数值自检（与 EditMode 单测共用期望值），无人值守也能跑。</summary>
        public string SelfCheck()
        {
            // 自检异常必须自带定位信息：否则桥只回一句 NullReferenceException，无法排查
            System.Collections.Generic.List<Seedfall.CheckResult> results;
            try
            {
                results = Seedfall.SelfCheck.RunAll();
            }
            catch (System.Exception ex)
            {
                string where = ex.StackTrace ?? "(no stack)";
                Log.Error($"SELFCHECK 抛异常：{ex.GetType().Name}: {ex.Message}\n{where}");
                return $"自检 CRASH：{ex.GetType().Name}: {ex.Message}\n堆栈首行：{FirstLine(where)}";
            }
            string report = Seedfall.SelfCheck.Report(results);
            Log.Info("SELFCHECK\n" + report);
            return report;
        }

        /// <summary>
        /// v171：**插件诊断** —— 一次调用回答"插件这条链现在通不通"。
        ///
        /// ★ 为什么不只靠 `SelfCheck()` ★
        /// 自检是一条长链：**前面任何一条断言抛异常，后面的插件断言就全都不跑了** ✗
        /// （实测：植物那一段的 fixture 抛 `KeyNotFoundException` 时，
        ///    `PluginChecks` 一次都没执行 ⇒ 插件的证据全部消失 ✓）
        /// 所以插件需要一条**自己就能跑完**的入口 ✓
        ///
        /// 它只做四件事，每件都是**可读的数字**：
        ///   ① 表里每把武器有几件、分别改的是哪一环
        ///   ② 往每把武器装到满 ⇒ **第 7 件被拒的原文**
        ///   ③ 装满之后的聚合修正（每个字段都打印出来）
        ///   ④ 商店 200 次刷新里插件占多少、出现过几种
        /// UI 那一层与后续自动化可以直接调它 ✓
        /// </summary>
        public string PluginDiag(string spec)
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.Append("PLUGINDIAG 表内 ").Append(TableDatabase.Plugins.Length)
              .Append(" 件｜每把武器 ").Append(PlayerGoods.MaxPluginsPerWeapon).Append(" 个插槽\n");

            // ── ① 分武器的清单：id[改了哪一环 数值]
            for (int w = 0; w <= 2; w++)
            {
                var slot = (WeaponSlot)w;
                int n = 0;
                sb.Append("  ").Append(RunSession.WeaponSlotName(slot)).Append("（");
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                {
                    PluginAsset a = TableDatabase.Plugins[i];
                    if (a.allowedWeapons == null || a.allowedWeapons.Length != 1 || a.allowedWeapons[0] != slot) continue;
                    n++;
                    sb.Append(a.id).Append('[').Append(WeaponSystem.ChannelOf(a.effect)).Append(' ')
                      .Append(a.amount.ToString("0.###"));
                    if (a.amount2 > 0f) sb.Append('/').Append(a.amount2.ToString("0.###"));
                    sb.Append("] ");
                }
                sb.Append("）⇒ ").Append(n).Append(" 件\n");
            }

            // ── ② 装满 + 第 7 件被拒的原文
            var s = new RunSession();
            s.Start(20240607UL);
            for (int w = 0; w <= 2; w++)
            {
                var slot = (WeaponSlot)w;
                string firstReject = "";
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                {
                    PluginAsset a = TableDatabase.Plugins[i];
                    if (a.allowedWeapons == null || a.allowedWeapons.Length != 1 || a.allowedWeapons[0] != slot) continue;
                    if (!WeaponSystem.Install(s.Goods, a, slot, out string why) && firstReject.Length == 0)
                        firstReject = a.id + " → " + why;
                }
                WeaponModifiers m = WeaponSystem.Aggregate(s.Goods, slot);
                sb.Append("  ").Append(RunSession.WeaponSlotName(slot))
                  .Append(" 装了 ").Append(s.Goods.SlotsUsedOn(slot)).Append('/').Append(PlayerGoods.MaxPluginsPerWeapon)
                  .Append(" 件｜首件被拒：").Append(firstReject.Length == 0 ? "(无)" : firstReject).Append('\n')
                  .Append("    聚合：伤害+").Append(m.DamageFlat.ToString("0.##"))
                  .Append(" ×").Append(m.DamageMult.ToString("0.###"))
                  .Append(" 攻速+").Append(m.AttackSpeedPct.ToString("P0"))
                  .Append(" 范围+").Append(m.RangePct.ToString("P0"))
                  .Append(" 暴击+").Append(m.CritRateFlat.ToString("P0")).Append('/').Append(m.CritDamageFlat.ToString("P0"))
                  .Append(" 爆炸半径+").Append(m.ExplodeRadiusPct.ToString("P0"))
                  .Append(" 弹道+").Append(m.ProjectileCount)
                  .Append(" 击退+").Append(m.KnockbackFlat.ToString("0.##"))
                  .Append("｜剑气").Append(m.SwordWaveCount)
                  .Append(" 寒冷+").Append(m.ChillPerHit)
                  .Append(" 回弹").Append(m.AmmoPerKill.ToString("0.#"))
                  .Append(" 灼烧").Append(m.BurnPerHitPct.ToString("P0"))
                  .Append(" 连锁").Append(m.ChainTargets).Append('×').Append(m.ChainRatio.ToString("P0"))
                  .Append(" 回声").Append(m.BlastEchoPct.ToString("P0"))
                  .Append(" 炸裂").Append(m.CritBlastPct.ToString("P0"))
                  .Append(" 满血").Append(m.FullHpBonusPct.ToString("P0"))
                  .Append("（来自 ").Append(m.SourceCount).Append(" 件）\n");
            }

            // ── ③ 全部效果 → "改了哪一环" 的对照（证明没有"写了没人用"的枚举值）
            sb.Append("  效果对照：");
            foreach (PluginEffectKind k in Enum.GetValues(typeof(PluginEffectKind)))
            {
                if (k == PluginEffectKind.None) continue;
                int used = 0;
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                    if (TableDatabase.Plugins[i].effect == k) used++;
                sb.Append(k).Append("→").Append(WeaponSystem.ChannelOf(k)).Append('(').Append(used).Append("件) ");
            }
            sb.Append('\n');

            // ── ④ 商店：插件占多少、出现过几种
            var probe = new RunSession();
            probe.Start(90210UL);
            probe.WaveIndex = 6;
            int nSeed = 0, nPlugin = 0, nItem = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                probe.Shop.Refresh(probe);
                for (int k = 0; k < probe.Shop.Offers.Count; k++)
                {
                    switch (probe.Shop.Offers[k].Kind)
                    {
                        case OfferKind.Seed: nSeed++; break;
                        case OfferKind.Plugin: nPlugin++; seen.Add(probe.Shop.Offers[k].Plugin.id); break;
                        case OfferKind.Item: nItem++; break;
                    }
                }
            }
            sb.Append("  商店 200 次刷新 800 个货位 ⇒ 种子 ").Append(nSeed).Append('/').Append((nSeed / 8f).ToString("0.0")).Append('%')
              .Append(" 插件 ").Append(nPlugin).Append('/').Append((nPlugin / 8f).ToString("0.0")).Append('%')
              .Append(" 道具 ").Append(nItem).Append('/').Append((nItem / 8f).ToString("0.0")).Append('%')
              .Append("（权重 种子").Append(probe.T.shop.seedWeight).Append("/插件").Append(probe.T.shop.pluginWeight)
              .Append("/道具").Append(probe.T.shop.itemWeight).Append("）｜出现过 ")
              .Append(seen.Count).Append('/').Append(TableDatabase.Plugins.Length).Append(" 种插件\n");

            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        private static string FirstLine(string text)
        {
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl).Trim();
        }

        public string Say(string message)
        {
            Log.Info("DEBUG " + message);
            return message;
        }

        public void SkipPhase()
        {
            RunSession s = S;
            if (s == null) return;
            s.WaveDirectorSkipToEnd();
            Log.Info("DEBUG SkipPhase");
        }

        public void KillAll()
        {
            RunSession s = S;
            if (s == null) return;
            for (int i = s.World.Enemies.Count - 1; i >= 0; i--) s.World.Enemies[i].Health.Kill();
            Log.Info("DEBUG KillAll 已清场");
        }

        public void JumpToWave(string index)
        {
            RunSession s = S;
            if (s == null) return;
            int wave = ParseInt(index, s.WaveIndex + 1);
            s.JumpToWave(wave);
            Log.Info($"DEBUG JumpToWave {wave}");
        }

        public void AddGold(string amount)
        {
            RunSession s = S;
            if (s == null) return;
            int g = ParseInt(amount, 50);
            s.AddGoldDirect(g);
            Log.Info($"DEBUG AddGold {g} → {s.Gold}");
        }

        public void AddExp(string amount)
        {
            RunSession s = S;
            if (s == null) return;
            float e = ParseFloat(amount, 50f);
            s.World.AddExp(e);
            Log.Info($"DEBUG AddExp {e} → Lv{s.Progress.Level} {s.Progress.Exp:0}/{s.Progress.ExpToNext:0}");
        }

        /// <summary>给种子袋塞一个种子。spec = "seedId[:mutation[:quality]]"，quality 0 白 … 4 金。</summary>
        public void GrantSeed(string spec)
        {
            RunSession s = S;
            if (s == null) return;
            string[] parts = (spec ?? "pea_shooter").Split(':');
            SeedDef def = TableDatabase.GetSeed(parts[0].Trim());
            string mutation = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
            int quality = parts.Length > 2 ? ParseInt(parts[2], 0) : 0;
            bool ok = s.Bag.AddSeed(def, mutation, quality, out string reason);
            Log.Info($"DEBUG GrantSeed {def.Id}{(mutation == null ? "" : ":" + mutation)} 品质={s.T.quality.NameOf(quality)} ok={ok} {reason}");
        }

        /// <summary>模拟鼠标点击种子袋槽位：点一下只是**选中**该槽位（合成走商店的"合成"按钮）。</summary>
        public void ClickSlot(string spec)
        {
            RunSession s = S;
            if (s == null) return;
            int slot = ParseInt(spec, -1);
            s.HandleBagClickPublic(slot);
            Log.Info($"DEBUG ClickSlot {slot} → 选中={s.MergeSelected} 槽位={BagSummary(s)}");
        }

        public void GrantItem(string id)
        {
            RunSession s = S;
            if (s == null) return;
            ItemDef item = TableDatabase.GetItem(id);
            if (item == null)
            {
                Log.Warn($"DEBUG GrantItem 未知道具 {id}");
                return;
            }
            s.ApplyItem(item);
            Log.Info($"DEBUG GrantItem {item.Id} → {item.Desc}");
        }

        public void HurtPlayer(string amount)
        {
            RunSession s = S;
            if (s == null) return;
            float dmg = ParseFloat(amount, 25f);
            s.World.Player.Health.ApplyDamage(dmg);
            Log.Info($"DEBUG HurtPlayer {dmg} → {s.World.Player.Health.Hp:0}/{s.World.Player.Health.MaxHp:0}");
        }

        public void SpawnEnemy(string spec)
        {
            RunSession s = S;
            if (s == null) return;
            string[] parts = (spec ?? "grunt:1").Split(':');
            string id = parts[0].Trim();
            int count = parts.Length > 1 ? ParseInt(parts[1], 1) : 1;
            Vector2 center = s.World.Player.Pos;
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = s.World.Rng.InsideCircle(4f);
                s.World.SpawnEnemy(id, World.ClampToArena(center + offset, 0.5f, s.T.farm.arenaHalf), s.WaveIndex);
            }
            Log.Info($"DEBUG SpawnEnemy {id} x{count}");
        }

        public void SetBot(string name)
        {
            RunSession s = S;
            if (s == null) return;
            if (string.IsNullOrEmpty(name) || name == "off" || name == "-1")
            {
                Boot.SetBot(-1);
                Log.Info("DEBUG SetBot off");
                return;
            }
            if (Enum.TryParse(name, true, out BotStrategy strategy))
            {
                Boot.SetBot((int)strategy);
                Log.Info($"DEBUG SetBot {strategy}");
            }
            else
            {
                Log.Warn($"DEBUG SetBot 未知策略 {name}（可用 Idle/Farmer/Balanced/Rush/off）");
            }
        }

        public void Restart(string seedText)
        {
            if (Boot == null) return;
            ulong seed = (ulong)ParseInt(seedText, 20240607);
            Boot.StartRun(seed);
            Log.Info($"DEBUG Restart seed={seed}");
        }

        public void Simulate(string spec)
        {
            string[] parts = (spec ?? "Balanced:20240607").Split(':');
            BotStrategy strategy = Enum.TryParse(parts[0].Trim(), true, out BotStrategy st) ? st : BotStrategy.Balanced;
            ulong seed = (ulong)(parts.Length > 1 ? ParseInt(parts[1], 20240607) : 20240607);
            SimResult r = RunSimulator.Run(strategy, seed);
            Log.Info("SIM " + r.ToLine());
        }

        public void DeterminismCheck(string spec)
        {
            string[] parts = (spec ?? "Balanced:20240607").Split(':');
            BotStrategy strategy = Enum.TryParse(parts[0].Trim(), true, out BotStrategy st) ? st : BotStrategy.Balanced;
            ulong seed = (ulong)(parts.Length > 1 ? ParseInt(parts[1], 20240607) : 20240607);
            bool ok = RunSimulator.VerifyDeterminism(strategy, seed, out string report);
            Log.Info($"DETERMINISM {(ok ? "PASS" : "FAIL")}\n{report}");
        }

        public void TelemetryTail(string countText)
        {
            int n = ParseInt(countText, 20);
            int from = Mathf.Max(0, Telemetry.Buffer.Count - n);
            var sb = new StringBuilder();
            for (int i = from; i < Telemetry.Buffer.Count; i++) sb.AppendLine(Telemetry.Buffer[i]);
            Log.Info("TELEMETRY\n" + sb);
        }

        private static string Fail(string why)
        {
            Log.Warn("DEBUG " + why);
            return why;
        }

        private static int ParseInt(string text, int fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            return int.TryParse(text.Trim(), out int v) ? v : fallback;
        }

        private static float ParseFloat(string text, float fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            return float.TryParse(text.Trim(), out float v) ? v : fallback;
        }
    }
}
