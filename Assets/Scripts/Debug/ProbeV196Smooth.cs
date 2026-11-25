using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v196：**"移动动画平不平"三把尺子**（只读探针）★
    ///
    /// ── 为什么又开一个，而不是接着用 `ProbeV191Motion` ──
    /// 上一轮的探针量的是 **腿摆振幅** 的逐帧跳变（`振幅 max|Δ|`）——它只回答"走↔停那一刀切没切"。
    /// 这一轮的验收要的是**三条别的读数**，而它们都不在它的视野里：
    ///   ① `max|Δ²|` —— 腿摆角的**二阶差分**。一阶差分对一条光滑正弦恒等于"转速"，判不出抖；
    ///      二阶差分才是"拐得急不急" ⇒ 抽搐在它下面无处可藏 ✓（用户原话就是这个指标）
    ///   ② 反向次数 —— `Δ` 的符号翻转。★ 关键：只数**同向段 ≤2 帧**的翻转（"急反转"）★
    ///      一条干净正弦每周期本来就要翻转两次（波峰/波谷），把它也数进去只是在数"跑了几圈"，
    ///      跑得慢就"变好"——那是自欺 ✗ 而"两帧内掉头"只可能是抖 ✓
    ///   ③ 最大帧位移 ÷ 中位帧位移（**dt 归一**）——
    ///      这台编辑器实测 dt 在 0.02~0.33s 之间跳（同一台机器上还有别的自动化在跑），
    ///      原始 Δ 会被 dt 尖峰主导 ⇒ 必须换成 `Δ/dt`（m/s）再比 ✓
    /// 额外一把，专门验这一轮的改法（相位 ∝ 位移）：
    ///   ④ `相位/位移`（rad/m）及其**区间**。若相位是时间驱动的，这个比会随移速漂；
    ///      若相位 ∝ 位移，它恒等于 `1 ÷ 设计移速` ⇒ min≈max ✓
    ///
    /// ── 挂钩 ──
    /// 桥：`path=ProbeV196Smooth, method=Arm, value="帧数:tag"` → 报告落
    /// `Library/legsmooth-report.txt`（同时打控制台）。`method=Report` 取上次原文。
    ///
    /// ⚠️ **只读**：只写自己的数组，不碰任何玩法字段、不碰任何 Transform ✓
    /// </summary>
    [DefaultExecutionOrder(20010)]      // 排在 `ProbeV191Motion`（20000）之后：读的是同一帧的最终态
    [DisallowMultipleComponent]
    public sealed class ProbeV196Smooth : MonoBehaviour
    {
        /// <summary>采样窗口上限（帧）。</summary>
        public const int MaxFrames = 900;

        /// <summary>盯的四个 id（与 `Resources/Data/enemies.json` 同字；BOSS 不在范围内）。</summary>
        private static readonly string[] Species = { "grunt", "shooter", "jumper", "pig" };

        /// <summary>每个物种最多跟几只实例（够判"最差那一只"）。</summary>
        private const int MaxTracksPerSpecies = 3;

        /// <summary>出生后跳过的帧数（池复用残留姿态 / 首帧 `Play()` / `Moved()` 还没稳）。</summary>
        private const int SettleFrames = 3;

        /// <summary>只统计 `dt ≤ 这个值` 的帧做角度差分（大 dt 一帧就把正弦走完，那是 dt 的错）。</summary>
        private const float DtGate = 0.05f;

        /// <summary>算"反转"时的最小角度门槛（度）—— 低于它的是量化台阶，不是方向。</summary>
        private const float FlipDeadband = 0.5f;

        private sealed class Track
        {
            public int Id;
            public string DefId;
            public int Seen, First = -1, Last = -1;
            public readonly Vector2[] Pos = new Vector2[MaxFrames];
            public readonly float[] Dt = new float[MaxFrames];
            public readonly float[] Phase = new float[MaxFrames];
            public readonly float[] Amp = new float[MaxFrames];
            public readonly int[] State = new int[MaxFrames];
            public readonly bool[] Has = new bool[MaxFrames];
            /// <summary>六条腿的摆角（度）。`Leg[frame*6 + i]`。</summary>
            public readonly float[] Leg = new float[MaxFrames * 6];
            /// <summary>这一帧有没有腿角（小猪走旧版 FBX ⇒ 没有"腿摆角"这个概念）。</summary>
            public readonly bool[] HasLeg = new bool[MaxFrames];
        }

        private static ProbeV196Smooth _live;

        private readonly List<Track> _tracks = new List<Track>(12);
        private readonly Dictionary<string, int> _perSpecies = new Dictionary<string, int>(4);

        private bool _armed;
        private int _frames = 360;
        private int _frame;
        private string _tag = "live";
        private string _report = "";
        private float _sumDt, _maxDt;
        private int _dtCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV196Smooth>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV196Smooth");
            go.AddComponent<ProbeV196Smooth>();
        }

        private void Awake() { _live = this; }
        private void OnDestroy() { if (_live == this) _live = null; }

        /// <summary>桥的入口：布防。spec = `帧数[:tag]`，默认 `360:live`。</summary>
        public string Arm(string spec)
        {
            string text = (spec ?? string.Empty).Trim();
            int colon = text.IndexOf(':');
            _frames = 360;
            _tag = "live";
            if (colon > 0)
            {
                int.TryParse(text.Substring(0, colon), out _frames);
                _tag = text.Substring(colon + 1).Trim();
            }
            else if (text.Length > 0) int.TryParse(text, out _frames);
            if (_frames <= 0) _frames = 360;
            _frames = Mathf.Clamp(_frames, 8, MaxFrames);

            _tracks.Clear();
            _perSpecies.Clear();
            _frame = 0;
            _report = "";
            _sumDt = 0f;
            _maxDt = 0f;
            _dtCount = 0;
            _armed = true;
            string msg = "SMOOTH 已布防 帧数=" + _frames + " tag=" + _tag
                       + "（采满自动出报告：Library/legsmooth-report.txt + 控制台）";
            Log.Info("DEBUG " + msg);
            return msg;
        }

        /// <summary>上次报告原文。</summary>
        public string Report() => _report;

        /// <summary>在采集中？</summary>
        public bool Armed => _armed;

        private void LateUpdate()
        {
            if (!_armed) return;
            try { Sample(); }
            catch (System.Exception ex)
            {
                _armed = false;
                _report = "SMOOTH 采样异常：" + ex;
                Log.Warn(_report);
            }
        }

        private void Sample()
        {
            World w = CurrentWorld();
            if (w == null) return;              // 会话没起来：不推进帧号（否则把窗口烧光）

            float dt = Time.deltaTime;
            _sumDt += dt;
            _dtCount++;
            if (dt > _maxDt) _maxDt = dt;

            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.Def == null) continue;
                string defId = e.Def.Id;
                if (System.Array.IndexOf(Species, defId) < 0) continue;

                Track t = TrackFor(e.Id, defId);
                if (t == null) continue;

                int f = _frame;
                ProceduralBugs.BugView bug = ProceduralBugs.ViewOf(e.Id);
                if (bug != null && bug.Go != null)
                {
                    Vector3 p = bug.Go.transform.position;
                    t.Pos[f] = new Vector2(p.x, p.z);
                    t.Dt[f] = dt;
                    t.Phase[f] = bug.Phase;
                    t.Amp[f] = bug.SwingAmpDeg;
                    t.State[f] = (int)bug.Current;
                    for (int k = 0; k < 6; k++) t.Leg[f * 6 + k] = bug.LegSwing(k);
                    t.HasLeg[f] = true;
                    t.Has[f] = true;
                }
                else
                {
                    // 小猪走旧版 FBX（`ModelView`）：只有位置/状态，没有腿摆角与相位 ⇒ 照实标空 ✓
                    ModelView pig = ProceduralBugs.PigViewOf(e.Id);
                    if (pig == null || pig.Go == null) continue;
                    Vector3 p = pig.Go.transform.position;
                    t.Pos[f] = new Vector2(p.x, p.z);
                    t.Dt[f] = dt;
                    t.State[f] = (int)pig.Current;
                    t.Has[f] = true;
                }
                t.Seen++;
                if (t.First < 0) t.First = f;
                t.Last = f;
            }

            _frame++;
            if (_frame >= _frames) Finish();
        }

        private static World CurrentWorld()
        {
            if (GameBootstrap.Live == null) return null;
            for (int i = 0; i < GameBootstrap.Live.Count; i++)
            {
                GameBootstrap boot = GameBootstrap.Live[i];
                if (boot != null && boot.Session != null && boot.Session.World != null) return boot.Session.World;
            }
            return null;
        }

        private Track TrackFor(int id, string defId)
        {
            for (int i = 0; i < _tracks.Count; i++) if (_tracks[i].Id == id) return _tracks[i];
            _perSpecies.TryGetValue(defId, out int n);
            if (n >= MaxTracksPerSpecies) return null;
            _perSpecies[defId] = n + 1;
            var t = new Track { Id = id, DefId = defId };
            _tracks.Add(t);
            return t;
        }

        // ══════════════════════════════════════════════════════════════════
        // 读数
        // ══════════════════════════════════════════════════════════════════

        private struct Stats
        {
            public bool Ok;
            // ① 腿摆角的二阶差分
            public bool HasLeg;
            public float MaxD, MaxD2, MaxD2Norm, MedLegDelta;
            public int MaxD2Frame, MaxD2Leg;
            public int FlipTotal, FlipShort, MinRun;
            // ② 位移（dt 归一）
            public float MaxDelta, MedDelta, Spike, MaxV, MedV, VSpike;
            // ③ 相位 / 位移
            public float SumPhase, SumPos;
            public float PhasePerMeter, PhasePerMeterMin, PhasePerMeterMax;
            public int MovFrames, AmpDeltaFrames;
            public float AmpMaxStep;
        }

        private static Stats Analyze(Track t)
        {
            var st = new Stats { MinRun = -1, PhasePerMeterMin = float.MaxValue };

            var dList = new List<float>(MaxFrames);
            var vList = new List<float>(MaxFrames);
            var legD = new List<float>(MaxFrames * 6);

            Vector2 prevPos = Vector2.zero;
            bool hasPrev = false;

            int from = (t.First < 0 ? 0 : t.First) + SettleFrames;
            for (int i = from; i <= t.Last; i++)
            {
                if (!t.Has[i]) { hasPrev = false; continue; }
                if (!hasPrev || i - 1 < from || !t.Has[i - 1])
                {
                    prevPos = t.Pos[i];
                    hasPrev = true;
                    continue;
                }

                // ── ② 位移：dt 归一 ──
                Vector2 dp = t.Pos[i] - prevPos;
                float ad = dp.magnitude;
                float dtf = Mathf.Max(1e-4f, t.Dt[i]);
                float v = ad / dtf;
                dList.Add(ad);
                vList.Add(v);
                if (ad > st.MaxDelta) st.MaxDelta = ad;
                if (v > st.MaxV) st.MaxV = v;
                if (ad > 0.0005f) st.MovFrames++;

                // ── ③ 相位 / 位移 ──
                if (t.HasLeg[i] && t.HasLeg[i - 1])
                {
                    float dphase = Mathf.Abs(t.Phase[i] - t.Phase[i - 1]);
                    if (ad > 1e-4f)
                    {
                        float ratio = dphase / ad;
                        st.SumPhase += dphase;      // ★ 两个都要累加：比值 = Σ相位 ÷ Σ位移 ★
                        st.SumPos += ad;
                        if (ratio < st.PhasePerMeterMin) st.PhasePerMeterMin = ratio;
                        if (ratio > st.PhasePerMeterMax) st.PhasePerMeterMax = ratio;
                    }
                }

                // ── ① 腿摆角：一阶 / 二阶差分 ──
                if (t.HasLeg[i] && t.HasLeg[i - 1] && t.Dt[i] <= DtGate)
                {
                    st.HasLeg = true;
                    for (int k = 0; k < 6; k++)
                    {
                        float a0 = t.Leg[(i - 1) * 6 + k];
                        float a1 = t.Leg[i * 6 + k];
                        float d1 = a1 - a0;
                        legD.Add(d1);
                        if (Mathf.Abs(d1) > st.MaxD) st.MaxD = Mathf.Abs(d1);
                        // 二阶差分要**再往前一帧**的腿角
                        if (i - 2 >= from && t.HasLeg[i - 2])
                        {
                            float a0b = t.Leg[(i - 2) * 6 + k];
                            float d0 = a0 - a0b;
                            float d2 = d1 - d0;
                            if (Mathf.Abs(d2) > st.MaxD2)
                            {
                                st.MaxD2 = Mathf.Abs(d2);
                                st.MaxD2Frame = i;
                                st.MaxD2Leg = k;
                                st.MaxD2Norm = Mathf.Abs(d2) / dtf;    // °/s
                            }
                            // 反向：只数**同向段 ≤2 帧**的翻转（真抖动才长这样）
                            if (Mathf.Abs(d0) > FlipDeadband && Mathf.Abs(d1) > FlipDeadband
                                && (d0 > 0f) != (d1 > 0f)) st.FlipShort++;
                        }
                    }
                    // 振幅的逐帧跳变（上一轮那一刀的口径，留作对照）
                    float damp = Mathf.Abs(t.Amp[i] - t.Amp[i - 1]);
                    if (damp > st.AmpMaxStep) st.AmpMaxStep = damp;
                    if (t.State[i] != t.State[i - 1]) st.AmpDeltaFrames++;
                }

                prevPos = t.Pos[i];
            }

            // 同向段最小长度（腿角 Δ 的"连续几帧同向"）—— 顺带看"翻转得有多碎"
            {
                int run = 0, sign = 0;
                for (int i = 0; i < legD.Count; i++)
                {
                    if (Mathf.Abs(legD[i]) <= FlipDeadband) continue;
                    int s = legD[i] > 0f ? 1 : -1;
                    if (s == sign) { run++; }
                    else
                    {
                        if (run > 0 && (st.MinRun < 0 || run < st.MinRun)) st.MinRun = run;
                        st.FlipTotal++;
                        run = 1;
                        sign = s;
                    }
                }
                if (run > 0 && (st.MinRun < 0 || run < st.MinRun)) st.MinRun = run;
            }

            if (dList.Count < 8) return st;
            st.Ok = true;
            dList.Sort(); vList.Sort();
            st.MedDelta = dList[dList.Count / 2];
            st.MedV = vList[vList.Count / 2];
            st.Spike = st.MedDelta > 1e-5f ? st.MaxDelta / st.MedDelta : 0f;
            st.VSpike = st.MedV > 1e-5f ? st.MaxV / st.MedV : 0f;
            if (legD.Count > 0)
            {
                legD.Sort();
                st.MedLegDelta = legD[legD.Count / 2];
            }
            if (st.PhasePerMeterMin > float.MaxValue * 0.5f) st.PhasePerMeterMin = 0f;
            // ★ 比值 = Σ相位 ÷ Σ位移（只累加相位是"弧度总和"，不是 rad/m ✗ 第一版踩过）★
            st.PhasePerMeter = st.SumPos > 1e-4f ? st.SumPhase / st.SumPos : 0f;
            return st;
        }

        private void Finish()
        {
            _armed = false;
            var sb = new StringBuilder();
            sb.Append("[Seedfall] SMOOTH ").Append(_tag)
              .Append(" | 窗口=").Append(_frame).Append("帧")
              .Append(" 平均dt=").Append((_dtCount > 0 ? _sumDt / _dtCount : 0f).ToString("0.000")).Append("s")
              .Append(" 最大dt=").Append(_maxDt.ToString("0.000")).Append("s")
              .Append(" 实例=").Append(_tracks.Count).Append("只");
            sb.Append("\n  口径：腿角 = `BugView.LegSwing(i)`（度）｜只统计 dt≤")
              .Append(DtGate.ToString("0.00")).Append("s 的帧｜反转只数同向段≤2 帧的");

            for (int s = 0; s < Species.Length; s++)
            {
                string sp = Species[s];
                var mine = new List<Track>();
                for (int i = 0; i < _tracks.Count; i++) if (_tracks[i].DefId == sp) mine.Add(_tracks[i]);
                if (mine.Count == 0)
                {
                    sb.Append("\n  ").Append(sp.PadRight(8)).Append(" **未测**：窗口里没有渲染出来的实例");
                    continue;
                }
                for (int i = 0; i < mine.Count; i++)
                {
                    Stats st = Analyze(mine[i]);
                    sb.Append("\n  ").Append(sp.PadRight(8))
                      .Append(" 实例#").Append(mine[i].Id)
                      .Append(" 采样=").Append(mine[i].Seen).Append("帧")
                      .Append("(f").Append(mine[i].First).Append("-f").Append(mine[i].Last).Append(')')
                      .Append(Line(st));
                }
            }

            _report = sb.ToString();
            Debug.Log(_report);
            try
            {
                string dir = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "Library"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "legsmooth-report.txt"),
                                            _report, new UTF8Encoding(false));
            }
            catch (System.Exception) { /* 落盘失败不影响控制台那一份 */ }
        }

        private static string Line(in Stats st)
        {
            var sb = new StringBuilder();
            if (st.HasLeg)
            {
                sb.Append(" 腿角 max|Δ|=").Append(st.MaxD.ToString("0.00")).Append("°")
                  .Append(" max|Δ²|=").Append(st.MaxD2.ToString("0.00")).Append("°@f").Append(st.MaxD2Frame)
                  .Append("/L").Append(st.MaxD2Leg)
                  .Append("(").Append(st.MaxD2Norm.ToString("0")).Append("°/s)")
                  .Append(" 反转=").Append(st.FlipShort).Append("次(急)")
                  .Append("/").Append(st.FlipTotal).Append("次(总)")
                  .Append(" 最短同向=").Append(st.MinRun < 0 ? "—" : st.MinRun + "帧");
            }
            else sb.Append(" 腿角 未测（旧版 FBX 无腿摆角）");
            if (!st.Ok) return sb.Append("  ⚠ 有效帧 <8 ⇒ **未测**").ToString();
            sb.Append(" | 位移 maxΔ=").Append(st.MaxDelta.ToString("0.0000")).Append("m")
              .Append(" 中位=").Append(st.MedDelta.ToString("0.0000"))
              .Append(" 峰/中=").Append(st.Spike.ToString("0.00")).Append(st.Spike <= 3f ? " ✓" : " ✗")
              .Append(" | 速度 max=").Append(st.MaxV.ToString("0.00")).Append("m/s")
              .Append(" 中位=").Append(st.MedV.ToString("0.00"))
              .Append(" 峰/中=").Append(st.VSpike.ToString("0.00")).Append(st.VSpike <= 3f ? " ✓" : " ✗")
              .Append(" | 相位/位移=").Append(st.PhasePerMeter.ToString("0.0")).Append("rad/m")
              .Append("(区间 ").Append(st.PhasePerMeterMin.ToString("0.0")).Append("~")
              .Append(st.PhasePerMeterMax.ToString("0.0")).Append(')')
              .Append(" | 移动帧=").Append(st.MovFrames)
              .Append(" | 振幅 max|Δ|=").Append(st.AmpMaxStep.ToString("0.00")).Append("°")
              .Append("(走停切换=").Append(st.AmpDeltaFrames).Append("次)");
            return sb.ToString();
        }

        /// <summary>场景里那一个探针（诊断用）。</summary>
        public static bool Present => _live != null;
    }
}
