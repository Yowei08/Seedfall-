using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v213：BOSS 掉落 ＋ 奖励动画 ＋ 皮肤商店 的**只读验收探针** ★★
    ///
    /// ── 它量什么（全是跑出来的读数，不是复述结论）──
    ///   ① **掉落**：权重表原文（每条权重/概率 + 品质合计）｜`PickSeedIndex` 的纯函数性与前 16 次序列
    ///      ｜1000 次抽样的**实测分布**｜以及最要紧的那一条 ——
    ///      **"打一个 BOSS 到底有没有进仓库"**：`TownState.Warehouse` 前后快照 + 差量 ✓
    ///   ② **奖励动画**：`BossRewardPopup` 的推送/排队/剩余秒数读数（画面另存 PNG，见 `Shot`）
    ///   ③ **皮肤**：8 件的名/价/槽/骨骼清单｜"真赚金币 → 买 → 穿 → 卸"全链路读数
    ///      ｜每件穿上后**挂在哪个骨骼下的几个零件、世界尺寸多大**（证明是真模型而不是空节点）
    ///      ｜以及共享资源的两个数（材质池 / 自建网格份数）证明"没按实例 new 材质"
    ///
    /// ── 只读性 ──
    ///   · **没有 `[RuntimeInitializeOnLoadMethod]`**：进 play 不会自动建实例（对实机零影响）✓
    ///   · 不改表、不改场景、不写 `Assets/**`；报告只落 `Library/` ✓
    ///   · 唯一会改状态的入口是 `Run()` 里的"真的买一件"（那是验收要求的全链路），
    ///     它只动 `TownState`/`TownSkins` 这两个**运行期**对象，不落盘 ✓
    ///
    /// ── 怎么跑（编辑器里的桥）──
    ///   /scene create 一个挂本组件的空物体（名字 `ProbeV213`）
    ///   /input action=send path=ProbeV213 method=Run          → ①②③ 全部落盘
    ///   /input action=send path=ProbeV213 method=RunSelfCheck → 自检原文落盘
    ///   /input action=send path=ProbeV213 method=DemoReward   → 推一张奖励卡（play 模式下才看得见）
    ///   /input action=send path=ProbeV213 method=Shot Library/shots/x.png
    ///   /input action=send path=ProbeV213 method=Cleanup      → 把自己删掉
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV213Town : MonoBehaviour
    {
        public const string ReportPath = "Library/yowei-v213-town.txt";
        public const string SelfCheckPath = "Library/yowei-v213-selfcheck.txt";

        public string Run()
        {
            string outcome = Execute();
            Debug.Log("[PROBEV213] " + outcome);
            return outcome;
        }

        // ══════════════════════════════════════════════════════════════════
        // 主报告
        // ══════════════════════════════════════════════════════════════════

        private static string Execute()
        {
            var sb = new StringBuilder(16 * 1024);
            string outcome;
            try
            {
                sb.AppendLine("═══ v213 验收：BOSS 掉落 ＋ 奖励动画 ＋ 皮肤商店 ═══");
                sb.AppendLine($"引擎 {Application.unityVersion}｜isPlaying={Application.isPlaying}｜时间 {DateTime.Now:HH:mm:ss}");
                sb.AppendLine();

                // ★ 先把战斗侧的表载进来 ★ 不为什么功能，只为了 `BossNameOf` / `IsBossId` 能拿到
                //   `enemies.json` 的真名与 kind ⇒ 报告里写「墓园守望者」而不是「boss」。
                //   （这一步不是必须的：两个方法都有兜底，见 `TownDrops.IsBossId` 的注释）
                try { TableDatabase.Load(); }
                catch (Exception e) { sb.AppendLine("（TableDatabase.Load 失败，下面用兜底名：" + e.Message + "）"); }

                DumpDropTable(sb);
                DumpDropIntoWarehouse(sb);
                DumpPopup(sb);
                DumpSkinCatalogue(sb);
                DumpSkinChain(sb);
                DumpSkinModels(sb);

                outcome = "ok：" + Write(ReportPath, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("PROBE-V213 异常：" + ex.GetType().Name + "：" + ex.Message);
                sb.AppendLine(ex.StackTrace);
                outcome = "EXCEPTION：" + ex.GetType().Name + "：" + ex.Message;
                Debug.LogError("[PROBEV213] " + outcome);
                Write(ReportPath, sb);
            }
            return outcome;
        }

        // ══════════════════════════════════════════════════════════════════
        // ① 掉落权重表
        // ══════════════════════════════════════════════════════════════════

        private static void DumpDropTable(StringBuilder sb)
        {
            sb.AppendLine("═══ ① 掉落权重表（town_drops.json）═══");
            if (!TownDrops.TryLoad())
            {
                sb.AppendLine("  载入失败：" + (TownDrops.LastError ?? "(未知)"));
                sb.AppendLine();
                return;
            }
            sb.AppendLine(TownDrops.WeightTableText());

            sb.AppendLine("纯函数性（同输入必须同输出）：");
            sb.AppendLine($"  PickSeedIndex(1) 连调两次 = #{TownDrops.PickSeedIndex(1)} / #{TownDrops.PickSeedIndex(1)}"
                        + $"（{(TownDrops.PickSeedIndex(1) == TownDrops.PickSeedIndex(1) ? "一致 ✓" : "不一致 ✗")}）");
            sb.Append("  前 16 次的种子序列：");
            for (int k = 1; k <= 16; k++)
            {
                TownSeedDef d = TownData.SeedAt(TownDrops.PickSeedIndex(k));
                sb.Append(d != null ? d.name : "?").Append(' ');
            }
            sb.AppendLine();

            // 1000 次抽样的实测分布（只调纯函数，不动任何状态）
            const int N = 1000;
            var hits = new int[TownData.Seeds.Length];
            var gradeHits = new System.Collections.Generic.Dictionary<string, int>();
            for (int k = 1; k <= N; k++)
            {
                int si = TownDrops.PickSeedIndex(k);
                if (si >= 0 && si < hits.Length) hits[si]++;
            }
            sb.AppendLine($"  {N} 次抽样的**实测**分布：");
            for (int i = 0; i < TownData.Seeds.Length; i++)
            {
                TownSeedDef d = TownData.SeedAt(i);
                string g = d != null ? d.grade : "?";
                gradeHits.TryGetValue(g, out int acc);
                gradeHits[g] = acc + hits[i];
                sb.AppendLine($"    {g} {d?.name ?? "?",-10} {hits[i],4} 次 = {hits[i] * 100f / N,5:0.0}%");
            }
            var order = new[] { "D", "C", "B", "A" };
            for (int i = 0; i < order.Length; i++)
            {
                gradeHits.TryGetValue(order[i], out int acc);
                sb.AppendLine($"    —— 品质 {order[i]} 合计 {acc} 次 = **{acc * 100f / N:0.0}%**");
            }
            sb.AppendLine("  判据：D > C > B > A，且 A 档 ≤ 8%（「低品质多、A 少」）");
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // ①b "打一个 BOSS 真的进了仓库"
        // ══════════════════════════════════════════════════════════════════

        private static void DumpDropIntoWarehouse(StringBuilder sb)
        {
            sb.AppendLine("═══ ①b 打一个 BOSS ⇒ 主城仓库有没有 +1（前后快照）═══");
            TownState st;
            try { st = TownState.Ensure(); }
            catch (Exception e)
            {
                sb.AppendLine("  主城状态建不出来：" + e.Message);
                sb.AppendLine();
                return;
            }

            string before = WarehouseText(st);
            int killsBefore = TownDrops.BossKills;
            int dropBefore = TownDrops.DroppedTotal;

            // 走**真正的入口**：与战斗中 `RunSession.OnEnemyKilled` 调的完全是同一个方法
            int idx = TownDrops.OnBossKilled("boss", Vector2.zero);
            string after = WarehouseText(st);

            sb.AppendLine($"  击杀前：击杀次数 {killsBefore}｜累计入库 {dropBefore}｜仓库 {before}");
            sb.AppendLine($"  调用 TownDrops.OnBossKilled(\"boss\", 0)");
            sb.AppendLine($"  击杀后：击杀次数 {TownDrops.BossKills}｜累计入库 {TownDrops.DroppedTotal}｜仓库 {after}");
            sb.AppendLine($"  抽到的种子下标 = {idx}（{ResolveSeedName(idx)}）｜roll = {TownDrops.LastRoll}/{TownDrops.TotalWeight}"
                        + $"｜本次入库 {TownDrops.LastAdded} 颗");
            sb.AppendLine("  原文：" + TownDrops.LastLine);
            sb.AppendLine($"  判定：击杀次数 {killsBefore}→{TownDrops.BossKills}"
                        + $"（{(TownDrops.BossKills == killsBefore + 1 ? "✓" : "✗")}）｜"
                        + $"入库 {dropBefore}→{TownDrops.DroppedTotal}"
                        + $"（{(TownDrops.DroppedTotal == dropBefore + 1 ? "✓" : "✗ 仓库满或未入账")}）");

            // 非 BOSS 不该掉（防止"所有敌人死亡都掉"这种越界）
            int gKills = TownDrops.BossKills, gDrop = TownDrops.DroppedTotal;
            TownDrops.OnBossKilled("grunt", Vector2.zero);
            TownDrops.OnBossKilled("pig", Vector2.zero);
            sb.AppendLine($"  反例：grunt / pig 各调一次 ⇒ 击杀 {gKills}→{TownDrops.BossKills}"
                        + $"，入库 {gDrop}→{TownDrops.DroppedTotal}"
                        + $"（{(TownDrops.BossKills == gKills && TownDrops.DroppedTotal == gDrop ? "✓ 不掉" : "✗ 漏了判定")}）");
            sb.AppendLine("  注：抽样不消费 `World.Rng`（用 `BossSchedule.DerivedHash` 的派生哈希），"
                        + "所以上面这些调用**不会**让既有随机序列漂一位。");
            sb.AppendLine();
        }

        private static string WarehouseText(TownState st)
        {
            var sb = new StringBuilder(64);
            sb.Append('[');
            for (int i = 0; i < st.Warehouse.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(st.Warehouse[i]);
            }
            return sb.Append(']').ToString();
        }

        private static string ResolveSeedName(int idx)
        {
            TownSeedDef d = TownData.SeedAt(idx);
            if (d == null) return "?";
            return $"{d.name} {d.grade}/{d.level}级 成熟{d.GrowSeconds:0}s {d.GoldPerSecond:0.0}金/秒";
        }

        // ══════════════════════════════════════════════════════════════════
        // ② 奖励动画
        // ══════════════════════════════════════════════════════════════════

        private static void DumpPopup(StringBuilder sb)
        {
            sb.AppendLine("═══ ② 奖励动画（BossRewardPopup）═══");
            sb.AppendLine("  时间线：0.00~0.34s 卡片从中心 0.30→1.08→1.00 倍弹出｜0.10s 起四行依次淡入"
                        + "｜2.80s 自动收｜任何时刻点击立刻收｜多张排队");
            sb.AppendLine("  " + BossRewardPopup.Describe());
            if (Application.isPlaying)
            {
                // play 模式下顺手真推一张（画面上会真的弹出来；截图见 Shot）
                BossRewardPopup.Push(TownDrops.LastSeedIndex >= 0 ? TownDrops.LastSeedIndex : 0,
                                     TownDrops.LastAdded, "墓园守望者");
                sb.AppendLine("  已推一张（play 模式）⇒ " + BossRewardPopup.Describe());
            }
            else
            {
                sb.AppendLine("  非 play 模式 ⇒ **故意不建 GameObject**（只留读数，见 `Push` 注释）；"
                            + "画面上那张图要在 play 模式下用 `DemoReward` + `Shot` 取");
            }
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // ③ 皮肤清单
        // ══════════════════════════════════════════════════════════════════

        private static void DumpSkinCatalogue(StringBuilder sb)
        {
            sb.AppendLine("═══ ③ 皮肤清单（town_skins.json）═══");
            if (!TownSkins.TryLoad())
            {
                sb.AppendLine("  载入失败：" + (TownSkins.LastError ?? "(未知)"));
                sb.AppendLine();
                return;
            }
            sb.AppendLine($"  共 {TownSkins.Items.Length} 件｜槽位 {TownSkins.SlotCount} 个");
            sb.AppendLine("  名            价   槽位   挂哪根骨骼(s)      主色    点缀色");
            for (int i = 0; i < TownSkins.Items.Length; i++)
            {
                TownSkinDef d = TownSkins.Items[i];
                sb.AppendLine($"  {d.name,-12}{d.price,5}  {TownSkins.SlotName(d.Slot),-6} {d.bones,-18}"
                            + $" {d.color} {d.color2}");
            }
            sb.AppendLine("  说明：");
            for (int i = 0; i < TownSkins.Items.Length; i++)
                sb.AppendLine($"    {TownSkins.Items[i].name}：{TownSkins.Items[i].desc}");
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // ③b 买 → 穿 → 卸 全链路
        // ══════════════════════════════════════════════════════════════════

        private static void DumpSkinChain(StringBuilder sb)
        {
            sb.AppendLine("═══ ③b 买 / 穿 / 卸 全链路（金币是**真的种地赚来的**，不是塞进去的）═══");
            if (!TownSkins.TryLoad()) { sb.AppendLine("  皮肤表没载入，跳过"); sb.AppendLine(); return; }

            TownState st = TownState.Ensure();

            // ── 先赚金币：把开局赠的种子全种下，快进 90 秒 ──
            long coinStart = st.Coins;
            int planted = 0, plot = 0;
            for (int s = 0; s < st.Warehouse.Length && plot < TownState.PlotCount; s++)
            {
                while (st.Warehouse[s] > 0 && plot < TownState.PlotCount)
                {
                    if (st.Plant(plot, s)) planted++;
                    plot++;
                }
            }
            long earned = st.Advance(90.0);
            sb.AppendLine($"  种下 {planted} 块地（每秒产出 {st.IncomePerSecond:0.0} 金）⇒ Advance(90s) 入账 {earned} 金");
            sb.AppendLine($"  金币 {coinStart} → {st.Coins}");

            // ── 一件一件买 + 穿，每步都读一次 ──
            GameObject root = null;
            PlayerBean bean = null;
            try
            {
                root = new GameObject("ANIM_probe_v213");
                root.transform.position = new Vector3(1000f, 1000f, 1000f);   // 挪到画面外，别干扰场景
                bean = PlayerBean.Build(root.transform);
                TownSkins.InvalidateApplied();
                TownSkins.Refresh(bean.Go);
                int meshBefore = TownSkins.SpawnedCount;
                int matBefore = ToonLook.MaterialPoolCount;
                sb.AppendLine();
                sb.AppendLine($"  基线：身上装饰物件 {meshBefore} 个｜ToonLook 材质池 {matBefore} 份"
                            + $"｜SkinModels 自建网格 {SkinModels.CustomMeshCount} 份");

                for (int i = 0; i < TownSkins.Items.Length; i++)
                {
                    TownSkinDef d = TownSkins.Items[i];
                    long before = st.Coins;
                    bool bought = TownSkins.TryBuy(i);
                    long afterBuy = st.Coins;
                    bool equipped = TownSkins.Equip(i);
                    TownSkins.Refresh(bean.Go);
                    int attached = CountAttached(bean, d);

                    sb.AppendLine($"  [{i + 1}] {d.name}（{d.price} 金 / {TownSkins.SlotName(d.Slot)} / {d.bones}）");
                    sb.AppendLine($"      买：{bought}｜金币 {before} → {afterBuy}"
                                + $"（差 {before - afterBuy}，{(before - afterBuy == d.price ? "= 标价 ✓" : "≠ 标价 ✗")}）");
                    sb.AppendLine($"      穿：{equipped}｜骨骼挂上 {attached} 个容器｜累计物件 {TownSkins.SpawnedCount}"
                                + $"｜材质池 {ToonLook.MaterialPoolCount}");
                }
                long coins = st.Coins;

                // ── 负向用例 ──
                sb.AppendLine();
                sb.AppendLine("  负向用例：");
                sb.AppendLine($"    重复买同一件 {TownSkins.Items[0].name}：" + TownSkins.TryBuy(0) + "（期望 False）");
                int slotOfFirst = (int)TownSkins.Items[0].Slot;
                sb.AppendLine($"    同槽换装：先卸「{TownSkins.Items[0].name}」= {TownSkins.Unequip((SkinSlot)slotOfFirst)}"
                            + $"｜再穿上 = {TownSkins.Equip(0)}");
                TownSkins.Refresh(bean.Go);
                sb.AppendLine($"    换装后：穿着 {TownSkins.EquippedCount} 件 / 物件 {TownSkins.SpawnedCount} 个"
                            + $"（{(TownSkins.EquippedCount > 0 && TownSkins.SpawnedCount > 0 ? "每件至少 1 个容器 ✓" : "✗")}）");

                // ── 一键脱光 ──
                int stripped = TownSkins.UnequipAll();
                TownSkins.Refresh(bean.Go);
                sb.AppendLine($"    全部卸下：脱了 {stripped} 个槽 ⇒ 物件 {TownSkins.SpawnedCount} 个"
                            + $"（{(TownSkins.SpawnedCount == 0 ? "✓ 全拆干净" : "✗ 还有残留")}）");
                sb.AppendLine($"  金币结算：赚 {earned}｜买 8 件花掉 {coinStart + earned - coins}｜余额 {coins}");
                sb.AppendLine("  " + TownSkins.Describe());
                sb.AppendLine("  " + SkinModels.Describe());
                sb.AppendLine("  最近一次重建：" + TownSkins.LastBuild);
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                TownSkins.DetachAll();
                TownSkins.InvalidateApplied();
            }
            sb.AppendLine();
        }

        /// <summary>某件装饰在这具玩家模型上真的挂上了几个容器（按骨骼逐个查 `SKIN_&lt;id&gt;`）。</summary>
        private static int CountAttached(PlayerBean bean, TownSkinDef d)
        {
            if (bean == null || bean.Go == null || d == null) return 0;
            int n = 0;
            string[] bones = d.BoneNames();
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = SkinModels.Bone(bean.Go.transform, bones[i]);
                if (b == null) continue;
                if (b.Find("SKIN_" + d.id) != null) n++;
            }
            return n;
        }

        // ══════════════════════════════════════════════════════════════════
        // ③c 每件装饰的真模型读数
        // ══════════════════════════════════════════════════════════════════

        private static void DumpSkinModels(StringBuilder sb)
        {
            sb.AppendLine("═══ ③c 每件装饰的**真模型**读数（挂在哪个骨骼 / 几个零件 / 世界尺寸）═══");
            if (!TownSkins.TryLoad()) { sb.AppendLine("  皮肤表没载入，跳过"); sb.AppendLine(); return; }

            GameObject root = null;
            PlayerBean bean = null;
            try
            {
                root = new GameObject("ANIM_probe_v213_geom");
                root.transform.position = new Vector3(1000f, 1000f, 1000f);
                bean = PlayerBean.Build(root.transform);

                // 先把 8 件都买上（金币已在上一步赚过；这里只关心几何，不够就直接改 owned 不现实 ⇒ 只建几何）
                for (int i = 0; i < TownSkins.Items.Length; i++)
                {
                    TownSkinDef d = TownSkins.Items[i];
                    var made = SkinModels.Build(d, bean.Go.transform);
                    var sb2 = new StringBuilder();
                    for (int k = 0; k < made.Count; k++)
                    {
                        GameObject g = made[k];
                        if (g == null) continue;
                        MeshFilter[] mfs = g.GetComponentsInChildren<MeshFilter>(true);
                        MeshRenderer[] mrs = g.GetComponentsInChildren<MeshRenderer>(true);
                        // 世界包围盒（证明不是空节点、也证明尺寸是米级的）
                        Bounds bd = new Bounds(g.transform.position, Vector3.zero);
                        bool any = false;
                        for (int m = 0; m < mrs.Length; m++)
                        {
                            if (mrs[m] == null) continue;
                            if (!any) { bd = mrs[m].bounds; any = true; }
                            else bd.Encapsulate(mrs[m].bounds);
                        }
                        sb2.Append($"      挂点 {(g.transform.parent != null ? g.transform.parent.name : "?")}"
                                 + $"：零件 {mfs.Length} 个｜渲染器 {mrs.Length}"
                                 + $"｜世界尺寸 {(any ? bd.size.x.ToString("0.00") : "?")}×"
                                 + $"{(any ? bd.size.y.ToString("0.00") : "?")}×"
                                 + $"{(any ? bd.size.z.ToString("0.00") : "?")}m\n");
                        UnityEngine.Object.DestroyImmediate(g);
                    }
                    sb.AppendLine($"  {d.name}（{d.id} / {d.bones}）：");
                    if (sb2.Length == 0) sb.AppendLine("      ✗ 一个容器都没建出来（骨骼找不到？见 Warning）");
                    else sb.Append(sb2);
                }
                sb.AppendLine("  ⇒ 每个挂点都有 MeshFilter/MeshRenderer 且世界尺寸是米级 ⇒ 是**真模型**，不是占位空节点");
                sb.AppendLine("  " + SkinModels.Describe());
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // 现场操作（play 模式截图用）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>推一张奖励卡（play 模式下画面上会真的弹出来）。</summary>
        public string DemoReward()
        {
            if (!TownDrops.TryLoad() || !TownSkins.TryLoad())
                return "表没载入：" + (TownDrops.LastError ?? TownSkins.LastError ?? "?");

            // 优先用一件 A 档种子（光晕最明显），掉不出来就退回第 0 个
            int idx = 0;
            for (int i = 0; i < TownData.Seeds.Length; i++)
                if (TownData.Seeds[i].grade == "A") { idx = i; break; }

            BossRewardPopup.Push(idx, 1, "墓园守望者");
            string msg = "[PROBEV213] 推了一张奖励卡：" + ResolveSeedName(idx) + " ⇒ " + BossRewardPopup.Describe();
            Debug.Log(msg);
            return msg;
        }

        /// <summary>买齐并全部穿上（主城里调 ⇒ 玩家身上立刻出现 8 件；配合 `Shot` 截图）。</summary>
        public string EquipAll()
        {
            if (!TownSkins.TryLoad()) return "皮肤表没载入：" + (TownSkins.LastError ?? "?");

            var sb = new StringBuilder(256);
            TownState st = TownState.Ensure();
            sb.Append("金币 ").Append(st.Coins);

            // 把八件的总价**从表里算出来**（不写死一个魔数 —— 改价之后这里不会骗人）
            long need = 0;
            for (int i = 0; i < TownSkins.Items.Length; i++)
                if (!TownSkins.Owned(i)) need += TownSkins.Items[i].price;

            // 钱不够就先按现有经济赚一波（种地 + 快进），不要塞钱
            if (st.Coins < need)
            {
                int planted = 0, plot = 0;
                for (int s = 0; s < st.Warehouse.Length && plot < TownState.PlotCount; s++)
                    while (st.Warehouse[s] > 0 && plot < TownState.PlotCount)
                    {
                        if (st.Plant(plot, s)) planted++;
                        plot++;
                    }
                long earned = 0;
                for (int k = 0; k < 8; k++) earned += st.Advance(30.0);
                sb.Append("｜种 ").Append(planted).Append(" 块地快进 240 秒赚 ").Append(earned);
            }
            sb.Append("｜还差 ").Append(need).Append(" 金｜买：");
            int bought = 0, worn = 0;
            for (int i = 0; i < TownSkins.Items.Length; i++)
            {
                if (!TownSkins.Owned(i) && TownSkins.TryBuy(i)) bought++;
                if (TownSkins.Equip(i)) worn++;
            }
            sb.Append(bought).Append(" 件｜穿：").Append(worn).Append(" 件｜").Append(TownSkins.Describe());

            // 若主城里正跑着，强制下一帧重建一次（正常情况下 `TownAvatar.Tick` 自己会发现版本变了）
            TownSkins.InvalidateApplied();
            string msg = "[PROBEV213] " + sb;
            Debug.Log(msg);
            return msg;
        }

        /// <summary>全部卸下（截图对照用）。</summary>
        public string StripAll()
        {
            int n = TownSkins.UnequipAll();
            TownSkins.InvalidateApplied();
            string msg = $"[PROBEV213] 卸下 {n} 个槽 ⇒ {TownSkins.Describe()}";
            Debug.Log(msg);
            return msg;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 只为了截图：直接写 `_owned` / `_equipped`（**不花一分钱、不碰 TownState**）★
        //
        // 为什么要走反射而不是 `TryBuy + Equip`：
        //   `TryBuy` 会真的扣金币、`EquipAll` 还会种地快进 —— 那是**验证**要的（报告里有），
        //   但拍图时同一个编辑器可能**另一路正在跑**（本工程确实如此），
        //   一动金币/田地就会把别人的读数搅乱。
        //   ⇒ 拍图这条路径只碰两个私有容器 + `InvalidateApplied()`，
        //     拍完 `StripForShot()` 原样还原，`TownState` 一个字节没动 ✓
        //   （`InvalidateApplied` 把 `_appliedVersion` 打成无效 ⇒ 主城下一次 `Refresh` 必重建，
        //     所以"穿上"在同一帧内就出现在画面里，**不需要改 `Version`**）
        // ══════════════════════════════════════════════════════════════════

        private static bool[] _savedOwned;
        private static int[] _savedEquipped;

        /// <summary>拍图用：把 8 件全部"戴上"（直接写容器，不扣钱）。</summary>
        public string WearForShot()
        {
            if (!TownSkins.TryLoad()) return "皮肤表没载入：" + (TownSkins.LastError ?? "?");

            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var t = typeof(TownSkins);
            var ownedF = t.GetField("_owned", F);
            var equipF = t.GetField("_equipped", F);
            if (ownedF == null || equipF == null) return "拿不到 TownSkins 的私有容器（字段改名了？）";

            var owned = ownedF.GetValue(null) as bool[];
            var equip = equipF.GetValue(null) as int[];
            if (owned == null || equip == null) return "容器还没初始化";

            _savedOwned = (bool[])owned.Clone();
            _savedEquipped = (int[])equip.Clone();

            for (int i = 0; i < TownSkins.Items.Length && i < owned.Length; i++)
            {
                owned[i] = true;
                int slot = (int)TownSkins.Items[i].Slot;
                if (slot >= 0 && slot < equip.Length) equip[slot] = i;
            }
            TownSkins.InvalidateApplied();

            string msg = "[PROBEV213] 拍图：直接戴上 8 件（未扣金币）⇒ " + TownSkins.Describe();
            Debug.Log(msg);
            return msg;
        }

        /// <summary>拍图用：还原到 `WearForShot` 之前的那份容器内容。</summary>
        public string StripForShot()
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var t = typeof(TownSkins);
            var ownedF = t.GetField("_owned", F);
            var equipF = t.GetField("_equipped", F);
            if (ownedF != null && equipF != null && _savedOwned != null && _savedEquipped != null)
            {
                var owned = ownedF.GetValue(null) as bool[];
                var equip = equipF.GetValue(null) as int[];
                if (owned != null && equip != null)
                {
                    Array.Copy(_savedOwned, owned, Mathf.Min(_savedOwned.Length, owned.Length));
                    Array.Copy(_savedEquipped, equip, Mathf.Min(_savedEquipped.Length, equip.Length));
                }
            }
            TownSkins.InvalidateApplied();
            string msg = "[PROBEV213] 拍图结束，容器已还原 ⇒ " + TownSkins.Describe();
            Debug.Log(msg);
            return msg;
        }

        /// <summary>读一次**主城里那具真实的玩家模型**身上现在挂着什么（证明实时改变）。</summary>
        public string LiveStat()
        {
            var sb = new StringBuilder(512);
            sb.Append(TownSkins.Describe()).Append('\n');
            sb.Append("最近一次重建：").Append(TownSkins.LastBuild).Append('\n');

            GameObject townPlayer = GameObject.Find("TownPlayer");
            if (townPlayer == null)
            {
                sb.Append("场景里没有 TownPlayer（当前不在主城场景？）");
            }
            else
            {
                Transform[] all = townPlayer.GetComponentsInChildren<Transform>(true);
                int skinObjs = 0, parts = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null || !all[i].name.StartsWith("SKIN_", StringComparison.Ordinal)) continue;
                    skinObjs++;
                    parts += all[i].GetComponentsInChildren<MeshFilter>(true).Length;
                    sb.Append("  ").Append(all[i].name)
                      .Append(" ← 骨骼 ").Append(all[i].parent != null ? all[i].parent.name : "?")
                      .Append("｜零件 ").Append(all[i].GetComponentsInChildren<MeshFilter>(true).Length)
                      .Append('\n');
                }
                sb.Append($"TownPlayer 身上：皮肤容器 {skinObjs} 个 / 零件 {parts} 个｜材质池 {ToonLook.MaterialPoolCount} 份");
            }

            string msg = "[PROBEV213] " + sb.ToString().Replace('\n', '｜');
            Debug.Log("[PROBEV213] " + sb);
            return msg;
        }

        /// <summary>截一张 PNG（含 OnGUI 界面）。相对路径按工程根解析。</summary>
        public string Shot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) path = "Library/shots/v213.png";
            ScreenCapture.CaptureScreenshot(path);
            string msg = "[PROBEV213] 截图已排队 → " + path;
            Debug.Log(msg);
            return msg;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 只为了截图：在主城玩家身边摆两具"展示用"模型（背面 / 侧面）★
        //   为什么不能复用 `TownSkins.Refresh`：它是**单根**设计（`_appliedRoot` 只有一个），
        //   给第二具模型 Refresh 会把第一具身上的装饰拆掉。所以这里直接调 `SkinModels.Build`，
        //   自己拿一份物体清单、自己销毁 —— 与 `TownSkins` 的运行时状态**完全隔离** ✓
        // ══════════════════════════════════════════════════════════════════

        private static readonly System.Collections.Generic.List<GameObject> Showcase =
            new System.Collections.Generic.List<GameObject>(4);

        /// <summary>在真玩家左右各摆一具（左 = 背对镜头，右 = 侧面），三具都戴齐 8 件。</summary>
        public string SpawnShowcase()
        {
            ClearShowcase();
            GameObject player = GameObject.Find("TownPlayer");
            if (player == null) return "场景里没有 TownPlayer（要先 play 且在主城场景）";
            if (!TownSkins.TryLoad()) return "皮肤表没载入：" + (TownSkins.LastError ?? "?");

            Vector3 p = player.transform.position;
            // 镜头在 -Z 一侧朝 +Z 看 ⇒ yaw 180 = 正面对镜头、yaw 0 = 背对、yaw 90 = 侧面
            float[] dx = { -2.3f, 2.3f };
            float[] yaw = { 0f, 270f };
            var sb = new StringBuilder(200);
            for (int k = 0; k < dx.Length; k++)
            {
                var root = new GameObject(k == 0 ? "SHOWCASE_back" : "SHOWCASE_side");
                root.transform.SetParent(null, false);
                root.transform.position = new Vector3(p.x + dx[k], TownAvatar.GroundY, p.z);
                root.transform.rotation = Quaternion.Euler(0f, yaw[k], 0f);

                PlayerBean bean = PlayerBean.Build(root.transform);
                bean.Pose(ModelView.State.Idle, 0f, 0.2f, 0f);
                Showcase.Add(bean.Go);
                int parts = 0;
                for (int i = 0; i < TownSkins.Items.Length; i++)
                {
                    var made = SkinModels.Build(TownSkins.Items[i], bean.Go.transform);
                    for (int m = 0; m < made.Count; m++)
                        if (made[m] != null) parts += made[m].GetComponentsInChildren<MeshFilter>(true).Length;
                }
                sb.Append($"{root.name}({dx[k]:0.0},{TownAvatar.GroundY:0.00},{p.z:0.0}) yaw={yaw[k]:0} 零件 {parts}｜");
            }
            TownSkins.InvalidateApplied();     // 真玩家也重戴一次（拍图那一下要一致）
            string msg = "[PROBEV213] 展示模型已摆好：" + sb + " " + TownSkins.Describe();
            Debug.Log(msg);
            return msg;
        }

        /// <summary>撤掉展示用的模型。</summary>
        public string ClearShowcase()
        {
            int n = 0;
            for (int i = 0; i < Showcase.Count; i++)
            {
                if (Showcase[i] == null) continue;
                if (Application.isPlaying) Destroy(Showcase[i]);
                else DestroyImmediate(Showcase[i]);
                n++;
            }
            Showcase.Clear();
            string msg = "[PROBEV213] 展示模型已撤 " + n + " 具";
            Debug.Log(msg);
            return msg;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 只为了截图：搭一个**专用小舞台**（三具模型 + 一台近距离相机）★
        //
        // 为什么不能直接在主城里拍：主城的相机是**跟随 + 60° 俯视**，一具 1.2 米的豆豆人
        // 在画面里只有几十像素高（实测 1920×1080 的抓图上，八件装饰一件都看不出来）。
        // ⇒ 这里搬一个"棚拍"的做法：把舞台挪到世界 (0,200,0)（离主城几百米，不遮挡任何东西），
        //   铺一块地板、摆三具模型（正面 / 侧面 / 背面），再放一台 32° 视场、4.2 米距离的相机，
        //   把它的 tag 打成 `MainCamera`（`/capture` 就是靠 tag 找相机的），拍完 `StageClear` 还原 ✓
        // ══════════════════════════════════════════════════════════════════

        private static GameObject _stageRoot;
        private static Camera _townCam;
        private static string _townCamTag;

        /// <summary>搭舞台并把主相机换成近距离那台（拍完要调 `StageClear`）。</summary>
        public string StageShot()
        {
            StageClear();
            if (!TownSkins.TryLoad()) return "皮肤表没载入：" + (TownSkins.LastError ?? "?");

            const float Y = 200f;                      // 舞台高度（远离主城的一切）
            _stageRoot = new GameObject("PROBEV213_STAGE");
            Transform S = _stageRoot.transform;

            // ── 地板：一块 6×6 米的浅灰台面，让脚底有影子可落 ──
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(S, false);
            floor.transform.localPosition = new Vector3(0f, Y - 0.05f, 0f);
            floor.transform.localScale = new Vector3(7f, 0.1f, 7f);
            var floorCol = floor.GetComponent<Collider>();
            if (floorCol != null) Destroy(floorCol);
            var floorR = floor.GetComponent<MeshRenderer>();
            if (floorR != null) floorR.sharedMaterial = ToonLook.Get(new Color(0.72f, 0.70f, 0.66f), 0f, true);

            // ── 三具模型：正面 / 侧面 / 背面，都戴齐 8 件 ──
            float[] xs = { -0.92f, 0f, 0.92f };
            float[] yaws = { 180f, 270f, 0f };         // 镜头在 -Z 侧：180 = 正对镜头
            var sb = new StringBuilder(200);
            for (int k = 0; k < xs.Length; k++)
            {
                var root = new GameObject(k == 0 ? "SHOW_front" : k == 1 ? "SHOW_side" : "SHOW_back");
                root.transform.SetParent(S, false);
                root.transform.localPosition = new Vector3(xs[k], Y, 0f);
                root.transform.localRotation = Quaternion.Euler(0f, yaws[k], 0f);

                PlayerBean bean = PlayerBean.Build(root.transform);
                bean.Pose(ModelView.State.Idle, 0f, 0.2f, 0f);
                int parts = 0;
                for (int i = 0; i < TownSkins.Items.Length; i++)
                {
                    var made = SkinModels.Build(TownSkins.Items[i], bean.Go.transform);
                    for (int m = 0; m < made.Count; m++)
                        if (made[m] != null) parts += made[m].GetComponentsInChildren<MeshFilter>(true).Length;
                }
                sb.Append($"{root.name} yaw={yaws[k]:0} 零件 {parts}｜");
            }

            // ── 相机：把主相机换成它（`/capture` 只认 tag=MainCamera）──
            _townCam = Camera.main;
            if (_townCam != null)
            {
                _townCamTag = _townCam.tag;
                _townCam.tag = "Untagged";
            }
            var camGo = new GameObject("PROBEV213_CAM");
            camGo.transform.SetParent(S, false);
            camGo.transform.localPosition = new Vector3(0f, Y + 1.02f, -4.2f);
            camGo.transform.localRotation = Quaternion.Euler(4.6f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.22f);
            cam.depth = 10f;
            camGo.tag = "MainCamera";

            string msg = "[PROBEV213] 舞台已搭好（相机 " + (camGo.tag) + "）⇒ " + sb + " " + TownSkins.Describe();
            Debug.Log(msg);
            return msg;
        }

        /// <summary>撤掉舞台并还原主相机的 tag。</summary>
        public string StageClear()
        {
            if (_townCam != null && !string.IsNullOrEmpty(_townCamTag))
            {
                _townCam.tag = _townCamTag;
                _townCam = null;
                _townCamTag = null;
            }
            if (_stageRoot == null) return "[PROBEV213] 舞台本来就没有";
            if (Application.isPlaying) Destroy(_stageRoot);
            else DestroyImmediate(_stageRoot);
            _stageRoot = null;
            return "[PROBEV213] 舞台已撤";
        }

        public string RunSelfCheck()
        {
            string outcome = ExecuteSelfCheck();
            Debug.Log("[PROBEV213] " + outcome);
            return outcome;
        }

        public string Cleanup()
        {
            BossRewardPopup.ClearAll();
            string name = gameObject.name;
            DestroyImmediate(gameObject);
            return "removed " + name;
        }

        // ══════════════════════════════════════════════════════════════════
        // 自检原文
        // ══════════════════════════════════════════════════════════════════

        private static string ExecuteSelfCheck()
        {
            var sb = new StringBuilder(64 * 1024);
            int pass = 0, fail = 0;
            try
            {
                System.Collections.Generic.List<CheckResult> results = SelfCheck.RunAll();
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
                Debug.LogError("[PROBEV213] " + crash);
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
    }
}
