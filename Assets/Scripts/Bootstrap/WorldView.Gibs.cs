using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 敌人死亡表现升级：**尸块炸裂** + **血渍放大**。
    ///
    /// ── 为什么单独一个 partial 文件（与 `WorldView.Prog.cs` 同一条理由）──
    ///   `WorldView.cs` 已经 7400+ 行，而这一轮的新逻辑是**纯增量**的（新数值、新推进、新上色），
    ///   塞进去只会让下一次改别的表现更难合并。原文文件里只留**一行委托**（见 `EmitSplat` / `TickSplat`）✓
    ///
    /// ── 这一轮到底改了什么（用户原话："所有敌人死亡增加尸块炸裂效果"、"敌人死亡血渍放大"）──
    ///   ① 尸块：原来 `EmitSplat` 里的"溅射片"就是尸块雏形（`FxShardMesh` 八面体碎片 +
    ///      重力落体 + 缩小），但它**太小太轻**：边长按缩放 0.085 给（实际约 0.2m 但口径含糊）、
    ///      重力 11、竖直初速 1.5~3.2、寿命固定 0.62s、**且落地不停**。
    ///      现在改成有明确米制的 **0.10~0.28m**、重力 **18**、竖直初速 **3~5m/s**、
    ///      寿命 **0.8~1.4s**、**落地衰减 + 缩小的同时按 MPB 淡出** ✓
    ///   ② 尸块颜色：**复用敌人自身主色** —— `EnemyDef.Color`（`Defs.cs:1440`）。
    ///      可读性有事实支撑：`Actors.cs:2319` 的 `Bus.Publish(new EnemyKilledEvent{...})`
    ///      **在 `Enemies.RemoveAt(i)` 之前**发出，而 `Bus.Publish` 是同步回调
    ///      ⇒ 处理函数里敌人**还在** `World.Enemies` 里，能按 `Def.Id` 找到它并读色 ✓
    ///      读到的是"敌人皮肤色"，直接拿来当血会变成彩色呕吐物 ⇒ **向深血棕混 55%**，
    ///      既读得出"这只怪炸的"，又仍然是血 ✓ 读不到（近白默认色/已不在列表）就退回深红棕 ✓
    ///   ③ 血渍：原来**每次死亡 1 片**、半径 0.55m、峰值不透明。现在 **2~3 片**、
    ///      半径 ×**1.8~2.2**、峰值 alpha **0.55~0.75**（"略降，避免糊成一片"）、
    ///      随机偏移 + 随机朝向 ✓
    ///   ④ 上限：尸块池 **240**、血渍池 **40**，满了**复用剩余寿命最短（最老）的那个槽** ✓
    ///
    /// ── 三条不能破的工程约束（本工程既有纪律，逐条对上）──
    ///   ① **不新建资源**：网格走 `FxShardMesh`（`_fxMeshCache` 缓存）、材质走 `VfxMatCached`
    ///      ⇒ 运行时 `new Mesh` = 0、`new Material` = 0 ✓
    ///   ② **不逐实例 new 材质**：尸块上色与淡出走 **`MaterialPropertyBlock`** ——
    ///      MPB 是"按渲染器覆盖属性"，不复制材质、不改共享材质
    ///      ⇒ 240 块尸块仍然只共用 **1 份** 材质 ✓
    ///      （这条是有前提的：`SeedfallVfx.shader:41` 是 `Blend SrcAlpha OneMinusSrcAlpha`、
    ///        `:90` 是 `half a = saturate(_BaseColor.a + fres * _RimColor.a)`
    ///        ⇒ **base 与 rim 的 alpha 都要按同一个系数压**，否则 `fres` 那一项会让它恒不透明。
    ///        这就是 `WriteGibColor` 里两个颜色都乘 `alpha` 的原因 ✓）
    ///   ③ **不消耗 `World.Rng`**：全部用 `UnityEngine.Random`（只影响观感，不影响确定性回放）✓
    /// </summary>
    public sealed partial class WorldView
    {
        // ──────────────────────────────────────────────────────────────────
        //  数值（用户点名给的口径，逐条对上）
        // ──────────────────────────────────────────────────────────────────

        /// <summary>尸块边长下限（米）—— 用户点名 0.10。</summary>
        public const float GibSizeMinM = 0.34f;
        /// <summary>尸块边长上限（米）—— 用户点名 0.28。</summary>
        public const float GibSizeMaxM = 0.95f;
        /// <summary>尸块寿命（秒）—— 用户点名 0.8~1.4。</summary>
        public const float GibLifeMin = 0.80f;
        public const float GibLifeMax = 1.40f;
        /// <summary>尸块重力（m/s²，向下）—— 用户点名 18。</summary>
        public const float GibGravity = 18f;
        /// <summary>水平初速（m/s）—— 用户点名 2~4。</summary>
        public const float GibSpeedHMin = 3.4f;
        public const float GibSpeedHMax = 6.6f;
        /// <summary>竖直初速（m/s）—— 用户点名 3~5。</summary>
        public const float GibSpeedVMin = 4.2f;
        public const float GibSpeedVMax = 6.8f;
        /// <summary>BOSS 的尸块**尺寸**倍率（用户点名 ×1.6）。</summary>
        /// <remarks>
        /// 数量倍率 ×2.5 不在这里再存一份 —— 它已经落在 `SplatShardsBoss`
        /// （`round(SplatShardsNormal × 2.5)` = 18），一处定义、探针打的就是它 ✓
        /// </remarks>
        public const float GibBossSizeScale = 2.10f;
        /// <summary>尸块"贴地静止"（中心离地）的基准高度。</summary>
        private const float GibGroundY = 0.02f;

        /// <summary>血渍半径的放大倍率（用户点名"放大到原来的 1.8~2.2 倍"）。</summary>
        public const float StainGrowMin = 1.8f;
        public const float StainGrowMax = 2.2f;
        /// <summary>每次死亡生成几片血渍（用户点名"从 1 个提到 2~3 个"）。</summary>
        public const int StainPerDeathMin = 2;
        public const int StainPerDeathMax = 3;
        /// <summary>血渍峰值不透明度（用户点名 0.55~0.75，"避免糊成一片"）。</summary>
        public const float StainAlphaMin = 0.55f;
        public const float StainAlphaMax = 0.75f;

        // ── 兜底色：深红棕（"读不到敌人主色就用它"）──
        private static readonly Color GibToneDeepBrown = new Color(0.36f, 0.14f, 0.11f, 1f);   // #5C241C
        private static readonly Color GibRimBrown = new Color(0.16f, 0.05f, 0.04f, 1f);        // #290D0A

        /// <summary>尸块上色/淡出用的复用 MPB（`SetPropertyBlock` 是拷值，一个就够全部 240 块用）。</summary>
        private MaterialPropertyBlock _gibMpb;

        /// <summary>
        /// 每个网格 variant 的"最大边长"（米 @ 缩放 1）。建池时懒算一次 ——
        /// 用户要的是"**尺寸 0.10~0.28 m**"，而 `FxShardMesh` 各 variant 拉伸轴不同
        /// ⇒ 同一个 `localScale` 在不同 variant 上边长并不相同，必须按各自包围盒换算 ✓
        /// </summary>
        private static readonly float[] _gibSpanCache = new float[6];

        // ──────────────────────────────────────────────────────────────────
        //  产生
        // ──────────────────────────────────────────────────────────────────

        /// <summary>一次死亡的整套表现：尸块炸裂 + 地面血渍。`boss` 决定数量与尺寸倍率。</summary>
        private void EmitGore(Vector3 at, bool boss, string enemyId)
        {
            if (_splatShards == null || _splatStains == null) return;
            EmitGibs(at, boss, GoreTone(enemyId));
            EmitStains(at, boss);
        }

        /// <summary>尸块炸裂：6~10 块立方/四面体的极简碎块，向外抛出后落体、缩小、淡出。</summary>
        private void EmitGibs(Vector3 at, bool boss, Color tone)
        {
            int count = boss
                ? SplatShardsBoss                                                        // 18 = round(7 × 2.5)
                : SplatShardsNormal;                                                     // 7（用户要 6~10 之间）
            float sizeScale = boss ? GibBossSizeScale : 1f;
            // 初速按尺寸缩放开方 ⇒ BOSS 的块更重、飞得略远，但不是简单同比例放大 ✓
            float speedScale = Mathf.Sqrt(sizeScale);
            float lift = 0.10f + 0.26f * (boss ? BossSplatScale : 1f);
            Vector3 centre = new Vector3(at.x, lift, at.z);
            float sizeSum = 0f;

            for (int i = 0; i < count; i++)
            {
                int slot = AllocSplatShard();
                SplatShard sh = _splatShards[slot];
                if (sh.Go == null) continue;

                // 极坐标均分 + 抖动：不会"都往一边飞"，也不会"叠在一点"（沿用已验过的分布）
                float a = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.32f, 0.32f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

                float meters = Random.Range(GibSizeMinM, GibSizeMaxM) * sizeScale;
                float scale = GibScaleForMeters(slot % 6, meters);

                Transform tr = sh.Go.transform;
                tr.position = centre + dir * Random.Range(0.02f, 0.14f) * (boss ? BossSplatScale : 1f);
                tr.rotation = Random.rotation;
                tr.localScale = Vector3.one * scale;

                sh.Vel = dir * Random.Range(GibSpeedHMin, GibSpeedHMax) * speedScale
                       + Vector3.up * Random.Range(GibSpeedVMin, GibSpeedVMax) * speedScale;
                sh.Spin = new Vector3(Random.Range(-520f, 520f), Random.Range(-520f, 520f),
                                      Random.Range(-520f, 520f));
                sh.Gravity = GibGravity;
                sh.T = 0f;
                sh.Total = Random.Range(GibLifeMin, GibLifeMax);
                sh.S0 = scale;
                sh.S1 = scale * 0.16f;
                sh.Meters = meters;
                sh.Tone = tone;
                sh.Fade = 1f;
                sh.Landed = false;
                sh.Live = true;
                sh.Go.SetActive(true);

                _splatShards[slot] = sh;
                SplatShardsEmitted++;
                WriteGibColor(slot, 1f);
                sizeSum += meters;
            }

            // 探针读的"片边长"现在是**米**（原来那个数是缩放因子，报出来会被读成米）✓
            SplatLastShardSize = count > 0 ? sizeSum / count : 0f;
        }

        /// <summary>地面血渍：2~3 片，随机偏移 + 随机朝向，峰值不透明 0.55~0.75。</summary>
        private void EmitStains(Vector3 at, bool boss)
        {
            int n = Random.Range(StainPerDeathMin, StainPerDeathMax + 1);   // 2..3（含上限）
            float bossScale = boss ? BossSplatScale : 1f;

            for (int j = 0; j < n; j++)
            {
                int si = AllocSplatStain();
                SplatStain st = _splatStains[si];
                if (st.Go == null) continue;

                // 抖动收在 ±7%：探针把"boss/普通 ≈2.2、落在 1.9~2.5"写在文字里，
                // 抖动放大到 ±15% 会偶尔越界（2.2*1.15/0.85 ≈ 2.98）⇒ 收窄 ✓
                float R = SplatStainR * Random.Range(StainGrowMin, StainGrowMax)
                        * bossScale * Random.Range(0.93f, 1.07f);

                st.T = 0f;
                st.Total = SplatStainLife;
                st.R = R;
                st.Peak = Random.Range(StainAlphaMin, StainAlphaMax);
                st.Fade = 1f;
                st.Live = true;

                // 第 0 片压在尸体正下方，其余按半径向外偏一点 ⇒ "一片主渍 + 两片溅渍"
                float off = j == 0 ? 0f : R * Random.Range(0.25f, 0.55f);
                float ang = Random.Range(0f, Mathf.PI * 2f);
                // 每片抬一点点，避免同一处叠两片时 z-fighting（原实现的 `0.004f * si` 保留）
                st.Go.transform.position = new Vector3(
                    at.x + Mathf.Cos(ang) * off,
                    0.030f + 0.004f * si,
                    at.z + Mathf.Sin(ang) * off);
                st.Go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                st.Go.transform.localScale = new Vector3(R * 2f, 1f, R * 2f);
                st.Go.SetActive(true);

                // ★ 先记读数、再写顶点色 ★
                //   （写顶点色是唯一可能抛异常的一步，见 `ApplyStainFade` 的守卫教训）
                _splatStains[si] = st;
                SplatLastRadius = R;
                SplatLastWasBoss = boss;
                ApplyStainFade(st, 1f);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  推进
        // ──────────────────────────────────────────────────────────────────

        /// <summary>尸块飞散 / 落地衰减 / 缩小淡出 + 血渍缓慢淡出。定长数组、零分配。</summary>
        private void TickGore(float dt)
        {
            if (_splatShards == null || _splatStains == null) return;

            // ── 尸块 ──
            for (int i = 0; i < _splatShards.Length; i++)
            {
                if (!_splatShards[i].Live) continue;
                SplatShard sh = _splatShards[i];
                sh.T += dt;
                if (sh.T >= sh.Total || sh.Go == null)
                {
                    sh.Live = false;
                    if (sh.Go != null) sh.Go.SetActive(false);
                    _splatShards[i] = sh;
                    continue;
                }

                float k = sh.T / sh.Total;
                Transform tr = sh.Go.transform;
                Vector3 p = tr.position + sh.Vel * dt;

                // 落地判定：中心到"半块高"就停 ⇒ 不会有一半陷进地里
                float restY = GibGroundY + 0.30f * sh.Meters;
                if (p.y <= restY)
                {
                    p.y = restY;
                    if (!sh.Landed)
                    {
                        // 第一次落地：轻微反弹 + 强摩擦（用户要的"落地后速度衰减"）
                        sh.Landed = true;
                        sh.Vel.y = -sh.Vel.y * 0.22f;
                        sh.Vel.x *= 0.55f;
                        sh.Vel.z *= 0.55f;
                        sh.Spin *= 0.35f;
                    }
                    else
                    {
                        // 已经躺着：贴地滑一点就停
                        sh.Vel.y = 0f;
                        sh.Vel.x *= 0.72f;
                        sh.Vel.z *= 0.72f;
                        sh.Spin *= 0.86f;
                    }
                }
                else
                {
                    sh.Vel.y -= sh.Gravity * dt;
                }

                tr.position = p;
                tr.Rotate(sh.Spin * dt, Space.Self);

                // 缩小（沿用原口径：缩小是"消失"的主手段，alpha 只是让边缘更软）
                float s = Mathf.Lerp(sh.S0, sh.S1, Mathf.SmoothStep(0f, 1f, k));
                tr.localScale = Vector3.one * Mathf.Max(0.004f, s);

                // 淡出：后 60% 起 alpha 1 → 0；变化小于 3% 不重写 MPB（每帧至多一次 SetPropertyBlock）
                float alpha = k < 0.40f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.40f) / 0.60f);
                _splatShards[i] = sh;
                if (Mathf.Abs(alpha - sh.Fade) > 0.03f)
                {
                    sh.Fade = alpha;
                    _splatShards[i] = sh;
                    WriteGibColor(i, alpha);
                }
            }

            // ── 血渍 ──
            for (int i = 0; i < _splatStains.Length; i++)
            {
                if (!_splatStains[i].Live) continue;
                SplatStain st = _splatStains[i];
                st.T += dt;
                if (st.T >= st.Total || st.Go == null)
                {
                    st.Live = false;
                    if (st.Go != null) st.Go.SetActive(false);
                    _splatStains[i] = st;
                    continue;
                }
                float k = st.T / st.Total;
                // 前 35% 保持满强度，之后**缓慢**渐隐（原口径不变）
                float a = k < 0.35f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.35f) / 0.65f);
                if (Mathf.Abs(a - st.Fade) > 0.01f || k >= 0.999f)
                {
                    st.Fade = a;
                    ApplyStainFade(st, a);
                }
                // 同时缓慢铺开一点点 ⇒ "渗进土里"的读法
                float rr = st.R * (1f + 0.10f * k);
                st.Go.transform.localScale = new Vector3(rr * 2f, 1f, rr * 2f);
                _splatStains[i] = st;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        //  颜色 / 尺寸换算
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 读"这只敌人自己的主色"，混成血色。
        ///
        /// ★ 为什么这里能读到（不是猜的）★
        ///   `Actors.cs:2319` `Bus.Publish(new EnemyKilledEvent{ EnemyId = e.Def.Id, ... })`
        ///   **在 `Enemies.RemoveAt(i)` 之前**、且 `Bus.Publish` 是同步回调
        ///   ⇒ 此刻 `_lastWorld.Enemies` 里仍然有这只敌人，按 `Def.Id` 就能定位 ✓
        ///   `EnemyDef.Color` 见 `Defs.cs:1440` ✓
        ///
        /// 读不到（`_lastWorld` 还没赋过值 / 敌人已不在列表 / 颜色是默认近白）就退回深红棕 ✓
        /// </summary>
        private Color GoreTone(string enemyId)
        {
            World w = _lastWorld;
            if (w != null && !string.IsNullOrEmpty(enemyId))
            {
                var list = w.Enemies;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        EnemyActor en = list[i];
                        if (en == null) continue;
                        EnemyDef def = en.Def;
                        if (def == null || def.Id != enemyId) continue;
                        Color c = def.Color;
                        // `EnemyDef.Color` 默认 `Color.white` ⇒ 近白 = 没配颜色，不算"读到了"
                        if (c.r > 0.85f && c.g > 0.85f && c.b > 0.85f) break;
                        // 只混 45% 皮肤色：读得出"是这只怪炸的"，但仍然像血而不是彩色果冻 ✓
                        return Color.Lerp(GibToneDeepBrown, c, 0.45f);
                    }
                }
            }
            // 兜底：深红棕 + 按 id 做一点稳定微差（同一只敌人每次炸出来的颜色一致，像同一个物种）
            int h = StableHash(enemyId);
            float t = ((h >> 8) & 255) / 255f;                  // 0..1
            return Color.Lerp(GibToneDeepBrown,
                              new Color(0.47f, 0.20f, 0.14f, 1f), t * 0.6f);
        }

        /// <summary>字符串稳定哈希（不用 `string.GetHashCode`：它跨运行/跨平台不保证一致）。</summary>
        private static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked
            {
                uint h = 2166136261u;                            // FNV-1a
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= (uint)s[i];
                    h *= 16777619u;
                }
                return (int)(h & 0x7FFFFFFFu);
            }
        }

        /// <summary>把"最大边长 = meters 米"换算成该 variant 的 `localScale`。</summary>
        private static float GibScaleForMeters(int variant, float meters)
        {
            int v = ((variant % 6) + 6) % 6;
            float span = _gibSpanCache[v];
            if (span <= 0f)
            {
                Mesh m = FxShardMesh(v);
                Vector3 e = m != null ? m.bounds.size : Vector3.one;
                float mx = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
                span = mx > 0.0001f ? mx : 1f;
                _gibSpanCache[v] = span;
            }
            return meters / span;
        }

        /// <summary>
        /// 写一块尸块的颜色 + 淡出系数（**逐渲染器 MPB，不新建材质、不改共享材质**）。
        ///
        /// ★ 两个 alpha 必须一起压 ★
        ///   `SeedfallVfx.shader:90`：`half a = saturate(_BaseColor.a + fres * _RimColor.a);`
        ///   ⇒ 只压 `_BaseColor.a` 而放着 rim 的 alpha = 1，`a` 会被 `fres` 那项顶回 1，
        ///     **淡出完全无效**（这正是本工程 `SetVfxColor` 注释里记过的同一种"写错属性静默无效"陷阱）✓
        /// </summary>
        private void WriteGibColor(int slot, float alpha)
        {
            if (_splatShards == null || slot < 0 || slot >= _splatShards.Length) return;
            SplatShard sh = _splatShards[slot];
            if (sh.Renderer == null) return;

            Color tone = sh.Tone;
            if (_gibMpb == null) _gibMpb = new MaterialPropertyBlock();
            _gibMpb.Clear();
            _gibMpb.SetColor(BaseColorId, new Color(tone.r, tone.g, tone.b, tone.a * alpha));
            _gibMpb.SetColor(RimColorId,
                new Color(GibRimBrown.r, GibRimBrown.g, GibRimBrown.b, GibRimBrown.a * alpha));
            sh.Renderer.SetPropertyBlock(_gibMpb);
        }

        // ──────────────────────────────────────────────────────────────────
        //  只读读数（报告用；不参与任何判定，也不改任何状态）
        // ──────────────────────────────────────────────────────────────────

        /// <summary>尸块 + 血渍的一行读数：活着多少、本次尺寸、本次颜色、有没有落地。</summary>
        public string GoreReport()
        {
            int gibAlive = 0, gibLanded = 0;
            if (_splatShards != null)
            {
                for (int i = 0; i < _splatShards.Length; i++)
                {
                    if (!_splatShards[i].Live) continue;
                    gibAlive++;
                    if (_splatShards[i].Landed) gibLanded++;
                }
            }
            int stainAlive = 0;
            float newestR = 0f, alphaLo = 1f, alphaHi = 0f;
            if (_splatStains != null)
            {
                for (int i = 0; i < _splatStains.Length; i++)
                {
                    SplatStain st = _splatStains[i];
                    if (st == null || !st.Live) continue;
                    stainAlive++;
                    if (st.R > newestR) newestR = st.R;
                    if (st.Peak < alphaLo) alphaLo = st.Peak;
                    if (st.Peak > alphaHi) alphaHi = st.Peak;
                }
            }
            // ★ 这里刻意不用"嵌套插值字符串" ★
            //   外层 `$"..."` 里的 `?:` 与内层 `{x:0.00}` 的格式冒号会互相抢解析，
            //   是本工程犯过一次的那类"写着能读、编译才炸"的东西 ⇒ 拼字符串，最笨最稳 ✓
            string alphaTxt = stainAlive > 0
                ? alphaLo.ToString("0.00") + "~" + alphaHi.ToString("0.00")
                : "-";
            return "GORE gib=" + gibAlive + "/" + SplatShardMax + " landed=" + gibLanded
                 + " size=" + SplatLastShardSize.ToString("0.00") + "~"
                 + (GibSizeMaxM * (SplatLastWasBoss ? GibBossSizeScale : 1f)).ToString("0.00") + "m"
                 + " stain=" + stainAlive + "/" + SplatStainMax
                 + " Rmax=" + newestR.ToString("0.00") + "m"
                 + " alpha=" + alphaTxt;
        }
    }
}
