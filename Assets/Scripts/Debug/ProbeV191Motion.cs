using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v191-b：**敌人移动抽搐探针**（只读，v191 那套的替代实现）★
    ///
    /// ── 它为什么取代 `ProceduralBugs.ArmEnemyMotionProbe` ──
    /// 旧探针（v191）有三个实测出来的硬伤，每一个都让结论**读不出来或读错**：
    ///   ① **按物种记 4 个固定槽 + 只认第一次见到的那一只实例** ⇒ 那只一离场，该槽
    ///      在剩下的窗口里**永久失明** ⇒ 输出 `位置 maxΔ=0.0000m@f-1`。
    ///      而这行字与"这个物种一点都不抽"**长得一模一样** ✗
    ///      上一轮 `grunt` / `shooter` 就是被这一条骗过去的（`Library/emotion-report.txt`）✓
    ///   ② 只量 `Transform` 的位置/朝向 ⇒ **腿的抖动、Walk↔Idle 的跳变全在视野外** ✗
    ///      而用户说的是"**移动动画**抽搐"——动画在关节上，不在 Transform 上 ✓
    ///   ③ 采样挂在 `SyncEnemies` 的热路径里 ⇒ 探针自己有 bug 就会把玩法整帧抛出去 ✗
    ///
    /// ── 现在的四条尺子（每条都可复算、都可判 PASS/FAIL）──
    ///   ① 位置 `Δ`：**最大帧位移 ÷ 中位帧位移 ≤ 3**（尖峰 / 瞬移）
    ///   ② 朝向 `Δ`：同一符号的连续帧数（"连续 3 帧不翻转"= 没有长度 ≤2 的孤立段）
    ///   ③ 腿摆角 `Δ²`：`_legSwing` 的二阶差分（腿真的在抽时这里最大）
    ///   ④ 状态翻转：`Walk↔Idle` 每帧来回跳会让腿振幅在 26°↔4.5° 之间闪
    ///      ⇒ 这是"动画抽搐"最直接的指纹（`BugView.Play` 一换状态就换振幅组）✓
    ///
    /// ── 为什么挂在 `LateUpdate` 而不是塞进 `SyncEnemies` ──
    /// `World.Simulate`（玩法）在 `Update` 里跑、渲染体在它内部被摆好 ⇒
    /// `LateUpdate` 读到的是**这一帧最终**的位置/朝向/相位/腿角 ✓（与"玩家看见的那一帧"同一份）
    /// 而且探针不占热路径：它出任何问题都只影响自己这一帧的采样 ✓
    ///
    /// ⚠️ 只读：不写任何玩法字段、不碰任何 Transform ✓
    /// </summary>
    [DefaultExecutionOrder(20000)]      // 排在所有 LateUpdate 之后（机位/相机之后再读）
    [DisallowMultipleComponent]
    public sealed class ProbeV191Motion : MonoBehaviour
    {
        /// <summary>采样窗口上限（帧）。900 帧 @60fps = 15 秒，够覆盖 2 个跳跃周期。</summary>
        public const int MaxFrames = 900;

        /// <summary>盯的四个物种（与 `Resources/Data/enemies.json` 的 `id` 同字）。</summary>
        private static readonly string[] Species = { "grunt", "shooter", "jumper", "pig" };

        /// <summary>每个物种最多跟几只**实例**（够判"最差那一只"，又不至于把内存拉爆）。</summary>
        private const int MaxTracksPerSpecies = 3;

        private sealed class Track
        {
            public int Id;
            public string DefId;
            public int Seen;                    // 有值的帧数
            public int First = -1, Last = -1;
            public readonly Vector2[] Pos = new Vector2[MaxFrames];
            public readonly float[] Yaw = new float[MaxFrames];
            public readonly float[] Phase = new float[MaxFrames];
            public readonly float[] Amp = new float[MaxFrames];    // 腿摆**振幅**（走↔停的唯一差别）
            public readonly int[] State = new int[MaxFrames];
            public readonly bool[] Has = new bool[MaxFrames];
            /// <summary>该帧的 `Time.deltaTime`（dt 归一用；见 `Stats.MaxV`）。</summary>
            public readonly float[] Dt = new float[MaxFrames];
        }

        private static ProbeV191Motion _live;        // 场景里那一个（桥按名字找组件）

        private readonly List<Track> _tracks = new List<Track>(12);
        private readonly Dictionary<int, string> _speciesSeen = new Dictionary<int, string>(16);
        private readonly Dictionary<string, int> _perSpecies = new Dictionary<string, int>(4);

        private bool _armed;
        private int _frames = 180;
        private int _frame;
        private string _tag = "live";
        private string _report = "";
        private float _sumDt;
        private int _dtCount;
        private float _maxDt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV191Motion>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV191Motion");
            go.AddComponent<ProbeV191Motion>();
        }

        private void Awake() { _live = this; }
        private void OnDestroy() { if (_live == this) _live = null; }

        /// <summary>桥的入口：布防。spec = `帧数[:tag]`，默认 `180:live`。</summary>
        public string Arm(string spec)
        {
            string text = (spec ?? string.Empty).Trim();
            int colon = text.IndexOf(':');
            _frames = 180;
            _tag = "live";
            if (colon > 0)
            {
                int.TryParse(text.Substring(0, colon), out _frames);
                _tag = text.Substring(colon + 1).Trim();
            }
            else if (text.Length > 0) int.TryParse(text, out _frames);
            if (_frames <= 0) _frames = 180;
            _frames = Mathf.Clamp(_frames, 8, MaxFrames);

            _tracks.Clear();
            _speciesSeen.Clear();
            _perSpecies.Clear();
            _frame = 0;
            _report = "";
            _sumDt = 0f;
            _dtCount = 0;
            _maxDt = 0f;
            _armed = true;
            string msg = "MOTION2 已布防 帧数=" + _frames + " tag=" + _tag
                       + "（采满自动出报告：Library/emotion2-report.txt + 控制台）";
            Log.Info("DEBUG " + msg);
            return msg;
        }

        /// <summary>上次报告原文（桥可以直接取）。</summary>
        public string Report() => _report;

        /// <summary>在采集中？（避免二次布防覆盖）</summary>
        public bool Armed => _armed;

        /// <summary>
        /// 一次性**现场快照**：场上每只敌人的位置 / 与玩家的距离 / 半径和 / 异常状态 / 移速因子。
        ///
        /// 为什么要它：探针量到的是"渲染体没动"，但"为什么没动"有四种互斥可能
        /// （被冻住 / 贴住玩家了 / 移速因子 0 / 根本没被 tick）⇒ 只看位置分不出来 ✗
        /// 这一行一个原因，读一眼就能排除三种 ✓
        /// </summary>
        public string Dump(string spec)
        {
            World w = CurrentWorld();
            if (w == null) return "DUMP 没有会话";
            PlayerActor p = w.Player;
            var sb = new StringBuilder();
            sb.Append("DUMP 敌人=").Append(w.Enemies.Count)
              .Append(" 玩家=").Append(p == null ? "(无)" : (p.Pos.x.ToString("0.00") + "," + p.Pos.y.ToString("0.00")
                        + " r=" + p.Radius.ToString("0.000")))
              .Append(" 世界时间=").Append(w.Time.ToString("0.00"));
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.Def == null) continue;
                float dist = p == null ? -1f : Vector2.Distance(p.Pos, e.Pos);
                float reach = p == null ? -1f : p.Radius + e.Radius;
                float sf = e.Status.SpeedFactor(w.Tuning.formula);
                sb.Append("\n  #").Append(e.Id).Append(' ').Append(e.Def.Id.PadRight(8))
                  .Append(" pos=(").Append(e.Pos.x.ToString("0.00")).Append(',').Append(e.Pos.y.ToString("0.00")).Append(')')
                  .Append(" r=").Append(e.Radius.ToString("0.00"))
                  .Append(" dist=").Append(dist.ToString("0.00"))
                  .Append(dist > reach ? " >" : " ≤").Append("reach(").Append(reach.ToString("0.00")).Append(')')
                  .Append(" 移速因子=").Append(sf.ToString("0.00"))
                  .Append(" alive=").Append(e.Alive ? '1' : '0')
                  .Append(" frozen=").Append(e.Status.Frozen ? '1' : '0')
                  .Append(" chill=").Append(e.Status.ChillStacks)
                  .Append(" 插件减速=").Append(e.Status.PluginSlow.ToString("0.00"))
                  .Append(" kb=(").Append(e.KnockbackVel.x.ToString("0.00")).Append(',')
                  .Append(e.KnockbackVel.y.ToString("0.00")).Append(')')
                  .Append(" 视体=").Append(ProceduralBugs.ViewOf(e.Id) != null ? "虫"
                          : ProceduralBugs.PigViewOf(e.Id) != null ? "猪" : "无");
            }
            string text = sb.ToString();
            Log.Info("DEBUG " + text);
            return text;
        }

        private void LateUpdate()
        {
            if (!_armed) return;
            try { Sample(); }
            catch (System.Exception ex)
            {
                _armed = false;
                _report = "MOTION2 采样异常：" + ex;
                Log.Warn(_report);
            }
        }

        private void Sample()
        {
            World w = CurrentWorld();
            if (w == null) return;                  // 会话还没起来：不推进帧号（否则把窗口烧光）

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
                if (t == null) continue;            // 该物种已经跟满 3 只实例

                int f = _frame;
                Vector3 p; float yaw; int st; float phase; float amp;
                if (!Read(e, out p, out yaw, out st, out phase, out amp)) continue;

                t.Pos[f] = new Vector2(p.x, p.z);   // 世界 XZ = 玩法平面（y 在这里没有意义）
                t.Yaw[f] = Mathf.DeltaAngle(0f, yaw);   // 最短弧存：359→1 不许读成假跳变
                t.Phase[f] = phase;
                t.Amp[f] = amp;
                t.State[f] = st;
                t.Has[f] = true;
                t.Dt[f] = dt;
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

        /// <summary>本帧这只敌人的**渲染态**：位置 / 朝向 / 状态 / 相位 / 两条腿摆角。</summary>
        private static bool Read(EnemyActor e, out Vector3 pos, out float yaw, out int state,
                                 out float phase, out float amp)
        {
            pos = default; yaw = 0f; state = 0; phase = 0f; amp = 0f;

            // 小猪（Wander）走**旧版 FBX 路径**，其余走程序化虫怪 —— 两条路都要能量到
            ProceduralBugs.BugView bug = ProceduralBugs.ViewOf(e.Id);
            if (bug != null && bug.Go != null)
            {
                pos = bug.Go.transform.position;
                yaw = bug.Go.transform.eulerAngles.y;
                state = (int)bug.Current;
                phase = bug.Phase;
                amp = bug.SwingAmpDeg;
                return true;
            }
            ModelView pig = ProceduralBugs.PigViewOf(e.Id);
            if (pig != null && pig.Go != null)
            {
                pos = pig.Go.transform.position;
                yaw = pig.Go.transform.eulerAngles.y;
                state = (int)pig.Current;
                // 旧版模型没有"腿相位"这个概念 ⇒ 相位/腿角一律留 0（报告里读得到这个事实）
                return true;
            }
            return false;
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
        // 报告
        // ══════════════════════════════════════════════════════════════════

        private void Finish()
        {
            _armed = false;
            var sb = new StringBuilder();
            sb.Append("[Seedfall] MOTION2 ").Append(_tag)
              .Append(" | 窗口=").Append(_frame).Append("帧")
              .Append(" 平均dt=").Append((_dtCount > 0 ? _sumDt / _dtCount : 0f).ToString("0.000")).Append("s")
              .Append(" 最大dt=").Append(_maxDt.ToString("0.000")).Append("s")
              .Append(" 实例=").Append(_tracks.Count).Append("只");

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
                Track worst = null;
                Stats worstSt = default;
                float worstScore = -1f;
                for (int i = 0; i < mine.Count; i++)
                {
                    Stats st = Analyze(mine[i]);
                    float score = st.Ok ? st.VSpike : 0f;      // 按 **dt 归一**的读数选"最差"
                    if (score > worstScore) { worstScore = score; worst = mine[i]; worstSt = st; }
                    sb.Append("\n  ").Append(sp.PadRight(8))
                      .Append(" 实例#").Append(mine[i].Id)
                      .Append(" 采样=").Append(mine[i].Seen).Append('帧')
                      .Append("(f").Append(mine[i].First).Append("-f").Append(mine[i].Last).Append(')')
                      .Append(Line(st));
                }
                if (mine.Count > 1 && worst != null)
                {
                    sb.Append("\n  ").Append(sp.PadRight(8)).Append(" ⇒ 最差实例#").Append(worst.Id)
                      .Append(" 速度峰/中=").Append(worstSt.VSpike.ToString("0.00"))
                      .Append(worstSt.VSpike <= 3f ? " ✓" : " ✗")
                      .Append(" 位置峰/中=").Append(worstSt.Spike.ToString("0.00"))
                      .Append(" 急反转=").Append(worstSt.Whiplash)
                      .Append(worstSt.Whiplash == 0 ? " ✓" : " ✗");
                }
            }

            _report = sb.ToString();
            Debug.Log(_report);
            try
            {
                string dir = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "Library"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "emotion2-report.txt"),
                                            _report, new UTF8Encoding(false));
            }
            catch (System.Exception) { /* 落盘失败不影响控制台那一份 */ }
        }

        private struct Stats
        {
            public bool Ok;
            public float MaxD, MedD, Spike;
            public int MaxDFrame;
            /// <summary>★ dt 归一的读数 ★ 逐帧位移 ÷ 该帧 dt（m/s）——
            /// 这台编辑器实测 dt 在 0.02~0.33s 之间跳，**原始 Δ 会被 dt 尖峰主导** ✗
            /// 所以"最大速度 ÷ 中位速度"才是与帧率无关的那把尺子 ✓</summary>
            public float MaxV, MedV, VSpike;
            public int MaxVFrame;
            public float MaxYaw, MedYaw;
            public int Flips, Whiplash, MinRun, MaxRun;
            /// <summary>腿摆**振幅**的最大逐帧跳变（度）—— "走↔停"是不是"一帧切"的判据。</summary>
            public float MaxAmpD;
            public int MaxAmpDFrame;
            public int StateTrans;
            public int AmpSkipped;
            public int StateFlips, HitFlips;
            public float PhasePerMeter;
            public int MovFrames;
        }

        /// <summary>每个实例开头跳过的采样帧数（出生那一帧的状态/相位本来就与"稳态"不同）。</summary>
        private const int SettleFrames = 3;

        /// <summary>一根实例的全部读数（位置 / 朝向 / 腿 / 状态）。</summary>
        private static Stats Analyze(Track t)
        {
            var st = new Stats { MaxDFrame = -1, MinRun = -1, MaxVFrame = -1 };
            var dList = new List<float>(MaxFrames);
            var yList = new List<float>(MaxFrames);
            var vList = new List<float>(MaxFrames);

            Vector2 prevPos = Vector2.zero;
            float prevYaw = 0f, prevDx = 0f;
            bool hasPrev = false;
            int runSign = 0, runLen = 0;

            // ★ 跳过出生后的前 `SettleFrames` 帧 ★
            // 那几帧里有：池复用的残留姿态、第一帧 `Play()` 换状态、以及 `Moved()` 还没稳
            // ⇒ 它们不是"稳态抽搐"，混进来只会把判据读坏 ✓
            int from = (t.First < 0 ? 0 : t.First) + SettleFrames;
            for (int i = from; i <= t.Last; i++)
            {
                if (!t.Has[i]) { hasPrev = false; runSign = 0; runLen = 0; continue; }
                if (!hasPrev || !t.Has[i - 1] || i - 1 < from) { prevPos = t.Pos[i]; prevYaw = t.Yaw[i]; hasPrev = true; continue; }

                Vector2 dp = t.Pos[i] - prevPos;
                float ad = dp.magnitude;
                float dyaw = Mathf.DeltaAngle(prevYaw, t.Yaw[i]);
                float dtf = Mathf.Max(1e-4f, t.Dt[i]);
                float v = ad / dtf;

                dList.Add(ad);
                yList.Add(Mathf.Abs(dyaw));
                vList.Add(v);
                if (ad > st.MaxD) { st.MaxD = ad; st.MaxDFrame = i; }
                if (v > st.MaxV) { st.MaxV = v; st.MaxVFrame = i; }
                if (Mathf.Abs(dyaw) > st.MaxYaw) st.MaxYaw = Mathf.Abs(dyaw);
                if (ad > 0.0005f) st.MovFrames++;
                if (ad > 1e-4f) st.PhasePerMeter += Mathf.Abs(Mathf.DeltaAngle(0f, t.Phase[i] - t.Phase[i - 1])) / ad;
                else st.PhasePerMeter += Mathf.Abs(Mathf.DeltaAngle(0f, t.Phase[i] - t.Phase[i - 1])) * 0f;

                // ── 朝向：符号翻转 + 同向段长度 ──
                // 0.5° 以下不算（量化台阶会把每帧都读成"翻转"）
                if (Mathf.Abs(dyaw) > 0.5f)
                {
                    int sign = dyaw > 0f ? 1 : -1;
                    if (runSign == 0 || sign == runSign) { runLen++; }
                    else
                    {
                        if (st.MinRun < 0 || runLen < st.MinRun) st.MinRun = runLen;
                        if (st.MaxRun < runLen) st.MaxRun = runLen;
                        // ★ 急反转：上一段只有 1~2 帧就掉头 ⇒ "连续 3 帧不翻转"被破坏 ★
                        //   （单帧反向 / 两帧反向 = 画面上那一下"抽"）
                        if (runLen <= 2) st.Whiplash++;
                        st.Flips++;
                        runLen = 1;
                    }
                    runSign = sign;
                }

                // ── ★ 腿摆**振幅**的逐帧跳变（"走↔停"那一刀的指纹）★ ──
                // 为什么量振幅而不是腿角：腿角 = 振幅 × sin(相位)，相位在 6~9Hz 上跑
                // ⇒ 腿角每帧本来就会变 20°~65°（还取决于 dt）**判不出抽没抽** ✗
                // 而"走↔停"的唯一差别就是振幅（26° ↔ 4.2°）⇒ 它的逐帧 Δ 有确定含义 ✓
                // ★ 只统计 `dt ≤ 0.05s` 的帧 ★：这份数据里出现过 dt=0.333s 的尖峰
                //   （同一台编辑器上还有别的自动化在跑），而过渡是 `1−e^(−dt/τ)`
                //   ⇒ 一个大 dt 会让过渡**一帧走完**（那正是 dt 的错，不是插值的错）✓
                float damp = Mathf.Abs(t.Amp[i] - t.Amp[i - 1]);
                if (t.Dt[i] <= 0.05f)
                {
                    if (damp > st.MaxAmpD) { st.MaxAmpD = damp; st.MaxAmpDFrame = i; }
                    if (t.State[i] != t.State[i - 1]) st.StateTrans++;
                }
                else st.AmpSkipped++;

                // ── 状态翻转：Walk↔Idle 每帧跳 = 腿振幅 26↔4.5 闪 ──
                if (t.State[i] != t.State[i - 1])
                {
                    st.StateFlips++;
                    // 只数"走/停"之间的跳（受击/死亡是设计，不在这里记账）
                    bool a = t.State[i - 1] == (int)ModelView.State.Walk || t.State[i - 1] == (int)ModelView.State.Idle;
                    bool b = t.State[i] == (int)ModelView.State.Walk || t.State[i] == (int)ModelView.State.Idle;
                    if (a && b) st.HitFlips++;
                }
                prevDx = dp.x;
                prevPos = t.Pos[i];
                prevYaw = t.Yaw[i];
            }
            if (runLen > 0 && (st.MinRun < 0 || runLen < st.MinRun)) st.MinRun = runLen;

            if (dList.Count < 8) return st;
            st.Ok = true;
            dList.Sort(); yList.Sort(); vList.Sort();
            st.MedD = dList[dList.Count / 2];
            st.MedYaw = yList[yList.Count / 2];
            st.MedV = vList[vList.Count / 2];
            st.Spike = st.MedD > 1e-5f ? st.MaxD / st.MedD : 0f;
            st.VSpike = st.MedV > 1e-5f ? st.MaxV / st.MedV : 0f;
            return st;
        }

        private static string Line(in Stats st)
        {
            if (!st.Ok) return "  ⚠ 有效帧 <8 ⇒ **未测**（实例在场太短）";
            var sb = new StringBuilder();
            sb.Append(" 位置 maxΔ=").Append(st.MaxD.ToString("0.0000")).Append("m@f").Append(st.MaxDFrame)
              .Append(" 中位=").Append(st.MedD.ToString("0.0000"))
              .Append(" 峰/中=").Append(st.Spike.ToString("0.00")).Append(st.Spike <= 3f ? " ✓" : " ✗")
              .Append(" 移动帧=").Append(st.MovFrames)
              .Append(" | 速度 max=").Append(st.MaxV.ToString("0.00")).Append("m/s@f").Append(st.MaxVFrame)
              .Append(" 中位=").Append(st.MedV.ToString("0.00"))
              .Append(" 峰/中=").Append(st.VSpike.ToString("0.00")).Append(st.VSpike <= 3f ? " ✓" : " ✗")
              .Append(" | 朝向 maxΔ=").Append(st.MaxYaw.ToString("0.0")).Append("° 中位=").Append(st.MedYaw.ToString("0.0"))
              .Append("° 翻转=").Append(st.Flips)
              .Append(" 急反转=").Append(st.Whiplash).Append(st.Whiplash == 0 ? " ✓" : " ✗")
              .Append(" 最短同向段=").Append(st.MinRun < 0 ? "—" : st.MinRun + "帧")
              .Append(" | 振幅 max|Δ|=").Append(st.MaxAmpD.ToString("0.00")).Append("°@f").Append(st.MaxAmpDFrame)
              .Append(st.MaxAmpD <= 5f ? " ✓" : " ✗")
              .Append("(走停切换=").Append(st.StateTrans).Append("次)")
              .Append(st.AmpSkipped > 0 ? ("（dt大排除" + st.AmpSkipped + "帧）") : "")
              .Append(" | 状态翻转=").Append(st.StateFlips)
              .Append(" | 相位/位移=").Append(st.PhasePerMeter.ToString("0.0")).Append("rad/m");
            return sb.ToString();
        }

        /// <summary>场景里那一个探针（桥找不到组件时用来诊断）。</summary>
        public static bool Present => _live != null;
    }
}
