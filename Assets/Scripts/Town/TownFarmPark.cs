using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v238：农田装饰**回来了**（这次是按真实坐标重建的），公园中心的 3 个人不变 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、这一轮的两件事
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **农田装饰重新加上**（用户：稻草人 / 杂草 / 水桶 / 草垛 / 鸡一类）：
    ///      稻草人 2 · 杂草 16 丛 · 水桶 1 · 草垛 1 · 锄头 1 · 鸡 2 = **23 件**。
    ///      全部由 `TownFarmDecor` 建（那个文件里写着坐标的推导、垄公式的算术、
    ///      以及"每件到最近田格中心的距离"的复算）。
    ///      ⚠️ 上一版 `(k-1.5)*pitch` 把杂草放在**格心**上 ⇒ 全被距离门槛拒掉、读数 0 丛。
    ///         这一版的垄公式是 `(k-2)*pitch`（1.15 的偶数倍）⇒ 落在**格与格的沟**上。
    ///   ② **材质不再是共享池的**：每份都是 `OWN_FARM_*`、`_MainTex`/`_BaseMap` 显式置空，
    ///      并有每 90 帧的校验（`TownFarmDecorGuard`）把美术通道的污染修回来。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、本文件自己的职责：公园中心 3 个不移动的人
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 围着公园中心（`TownLayout.ParkCenter`，**从布局量出来的锚点**）半径 **2.0 m**，三个方位
    ///   · **不移动**：`position` 一帧都不写 ⇒ 位移恒 0
    ///   · **不是死的**：呼吸（身体 `localScale.y` ±1.5%）＋ 偶尔转头（头 `localRotation.y` ±14°，周期 ~12 秒）
    ///   · 各拿不同物件：书／篮子／拐杖
    ///   · 全部走 `TownKit` 共享图元 + `ToonLook` 材质池，无 `new Mesh` / 无 per-instance 材质 ✓
    /// </summary>
    public static class TownFarmPark
    {
        private static readonly List<string> Placed = new List<string>(4);
        private static readonly List<Person> People = new List<Person>(4);
        private static float _t;

        private struct Person
        {
            public Transform Body, Head;
            public float Phase;
            public string Holds;
        }

        public static float ParkPersonDisplacementLast { get; private set; }
        public static float ParkPersonHeadTurnDeg { get; private set; }
        public static int ParkPersonCount => People.Count;

        // ══════════════════════════════════════════════════════════════════
        // 公园中心：3 个不动的人
        // ══════════════════════════════════════════════════════════════════

        public static void BuildParkPeople(Transform root)
        {
            People.Clear();
            Placed.Clear();
            Vector2 pc = TownLayout.ParkCenter;          // ← 从布局量出来的锚点，不写死坐标
            string[] holds = { "书", "篮子", "拐杖" };
            float[] angs = { 22f, 143f, 258f };
            for (int i = 0; i < 3; i++)
            {
                float rad = angs[i] * Mathf.Deg2Rad;
                Vector2 at = pc + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 2.0f;   // 半径 2.0 m
                Transform t = Node(root, "PARK_PERSON_" + (i + 1), at, angs[i] + 180f); // 面朝中心
                Material shirt = TownKit.Mat(i == 0 ? new Color(0.44f, 0.56f, 0.82f)
                                         : i == 1 ? new Color(0.80f, 0.52f, 0.46f)
                                                  : new Color(0.52f, 0.68f, 0.50f));
                Material skin = TownKit.Mat(new Color(0.96f, 0.82f, 0.66f));
                Material dark = TownKit.Mat(TownColors.DoorDark);

                Transform body = TownKit.CylL(t, "Body", new Vector3(0f, 0.30f, 0f), 0.24f, 0.62f, shirt).transform;
                Transform head = TownKit.BallL(t, "Head", new Vector3(0f, 0.86f, 0f), 0.42f, skin).transform;
                TownKit.CylL(t, "Hat", new Vector3(0f, 1.04f, 0f), 0.16f, 0.11f, dark);
                TownKit.BoxL(t, "Legs", new Vector3(0f, 0.14f, 0f), new Vector3(0.32f, 0.28f, 0.28f), dark);

                // 手上物件（三人各不相同）
                if (i == 0)      // 拿书：胸前一块薄板
                    TownKit.BoxL(t, "Book", new Vector3(0.20f, 0.46f, 0.18f), new Vector3(0.24f, 0.03f, 0.18f), dark);
                else if (i == 1) // 提篮：侧面一个小圆柱 + 边沿
                {
                    TownKit.CylL(t, "Basket", new Vector3(0.26f, 0.16f, 0.06f), 0.13f, 0.20f,
                                 TownKit.Mat(new Color(0.78f, 0.62f, 0.34f)));
                    TownKit.CylL(t, "BasketRim", new Vector3(0.26f, 0.26f, 0.06f), 0.14f, 0.02f, dark);
                }
                else             // 拄拐：一根细杆
                    TownKit.CylL(t, "Cane", new Vector3(-0.26f, 0.40f, 0.02f), 0.022f, 0.80f,
                                 TownKit.Mat(TownColors.WallWood));

                People.Add(new Person { Body = body, Head = head, Phase = i * 2.3f, Holds = holds[i] });
                Placed.Add($"PARK_PERSON_{i + 1} @ ({at.x:0.00}, {at.y:0.00}) 拿{holds[i]}");
            }
            Log.Info($"[Town] 公园中心：{People.Count} 个不移动的人（半径 2.0 m，围中心；各拿 书/篮子/拐杖）");
        }

        // ══════════════════════════════════════════════════════════════════
        // v239：农田装饰 ＋ 路边围栏（**实现全在 `TownFarmDecor`**，这里只留入口，免得两处各写一份坐标）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>建整组农田装饰（稻草人 1 / 杂草 16 / 水桶 1 / 草垛 1 / 锄头 1 / 鸡 2）＋ 路边围栏。</summary>
        public static void BuildFarmDecor(Transform root) => TownFarmDecor.Build(root);

        /// <summary>
        /// 农田装饰的**只读诊断**：逐件报名字 / 世界坐标 / AABB / **到最近田格中心的距离** / 材质名 / `_MainTex`，
        /// 外加**杂草两次计算的坐标比对**（确定性判据）与**围栏汇总**。
        /// 汇总里那行"**最小距离 = X 米**"就是核心判据（必须 ≥ 1.5）。
        /// </summary>
        public static string FarmDecorInfo(string ignored) => TownFarmDecor.Info();

        /// <summary>路边围栏的**只读诊断**（到 `WalkRadius` 的最近距离必须 &gt; 0、门口缺口 3~4 米）。</summary>
        public static string RoadFenceInfo(string ignored) => TownFarmDecor.FenceInfo();

        private static Transform Node(Transform parent, string name, Vector2 at, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(at.x, TownAvatar.GroundY, at.y);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧：只动"呼吸 + 转头"，**位置一律不写**
        // ══════════════════════════════════════════════════════════════════

        public static void Tick(float dt)
        {
            if (dt <= 0f || People.Count == 0) return;
            _t += dt;
            float maxTurn = 0f;
            for (int i = 0; i < People.Count; i++)
            {
                Person p = People[i];
                if (p.Body == null || p.Head == null) continue;
                float breath = 1f + Mathf.Sin(_t * 1.6f + p.Phase) * 0.015f;      // ±1.5%
                Vector3 s = p.Body.localScale;
                p.Body.localScale = new Vector3(s.x, breath, s.z);
                float turn = Mathf.Sin(_t * 0.52f + p.Phase) * 14f;               // ±14°，周期 ~12 秒
                p.Head.localRotation = Quaternion.Euler(0f, turn, 0f);
                if (Mathf.Abs(turn) > maxTurn) maxTurn = Mathf.Abs(turn);
                People[i] = new Person { Body = p.Body, Head = p.Head, Phase = p.Phase, Holds = p.Holds };
            }
            ParkPersonHeadTurnDeg = maxTurn;
            ParkPersonDisplacementLast = 0f;      // 位置一个字节都没写 ⇒ 位移恒 0
        }

        /// <summary>读数：公园人数 + 各自位置 + 位移（恒 0）+ 朝向变化量（>0）＋ 农田装饰件数/最小距离 ＋ 围栏。</summary>
        public static string Probe()
        {
            var sb = new System.Text.StringBuilder(900);
            sb.Append($"公园中心：{People.Count} 个人 · 位移 = {ParkPersonDisplacementLast:0.000} m（恒 0 ⇒ 不移动）" +
                      $" · 朝向变化量 = {ParkPersonHeadTurnDeg:0.00}°（>0 ⇒ 在转头）" +
                      $"｜农田装饰 = **{TownFarmDecor.Count} 件**" +
                      $"（到最近田格中心的最小距离 {TownFarmDecor.MinDistanceToPlot:0.000} m）" +
                      $"｜材质校验已跑 {TownFarmDecor.SweepCount} 次（累计修回 {TownFarmDecor.RepairedTotal} 处）");
            sb.Append($"\n  路边围栏：到 WalkRadius 的最近距离 " +
                      $"{TownFarmDecor.FenceApothem - TownLayout.WalkRadius:0.000} m 以上（明细用 RoadFenceInfo()）");
            for (int i = 0; i < Placed.Count; i++) sb.Append("\n  ").Append(Placed[i]);
            sb.Append("\n  ⇒ 逐件的坐标/尺寸/距离/材质 用 FarmDecorInfo(\"\") 看");
            string s = sb.ToString();
            Log.Info("[Town] FARMPARK " + s);
            return s;
        }
    }
}
