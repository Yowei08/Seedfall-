using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v193 取证探针（**只读** · 默认静默）★★
    ///
    /// 回答一个问题，只回答这一个：
    ///   **整点回血在第 1 波（= 0 分钟 / 开局）到底发了几次事件、播了几次全屏特效？第 2 波呢？**
    ///
    /// 判据（用户给的）：
    ///   · 第 1 波（0 分钟/初始） ⇒ 事件 **0** 次、全屏特效 **0** 次
    ///   · 第 2 波（t=60s 之后）  ⇒ 事件 **1** 次、全屏特效 **1** 次
    ///
    /// ★ 为什么"第 1 波"要按 `e.Wave` 数，而不是按"开局 1 秒内"数 ★
    ///   `RunSession` 开局停在 `PhaseState.Prepare`（`tuning.shop.infinitePrepare = true`
    ///   ⇒ 它一直在等玩家选初始种子），真正的 `StartWave(1)` 落在**点击之后**（实测 t≈2.5s）
    ///   ⇒ "开局 1 秒内"这个时间窗**根本覆盖不到第 1 波**，数出来必然 0，是个假绿 ✗
    ///   所以按事件自带的 `Wave` 字段数，这才是判据本身 ✓
    ///
    /// ★ 为什么"特效"不能靠推断，必须真数 ★
    ///   `WorldView.OnMinuteHeal` 只是订阅者 ⇒ "事件 0 次 ⇒ 特效 0 次"是**推理**，不是读数。
    ///   这里直接盯 `WorldView._healT`（`&lt; 0` = 没在播，`&gt;= 0` = 在播）数**上升沿**，
    ///   于是"事件次数"和"特效次数"是两个**各自独立**的量测 ✓ 两者不一致会当场暴露 ✗
    ///   （反射读私有字段：本探针**只读**，不改 `WorldView` 一行代码 —— 那是交付约束）
    ///
    /// ★ 怎么开 ★
    ///   在 `&lt;工程&gt;/Library/yowei_v193_heal0.on` 放一个任意内容的小文件 ⇒ 进 Play 后自动开始记录。
    ///   删掉它 ⇒ 彻底静默（不改代码、不重编译）✓
    ///   报告同时 `Log.Info` 一份并写到 `&lt;工程&gt;/Library/yowei-v193-heal0.txt` ✓
    ///
    /// ★ 它改了什么 ★
    ///   三件，都是局内状态：
    ///     ① 替玩家"选了初始种子"那一下：调 `StartWave(1)`（**与 `TickPrepare` 末尾那一句相同**，
    ///        只是无人值守时 `infinitePrepare` 会让它永远等下去）
    ///     ② 保命：血量掉到 50% 以下时按 1 秒一次补满 —— 否则撑不到第 2 波，一行读数都没有
    ///        （走 `HealthPool.Heal`，与整点回血**不是同一条路**，不影响 `MinuteHealEvent` 计数）
    ///     ③ 到 95 秒自己收工写报告
    ///   不写任何工程文件、不碰表 ✓
    /// </summary>
    public sealed class ProbeV193Heal0 : MonoBehaviour
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v193_heal0.on");
        private static string OutPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-v193-heal0.txt");

        /// <summary>跑多久（秒）。第 2 波在 t≈60 之后，还要留一段证明"没有第二次"。</summary>
        private const float RunSeconds = 95f;

        /// <summary>
        /// 进 Play 时自举（`AfterSceneLoad`：那一刻 `GameBootstrap.Live` 才有东西）。
        /// 开着的判据只有"那个小文件在不在" ⇒ 删掉它就彻底静默，不必改代码、不必重编译 ✓
        /// ⚠️ 本探针用 `DontDestroyOnLoad`（要活过场景切换），代价是桥的 `/probe` **找不到它**
        ///    （`/probe` 只搜当前打开的场景，而 DDOL 在独立的 `DontDestroyOnLoad` 场景里）。
        ///    ⇒ 判据一律读**报告文件** `Library/yowei-v193-heal0.txt`，不要拿 `/probe` 当证据 ✗
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!File.Exists(FlagPath)) return;
            var go = new GameObject("ProbeV193Heal0");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV193Heal0>();
            Log.Info("PROBEV193HEAL0 armed（Library/yowei_v193_heal0.on 存在）");
        }

        // ── 读数（全部是本探针自己的计数，不读别人的统计）──
        private int _eventTotal;
        private int _eventWave1;                  // Wave <= 1 的事件次数（期望 0）
        private readonly StringBuilder _events = new StringBuilder();

        private int _fxTotal;                     // 全屏特效上升沿总次数
        private int _fxWave1;                     // 在"第 1 波"期间发生的特效次数（期望 0）
        private bool _fxWasPlaying;

        private float _elapsed;
        private bool _started;
        private float _nextHealAt;
        private bool _finished;

        private FieldInfo _healTField;
        private bool _reflectionReady;
        private string _reflectionNote = "(尚未取到 WorldView)";

        private void Awake()
        {
            _healTField = typeof(WorldView).GetField("_healT", BindingFlags.NonPublic | BindingFlags.Instance);
            _reflectionReady = _healTField != null;
            _reflectionNote = _reflectionReady
                ? "WorldView._healT 反射 OK"
                : "WorldView._healT **取不到** ⇒ 特效次数无法量测（报告会如实标注，别把它当成 0）";
            Bus.Subscribe<MinuteHealEvent>(OnMinuteHeal);
        }

        private void OnDestroy() => Bus.Unsubscribe<MinuteHealEvent>(OnMinuteHeal);

        private void OnMinuteHeal(MinuteHealEvent e)
        {
            _eventTotal++;
            if (e.Wave <= 1) _eventWave1++;
            _events.AppendLine($"    · 事件#{_eventTotal}  探针t={_elapsed:0.00}s  Wave={e.Wave}  " +
                               $"HealHp={e.HealHp:0.0}  HealShield={e.HealShield:0.0}  Hp={e.Hp:0.0}/{e.MaxHp:0.0}");
        }

        private RunSession Session()
        {
            if (GameBootstrap.Live == null || GameBootstrap.Live.Count == 0) return null;
            return GameBootstrap.Live[0] != null ? GameBootstrap.Live[0].Session : null;
        }

        private WorldView View()
        {
            if (GameBootstrap.Live == null || GameBootstrap.Live.Count == 0) return null;
            return GameBootstrap.Live[0] != null ? GameBootstrap.Live[0].View : null;
        }

        /// <summary>全屏特效"正在播吗"—— 直接读 `WorldView._healT`（`&lt; 0` 表示没在播）。</summary>
        private bool FxPlaying()
        {
            if (!_reflectionReady) return false;
            WorldView view = View();
            if (view == null) return false;
            try
            {
                return _healTField.GetValue(view) is float t && t >= 0f;
            }
            catch
            {
                _reflectionReady = false;
                _reflectionNote = "WorldView._healT 读取抛异常 ⇒ 特效次数**不可信**（不是真的没播）";
                return false;
            }
        }

        private void Update()
        {
            if (_finished) return;
            _elapsed += Time.unscaledDeltaTime;

            RunSession s = Session();

            // ⓪ 推过 Prepare（= 替玩家"选初始种子"；`TickPrepare` 末尾就是这句 StartWave(1)）
            if (!_started && s != null && s.WaveIndex <= 0)
            {
                _started = true;
                Log.Info($"PROBEV193 推过 Prepare：StartWave(1) @ 探针t={_elapsed:0.000}s" +
                         $"（Clock.GameTime={s.Clock.GameTime:0.000}s）");
                s.StartWave(1);
            }

            // ① 数全屏特效的**上升沿**（"没播 → 在播"那一下算一次），并记下当时是第几波
            bool now = FxPlaying();
            if (now && !_fxWasPlaying)
            {
                _fxTotal++;
                if (s == null || s.WaveIndex <= 1) _fxWave1++;
            }
            _fxWasPlaying = now;

            // ② 保命：否则第 2 波之前就死了，一行读数都拿不到
            if (s != null && s.World != null && s.World.Player != null && _elapsed >= _nextHealAt)
            {
                var hp = s.World.Player.Health;
                if (hp != null && hp.Hp < hp.MaxHp * 0.5f)
                {
                    hp.Heal(hp.MaxHp);
                    _nextHealAt = _elapsed + 1f;
                }
            }

            // ③ 到点收工
            if (_elapsed >= RunSeconds) Finish();
        }

        private void Finish()
        {
            _finished = true;
            string report = BuildReport();
            Log.Info(report);
            try { File.WriteAllText(OutPath, report); }
            catch (Exception e) { Log.Warn("PROBEV193 写盘失败：" + e.Message); }
        }

        private string BuildReport()
        {
            RunSession s = Session();
            var sb = new StringBuilder();
            sb.AppendLine("═══ PROBEV193HEAL0（整点回血 第 1 波 判据）═══");
            sb.AppendLine($"  探针观测 t={_elapsed:0.00}s｜最终 Wave={(s != null ? s.WaveIndex : -1)}｜" +
                          $"Clock.GameTime={(s != null ? s.Clock.GameTime : -1f):0.00}s");
            sb.AppendLine($"  反射：{_reflectionNote}");
            sb.AppendLine();
            sb.AppendLine($"  ① 第 1 波（0 分钟/初始）：MinuteHealEvent **{_eventWave1}** 次，" +
                          $"全屏特效 **{_fxWave1}** 次   期望 0 / 0");
            sb.AppendLine($"  ② 全程：MinuteHealEvent {_eventTotal} 次，全屏特效 {_fxTotal} 次" +
                          $"   （第 2 波起期望各 ≥1）");
            sb.AppendLine(_events.Length > 0 ? "  事件明细：\n" + _events : "  事件明细：（一次都没有）");
            bool pass = _eventWave1 == 0 && _fxWave1 == 0 && _eventTotal >= 1 && _fxTotal >= 1;
            sb.AppendLine($"  判定：{(_eventWave1 == 0 ? "PASS" : "FAIL")} 第1波事件 0 次｜" +
                          $"{(_fxWave1 == 0 ? "PASS" : "FAIL")} 第1波特效 0 次｜" +
                          $"{(_eventTotal >= 1 ? "PASS" : "FAIL")} 第2波起 ≥1 次事件｜" +
                          $"{(_fxTotal >= 1 ? "PASS" : "FAIL")} ≥1 次特效");
            sb.AppendLine($"  ⇒ 总判定：{(pass ? "PASS" : "FAIL")}");
            return sb.ToString();
        }
    }
}
