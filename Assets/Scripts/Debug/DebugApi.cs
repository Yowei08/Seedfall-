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
            // ★ v195：用**未过时**的重载（`FindObjectsSortMode` 版已被 Unity 6 标记 obsolete ⇒
            //   CS0618 警告 ×2/处）。这里的结果随后被按高度重新排序 ⇒ **不需要**引擎先排一遍 ✓
            Renderer[] rs = FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || r.transform == null) continue;
                // ★ v195：删掉 `if (r is CanvasRenderer) continue;` ★
                //   `CanvasRenderer` **不是** `Renderer` 的子类 ⇒ 那个判断永远为 false（CS0184 警告），
                //   而且它本来也过滤不掉任何东西（UI 根本不在 `rs` 里）⇒ 一行死代码 ✓
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

        // ══════════════════════════════════════════════════════════════════════
        // ★ v181：攻击**上下半身分层**的验收入口（三个，全部只写表现层/相机）★
        //
        // 为什么非要新增入口：这三件事都**没有现成的路**，而它们恰好是
        // "上半身跟没跟片段走 / 腰有没有断 / 脚滑不滑"唯一能取到数的三个口子 ✗
        //   `PoseFreeze` 停相位 —— 攻击只有 0.5~1.5 秒，桥往返几百毫秒，不停就永远拍在随机相位
        //   `SlideProbe` 量脚滑 —— 需要逐帧累加，单点读数做不到
        //   `PlayerCam`  取景   —— 默认跟随相机是远处俯视，玩家几十像素，看不出腰和腿
        // 三者都**不改变玩法**（与既有 `BattleView` / `Capture` / `DebugAttackPoseAt` 同一性质）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// v181：把攻击相位**冻结**在进度 `u`（0..1），`"off"` / `"-1"` / 空 = 解冻。
        ///
        /// 冻结只影响"攻击片段取哪一帧"和"程序化手部姿势取哪一相"，
        /// **不影响** locomotion 的时间轴 ⇒ 边走边打时腿照样在走 ✓
        /// （这是刻意的：否则"冻结"会把被测对象本身也冻掉）
        /// </summary>
        public string PoseFreeze(string spec)
        {
            string[] parts = (spec ?? string.Empty).Split(',');
            string key = parts[0].Trim().ToLowerInvariant();
            bool hold = parts.Length > 1 && parts[1].Trim().ToLowerInvariant() == "hold";
            if (key.Length == 0 || key == "off" || key == "-1")
            {
                ModelView.DebugAttackPoseAt = -1f;
                ModelView.DebugHoldAttack = false;
                Log.Info("DEBUG PoseFreeze off");
                return "姿态冻结=关";
            }
            float u = ParseFloat(key, -1f);
            if (u < 0f)
            {
                ModelView.DebugAttackPoseAt = -1f;
                ModelView.DebugHoldAttack = false;
                return "姿态冻结=关（u<0）";
            }
            ModelView.DebugAttackPoseAt = Mathf.Clamp01(u);
            // ★ 只有**显式**写 `u,hold` 才保持状态 ✗ —— 不把"冻结相位"偷偷升级成"冻结状态" ✓
            // 两者是两件事：前者要片段停在某一帧（读数用），后者还要**不许这个状态结束**（拍照用）
            ModelView.DebugHoldAttack = hold;
            string text = $"姿态冻结 u={ModelView.DebugAttackPoseAt:0.000} hold={hold}";
            Log.Info("DEBUG PoseFreeze " + text);
            return text;
        }

        /// <summary>
        /// v181：布防**脚滑采样**。spec = `帧数` 或 `帧数,tag`（默认 120 帧 ≈ 2 秒）。
        /// 采满后 `ModelView` 自己打一行 `FOOTSLIDE …` 到控制台（走 `/log` 读）。
        /// </summary>
        public void SlideProbe(string spec)
        {
            string text = (spec ?? string.Empty).Trim();
            int comma = text.IndexOf(',');
            int frames = comma > 0 ? ParseInt(text.Substring(0, comma), 120) : ParseInt(text, 120);
            string tag = comma > 0 ? text.Substring(comma + 1).Trim() : "footslide";
            ModelView.ArmFootSlideProbe(frames, tag);
            Log.Info($"DEBUG SlideProbe 布防 帧={frames} tag={tag}");
        }

        /// <summary>
        /// v181：把相机架到**角色相对方位**上 —— 拍"腰 / 腿"必须先看得见它们。
        /// spec = `相对角,距离,高度,俯角`；相对角 0 = 正后方、90 = 角色右手侧、180 = 正前方。
        ///
        /// ★ 为什么相对**角色朝向**而不是世界轴 ★
        /// 玩家一直在转身瞄准 ✗ 世界轴上的"侧面机位"在下一刻就变成正面或背面 ✓
        /// 相对朝向 ⇒ 同一个 spec 永远是同一个取景，两组截图才可比 ✓
        /// </summary>
        public string PlayerCam(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话";
            string[] parts = (spec ?? string.Empty).Split(',');
            float rel = parts.Length > 0 ? ParseFloat(parts[0], 90f) : 90f;
            float dist = parts.Length > 1 ? ParseFloat(parts[1], 4.6f) : 4.6f;
            float height = parts.Length > 2 ? ParseFloat(parts[2], 1.5f) : 1.5f;
            float pitch = parts.Length > 3 ? ParseFloat(parts[3], 10f) : 10f;

            Vector2 aim = s.World.Player.AimDir;
            Vector3 fwd = new Vector3(aim.x, 0f, aim.y);
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 dir = Quaternion.AngleAxis(rel, Vector3.up) * fwd;
            Vector2 pp = s.World.Player.Pos;
            _artFocus = new Vector3(pp.x, 0.6f, pp.y);
            _artCamOffset = dir * dist + Vector3.up * height;
            // 相机看向焦点 ⇒ 前向 = −dir；yaw 由 atan2(x,z) 反解（Unity 前向是 +Z）
            _artCamEuler = new Vector3(pitch, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg, 0f);
            _artShot = true;
            ArmArtShot();
            _artFollowPlayer = true;
            string text = $"机位 相对{rel:0}° 距{dist:0.0}m 高{height:0.0}m 俯{pitch:0}°";
            Log.Info("DEBUG PlayerCam " + text);
            return text;
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

        /// <summary>
        /// ★ v182：**抽搐（二阶差分）只读探针** ★ —— 推导见 `ModelView` 里"验收仪表③"那段。
        ///
        /// spec = `武器[:dt][:帧数][:move]`，默认 `hoe:1/60:120:stand`。
        /// 走 `ModelView.AttackJerkReplay`：**离屏定步长重放**（临时模型，跑完销毁），
        /// 不碰场上任何实例 ⇒ 只读 ✓ 返回值就是报告原文（`/input` 的 `returned` 字段里直接能读）✓
        /// </summary>
        public string AnimJerk(string spec)
        {
            string text = ModelView.AttackJerkReplay(spec);
            Log.Info("ANIMJERK " + text);
            return text;
        }

        /// <summary>
        /// v182：把抽搐采样布到**场上那个真玩家模型**上（逐帧，挂在 `ModelView.Tick` 末行）。
        /// spec = `帧数[:tag]`，默认 90。采满后 `ModelView` 自己打一行 `JERK …`（走 `/log` 读）。
        /// </summary>
        public void ArmAnimJerk(string spec)
        {
            string text = (spec ?? string.Empty).Trim();
            int colon = text.IndexOf(':');
            int frames = colon > 0 ? ParseInt(text.Substring(0, colon), 90) : ParseInt(text, 90);
            string tag = colon > 0 ? text.Substring(colon + 1).Trim() : "live";
            ModelView.ArmJerkProbe(frames, tag);
            Log.Info($"DEBUG ArmAnimJerk 布防 帧={frames} tag={tag}");
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
            // ★ v175：距离必须按**车的实际位置**算，不能再拿 `DockPoint` 顶替 ✗ ★
            //   车停靠时在**战场正上方**：`Player.Pos` 是地面坐标（Vector2）✓
            //   所以这里与 `TruckCanShop` 用同一条口径（水平距离 ✓）
            float half = s.T.farm.arenaHalf;
            Vector2 truckGround = truck.SamplePoint(s.Clock) * half;
            float dist = s.World != null && s.World.Player != null
                ? Vector2.Distance(s.World.Player.Pos, truckGround) : -1f;
            s.RequestShopOpen();
            Log.Info($"DEBUG ShopKey（按 E）→ 阶段 {truck.Current}｜离车（水平）{dist:0.00}m"
                     + $"｜车世界 ({s.TruckWorldPos.x:0.0},{s.TruckWorldPos.y:0.0},{s.TruckWorldPos.z:0.0})"
                     + $"｜可交互 {truck.CanShop(dist)}｜进店前 Phase={s.Phase}");
        }

        /// <summary>
        /// ★ v175：**商店车此刻的完整读数**（只读 ✓ 不改任何状态）★
        ///
        /// ★ 为什么需要它 ★
        /// "一个周期的时间线 + 停下的坐标 + 按 E 能不能进店"这份证据，
        /// 原先要把 `TruckDock` / `ShopKey` / `/probe` 三条输出**拼**起来才够 ✗
        /// 而每一轮 60 秒里阶段在变 ⇒ 分开取的三条读数**不是同一时刻的** ✗ 不能当证据 ✓
        /// ⇒ 一个方法把"这一段时刻"的全部关键量一次说完 ✓
        /// 每行都带 `t=游戏时间` ⇒ 多次调用可以直接排出时间线 ✓
        ///
        /// ⚠️ 它读的每个量都**来自引擎自己**（`Truck.*` / `TruckCanShop` / `TruckWorldPos`）✗
        ///    没有一处是"按设计值另算的"✓ 否则就成了"观测在自我安慰"✗
        /// </summary>
        public void TruckState()
        {
            RunSession s = S;
            if (s == null) { Log.Warn("DEBUG TruckState 会话未启动"); return; }
            ShopTruck t = s.Truck;
            Vector3 wp = s.TruckWorldPos;
            float half = s.T.farm.arenaHalf;
            float dist = s.World != null && s.World.Player != null
                ? Vector2.Distance(s.World.Player.Pos, new Vector2(wp.x, wp.z)) : -1f;
            Log.Info($"TRUCK t={s.Clock.GameTime:0.00}s｜{t.Current}"
                     + $"｜阶段已历 {t.PhaseElapsed(s.Clock.GameTime):0.00}s"
                     + $"／本段时长 {ShopTruck.DurationOf(t.Current):0.00}s"
                     + $"｜世界位置 ({wp.x:0.0}, {wp.y:0.0}, {wp.z:0.0})｜场地半宽 {half:0.0}"
                     + $"｜玩家水平距离 {dist:0.00}m"
                     + $"｜CanShop {t.CanShop(dist)}｜TruckCanShop(会话) {s.TruckCanShop}"
                     + $"｜停靠剩余 {t.DockSecondsLeft(s.Clock):0.0}s"
                     + $"｜全场敌人 {s.World?.Enemies.Count ?? -1}"
                     + $"｜击杀总数 {s.World?.KillCount ?? -1}"
                     + $"｜Phase {s.Phase}｜金币 {s.Gold}");
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
            Vector3 wp = s.TruckWorldPos;
            // 归一化坐标由世界位置**反推**（不重新采样一次 ✗ 否则读数是"另一时刻"的 ✓）
            Vector2 np = new Vector2(wp.x / s.T.farm.arenaHalf, wp.z / s.T.farm.arenaHalf);
            Log.Info($"DEBUG TruckDock → {p}｜归一化 ({np.x:0.00},{np.y:0.00})"
                     + $"｜世界 ({wp.x:0.0}, {wp.y:0.0}, {wp.z:0.0})｜场地半宽 {s.T.farm.arenaHalf:0.0}"
                     + $"｜停靠位 {ShopTruck.DockPoint.x:0.00},{ShopTruck.DockPoint.y:0.00}"
                     + $"（战场正上方，高度 {ShopTruck.HoverHeight:0.0}m）"
                     + $"｜停靠剩余 {s.Truck.DockSecondsLeft(s.Clock):0.0}s");
            // ★ v175：再打一行完整读数 ⇒ "摆到哪 + 那一刻能不能进店"是**同一时刻**的两个事实 ✓
            TruckState();
        }

        /// <summary>
        /// ★ v175.1：**绕场→停靠接缝的高度连续性**（复刻 `SelfCheck` 那条 ASSERT 的算法）★
        ///
        /// ★ 为什么单独做一个读数 ✗ ★
        /// 那条断言是按 **0.02s 步进采样**的 ✗ ⇒ 它取到的"绕场末尾"**永远不是** `PhaseT == 1` ✓
        /// 于是它实际测的是"**最后一步的高度增量**"✗
        /// ⇒ 只有把**同一算法**在真机上跑一遍，"修好了没有"才是可证的 ✓
        ///   （只看源码推"应该连续"是不够的 ✗ —— 那条断言当初就是这么红的 ✓）
        ///
        /// 同时给出**解析值**（`PhaseT` 恰好取 1 时）做对照：
        /// 两者都接近 0 才算真连续 ✓ 只有解析值是 0 而采样值大 ⇒ 说明是采样伪影 ✓
        /// </summary>
        public void TruckSeam()
        {
            var clk = new Clock();
            var t = new ShopTruck();
            t.Reset(clk);
            int ticks = Mathf.CeilToInt(
                (ShopTruck.CycleSeconds + ShopTruck.EnterSeconds + ShopTruck.LoopSeconds - 0.02f) / 0.02f);
            for (int i = 0; i < ticks; i++) { clk.Tick(0.02f); t.Tick(clk); }
            bool stillLoop = t.Current == ShopTruck.Phase.Looping;
            float loopEnd = t.HeightAt(clk);
            float loopAnalytic = ShopTruck.HoverHeight;      // PhaseT == 1 时的值（曲线右端）
            clk.Tick(0.02f); t.Tick(clk);
            float dockStart = t.HeightAt(clk);
            Log.Info($"TRUCK-SEAM｜{ShopTruck.Phase.Looping} 末尾（采样）{loopEnd:0.0000}m"
                     + $"｜解析端点 {loopAnalytic:0.0000}m"
                     + $"｜{ShopTruck.Phase.Dock} 开始 {dockStart:0.0000}m"
                     + $"｜★ 采样差 {Mathf.Abs(loopEnd - dockStart):0.000000}m ★（断言容差 0.001m）"
                     + $"｜解析差 {Mathf.Abs(loopAnalytic - dockStart):0.000000}m"
                     + $"｜末尾仍在 {t.Current}（前一步是 Looping={stillLoop}）"
                     + $"｜爬升段 = 绕场后 {(1f - ShopTruck.HoverRiseT) * 100f:0}%（{ShopTruck.LoopSeconds * (1f - ShopTruck.HoverRiseT):0.00}s）");
        }

        /// <summary>
        /// ★ 商店车一轮的**位置 / 高度时间序列**（只读观测）★
        ///
        /// ── 为什么单帧读数（`TruckState`）不够 ✗ ──
        /// 用户报的三个现象全是**时间序列**上的事实：
        ///   ① "绕场转了两圈" —— 只有把"一圈里走了多长路"累加起来才判得出 ✓
        ///   ② "离开时还在转" —— 只有看离场期的**横向位移**才判得出 ✓
        ///   ③ "最后飞天 / 停留地点异常" —— 只有看离场期的**高度序列**与**末点坐标**才判得出 ✓
        /// 单帧只能回答"这一瞬间在哪"，回答不了"这一段做了几圈" ✗
        ///
        /// ── 它为什么用**独立实例**，而不是场上那辆 ✓ ──
        /// 场上的车受 `Clock`（真人一局的时间）驱动，要等 97.5 秒才进停靠 ⇒ 不可复现 ✗
        /// 这里 `new ShopTruck()` + `new Clock()` **从 0 跑到两轮**，
        /// 所以同一份代码每次跑出的数**逐位相同** ⇒ 前后对比才有意义 ✓
        /// 与 `TruckSeam` 是同一个路数（那条也是自己造实例 ✓）✗ 不改任何玩法状态 ✓
        ///
        /// ── 打出来的四个判据（都能一眼判对错）──
        ///   ① `Entering` / `Looping` / `Leaving` 各走过多少米 ⇒ ÷ 路径长 = **几圈**
        ///      （绕两圈的病因就是"入场段也走了一整圈"⇒ 这两段各 1.00 圈加起来 2.00 ✓）
        ///   ② 离场高度序列 + 单调性 ⇒ 是不是**一路降**、降没降到**地面以下**
        ///   ③ `Dock → Leaving` 接缝**单帧位移** ⇒ 是不是瞬移（"飞天"的观感来源）
        ///   ④ 离场末点坐标 ⇒ 在不在**场地外**、还悬不悬在**半空**
        /// </summary>
        public string TruckTrace()
        {
            float half = TableDatabase.Tuning.farm.arenaHalf;
            float pathLen = ShopTruck.NormalizedLength() * half;
            const float step = 0.02f;
            int maxSteps = Mathf.CeilToInt(ShopTruck.CycleSeconds * 2.2f / step);

            var clk = new Clock();
            var t = new ShopTruck();
            t.Reset(clk);

            var timeline = new StringBuilder();
            var leaveHeights = new StringBuilder();
            ShopTruck.Phase prevPhase = t.Current;
            Vector3 prevWorld = t.SampleWorld(clk, half);
            Vector3 dockLast = prevWorld;
            float enterDist = 0f, loopDist = 0f, leaveDist = 0f;
            float maxJump = 0f;
            string maxJumpAt = "";
            float seamJump = 0f;
            bool seamTaken = false;
            Vector3 leaveEnd = prevWorld;
            Vector3 leaveStart = prevWorld;
            float leaveEndH = 0f, leaveMinH = float.MaxValue, leaveMaxH = float.MinValue;
            float nextLeaveLog = -1f;
            int enterings = 0, loopings = 0, leavings = 0;

            for (int i = 0; i < maxSteps; i++)
            {
                clk.Tick(step);
                t.Tick(clk);
                Vector3 w = t.SampleWorld(clk, half);
                float d = (w - prevWorld).magnitude;
                // ★ 数"圈"只认**水平**位移 ✗ ★
                //   `Looping` 最后 15% 是**边冲刺边升空**（0 → 6m）⇒
                //   若用 3D 距离，那 6 米垂直位移会被算进"绕场里程" ⇒ 一圈会被读成 1.01 圈 ✗
                //   （实测：3D 口径 116.0m、水平口径 115.2m = 恰好 1.00 圈 ✓）
                //   垂直位移仍留在 `d` 里，供"单帧最大位移"用（那是 3D 的观感问题 ✓）
                float dh = new Vector3(w.x - prevWorld.x, 0f, w.z - prevWorld.z).magnitude;

                if (t.Current != prevPhase)
                {
                    timeline.Append($"{clk.GameTime:0.00}s→{t.Current}　");
                    if (t.Current == ShopTruck.Phase.Entering) enterings++;
                    else if (t.Current == ShopTruck.Phase.Looping) loopings++;
                    else if (t.Current == ShopTruck.Phase.Leaving) leavings++;
                    // 接缝 = Dock 的最后一帧 → Leaving 的第一帧，`d` 正是在那一刻算出来的 ✓
                    if (prevPhase == ShopTruck.Phase.Dock && !seamTaken) { seamTaken = true; seamJump = d; }
                    // 离场起点 = Dock 的最后一帧（此刻 `prevWorld` 还是旧值 ✓）
                    if (t.Current == ShopTruck.Phase.Leaving) leaveStart = prevWorld;
                    prevPhase = t.Current;
                    nextLeaveLog = -1f;
                    // ⚠️ **换相那一帧的 `d` 不计入"走了多远"** ✗
                    //   那一帧的位移可能是一次"出生/换场"（Idle 里位置本无意义、
                    //   切到 `Entering` 的第一帧直接出现在入场点）⇒ 计入会把里程算高 ✓
                    //   这类瞬移**单独报**（出生瞬移 / 接缝瞬移），不算"行驶里程" ✓
                    prevWorld = w;
                    continue;
                }

                switch (t.Current)
                {
                    case ShopTruck.Phase.Entering: enterDist += dh; break;
                    case ShopTruck.Phase.Looping: loopDist += dh; break;
                    case ShopTruck.Phase.Leaving:
                    {
                        leaveDist += dh;
                        float h = t.HeightAt(clk);
                        leaveMinH = Mathf.Min(leaveMinH, h);
                        leaveMaxH = Mathf.Max(leaveMaxH, h);
                        leaveEnd = w;
                        leaveEndH = h;
                        // 每 0.25s 记一个高度（离场 2.5s ⇒ 约 10 个数），太密会刷屏 ✓
                        if (clk.GameTime >= nextLeaveLog)
                        {
                            nextLeaveLog = clk.GameTime + 0.25f;
                            leaveHeights.Append($"{h:0.00} ");
                        }
                        break;
                    }
                }

                if (d > maxJump) { maxJump = d; maxJumpAt = $"{clk.GameTime:0.00}s（{prevPhase}）"; }
                if (t.Current == ShopTruck.Phase.Dock) dockLast = w;
                prevWorld = w;
                if (clk.GameTime > ShopTruck.CycleSeconds * 2f) break;
            }

            // ★ 单调性单独跑一遍逐帧判（上面那一遍只按 0.25s 取样，可能漏掉回弹的一帧）★
            var clk2 = new Clock();
            var t2 = new ShopTruck();
            t2.Reset(clk2);
            float prevH = float.MaxValue;
            bool mono = true, sawLeave = false;
            for (int i = 0; i < maxSteps; i++)
            {
                clk2.Tick(step);
                t2.Tick(clk2);
                if (t2.Current != ShopTruck.Phase.Leaving) continue;
                sawLeave = true;
                float h = t2.HeightAt(clk2);
                if (h > prevH + 1e-4f) mono = false;
                prevH = h;
                if (clk2.GameTime > ShopTruck.CycleSeconds * 2f) break;
            }

            float laps = (enterDist + loopDist) / pathLen;
            float loopLaps = loopDist / pathLen;

            // ══ 旧算法对照（v179 之前）══
            // 为什么要在**同一次运行里**复刻旧公式，而不是"改前测一次、改后再测一次"✗
            //   `ShopTruck` 的取位公式已经被改掉了，要测旧的就得回退源码 ⇒
            //   两次测量之间会隔着一次重编译、一次重载、一次重新入场 ⇒ **尺子可能变了** ✓
            //   这里只复刻**两条纯函数**（取位 + 高度），状态机/时长/路径**都不碰** ⇒
            //   新旧两组数字出自**同一个循环、同一个 Clock、同一把尺子** ✓
            // ⚠️ 它**不**影响场上那辆车，也不参与任何玩法判定（只写进本方法的字符串）✓
            var lk = new Clock();
            var lt = new ShopTruck();
            lt.Reset(lk);
            ShopTruck.Phase lPrev = lt.Current;
            Vector3 lw = LegacyWorld(lt, lk, half);
            Vector3 lPrevWorld = lw;
            float lEnter = 0f, lLoop = 0f, lLeave = 0f;
            Vector3 lLeaveStart = lw, lLeaveEnd = lw;
            float lLeaveEndH = 0f, lSeam = 0f;
            bool lSeamTaken = false;
            for (int i = 0; i < maxSteps; i++)
            {
                lk.Tick(step);
                lt.Tick(lk);
                lw = LegacyWorld(lt, lk, half);
                float d = (lw - lPrevWorld).magnitude;
                float dh = new Vector3(lw.x - lPrevWorld.x, 0f, lw.z - lPrevWorld.z).magnitude;
                if (lt.Current != lPrev)
                {
                    if (lPrev == ShopTruck.Phase.Dock && !lSeamTaken) { lSeamTaken = true; lSeam = d; }
                    if (lt.Current == ShopTruck.Phase.Leaving) lLeaveStart = lPrevWorld;
                    lPrev = lt.Current;
                    lPrevWorld = lw;
                    continue;
                }
                switch (lt.Current)
                {
                    case ShopTruck.Phase.Entering: lEnter += dh; break;
                    case ShopTruck.Phase.Looping: lLoop += dh; break;
                    case ShopTruck.Phase.Leaving:
                        lLeave += dh;
                        lLeaveEnd = lw;
                        lLeaveEndH = LegacyWorld(lt, lk, half).y;
                        break;
                }
                lPrevWorld = lw;
                if (lk.GameTime > ShopTruck.CycleSeconds * 2f) break;
            }
            float lChord = new Vector3(lLeaveEnd.x - lLeaveStart.x, 0f, lLeaveEnd.z - lLeaveStart.z).magnitude;
            float chord = new Vector3(leaveEnd.x - leaveStart.x, 0f, leaveEnd.z - leaveStart.z).magnitude;
            float enterChord = (ShopTruck.EnterPoint - ShopTruck.EnterFrom).magnitude * half;
            float enterStraight = enterChord <= 0.001f ? 0f : enterDist / enterChord;
            bool outside = Mathf.Abs(leaveEnd.x) > half || Mathf.Abs(leaveEnd.z) > half;
            string text =
                $"TRUCK-TRACE｜场地半宽 {half:0.0}m｜路径一圈 {pathLen:0.00}m\n"
                + $"  阶段时间线：{timeline}\n"
                + $"  一轮里各阶段进入次数：{ShopTruck.Phase.Entering}×{enterings}"
                + $"｜{ShopTruck.Phase.Looping}×{loopings}｜{ShopTruck.Phase.Leaving}×{leavings}\n"
                + $"  入场 {enterDist:0.0}m（{enterDist / pathLen:0.00} 圈｜直线度 {enterStraight:0.000}）"
                + $"→验收 直线度 ≈1、≪ 1 圈\n"
                + $"  绕场 {loopDist:0.0}m（{loopLaps:0.00} 圈）"
                + $"→验收 ★ 1.00 圈（只绕一圈）★\n"
                + $"  离场 里程 {leaveDist:0.0}m｜净位移 {chord:0.0}m｜{StraightText(leaveDist, chord)}"
                + $"→验收 净位移 = 里程（直线开走）\n"
                + $"  ★ 一轮实际圈数 = {laps:0.00}（旧实现 1.98）★\n"
                + $"  离场高度序列（每 0.25s）：{leaveHeights}\n"
                + $"  离场高度区间 [{leaveMaxH:0.00}, {leaveMinH:0.00}]m"
                + $"｜逐帧单调不升={mono}（离场段取到={sawLeave}）\n"
                + $"  离场末点 世界 ({leaveEnd.x:0.0}, {leaveEndH:0.00}, {leaveEnd.z:0.0})"
                + $"｜在场地外={outside}｜仍停在空中={leaveEndH > 0.05f}"
                // ★ v205：多报一个"钻到地下" —— 用户报的"开到地里面"就是这个量 ✓
                + $"｜钻到地下={leaveEndH < -0.05f}\n"
                + $"  {ShopTruck.Phase.Dock}末点 ({dockLast.x:0.0}, {dockLast.y:0.0}, {dockLast.z:0.0})"
                + $"→{ShopTruck.Phase.Leaving}接缝单帧位移 {seamJump:0.00}m（验收 ≈0）\n"
                + $"  在场段单帧最大位移 = {maxJump:0.00}m @ {maxJumpAt}\n"
                + $"  ── 同一把尺子量旧算法（v179 之前）──\n"
                + $"  旧：入场 {lEnter:0.0}m（{lEnter / pathLen:0.00} 圈）"
                + $"｜绕场 {lLoop:0.0}m（{lLoop / pathLen:0.00} 圈）"
                + $"｜离场 里程 {lLeave:0.0}m｜净位移 {lChord:0.0}m｜{StraightText(lLeave, lChord)}\n"
                + $"  旧：一轮实际圈数 {(lEnter + lLoop) / pathLen:0.00}"
                + $"｜{ShopTruck.Phase.Dock}→{ShopTruck.Phase.Leaving}接缝 {lSeam:0.00}m"
                + $"｜离场末点 ({lLeaveEnd.x:0.0}, {lLeaveEndH:0.00}, {lLeaveEnd.z:0.0})\n"
                + $"  ⇒ 入场圈数 {lEnter / pathLen:0.00}→{enterDist / pathLen:0.00}"
                + $"｜离场净位移 {lChord:0.0}m→{chord:0.0}m"
                + $"｜接缝瞬移 {lSeam:0.00}m→{seamJump:0.00}m"
                + $"｜离场末点 x {lLeaveEnd.x:0.0}→{leaveEnd.x:0.0}（场地半宽 {half:0.0}）\n"
                + $"  ⇒ 离场末点高度 {lLeaveEndH:0.00}m→{leaveEndH:0.00}m（★ v205 验收：= 地面高度 {ShopTruck.ExitEndHeight:0.00}m，**不再**钻到地面以下）";
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// "是不是直线走的"这一判据的**文字版**（`TruckTrace` 用）。
        ///
        /// 为什么不直接打"里程 ÷ 净位移"✗
        ///   旧实现的离场**绕完一圈又回原地** ⇒ 净位移 ≈ 0.02m ⇒ 这个比值会算出 **5910** ✗
        ///   那不是"直线度"，那是**除零放大**的产物 ✓ 读的人只会以为程序坏了 ✓
        /// ⇒ 净位移小于 1m 时**直接说事实**（"起点终点相距 0.0m"），大于 1m 才报比值 ✓
        /// 走直线时 里程 == 净位移 ⇒ 比值 1.000 ✓ 一眼可判 ✓
        /// </summary>
        private static string StraightText(float dist, float chord)
            => chord < 1f ? $"起终点相距 {chord:0.00}m（绕回原地）" : $"直线度 {dist / chord:0.000}";

        /// <summary>
        /// ★ `TruckTrace` 的**旧算法对照**：v179 之前 `ShopTruck` 的取位 / 高度两条纯函数 ★
        ///
        /// 只复刻这两条公式，**不碰状态机、时长、路径**：
        ///   · 取位（旧）：`Dock` 取 `DockPoint`，**其余所有阶段**一律 `PointAt(PhaseT)`
        ///     ⇒ 入场 / 绕场 / 离场**各走一遍整条路径**（这就是"绕两圈"与"离开还在转"）
        ///   · 高度（旧）：`Leaving = (1 − PhaseT) × HoverHeight` ⇒ 只降到 **0**（地面）
        ///     ⇒ 车在场地中心落地，且接缝处高度还挂着 6m ⇒ "飞天"的观感 ✓
        /// ⚠️ 它**只为本方法的字符串服务**，不被任何玩法路径调用 ✓
        /// </summary>
        private static Vector3 LegacyWorld(ShopTruck t, Clock clock, float half)
        {
            float t01 = t.PhaseT(clock.GameTime);
            Vector2 np = t.Current == ShopTruck.Phase.Dock ? ShopTruck.DockPoint : ShopTruck.PointAt(t01);
            float h;
            switch (t.Current)
            {
                case ShopTruck.Phase.Dock: h = ShopTruck.HoverHeight; break;
                case ShopTruck.Phase.Leaving: h = (1f - t01) * ShopTruck.HoverHeight; break;
                case ShopTruck.Phase.Looping:
                {
                    if (t01 <= ShopTruck.HoverRiseT) { h = 0f; }
                    else
                    {
                        float k = (t01 - ShopTruck.HoverRiseT) / (1f - ShopTruck.HoverRiseT);
                        h = (k * k * (3f - 2f * k)) * ShopTruck.HoverHeight;
                    }
                    break;
                }
                default: h = 0f; break;
            }
            return new Vector3(np.x * half, h, np.y * half);
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
        /// 选开局种子（1/2/3），走的是**玩家按键那条路**：把 `InitialChoice` 喂进 `FixedTick`，
        /// 由 `RunSession.TickPrepare` 自己完成 加种子袋 → `AutoPlantSeed` → `StartWave` ✓
        ///
        /// ★ 为什么必须有它：`DebugApi` 现有的方法没有一个能离开 `Prepare` 阶段 ★
        /// 不选种子就永远是那个模态弹窗，后面的一切都看不到（这正是本轮复现卡住的地方）。
        /// 用 `FixedTick` 而不是直接改 `Session.InitialSeed` —— 后者会跳过
        /// "种子进袋 / 自动种下 / 开波"这三件事，种出来的状态与玩家按 1 得到的不是同一个 ✗
        /// </summary>
        public string DebugChooseSeed(int choice)
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动");
            if (s.Phase != PhaseState.Prepare) return $"当前阶段是 {s.Phase}，不是 Prepare（已经在跑或已选过）";
            int n = Mathf.Clamp(choice, 1, Mathf.Max(1, s.InitialChoices.Length));
            var input = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = n - 1
            };
            s.FixedTick(1f / 60f, input);
            string text = $"已选第 {n} 个初始种子 → 阶段 {s.Phase}｜"
                          + $"植物 {s.World.Farm.PlantCount}/{s.World.Farm.Columns} 株｜"
                          + $"已种格 {s.World.Farm.UsedPlotCount}/{s.World.Farm.PlotCount}";
            Log.Info("CHOOSESEED " + text);
            return text;
        }

        /// <summary>
        /// ★★ 造作物模型的那两个数 ★★ —— `RawHeight`（归一后的实测高度）与 `BaseScale`（根缩放）。
        ///
        /// 它们是 `ScaleToHeight` 的**输入**：`ScaleToHeight(h)` ⇒ `SetScaleFactor(h / RawHeight)`
        /// ⇒ `localScale = BaseScale × 该系数` ✓
        /// 所以只要打印这两个数，就能判出 `ModelView.Spawn` 里
        /// "网格是米制还是 1/100"那条判据到底走了哪一支 ✓（那一支决定要不要把根缩放压回 1）
        ///
        /// 同时报**世界位置**与**该格的世界坐标** —— 摆位差了多少一眼可见 ✓
        /// </summary>
        public string CropModelDiag()
        {
            RunSession s = S;
            if (s == null || s.World == null) return Fail("会话未启动");
            WorldView v = Boot != null ? Boot.View : null;
            if (v == null) return Fail("没有 WorldView（buildVisuals=false？）");
            FarmGrid g = s.World.Farm;
            var sb = new StringBuilder();
            sb.AppendLine($"CROPDIAG 列数={g.Columns} 行数={g.Rows} 格距={g.TileSize}");
            int shown = 0;
            for (int i = 0; i < g.Slots.Length && shown < 4; i++)
            {
                FarmPlot p = g.Slots[i];
                if (p == null || p.CropSeedId == null) continue;
                shown++;
                sb.AppendLine($"第{i}格 {p.CropSeedId} 状态={p.State} 生长={p.GrowthProgress01:P0} " +
                              $"格心=({p.World.x:0.00},{p.World.y:0.00})");
                AppendCropStage(sb, v.CropStageYoung(i), "幼苗");
                AppendCropStage(sb, v.CropStageGrown(i), "成长中");
                AppendCropStage(sb, v.CropStageMature(i), "成体");
            }
            if (shown == 0) sb.AppendLine("（还没有任何一格种着作物）");
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        private static void AppendCropStage(StringBuilder sb, ModelView mv, string label)
        {
            if (mv == null || mv.Go == null) { sb.AppendLine($"    {label}: (无)"); return; }
            Vector3 wp = mv.Go.transform.position;
            sb.AppendLine($"    {label}: RawHeight={mv.RawHeight:0.0000} 系数={mv.ScaleFactor:0.0000} " +
                          $"localScale={mv.Go.transform.localScale.x:0.000} BaseScale={mv.BaseScale.x:0.000} " +
                          $"居中={mv.LocalCenter.ToString("0.000")} 世界=({wp.x:0.00},{wp.y:0.00},{wp.z:0.00}) " +
                          $"激活={mv.Go.activeSelf}");
        }

        /// <summary>
        /// ★★ 全层级摆放审计 ★★ —— 按**渲染器**枚举，而不是按名字前缀。
        ///
        /// ★ 为什么不能按名字 ★：上一轮用的是"只枚举 `SCN_*` 与 `MDL_*`"✗
        /// ⇒ 得到"19 个对象 12 秒位移 ≤ 1mm，唯一会动的是小猪"✓
        /// ⇒ 而那个结论**只在那份名单内成立** ✗ —— 真正在游走的东西不在名单里 ✓
        /// 所以这一版**不做任何名字筛选**：谁有 Renderer 就报谁 ✓
        ///
        /// 判据（任何时刻读一次都能用，不需要自己记得上一帧）：
        ///   · `d`   = 到耕地中心的水平距离（耕地是 6×6×tileSize 的方形，中心在原点）
        ///   · `在田` = 是否落在耕地外沿内（`(Size-1)/2*tileSize + tileSize/2`）
        /// ↳ "在田外还有植物样子的东西"和"同一株两次读到的位置不同"都由这两列判出来 ✓
        /// </summary>
        public string AuditWorldPlacement()
        {
            if (!Application.isPlaying) return Fail("不在 play 模式，审计只对运行中的世界有意义");
            Renderer[] rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            var agg = new System.Collections.Generic.Dictionary<string, Vector3>();
            var count = new System.Collections.Generic.Dictionary<string, int>();
            var order = new System.Collections.Generic.List<string>();

            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || r.transform == null) continue;

                // 归组：走到**最外层**的命名容器（MDL_/SCN_/PLANT_/SOW_/RuinWall…），
                // 这样一个模型的十几个子件只留一条记录 ✓
                Transform t = r.transform;
                Transform up = t.parent;
                while (up != null)
                {
                    if (up.name == "SeedfallWorld" || up.name == "ArtPreview") break;
                    t = up;
                    up = up.parent;
                }
                string key = t.name;
                if (!agg.ContainsKey(key)) { agg[key] = Vector3.zero; count[key] = 0; order.Add(key); }
                agg[key] += r.bounds.center;
                count[key]++;
            }

            FarmDef farm = TableDatabase.Tuning.farm;
            float edge = (Mathf.Max(2, farm.gridSize) - 1) * 0.5f * farm.tileSize
                         + farm.tileSize * 0.5f;                   // 外沿：格心 ±6.25 + 半格 1.25 = 7.5

            var sb = new StringBuilder();
            sb.AppendLine($"AUDIT t={Time.realtimeSinceStartup:0.0}s 组数={order.Count} 渲染器={rs.Length} 田沿=±{edge:0.##}");
            for (int i = 0; i < order.Count; i++)
            {
                string k = order[i];
                Vector3 c = agg[k] / count[k];
                float d = new Vector2(c.x, c.z).magnitude;
                string flag = d <= edge ? "IN" : "OUT";
                sb.AppendLine($"{k}｜x={c.x:0.000} z={c.z:0.000}｜d={d:0.00} {flag}｜件={count[k]}");
            }
            string text = sb.ToString();
            Log.Info(text);
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

        /// <summary>
        /// ★ v180：**商店布局探针（只读）**★ —— 回答"界面到底抖不抖"。
        ///
        /// 用法：
        ///   `HudLayoutProbe("on")`  → 清空并开始记录（最多 60 帧）
        ///   `HudLayoutProbe("")`    → 读回已记录的每一帧
        ///   `HudLayoutProbe("off")` → 关闭
        ///
        /// 判据：把两行**去掉帧号后**逐字比对 —— 相同 ⇒ 布局稳定 ✓ 不同 ⇒ 抖动的就是那一段 ✓
        /// 它只读 `ImGuiHud` 的静态缓冲，**不改任何状态**（除了探针自己的开关与缓冲）✓
        /// </summary>
        public string HudLayoutProbe(string spec)
        {
            string arg = (spec ?? "").Trim().ToLowerInvariant();
            if (arg == "on" || arg == "1" || arg == "start")
            {
                ImGuiHud.LayoutProbe.Clear();
                ImGuiHud.LayoutProbeOn = true;
                Log.Info("LAYOUTPROBE on（下一帧起记录，最多 60 帧）");
                return "布局探针已开（下一帧起记录，最多 60 帧）→ 再调一次不带参数读取";
            }
            if (arg == "off" || arg == "0" || arg == "stop")
            {
                ImGuiHud.LayoutProbeOn = false;
                return "布局探针已关";
            }
            var sb2 = new StringBuilder();
            sb2.Append("LAYOUTPROBE ").Append(ImGuiHud.LayoutProbe.Count).Append(" 帧")
               .Append(ImGuiHud.LayoutProbeOn ? "（仍在记录）" : "（已停止）").Append('\n');
            for (int i = 0; i < ImGuiHud.LayoutProbe.Count; i++)
                sb2.Append("  ").Append(ImGuiHud.LayoutProbe[i]).Append('\n');
            string text = sb2.ToString();
            Log.Info(text);
            return text;
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
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
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

        // ══════════════════════════════════════════════════════════════════════
        // ★ v185：剑气抓图用的两个探针（只给验收，不参与任何玩法判定）★
        //
        // 为什么需要它们：桥接往返一次就 1 秒以上，而剑气寿命只有 0.264 秒 ⇒
        // "抓两张不同帧的图证明它在飞"这件事**靠手速做不到** ✗
        // 这与 `ForceSwing` / `FxHold` 是同一类工具：**把要验的那一瞬拉长**，
        // 而不是"看起来没问题就算验过了" ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v185：**当前这一局里在飞的剑气**（只读）★ —— 抓图时和截图一一对应地读它，
        /// 就能把"图里那道弯月此刻飞到哪儿了"变成可核对的数（而不是"看着像在动"）✓
        /// 输出：帧号 + 每一发的**框心位置** + 已飞 / 总飞行距离。
        /// </summary>
        public string WaveState(string spec)
        {
            RunSession s = S;
            if (s == null) return "无会话";
            var sb = new StringBuilder();
            sb.Append($"WAVESTATE 帧={Time.frameCount} 在飞={s.LiveSwordWaveCount}");
            for (int i = 0; i < s.LiveSwordWaveCount; i++)
            {
                Vector2 wp = s.SwordWavePosAt(i);
                sb.Append($"｜#{i} 框心=({wp.x:0.000},{wp.y:0.000})"
                          + $" 已飞={s.SwordWaveTravelAt(i):0.000}/{s.SwordWaveDistAt(i):0.000}m");
            }
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// v185 抓图专用：把朝向钉成 +X、关节冷却清零，然后**真的劈一刀**（走 `DebugAttackOnce`）。
        ///
        /// spec = 慢放倍率（默认 10）⇒ 剑气在这段时间里**持续飞行**，两次 `Capture` 之间
        /// 的位置差就是"它是子弹"的直接证据 ✓
        /// ★ 只设 `WorldView.DebugWaveHold`（只影响弯月的寿命/速度）★
        /// ⇒ 弯月**总是正好飞 `Dist`**（见 `OnSwordWave` 里 `Vel = Dist / Total` 那段）✓
        /// </summary>
        public string WaveFire(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话";
            float hold = ParseFloat(spec, 10f);
            WorldView.DebugWaveHold = Mathf.Max(1f, hold);   // ★ 只慢放剑气，别的特效照常
            PlayerActor p = s.World.Player;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.AimDir = new Vector2(1f, 0f);       // 钉住朝向 ⇒ 截图里的飞行方向可复现
            s.DebugAttackOnce(WeaponSlot.Hoe);
            float range = TableDatabase.Tuning.tools.hoe.range * s.Progress.Stats.Get(StatId.AtkRange);
            return $"WAVEFIRE 已劈出剑气｜剑气慢放×{WorldView.DebugWaveHold:0.##}"
                 + $"｜在飞={s.LiveSwordWaveCount}"
                 + $"｜飞行距离={RunSession.SwordWaveDistance(range):0.000}m"
                 + $"｜慢放后寿命≈{RunSession.SwordWaveLife(range) * WorldView.DebugWaveHold:0.00}s"
                 + $"｜前缘最远={RunSession.SwordWaveReach(range):0.000}m";
        }

        /// <summary>
        /// v185 抓图专用：**复现"改前"的剑气表现** —— 投一条旧版的 `BeamEvent`。
        ///
        /// 为什么不是"翻出上一版的截图"：`Docs/captures` 里没有留下剑气的图，
        /// 而"改前/改后"必须是**同一台机器、同一条相机、同一个插件**下的对比才有意义。
        /// 所以这里按旧版 `FireSwordWaves` 的**原参数**投一条：
        /// `From = 玩家`、`To = 玩家 + 朝向 × 攻击范围`、`HalfWidth = MeleeBoxHalfWidth(range, arc)`
        /// ⇒ `WorldView.OnBeam` 画出来的就是当时那根柱子（画法一个字都没改，它还在给水壶用）✓
        /// 唯一的差别：它是**瞬时**的（与旧版判定同步），所以只有一帧可见 ✓
        /// </summary>
        public string WaveLegacyShot(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话";
            float hold = ParseFloat(spec, 10f);
            // 旧版的柱子寿命是 `FxLife(0.12)` ⇒ 只有它需要慢放（表现层唯一的旋钮就是总旋钮）✓
            WorldView.DebugFxHold = Mathf.Max(0.02f, hold);
            PlayerActor p = s.World.Player;
            p.AimDir = new Vector2(1f, 0f);
            TuningDef t = TableDatabase.Tuning;
            float range = t.tools.hoe.range * s.Progress.Stats.Get(StatId.AtkRange);
            float halfW = RunSession.MeleeBoxHalfWidth(range, t.tools.hoe.arc);
            Bus.Publish(new BeamEvent
            {
                From = p.Pos,
                To = p.Pos + new Vector2(range, 0f),
                HalfWidth = halfW,
                Fx = PluginFx.For(s.ModsFor(WeaponSlot.Hoe), WeaponSlot.Hoe)
            });
            return $"WAVELEGACY 已投旧版瞬时剑气｜From=玩家 To=+{range:0.00}m"
                 + $"｜HalfWidth={halfW:0.000}m｜寿命=0.12s×{WorldView.DebugFxHold:0.##}";
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
        /// ★ v179：**把同一档的四株塞进种子袋** —— 只为验收"同档四株的卡能同框"★
        ///
        /// ★ 为什么需要它 ★
        /// 种子卡（`ImGuiHud.SeedCardPanel` → `PlantCellInfoLines`）**一次只画一个槽位** ✗
        /// 而 v179 要验的是"同档四株的金币 / 经验 / 成熟时间三个数**各不相同**"
        /// ⇒ 必须让四张卡同时出现在 `DrawPlantPanel` 的 2×3 格子里 ✓
        /// 开局只发 1 颗种子（选种三选一），商店买种要金币与运气 ⇒ 没有这条路就拍不到那张图 ✓
        ///
        /// ★ 为什么选这四株 ★
        /// `radish_guard` / `pea_shooter` / `toxic_mushroom` / `frost_ivy` = **D 档的全部四象限**
        /// （金型 6/1/16、验型 2/3/16、重型 8/4/32、快型 2/1/8）——
        /// 正是"同价值、不同节奏"那张表里的四行 ✓
        ///
        /// ★ 它只动种子袋，不动数值 ★
        /// `AddSeed` 是**商店买种走的同一个入口**（不是直接写槽位）⇒ 等级、成本与玩家买到的一致 ✓
        /// </summary>
        public string SeedCardsDemo()
        {
            RunSession s = S;
            if (s == null) return Fail("会话未启动");
            string[] ids = { "radish_guard", "pea_shooter", "toxic_mushroom", "frost_ivy" };
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                SeedDef def;
                try { def = TableDatabase.GetSeed(ids[i]); }
                catch (System.Exception) { sb.Append(ids[i] + "：无定义；"); continue; }
                if (!s.Bag.AddSeed(def, null, 0, out string why)) { sb.Append(def.Name + "：入袋失败（" + why + "）；"); continue; }
                sb.Append($"{def.Name} {SeedEconomy.CellGold(def)} 金／{SeedEconomy.CellExp(def)} 验／{def.GrowTime:0.#}s；");
            }
            s.SyncSeedSlots();
            // ★ 顺手关掉"选初始种子"模态，但**不开波** ★
            // `SeedChoiceOpen(s)` 判的是 `InitialSeed == null`，而 `TickPrepare` 只在**收到
            // `InitialChoice` 输入**时才 `StartWave(1)` ⇒ 直接写 `InitialSeed` 只会让模态消失，
            // 阶段仍是 Prepare ⇒ 主界面植物面板（2×3，只在 Prepare 且无模态时绘制）才画得出来 ✓
            // 不这么做的话，模态正好压在面板右列上（面板 x 10..390，模态 x 241..1181）✗
            if (s.InitialSeed == null && s.InitialChoices.Length > 0) s.InitialSeed = s.InitialChoices[0];
            string text = "同档四株已入袋：" + sb;
            Log.Info("SEEDCARDS " + text);
            return text;
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
                  // ★ v180：**不再逐字段打印 `WeaponModifiers`** ★
                  //   改前这里是 `m.DamageFlat … m.FullHpBonusPct` 的一长串**字段名**。
                  //   而另一条任务正在**重做插件模型** —— 这组字段整批换成了行为类字段
                  //   （`SwordWaveCount` / `BurnLayersPerHit` / `ExtraJets` / `SubBurstCount` …）✗
                  //   ⇒ 按名字打印会在我这个文件里留一串**随时会断的编译依赖** ✓
                  //   ⇒ 改成只报**与形状无关**的两件事：聚合来源件数（`SourceCount`，语义未变 ✓）
                  //      + 已装插件清单（id[通道 数值]，走 `ChannelOf` —— 唯一说法来源 ✓）
                  //   判据没减弱："装上以后聚合层有没有变"仍然看得见（清单 + 件数）✓
                  //   若将来要恢复逐字段读数，应按**当时**的 `WeaponModifiers` 重写，而不是恢复这几行 ✓
                  .Append("    聚合：来自 ").Append(m.SourceCount).Append(" 件 ⇒ ");
                int dumped = 0;
                foreach (PluginInstance pi in s.Goods.PluginsOn(slot))
                {
                    if (pi.Asset == null) continue;
                    if (dumped > 0) sb.Append('　');
                    sb.Append(pi.Asset.id).Append('[').Append(WeaponSystem.ChannelOf(pi.Asset.effect))
                      .Append(' ').Append(pi.Asset.amount.ToString("0.###"));
                    if (pi.Asset.amount2 > 0f) sb.Append('/').Append(pi.Asset.amount2.ToString("0.###"));
                    if (pi.Stacks > 1) sb.Append("×").Append(pi.Stacks);
                    sb.Append(']');
                    dumped++;
                }
                sb.Append('\n');
            }

            // ── ③ 全部效果 → "改了哪一环" 的对照（证明没有"写了没人用"的枚举值）
            // v180：`k == PluginEffectKind.None` 换成 `(int)k == 0` —— 另一条任务正在重做这张枚举，
            //       按名字写 `None` 会在改名那一轮**编译不过**；`None = 0` 是这张表的**契约**
            //       （"0 号不算一种效果"），比名字稳 ✓
            sb.Append("  效果对照：");
            foreach (PluginEffectKind k in Enum.GetValues(typeof(PluginEffectKind)))
            {
                if ((int)k == 0) continue;
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

        // ════════════════════════════════════════════════════════════════════
        // v176：**「每个插件都有特效」的自动判据**
        //
        // 规格原话：任何一个插件装上之后，那把武器的特效必须发生变化或被增加 ✓
        // 「变化」是视觉的事，但**判据不能靠看图** —— 图只能抽查，不能证明 30 件全中 ✗
        //
        // 所以判据落在这里：`PluginFx.Diff(空槽, 只装这一件)` 必须**非空**。
        // 而 `Diff` 比的是 `FxSpec` 的**全部可见通道**（本体色 / 霓虹边 / 尺寸 / 发光 /
        // 火花数 / 环数 / 拖尾 / 震屏）—— 这八个通道就是 `WorldView` 真正拿去画的全部输入 ✓
        // ⇒ 只要 Diff 非空，画面上就必然不同；Diff 为空 = 这个插件在画面上的存在感是零 ✗
        //
        // 另外附带两条更严的：
        //   ② 同一把武器上**不能有两件插件的签名相同** —— 那等于"装哪件都一样" ✗
        //   ③ `SourceCount` 必须 > 0 —— 否则"没装上"被误判成"装了但没变化" ✗
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 逐件插件审计"它到底改变了什么特效"。**这就是「每个插件都有特效」的自动化判据**。
        /// 走的是真路：`PlayerGoods.Acquire` → `WeaponSystem.Aggregate` → `PluginFx.For`
        /// —— 与玩家真的把它装上去时**同一条**链路 ✓
        /// </summary>
        public string PluginFxAudit()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.Append("PLUGINFX 插件×特效审计（判据：Diff(空槽, 只装这一件) 必须非空）\n");

            int total = 0, ok = 0, dup = 0, noSource = 0;
            var s = new RunSession();
            s.Start(20240607UL);

            for (int w = 0; w <= 2; w++)
            {
                WeaponSlot slot = (WeaponSlot)w;
                FxSpec baseSpec = PluginFx.Base(slot);
                // 签名 → 已经用过它的插件 id。同一把武器上重复就是"装哪件都一样" ✗
                var seen = new System.Collections.Generic.Dictionary<uint, string>();

                sb.Append("── ").Append(RunSession.WeaponSlotName(slot)).Append("｜空槽基线 ")
                  .Append(PluginFx.Hex(baseSpec.Core)).Append('/').Append(PluginFx.Hex(baseSpec.Rim))
                  .Append(" 尺寸×").Append(baseSpec.Scale.ToString("0.00"))
                  .Append(" 发光×").Append(baseSpec.Glow.ToString("0.00"))
                  .Append("（火花0 环0 拖尾0 震屏×1.00）\n");

                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                {
                    PluginAsset a = TableDatabase.Plugins[i];
                    if (a.allowedWeapons == null || a.allowedWeapons.Length != 1
                        || a.allowedWeapons[0] != slot) continue;
                    total++;

                    // 只装这一件：不 Clear 的话前面装的会叠加，测的就不是"这一件的贡献"
                    s.Goods.Plugins.Clear();
                    bool acquired = s.Goods.Acquire(a, slot);
                    WeaponModifiers m = WeaponSystem.Aggregate(s.Goods, slot);
                    FxSpec fx = PluginFx.For(m, slot);
                    string diff = PluginFx.Diff(baseSpec, fx);

                    bool pass = acquired && m.SourceCount > 0 && diff.Length > 0;
                    if (!acquired || m.SourceCount <= 0) noSource++;
                    if (pass) ok++;

                    string dupWith = "";
                    if (seen.TryGetValue(fx.Signature, out string other))
                    {
                        dup++;
                        dupWith = "  ⚠与 " + other + " 画出同一个样子";
                        pass = false;
                    }
                    else seen[fx.Signature] = a.id;

                    sb.Append(pass ? "  ✓ " : "  ✗ ")
                      .Append(a.id.PadRight(17))
                      .Append(WeaponSystem.ChannelOf(a.effect).PadRight(11))
                      .Append("色 ").Append(PluginFx.Hex(fx.Core)).Append('/').Append(PluginFx.Hex(fx.Rim))
                      .Append(" 尺寸×").Append(fx.Scale.ToString("0.00"))
                      .Append(" 发光×").Append(fx.Glow.ToString("0.00"))
                      .Append(" 火花").Append(fx.Sparks)
                      .Append(" 环").Append(fx.Rings)
                      .Append(" 拖尾").Append(fx.Trail.ToString("0.00"))
                      .Append(" 震屏×").Append(fx.Shake.ToString("0.00"))
                      .Append("｜变的是：").Append(diff.Length == 0 ? "(无！)" : diff)
                      .Append(dupWith)
                      .Append('\n');
                }
            }

            sb.Append("── 结论：").Append(ok).Append('/').Append(total)
              .Append(" 件插件都让特效发生了变化");
            if (noSource > 0) sb.Append("；⚠ ").Append(noSource).Append(" 件压根没装上（SourceCount=0）");
            if (dup > 0) sb.Append("；⚠ ").Append(dup).Append(" 件与同武器其它插件画出同一个样子");
            sb.Append(ok == total && noSource == 0 && dup == 0 ? " ✓ 全部通过" : " ✗ 有未通过项");
            sb.Append('\n');

            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// 把某件插件装到**当前手持的那把武器**上（空串 / "none" = 全部卸下），
        /// 并把该武器的特效参数与"相对空槽变了什么"打出来。
        ///
        /// 存在的理由：抓对比图需要一个**可脚本化的装插件入口** ——
        /// 商店买插件要随机刷新，没法在自动化里稳定复现"空槽 / A / B"三张图 ✗
        /// 而它是纯调试入口：只动 `Goods.Plugins`，不走购买、不花金币 ✓
        ///
        /// spec 也可以写成 "插件id@0|1|2" 指定武器，例如 "hoe_frostedge@0"。
        /// </summary>
        public string PluginFxApply(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话";
            TableDatabase.Load(true);

            string raw = (spec ?? "").Trim();
            string id = raw;
            int at = raw.IndexOf('@');
            int forceSlot = -1;
            if (at >= 0)
            {
                id = raw.Substring(0, at).Trim();
                forceSlot = ParseInt(raw.Substring(at + 1), -1);
            }

            WeaponSlot slot = forceSlot >= 0 ? (WeaponSlot)forceSlot
                                            : (WeaponSlot)s.World.Player.WeaponIndex;

            s.Goods.Plugins.Clear();
            FxSpec baseSpec = PluginFx.Base(slot);
            if (id.Length == 0 || id == "none" || id == "clear")
            {
                Log.Info("PLUGINFX 已卸下全部插件｜" + RunSession.WeaponSlotName(slot) + " 空槽 "
                         + PluginFx.Describe(baseSpec));
                return "空槽 " + PluginFx.Describe(baseSpec);
            }

            PluginAsset a = null;
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                if (TableDatabase.Plugins[i].id == id) { a = TableDatabase.Plugins[i]; break; }
            if (a == null) return "找不到插件 " + id;

            if (!s.Goods.Acquire(a, slot))
                return id + " 装不上 " + RunSession.WeaponSlotName(slot)
                     + "（allowedWeapons=" + (a.allowedWeapons == null ? "null" : string.Join(",", a.allowedWeapons)) + "）";

            WeaponModifiers m = WeaponSystem.Aggregate(s.Goods, slot);
            FxSpec fx = PluginFx.For(m, slot);
            string diff = PluginFx.Diff(baseSpec, fx);
            string line = "PLUGINFX " + id + " → " + RunSession.WeaponSlotName(slot)
                        + "｜" + PluginFx.Describe(fx)
                        + "｜变的是：" + (diff.Length == 0 ? "(无！)" : diff);
            Log.Info(line);
            return line;
        }

        /// <summary>
        /// v176：设置**特效慢放倍率**（只给验收抓图用）。
        ///
        /// 为什么必须有：水壶激光存活 0.12 秒、挥击弧 0.20 秒，
        /// 而桥接往返一次就 1 秒以上 —— 等命令回到手里，特效早就销毁了 ✗
        /// 那不是"效果没问题"，而是**没法验证**。所以留一个可控的慢放通道 ✓
        ///
        /// spec = "6"（两者都 6 倍）或 "fx:8,swing:4" 或 "normal"（恢复正常）。
        /// </summary>
        public string FxHold(string spec)
        {
            float fx = WorldView.DebugFxHold, sw = WorldView.DebugSwingHold;
            float wave = WorldView.DebugWaveHold;
            string raw = (spec ?? "").Trim().ToLowerInvariant();
            if (raw.Length == 0 || raw == "normal" || raw == "reset") { fx = 1f; sw = 0f; wave = 1f; }
            else
            {
                string[] parts = raw.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i].Trim();
                    if (part.Length == 0) continue;
                    int c = part.IndexOf(':');
                    if (c < 0) { float all = ParseFloat(part, 1f); fx = all; sw = all; wave = all; continue; }
                    string k = part.Substring(0, c).Trim();
                    float v = ParseFloat(part.Substring(c + 1), 1f);
                    if (k.StartsWith("wa")) wave = v;       // v185：剑气专属（必须先判，否则被 "w" 吃掉）
                    else if (k.StartsWith("fx")) fx = v;
                    else if (k.StartsWith("sw")) sw = v;
                }
            }
            WorldView.DebugFxHold = Mathf.Max(0.02f, fx);
            WorldView.DebugSwingHold = sw <= 0.01f ? 0f : sw;
            WorldView.DebugWaveHold = Mathf.Max(1f, wave);
            string line = "FXHOLD fx×" + WorldView.DebugFxHold.ToString("0.##")
                        + " swing=" + (WorldView.DebugSwingHold <= 0.01f
                            ? "正常" : WorldView.DebugSwingHold.ToString("0.##") + "s")
                        + " wave×" + WorldView.DebugWaveHold.ToString("0.##");
            Log.Info(line);
            return line;
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

        /// <summary>
        /// v183：**BOSS 血条只读探针** —— 一次调用回答"血条现在在哪、多大、朝向对不对、比例对不对"。
        ///
        /// ★ 为什么它不读 `WorldView` 的私有字段 ★
        ///   表现层的东西一律从**场景**里量（`SeedfallWorld/BOSS_BAR` 这一支），
        ///   这样探针验的是"玩家真正看到的那一份"，而不是"我以为写进去的那一份" ✓
        ///   逻辑侧的血量从 `S.World.Enemies` 按**与血条相同的判据**（`Kind == Boss &amp;&amp; Alive`）取 ✓
        ///
        /// 读到的每个数都有验收用途：
        ///   · `active`        —— BOSS 在不在（`false` 就是"不显示"）
        ///   · `pos` / 头顶高  —— 是否悬在 BOSS 上方、是否每帧跟随
        ///   · `dot(forward,cam)` —— billboard：= 1 表示牌面正对相机
        ///   · `左锚点 / 宽 / 实测比例` —— 填充条是否**从左往右**缩、比例是否 = hp/maxHp
        ///   · `血量原文`      —— 条上的文字到底写了什么
        ///   · `建/帧/写字`    —— 复用证据（物件只建一次、稳态不重建字符串）
        /// </summary>
        public string BossBarProbe()
        {
            RunSession s = S;
            if (s == null || s.World == null) return "BOSSBAR 无会话";

            EnemyActor boss = null;
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e == null || !e.Alive || e.Def == null) continue;
                if (e.Def.Kind != EnemyKind.Boss) continue;
                boss = e;
                break;
            }

            var sb = new StringBuilder("BOSSBAR ");
            if (boss == null) sb.Append("BOSS=无");
            else
                sb.Append("BOSS=").Append(boss.Def.Id).Append('#').Append(boss.Id)
                  .Append(" HP=").Append(boss.Health.Hp.ToString("0.0")).Append('/').Append(boss.Health.MaxHp.ToString("0.0"))
                  .Append(" 显示=").Append(Mathf.CeilToInt(boss.Health.Hp)).Append('/').Append(Mathf.CeilToInt(boss.Health.MaxHp))
                  .Append(" 比例=").Append(boss.Health.HpPct.ToString("0.000"))
                  .Append(" bossPos=(").Append(boss.Pos.x.ToString("0.00")).Append(',').Append(boss.Pos.y.ToString("0.00")).Append(')');

            Transform bar = null;
            GameObject world = GameObject.Find("SeedfallWorld");
            if (world != null)
            {
                for (int i = 0; i < world.transform.childCount; i++)
                {
                    Transform c = world.transform.GetChild(i);
                    if (c != null && c.name == "BOSS_BAR") { bar = c; break; }
                }
            }
            if (bar == null)
            {
                sb.Append(" ｜ 血条=未创建 ｜ 计数 建=").Append(WorldView.BossBarBuilds)
                  .Append(" 帧=").Append(WorldView.BossBarSyncFrames)
                  .Append(" 写字=").Append(WorldView.BossBarHpTextWrites);
                return sb.ToString();
            }

            sb.Append(" ｜ 血条 active=").Append(bar.gameObject.activeSelf)
              .Append(" 子物件=").Append(bar.childCount)
              .Append(" pos=(").Append(bar.position.x.ToString("0.00")).Append(',')
              .Append(bar.position.y.ToString("0.00")).Append(',')
              .Append(bar.position.z.ToString("0.00")).Append(')');

            Camera cam = Camera.main;
            if (cam != null)
                sb.Append(" ｜ dot(牌面forward,相机forward)=")
                  .Append(Vector3.Dot(bar.forward, cam.transform.forward).ToString("0.0000"));

            Transform backT = bar.Find("BossBarBack");
            Transform fillT = bar.Find("BossBarFill");
            if (backT != null)
                sb.Append(" ｜ 底条=").Append(backT.localScale.x.ToString("0.00")).Append('×')
                  .Append(backT.localScale.y.ToString("0.00")).Append("m z=").Append(backT.localPosition.z.ToString("0.00"));
            if (fillT != null && backT != null && backT.localScale.x > 0.0001f)
                sb.Append(" ｜ 填充 左锚点x=").Append(fillT.localPosition.x.ToString("0.00"))
                  .Append(" 宽=").Append(fillT.localScale.x.ToString("0.000")).Append('m')
                  .Append(" 实测比例=").Append((fillT.localScale.x / backT.localScale.x).ToString("0.000"));

            Transform hpT = bar.Find("BossBarHp");
            Transform nameT = bar.Find("BossBarName");
            if (hpT != null)
            {
                TextMesh tm = hpT.GetComponent<TextMesh>();
                sb.Append(" ｜ 血量文字=\"").Append(tm != null ? tm.text : "?").Append('"')
                  .Append(" 世界高=").Append(TextWorldHeight(tm).ToString("0.00")).Append('m');
            }
            if (nameT != null)
            {
                TextMesh tm = nameT.GetComponent<TextMesh>();
                sb.Append(" ｜ 名字=\"").Append(tm != null ? tm.text : "?").Append('"')
                  .Append(" 世界高=").Append(TextWorldHeight(tm).ToString("0.00")).Append('m');
            }

            // 头顶判定：血条底边高度 − BOSS 实测模型顶高（≥0 才算"在头顶上方"）
            if (boss != null)
            {
                sb.Append(" ｜ 跟随误差=")
                  .Append(new Vector2(bar.position.x - boss.Pos.x, bar.position.z - boss.Pos.y).magnitude.ToString("0.000"))
                  .Append("m");
            }

            sb.Append(" ｜ 计数 建=").Append(WorldView.BossBarBuilds)
              .Append(" 帧=").Append(WorldView.BossBarSyncFrames)
              .Append(" 写字=").Append(WorldView.BossBarHpTextWrites)
              .Append(" ｜ 材质份数=").Append(DistinctMaterialCount(bar))
              .Append(" ｜ 条材质=").Append(MaterialInfo(backT))
              .Append(" ｜ 填充材质=").Append(MaterialInfo(fillT));
            return sb.ToString();
        }

        private static float TextWorldHeight(TextMesh tm)
        {
            if (tm == null) return 0f;
            // `TextMesh` **没有 MeshFilter**（见 `WorldView.MakeBossBarText` 的说明）⇒ 只能问渲染器；
            // 而它的 `bounds` 在本版本里恒为 0（实测）⇒ 字高改由 `characterSize` 的标定式给出 ✓
            return tm.characterSize * tm.fontSize / 8.93f;
        }

        /// <summary>一份材质的三件事：哪个着色器、排在哪条队列、主色 alpha（判"是不是真的用上了 Vfx 置顶材质"）。</summary>
        private static string MaterialInfo(Transform t)
        {
            if (t == null) return "无";
            Renderer r = t.GetComponent<Renderer>();
            Material m = r != null ? r.sharedMaterial : null;
            if (m == null) return "无材质";
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            return (m.shader != null ? m.shader.name : "?") + "/q" + m.renderQueue
                   + "/a" + c.a.ToString("0.00");
        }

        /// <summary>血条这一支用了**几份**材质（复用证据：底/填充各一份，文字各自共享字体材质）。</summary>
        private static int DistinctMaterialCount(Transform root)
        {
            var seen = new System.Collections.Generic.List<Material>(4);
            Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || rs[i].sharedMaterial == null) continue;
                if (!seen.Contains(rs[i].sharedMaterial)) seen.Add(rs[i].sharedMaterial);
            }
            return seen.Count;
        }

        /// <summary>
        /// v183：**让场上那只 BOSS 掉血**（唯一一处非只读的探针，只为"受伤后的血条"这张验收图）。
        ///
        /// ★ 为什么必须加这一条 ★
        ///   本工程**没有自动攻击**（`RunSession.cs:3715`「只保留手动攻击——不按左键就不攻击」），
        ///   而攻击方向 `AimDir` 只由鼠标位置算（`RunSession.cs:3723`）⇒
        ///   无人值守的验证里**没有办法**把玩家转向 BOSS 再挥一刀 ✗
        ///   （实测：`AttackProbe hoe / fert / can` 连打五下，BOSS 一点血都没掉 —— 它不在扇形里）
        ///
        /// ★ 为什么这是可以接受的 ★
        ///   既有的 `HurtPlayer(amount)`（本文件上方）就是**同一类验收钩子**：
        ///   直接 `Health.ApplyDamage`，只改"当前这一局里那个角色"的血量 ✓
        ///   这里逐字照抄它的形状：只改**当前场上那只 BOSS**，不碰规则 / 数据表 / 存档 / 波次 ✓
        ///   而且走的仍是 `HealthPool.ApplyDamage`（真实受击的同一入口）⇒ 血条读到的是真数 ✓
        /// </summary>
        public string BossHurt(string amount)
        {
            RunSession s = S;
            if (s == null || s.World == null) return "无会话";

            EnemyActor boss = null;
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e == null || !e.Alive || e.Def == null) continue;
                if (e.Def.Kind != EnemyKind.Boss) continue;
                boss = e;
                break;
            }
            if (boss == null) return "场上没有 BOSS";

            float dmg = ParseFloat(amount, 100f);
            float before = boss.Health.Hp;
            float absorbed = boss.Health.ApplyDamage(dmg);
            string text = $"BOSSHURT {boss.Def.Id}#{boss.Id} 请求 {dmg:0.0} 实际掉 {absorbed:0.0}"
                        + $"｜{before:0.0} → {boss.Health.Hp:0.0} / {boss.Health.MaxHp:0.0}"
                        + $"（{boss.Health.HpPct:P1}）";
            Log.Info(text);
            return text;
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

        // ════════════════════════════════════════════════════════════════════════
        //  v181：战斗四项的**只读探针**（插件定价 / 暴击率 / 肥料充能 / 剑气）
        //
        //  ★ 为什么不复用 `SelfCheck` 里的夹具 ★
        //  自检的夹具为了"让别的读数稳定"，会**把暴击率清成 0**（`PluginBench`）、
        //  把目标摆在固定距离。拿它来量"暴击率到底是多少"等于先把被测对象清零 ✗
        //  所以这里每个探针都**自己建一局、自己摆敌人、只读数字**，
        //  不碰正在运行的那一局，也不写任何存档 / 数据表 ✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>档位字母 —— 与 `GoodsQuality` 的索引 0..4 对齐（D/C/B/A/S）。</summary>
        private static readonly string[] TierLetters = { "D", "C", "B", "A", "S" };

        /// <summary>
        /// ① **插件定价表**：12 件 × 第 1/3/5 波的实付价（只读）。
        /// 走的是项目里唯一的定价入口 `RunSession.ShopPriceFrom`，不另写一份算式 ✓
        /// </summary>
        public string ProbePrices()
        {
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            sb.Append("PROBE_PRICES 定价链 = basePrice × 档位倍率 × (1+")
              .Append(t.shop.priceWaveStep.ToString("0.###")).Append(")^(波-1)\n档位倍率：");
            for (int q = 0; q <= 4; q++)
            {
                sb.Append(TierLetters[q]).Append('（').Append(t.quality.NameOf(q)).Append("）×")
                  .Append(t.quality.PriceMult(q).ToString("0.##")).Append("  ");
            }
            sb.Append("\n").Append("id".PadRight(18)).Append("档   ").Append("base".PadLeft(6))
              .Append("W1".PadLeft(7)).Append("W3".PadLeft(7)).Append("W5".PadLeft(7)).Append('\n');
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset p = TableDatabase.Plugins[i];
                if (p == null) continue;
                int q = t.quality.Clamp((int)p.quality);
                sb.Append(p.id.PadRight(18))
                  .Append((TierLetters[q] + "  ").PadRight(5))
                  .Append(p.basePrice.ToString().PadLeft(6))
                  .Append(SeedSlot.ShopPriceFrom(p.basePrice, q, 1).ToString().PadLeft(7))
                  .Append(SeedSlot.ShopPriceFrom(p.basePrice, q, 3).ToString().PadLeft(7))
                  .Append(SeedSlot.ShopPriceFrom(p.basePrice, q, 5).ToString().PadLeft(7))
                  .Append('\n');
            }
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ② **暴击率实测**：打 N 刀，数 `PopupKind.Crit` 出现了几次（只读）。
        ///
        /// ★ 为什么数弹字而不是数伤害 ★
        /// 伤害是 `(基准+属性)×倍率×暴击` 的连乘，一次读数分不清"暴击没触发"还是
        /// "属性没读到" ✗ 而 `PopupKind.Crit` **只在 `crit == true` 时**由
        /// `Actor.OnHitLanded` 发出（见 `Actors.cs`）⇒ 它是暴击的**直接证据** ✓
        /// </summary>
        public string ProbeCrit(string spec)
        {
            int n = Mathf.Clamp(ParseInt(spec, 1000), 10, 20000);
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            // 挥击框内放一只打不死的敌人：每刀必然结算一次 ⇒ 每刀一个弹字
            s.World.SpawnEnemy("grunt", new Vector2(1.2f, 0f), 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = new Vector2(1.2f, 0f);
            e.Health.Setup(1e9f, 0f);

            int crit = 0, popups = 0;
            System.Action<FloatingTextEvent> onPopup = ev =>
            {
                if (ev.Kind != PopupKind.Crit && ev.Kind != PopupKind.DamageToEnemy) return;
                popups++;
                if (ev.Kind == PopupKind.Crit) crit++;
            };
            float statRate = s.Progress.Stats.Get(StatId.CritRate);
            Bus.Subscribe(onPopup);
            try
            {
                for (int i = 0; i < n; i++) s.DebugAttackOnce(WeaponSlot.Hoe);
            }
            finally { Bus.Unsubscribe(onPopup); }

            float measured = popups > 0 ? (float)crit / popups : 0f;
            string text = $"PROBE_CRIT 挥击 {n} 次｜弹字 {popups}｜暴击 {crit}"
                        + $"｜实测 {measured:P2}｜StatBlock.CritRate = {statRate:0.0000}"
                        + $"｜每弹字期望 {1f / Mathf.Max(1e-6f, statRate):0.0} 次";
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ③ **肥料充能**：N 次投掷里「充能掉了但一点伤害都没有」的次数（只读）。
        ///
        /// 三种摆法各测一遍，因为它们对应三种**不同的**失败机制：
        ///   `core` —— 敌人正好站在爆心（对照组，任何版本都该命中）；
        ///   `edge` —— 敌人**身体压在爆环上、圆心在外 0.2m**（"看着炸到了却没伤害"）；
        ///   `wave` —— 投出去之后立刻跨分钟（前摇队列被清 ⇒ 充能白扣）。
        /// </summary>
        public string ProbeFert(string spec)
        {
            string[] parts = (spec ?? "core:20").Split(':');
            string mode = parts[0].Trim().ToLowerInvariant();
            int n = Mathf.Clamp(parts.Length > 1 ? ParseInt(parts[1], 20) : 20, 1, 200);

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);

            TuningDef t = TableDatabase.Tuning;
            float radius = RunSession.BlastBaseRadius(t.tools.fertilizer.baseExplodeRange)
                           * s.Progress.Stats.Get(StatId.ExplodeRange);

            // 落点固定 (3,0)：`CurrentBlastCenter` = 射程钳制后的鼠标点
            Vector2 center = new Vector2(3f, 0f);
            s.AimWorldLast = center;

            int bad = 0, thrown = 0, lost = 0;
            float enemyRadius = 0f;
            for (int i = 0; i < n; i++)
            {
                s.World.Enemies.Clear();
                Vector2 at;
                if (mode == "edge") at = center + new Vector2(radius + 0.2f, 0f);
                else at = center;
                s.World.SpawnEnemy("grunt", at, 1);
                EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
                e.Pos = at;
                e.Health.Setup(1e5f, 0f);
                enemyRadius = e.Radius;

                s.FertAmmo = RunSession.FertAmmoMax;
                s.AimWorldLast = center;
                int ammo0 = s.FertAmmo;
                float hp0 = e.Health.Hp;

                s.DebugAttackOnce(WeaponSlot.Fertilizer);
                if (mode == "wave")
                {
                    // 前摇还没走完就跨分钟：这一投的爆炸被 `ClearTransientCombatState` 丢掉
                    s.StartWave(2);
                }

                int ammo1 = s.FertAmmo;
                float dealt = hp0 - e.Health.Hp;
                thrown++;
                bool dropped = ammo1 < ammo0;
                if (dropped) lost += ammo0 - ammo1;
                if (dropped && dealt <= 0f) bad++;
            }

            string text = $"PROBE_FERT mode={mode}｜投掷 {thrown}｜爆炸半径 {radius:0.00}m"
                        + $"｜敌人半径 {enemyRadius:0.00}m（判定用 center±radius+敌人半径）"
                        + $"｜充电合计 −{lost}｜★充能掉了但无伤害 = {bad}/{thrown}★";
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ③b **逐帧**看一发肥料：充能在哪一帧掉、伤害在哪一帧结算（只读）。
        /// 这是"充能掉了但没伤害"这件事的**直接证据** —— 两件事发生在**不同帧**上：
        /// 充能在投掷帧就扣掉，伤害要等 0.5 秒前摇走完、爆炸帧才结算 ✓
        /// </summary>
        public string ProbeFertFrames(string spec)
        {
            bool edge = !string.Equals((spec ?? "edge").Trim(), "core", StringComparison.OrdinalIgnoreCase);

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);

            TuningDef t = TableDatabase.Tuning;
            float radius = RunSession.BlastBaseRadius(t.tools.fertilizer.baseExplodeRange)
                           * s.Progress.Stats.Get(StatId.ExplodeRange);
            Vector2 center = new Vector2(3f, 0f);
            Vector2 at = edge ? center + new Vector2(radius + 0.2f, 0f) : center;
            s.World.SpawnEnemy("grunt", at, 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = at;
            e.Health.Setup(1e5f, 0f);

            s.FertAmmo = RunSession.FertAmmoMax;
            s.AimWorldLast = center;

            var sb = new StringBuilder();
            sb.Append("PROBE_FERT_FRAMES 摆法=").Append(edge ? "贴边（圆心在爆环外 0.2m）" : "正中")
              .Append($"｜爆半径 {radius:0.00}m｜敌人半径 {e.Radius:0.00}m")
              .Append($"｜圆心距 {Vector2.Distance(e.Pos, center):0.00}m\n")
              .Append("帧   t(s)   充能 待爆  累计伤害  事件\n");

            int ammoPrev = s.FertAmmo;
            float hpPrev = e.Health.Hp, cum = 0f;
            s.DebugThrowOnly();          // ★ 只投掷，不炸
            float tt = 0f;
            for (int k = 0; k <= 32; k++)
            {
                float dealt = hpPrev - e.Health.Hp;
                cum += dealt;
                hpPrev = e.Health.Hp;
                string note = "";
                if (s.FertAmmo < ammoPrev) note += "← 充能 −1（投掷帧） ";
                if (dealt > 0f) note += "★伤害结算 ";
                if (note.Length == 0 && s.PendingBlastCount > 0) note = "前摇中";
                sb.Append($"{k,3}  {tt,5:0.00}  {s.FertAmmo,4}  {s.PendingBlastCount,4}  {cum,8:0.##}  {note}\n");
                ammoPrev = s.FertAmmo;
                s.DebugAdvanceAttack(1f / 60f);
                tt += 1f / 60f;
            }
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ④ **锄头剑气**：把敌人摆在正前方 8 处 + 侧偏 2 处，装 0/1/3 层剑气后的
        /// **单次挥击伤害**（只读）。
        ///
        /// ★ v185：剑气变成**飞行子弹**之后，这一组位置的"意义"全变了 ★
        /// 改前（瞬时矩形）：`along ∈ (0, 3.80]`，超过 3.80m 一律 0。
        /// 改后（飞行 + 同尺寸框）：框心从 1.90m 飞到 6.65m ⇒ 前缘最远 **8.55m**
        /// ⇒ 4.0 / 5.0 / 6.0 / 8.0 这几处从"0"变成"吃得到"，
        ///   而 9.5（= 2d）在最远之外仍是 0 —— **两头都钉住**才叫证明射程变了 ✓
        ///
        /// ★ 为什么要测侧偏 ★
        /// 正前方的敌人，任何宽度的判定都打得中 ⇒ 只测正前方**证不出**框宽对不对 ✗
        /// `1.5 前/2.1 侧` 落在"扇形之外（54.5° &gt; 50°）、框之内（2.1 &lt; 2.91+0.45）"
        /// ⇒ 这一处有伤害就证明"框宽 = 近战框宽"这条还成立 ✓
        /// `1.5 前/3.6 侧` 超过 `2.91+0.45=3.36` ⇒ 必须 0（框宽没有被放大）✓
        /// </summary>
        public string ProbeSwordWave(string spec)
        {
            int stacks = Mathf.Clamp(ParseInt(spec, 1), 0, 3);
            TuningDef t = TableDatabase.Tuning;
            float meleeRange = t.tools.hoe.range;
            float meleeHalfW = RunSession.MeleeBoxHalfWidth(meleeRange, t.tools.hoe.arc);
            float waveDist = RunSession.SwordWaveDistance(meleeRange);
            float waveReach = RunSession.SwordWaveReach(meleeRange);

            // 9 个位置各自钉住一条性质，缺一个就说不清"到底是哪一段变了"：
            var spots = new (string name, Vector2 pos, float along, float perp)[]
            {
                ("正前 2.0m",        new Vector2(2.0f, 0f), 2.0f, 0f),   // 近战扇形内 ⇒ 剑气被 ④ 去重
                ("正前 2.6m",        new Vector2(2.6f, 0f), 2.6f, 0f),   // 自检夹具位置
                ("正前 3.8m(近战边)", new Vector2(3.8f, 0f), 3.8f, 0f),   // 近战射程边界
                ("正前 4.0m",        new Vector2(4.0f, 0f), 4.0f, 0f),   // 改前 0 → 改后 吃得到
                ("正前 5.0m",        new Vector2(5.0f, 0f), 5.0f, 0f),   // 改前 0 → 改后 吃得到
                ("正前 6.0m",        new Vector2(6.0f, 0f), 6.0f, 0f),   // 改前 0 → 改后 吃得到
                ("正前 8.0m",        new Vector2(8.0f, 0f), 8.0f, 0f),   // 框前缘 8.55m 之内 ⇒ 吃得到
                ("正前 9.5m(外)",    new Vector2(9.5f, 0f), 9.5f, 0f),   // 超过 8.55+0.45 ⇒ 必须 0
                ("1.5 前/2.1 侧",    new Vector2(1.5f, 2.1f), 1.5f, 2.1f), // 出扇形、框内 ⇒ 剑气纯增量
                ("1.5 前/3.6 侧",    new Vector2(1.5f, 3.6f), 1.5f, 3.6f)  // 超半宽+半径 ⇒ 必须 0
            };

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);   // 关掉暴击 ⇒ 读数可复现

            if (stacks > 0)
            {
                PluginAsset sw = null;
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                    if (TableDatabase.Plugins[i] != null && TableDatabase.Plugins[i].id == "hoe_swordwave")
                        sw = TableDatabase.Plugins[i];
                if (sw == null) { Log.Warn("PROBE_SWAVE 表里没有 hoe_swordwave"); return "PROBE_SWAVE 无 hoe_swordwave"; }
                for (int k = 0; k < stacks; k++)
                    if (!WeaponSystem.Install(s.Goods, sw, WeaponSlot.Hoe, out string why))
                    { Log.Warn($"PROBE_SWAVE 装第 {k + 1} 层失败：{why}"); break; }
            }

            var enemies = new EnemyActor[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                s.World.SpawnEnemy("grunt", spots[i].pos, 1);
                enemies[i] = s.World.Enemies[s.World.Enemies.Count - 1];
                enemies[i].Pos = spots[i].pos;
                enemies[i].Health.Setup(1e5f, 0f);
            }
            float enemyR = enemies[0].Radius;

            s.DebugAttackOnce(WeaponSlot.Hoe);
            s.DebugAdvanceAttack(0.5f);      // 把在飞的剑气 / 第二刀 / 后摇都推完

            WeaponModifiers pm = s.ModsFor(WeaponSlot.Hoe);
            var sb = new StringBuilder();
            sb.Append($"PROBE_SWAVE 剑气={stacks} 层｜SwordWaveCount={pm.SwordWaveCount}"
                      + $"｜近战 range={meleeRange:0.00}m arc={t.tools.hoe.arc:0.##}°"
                      + $" ⇒ 扇形外接矩形半宽={meleeHalfW:0.000}m（+敌半径 {enemyR:0.00}）\n");
            sb.Append($"  剑气=子弹：判定框 {2f * RunSession.SwordWaveHalfLen(meleeRange):0.00}m 长 × "
                      + $"{2f * meleeHalfW:0.00}m 宽（= 近战框）；飞行距离={waveDist:0.000}m"
                      + $"（= 攻击范围 × {RunSession.SwordWaveDistMult:0.##}）；速度={RunSession.SwordWaveSpeed:0.#}m/s"
                      + $" ⇒ 寿命={RunSession.SwordWaveLife(meleeRange):0.000}s"
                      + $"；前缘最远={waveReach:0.00}m（+敌半径 {waveReach + enemyR:0.00}m）\n");
            sb.Append($"  飞行结束：场上在飞剑气 = {s.LiveSwordWaveCount}\n");
            for (int i = 0; i < spots.Length; i++)
            {
                float dealt = 1e5f - enemies[i].Health.Hp;
                sb.Append("  ").Append(spots[i].name.PadRight(16))
                  .Append($"沿 {spots[i].along:0.0}m 侧 {spots[i].perp:0.0}m ⇒ 伤害 {dealt:0.####}").Append('\n');
            }
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ★ v186：**长跑读数**（无头、确定性、跟渲染无关）★
        ///
        /// 需求要"跑到 20 分钟 Exception = 0"。**实时帧循环做不到可复现**：
        ///   ① 一局要真等 20 分钟；
        ///   ② 每次脚本重编 / 别人按 stop，play 就被打断（本轮实测被打断 5 次）；
        ///   ③ 帧率抖动会改变 dt ⇒ 同一次实验不可重放 ✗
        /// 而 `RunSimulator.Run(..., keepAlive: true)` 走的是**同一条逻辑主干**
        /// （`session.FixedTick(Dt, bot.Tick(Dt))`，固定 dt = 1/60 ⇒ 与实时帧完全同一条路），
        /// 只是不吃渲染 ⇒ **25 分钟的玩法时间几秒钟就跑完，且逐位可复现** ✓
        ///
        /// `keepAlive` = 每步把玩家补满血：本读数量的是"**逻辑跑 20+ 分钟会不会抛异常**"，
        /// 不是"这局能不能赢" ⇒ 玩家不该因为平衡数值先死掉而让读数提前结束 ✓
        ///
        /// 另外每分钟记一条 `敌 N`（在 `FixedTick` **之前**采样）⇒
        /// 这就是"跨分钟边界有没有**清场**"的直接证据（用户的症状正是"某几分钟敌人整批消失"）✓
        /// </summary>
        public string ProbeLongRun(string spec)
        {
            string[] parts = (spec ?? "").Split(':');
            int minutes = Mathf.Clamp(parts.Length > 0 && parts[0].Trim().Length > 0 ? ParseInt(parts[0], 20) : 20, 1, 60);
            BotStrategy strategy = parts.Length > 1 && Enum.TryParse(parts[1].Trim(), true, out BotStrategy st)
                ? st : BotStrategy.Balanced;
            ulong seed = (ulong)(parts.Length > 2 ? ParseInt(parts[2], 20240607) : 20240607);
            float seconds = minutes * 60f;

            var minuteRows = new System.Collections.Generic.List<string>();
            var clearRows = new System.Collections.Generic.List<string>();
            int lastMinute = -1;
            int lastDespawnCalls = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string text;
            try
            {
                SimResult r = RunSimulator.Run(strategy, seed, seconds, true, (t, sess, bot) =>
                {
                    // ★ 每一次"整场清除"都要留下当时的上下文 ★
                    //   两条路都在 `TickCombat` 里：① 分钟硬上限 ② 分钟走完清残敌
                    //   ⇒ 记 `CombatElapsed / minuteLen / IsBossWave / WaveIndex vs Clock` 就能区分是哪条 ✓
                    if (sess.World.DespawnAllCalls != lastDespawnCalls)
                    {
                        lastDespawnCalls = sess.World.DespawnAllCalls;
                        clearRows.Add($"清除#{lastDespawnCalls} @{t:0.0}s（第 {(int)(t / 60f) + 1} 分钟）"
                                    + $" CombatElapsed={sess.Waves.CombatElapsed:0.00}s"
                                    + $" minuteLen={Clock.SecondsPerMinute:0.#}s"
                                    + $" IsBoss={sess.Waves.IsBossWave}"
                                    + $" WaveIndex={sess.WaveIndex}/Clock={sess.Clock.WaveIndex}"
                                    + $" 阶段={sess.Phase}");
                    }
                    int m = (int)(t / 60f) + 1;
                    if (m == lastMinute) return;
                    lastMinute = m;
                    int boss = 0;
                    for (int i = 0; i < sess.World.Enemies.Count; i++)
                    {
                        EnemyActor e = sess.World.Enemies[i];
                        if (e != null && e.Def != null && e.Def.Kind == EnemyKind.Boss) boss++;
                    }
                    minuteRows.Add($"分钟{m}:敌{sess.World.Enemies.Count}" + (boss > 0 ? $"(含BOSS{boss})" : ""));
                });
                sw.Stop();
                text = $"PROBE_LONGRUN OK 无异常｜策略 {r.Strategy} seed={r.Seed} keepAlive=on"
                     + $"｜跑满 {r.Duration:0}s = {r.Duration / 60f:0.0} 分钟（上限 {minutes} 分钟）"
                     + $"｜固定步 {r.Steps}｜到达第 {r.Wave} 分钟｜击杀 {r.Kills}｜等级 {r.Level}"
                     + $"｜收割 {r.Harvests}｜进店 {r.ShopVisits}（空货架 {r.EmptyShelfVisits}）"
                     + $"｜**整场清除调用次数 = {lastDespawnCalls}**"
                     + $"｜墙钟用时 {sw.ElapsedMilliseconds}ms\n"
                     + "  每分钟敌人数（FixedTick 之前采样）：\n    " + string.Join(" ", minuteRows.ToArray())
                     + (clearRows.Count > 0
                        ? "\n  **整场清除逐条**（" + clearRows.Count + " 次）：\n    " + string.Join("\n    ", clearRows.ToArray())
                        : "\n  **整场清除逐条**：0 次 ✓");
            }
            catch (Exception ex)
            {
                sw.Stop();
                text = $"PROBE_LONGRUN THREW {ex.GetType().Name}: {ex.Message}"
                     + $"｜跑到了第 {lastMinute} 分钟｜墙钟 {sw.ElapsedMilliseconds}ms\n"
                     + FirstFrames(ex.StackTrace, 8);
            }
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ★ v186：**剑气收割作物**的读数（用户原话："让剑气也可以收割植物"）★
        ///
        /// 夹具（每个位置钉住一条性质，缺一条就说不清是哪一段在起作用）：
        ///   ① 三株**成熟**作物摆在近战扇形**射程之外**（距离 &gt; range + 1.2m）而剑气框能扫到的地方
        ///      ⇒ 它们只能被**剑气**收到（若被近战先收了，读数会露馅：`近战扇形内成熟数` 不会是 0）✓
        ///   ② 同一排再摆一株**未成熟** ⇒ 剑气从它身上飞过去，它必须**原样不动** ✓
        /// ⇒ 期望：`收 3 株`、`未成熟 1 株仍是 Growing`、`近战扇形内成熟数 = 0`（证明功劳不在近战）✓
        ///
        /// ★ 为什么不直接读"收了几株"这个计数 ★
        /// 项目里没有这个计数器（收割走 `HarvestPlot`，只发弹字与金币）⇒ 读数用**地块状态**：
        /// 成熟 = `PlotState.Grown`，收完 = 回到 `Growing`（`FarmPlot.Harvest` 的既有行为）✓
        /// </summary>
        public string ProbeSwordHarvest(string spec)
        {
            int stacks = Mathf.Clamp(ParseInt(spec, 1), 0, 3);
            TuningDef t = TableDatabase.Tuning;
            float meleeRange = t.tools.hoe.range * 1f;
            float meleeGrab = meleeRange + 1.2f;               // 与 `HarvestRipeInMeleeBox` 同一个判据

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);

            // 装剑气（0 层 = 对照组：同一套夹具、不劈剑气 ⇒ 一株都不该被收）
            if (stacks > 0)
            {
                PluginAsset sw = null;
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                    if (TableDatabase.Plugins[i] != null && TableDatabase.Plugins[i].id == "hoe_swordwave")
                        sw = TableDatabase.Plugins[i];
                if (sw == null) { Log.Warn("PROBE_SWHARVEST 表里没有 hoe_swordwave"); return "PROBE_SWHARVEST 无 hoe_swordwave"; }
                for (int k = 0; k < stacks; k++)
                    if (!WeaponSystem.Install(s.Goods, sw, WeaponSlot.Hoe, out string why))
                    { Log.Warn($"PROBE_SWHARVEST 装第 {k + 1} 层失败：{why}"); break; }
            }

            // ★ 位置不是写死的坐标，而是**从农场自己的格子在哪儿推出来** ★
            //   写死 (7.5, 0) 这类坐标会随"田地从几列几行开始 / 有没有偏移"整批落空
            //   （第一版就是这么落空的：4 个位置一个都没命中）⇒ 改成：
            //     ① 取**最右**的那一格，把玩家摆到它左边 7.2m、朝 +X 劈
            //     ② 候选 = 距离落在 (近战抓取距离, 剑气够得着) 区间、且横向偏移 ≤ 2.5m 的格子
            FarmGrid farm = s.World.Farm;
            FarmPlot rightmost = null;
            for (int i = 0; i < farm.Slots.Length; i++)
            {
                FarmPlot plot = farm.Slots[i];
                if (plot == null) continue;
                if (rightmost == null || plot.World.x > rightmost.World.x) rightmost = plot;
            }
            if (rightmost == null) { Log.Warn("PROBE_SWHARVEST 农场一格都没有"); return "PROBE_SWHARVEST 无地块"; }
            p.Pos = rightmost.World - new Vector2(7.2f, 0f);
            p.AimDir = Vector2.right;

            // 剑气能到多远：与 `SwordWaveReach` 同一条式子（探针里不另算一份）
            float reach = RunSession.SwordWaveReach(meleeRange) + 1.2f;
            var cand = new System.Collections.Generic.List<(FarmPlot plot, float dist, float perp)>();
            for (int i = 0; i < farm.Slots.Length; i++)
            {
                FarmPlot plot = farm.Slots[i];
                if (plot == null) continue;
                Vector2 rel = plot.World - p.Pos;
                // ★ 必须在**前方**：剑气只朝 +X 飞 ⇒ 身后的格子在候选里等于假阳性（第一版就漏了这条）★
                if (rel.x <= 0.5f) continue;
                float dist = rel.magnitude;
                if (dist <= meleeGrab + 0.05f) continue;      // 近战扇形够得着的不要（否则功劳分不清）
                if (dist > reach) continue;
                float perp = Mathf.Abs(rel.x * p.AimDir.y - rel.y * p.AimDir.x);
                if (perp > 2.5f) continue;
                cand.Add((plot, dist, perp));
            }
            cand.Sort((a, b) => a.dist.CompareTo(b.dist));

            // 前 3 株成熟、第 4 株**只种不催熟**（"不成熟的不能收"）
            var hit = new FarmPlot[cand.Count];
            var ripe = new bool[cand.Count];
            SeedDef def = TableDatabase.GetSeed("radish_guard");
            int found = 0;
            for (int k = 0; k < cand.Count && found < 4; k++)
            {
                FarmPlot plot = cand[k].plot;
                plot.ResetToEmpty();
                plot.BeginPlant(t.farm, def, 100, 1);
                bool makeRipe = found < 3;
                if (makeRipe) plot.TickGrowth(9999f);
                hit[found] = plot;
                ripe[found] = makeRipe;
                found++;
            }

            int ripeBefore = 0;
            for (int k = 0; k < hit.Length; k++) if (hit[k] != null && hit[k].IsRipe) ripeBefore++;

            // 近战扇形内有没有成熟地块（必须 0 —— 否则读数分不清功劳归谁）
            int ripeInMelee = 0;
            for (int i = 0; i < farm.Slots.Length; i++)
            {
                FarmPlot plot = farm.Slots[i];
                if (plot == null || !plot.IsRipe) continue;
                Vector2 to = plot.World - p.Pos;
                if (to.magnitude > meleeGrab) continue;
                if (to.magnitude > 0.01f && Vector2.Angle(Vector2.right, to) > t.tools.hoe.arc * 0.5f) continue;
                ripeInMelee++;
            }

            int waves = s.LiveSwordWaveCount;
            s.DebugAttackOnce(WeaponSlot.Hoe);
            int wavesAfterSwing = s.LiveSwordWaveCount;
            s.DebugAdvanceAttack(0.5f);                               // 把剑气推完

            int ripeAfter = 0, stillGrowing = 0;
            for (int k = 0; k < hit.Length; k++)
            {
                if (hit[k] == null) continue;
                if (hit[k].IsRipe) ripeAfter++;
                if (hit[k].State == PlotState.Growing) stillGrowing++;
            }

            var sb = new StringBuilder();
            sb.Append($"PROBE_SWHARVEST 剑气={stacks} 层｜近战 range={meleeRange:0.00}m（+地块 {meleeGrab:0.00}m）")
              .Append($"｜玩家摆在 ({p.Pos.x:0.0},{p.Pos.y:0.0}) 朝 +X｜候选格 {cand.Count} 个｜摆上 {found} 格\n");
            sb.Append($"  劈之前：成熟 {ripeBefore} 株｜近战扇形内成熟 {ripeInMelee} 株（**必须 0**）")
              .Append($"｜在飞剑气 {waves} 发\n");
            sb.Append($"  劈出去：在飞剑气 {wavesAfterSwing} 发｜推进 0.5s 后 {s.LiveSwordWaveCount} 发\n");
            sb.Append($"  结果：仍成熟 {ripeAfter} 株｜已经回到 Growing 的 {stillGrowing} 株")
              .Append($"｜★ 收成 = 劈前 {ripeBefore} → 劈后 {ripeAfter} ⇒ 收了 {ripeBefore - ripeAfter} 株 ★\n");
            for (int k = 0; k < found; k++)
            {
                if (hit[k] == null) { sb.Append($"    第{k + 1}格 缺\n"); continue; }
                sb.Append($"    第{k + 1}格 {(ripe[k] ? "成熟" : "未成熟")} @({hit[k].World.x:0.0},{hit[k].World.y:0.0})")
                  .Append($" 距离={((hit[k].World - p.Pos).magnitude):0.00}m ⇒ 现在 {hit[k].State}\n");
            }
            string text = sb.ToString();
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ④b **剑气的飞行读数**（v185 新增，只读）—— 三件事一起量：
        ///
        ///  ① **它是子弹不是瞬时线**：同一发子弹逐帧打印 `框心位置 / 已飞距离`，
        ///     第 0 帧之后每一帧都不一样 ⇒ "帧与帧之间有中间状态" 是可读的 ✓
        ///  ② **飞行距离 vs 攻击范围**：把 `AtkRange` 设成三档，打印
        ///     `攻击范围 → 飞行距离 → 前缘最远 → 框半宽 → 寿命` 的对应表 ✓
        ///  ③ **命中窗口**：在 `0.5d / 1.0d / 1.5d / 2.0d` 四处各摆一个敌人，
        ///     各自挨多少伤害 ⇒ 掉血说明"子弹飞到了"，0 说明"没飞到"
        ///     （`d` = 飞行距离，**不是**前缘最远 —— 两者差一个框长，必须分开报）✓
        ///
        /// ⚠️ 为什么不能只测 `1.0d`：`1.0d` 是**飞行终点**，但它不是**判定终点**。
        ///   判定终点 = 前缘最远 = `攻击范围 + 飞行距离`。只测 `d` 会把"射程变长了"这件事读漏 ✓
        /// </summary>
        public string ProbeWaveFlight(string spec)
        {
            TuningDef t = TableDatabase.Tuning;
            float baseRange = t.tools.hoe.range;
            var sb = new StringBuilder();

            // ── ② 对应表：攻击范围 → 飞行距离 ──
            sb.Append("PROBE_WAVEFLIGHT ① 攻击范围 → 飞行距离（`SwordWaveDistMult = "
                      + RunSession.SwordWaveDistMult.ToString("0.##") + "`，"
                      + "`SwordWaveSpeed = " + RunSession.SwordWaveSpeed.ToString("0.#") + "m/s`）\n");
            sb.Append("  攻击范围  飞行距离  前缘最远  框长   框半宽   寿命(s)\n");
            float[] targets = { 3.8f, 5.0f, 7.0f };
            for (int i = 0; i < targets.Length; i++)
            {
                float r = targets[i];
                float halfW = RunSession.MeleeBoxHalfWidth(r, t.tools.hoe.arc);
                sb.Append($"  {r,6:0.00}m  ");
                sb.Append($"{RunSession.SwordWaveDistance(r),7:0.000}m  ");
                sb.Append($"{RunSession.SwordWaveReach(r),7:0.000}m  ");
                sb.Append($"{2f * RunSession.SwordWaveHalfLen(r),5:0.00}m  ");
                sb.Append($"{halfW,6:0.000}m  ");
                sb.Append($"{RunSession.SwordWaveLife(r),6:0.000}\n");
            }

            // ── ① 逐帧位置：一发剑气在飞的时候，每一帧的框心 ──
            sb.Append("PROBE_WAVEFLIGHT ② 逐帧位置（同一发剑气，dt = 1/60，只打印前 12 帧 + 末帧）\n");
            RunSession s = WaveBench(out PlayerActor p, out EnemyActor probeEnemy);
            probeEnemy.Pos = new Vector2(-40f, -40f);      // 挪开，别被当成靶子
            float range = baseRange * s.Progress.Stats.Get(StatId.AtkRange);
            s.DebugAttackOnce(WeaponSlot.Hoe);
            var posSeen = new System.Collections.Generic.List<string>();
            for (int f = 0; f <= 20 && s.LiveSwordWaveCount > 0; f++)
            {
                if (f < 12 || s.LiveSwordWaveCount > 0)
                {
                    Vector2 wp = s.SwordWavePosAt(0);
                    posSeen.Add($"f{f,2}: x={wp.x,6:0.000} 已飞={s.SwordWaveTravelAt(0),6:0.000}m/{s.SwordWaveDistAt(0):0.000}m");
                }
                s.DebugAdvanceAttack(1f / 60f);
            }
            for (int i = 0; i < posSeen.Count; i++) sb.Append("  ").Append(posSeen[i]).Append('\n');
            sb.Append($"  ⇒ 不同位置的帧数 = {DistinctPositions(posSeen)}（= 1 就说明它还是「瞬间线」）\n");

            // ── ③ 命中窗口：0.5d / 1.0d / 1.5d / 2.0d ──
            float d = RunSession.SwordWaveDistance(range);
            float reach = RunSession.SwordWaveReach(range);
            sb.Append($"PROBE_WAVEFLIGHT ③ 命中读数（d = 飞行距离 = {d:0.000}m，前缘最远 = {reach:0.000}m）\n");
            float[] mults = { 0.5f, 1.0f, 1.5f, 2.0f };
            for (int i = 0; i < mults.Length; i++)
            {
                float at = d * mults[i];
                RunSession b = WaveBench(out PlayerActor bp, out EnemyActor be);
                be.Pos = new Vector2(-40f, -40f);
                b.World.SpawnEnemy("grunt", new Vector2(at, 0f), 1);
                EnemyActor target = b.World.Enemies[b.World.Enemies.Count - 1];
                target.Pos = new Vector2(at, 0f);
                target.Health.Setup(1e5f, 0f);
                float r = target.Radius;
                b.DebugAttackOnce(WeaponSlot.Hoe);
                b.DebugAdvanceAttack(0.5f);
                float dealt = 1e5f - target.Health.Hp;
                bool geometricallyHittable = at <= reach + r;
                sb.Append($"  距离 = {mults[i]:0.0}d = {at,6:0.000}m ⇒ 伤害 {dealt,6:0.####}"
                          + $"（几何上 {(geometricallyHittable ? "够得到" : "够不到")}：上限 {reach + r:0.000}m）"
                          + $"{(dealt > 0f ? " ★命中" : " 未命中")}\n");
            }
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>`ProbeWaveFlight` 的靶场：关暴击、站原点、朝 +X、装 1 层剑气。</summary>
        private RunSession WaveBench(out PlayerActor p, out EnemyActor spare)
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset a = TableDatabase.Plugins[i];
                if (a != null && a.id == "hoe_swordwave") { WeaponSystem.Install(s.Goods, a, WeaponSlot.Hoe, out _); break; }
            }
            // 放一个"占位靶"在远处：`SwingHoe` 会先做收割判定，场上完全没有敌人时行为一致，
            // 但留一个可写的引用能让调用点少一次判空。
            s.World.SpawnEnemy("grunt", new Vector2(-40f, -40f), 1);
            spare = s.World.Enemies[s.World.Enemies.Count - 1];
            spare.Pos = new Vector2(-40f, -40f);
            spare.Health.Setup(1e5f, 0f);
            return s;
        }

        /// <summary>数一数"逐帧位置"这一列里出现了几种不同的框心位置（用来证明它在飞）。</summary>
        private static int DistinctPositions(System.Collections.Generic.List<string> lines)
        {
            var set = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                int cut = lines[i].IndexOf("已飞=", StringComparison.Ordinal);
                set.Add(cut > 0 ? lines[i].Substring(0, cut) : lines[i]);
            }
            return set.Count;
        }

        /// <summary>
        /// ③c **肥料跨波保留**：把充能设成 7 之后走一次 `StartWave`，看它还在不在（只读）。
        ///
        /// 两件事分开量，因为它们修的是两个不同的丢法：
        ///   `plain`  —— 手上 7 发，什么都不做就过分钟：旧版 → 0（白攒）✓
        ///   `refund` —— 刚投出一发（6 发 + 1 发在前摇），过分钟把队列清掉：旧版 → 0（连那发也吞了）
        /// </summary>
        public string ProbeFertCarry()
        {
            var sb = new StringBuilder();
            sb.Append("PROBE_FERT_CARRY 上限 ").Append(RunSession.FertAmmoMax).Append('\n');

            // ① 纯跨波保留
            RunSession a = FertBench();
            a.FertAmmo = 7;
            int a0 = a.FertAmmo;
            a.StartWave(2);
            sb.Append($"  plain ：跨波前 {a0} → 跨波后 {a.FertAmmo}（期望保留 {Mathf.Min(a0, RunSession.FertAmmoMax)}）\n");

            // ② 投出一发后再跨波（前摇队列被清 ⇒ 那 1 发要退回来）
            RunSession b = FertBench();
            b.FertAmmo = 7;
            b.DebugThrowOnly();
            int bThrown = b.FertAmmo;
            int bPend = b.PendingBlastCount;
            b.StartWave(2);
            sb.Append($"  refund：投掷后 {bThrown}（待爆 {bPend}）→ 跨波后 {b.FertAmmo}"
                    + $"（期望 {Mathf.Min(bThrown + bPend, RunSession.FertAmmoMax)}）\n");

            // ③ 上限仍然夹得住
            RunSession c = FertBench();
            c.FertAmmo = RunSession.FertAmmoMax;
            c.StartWave(2);
            sb.Append($"  clamp ：满弹 {RunSession.FertAmmoMax} → 跨波后 {c.FertAmmo}（不得超上限）\n");

            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>给肥料探针用的一局：玩家在原点、无敌人、冷却清零。</summary>
        private static RunSession FertBench()
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.AimWorldLast = new Vector2(3f, 0f);
            return s;
        }

        /// <summary>
        /// ④b **四件锄头插件全装：一次挥击的总伤害 ÷ 空手**（只读）。
        /// 对应自检 `AllHoePluginsTotalRatio` —— v182 把夹具从 5.5m 挪到 2.6m 之后，
        /// "空手读数"不再是 0（近战扇形自己就够得到），所以那条断言的**含义**从
        /// "剑气打到了框外"变成了"双击 + 剑气的叠加"。这里把比值**实测出来**，
        /// 而不是只说一句"它过了" ✓
        /// </summary>
        public string ProbeHoeVolley()
        {
            float bare = HoeVolley(false);
            float full = HoeVolley(true);
            float ratio = bare <= 1e-4f ? (full > 0f ? 999f : 0f) : full / bare;
            string text = $"PROBE_HOE_VOLLEY 3 只敌人摆在 ±12°/0° @2.6m｜空手 {bare:0.##}｜四件全装 {full:0.##}"
                        + $"｜比值 ×{ratio:0.00}（自检断言要求 > 1.20 ⇒ {(ratio > 1.2f ? "PASS" : "FAIL")}）";
            Log.Info(text);
            return text;
        }

        /// <summary>一次挥击动作对 ±12°/0° @2.6m 三只敌人的总伤害。</summary>
        private static float HoeVolley(bool allPlugins)
        {
            RunSession s = new RunSession();
            s.Start(4242UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);   // 关掉暴击 ⇒ 读数可复现

            if (allPlugins)
            {
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                {
                    PluginAsset a = TableDatabase.Plugins[i];
                    if (a == null || a.allowedWeapons == null) continue;
                    if (a.allowedWeapons.Length != 1 || a.allowedWeapons[0] != WeaponSlot.Hoe) continue;
                    WeaponSystem.Install(s.Goods, a, WeaponSlot.Hoe, out _);
                }
            }

            const float d = 2.6f;
            var es = new EnemyActor[3];
            for (int i = 0; i < 3; i++)
            {
                float ang = (i - 1) * 12f * Mathf.Deg2Rad;      // −12° / 0° / +12°
                Vector2 pos = new Vector2(d * Mathf.Cos(ang), d * Mathf.Sin(ang));
                s.World.SpawnEnemy("grunt", pos, 1);
                es[i] = s.World.Enemies[s.World.Enemies.Count - 1];
                es[i].Pos = pos;
                es[i].Health.Setup(1e5f, 0f);
            }

            float hp0 = es[0].Health.Hp + es[1].Health.Hp + es[2].Health.Hp;
            s.DebugAttackOnce(WeaponSlot.Hoe);
            s.DebugAdvanceAttack(0.5f);      // 把双击的第二刀推完
            float hp1 = es[0].Health.Hp + es[1].Health.Hp + es[2].Health.Hp;
            return hp0 - hp1;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v184：本轮（伤害/暴击核心 + 一批数值）的**只读验收探针** ★
        //
        // 为什么不写进 SelfCheck：用户硬约束把断言条数钉在 **437** 条 ✗
        // 加一条就是 438 ⇒ 那不是"多验了一条"，而是**基线漂移** ✓
        // 而这些读数（同一刀在三处逐位相同、贴脸挥空的次数、买前买后四段）
        // 本来就是"一张表"，不是一句 PASS/FAIL 能表达的 ✓
        //
        // 纪律：全部只在自己**新建的隔离 RunSession** 里跑，不碰正在进行的局；
        //       除了 `ProbePluginBuy` 会在那个隔离局里"买一次"，没有任何写操作 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ③ **同一刀在三处的读数**：武器实际伤害 / 敌人受到伤害 / UI 弹字。
        /// spec = `weapon,crit` —— weapon ∈ {hoe, can, fert}，crit = 1 表示"必暴击"（暴击率置 1）。
        ///
        /// 判据是**逐位相同**，不是"差不多"：
        ///   目标血量给到 1e9 ⇒ `dealt` 不会被血量裁剪 ⇒ 三者必须相等 ✓
        ///   （若拿正常怪测，最后一击 `dealt` 会被剩余血量截断 —— 那是**正确**的差异，
        ///     所以验证"同一个值"必须先把"血量上限"这个变量去掉 ✓）
        /// </summary>
        public string ProbeDamageTrace(string spec)
        {
            // 表没载入时 `RunSession.Start` 会在 `TableDatabase.Tuning` 上 NRE ⇒ 先垫一次（幂等）
            TableDatabase.Load(false);
            string key = (spec ?? "hoe,0").Trim().ToLowerInvariant();
            // ★ v186：`all` ⇒ 一次把**六组**读数全打出来（锄/壶/肥 × 普通/暴击）★
            //   逐组调 `TraceOne`（每组都是独立的隔离局），最后数"是"的个数 ⇒
            //   一句话就能回答"三处是不是同一个值、六组是不是全绿" ✓
            if (key == "all" || key == "six" || key == "6")
            {
                var sb = new StringBuilder();
                sb.Append("PROBE_DAMAGE 六组读数（锄头/水壶/肥料 × 普通/暴击）\n");
                string[] weapons = { "hoe", "can", "fert" };
                int yes = 0;
                for (int w = 0; w < weapons.Length; w++)
                    for (int c = 0; c < 2; c++)
                    {
                        string line = TraceOne(weapons[w], c == 1);
                        // ★ v187：两层都要绿 —— 同步（逐位相同）＋ 正确（公式一致）★
                        if (line.Contains("逐位相同=是") && line.Contains("公式一致=是")) yes++;
                        sb.Append("  ").Append(line).Append('\n');
                    }
                sb.Append("  六行全\"是\" = ").Append(yes == 6 ? "是" : "否").Append("（").Append(yes).Append("/6）");
                string all = sb.ToString();
                Log.Info(all);
                return all;
            }

            string[] parts = key.Split(',');
            bool forceCrit = parts.Length > 1 && ParseInt(parts[1], 0) == 1;
            string text = "PROBE_DAMAGE " + TraceOne(parts[0].Trim(), forceCrit);
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// 三处读数的单组实现（武器 × 是否必暴击各一次）。
        ///
        /// ★ v187：判据**换成了两层**（用户要求："三处一致只证明同步，不证明正确"）★
        ///   第一层 **同步**（三处是不是同一个数）：
        ///     ① UI 弹字     —— **真正上屏的那条 `FloatingTextEvent` 的文字**（不是重算的数）
        ///        必须 == `DamageResult.RawValue`
        ///        （v187 起弹字取 `RawValue`；改前取 `Dealt` ⇒ 敌人血一薄就弹成剩余血 ✗）
        ///     ② 敌人受到伤害 —— 血量差，必须 == `DamageResult.Dealt`（结算与血量对得上）
        ///   第二层 **正确**（那个数是不是**应有的数**）：
        ///     ③ `RawValue` 必须 == **独立再算一遍的期望值** ——
        ///        期望值走武器卡同一条式子（`WeaponDamage` / `WeaponBlastDamage`，暴击与否按本次固定）
        ///        ⚠️ 没有这一层的话，"三处一起错"照样全绿（本工程吃过这个亏：卡面与引擎一起漂）
        ///
        /// 目标血量给到 1e5 ⇒ `Dealt` 不会被血量裁剪 ⇒ ①② 可以直接比 ✓
        /// （若拿正常怪测，最后一击 `Dealt` 会被剩余血截断 —— 那是**正确**的差异；
        ///   而那种情况下弹字**仍然**显示 `RawValue`，所以下方 `ProbeBlow` 专门用自然血靶再验一遍 ✓）
        /// </summary>
        private string TraceOne(string key, bool forceCrit)
        {
            WeaponSlot slot = key.StartsWith("can") ? WeaponSlot.Can
                : key.StartsWith("fert") ? WeaponSlot.Fertilizer : WeaponSlot.Hoe;

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            // 暴击固定（0 或 1）⇒ 读数可复现，不必靠运气
            s.Progress.Stats.SetBase(StatId.CritRate, forceCrit ? 1f : 0f);

            s.World.SpawnEnemy("grunt", new Vector2(1.2f, 0f), 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = new Vector2(1.2f, 0f);
            // ★ 血量给 1e5 而不是 1e9 ★ —— 后者是 float 精度陷阱：
            //   1e9 附近的 ulp ≈ 64 ⇒ `1e9 - 5` **仍然等于 1e9** ⇒ "敌人受到伤害"读出来恒为 0 ✗
            //   1e5 处 ulp ≈ 0.0078 ⇒ 5 点伤害能逐位读出来 ✓ 同时足够大到不会被一击打死 ✓
            e.Health.Setup(1e5f, 0f);
            // 肥料要有个落点：`CurrentBlastCenter` 读的就是它（不设的话爆心在射程钳制后的默认处）
            s.AimWorldLast = new Vector2(1.2f, 0f);

            string popup = "(无弹字)";
            System.Action<FloatingTextEvent> onPopup = ev =>
            {
                if (ev.Kind != PopupKind.Crit && ev.Kind != PopupKind.DamageToEnemy) return;
                popup = ev.Text + (ev.Kind == PopupKind.Crit ? "[暴击样式]" : "[普通样式]");
            };

            float hp0 = e.Health.Hp;
            Bus.Subscribe(onPopup);
            try { s.DebugAttackOnce(slot); }
            finally { Bus.Unsubscribe(onPopup); }

            DamageResult r = s.World.LastHit;          // ← 结算的唯一来源（v184）
            StatBlock st2 = s.Progress.Stats;
            float hpDelta = hp0 - e.Health.Hp;

            // ── ③ 期望值：与武器卡**同一条式子**独立再算一遍（第二层判据）──
            TuningDef tt = TableDatabase.Tuning;
            ToolDef tool = slot == WeaponSlot.Can ? tt.tools.can
                         : slot == WeaponSlot.Fertilizer ? tt.tools.fertilizer : tt.tools.hoe;
            float dmgStat = slot == WeaponSlot.Can ? st2.Get(StatId.RangedDamage)
                          : slot == WeaponSlot.Fertilizer ? st2.Get(StatId.BlastDamage)
                          : st2.Get(StatId.MeleeDamage);
            float expected = slot == WeaponSlot.Fertilizer
                ? DamageResolver.WeaponBlastDamage(tool.baseValue, dmgStat, st2.Get(StatId.AtkMult),
                                                   forceCrit, st2.Get(StatId.CritDmg), tt.formula.minDamage)
                : DamageResolver.WeaponDamage(tool.baseValue, dmgStat, st2.Get(StatId.AtkMult),
                                              forceCrit, st2.Get(StatId.CritDmg), tt.formula.minDamage);

            // ── ① 弹字的数：从**上屏那条事件文字**里读回来（"-12.5" ⇒ 12.5），不是用 r 反推 ──
            //   ★ v187 起可能是小数（弹字不再四舍五入），所以按 float 解析 ★
            float popupValue = float.NaN;
            if (popup.StartsWith("-"))
            {
                int cut = popup.IndexOfAny(new[] { '[', '！', '!' });
                string digits = cut > 0 ? popup.Substring(1, cut - 1) : popup.Substring(1);
                float.TryParse(digits, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out popupValue);
            }
            bool sync = Mathf.Abs(r.Dealt - hpDelta) < 1e-4f
                        && !float.IsNaN(popupValue)
                        && Mathf.Abs(popupValue - r.RawValue) < 1e-3f;
            bool correct = Mathf.Abs(r.RawValue - expected) < 1e-4f;
            string text = $"武器={key}{(forceCrit ? " 必暴击" : " 无暴击")}"
                        + $"｜武器实际伤害={r.RawValue:0.####}"
                        + $"｜应有值={expected:0.####}"
                        + $"｜敌人受到伤害={hpDelta:0.####}（结算 Dealt={r.Dealt:0.####}）"
                        + $"｜UI 弹字={popup}"
                        + $"｜暴击标记={r.Crit}"
                        + $"｜逐位相同={(sync ? "是" : "否")}"
                        + $"｜公式一致={(correct ? "是" : "否")}";
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ★ v187：**一刀的四个数并排**（用户需求②③的读数工具，只读）★
        ///
        /// 四列 =  `武器卡算的 / RawValue / Dealt / 弹字文字`
        ///   · 武器卡算的 —— 与 `ImGuiHud.DrawMenuWeaponCard` **同一个函数、同一批入参**
        ///     （`DamageResolver.WeaponDamage` / `WeaponBlastDamage`，`crit: false`）✓
        ///   · RawValue  —— `DamageResult.RawValue`（武器侧算完、未经血量裁剪）
        ///   · Dealt     —— `DamageResult.Dealt`（**真正从血/盾里扣掉的量**）
        ///   · 弹字      —— 真正上屏那条 `FloatingTextEvent.Text`（不是重算的数）
        ///
        /// ── 为什么要专门量"自然血量"的靶（spec = `live`）──
        /// 上一版（`ProbeDamageTrace`）的靶是 1e5 血 ⇒ `Dealt` **永远不会被血条裁剪** ⇒
        /// 四列必然相等，于是它**证不出**玩家在实战里看到的那件事：
        /// 第 1 波基础兵只有 **2.8 血**（`enemies.json`：`grunt.baseHp = 2.8`），
        /// 而锄头一下就是 4~5 点、暴击 8~10 点 ⇒ **每一刀都是溢出伤害**，
        /// `Dealt` 恒等于"这只兵剩下的血" ⇒
        ///   ① 暴击与不暴击弹出**同一个数**（"暴击后伤害无变化"）✓
        ///   ② 弹字那个数**永远不等于**卡面算出来的伤害（"弹字与计算不符"）✓
        /// ⇒ 所以本探针把"靶血量"当成一个**显式变量**打出来，两种靶各一行，差在哪一眼可见 ✓
        ///
        /// spec：`big`（默认，1e5 血靶）/ `live`（自然血量靶）/ 两个都给 = 两行
        /// </summary>
        public string ProbeBlow(string spec)
        {
            TableDatabase.Load(false);
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            PlayerActor p = s.World.Player;
            if (p == null) { s.Dispose(); return "PROBE_BLOW 无玩家"; }
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;

            TuningDef t = TableDatabase.Tuning;
            StatBlock st = s.Progress.Stats;
            float atkMult = st.Get(StatId.AtkMult);
            float critDmg = st.Get(StatId.CritDmg);

            var sb = new StringBuilder();
            sb.Append("PROBE_BLOW 一刀四数｜卡面 / RawValue / Dealt / 弹字\n");
            sb.Append($"  基准：伤害倍数={atkMult:P0}｜暴击伤害={critDmg:P0}"
                      + $"｜爆炸倍率=已删除（v195）｜minDamage={t.formula.minDamage:0.##}\n");

            string[] keys = { "hoe", "can", "fert" };
            float[] hpSet = { 1e5f, 0f };          // 0 = 不动它（= 自然血量）
            string[] hpName = { "1e5血靶", "自然血靶" };
            for (int w = 0; w < keys.Length; w++)
            {
                WeaponSlot slot = keys[w] == "can" ? WeaponSlot.Can
                                : keys[w] == "fert" ? WeaponSlot.Fertilizer : WeaponSlot.Hoe;
                ToolDef tool = slot == WeaponSlot.Can ? t.tools.can
                             : slot == WeaponSlot.Fertilizer ? t.tools.fertilizer : t.tools.hoe;
                float dmgStat = slot == WeaponSlot.Can ? st.Get(StatId.RangedDamage)
                              : slot == WeaponSlot.Fertilizer ? st.Get(StatId.BlastDamage)
                              : st.Get(StatId.MeleeDamage);
                string statName = slot == WeaponSlot.Can ? "远程伤害"
                                : slot == WeaponSlot.Fertilizer ? "爆炸伤害" : "近战伤害";
                // 与 `DrawMenuWeaponCard` 逐项相同（crit: false = 卡面印的是"不暴击时的伤害"）
                float card = slot == WeaponSlot.Fertilizer
                    ? DamageResolver.WeaponBlastDamage(tool.baseValue, dmgStat, atkMult, false, critDmg,
                                                       t.formula.minDamage)
                    : DamageResolver.WeaponDamage(tool.baseValue, dmgStat, atkMult, false, critDmg,
                                                  t.formula.minDamage);
                sb.Append($"  [{RunSession.WeaponSlotName(slot)}] 卡面 =（基础 {tool.baseValue:0.#} + {statName} {dmgStat:0.#}）"
                          + $"× {atkMult:P0}"
                          + $" = {card:0.####}\n");

                for (int c = 0; c < 2; c++)
                    for (int h = 0; h < hpSet.Length; h++)
                    {
                        if (spec == "big" && h == 1) continue;
                        if (spec == "live" && h == 0) continue;

                        s.World.Enemies.Clear();
                        s.World.SpawnEnemy("grunt", new Vector2(1.2f, 0f), 1);
                        EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
                        e.Pos = new Vector2(1.2f, 0f);
                        if (hpSet[h] > 0f) e.Health.Setup(hpSet[h], 0f);
                        float hpBefore = e.Health.Hp;
                        s.AimWorldLast = new Vector2(1.2f, 0f);
                        // 暴击固定（0 或 1）⇒ 读数可复现，不靠运气
                        st.SetBase(StatId.CritRate, c == 1 ? 1f : 0f);
                        for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;

                        string popup = "(无弹字)";
                        System.Action<FloatingTextEvent> onPopup = ev =>
                        {
                            if (ev.Kind != PopupKind.Crit && ev.Kind != PopupKind.DamageToEnemy) return;
                            popup = ev.Text + (ev.Kind == PopupKind.Crit ? "[暴击样式]" : "[普通样式]");
                        };
                        Bus.Subscribe(onPopup);
                        try { s.DebugAttackOnce(slot); }
                        finally { Bus.Unsubscribe(onPopup); }

                        DamageResult r = s.World.LastHit;
                        float hpDelta = hpBefore - e.Health.Hp;
                        // ③ 弹字里那个数：从**上屏文字**读回来（"-12.5" ⇒ 12.5），不是用 r 反推
                        float shown = float.NaN;
                        if (popup.StartsWith("-"))
                        {
                            int cut = popup.IndexOfAny(new[] { '[', '！', '!' });
                            string digits = cut > 0 ? popup.Substring(1, cut - 1) : popup.Substring(1);
                            float.TryParse(digits, System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out shown);
                        }
                        // ★ 判据分三段（v187）★
                        //   ① `弹字 = Raw`  —— 用户要求的那一条（弹字显示这一击算出来的伤害）
                        //   ② `Raw = 卡 × (暴击 ? 暴伤 : 1)` —— 引擎与**武器卡**同一条式子；
                        //      卡面印的是**不暴击**的值（用户："暴击效果作为内部计算，不显示"）⇒
                        //      暴击那一行天然是"卡 × 200%"，所以判据要**带上暴击倍率**再比，
                        //      否则会把自己按规格实现的东西读成缺陷 ✗
                        //   ③ `Dealt = 血量差` —— 结算层与血条一致；`Dealt ≤ Raw` 是**溢出伤害**的
                        //      定义（2.8 血的兵吃 8 点砍），不是错误 ⇒ 单独报 ✓
                        bool shownOk = !float.IsNaN(shown) && Mathf.Abs(shown - r.RawValue) < 1e-3f;
                        bool cardOk = Mathf.Abs(r.RawValue - card * (c == 1 ? critDmg : 1f)) < 1e-4f;
                        bool dealtOk = Mathf.Abs(r.Dealt - hpDelta) < 1e-4f;
                        sb.Append($"    {(c == 1 ? "必暴击" : "无暴击")} {hpName[h],-10}"
                                  + $" 卡={card,7:0.####} Raw={r.RawValue,7:0.####} Dealt={r.Dealt,7:0.####}"
                                  + $" 弹字={popup,-14} 血={hpBefore:0.###}→{e.Health.Hp:0.###}"
                                  + $" 暴击标记={r.Crit}"
                                  + $" 弹字=Raw={(shownOk ? "是" : "否")}"
                                  + $" 卡×暴击=Raw={(cardOk ? "是" : "否")}"
                                  + $" Dealt=血量差={(dealtOk ? "是" : "否")}"
                                  + $" 溢出={(r.RawValue - r.Dealt > 1e-3f ? "是" : "否")}\n");
                    }
            }
            string text = sb.ToString();
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ★ v186：**玩家受击弹字的只读验收**（需求⑤：扣血红 / 扣护盾蓝）★
        ///
        /// 为什么必须量"弹字"而不是只量血量：
        /// 血量读数只能证明"掉了多少"，证明不了"弹出来的是什么颜色、弹了几行"✗
        /// 而需求⑤要的正是后者 —— 所以这里订阅 `FloatingTextEvent`，
        /// 把**真正上屏的那两行**连同 `PopupStyle.ColorOf(kind)` 的颜色一起打出来 ✓
        ///
        /// 走的是**真实伤害链路**（`World.DealDamage` → `DamageResolver.Resolve` →
        /// `Actor.OnHitLanded`），不是自己拼一条弹字 ⇒ 这条探针能证明"入口在那里"✓
        /// spec = 伤害量（默认 `4`）；玩家开局 生命 10 / 护盾 2 ⇒ 4 点正好 "盾 2 + 血 2" 两行都出 ✓
        /// </summary>
        public string ProbePlayerPopup(string spec)
        {
            TableDatabase.Load(false);      // 同上：表未载入时 RunSession.Start 会 NRE
            float dmg = ParseFloat(spec, 4f);
            if (dmg <= 0f) dmg = 4f;
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            PlayerActor p = s.World.Player;
            // 让两条线的分量都可预测：护盾 2 / 生命 10（= tuning.player 的开局值）
            p.Health.Setup(10f, 2f);

            var lines = new System.Collections.Generic.List<string>();
            System.Action<FloatingTextEvent> onPopup = ev =>
            {
                if (ev.Kind != PopupKind.DamageToPlayer && ev.Kind != PopupKind.DamageToPlayerShield) return;
                Color c = PopupStyle.ColorOf(ev.Kind);
                lines.Add($"{ev.Kind} 文字={ev.Text} 颜色=({c.r:0.00},{c.g:0.00},{c.b:0.00})");
            };
            Bus.Subscribe(onPopup);
            DamageResult r;
            try
            {
                r = s.World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical,
                    RawValue = dmg,
                    Source = null,
                    Target = p,
                    IgnoreDodge = true
                });
            }
            finally { Bus.Unsubscribe(onPopup); }

            var sb = new StringBuilder();
            sb.Append("PROBE_PLAYER_POPUP 受击 ").Append(dmg.ToString("0.##")).Append(" 点")
              .Append("｜结算 Dealt=").Append(r.Dealt.ToString("0.##"))
              .Append("（护盾 ").Append(r.ShieldDealt.ToString("0.##"))
              .Append(" / 生命 ").Append(r.HpDealt.ToString("0.##")).Append("）\n");
            sb.Append("  弹字行数 = ").Append(lines.Count).Append('\n');
            for (int i = 0; i < lines.Count; i++)
                sb.Append("  第 ").Append(i + 1).Append(" 行：").Append(lines[i]).Append('\n');
            sb.Append("  判据：扣血那行颜色 = PopupStyle 红 (1.00,0.24,0.22)；")
              .Append("扣护盾那行颜色 = PopupStyle 蓝 (0.36,0.68,1.00)\n");
            string text = sb.ToString();
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ⑤ **"挥了但零伤害"的次数**：每个距离各挥 N 次，数"这一刀一点血都没掉"的次数。
        ///
        /// 距离表第一位是 **0m（敌人圆心压在玩家圆心上）** —— 那是 v184 修的那个退化：
        /// `Vector2.normalized` 对零向量返回零向量 ⇒ 点积恒 0 ⇒ 被判成"在扇形外" ✗
        /// 后面几个是对照组（0.02m 刚过退化阈值、0.3m 贴身、3.5m 射程边缘）✓
        /// </summary>
        public string ProbeHoeMiss(string spec)
        {
            int n = Mathf.Clamp(ParseInt(spec, 200), 10, 5000);
            float[] dists = { 0f, 0.02f, 0.3f, 1.2f, 2.6f, 3.5f };
            var sb = new StringBuilder();
            sb.Append("PROBE_HOE_MISS 每个距离各挥 ").Append(n).Append(" 次（暴击关闭，单只 1e5 血敌人）\n");
            sb.Append("  「实际零伤害」= 真的没掉血；「旧判据漏」= 用 v184 之前的式子 ")
              .Append("`Dot(AimDir, delta.normalized) < cos(arc/2)` 会在同一几何上判成漏；")
              .Append("「新判据漏」= 现在代码里的式子（多一条『圆心重合即命中』）\n");
            int totalEmpty = 0, totalOld = 0, totalNew = 0;
            float cosLimit = Mathf.Cos(TableDatabase.Tuning.tools.hoe.arc * 0.5f * Mathf.Deg2Rad);
            for (int d = 0; d < dists.Length; d++)
            {
                RunSession s = new RunSession();
                s.Start(20240607UL);
                s.StartWave(1);
                s.World.Enemies.Clear();
                PlayerActor p = s.World.Player;
                p.Pos = Vector2.zero;
                p.AimDir = Vector2.right;
                s.Progress.Stats.SetBase(StatId.CritRate, 0f);

                Vector2 at = new Vector2(dists[d], 0f);
                s.World.SpawnEnemy("grunt", at, 1);
                EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
                // ★ 1e5 而不是 1e9 ★ —— `1e9 ± 5` 在 float 里是同一个数（ulp≈64），
                //   拿它当"打不死的高血怪"会让"这一刀掉没掉血"**恒判为没掉** ⇒ 探针自己全报零伤害 ✗
                e.Health.Setup(1e5f, 0f);

                int empty = 0, oldMiss = 0, newMiss = 0;
                for (int i = 0; i < n; i++)
                {
                    // 每刀前把双方摆回原位：击退会把敌人推开，那会污染下一条读数
                    e.Pos = at;
                    e.KnockbackVel = Vector2.zero;
                    p.Pos = Vector2.zero;
                    p.AimDir = Vector2.right;
                    p.SwitchWindup = 0f;
                    for (int k = 0; k < p.WeaponCds.Length; k++) p.WeaponCds[k] = 0f;

                    // 同一份几何上，把"旧式子"与"新式子"各算一次（**判定规则**的对照，
                    // 而不是"改了之后再看有没有坏"）—— 这样前后两个数出自同一组输入 ✓
                    Vector2 delta = e.Pos - p.Pos;
                    float dist = delta.magnitude;
                    if (Vector2.Dot(p.AimDir, delta.normalized) < cosLimit) oldMiss++;          // v184 之前
                    if (dist > RunSession.MeleeCoincidentDist
                        && Vector2.Dot(p.AimDir, delta / dist) < cosLimit) newMiss++;           // v184 之后

                    float h0 = e.Health.Hp;
                    s.DebugAttackOnce(WeaponSlot.Hoe);
                    if (e.Health.Hp >= h0 - 1e-4f) empty++;
                }
                totalEmpty += empty; totalOld += oldMiss; totalNew += newMiss;
                sb.Append("  距离 ").Append(dists[d].ToString("0.00")).Append("m：挥 ").Append(n)
                  .Append(" 次｜实际零伤害 ").Append(empty)
                  .Append("｜旧判据漏 ").Append(oldMiss)
                  .Append("｜新判据漏 ").Append(newMiss).Append('\n');
                s.Dispose();
            }
            sb.Append("  合计：挥 ").Append(n * dists.Length).Append(" 次｜实际零伤害 ").Append(totalEmpty)
              .Append("｜旧判据漏 ").Append(totalOld).Append("｜新判据漏 ").Append(totalNew);
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ⑥ **插件 买前 → 买后 → 挂载 → 生效** 四段读数。
        /// spec = 插件 id（默认 `hoe_swordwave`）。买的那一下走的**就是** `ShopSystem.TryBuy`（玩家那条路）。
        /// </summary>
        public string ProbePluginBuy(string spec)
        {
            string id = string.IsNullOrEmpty(spec) ? "hoe_swordwave" : spec.Trim();
            PluginAsset a = null;
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                if (TableDatabase.Plugins[i] != null && TableDatabase.Plugins[i].id == id)
                { a = TableDatabase.Plugins[i]; break; }
            if (a == null) return Fail("插件表里没有 " + id);

            WeaponSlot slot = a.allowedWeapons != null && a.allowedWeapons.Length > 0
                ? a.allowedWeapons[0] : WeaponSlot.Hoe;

            RunSession s = new RunSession();
            s.Start(4242UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            s.Gold = 999;
            s.Shop.Offers.Clear();
            s.Shop.Offers.Add(new ShopOffer
            {
                Kind = OfferKind.Plugin, Plugin = a, Price = 10, Quality = 1,
                Title = a.displayName, Subtitle = "探针"
            });

            int bag0 = s.Goods.PendingCount;
            int slots0 = s.Goods.SlotsUsedOn(slot);
            int mods0 = s.ModsFor(slot).SourceCount;
            float ch0 = PluginChannelValue(s.ModsFor(slot), a.effect);

            bool bought = s.Shop.TryBuy(0, s, out string why);
            int bag1 = s.Goods.PendingCount;
            int slots1 = s.Goods.SlotsUsedOn(slot);
            int mods1 = s.ModsFor(slot).SourceCount;
            float ch1 = PluginChannelValue(s.ModsFor(slot), a.effect);

            string text = $"PROBE_PLUGINBUY {id}（{WeaponSystem.ChannelOf(a.effect)}）→ {RunSession.WeaponSlotName(slot)}\n"
                        + $"  ① 买前：背包待装 {bag0}｜{RunSession.WeaponSlotName(slot)} 插件位 {slots0}/{PlayerGoods.MaxPluginsPerWeapon}"
                        + $"｜聚合来源 {mods0} 件｜通道值 {ch0:0.###}\n"
                        + $"  ② 买入：TryBuy={(bought ? "成功" : "失败（" + why + "）")}｜金币 999→{s.Gold}\n"
                        + $"  ③ 挂载：背包待装 {bag1}｜插件位 {slots1}/{PlayerGoods.MaxPluginsPerWeapon}（↑{slots1 - slots0}）\n"
                        + $"  ④ 生效：聚合来源 {mods0}→{mods1} 件｜通道值 {ch0:0.###}→{ch1:0.###}（↑{ch1 - ch0:0.###}）";
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ③ **商店卖 2 级 / 3 级种子：买入链路**（价目 → 买 2 级 → 种下 → 满袋合成）。
        /// spec = 波次（省略 = 1）。买的那一下走的**就是** `ShopSystem.TryBuy`（玩家那条路）✓
        ///
        /// ★ 为什么这条探针必须存在 ★
        /// "货架写着 2 级、买回袋子却变 1 级"这类缺陷**不报错、不崩、日志干净**：
        /// 旧实现 `SeedBag.AddSeed` 把 `Level` **写死成 `StartingLevel`** ⇒ 买 2 级等于买 1 级，
        /// 而价格照收 2 级的 ✗ 所以判据只能读**袋里的 `SeedSlot.Level`** 与
        /// **地里的 `FarmPlot.Level`**，不能读货架（货架是"我打算卖什么"，不是结果）✓
        /// </summary>
        public string ProbeShopSeedBuy(string spec)
        {
            TableDatabase.Load(true);
            int wave = Mathf.Max(1, ParseInt(spec, 1));
            SeedDef d = TableDatabase.GetSeed("radish_guard");
            if (d == null) return Fail("种子里没有 radish_guard");
            int lv1 = SeedSlot.ShopPriceFrom(d.Price, d.RarityTier, wave);
            int p2 = ShopSystem.SeedLevelPrice(lv1, 2);
            int p3 = ShopSystem.SeedLevelPrice(lv1, 3);
            int c2 = ShopSystem.SeedMergeCost(lv1, 2);
            int c3 = ShopSystem.SeedMergeCost(lv1, 3);

            RunSession s = new RunSession();
            s.Start(4242UL);
            s.StartWave(wave);
            s.World.Enemies.Clear();
            s.Gold = 999;
            // ① 货架：同一种的 1/2/3 级各一张 + 一张多余的 2 级（第 ④ 步用）
            s.Shop.Offers.Clear();
            for (int lv = 1; lv <= 3; lv++)
                s.Shop.Offers.Add(new ShopOffer
                {
                    Kind = OfferKind.Seed, Seed = d, Quality = d.RarityTier, Level = lv,
                    Price = ShopSystem.SeedLevelPrice(lv1, lv), Title = d.Name, Subtitle = "探针"
                });
            s.Shop.Offers.Add(new ShopOffer
            {
                Kind = OfferKind.Seed, Seed = d, Quality = d.RarityTier, Level = 2,
                Price = p2, Title = d.Name, Subtitle = "探针（满袋合成用）"
            });

            // ② 买第 2 张（2 级）：袋里必须是 2 级，地里那一株也必须是 2 级
            int gold0 = s.Gold;
            bool ok2 = s.Shop.TryBuy(1, s, out string why2);
            int gold1 = s.Gold;
            int bagLevel = 0, bagSlot = -1;
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot b = s.Bag[i];
                if (b.IsEmpty || b.Def == null || b.Def.Id != d.Id) continue;
                bagLevel = b.Level; bagSlot = i; break;
            }
            int plotLevel = 0;
            for (int i = 0; i < s.World.Farm.Slots.Length; i++)
            {
                FarmPlot p = s.World.Farm.Slots[i];
                if (p != null && p.CropSeedId == d.Id) { plotLevel = p.Level; break; }
            }

            // ③ 满袋买入：`TryMergeWithExisting` 必须按**商品等级**找槽位（找 2 级，不是 1 级）
            while (s.Bag.FirstEmpty() >= 0) s.Bag.AddSeed(d, null, d.RarityTier, out _);
            int lv2Slot = -1;
            for (int i = 0; i < s.Bag.SlotCount; i++)
                if (!s.Bag[i].IsEmpty && s.Bag[i].Level == 2) { lv2Slot = i; break; }
            int lv2BeforeCnt = 0;
            for (int i = 0; i < s.Bag.SlotCount; i++) if (!s.Bag[i].IsEmpty && s.Bag[i].Level == 2) lv2BeforeCnt++;
            int gold2 = s.Gold;
            bool okFull = s.Shop.TryBuy(3, s, out string whyFull);
            int lv2After = lv2Slot >= 0 ? s.Bag[lv2Slot].Level : 0;

            string text = "PROBE_SHOP_SEED_BUY " + d.Id + "｜波次 " + wave
                + $"｜1级价 {lv1}｜2级价 {p2}（成本 {c2}，成本×{p2 / (float)c2:0.000}）"
                + $"｜3级价 {p3}（成本 {c3}，成本×{p3 / (float)c3:0.000}）\n"
                + $"  ② 买 2 级：TryBuy={(ok2 ? "成功" : "失败（" + why2 + "）")}｜金币 {gold0}→{gold1}"
                + $"（扣 {gold0 - gold1}，正是 2 级价 {p2}）｜袋槽 {bagSlot} = **Lv{bagLevel}**"
                + $"｜种下的地块等级 = **Lv{plotLevel}**\n"
                + $"  ③ 满袋再买一张 2 级：TryBuy={(okFull ? "成功" : "失败（" + whyFull + "）")}"
                + $"｜金币 {gold2}→{s.Gold}｜2 级槽数 {lv2BeforeCnt}→"
                + $"{CountBagLevel(s, 2)}｜槽 {lv2Slot} 的等级 Lv2→**Lv{lv2After}**（说明它并进了 2 级槽，没并进 1 级槽）";
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>袋里某等级的槽数（探针读数用，只读）。</summary>
        private static int CountBagLevel(RunSession s, int level)
        {
            int n = 0;
            for (int i = 0; i < s.Bag.SlotCount; i++)
                if (!s.Bag[i].IsEmpty && s.Bag[i].Level == level) n++;
            return n;
        }

        /// <summary>某条插件通道在聚合结果里的当前值（只读，用于"装前/装后"读数）。</summary>
        private static float PluginChannelValue(in WeaponModifiers m, PluginEffectKind kind)
        {
            switch (kind)
            {
                case PluginEffectKind.SwordWave: return m.SwordWaveCount;
                case PluginEffectKind.BurnOnHit: return m.BurnLayersPerHit;
                case PluginEffectKind.DoubleStrike: return m.ExtraSwings;
                case PluginEffectKind.Lifesteal: return m.LifestealPct;
                case PluginEffectKind.MultiJet: return m.ExtraJets;
                case PluginEffectKind.SlowOnHit: return m.SlowLayersPerHit;
                case PluginEffectKind.ThickJet: return m.BeamWidthPct;
                case PluginEffectKind.AutoCharge: return m.AutoChargePerSecond;
                case PluginEffectKind.SubBurst: return m.SubBurstCount;
                case PluginEffectKind.PoisonZone: return m.PoisonLayers;
                case PluginEffectKind.FreeBurst: return m.FreeBurstChance;
                case PluginEffectKind.ChainBurst: return m.ChainBurstCount;
                default: return 0f;
            }
        }

        /// <summary>
        /// ② **暴击率面板值与实测值**：模拟"抽到 k 张暴击率卡"，打印属性面板那一行
        /// **真正会显示成什么**（直接调 `ImGuiHud.AddRelative`，不另算一份）+ N 次挥击的实测暴击率。
        /// spec = 卡片张数（默认 4）。
        ///
        /// ★ 这个探针是用来钉住"+240%"那条读数的 ★
        /// 4 张卡 × 3 点 = +0.12 ⇒ 真值 5%→17%；而面板用「相对涨幅」显示 ⇒ (0.17/0.05−1)=**+240%** ✓
        /// 两个数都不算错，**是同一个数的两种单位** —— 面板选了让玩家误解的那一种 ✗
        /// </summary>
        public string ProbeCritPanel(string spec)
        {
            int picks = Mathf.Clamp(ParseInt(spec, 4), 0, 20);
            TuningDef t = TableDatabase.Tuning;
            // 卡片数值从表里读（与 `RunSession.PickRewardCard` 同一个来源，不写死 3）
            float perCard = 0.03f;
            for (int i = 0; i < t.levelReward.stats.Length; i++)
                if (t.levelReward.stats[i].id == "CritRate")
                { perCard = t.levelReward.stats[i].amount / 100f; break; }

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            StatBlock st = s.Progress.Stats;
            for (int i = 0; i < picks; i++)
                st.AddModifier(new StatModifier(StatId.CritRate, perCard, ModOp.Add, "probe:CritRate"));

            float cur = st.Get(StatId.CritRate);
            string panel = ImGuiHud.AddRelative(cur, t.player.critRate);
            string points = ImGuiHud.AddPoints(cur, t.player.critRate);

            s.World.SpawnEnemy("grunt", new Vector2(1.2f, 0f), 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = new Vector2(1.2f, 0f);
            e.Health.Setup(1e9f, 0f);

            const int N = 1000;
            int crit = 0, popups = 0;
            System.Action<FloatingTextEvent> onPopup = ev =>
            {
                if (ev.Kind != PopupKind.Crit && ev.Kind != PopupKind.DamageToEnemy) return;
                popups++;
                if (ev.Kind == PopupKind.Crit) crit++;
            };
            Bus.Subscribe(onPopup);
            try
            {
                for (int i = 0; i < N; i++)
                {
                    e.Pos = new Vector2(1.2f, 0f);
                    p.Pos = Vector2.zero;
                    p.AimDir = Vector2.right;
                    p.SwitchWindup = 0f;
                    for (int k = 0; k < p.WeaponCds.Length; k++) p.WeaponCds[k] = 0f;
                    s.DebugAttackOnce(WeaponSlot.Hoe);
                }
            }
            finally { Bus.Unsubscribe(onPopup); }

            float measured = popups > 0 ? (float)crit / popups : 0f;
            string text = $"PROBE_CRITPANEL 暴击率卡 ×{picks}（每张 +{perCard:0.###}，共 +{picks * perCard:0.###}）"
                        + $"｜基础 {t.player.critRate:0.###}｜当前 StatBlock={cur:0.###}（{cur * 100f:0.#}%）"
                        + $"｜属性面板 AddRelative={panel}｜按百分点 AddPoints={points}"
                        + $"｜实测 {crit}/{popups}={measured:P2}（N={N}）｜期望 {cur:P2}";
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ④⑦ **护盾 5 秒 / 升级 +1HP & 护盾 +0 / 自充每秒 / 雨滴概率** 四段读数（只读）。
        /// </summary>
        public string ProbeRegen()
        {
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            sb.Append("PROBE_REGEN\n");

            // ── ① 护盾自动恢复：受击后多少秒开始回 ──
            {
                RunSession s = new RunSession();
                s.Start(20240607UL);
                s.StartWave(1);
                PlayerActor p = s.World.Player;
                s.World.DealDamage(new DamageRequest
                { Type = DamageType.Physical, RawValue = 1f, Target = p, IgnoreDodge = true });
                p.Health.Shield = 0f;
                float delay = -1f;
                for (int i = 0; i < 600; i++)        // 0.05s × 600 = 30s
                {
                    p.Tick(0.05f, s.World);
                    if (p.Health.Shield > 0.0001f) { delay = (i + 1) * 0.05f; break; }
                }
                sb.Append("  ① 护盾自动恢复：配置 shieldRegenDelay=").Append(t.player.shieldRegenDelay.ToString("0.##"))
                  .Append("s｜实测开始回盾于 ").Append(delay < 0f ? "30s 内没回" : delay.ToString("0.00") + "s")
                  .Append("（步长 0.05s）｜每秒速率 = max(MaxShield×").Append(t.player.shieldRegenPct.ToString("0.##"))
                  .Append(", ").Append(t.player.shieldRegenMin.ToString("0.##")).Append(") = ")
                  .Append(Mathf.Max(p.Health.MaxShield * t.player.shieldRegenPct, t.player.shieldRegenMin).ToString("0.##"))
                  .Append("/秒\n");
                s.Dispose();
            }

            // ── ② 升级：每级 MaxHp / MaxShield 的增量 ──
            {
                RunSession s = new RunSession();
                s.Start(20240607UL);
                s.StartWave(1);
                PlayerProgress g = s.Progress;
                float hp0 = g.Stats.Get(StatId.MaxHp), sh0 = g.Stats.Get(StatId.Shield);
                int lv0 = g.Level;
                g.AddExp(1e6f, out int gained);
                float hp1 = g.Stats.Get(StatId.MaxHp), sh1 = g.Stats.Get(StatId.Shield);
                float perHp = gained > 0 ? (hp1 - hp0) / gained : 0f;
                float perSh = gained > 0 ? (sh1 - sh0) / gained : 0f;
                sb.Append("  ② 升级：配置 hpPerLevel=").Append(t.growth.hpPerLevel.ToString("0.##"))
                  .Append("／shieldPerLevel=").Append(t.growth.shieldPerLevel.ToString("0.##"))
                  .Append("｜Lv").Append(lv0).Append("→").Append(g.Level).Append("（+").Append(gained).Append(" 级）")
                  .Append("｜MaxHp ").Append(hp0.ToString("0.##")).Append("→").Append(hp1.ToString("0.##"))
                  .Append("（每级 +").Append(perHp.ToString("0.##")).Append("）")
                  .Append("｜MaxShield ").Append(sh0.ToString("0.##")).Append("→").Append(sh1.ToString("0.##"))
                  .Append("（每级 +").Append(perSh.ToString("0.##")).Append("）\n");
                s.Dispose();
            }

            // ── ③ 自充：装一件 can_autocharge，跑 2 秒看水涨了多少 ──
            {
                PluginAsset ac = null;
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                    if (TableDatabase.Plugins[i] != null && TableDatabase.Plugins[i].id == "can_autocharge")
                    { ac = TableDatabase.Plugins[i]; break; }
                RunSession s = new RunSession();
                s.Start(20240607UL);
                s.StartWave(1);
                s.SetPhasePublic(PhaseState.Combat);
                float rateCfg = 0f;
                if (ac != null && WeaponSystem.Install(s.Goods, ac, WeaponSlot.Can, out _))
                    rateCfg = s.ModsFor(WeaponSlot.Can).AutoChargePerSecond;
                s.WaterAmmo = 0;
                const float secs = 2f;
                int frames = Mathf.RoundToInt(secs / (1f / 60f));
                for (int i = 0; i < frames; i++) s.FixedTick(1f / 60f, default);
                sb.Append("  ③ 自充：表内 amount=").Append(ac != null ? ac.amount.ToString("0.##") : "(缺行)")
                  .Append("/层｜聚合 AutoChargePerSecond=").Append(rateCfg.ToString("0.##")).Append("/秒")
                  .Append("｜水壶装入后 WaterAmmo 0→").Append(s.WaterAmmo)
                  .Append("（实测 ").Append(secs.ToString("0.#")).Append(" 秒回 ")
                  .Append(s.WaterAmmo).Append(" 点 ⇒ ").Append((s.WaterAmmo / secs).ToString("0.##")).Append("/秒）\n");
                s.Dispose();
            }

            // ── ④ 雨滴：击杀掉落概率（常数 + 400 次击杀的实测）──
            {
                RunSession s = new RunSession();
                s.Start(20240607UL);
                s.StartWave(1);
                s.World.Enemies.Clear();
                int drops = 0;
                System.Action<RainDropEvent> onRain = _ => drops++;
                Bus.Subscribe(onRain);
                const int kills = 400;
                try
                {
                    for (int i = 0; i < kills; i++)
                    {
                        s.World.Enemies.Clear();
                        s.World.SpawnEnemy("grunt", new Vector2(3f, 0f), 1);
                        EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
                        e.Health.Setup(1f, 0f);
                        s.World.DealDamage(new DamageRequest
                        { Type = DamageType.Physical, RawValue = 99f, Source = s.World.Player, Target = e, IgnoreDodge = true });
                        // ★ 掉落掷骰在 `World.Cleanup()` 里（`Simulate` 的第 7 步），不在 `DealDamage` ★
                        //   不推一帧的话：怪死了、却没被清出场 ⇒ 一次雨滴判定都不会发生 ⇒ 读数恒为 0 ✗
                        s.World.Simulate(1f / 60f);
                    }
                }
                finally { Bus.Unsubscribe(onRain); }
                sb.Append("  ④ 雨滴：RainDropChancePerKill=").Append(World.RainDropChancePerKill.ToString("0.00"))
                  .Append("｜实测 ").Append(kills).Append(" 次击杀掉 ").Append(drops)
                  .Append(" 滴 = ").Append(((float)drops / kills).ToString("0.000")).Append('\n');
                s.Dispose();
            }

            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ★ v188：**持续效果表现 / 弹字图标 / 蘑菇云** 的验收探针 ★
        //
        //  为什么不写进 `SelfCheck`：断言分母被钉在 **439** 条，加一条就是基线漂移 ✗
        //  而这三条要的证据是"**画面上真的有**"＋"层数变了密度就变"，
        //  那不是一句 PASS/FAIL 能表达的 ⇒ 一条探针 + 一张图 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// v188 ① ② ③：**在活会话里搭出"带层数的敌人 + 一块毒区"**，并打印表现层读数。
        ///
        /// spec：`burn:1,burn:3,slow:1,slow:5,poison:2[,dist:2.6]`
        ///   · 同一个键写几次就造几只敌人（按出现顺序给层数）
        ///   ⇒ `burn:1,burn:3` 就是"**1 层 vs 3 层**"的同框对照 ✓
        ///
        /// ★ 为什么用**活会话** `S` 而不是 `new RunSession()` ★
        ///   本条要求的是"看得见"；新建的会话不参与渲染（`WorldView` 只跟 `Boot.Session` 走）
        ///   ⇒ 只有活会话能拍图 ✓
        /// ★ 它改的是**局内实体**（敌人 / 持续效果表），不改任何插件或数值配置 ★
        /// </summary>
        public string ProbeDotFx(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话（先 play 并进入 Combat）";

            float dist = 2.6f;
            float side = 0f;      // 整排横向偏移（避开玩家模型的遮挡）
            float span = 2.5f;    // 相邻两只敌人的间距
            var done = new System.Collections.Generic.List<string>();
            var burnStacks = new System.Collections.Generic.List<int>();
            var slowStacks = new System.Collections.Generic.List<int>();
            int poisonLayers = 0;
            string[] parts = (spec ?? "").Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;
                int c = part.IndexOf(':');
                if (c < 0) continue;
                string k = part.Substring(0, c).Trim().ToLowerInvariant();
                float v = ParseFloat(part.Substring(c + 1), 1f);
                if (k == "burn") burnStacks.Add(Mathf.Clamp(Mathf.RoundToInt(v), 1, PluginDotTable.BurnMaxStacks));
                else if (k == "slow") slowStacks.Add(Mathf.Clamp(Mathf.RoundToInt(v), 1, PluginDotTable.SlowMaxStacks));
                else if (k == "poison") poisonLayers = Mathf.Max(1, Mathf.RoundToInt(v));
                else if (k == "dist") dist = Mathf.Max(0.5f, v);
                // ★ v188：整体横向偏移（+ = 世界 +X）。
                //   为什么必须有：特写机位 `BattleView` 的焦点是**玩家**，
                //   敌人摆在正中会被玩家模型的头挡住 ✗ 加一个 `side` 就能把整排挪出遮挡 ✓
                else if (k == "side") side = v;
                else if (k == "span") span = Mathf.Max(0.4f, v);
            }

            World w = s.World;
            PlayerActor p = w.Player;
            // ★ 站在原点、朝 +Z：机位与敌人的相对位置从此可算（而不是"看运气"）★
            p.Pos = Vector2.zero;
            p.MoveInput = Vector2.zero;
            p.AimDir = Vector2.up;

            w.Enemies.Clear();
            s.Dots.Clear();

            int n = burnStacks.Count + slowStacks.Count;
            float rowZ = dist;                     // 敌人这一排离玩家多远（默认 2.6m：特写机位能同时装下几只）
            int idx = 0;
            for (int i = 0; i < burnStacks.Count; i++, idx++)
            {
                Vector2 at = EnemySlot(idx, n, rowZ, span, side);
                w.SpawnEnemy("grunt", at, Mathf.Max(1, s.WaveIndex));
                EnemyActor e = w.Enemies[w.Enemies.Count - 1];
                e.Pos = at;
                e.Health.Setup(1e6f, 0f);            // 打不死：这一屏只看表现，不谈击杀
                s.Dots.ApplyBurn(e.Id, 6f, burnStacks[i]);
                done.Add($"灼烧#{e.Id}×{burnStacks[i]}层");
            }
            for (int i = 0; i < slowStacks.Count; i++, idx++)
            {
                Vector2 at = EnemySlot(idx, n, rowZ, span, side);
                w.SpawnEnemy("grunt", at, Mathf.Max(1, s.WaveIndex));
                EnemyActor e = w.Enemies[w.Enemies.Count - 1];
                e.Pos = at;
                e.Health.Setup(1e6f, 0f);
                s.Dots.ApplySlow(e.Id, 4f, 0.12f, slowStacks[i]);
                done.Add($"减速#{e.Id}×{slowStacks[i]}层");
            }
            if (poisonLayers > 0)
            {
                // 毒区放在敌人**后面**：贴地的盘要单独看得见，压在人脚下会读不清
                var zc = new Vector2(0f, rowZ + 2.4f);
                s.Dots.AddPoisonZone(zc, 2.4f, 5f, poisonLayers);
                done.Add($"毒区@({zc.x:0.0},{zc.y:0.0})r2.40×{poisonLayers}层");
            }
            s.Dots.SyncSlow(w);

            string table = s.Dots.Describe();
            string fx = Boot != null && Boot.View != null ? Boot.View.DotFxReport() : "(无 WorldView)";
            string text = "PROBE_DOTFX 造出 " + (done.Count == 0 ? "（空）" : string.Join("｜", done))
                        + "\n  表：" + table
                        + "\n  表现：" + fx
                        + $"\n  层数→箭头（规则）：1层={WorldView.SlowArrowCount(1)} 2层={WorldView.SlowArrowCount(2)}"
                        + $" 3层={WorldView.SlowArrowCount(3)} 4层={WorldView.SlowArrowCount(4)} 5层={WorldView.SlowArrowCount(5)}"
                        + "（上限 " + WorldView.SlowArrowMax + "）"
                        + "\n  层数→火星率（规则）：" + BurnRateLine();
            Log.Info(text);
            return text;
        }

        /// <summary>v188：把"几层 → 每秒几颗火星"的式子打成一行（与 `SyncBurnSparks` 同一条）。</summary>
        private static string BurnRateLine()
        {
            var sb = new StringBuilder();
            for (int k = 1; k <= PluginDotTable.BurnMaxStacks; k++)
            {
                if (sb.Length > 0) sb.Append("｜");
                sb.Append(k).Append("层=").Append(WorldView.BurnSparkRate(k).ToString("0")).Append("颗/秒");
            }
            return sb.ToString();
        }

        /// <summary>v188：只读——直接读表现层的持续效果读数（搭场景用 `ProbeDotFx`）。</summary>
        public string DotFxStatus()
        {
            string text = Boot != null && Boot.View != null ? Boot.View.DotFxReport() : "无视图";
            Log.Info(text);
            return text;
        }

        /// <summary>把第 <paramref name="i"/> 只敌人排在玩家正前方的一排里（正中对称 + 整体偏移）。</summary>
        private static Vector2 EnemySlot(int i, int n, float rowZ, float span, float side)
        {
            float x = (i - (n - 1) * 0.5f) * span + side;
            return new Vector2(x, rowZ);
        }

        /// <summary>
        /// v188 ④：**三种持续伤害各弹一次**，并把真正上屏的 `Kind / 颜色 / 图标` 打出来。
        ///
        /// 走的是**真路**：`PluginDotTable.Tick` → `Hit` → `World.DealDamage` →
        /// `Actor.OnHitLanded` → `Bus.Publish(FloatingTextEvent)` ⇒ 这里抓到的就是玩家会看到的 ✓
        /// （不是自己拼一个 `FloatingTextEvent` —— 那样验的是"我能不能画"，不是"游戏会不会这样画"）
        /// </summary>
        public string ProbeDotPopups()
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话（先 play）";

            World w = s.World;
            w.Enemies.Clear();
            s.Dots.Clear();
            w.Player.Pos = Vector2.zero;
            w.Player.AimDir = Vector2.up;

            // 三只并排：灼烧 / 减速 / 毒区各一只（毒区行不认敌人，但被它打中的是站在块里的那只）
            // ★ v188：三只**必须拉开**（第一版 ±2.6m，弹字横向只有约 110 像素间距 ⇒ 三条字叠在一起，
            //   图上看不清"哪个图标配哪条字"✗）⇒ ±5.6m + 纵深错开，三条字在屏幕上分得开 ✓
            Vector2[] at = { new Vector2(-5.6f, 3.0f), new Vector2(0f, 4.4f), new Vector2(5.6f, 3.0f) };
            int[] ids = new int[3];
            for (int i = 0; i < 3; i++)
            {
                w.SpawnEnemy("grunt", at[i], Mathf.Max(1, s.WaveIndex));
                EnemyActor e = w.Enemies[w.Enemies.Count - 1];
                e.Pos = at[i];
                e.Health.Setup(1e6f, 0f);
                ids[i] = e.Id;
            }
            s.Dots.ApplyBurn(ids[0], 6f, 3);                 // 3 层灼烧 → 18/秒
            s.Dots.ApplySlow(ids[1], 4f, 0.12f, 5);          // 5 层减速 → 20/秒
            s.Dots.AddPoisonZone(at[2], 2.0f, 5f, 2);        // 2 层毒区 → 10/秒
            s.Dots.SyncSlow(w);

            var seen = new StringBuilder();
            int hits = 0;
            System.Action<FloatingTextEvent> onPopup = ev =>
            {
                if (ev.Kind != PopupKind.DamageBurn && ev.Kind != PopupKind.DamageSlow
                    && ev.Kind != PopupKind.DamagePoison) return;
                hits++;
                Color c = PopupStyle.ColorOf(ev.Kind);
                PopupIcon ic = PopupStyle.IconOf(ev.Kind);
                seen.Append("  ").Append(ev.Kind).Append(" \"").Append(ev.Text).Append('"')
                    .Append(" 色=(").Append(c.r.ToString("0.00")).Append(',').Append(c.g.ToString("0.00"))
                    .Append(',').Append(c.b.ToString("0.00")).Append(')')
                    .Append(" 图标=").Append(IconName(ic))
                    .Append(" 字号×").Append(PopupStyle.ScaleOf(ev.Kind).ToString("0.00"))
                    .Append('\n');
            };
            Bus.Subscribe(onPopup);
            try
            {
                s.Dots.Tick(1f, w, w.Player);     // ★ 满 1 秒 ⇒ 三种持续伤害各结算一次
            }
            finally { Bus.Unsubscribe(onPopup); }

            string text = "PROBE_DOTPOPUP 三种持续伤害各结算一次 ⇒ 弹字 " + hits + " 条：\n" + seen
                        + "  说明：图标走**贴图**（不是 emoji）：🔥💧💀 在 `Microsoft YaHei` 里没有字形，"
                        + "IMGUI 也不认彩色 emoji ⇒ 画出来是豆腐块；\n"
                        + "  这里用 11×11 位掩码生成的小纹理，颜色直接取 `PopupStyle.ColorOf(kind)`（与数字同色）✓";
            Log.Info(text);
            return text;
        }

        /// <summary>v188：图标枚举 → 中文名（给读数用）。</summary>
        private static string IconName(PopupIcon ic)
        {
            switch (ic)
            {
                case PopupIcon.Flame: return "小火苗(Flame)";
                case PopupIcon.Drop: return "水滴(Drop)";
                case PopupIcon.Skull: return "骷髅头(Skull)";
                default: return "无";
            }
        }

        /// <summary>
        /// v188 ⑤：**在玩家前方引爆一次肥料**（可选装上 `fert_subburst` ⇒ 子爆同框），
        /// 并打印蘑菇云五组构件的数量与最长剩余寿命。
        ///
        /// spec = `sub`（装子爆）/ `plain`（不装）/ `dist:4.2`
        /// 为什么要让子爆一起出现：用户原话"**子爆炸同样继承**"——
        /// 那句只能在**同一帧里同时看到主爆与子爆的云**时才算验过 ✓
        /// </summary>
        public string ProbeFertCloud(string spec)
        {
            RunSession s = S;
            if (s == null || s.World == null || s.World.Player == null) return "无会话（先 play）";
            string raw = (spec ?? "").Trim().ToLowerInvariant();
            float dist = 4.2f;
            int dc = raw.IndexOf("dist:", StringComparison.Ordinal);
            if (dc >= 0) dist = ParseFloat(raw.Substring(dc + 5), 4.2f);
            bool sub = !raw.Contains("plain");

            s.World.Player.Pos = Vector2.zero;
            s.World.Player.AimDir = Vector2.up;

            string plug;
            if (sub) plug = PluginFxApply("fert_subburst@2");
            else { s.Goods.Plugins.Clear(); plug = "未装插件（空槽）"; }

            WeaponModifiers pm = s.ModsFor(WeaponSlot.Fertilizer);
            var at = new Vector2(0f, Mathf.Max(1.0f, dist));
            int hit = s.ExplodeAt(at, s.World.Player);

            string fx = Boot != null && Boot.View != null ? Boot.View.DotFxReport() : "(无 WorldView)";
            string text = "PROBE_FERTCLOUD 爆点=(" + at.x.ToString("0.0") + "," + at.y.ToString("0.0") + ")"
                        + "｜子爆层数=" + pm.SubBurstCount + "｜命中 " + hit + " 只"
                        + "\n  " + plug
                        + "\n  表现：" + fx;
            Log.Info(text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v185：**商店种子货架"1/2/3 级种子"的两条只读探针** ★
        //
        // 为什么不写进 SelfCheck：断言条数被钉在 **437** 条，加一条就是基线漂移 ✗
        // 而且这里要的是**一张表**（价 / 成本 / 折扣），不是一句 PASS/FAIL ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ① **价目表**（纯计算，不建局、不改任何状态）：按品质档列出
        ///    1级价 / 2级价 / 2级合成成本 / 折扣 / 3级价 / 3级合成成本 / 折扣。
        /// spec = 波次（省略 = 1）。
        ///
        /// ★ 为什么"合成成本"要在这里重算一遍，而不是读 `SeedLevelPrice` ★
        /// 折扣的分母必须与**合成规则**同源：2 合 1 ⇒ 成本 = 1级价 × 2^(级−1) ✓
        /// 两者都由 `ShopSystem` 提供（`SeedMergeCost` / `SeedLevelPrice`），本方法只负责排版 ✓
        /// 所以这张表本身就是"定价公式与合成规则是否自洽"的证据 ✓
        /// </summary>
        public string ProbeShopSeedLevels(string spec)
        {
            TableDatabase.Load(true);
            int wave = Mathf.Max(1, ParseInt(spec, 1));
            var sb = new StringBuilder();
            sb.Append("PROBE_SHOP_SEED_LEVELS｜合成规则=2 合 1（SeedBag.Merge）｜arity=")
              .Append(ShopSystem.SeedMergeArity)
              .Append("｜k2=").Append(ShopSystem.SeedLv2Discount.ToString("0.00"))
              .Append("｜k3=").Append(ShopSystem.SeedLv3Discount.ToString("0.00"))
              .Append("｜等级权重 1级/2级/3级 = ")
              .Append(ShopSystem.SeedLevelWeights[1]).Append('/')
              .Append(ShopSystem.SeedLevelWeights[2]).Append('/')
              .Append(ShopSystem.SeedLevelWeights[3])
              .Append("｜波次=").Append(wave).Append('\n');
            sb.Append("  档 | 1级价 | 2级价 | 2级成本 | 2级折扣 | 3级价 | 3级成本 | 3级折扣 | 代表种子\n");
            for (int g = 0; g <= 4; g++)
            {
                SeedDef[] ofGrade = TableDatabase.SeedsOfGrade((SeedGrade)g);
                if (ofGrade == null || ofGrade.Length == 0) continue;
                SeedDef d = ofGrade[0];
                int lv1 = SeedSlot.ShopPriceFrom(d.Price, d.RarityTier, wave);
                int p2 = ShopSystem.SeedLevelPrice(lv1, 2);
                int p3 = ShopSystem.SeedLevelPrice(lv1, 3);
                int c2 = ShopSystem.SeedMergeCost(lv1, 2);
                int c3 = ShopSystem.SeedMergeCost(lv1, 3);
                sb.Append("  ").Append((SeedGrade)g).Append("  | ")
                  .Append(lv1).Append(" | ").Append(p2).Append(" | ").Append(c2)
                  .Append(" | ").Append((p2 / (float)c2).ToString("P1"))
                  .Append(" | ").Append(p3).Append(" | ").Append(c3)
                  .Append(" | ").Append((p3 / (float)c3).ToString("P1"))
                  .Append(" | ").Append(d.Id).Append("（base ").Append(d.Price)
                  .Append(" × 品质 ").Append(TableDatabase.Tuning.quality.PriceMult(d.RarityTier).ToString("0.0"))
                  .Append(" × 波次 ").Append(TableDatabase.Tuning.shop.WavePriceMult(wave).ToString("0.000"))
                  .Append("）\n");
            }
            // 两条硬约束的**自证**（全部档位都要成立，任一条不成立就写在后面）
            int bad = 0; string badWhere = "";
            for (int g = 0; g <= 4; g++)
            {
                SeedDef[] ofGrade = TableDatabase.SeedsOfGrade((SeedGrade)g);
                if (ofGrade == null || ofGrade.Length == 0) continue;
                for (int k = 0; k < ofGrade.Length; k++)
                {
                    int lv1 = SeedSlot.ShopPriceFrom(ofGrade[k].Price, ofGrade[k].RarityTier, wave);
                    int p2 = ShopSystem.SeedLevelPrice(lv1, 2);
                    int p3 = ShopSystem.SeedLevelPrice(lv1, 3);
                    int c2 = ShopSystem.SeedMergeCost(lv1, 2);
                    int c3 = ShopSystem.SeedMergeCost(lv1, 3);
                    if (p2 <= lv1) { bad++; badWhere = ofGrade[k].Id + " 2级价 " + p2 + " ≤ 1级价 " + lv1; }
                    if (p3 <= p2) { bad++; badWhere = ofGrade[k].Id + " 3级价 " + p3 + " ≤ 2级价 " + p2; }
                    if (p2 >= c2) { bad++; badWhere = ofGrade[k].Id + " 2级价 " + p2 + " ≥ 成本 " + c2; }
                    if (p3 >= c3) { bad++; badWhere = ofGrade[k].Id + " 3级价 " + p3 + " ≥ 成本 " + c3; }
                }
            }
            sb.Append("  约束：2级价>1级价 且 3级价>2级价 且 两者都<各自合成成本 ⇒ ")
              .Append(bad == 0 ? "全部 16 个种子成立 ✓" : (bad + " 处不成立：" + badWhere));
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// ② **抓图夹具**：在一批**自然刷新**里搜一个"同时含 1 级 + 2 级 + 3 级种子"的货架，
        ///    把它装进当前局并进店（`PhaseState.Shop`），好让一张截图同时看到三档。
        /// spec = 最多搜多少次刷新（默认 4000）。
        ///
        /// ★ 为什么是"搜自然刷新"而不是"手搓三个 ShopOffer" ★
        /// 手搓的货架只能证明"UI 画得出来"，证明不了"真的掷得出来" ✗
        /// 搜出来的那一个则是**同一条掷法**的产物（等级走 `RollSeedLevel`、
        /// 品质/品种走 `World.Rng`、价格走 `SeedLevelPrice`）⇒ 截图为真实结果 ✓
        /// 报告里同时给出命中率与等级分布，两者一起才是"稀有度真的生效"的证据 ✓
        ///
        /// ⚠️ 它会**改当前局的货架与相位**（进店）—— 只用于抓图/人工验货，不是只读探针 ✗
        /// </summary>
        public string ProbeShopSeedCards(string spec)
        {
            TableDatabase.Load(true);
            RunSession live = S;
            if (live == null) return Fail("会话未启动，装不了货架");
            int wave = Mathf.Max(1, live.WaveIndex);
            int maxTry = Mathf.Clamp(ParseInt(spec, 4000), 1, 20000);

            // ── ① 等级分布实测（隔离局，2000 次刷新）──
            var probe = new RunSession();
            probe.Start(20240607UL);
            probe.StartWave(wave);
            int[] lvHit = new int[4];
            int seedSlots = 0, refrWithHi = 0;
            const int refreshes = 2000;
            for (int r = 0; r < refreshes; r++)
            {
                probe.Shop.RerollCount = r;     // 换一个等级掷法（等价于"又刷了一次货架"）
                probe.Shop.Refresh(probe);
                bool hi = false;
                for (int i = 0; i < probe.Shop.Offers.Count; i++)
                {
                    ShopOffer oo = probe.Shop.Offers[i];
                    if (oo.Kind != OfferKind.Seed) continue;
                    int lv = Mathf.Clamp(oo.Level, 1, 3);
                    lvHit[lv]++; seedSlots++;
                    if (lv > 1) hi = true;
                }
                if (hi) refrWithHi++;
            }

            // ── ② 搜一个"1/2/3 级同时在场"的货架 ──
            var search = new RunSession();
            search.Start(20240607UL);
            search.StartWave(wave);
            int used = -1;
            for (int attempt = 0; attempt < maxTry; attempt++)
            {
                search.Shop.RerollCount = attempt;
                search.Shop.Refresh(search);
                if (HasSeedLevels123(search.Shop.Offers)) { used = attempt + 1; break; }
            }

            var sb = new StringBuilder();
            sb.Append("PROBE_SHOP_SEED_CARDS｜波次=").Append(wave)
              .Append("｜等级权重 ").Append(ShopSystem.SeedLevelWeights[1]).Append('/')
              .Append(ShopSystem.SeedLevelWeights[2]).Append('/').Append(ShopSystem.SeedLevelWeights[3]).Append('\n');
            sb.Append("  ① ").Append(refreshes).Append(" 次自然刷新共 ").Append(seedSlots)
              .Append(" 个种子货位 ⇒ 1级 ").Append(lvHit[1])
              .Append("（").Append(seedSlots > 0 ? (lvHit[1] / (float)seedSlots).ToString("P1") : "n/a").Append("）")
              .Append(" 2级 ").Append(lvHit[2])
              .Append("（").Append(seedSlots > 0 ? (lvHit[2] / (float)seedSlots).ToString("P1") : "n/a").Append("）")
              .Append(" 3级 ").Append(lvHit[3])
              .Append("（").Append(seedSlots > 0 ? (lvHit[3] / (float)seedSlots).ToString("P1") : "n/a").Append("）")
              .Append("｜含 2 级以上种子的刷新 ").Append(refrWithHi)
              .Append("（").Append((refrWithHi / (float)refreshes).ToString("P1")).Append("）\n");

            if (used < 0)
            {
                probe.Dispose(); search.Dispose();
                string none = sb.Append("  ② ").Append(maxTry).Append(" 次内没搜到 1/2/3 级同时在场的货架（未改当前局）").ToString();
                Log.Warn(none);
                return none;
            }

            for (int i = 0; i < search.Shop.Offers.Count; i++)
            {
                ShopOffer oo = search.Shop.Offers[i];
                sb.Append("    第 ").Append(i + 1).Append(" 格 ")
                  .Append(oo.Kind == OfferKind.Seed ? (oo.Seed.Id + " Lv" + oo.Level + " " + oo.Price + " 金")
                        : oo.Kind == OfferKind.Plugin ? ("插件 " + oo.Price + " 金")
                        : oo.Kind == OfferKind.Item ? ("道具 " + oo.Price + " 金") : "空")
                  .Append('\n');
            }
            sb.Append("  ② 第 ").Append(used).Append(" 次刷新出现 ⇒ 装进当前局并进店\n");

            // ── ③ 装进当前局 + 进店（抓图用）──
            live.Shop.Offers.Clear();
            for (int i = 0; i < search.Shop.Offers.Count; i++) live.Shop.Offers.Add(search.Shop.Offers[i]);
            live.SetPhasePublic(PhaseState.Shop);
            sb.Append("  ③ 当前局相位=").Append(live.Phase)
              .Append("｜货位数=").Append(live.Shop.Offers.Count)
              .Append("｜金币=").Append(live.Gold);
            probe.Dispose(); search.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>货架里是否**同时**有 1 级、2 级、3 级种子（`ProbeShopSeedCards` 的判据）。</summary>
        private static bool HasSeedLevels123(System.Collections.Generic.List<ShopOffer> offers)
        {
            bool l1 = false, l2 = false, l3 = false;
            for (int i = 0; i < offers.Count; i++)
            {
                ShopOffer o = offers[i];
                if (o.Kind != OfferKind.Seed) continue;
                if (o.Level <= 1) l1 = true;
                else if (o.Level == 2) l2 = true;
                else l3 = true;
            }
            return l1 && l2 && l3;
        }

        // ════════════════════════════════════════════════════════════════════════
        // v186：本轮验收用的三项**只读探针**
        //
        // ★ 为什么都要"自己造一局"而不是读正在跑的那一局 ★
        // 实机那一局要复现"第 15 分钟 / 46 只敌人"得真跑 15 分钟，而且**得复现得一模一样**
        // 才能对比修前修后 ✗ 而这三件事（持续效果落在自己的结算秒里 / BOSS 第一个 tick /
        // 锁定商品的价格）**都是确定性的**：给同样的输入必然给同样的结果 ✓
        // ⇒ 造一个最小夹具，逐位可复现，修前修后各跑一次就是对比数据 ✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>取栈的前 <paramref name="n"/> 帧（只留 `方法 (文件:行)`，去掉参数与路径噪声）。</summary>
        private static string FirstFrames(string stack, int n)
        {
            if (string.IsNullOrEmpty(stack)) return "（无栈）";
            string[] lines = stack.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = 0; i < lines.Length && shown < n; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0) continue;
                sb.Append("    #").Append(shown).Append(' ').Append(t).Append('\n');
                shown++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// ① **持续效果 Tick 的下标越界复现**。
        ///
        /// 夹具形状 = 实机出事那一刻的形状："表里有多行持续效果，其中一行在这一秒里把目标打死"。
        /// 陪跑行挂在**打不死**的敌人身上（血 1e9）⇒ 它们只会待着不动，把表撑到多行；
        /// 真正的行挂在血 10 的敌人身上、秒伤 999 ⇒ 这一秒必死 ⇒
        /// `Settle` 里 `OnEnemyDied` 会把属于它的行**当场删掉**。
        ///
        /// 修好时期望：不抛异常、行被清掉、`死亡清行` 至少 +1。
        /// </summary>
        public string ProbeDotCrash(string spec)
        {
            int rows = Mathf.Clamp(ParseInt(spec, 1), 1, 8);
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            s.Dots.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;

            for (int i = 0; i < rows - 1; i++)
            {
                s.World.SpawnEnemy("grunt", new Vector2(3f + i, 0f), 1);
                EnemyActor keep = s.World.Enemies[s.World.Enemies.Count - 1];
                keep.Pos = new Vector2(3f + i, 0f);
                keep.Health.Setup(1e9f, 0f);
                s.Dots.ApplyBurn(keep.Id, 1f, 1);
            }
            s.World.SpawnEnemy("grunt", new Vector2(2f, 0f), 1);
            EnemyActor victim = s.World.Enemies[s.World.Enemies.Count - 1];
            victim.Pos = new Vector2(2f, 0f);
            victim.Health.Setup(10f, 0f);
            s.Dots.ApplyBurn(victim.Id, 999f, 1);

            int before = s.Dots.Count;
            string text;
            try
            {
                s.Dots.Tick(1.01f, s.World, p);
                text = $"PROBE_DOTCRASH OK 未抛异常｜行 {before}→{s.Dots.Count}"
                     + $"｜死亡清行={s.Dots.DeathClears}｜到期清行={s.Dots.ExpiredRows}"
                     + $"｜受害人血={victim.Health.Hp:0.##}";
            }
            catch (Exception ex)
            {
                text = $"PROBE_DOTCRASH THREW {ex.GetType().Name}: {ex.Message}"
                     + $"｜行 {before}→{s.Dots.Count}｜死亡清行={s.Dots.DeathClears}"
                     + $"｜受害人血={victim.Health.Hp:0.##}\n" + FirstFrames(ex.StackTrace, 6);
            }
            Log.Info(text);
            s.Dispose();
            return text;
        }

        /// <summary>
        /// ② **BOSS 入场会不会清场**：12 只血 1e9（打不死）的小怪在场，刷一只 BOSS，推进 0.1 秒。
        ///
        /// 判据是二值的：小怪**只能**被"清场"弄死（血 1e9，玩家那点伤害动不了它）⇒
        ///   修前：存活 1（只剩 BOSS）✗
        ///   修后：存活 13（12 小怪 + BOSS）✓
        /// 顺带读入场状态：`入场中` 应在刷出后为 true、推进 1.4 秒后为 false。
        /// </summary>
        public string ProbeBossEntry(string spec)
        {
            int minions = Mathf.Clamp(ParseInt(spec, 12), 1, 40);
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(10);
            s.World.Enemies.Clear();
            for (int i = 0; i < minions; i++)
            {
                float a = (i / (float)minions) * Mathf.PI * 2f;
                Vector2 pos = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8f;
                s.World.SpawnEnemy("grunt", pos, 10);
                EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
                e.Pos = pos;
                e.Health.Setup(1e9f, 0f);
            }
            int before = s.World.Enemies.Count;
            s.World.SpawnEnemy("boss", new Vector2(0f, 6f), 10, 1);
            EnemyActor spawned = s.World.Enemies[s.World.Enemies.Count - 1];
            spawned.Pos = new Vector2(0f, 6f);
            BossEnemy boss = spawned as BossEnemy;
            bool inEntryAtSpawn = boss != null && boss.InEntrance;
            float entryLen = BossEnemy.EntranceSeconds;

            s.World.Simulate(0.1f);
            int aliveAfter = AliveCount(s);
            int projAfter = s.World.Projectiles.Count;
            s.World.Simulate(1.4f);
            int aliveLate = AliveCount(s);
            bool inEntryLate = boss != null && boss.InEntrance;

            string text = $"PROBE_BOSSENTRY 小怪 {minions}（血 1e9，不可被击杀）｜刷 BOSS 前 {before}"
                        + $"｜推进 0.1s 后存活 {aliveAfter}"
                        + $"｜再推进 1.4s 后存活 {aliveLate}"
                        + $"｜入场时长 {entryLen:0.##}s"
                        + $"｜入场中 刷出时={inEntryAtSpawn} 1.5s后={inEntryLate}"
                        + $"｜0.1s 时敌方弹道 {projAfter}";
            Log.Info(text);
            s.Dispose();
            return text;
        }

        private static int AliveCount(RunSession s)
        {
            int n = 0;
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e != null && e.Alive && !e.Health.IsDead) n++;
            }
            return n;
        }

        /// <summary>
        /// ③ **定价链**：三类商品 × 第 1/5/10/15 波的实付价，全部走**唯一的定价入口**
        /// `SeedSlot.ShopPriceFrom`（不在探针里另写一份算式）。
        ///
        /// 另有第二段 —— **锁定商品的价格会不会跟着波次涨**：
        /// 造局 → 第 1 分钟刷货架 → 锁一格 → 跳到第 10 分钟再刷 → 读那一格还是不是第 1 分钟的价。
        /// 这一段是"部分商品价格没有跟随上涨"的直接判据（锁定项原来把旧价原样带进来）。
        /// </summary>
        public string ProbePriceRise(string spec)
        {
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            int[] waves = { 1, 5, 10, 15 };
            sb.Append("PROBE_PRICE 定价链 = 基础价 × 档位倍率 × (1+")
              .Append(t.shop.priceWaveStep.ToString("0.###")).Append(")^(波-1)\n");
            sb.Append("档位倍率：");
            for (int q = 0; q <= 4; q++)
                sb.Append(TierLetters[q]).Append('×').Append(t.quality.PriceMult(q).ToString("0.##")).Append("  ");
            sb.Append('\n').Append("类  档 id".PadRight(30)).Append("base".PadLeft(6));
            for (int w = 0; w < waves.Length; w++) sb.Append(("W" + waves[w]).PadLeft(7));
            sb.Append('\n');

            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                if (it == null) continue;
                int q = Mathf.Clamp((int)it.Grade, 0, 4);
                sb.Append("道具 ").Append(TierLetters[q]).Append(' ').Append(it.Id.PadRight(27))
                  .Append(it.Price.ToString().PadLeft(6));
                for (int w = 0; w < waves.Length; w++)
                    sb.Append(SeedSlot.ShopPriceFrom(it.Price, q, waves[w]).ToString().PadLeft(7));
                sb.Append('\n');
            }
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset pl = TableDatabase.Plugins[i];
                if (pl == null) continue;
                int q = t.quality.Clamp((int)pl.quality);
                sb.Append("插件 ").Append(TierLetters[q]).Append(' ').Append(pl.id.PadRight(27))
                  .Append(pl.basePrice.ToString().PadLeft(6));
                for (int w = 0; w < waves.Length; w++)
                    sb.Append(SeedSlot.ShopPriceFrom(pl.basePrice, q, waves[w]).ToString().PadLeft(7));
                sb.Append('\n');
            }
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                if (d == null) continue;
                int q = Mathf.Clamp(d.RarityTier, 0, 4);
                sb.Append("种子 ").Append(TierLetters[q]).Append(' ').Append(d.Id.PadRight(27))
                  .Append(d.Price.ToString().PadLeft(6));
                for (int w = 0; w < waves.Length; w++)
                    sb.Append(SeedSlot.ShopPriceFrom(d.Price, q, waves[w]).ToString().PadLeft(7));
                sb.Append('\n');
            }

            // ── 第二段：锁定商品的价格是否跟随波次 ──
            //   判据：第 1 分钟锁一格 → 跳到第 10 分钟再刷 → 那一格的价格**必须**等于
            //   `ShopSystem.PriceOf(该货位, 10)`（同一个函数的期望值，不是我在探针里另算一份）
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.Shop.ResetForPhase();
            s.Shop.Refresh(s);
            int lockedAt = -1, priceAt1 = 0;
            if (s.Shop.Offers.Count > 0)
            {
                ShopOffer o = s.Shop.Offers[0];
                o.Locked = true;
                s.Shop.Offers[0] = o;
                lockedAt = 0;
                priceAt1 = o.Price;
                string label = $"{o.Kind}/{o.Title}/Lv{o.Level}/Q{o.Quality}";
                int expect10 = ShopSystem.PriceOf(o, 10);
                s.StartWave(10);
                s.Shop.Refresh(s);
                int priceAt10 = -1;
                for (int i = 0; i < s.Shop.Offers.Count; i++)
                {
                    ShopOffer c = s.Shop.Offers[i];
                    if (!c.Locked || c.Kind != o.Kind || c.Title != o.Title || c.Level != o.Level) continue;
                    priceAt10 = c.Price;
                    break;
                }
                sb.Append("锁定项 ").Append(label)
                  .Append("：第 1 分钟 ").Append(priceAt1).Append(" 金 → 第 10 分钟 ")
                  .Append(priceAt10 < 0 ? "（丢了）" : priceAt10 + " 金")
                  .Append("｜按当前波次应为 ").Append(expect10).Append(" 金")
                  .Append(priceAt10 == expect10 ? " ⇒ 跟随上涨 ✓" : " ⇒ **价格没跟随** ✗")
                  .Append('\n');
            }
            sb.Append("锁定格下标=").Append(lockedAt).Append("｜货位数=").Append(s.Shop.Offers.Count);
            s.Dispose();

            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        // ════════════════════════════════════════════════════════════════════════
        // ★ 本轮四条插件需求（① 多柱扇形+耗水 ② 剑气扇形 ③ 双击间隔 ④ 自充水球）的**实测探针** ★
        //
        // ★ 它为什么必须存在 ★
        // 这四条里有三条改的是**几何**（夹角、横向距离）与**时间**（间隔），
        // 而自检的断言只看"装前 → 装后 有没有变"⇒ 变了不等于"不重叠"、更不等于"0.2 / 攻速" ✗
        // 所以这里把判据本身打出来：**横向距离 vs 弯月宽度**、**夹角**、**实测第二刀时刻**、
        // **每层发几颗水球**。全部是"算出来的数 + 真的跑出来的数"两列并排 ✓
        //
        // spec：
        //   （省略）  = ①②③④ 四张表全打
        //   "balls"   = **特效演示**：把 `DebugFxHold` 调大后投递一轮真实水球事件（抓图用）
        //   "hold:N"  = 只设特效慢放倍率（N 倍），返回当前值
        // ════════════════════════════════════════════════════════════════════════
        public string ProbePluginRework(string spec)
        {
            TableDatabase.Load(true);
            string mode = (spec ?? "").Trim().ToLowerInvariant();
            if (mode.StartsWith("hold:"))
            {
                float h = ParseFloat(mode.Substring(5), 1f);
                WorldView.DebugFxHold = Mathf.Max(0.02f, h);
                string r0 = "FXHOLD WorldView.DebugFxHold = " + WorldView.DebugFxHold.ToString("0.0");
                Log.Info(r0);
                return r0;
            }
            if (mode == "balls" || mode == "splash") return DemoWaterBalls(mode);

            var sb = new StringBuilder();
            RunSession s = PluginReworkBench(20260101UL, out PlayerActor p);
            TuningDef t = s.T;
            ToolDef hoe = t.tools.hoe;
            ToolDef can = t.tools.can;
            StatBlock st = s.Progress.Stats;

            sb.Append("PROBE_PLUGIN_REWORK 基线：锄头 range=").Append(hoe.range.ToString("0.##"))
              .Append("m arc=").Append(hoe.arc.ToString("0.#")).Append("°｜水壶 range=")
              .Append(can.range.ToString("0.##")).Append("m beamWidth=").Append(can.beamWidth.ToString("0.##"))
              .Append("m｜攻击范围属性=").Append(st.Get(StatId.AtkRange).ToString("0.00"))
              .Append("｜远程伤害=").Append(st.Get(StatId.RangedDamage).ToString("0.##"))
              .Append("｜攻速=").Append(p.AttackSpeed.ToString("0.00")).Append('\n');

            // ── ① 水柱：扇形角度 + 射程末端横向距离 vs 柱宽
            {
                float range = can.range * st.Get(StatId.AtkRange);
                float halfW = can.beamWidth > 0f ? can.beamWidth : 1.4f;
                float step = RunSession.BeamFanStepDeg(halfW, range);
                float sep = 2f * range * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);
                sb.Append("① 水柱扇形：相邻夹角 ").Append(step.ToString("0.00")).Append("°（判据 2·d·sin(Δθ/2) ≥ 柱宽）\n");
                sb.Append("   射程末端 d=").Append(range.ToString("0.00")).Append("m ⇒ 横向距离 ")
                  .Append(sep.ToString("0.000")).Append("m vs 柱宽 ").Append((2f * halfW).ToString("0.000"))
                  .Append("m ⇒ ").Append(sep >= 2f * halfW ? "PASS（不重叠）" : "FAIL（仍重叠）")
                  .Append("｜余量 ").Append((sep - 2f * halfW).ToString("0.000")).Append("m\n");
                sb.Append("   每道角度：");
                for (int n = 1; n <= 4; n++)
                {
                    sb.Append(" ").Append(n).Append("道[");
                    for (int b = 0; b < n; b++)
                    {
                        float a = step * (b - (n - 1) * 0.5f);
                        if (b > 0) sb.Append(' ');
                        sb.Append(a.ToString("+0.0;-0.0;0.0")).Append("°");
                    }
                    sb.Append("]");
                }
                sb.Append('\n');
            }

            // ── ② 剑气：相邻夹角 + 参考距离上的横向距离 vs 弯月宽度（判据 PASS/FAIL）
            {
                float range = hoe.range * st.Get(StatId.AtkRange);
                float halfW = RunSession.MeleeBoxHalfWidth(range, hoe.arc);      // 弯月半宽
                float dRef = RunSession.SwordWaveHalfLen(range) + RunSession.SwordWaveDistance(range);
                float step = RunSession.SwordWaveSpreadDeg(range, halfW);
                float sep = 2f * dRef * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad);
                sb.Append("② 剑气扇形：相邻最小夹角 ").Append(step.ToString("0.00"))
                  .Append("° = 2·asin(HalfWidth×1.05 / d_ref)｜HalfWidth=").Append(halfW.ToString("0.000"))
                  .Append("m d_ref=").Append(dRef.ToString("0.000")).Append("m\n");
                sb.Append("   参考距离上横向距离 ").Append(sep.ToString("0.000")).Append("m vs 弯月宽度 ")
                  .Append((2f * halfW).ToString("0.000")).Append("m ⇒ ")
                  .Append(sep >= 2f * halfW ? "PASS（不重叠）" : "FAIL（仍重叠）")
                  .Append("｜余量 ").Append((sep - 2f * halfW).ToString("0.000")).Append("m\n");
                sb.Append("   每条角度（1/2/3 条）：");
                for (int n = 1; n <= 3; n++)
                {
                    sb.Append(" ").Append(n).Append("条[");
                    for (int k = 0; k < n; k++)
                    {
                        float a = step * (k - (n - 1) * 0.5f);
                        if (k > 0) sb.Append(' ');
                        sb.Append(a.ToString("+0.0;-0.0;0.0")).Append("°");
                    }
                    sb.Append("]");
                }
                sb.Append('\n');
            }

            // ── ③ 双击：式子值 + 实测（装 1 层，量"第二刀真的在第几帧落下"）
            {
                sb.Append("③ 双击间隔 = 0.2 / 攻速：");
                float[] grades = { 1f, 1.5f, 2f };
                for (int i = 0; i < grades.Length; i++)
                {
                    sb.Append(" 攻速").Append(grades[i].ToString("0.0")).Append(" ⇒ ")
                      .Append(RunSession.SwingGapFor(grades[i]).ToString("0.000")).Append("s");
                }
                sb.Append('\n');
                sb.Append("   实测（装 1 层 hoe_doublestrike，敌人在 2.0m，逐帧推进）：");
                float[] aspd = { 1f, 1.5f, 2f };
                for (int gi = 0; gi < aspd.Length; gi++)
                {
                    int frames = MeasureSecondSwingFrame(aspd[gi]);
                    sb.Append(" 攻速").Append(aspd[gi].ToString("0.0")).Append(" ⇒ 第 ")
                      .Append(frames).Append(" 帧（").Append((frames / 60f).ToString("0.000")).Append("s）");
                }
                sb.Append('\n');
            }

            // ── ④ 水球：层数 × 发数 × 伤害（真的打在一个敌人身上）
            {
                sb.Append("④ 自充水球（每次耗水）：");
                for (int layer = 1; layer <= 3; layer++)
                {
                    MeasureWaterBalls(layer, out int perPay, out float dmgPerBall, out float dealtTotal, out int spawned,
                                      out float waterSpent);
                    sb.Append("\n   ").Append(layer).Append(" 层 ⇒ 每次 ").Append(perPay)
                      .Append(" 颗｜单颗伤害 ").Append(dmgPerBall.ToString("0.000"))
                      .Append("（= 水壶命中 ").Append((dmgPerBall * 2f).ToString("0.000")).Append(" × 0.50）")
                      .Append("｜一轮实测命中总伤害 ").Append(dealtTotal.ToString("0.000"))
                      .Append("（共发 ").Append(spawned).Append(" 颗）｜本次耗水 ").Append(waterSpent.ToString("0"));
                }
                sb.Append('\n');
            }

            // ── ①-读数：1 秒连射的耗水（层数 0..3，走真的 `TickBeam`）
            {
                sb.Append("① 连射 1 秒的耗水（每道独立扣水，基线已 +10）：");
                for (int layer = 0; layer <= 3; layer++)
                {
                    float used = MeasureBeamWater(layer, 60);
                    sb.Append(" ").Append(layer).Append(" 层（").Append(1 + layer).Append(" 道）⇒ ")
                      .Append(used.ToString("0")).Append(" 点");
                }
                sb.Append("\n   注：水箱上限 ").Append(RunSession.WaterAmmoMax)
                  .Append(" 点 ⇒ 一次扣 ").Append(20).Append(" 点/道；3/4 道一次要 60/80 点，")
                  .Append("1 秒内只付得起 1 次（读数因此低于 1 道），2 道付得起 2 次 ✓\n");
            }

            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>探针用的可控局面：与自检 `PluginBench` 逐字同一套夹具（属性 +2、暴击 0、满水满肥）。</summary>
        private static RunSession PluginReworkBench(ulong seed, out PlayerActor p)
        {
            var s = new RunSession();
            s.Start(seed);
            s.StartWave(1);
            s.World.Enemies.Clear();
            p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);
            const float BenchStat = 2f;
            s.Progress.Stats.AddModifier(new StatModifier(StatId.MeleeDamage, BenchStat, ModOp.Add, "probe"));
            s.Progress.Stats.AddModifier(new StatModifier(StatId.RangedDamage, BenchStat, ModOp.Add, "probe"));
            s.Progress.Stats.AddModifier(new StatModifier(StatId.BlastDamage, BenchStat, ModOp.Add, "probe"));
            s.WaterAmmo = RunSession.WaterAmmoMax;
            s.FertAmmo = RunSession.FertAmmoMax;
            s.AimWorldLast = new Vector2(3f, 0f);
            return s;
        }

        private static EnemyActor ProbeEnemy(RunSession s, Vector2 pos, float hp = 1e5f)
        {
            s.World.SpawnEnemy("grunt", pos, 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = pos;
            e.Health.Setup(hp, 0f);
            return e;
        }

        private static PluginAsset ProbePlugin(string id)
        {
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                if (TableDatabase.Plugins[i] != null && TableDatabase.Plugins[i].id == id)
                    return TableDatabase.Plugins[i];
            return null;
        }

        /// <summary>装 <paramref name="layers"/> 层某插件（走真的 `WeaponSystem.Install`）。</summary>
        private static bool ProbeInstall(RunSession s, string id, WeaponSlot slot, int layers)
        {
            PluginAsset a = ProbePlugin(id);
            if (a == null) return false;
            for (int i = 0; i < layers; i++)
                if (!WeaponSystem.Install(s.Goods, a, slot, out _)) return i > 0;
            return true;
        }

        /// <summary>按住水壶连射 <paramref name="ticks"/> 帧，返回耗掉的水（与自检 `BeamAmmoUsed` 同一个写法）。</summary>
        private static float MeasureBeamWater(int multijetLayers, int ticks)
        {
            RunSession s = PluginReworkBench(777001UL + (ulong)multijetLayers, out PlayerActor p);
            if (multijetLayers > 0) ProbeInstall(s, "can_multijet", WeaponSlot.Can, multijetLayers);
            p.SwitchWeapon(PlayerActor.WeaponCan, s.World, true);
            int start = s.WaterAmmo;
            for (int i = 0; i < ticks; i++)
                s.FixedTick(1f / 60f, new FrameInput
                {
                    Fire = true, AimWorld = new Vector2(5f, 0f),
                    SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
                });
            float used = start - s.WaterAmmo;
            s.Dispose();
            return used;
        }

        /// <summary>
        /// 桥入口：**多柱的每道水耗 / 一次总耗**（走真的 `TickBeam` 扣水，不是复算）。
        ///
        /// ── 为什么不能直接读 `ProbePluginRework` 的 ① ──
        /// 那一行量的是"连射 1 秒耗掉多少"，受**攻速**与**水箱上限**夹着 ⇒ 读不出"每道多少"✗
        /// 这里只推**一帧**（`MeasureBeamWater(layers, 1)`）：起手那一帧 `TickBeam` 必扣一次
        /// （`!_beamFiring` 分支），且本工程没有"每帧回水" ⇒ 这一次的差值 = **一次总耗** ✓
        ///
        /// 判据（用户要求）：每道 = 水壶本体（20）⇒ 1/2/3/4 道 = 20/40/60/80，
        /// 4 道必须 **≤ 水箱上限 100**（改前是 25/道 ⇒ 4 道 100，恰好扣光）✓
        /// </summary>
        public string JetWaterReport()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder(384);
            sb.Append("JETWATER 多柱水耗（一帧起手 = 一次总耗；水箱上限 ")
              .Append(RunSession.WaterAmmoMax).Append(" 点）\n");
            for (int layers = 0; layers <= 3; layers++)
            {
                int jets = 1 + layers;
                float used = MeasureBeamWater(layers, 1);      // 真扣水
                // 交叉核对：同一个数由"唯一实现"的纯函数给出（两者必须相等）
                WeaponModifiers m = WeaponModifiers.None;
                m.ExtraJets = layers;
                int pure = WeaponSystem.BeamWaterCostPerFire(20, m);
                sb.Append("  ").Append(layers).Append(" 层（").Append(jets).Append(" 道）⇒ 一次总耗 ")
                  .Append(used.ToString("0")).Append(" 点｜每道 ").Append((used / jets).ToString("0.##"))
                  .Append(" 点｜纯函数 ").Append(pure).Append(" 点｜")
                  .Append(Mathf.Approximately(used, pure) ? "一致 ✓" : "**不一致** ✗")
                  .Append("｜剩 ").Append(RunSession.WaterAmmoMax - pure).Append(" 点\n");
            }
            sb.Append("  判据：4 道一次 ≤ ").Append(RunSession.WaterAmmoMax).Append(" 点 ⇒ ")
              .Append(WeaponSystem.BeamWaterCostPerFire(20, JetModsForLayers(3)) <= RunSession.WaterAmmoMax
                      ? "PASS" : "FAIL")
              .Append("｜喷一次还剩 ")
              .Append(RunSession.WaterAmmoMax - WeaponSystem.BeamWaterCostPerFire(20, JetModsForLayers(3)))
              .Append(" 点（> 0 ⇒ 不再扣光）");
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>`JetWaterReport` 的小工具：造一个"装了 N 层多柱"的修正器。</summary>
        private static WeaponModifiers JetModsForLayers(int layers)
        {
            WeaponModifiers m = WeaponModifiers.None;
            m.ExtraJets = layers;
            return m;
        }


        /// <summary>装 1 层双击，量"第二刀在第几帧落下"（逐帧推进，看血第一次再掉）。</summary>
        private static int MeasureSecondSwingFrame(float attackSpeed)
        {
            RunSession s = PluginReworkBench(777101UL, out PlayerActor p);
            s.Progress.Stats.SetBase(StatId.AtkSpeed, attackSpeed);
            ProbeInstall(s, "hoe_doublestrike", WeaponSlot.Hoe, 1);
            EnemyActor e = ProbeEnemy(s, new Vector2(2f, 0f));
            float hp0 = e.Health.Hp;
            s.DebugAttackOnce(WeaponSlot.Hoe);
            float hp1 = e.Health.Hp;          // 第 0 刀
            int frame = 0;
            for (int i = 0; i < 90; i++)
            {
                s.FixedTick(1f / 60f, new FrameInput
                {
                    SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
                });
                frame++;
                if (e.Health.Hp < hp1 - 1e-4f) break;
            }
            bool fired = e.Health.Hp < hp1 - 1e-4f;
            Log.Info($"[水球/双击实测] 攻速 {attackSpeed:0.0}：第 0 刀 −{hp0 - hp1:0.000}｜第二刀"
                     + (fired ? $"第 {frame} 帧（{frame / 60f:0.000}s）−{hp1 - e.Health.Hp:0.000}" : "没落下"));
            s.Dispose();
            return fired ? frame : -1;
        }

        /// <summary>
        /// 装 <paramref name="layers"/> 层自充，**只发一轮**（第一次扣水那一下），量
        /// "每次耗水发几颗 / 单颗伤害 / 这一轮打在敌人身上的总伤害"。
        ///
        /// ★ 为什么只发一轮 ★
        /// 第 1、2 轮是同一秒内的两次扣水，它们的球会在飞行中交错 ⇒
        /// "这一轮打了多少"就算不清了（第一版探针就是在这里读到 5.0 / 22.0 这种对不上的数）✗
        /// 只发一轮 ⇒ 敌人掉的血**只能**来自这一轮，读数与 发数 × 单颗 可直接对上 ✓
        ///
        /// ★ 敌人放在**玩家背后**（−4,0），瞄准朝 +X ⇒ 激光打不到它 ★
        /// 否则读数里混进激光本体伤害，"水球打了多少"就分不出来 ✓
        /// </summary>
        private static void MeasureWaterBalls(int layers, out int perPay, out float dmgPerBall,
                                              out float dealt, out int spawned, out float waterSpent)
        {
            RunSession s = PluginReworkBench(777201UL + (ulong)layers, out PlayerActor p);
            ProbeInstall(s, "can_autocharge", WeaponSlot.Can, layers);
            EnemyActor e = ProbeEnemy(s, new Vector2(-4f, 0f));
            p.SwitchWeapon(PlayerActor.WeaponCan, s.World, true);
            int water0 = s.WaterAmmo;
            int before = s.WaterBallSpawnedTotal;
            float hp0 = e.Health.Hp;                 // ★ 在**开火之前**取样（否则第一轮已经打完了）
            // 第一帧 = 起手那一次扣水 ⇒ 应当正好发 层数×2 颗
            s.FixedTick(1f / 60f, new FrameInput
            {
                Fire = true, AimWorld = new Vector2(8f, 0f),
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
            });
            perPay = s.WaterBallSpawnedTotal - before;
            dmgPerBall = s.WaterBallDmgAt(0);
            waterSpent = water0 - s.WaterAmmo;
            // 松手，让这一轮飞完（4m / 14m/s ≈ 0.29s；给 1 秒）
            for (int i = 0; i < 60; i++)
                s.FixedTick(1f / 60f, new FrameInput
                {
                    SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
                });
            spawned = s.WaterBallSpawnedTotal - before;
            dealt = hp0 - e.Health.Hp;
            Log.Info($"[水球实测] {layers} 层：每次发 {perPay} 颗｜单颗 {dmgPerBall:0.000}｜本轮共发 {spawned} 颗"
                     + $"｜命中总伤害 {dealt:0.000}（应 = {perPay} × {dmgPerBall:0.000} = {perPay * dmgPerBall:0.000}）"
                     + $"｜本次耗水 {waterSpent:0}");
            s.Dispose();
        }

        /// <summary>
        /// **水球特效演示**（抓图用）：把 `DebugFxHold` 调大后投递一套**真实**的水球事件。
        /// ★ 事件与参数和 `RunSession.FireWaterBalls` 发出的**逐字相同**（同样的速度/半径/外观）★
        /// ⇒ 抓到的图就是玩游戏时会看到的东西，不是另画一套 ✓
        ///
        /// spec：
        ///   "balls"  = 只投**起飞**（6 颗扇形、落点距离 3.5~7.4m 错开）⇒ 拍"它们在空中"✓
        ///   "splash" = 只投**落点溅射**（同一个扇形、同样 6 个落点）⇒ 拍"打中时"✓
        /// ★ 分两次是刻意的 ★：一次全投的话溅射环会先亮、球还没飞出去图就"炸完了" ✗
        /// </summary>
        private string DemoWaterBalls(string spec)
        {
            RunSession s = S;
            Vector2 origin = s != null && s.World != null && s.World.Player != null
                ? s.World.Player.Pos : Vector2.zero;
            // 外观取"装了自充"的那一份（水绿霓虹边 + 更亮），与真实发射一致
            WeaponModifiers m = WeaponModifiers.None;
            m.SourceCount = 1;
            m.AutoChargeLayers = 3;
            m.AutoChargePerSecond = 12f;
            FxSpec fx = PluginFx.For(m, WeaponSlot.Can);

            bool splashOnly = (spec ?? "").Trim().ToLowerInvariant() == "splash";
            WorldView.DebugFxHold = 6f;
            const int count = 6;
            float[] dists = { 4.0f, 5.0f, 6.0f, 7.0f, 8.0f, 9.0f };
            float[] angs = { -55f, -33f, -11f, 11f, 33f, 55f };
            for (int i = 0; i < count; i++)
            {
                float a = angs[i] * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (!splashOnly)
                    Bus.Publish(new WaterBallEvent
                    {
                        From = origin, Dir = dir, Dist = dists[i], Speed = RunSession.WaterBallSpeed,
                        Index = i, Count = count, Fx = fx
                    });
                else
                    Bus.Publish(new WaterBallHitEvent { Pos = origin + dir * dists[i], Damage = 2.5f, Fx = fx });
            }
            string text = "BALLDEMO(" + (splashOnly ? "splash" : "balls") + ") " + count
                        + " 颗｜Dist 4~9m｜扇形 ±55°｜Speed=" + RunSession.WaterBallSpeed.ToString("0.#") + "m/s｜半径="
                        + RunSession.WaterBallRadius.ToString("0.00") + "m｜DebugFxHold="
                        + WorldView.DebugFxHold.ToString("0.0") + "｜起点 " + origin.x.ToString("0.00") + ","
                        + origin.y.ToString("0.00") + "｜外观 " + PluginFx.Describe(fx);
            Log.Info(text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v189：**时序路探针**（整点跨分 / 短按输入 / 抽卡门控 / BOSS 清敌 / 充能速率）★
        //
        // 全部**只读**：建临时 RunSession、用固定步驱动、把读数排成表 ⇒ 不改任何玩法规则 ✓
        // 为什么单独一组（而不是塞进 `SelfCheck`）：
        //   `SelfCheck` 的断言是"规则对不对"，而这里要回答的是"**跨过整分钟那一帧，
        //   哪几个数变了**" —— 这是一个**前后对照**的问题，只有把两帧并排才能看出来 ✗
        //   把它做成断言就退化成"某天红了但看不出差多少" ✓
        //
        // 驱动方式与真人一致：`ConsumeFrameEdges` → `FixedTick(1/60)`（与 `GameBootstrap.Update`
        // 同一条路），所以读到的就是玩家会遇到的东西 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>跨分前后要逐项比对的那几个量（缺一个都会漏掉一类症状）。</summary>
        private struct MinuteSnap
        {
            public int Wave;            // 实际回合（RunSession.WaveIndex）
            public int ClockWave;       // 时钟派生的分钟（Clock.WaveIndex）
            public float HoeCd;         // 锄头冷却 = "攻速/攻击间隔"的唯一可读量 ⇒ 攻击是否被重置
            public int Water;           // 水壶充能
            public int Fert;            // 肥料充能
            public int Enemies;         // 场上敌人数
            public int Pending;         // 待处理效果 = 前摇肥料 + 双击刀 + 剑气 + 水球
            public int Despawn;         // `DespawnAllEnemies` 被调用次数（v186 探针）
            public int Kills;           // 累计击杀
            public int Phase;           // PhaseState 的整数值
            public int LiveIds;         // 场上存活敌人 id 的个数（用来算"跨分瞬间消失"）
        }

        private static MinuteSnap SnapOf(RunSession s)
        {
            MinuteSnap n = new MinuteSnap();
            n.Wave = s.WaveIndex;
            n.ClockWave = s.Clock.WaveIndex;
            n.HoeCd = s.World.Player.WeaponCds[PlayerActor.WeaponHoe];
            n.Water = s.WaterAmmo;
            n.Fert = s.FertAmmo;
            n.Enemies = s.World.Enemies.Count;
            n.Pending = s.PendingBlastCount + s.PendingSwingCount
                        + s.LiveSwordWaveCount + s.LiveWaterBallCount;
            n.Despawn = s.World.DespawnAllCalls;
            n.Kills = s.Kills;
            n.Phase = (int)s.Phase;
            n.LiveIds = AliveIdsOf(s).Count;
            return n;
        }

        private static System.Collections.Generic.HashSet<int> AliveIdsOf(RunSession s)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e != null && e.Alive) set.Add(e.Id);
            }
            return set;
        }

        /// <summary>一次跨分事件的读数行。</summary>
        private sealed class MinuteRow
        {
            public int FromWave, ToWave;
            public MinuteSnap Pre, Post;
            public int Vanished;        // 跨分那一帧**凭空消失**的敌人（清场的直接证据）
            public int KilledInTick;    // 同一帧走死亡路径的数量（不是清场，是打死）
            public float Dt;
        }

        /// <summary>
        /// **① 整点跨分探针**：跑到第 N 分钟，打印**每一次跨分前后**的五组读数 ＋ 清场判定。
        ///
        /// spec：分钟数（默认 3）。
        /// 满水那一趟（`WaterAmmo = 满`）用来判"逐项相同"；半水那一趟用来确认
        /// "跨分只该有 +50 水"这一个**设计内**的变化（别的任何变化都是异常）。
        /// 读数全部来自公开字段，不新增任何状态 ✓
        /// </summary>
        public string ProbeMinute(string spec)
        {
            int minutes = Mathf.Clamp(ParseInt(spec, 3), 1, 6);
            StringBuilder sb = new StringBuilder();
            sb.Append("PROBE_MINUTE 跑到第 ").Append(minutes).Append(" 分钟｜固定步 ")
              .Append((GameClock.FixedDt * 1000f).ToString("0.0")).Append("ms\n");
            sb.Append(MinutePass("满水（100）", minutes, RunSession.WaterAmmoMax));
            sb.Append(MinutePass("半水（30）", minutes, 30));
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        private string MinutePass(string label, int minutes, int waterStart)
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);              // 不死：本探针只关心时序，不关心生存
            KeepAlive(p);                          // ★ 每步补满才是真的不死（Setup 会被 StatBlock 覆盖）
            s.WaterAmmo = waterStart;
            s.FertAmmo = 5;                        // 非零非满 ⇒ 任何清零/重置都会露出来

            // 靶子放在中心区（车走外圈、半径 ≠ 中心）⇒ 不会被商店车清扫掉，
            // 于是"敌人变少"只可能来自跨分逻辑本身 ✓
            s.World.SpawnEnemy("grunt", new Vector2(1.2f, 0f), 1);
            EnemyActor dummy = s.World.Enemies[s.World.Enemies.Count - 1];
            dummy.Health.Setup(1e5f, 0f);
            dummy.Pos = new Vector2(1.2f, 0f);

            int totalTicks = Mathf.RoundToInt(minutes * 60f / GameClock.FixedDt);
            int lastWave = s.WaveIndex;
            System.Collections.Generic.List<MinuteRow> rows = new System.Collections.Generic.List<MinuteRow>();
            System.Collections.Generic.HashSet<int> preIds = null;
            MinuteSnap pre = SnapOf(s);
            int preKills = s.Kills;

            for (int i = 0; i < totalTicks; i++)
            {
                float t = s.RunTime;
                float toBoundary = (Mathf.Floor(t / 60f) + 1f) * 60f - t;
                // 边界前 ~0.1 秒挥一刀 ⇒ 跨分那一帧锄头 CD 必须是"还剩一点"而不是 0
                bool swingNow = toBoundary <= 0.12f && toBoundary > 0.09f;
                FrameInput fi = new FrameInput
                {
                    Fire = swingNow,
                    AimWorld = dummy.Pos,
                    BuyOffer = -1, SellSlot = -1, InitialChoice = -1,
                    SelectSeed = -1, RewardPick = -1
                };
                preIds = AliveIdsOf(s);
                pre = SnapOf(s);
                preKills = s.Kills;

                s.ConsumeFrameEdges(ref fi);
                KeepAlive(p);
                s.FixedTick(GameClock.FixedDt, fi);

                if (s.WaveIndex != lastWave)
                {
                    var row = new MinuteRow
                    {
                        FromWave = lastWave, ToWave = s.WaveIndex,
                        Pre = pre, Post = SnapOf(s),
                        KilledInTick = s.Kills - preKills,
                        Dt = GameClock.FixedDt
                    };
                    // 跨分后仍活着的 id ∩ 跨分前的 id ⇒ 缺的那些就是"凭空消失"
                    var after = AliveIdsOf(s);
                    int vanished = 0;
                    foreach (int id in preIds) if (after.Contains(id)) vanished++;
                    row.Vanished = preIds.Count - vanished;
                    rows.Add(row);
                    lastWave = s.WaveIndex;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("── ").Append(label).Append(" ──\n");
            // 诊断尾巴：没有跨分行时，靠这一行区分"没跑起来"与"跑了但没跨分" ✓
            sb.Append("  驱动核对：t=").Append(s.RunTime.ToString("0.00"))
              .Append("s｜Wave=").Append(s.WaveIndex)
              .Append("｜ClockWave=").Append(s.Clock.WaveIndex)
              .Append("｜Phase=").Append(s.Phase)
              .Append("｜敌=").Append(s.World.Enemies.Count)
              .Append("｜跨分事件=").Append(rows.Count).Append('\n');
            sb.Append("  跨分  锄头CD(前→后)      水(前→后)   肥料(前→后)  敌数(前→后)  ")
              .Append("待处理(前→后)  消失  击杀  清场调用  阶段\n");
            for (int i = 0; i < rows.Count; i++)
            {
                MinuteRow r = rows[i];
                sb.Append("  ").Append(r.FromWave).Append("→").Append(r.ToWave).Append("   ")
                  .Append(r.Pre.HoeCd.ToString("0.0000")).Append("→").Append(r.Post.HoeCd.ToString("0.0000"))
                  .Append("   ").Append(r.Pre.Water).Append("→").Append(r.Post.Water)
                  .Append("   ").Append(r.Pre.Fert).Append("→").Append(r.Post.Fert)
                  .Append("   ").Append(r.Pre.Enemies).Append("→").Append(r.Post.Enemies)
                  .Append("   ").Append(r.Pre.Pending).Append("→").Append(r.Post.Pending)
                  .Append("   ").Append(r.Vanished)
                  .Append("   ").Append(r.KilledInTick)
                  .Append("   ").Append(r.Post.Despawn - r.Pre.Despawn)
                  .Append("   ").Append(r.Pre.Phase).Append("→").Append(r.Post.Phase)
                  .Append('\n');
            }
            // 逐项判定：除"该变的"（Wave/ClockWave/水 +50）以外必须全同
            int badCd = 0, badFert = 0, badWater = 0, badPending = 0, badClear = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                MinuteRow r = rows[i];
                // 锄头 CD 自然衰减 = 一个固定步；被清零则是"跳到 0"
                float natural = Mathf.Max(0f, r.Pre.HoeCd - GameClock.FixedDt);
                if (Mathf.Abs(r.Post.HoeCd - natural) > 0.002f) badCd++;
                if (r.Post.Fert != r.Pre.Fert) badFert++;
                int expectWater = Mathf.Min(RunSession.WaterAmmoMax, r.Pre.Water + 50);
                if (r.Post.Water != expectWater) badWater++;
                if (r.Post.Pending != r.Pre.Pending) badPending++;
                if (r.Vanished != 0 || r.Post.Despawn != r.Pre.Despawn) badClear++;
            }
            sb.Append("  判定：CD 被重置 ").Append(badCd).Append(" 次｜肥料被改 ").Append(badFert)
              .Append(" 次｜水≠+50 ").Append(badWater).Append(" 次｜待处理被清 ").Append(badPending)
              .Append(" 次｜异常清场 ").Append(badClear).Append(" 次\n");
            s.Dispose();
            return sb.ToString();
        }

        /// <summary>
        /// **② 短按单点探针**：N 次"按 1 个固定步就松开"，逐次记
        /// 「有没有动画（`MeleeSwingEvent`）」与「有没有伤害（靶子掉血）」两列 ⇒ 配对表。
        ///
        /// 判据：两列必须**同时成立或同时不成立**。`有动画 && 无伤害` = 用户说的
        /// "出动画了但没实际攻击"；`无动画 && 无伤害` 若是**冷却内**的点按则可接受（被吞掉的是
        /// 输入而不是表现），但若因此"点了没反应、必须再点一次"，就是用户说的"什么都没" ✓
        ///
        /// spec："间隔毫秒,次数"（默认 500,20）
        /// </summary>
        public string ProbeTap(string spec)
        {
            int spacingMs = 500, taps = 20;
            if (!string.IsNullOrEmpty(spec))
            {
                string[] parts = spec.Split(',');
                spacingMs = Mathf.Clamp(ParseInt(parts[0], 500), 50, 3000);
                if (parts.Length > 1) taps = Mathf.Clamp(ParseInt(parts[1], 20), 1, 60);
            }
            int gapTicks = Mathf.Max(1, Mathf.RoundToInt(spacingMs / 1000f / GameClock.FixedDt));

            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.World.SpawnEnemy("grunt", new Vector2(2f, 0f), 1);
            EnemyActor dummy = s.World.Enemies[s.World.Enemies.Count - 1];
            dummy.Health.Setup(1e5f, 0f);
            dummy.Pos = new Vector2(2f, 0f);      // 在近战框内且正对 ⇒ 任何"真挥击"都必然掉血

            int swingCount = 0;
            System.Action<MeleeSwingEvent> onSwing = ev => swingCount++;
            Bus.Subscribe(onSwing);

            StringBuilder sb = new StringBuilder();
            float cdNeed = TableDatabase.Tuning.tools.hoe.cooldown / Mathf.Max(0.05f, p.AttackSpeed);
            sb.Append("PROBE_TAP 点按 ").Append(taps).Append(" 次｜间隔 ").Append(spacingMs)
              .Append("ms｜锄头冷却 ").Append(cdNeed.ToString("0.000")).Append("s｜靶子 (2,0)\n");
            sb.Append("  #  按下时CD  有动画  有伤害  判定\n");
            sb.Append("    附带：按下瞬间「距离 / 场上敌人 / 靶子存活 / 靶子HP」—— 用来把\n");
            sb.Append("    「判定被跳过」和「靶子根本不在框里」分开（否则探针自己会造假阳性）\n");
            int mismatch = 0, animOnly = 0, noneCount = 0;
            try
            {
                for (int k = 0; k < taps; k++)
                {
                    int sw0 = swingCount;
                    float hp0 = dummy.Health.Hp;
                    float cdAtPress = p.WeaponCds[PlayerActor.WeaponHoe];
                    float distAtPress = Vector2.Distance(p.Pos, dummy.Pos);
                    int foesAtPress = s.World.Enemies.Count;
                    bool aliveAtPress = dummy.Alive;

                    // ── 按下：**只按 1 个固定步**（短按的最小单位）──
                    FrameInput press = new FrameInput
                    {
                        Fire = true, AimWorld = dummy.Pos,
                        BuyOffer = -1, SellSlot = -1, InitialChoice = -1,
                        SelectSeed = -1, RewardPick = -1
                    };
                    s.ConsumeFrameEdges(ref press);
                    KeepAlive(p);
                    s.FixedTick(GameClock.FixedDt, press);

                    // ── 松开：先过一遍帧级入口（**真实循环每渲染帧都调它**），再走完剩余间隔 ──
                    //   ★ 漏了这一步，`_fireWasDown` 会一直停在 true ⇒ 后面的"按下沿"再也取不到 ⇒
                    //     闩永远不置位 ⇒ 结论会变成"点了全被吞"（那是探针的锅，不是游戏的）✗
                    FrameInput idle = new FrameInput
                    {
                        Fire = false, AimWorld = dummy.Pos,
                        BuyOffer = -1, SellSlot = -1, InitialChoice = -1,
                        SelectSeed = -1, RewardPick = -1
                    };
                    FrameInput releaseEdge = idle;
                    s.ConsumeFrameEdges(ref releaseEdge);
                    for (int t = 1; t < gapTicks; t++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }

                    bool anim = swingCount - sw0 > 0;
                    bool dmg = hp0 - dummy.Health.Hp > 1e-4f;
                    string verdict;
                    if (anim && dmg) verdict = "OK（表现与判定都有）";
                    else if (!anim && !dmg) { verdict = "被吞（冷却内，表现也没有）"; noneCount++; }
                    else if (anim && !dmg) { verdict = "★异常：有动画无伤害"; animOnly++; mismatch++; }
                    else { verdict = "★异常：有伤害无动画"; mismatch++; }
                    sb.Append("  ").Append((k + 1).ToString().PadLeft(2)).Append("  ")
                      .Append(cdAtPress.ToString("0.0000").PadLeft(7)).Append("   ")
                      .Append(anim ? " 有 " : " 无 ").Append("    ")
                      .Append(dmg ? " 有 " : " 无 ").Append("    ").Append(verdict)
                      .Append("   ｜d=").Append(distAtPress.ToString("0.00"))
                      .Append(" 敌=").Append(foesAtPress)
                      .Append(" 活=").Append(aliveAtPress ? "1" : "0")
                      .Append(" HP=").Append(hp0.ToString("0.0"))
                      .Append('\n');
                }

                // ── 渲染帧级：**一帧 8ms（120fps）的"点一下"** 会不会整个被吞 ──
                // 真人短按的最短形态就是"按下与松开落在同一个渲染帧内"。
                // `GameClock.Advance` 在累加器不足 FixedDt 时**一个固定步都不跑** ⇒
                // 若输入只有"电平"没有"闩"，这一次点击就凭空消失 ✓
                int sw1 = swingCount;
                float hp1 = dummy.Health.Hp;
                // ★ 先把冷却清零 ★：这一行要测的是"**输入有没有送达**"，
                //   不是射速限制。留着 0.7s 冷却的话，探针只跑 2 个固定步就结束，
                //   闩还没机会兑现 ⇒ 又会读成"被吞"（探针第二次造假阳性）✗
                for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
                p.SwitchWindup = 0f;
                FrameInput edge = new FrameInput
                {
                    Fire = true, AimWorld = dummy.Pos,
                    BuyOffer = -1, SellSlot = -1, InitialChoice = -1,
                    SelectSeed = -1, RewardPick = -1
                };
                s.ConsumeFrameEdges(ref edge);
                GameClock gc = new GameClock();
                gc.Advance(0.008f, 5, dt => s.FixedTick(dt, edge));        // 120fps 的"按下那一帧"
                FrameInput up = new FrameInput
                {
                    Fire = false, AimWorld = dummy.Pos,
                    BuyOffer = -1, SellSlot = -1, InitialChoice = -1,
                    SelectSeed = -1, RewardPick = -1
                };
                FrameInput upEdge = up;
                s.ConsumeFrameEdges(ref upEdge);                            // 下一渲染帧：已经松开
                gc.Advance(0.008f, 5, dt => s.FixedTick(dt, up));          // 下一帧已松开
                // 给闩一个真正兑现的机会：再跑 0.25s（= 闩的窗口长度）
                gc.Advance(0.25f, 20, dt => { KeepAlive(p); s.FixedTick(dt, up); });
                bool edgeAnim = swingCount - sw1 > 0;
                bool edgeDmg = hp1 - dummy.Health.Hp > 1e-4f;
                sb.Append("  120fps 单帧点按（8ms 按下/抬起）：动画=").Append(edgeAnim ? "有" : "无")
                  .Append("｜伤害=").Append(edgeDmg ? "有" : "无")
                  .Append(edgeAnim || edgeDmg ? "" : " ⇒ ★这次点击整个被吞掉了★").Append('\n');
            }
            finally { Bus.Unsubscribe(onSwing); }

            sb.Append("  小结：有动画无伤害 ").Append(animOnly).Append(" 次｜被吞 ").Append(noneCount)
              .Append(" 次｜配对不符 ").Append(mismatch).Append(" 次 / 共 ").Append(taps).Append(" 次\n");
            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// **③ 抽卡门控探针**：把车推进到"绕场中"，再打开抽卡面板，**连拍两帧**看车坐标。
        ///
        /// 判据：面板开着时两帧的 `TruckWorldPos` **逐位相同**（连 1e-6 都不许动）；
        /// 关掉面板之后车必须**继续动**（否则只是"车停了"而不是"门控对了"）✓
        /// 同时打印 `Phase` 的名字 —— 因为门控的判据就是它 ✓
        /// </summary>
        public string ProbeTruckPause(string spec)
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.Health.Setup(1e6f, 0f);
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;

            FrameInput idle = new FrameInput
            {
                Fire = false, BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };

            // ── 推进到 t≈64s：车 60s 入场、62.5~67.5s 绕场 ⇒ 此刻取位是**动**的 ──
            int to64 = Mathf.RoundToInt(64f / GameClock.FixedDt);
            for (int i = 0; i < to64; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }

            ShopTruck.Phase truckPhase = s.Truck.Current;
            Vector3 moving0 = s.TruckWorldPos;
            for (int i = 0; i < 2; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }
            Vector3 moving1 = s.TruckWorldPos;
            float clockAt = s.Clock.GameTime;

            // ── 打开抽卡面板（升级奖励 = PhaseState.LevelReward）──
            s.DebugShowReward();
            string phaseName = s.Phase.ToString();
            int cards = s.RewardCards.Count;
            bool panelOpen = s.RewardPanelOpen;
            Vector3 open0 = s.TruckWorldPos;
            float g0 = s.Clock.GameTime;
            for (int i = 0; i < 2; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }
            Vector3 open1 = s.TruckWorldPos;
            float g1 = s.Clock.GameTime;

            // ── 关面板，再跑 1 秒：车必须重新动起来 ──
            bool picked = cards > 0 && s.PickRewardCard(0);
            string phaseAfter = s.Phase.ToString();
            Vector3 closeA = s.TruckWorldPos;
            for (int i = 0; i < 60; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }
            Vector3 closeB = s.TruckWorldPos;

            bool frozen = open0 == open1;             // Vector3 的 == 是逐分量 float 比较
            bool clockFrozen = Mathf.Abs(g1 - g0) < 1e-9f;
            bool resumed = closeA != closeB;
            bool wasMoving = moving0 != moving1;

            StringBuilder sb = new StringBuilder();
            sb.Append("PROBE_TRUCKPAUSE\n");
            sb.Append("  ① 面板前车在 ").Append(truckPhase).Append("｜t=").Append(clockAt.ToString("0.000"))
              .Append("s｜两帧位置 ").Append(FmtV(moving0)).Append(" → ").Append(FmtV(moving1))
              .Append(wasMoving ? "（在动 ✔）" : "（★没动 ⇒ 本探针无效★）").Append('\n');
            sb.Append("  ② 抽卡阶段名 = ").Append(phaseName)
              .Append("｜RewardPanelOpen=").Append(panelOpen)
              .Append("｜卡数=").Append(cards).Append('\n');
            sb.Append("  ③ 面板开着两帧：车 ").Append(FmtV(open0)).Append(" → ").Append(FmtV(open1))
              .Append(frozen ? "　★逐位相同 ✔★" : "　★还在动 ✘★")
              .Append("｜世界时钟 ").Append(g0.ToString("0.0000")).Append(" → ").Append(g1.ToString("0.0000"))
              .Append(clockFrozen ? "（停摆 ✔）" : "（★仍在走 ✘★）").Append('\n');
            sb.Append("  ④ 选卡后阶段 = ").Append(phaseAfter).Append("｜pick=").Append(picked)
              .Append("｜再跑 1 秒：车 ").Append(FmtV(closeA)).Append(" → ").Append(FmtV(closeB))
              .Append(resumed ? "（恢复移动 ✔）" : "（★仍不动 ✘★）").Append('\n');
            sb.Append("  判定：面板期间车「").Append(frozen ? "冻结" : "在动")
              .Append("」＋时钟「").Append(clockFrozen ? "停摆" : "在走")
              .Append("」＋关面板后「").Append(resumed ? "恢复" : "仍停").Append("」\n");
            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        private static string FmtV(Vector3 v)
            => "(" + v.x.ToString("0.000000") + "," + v.y.ToString("0.000000") + "," + v.z.ToString("0.000000") + ")";

        /// <summary>把一串整数排成一行（逐秒时间线用）。</summary>
        private static string[] ToText(System.Collections.Generic.List<int> v)
        {
            string[] a = new string[v.Count];
            for (int i = 0; i < v.Count; i++) a[i] = v[i].ToString();
            return a;
        }

        /// <summary>
        /// 让玩家在本探针里不死 —— **每固定步补满**，而不是开局 `Health.Setup(1e6)`。
        ///
        /// ★ 为什么 `Setup` 不行（实测踩到） ★
        /// 第一版探针用 `p.Health.Setup(1e6f, 0f)`，结果跑到 **t=13.90s 就变成 `Phase=Defeat`**：
        /// `MaxHp` 是**属性派生的**（每帧按 `StatBlock` 重算）⇒ `Setup` 写进去的 1e6 立刻被覆盖，
        /// 1 万多个固定步只跑了 14 秒就结束 ⇒ 探针静默产出一张**空表**（0 个跨分事件）✗
        /// 这正是"探针自己先死"这类假阴性：表是空的，但它不代表"没有异常" ✓
        /// 做法照抄 `RunSimulator.TopUpPlayer`（那里也是"在 FixedTick 之前补"）✓
        /// </summary>
        private static void KeepAlive(PlayerActor p)
        {
            if (p == null) return;
            HealthPool hp = p.Health;
            if (hp.Hp < hp.MaxHp) hp.Hp = hp.MaxHp;
            if (hp.Shield < hp.MaxShield) hp.Shield = hp.MaxShield;
        }

        /// <summary>
        /// **④ BOSS 清敌探针**：先摆 N 只小怪，再刷一只 BOSS，跑 3 秒。
        ///
        /// 判据：**刷前 N → 刷后 N**（BOSS 本体不算小怪）⇒ 一只都不许被收走。
        /// 同时把"走死亡路径的数量"与"凭空消失的数量"分开报：
        /// 前者是战斗（真的被打死），后者才叫清场 ✓
        /// spec：小怪数（默认 8）。
        /// </summary>
        public string ProbeBossClear(string spec)
        {
            // spec = "小怪数,秒数"（默认 8,60）；只给一个数时当作秒数（兼容早先的调用）
            int n = 8, secs = 60;
            if (!string.IsNullOrEmpty(spec) && spec.IndexOf(',') >= 0)
            {
                string[] parts = spec.Split(',');
                n = Mathf.Clamp(ParseInt(parts[0], 8), 1, 40);
                secs = Mathf.Clamp(ParseInt(parts[1], 60), 3, 90);
            }
            else if (!string.IsNullOrEmpty(spec)) secs = Mathf.Clamp(ParseInt(spec, 60), 3, 90);
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.Health.Setup(1e6f, 0f);
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;

            // 小怪摆在场上（避开中心 1.2m 内，免得被 BOSS 冲锋顺手撞到）
            for (int i = 0; i < n; i++)
            {
                float ang = (i / (float)n) * Mathf.PI * 2f;
                Vector2 at = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 8f;
                s.World.SpawnEnemy("grunt", at, 1);
            }
            // 让小怪别死：血量给 **1e5**（不是 1e9 —— 1e9 附近 float ulp≈64，
            // 扣 4 点血读出来仍等于 1e9，会把"有伤害"读成"没伤害"，见 :4091 那条既有说明）✓
            for (int i = 0; i < s.World.Enemies.Count; i++) s.World.Enemies[i].Health.Setup(1e5f, 0f);

            int beforeMinions = 0;
            System.Collections.Generic.HashSet<int> preIds = AliveIdsOf(s);
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e != null && e.Def != null && e.Def.Kind == EnemyKind.Boss) continue;
                beforeMinions++;
            }
            int despawnBefore = s.World.DespawnAllCalls;
            int killsBefore = s.Kills;

            // 刷 BOSS —— 走 `RunSession` 里那条唯一入口之后会用的同一个方法
            s.World.SpawnEnemy("boss", new Vector2(0f, 9f), Mathf.Max(1, s.WaveIndex));
            EnemyActor boss = null;
            for (int i = s.World.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e != null && e.Def != null && e.Def.Kind == EnemyKind.Boss) { boss = e; break; }
            }

            FrameInput idle = new FrameInput
            {
                Fire = false, BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };
            // BOSS 入场 1.2s + 出手若干次 ⇒ **跑满 60 秒**（= 一整分钟、也是 BOSS 战时限的子集）
            // 逐秒采样：因为"清场"如果是**某个阶段/某一招**触发的，只在 3 秒里看不出 ✓
            int ticks = Mathf.RoundToInt(secs / GameClock.FixedDt);
            var secMinions = new System.Collections.Generic.List<int>();
            var secKills = new System.Collections.Generic.List<int>();
            var secDespawn = new System.Collections.Generic.List<int>();
            for (int i = 0; i < ticks; i++)
            {
                KeepAlive(p);
                s.FixedTick(GameClock.FixedDt, idle);
                if ((i + 1) % 60 != 0) continue;         // 每秒采一次
                int mn = 0;
                for (int k = 0; k < s.World.Enemies.Count; k++)
                {
                    EnemyActor e = s.World.Enemies[k];
                    if (e == null || e.Def == null) continue;
                    if (e.Def.Kind == EnemyKind.Boss) continue;
                    mn++;
                }
                secMinions.Add(mn);
                secKills.Add(s.Kills);
                secDespawn.Add(s.World.DespawnAllCalls);
            }

            int afterMinions = 0;
            var afterIds = AliveIdsOf(s);
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                EnemyActor e = s.World.Enemies[i];
                if (e != null && e.Def != null && e.Def.Kind == EnemyKind.Boss) continue;
                afterMinions++;
            }
            int vanished = 0;
            foreach (int id in preIds) if (!afterIds.Contains(id)) vanished++;

            // 逐秒找"掉了但没人死"的那一秒 —— 那就是清场
            StringBuilder dips = new StringBuilder();
            int unexplained = 0;
            for (int i = 0; i < secMinions.Count; i++)
            {
                int prev = i == 0 ? beforeMinions : secMinions[i - 1];
                int prevKills = i == 0 ? killsBefore : secKills[i - 1];
                int prevDespawn = i == 0 ? despawnBefore : secDespawn[i - 1];
                if (secMinions[i] < prev)
                {
                    bool bad = (secKills[i] - prevKills) < (prev - secMinions[i])
                               || secDespawn[i] != prevDespawn;
                    if (bad) unexplained++;
                    dips.Append("    t=").Append((i + 1) * 1.0f).Append("s  小怪 ").Append(prev)
                        .Append("→").Append(secMinions[i])
                        .Append("｜击杀+").Append(secKills[i] - prevKills)
                        .Append("｜清场调用+").Append(secDespawn[i] - prevDespawn)
                        .Append(bad ? "  ★无法用「打死」解释★" : "  （被打死，正常）").Append('\n');
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("PROBE_BOSSCLEAR 小怪 ").Append(n).Append(" 只 ＋ BOSS 1 只 ⇒ 跑 ")
              .Append(secs).Append("s（").Append(ticks).Append(" 固定步）\n");
            sb.Append("  小怪数：刷前 ").Append(beforeMinions).Append(" → 刷后 ").Append(afterMinions)
              .Append(beforeMinions == afterMinions ? "　★保持不变 ✔★" : "　★有变化 ✘★").Append('\n');
            sb.Append("  凭空消失（不在任何死亡路径上）：").Append(vanished)
              .Append(vanished == 0 ? " ✔" : " ✘").Append('\n');
            sb.Append("  走死亡路径：").Append(s.Kills - killsBefore).Append(" 只（击杀计入，不是清场）\n");
            sb.Append("  逐秒小怪数：").Append(string.Join(" ", ToText(secMinions))).Append('\n');
            sb.Append("  无法用「打死」解释的下降：").Append(unexplained).Append(" 次\n").Append(dips.ToString());
            sb.Append("  DespawnAllEnemies 调用：").Append(despawnBefore).Append(" → ").Append(s.World.DespawnAllCalls)
              .Append(s.World.DespawnAllCalls == despawnBefore ? " ✔（没被调用）" : " ✘（被调用了）").Append('\n');
            sb.Append("  BOSS：").Append(boss == null ? "★没刷出来 ✘★"
                : ("在场，HP=" + boss.Health.Hp.ToString("0") + "/" + boss.Health.MaxHp.ToString("0")))
              .Append('\n');
            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// **⑤ 自充速率探针**：装上 `can_autocharge`（按表里的真实数值），
        /// 把水箱清空后跑 **2.0 秒**，看回了多少点 ⇒ 每秒回量是**实测**，不是读常量。
        ///
        /// ★ 本探针**不改数值** ★ —— 表里是 12 就报 12，是 10 就报 10（改由数值那条路负责）✓
        /// </summary>
        public string ProbeCharge(string spec)
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.Health.Setup(1e6f, 0f);

            PluginAsset charge = null;
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                if (TableDatabase.Plugins[i].id == "can_autocharge") { charge = TableDatabase.Plugins[i]; break; }

            string installNote;
            if (charge == null) installNote = "★表里没有 can_autocharge ✘★";
            else
            {
                bool ok = s.InstallPlugin(charge, WeaponSlot.Can, out string reason);
                installNote = ok ? $"已装 {charge.id}（amount={charge.amount:0.###}）" : "装不上：" + reason;
            }

            float rate = s.ModsFor(WeaponSlot.Can).AutoChargePerSecond;
            int perLayer = charge == null ? 0 : Mathf.RoundToInt(charge.amount);
            int layers = s.ModsFor(WeaponSlot.Can).AutoChargeLayers;

            // 水箱清空 ⇒ 回水不会被上限截断
            s.WaterAmmo = 0;
            FrameInput idle = new FrameInput
            {
                Fire = false, BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };
            int ticks = Mathf.RoundToInt(2f / GameClock.FixedDt);   // 2.0 秒
            for (int i = 0; i < ticks; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }
            int after2s = s.WaterAmmo;

            // 对照：再跑 1 秒（看斜率是否稳定在 rate）
            for (int i = 0; i < Mathf.RoundToInt(1f / GameClock.FixedDt); i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }
            int after3s = s.WaterAmmo;

            StringBuilder sb = new StringBuilder();
            sb.Append("PROBE_CHARGE ").Append(installNote).Append('\n');
            sb.Append("  表值 amount/层 = ").Append(perLayer).Append("｜聚合 AutoChargePerSecond = ")
              .Append(rate.ToString("0.###")).Append("｜层数 = ").Append(layers).Append('\n');
            sb.Append("  水箱 0 → 2.0s 后 = ").Append(after2s).Append(" 点（期望 ").Append(rate * 2f).Append("）\n");
            sb.Append("  再 1.0s → 共 3.0s = ").Append(after3s).Append(" 点（期望 ").Append(rate * 3f).Append("）\n");
            sb.Append("  实测速率 = ").Append((after2s / 2f).ToString("0.0")).Append(" 点/秒\n");
            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        private static string Fail(string why)
        {
            Log.Warn("DEBUG " + why);
            return why;
        }

        /// <summary>
        /// **③ 附带：商店面板打开时，世界时间是否停摆** —— 只读探针。
        ///
        /// 抽卡那条闸门（`RewardPanelOpen`）只认 `Phase == LevelReward`；
        /// 而 `PhaseState.Shop` 走的是**另一个阶段** ⇒ 它是否停摆必须单独测。
        /// 判据与抽卡那条完全一样：面板开着两帧，`Clock.GameTime` 与 `TruckWorldPos` 都必须**逐位不变** ✓
        /// </summary>
        public string ProbeShopPause(string spec)
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;

            FrameInput idle = new FrameInput
            {
                Fire = false, BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };
            // 先跑到 64s（车在绕场 ⇒ 位置是动的）
            int to64 = Mathf.RoundToInt(64f / GameClock.FixedDt);
            for (int i = 0; i < to64; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }

            s.ForceShopForDebug();                 // 进商店（调试入口，与真实 E 键同一条 SetPhase）
            string phase = s.Phase.ToString();
            Vector3 a = s.TruckWorldPos;
            float g0 = s.Clock.GameTime;
            for (int i = 0; i < 60; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }   // 店里 1 秒
            Vector3 b = s.TruckWorldPos;
            float g1 = s.Clock.GameTime;

            StringBuilder sb = new StringBuilder();
            sb.Append("PROBE_SHOPPAUSE 进店后阶段 = ").Append(phase).Append('\n');
            sb.Append("  店里过 1.0s：世界时钟 ").Append(g0.ToString("0.0000")).Append(" → ").Append(g1.ToString("0.0000"))
              .Append("（Δ=").Append((g1 - g0).ToString("0.0000")).Append("s）")
              .Append(Mathf.Abs(g1 - g0) < 1e-6f ? "　★停摆 ✔★" : "　★仍在走 ✘★").Append('\n');
            sb.Append("  车：").Append(FmtV(a)).Append(" → ").Append(FmtV(b))
              .Append(a == b ? "　★逐位相同 ✔★" : "　★在动 ✘★").Append('\n');
            sb.Append("  店还开着吗：Phase=").Append(s.Phase)
              .Append("｜shopCanShop=").Append(s.TruckCanShop).Append('\n');
            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// **① 附带：整点的"清除敌人"到底是谁清的** —— 只读探针。
        ///
        /// 用户把"清除敌人"列在整点异常里，但 `ProbeMinute` 显示跨分那一帧
        /// **消失 0 只、`DespawnAllEnemies` 0 次** ⇒ 清敌不在跨分逻辑上 ✗
        /// 而全工程 `Health.Kill()` 的**唯一玩法调用点**是商店车的巡回清扫
        /// （`RunSession.TickShopTruck` 的 Looping 分支，半径 = arenaHalf × 0.16 = 3.2m）。
        /// 车的周期恰好是 **60 秒**（`ShopTruck.CycleSeconds`）⇒ 与"整点"逐字吻合 ✓
        ///
        /// 本探针把小怪摆在**车行道（外圈 r≈17m）**上，跑 80 秒，逐秒记小怪数：
        /// 若它们只在车的 Looping 窗口（t≈62.5~67.5s）成批消失，那"整点清敌"就是车 ✓
        /// </summary>
        public string ProbeTruckSweep(string spec)
        {
            int secs = Mathf.Clamp(ParseInt(spec, 80), 10, 120);
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;

            // 车行道半径：路径是归一化坐标 × arenaHalf；外圈约 0.85 × 20 = 17m
            float ring = TableDatabase.Tuning.farm.arenaHalf * 0.85f;
            for (int i = 0; i < 8; i++)
            {
                float ang = (i / 8f) * Mathf.PI * 2f;
                s.World.SpawnEnemy("grunt", new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ring, 1);
            }
            for (int i = 0; i < s.World.Enemies.Count; i++) s.World.Enemies[i].Health.Setup(1e5f, 0f);
            int start = s.World.Enemies.Count;

            FrameInput idle = new FrameInput
            {
                Fire = false, BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };
            int ticks = Mathf.RoundToInt(secs / GameClock.FixedDt);
            var counts = new System.Collections.Generic.List<int>();
            var truckPhase = new System.Collections.Generic.List<string>();
            for (int i = 0; i < ticks; i++)
            {
                KeepAlive(p);
                s.FixedTick(GameClock.FixedDt, idle);
                if ((i + 1) % 60 != 0) continue;
                counts.Add(s.World.Enemies.Count);
                truckPhase.Add(s.Truck.Current.ToString());
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("PROBE_TRUCKSWEEP 小怪 ").Append(start).Append(" 只摆在车行道 r=")
              .Append(ring.ToString("0.0")).Append("m｜跑 ").Append(secs).Append("s\n");
            sb.Append("  逐秒小怪数：").Append(string.Join(" ", ToText(counts))).Append('\n');
            sb.Append("  逐秒车状态：").Append(string.Join(" ", truckPhase.ToArray())).Append('\n');
            int firstDropAt = -1, dropped = 0;
            for (int i = 1; i < counts.Count; i++)
            {
                if (counts[i] >= counts[i - 1]) continue;
                if (firstDropAt < 0) firstDropAt = i + 1;
                dropped += counts[i - 1] - counts[i];
            }
            sb.Append("  首次变少：t=").Append(firstDropAt).Append("s｜共被收走 ").Append(dropped)
              .Append(" 只｜击杀计数 ").Append(s.Kills)
              .Append("｜DespawnAllEnemies 调用 ").Append(s.World.DespawnAllCalls).Append('\n');
            s.Dispose();
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v189：**跨分瞬间的过程量**（肥料前摇 / 水壶激光）★
        //
        // ① 的"充能异常消失"有两个独立的来源，必须分开测：
        //   · 肥料：`ClearTransientCombatState` 会 `_pendingBlasts.Clear()` ——
        //     已经扣掉的充能靠 `_fertRefund` 退回，但**那一发炸弹没了** ✗
        //   · 水壶：同一处 `StopBeam()` 把 `_beamFiring` 归零 ⇒ 下一帧重新付 20 点起手费 ✗
        //     （玩家按住不放却在一秒里被扣两次 20 ⇒ "充能异常消失"）✓
        // 两个都能用"跨分前后逐帧的水位/前摇数"直接看见 ✓
        // ══════════════════════════════════════════════════════════════════════
        public string ProbeBoundaryFx(string spec)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(BoundaryFert());
            sb.Append(BoundaryBeam());
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>肥料：跨分前 0.3s 投一发（前摇 0.5s）⇒ 跨分瞬间那颗炸弹还在不在。</summary>
        private string BoundaryFert()
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            s.AimWorldLast = new Vector2(3f, 0f);
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            s.FertAmmo = 5;
            s.WaterAmmo = RunSession.WaterAmmoMax;

            FrameInput idle = new FrameInput
            {
                Fire = false, BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };
            // 跑到 t = 59.70s
            int to5970 = Mathf.RoundToInt(59.70f / GameClock.FixedDt);
            for (int i = 0; i < to5970; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idle); }

            int fertBefore = s.FertAmmo;
            s.DebugThrowOnly();                       // 与玩家按键同一条 `UseTool` ⇒ 扣充能 + 排前摇
            int fertAfterThrow = s.FertAmmo;
            int pendAfterThrow = s.PendingBlastCount;

            // 跨分前后各取几个点
            var marks = new float[] { 59.85f, 59.99f, 60.02f, 60.20f, 60.60f, 61.20f };
            StringBuilder sb = new StringBuilder();
            sb.Append("── 肥料：跨分前 0.30s 投一发 ──\n");
            sb.Append("  投掷瞬间：肥料 ").Append(fertBefore).Append("→").Append(fertAfterThrow)
              .Append("｜待爆 ").Append(pendAfterThrow).Append('\n');
            sb.Append("  时刻   待爆  肥料  说明\n");
            int mi = 0;
            int totalTicks = Mathf.RoundToInt(2.0f / GameClock.FixedDt);
            for (int i = 0; i < totalTicks && mi < marks.Length; i++)
            {
                KeepAlive(p);
                s.FixedTick(GameClock.FixedDt, idle);
                if (s.RunTime < marks[mi]) continue;
                sb.Append("  ").Append(s.RunTime.ToString("00.000")).Append("   ")
                  .Append(s.PendingBlastCount).Append("     ").Append(s.FertAmmo).Append("     ")
                  .Append(s.RunTime >= 60f ? "（已跨分）" : "（未跨分）").Append('\n');
                mi++;
            }
            s.Dispose();
            return sb.ToString();
        }

        /// <summary>水壶：跨分前后各 1 秒**按住不放** ⇒ 水位斜率有没有多出一次 20 点。</summary>
        private string BoundaryBeam()
        {
            RunSession s = new RunSession();
            s.Start(20240607UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWeapon(PlayerActor.WeaponCan, s.World);
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            s.WaterAmmo = RunSession.WaterAmmoMax;

            FrameInput hold = new FrameInput
            {
                Fire = true, AimWorld = new Vector2(6f, 0f), BuyOffer = -1, SellSlot = -1,
                InitialChoice = -1, SelectSeed = -1, RewardPick = -1
            };
            // 跑到 t = 59.0s，然后按住左键跨过整分
            int to59 = Mathf.RoundToInt(59.0f / GameClock.FixedDt);
            FrameInput idleHold = hold; idleHold.Fire = false;
            for (int i = 0; i < to59; i++) { KeepAlive(p); s.FixedTick(GameClock.FixedDt, idleHold); }

            int ticks = Mathf.RoundToInt(2.0f / GameClock.FixedDt);
            int prevWater = s.WaterAmmo;
            int drops = 0, dropsAtBoundary = 0;
            int waterAt5999 = -1, waterAt6001 = -1;
            float lastDropAt = -99f;
            StringBuilder tl = new StringBuilder();
            for (int i = 0; i < ticks; i++)
            {
                KeepAlive(p);
                s.FixedTick(GameClock.FixedDt, hold);
                int w = s.WaterAmmo;
                if (w != prevWater)
                {
                    if (w < prevWater)
                    {
                        drops++;
                        bool nearBoundary = Mathf.Abs(s.RunTime - 60f) < 0.1f;
                        if (nearBoundary) dropsAtBoundary++;
                        if (tl.Length < 400)
                            tl.Append("   扣水 t=").Append(s.RunTime.ToString("00.000"))
                              .Append("  ").Append(prevWater).Append("→").Append(w)
                              .Append(nearBoundary ? "  ★在跨分处★" : "").Append('\n');
                        lastDropAt = s.RunTime;
                    }
                    prevWater = w;
                }
                if (waterAt5999 < 0 && s.RunTime >= 59.99f) waterAt5999 = w;
                if (waterAt6001 < 0 && s.RunTime >= 60.01f) waterAt6001 = w;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("── 水壶：t=59.0 → 61.0 按住不放（每 0.5s 该扣 20）──\n");
            sb.Append("  t=59.99 水=").Append(waterAt5999).Append("｜t=60.01 水=").Append(waterAt6001)
              .Append("｜差 ").Append(waterAt6001 - waterAt5999).Append('\n');
            sb.Append("  2 秒内扣水次数 = ").Append(drops).Append("（正常 4 次 = 80 点）")
              .Append("｜其中落在跨分处 = ").Append(dropsAtBoundary).Append('\n');
            sb.Append(tl.ToString());
            sb.Append("  末次扣水 t=").Append(lastDropAt.ToString("00.000")).Append('\n');
            s.Dispose();
            return sb.ToString();
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
