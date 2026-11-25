using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v191 补丁：**弹字审计探针**（只读 · 默认静默）★
    ///
    /// 用户原话："重新补上角色受击弹字，检查所有弹字是否有问题"
    /// ⇒ 这个文件只回答两件事，且**两件都给数字**：
    ///   ① 每一类弹字「真的弹了没有 / 弹的是什么字 / 那个数对不对」
    ///   ② 弹字里的数 == 实际到账 / 实际扣血（逐位比，不是"看起来像"）
    ///
    /// ── 它为什么是一个**新文件** ──
    /// 与 `ProbeV189` / `ProbeV190` / `ProbeV191Shots` 同一条纪律：
    /// 探针只往新文件里加，不去改 `DebugApi.cs` 那 6700 行（并行改动线还在追加它）✓
    /// `DebugApi.cs` 一个字都不用动 —— 桥可以直接按组件名调本类 ✓
    ///
    /// ── 怎么开 ──
    /// 进 Play 之前放一个空文件：
    ///   ① `&lt;工程&gt;/Library/yowei_popups_probe.on` ⇒ 跑一次**全表**（`Start()` 里，与控制台读回）
    ///   ② `&lt;工程&gt;/Library/yowei_popups_shot.on`  ⇒ 跑**抓图序列**（4 张：0.7 无盾 / 0.7 有盾 / 两行 / 致死）
    /// 删掉它们 ⇒ 彻底静默（不改代码、不重编译）✓
    ///
    /// ── 读数从哪来 ──
    /// 三类来源，**都取真实的那个**：
    ///   · 上屏原文 —— `RunSession.Popups[i].Text`（这是 `ImGuiHud.DrawPopups` 真的画出去的那条字符串，
    ///     已经过 `PopupStyle.TextOf` ⇒ 暴击的"！"也在里面）✓
    ///   · 事件原文 —— 订阅 `FloatingTextEvent`（发布端发的裸串，用来区分"没发"与"发了但被漏斗拦下"）✓
    ///   · 期望值 —— **独立再算一遍**（血量差 / 到账差 / `DamageResult` 分量），不是拿弹字反推 ✓
    ///     没有这一层的话，"弹字与结算一起错"照样全绿（本工程吃过这个亏：卡面与引擎一起漂）
    ///
    /// ── 四查（用户给的判据）──
    ///   ① 数字对不对（与"实际到账 / 实际扣血"逐位一致）
    ///   ② 正负号对不对（伤害 `−` / 获得 `+`）
    ///   ③ 格式对不对（整数不带小数点；小数 `0.##`；**不出现 `-0`**）
    ///   ④ 位置 / 颜色 / 图标对不对
    ///
    /// ⚠️ 本文件**不改任何东西**：每段都用临时 `RunSession`，跑完 `Dispose()`；
    ///    唯一动到"外面"的是抓图序列，它只掰**当前这一局内存里**的玩家血量与弹药 ✓
    /// </summary>
    public sealed class ProbePopups : MonoBehaviour
    {
        // ═══════════════════════════════════════════ 开关 / 路径
        private static string Lib(string name)
            => Path.GetFullPath(Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", name));

        private static string FlagAudit => Lib("yowei_popups_probe.on");
        private static string FlagShot => Lib("yowei_popups_shot.on");

        /// <summary>
        /// 图名前缀（改前 / 改后两轮要**同名不同前缀**，否则后一张把前一张盖掉，前后对照就没了）。
        /// 放 `<工程>/Library/yowei_popups_tag.txt`，内容就是前缀（例：`BEFORE`）。
        /// </summary>
        private static string TagFile => Lib("yowei_popups_tag.txt");

        private static string Tag
        {
            get
            {
                try
                {
                    if (File.Exists(TagFile))
                    {
                        string t = File.ReadAllText(TagFile).Trim();
                        if (t.Length > 0) return t.Replace('/', '_').Replace('\\', '_') + "-";
                    }
                }
                catch { /* 读不到就当作没有前缀 */ }
                return "";
            }
        }

        private static string ShotDir => Lib("yowei-shots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            bool audit = File.Exists(FlagAudit);
            bool shot = File.Exists(FlagShot);
            if (!audit && !shot) return;
            var go = new GameObject("ProbePopups");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<ProbePopups>();
            c._audit = audit;
            c._shot = shot;
            Log.Info($"PROBEPOPUPS armed（audit={audit} shot={shot}）");
        }

        private bool _audit, _shot;

        private void Start()
        {
            if (_audit)
            {
                Log.Info("PROBEPOPUPS AUDIT BEGIN");
                try
                {
                    // ★ 分块打 ★ —— 一条 Log 塞两万字符在控制台里会看不清，
                    //   而桥的 /log 是逐条取的 ⇒ 分块对人对机器都更好读 ✓
                    foreach (string chunk in AuditChunks()) Log.Info(chunk);
                }
                catch (Exception e)
                {
                    // 探针自己崩了也要看得见 —— 否则"没有输出"会被读成"没问题"
                    Log.Error("PROBEPOPUPS CRASH " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                }
                Log.Info("PROBEPOPUPS AUDIT END");
            }
            if (_shot) StartCoroutine(ShotSequence());
        }

        // ══════════════════════════════════════════════════════════
        //  行模型：一条"本应出现的弹字" + 它的四查结果
        // ══════════════════════════════════════════════════════════
        private sealed class Row
        {
            public string Cat;          // 类别
            public string Trigger;      // 触发条件（这一行是怎么造出来的）
            public string Source;       // 取数源（期望值从哪儿算的）
            public PopupKind Kind;      // 期望的 kind（决定颜色/图标）
            public string Text;         // 实测上屏原文；null = 没出
            public Color DrawColor;     // 实测颜色（RunSession.Popups[i].Color）
            public Vector2 DrawPos;
            public float Expect;        // 期望数值（取绝对值）
            public bool Negative = true;// 期望负号？
            public bool HasNumber = true;
            public Vector2? Anchor;     // 期望位置（角色 / 地块坐标）
            public float PosTol = 0.62f;// 上屏坐标允许离锚点多远
            public string Extra = "";
            public bool Fail;
        }

        private static readonly List<Row> _rows = new List<Row>();
        private static readonly List<string> _notes = new List<string>();

        private static Row Add(string cat, string trigger, string source, PopupKind kind,
                        string text, Color color, float expect, bool negative,
                        Vector2? anchor = null, Vector2 drawPos = default, string extra = "",
                        float posTol = 0.62f)
        {
            var r = new Row
            {
                Cat = cat, Trigger = trigger, Source = source, Kind = kind,
                Text = text, DrawColor = color, Expect = expect, Negative = negative,
                Anchor = anchor, DrawPos = drawPos, Extra = extra, PosTol = posTol
            };
            _rows.Add(r);
            return r;
        }

        // ══════════════════════════════════════════════════════════
        //  期望色 / 期望图标 —— 这是**规格的第二份**，故意抄一遍
        //
        //  为什么不直接问 `PopupStyle.ColorOf`：
        //  那样只能证明"代码等于代码"（恒真）✗
        //  把规格里的 RGB 抄在这里，才是"代码 vs 规格"的对比 ✓
        //  （与 `DebugApi.TraceOne` 里"期望值独立再算一遍"同一条纪律）
        // ══════════════════════════════════════════════════════════
        private static Color SpecColor(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageToPlayer: return new Color(1.00f, 0.24f, 0.22f);  // 红
                case PopupKind.DamageToPlayerShield: return new Color(0.36f, 0.68f, 1.00f); // 蓝
                case PopupKind.DamageBurn: return new Color(1.00f, 0.62f, 0.16f);      // 火苗橙
                case PopupKind.DamageSlow: return new Color(0.42f, 0.78f, 1.00f);      // 水滴蓝
                case PopupKind.DamagePoison: return new Color(0.46f, 1.00f, 0.42f);    // 骷髅绿
                case PopupKind.Heal: return new Color(0.42f, 1.00f, 0.48f);            // 绿
                case PopupKind.ExpGain: return new Color(0.42f, 1.00f, 0.48f);         // 绿
                case PopupKind.GoldGain: return new Color(1.00f, 0.88f, 0.22f);        // 黄
                case PopupKind.PlantMature: return new Color(1.00f, 0.88f, 0.22f);     // 黄
                case PopupKind.Crit: return new Color(1.00f, 0.88f, 0.22f);            // 黄
                case PopupKind.LevelUp: return new Color(1.00f, 0.92f, 0.45f);         // 金
                case PopupKind.Bomb: return new Color(1.00f, 0.24f, 0.22f);            // 红
                case PopupKind.PlantThirst: return new Color(1.00f, 0.24f, 0.22f);     // 红
                default: return new Color(1.00f, 0.98f, 0.96f);                        // 敌人受击白 / Info
            }
        }

        private static PopupIcon SpecIcon(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageBurn: return PopupIcon.Flame;
                case PopupKind.DamageSlow: return PopupIcon.Drop;
                case PopupKind.DamagePoison: return PopupIcon.Skull;
                default: return PopupIcon.None;
            }
        }

        /// <summary>
        /// 玩家红/蓝两行**应当**的固定错位（规格的第二份，故意抄一遍）。
        /// 判据是"上屏坐标落在 `锚点 + 这个错位` 周围 `ui.popupRadius`(0.6m) 内" ——
        /// 拿 `PopupStyle.StackOffsetOf` 自己当期望值只能证明"代码等于代码"✗
        /// </summary>
        private static Vector2 SpecStack(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageToPlayer: return new Vector2(0f, 0.38f);        // 红在上
                case PopupKind.DamageToPlayerShield: return new Vector2(0f, -0.38f); // 蓝在下
                default: return Vector2.zero;
            }
        }

        private static string KindName(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageToEnemy: return "敌人受击";
                case PopupKind.DamageToPlayer: return "玩家扣血";
                case PopupKind.DamageToPlayerShield: return "玩家扣盾";
                case PopupKind.Heal: return "回血";
                case PopupKind.PlantMature: return "成熟";
                case PopupKind.PlantThirst: return "缺水";
                case PopupKind.Harvest: return "收获";
                case PopupKind.GoldGain: return "金币";
                case PopupKind.ExpGain: return "经验";
                case PopupKind.Crit: return "暴击";
                case PopupKind.LevelUp: return "升级";
                case PopupKind.Bomb: return "爆炸拟声";
                case PopupKind.DamageBurn: return "灼烧";
                case PopupKind.DamageSlow: return "减速";
                case PopupKind.DamagePoison: return "毒区";
                default: return "信息";
            }
        }

        private static string ColorName(Color c)
        {
            if (Near(c, new Color(1.00f, 0.24f, 0.22f))) return "红";
            if (Near(c, new Color(0.36f, 0.68f, 1.00f))) return "蓝";
            if (Near(c, new Color(1.00f, 0.62f, 0.16f))) return "橙";
            if (Near(c, new Color(0.42f, 0.78f, 1.00f))) return "浅蓝";
            if (Near(c, new Color(0.46f, 1.00f, 0.42f))) return "毒绿";
            if (Near(c, new Color(0.42f, 1.00f, 0.48f))) return "绿";
            if (Near(c, new Color(1.00f, 0.88f, 0.22f))) return "黄";
            if (Near(c, new Color(1.00f, 0.92f, 0.45f))) return "金";
            if (Near(c, new Color(1.00f, 0.98f, 0.96f))) return "白";
            return $"rgb({c.r:0.00},{c.g:0.00},{c.b:0.00})";
        }

        private static bool Near(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        private static string IconName(PopupIcon i)
        {
            switch (i)
            {
                case PopupIcon.Flame: return "火苗";
                case PopupIcon.Drop: return "水滴";
                case PopupIcon.Skull: return "骷髅";
                default: return "无图标";
            }
        }

        // ══════════════════════════════════════════════════════════
        //  数字抽取 + 格式判定
        // ══════════════════════════════════════════════════════════

        /// <summary>
        /// 从弹字原文里把数字挖出来。
        ///
        /// 规则：找**第一个**「正负号 + 紧跟数字」的位置。
        ///   `"-12.5"`            ⇒ 符号 '-'，数字 "12.5"
        ///   `"-8！"`             ⇒ 符号 '-'，数字 "8"（暴击那行的全角叹号）
        ///   `"金币+12 X2"`       ⇒ 符号 '+'，数字 "12"（`X2` 前面没有符号 ⇒ 不会被误取）
        ///   `"经验值+9"`         ⇒ 符号 '+'，数字 "9"
        ///   `"Lv 5"` / `"成熟"`  ⇒ 没有符号 ⇒ 没有数字
        /// </summary>
        private static bool ExtractNumber(string text, out char sign, out string digits, out float value)
        {
            sign = '\0'; digits = null; value = float.NaN;
            if (string.IsNullOrEmpty(text)) return false;
            int start = -1;
            for (int i = 0; i < text.Length - 1; i++)
            {
                if ((text[i] == '-' || text[i] == '+') && char.IsDigit(text[i + 1]))
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return false;
            sign = text[start];
            int end = start + 1;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
            digits = text.Substring(start + 1, end - start - 1);
            return float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>格式三判：整数不带小数点 / 小数 1~2 位且不以 0 收尾 / **不许出现 `-0`**。</summary>
        private static bool FormatOk(char sign, string digits, float value, out string why)
        {
            why = "整✓";
            if (digits == null) { why = "无数字"; return false; }
            int dot = digits.IndexOf('.');
            if (dot < 0)
            {
                // 整数分支：不许有小数点（走到了这里就是没有）✓
                // ★ 但 `-0` 必须挡：符号是负、值是 0 ⇒ "扣了 0 点"是个谎 ✗
                if (sign == '-' && value <= 0f) { why = "出现 -0"; return false; }
                return true;
            }
            int frac = digits.Length - dot - 1;
            why = $"小数{frac}位{(digits[digits.Length - 1] == '0' ? "/尾0" : "")}";
            if (frac < 1 || frac > 2) return false;
            if (digits[digits.Length - 1] == '0') return false;   // `0.##` 不会留尾零
            if (sign == '-' && value <= 0f) { why = "出现 -0"; return false; }
            return true;
        }

        /// <summary>一条行的四查，输出成 `①4=4✓ ②−✓ ③整✓ ④白/无/位✓`。</summary>
        private static string Verdict(Row r)
        {
            var sb = new StringBuilder();
            bool ok = true;

            // ── ① 数字 ──
            float tol = 1e-3f;
            bool numOk;
            string numTxt;
            if (!r.HasNumber)
            {
                numOk = true;
                numTxt = "无数值";
            }
            else if (r.Text == null)
            {
                numOk = false;
                numTxt = $"期望 {r.Expect:0.##}／**没弹**";
            }
            else if (!ExtractNumber(r.Text, out char sgn, out string digits, out float got))
            {
                numOk = false;
                numTxt = "取不到数";
            }
            else
            {
                if (digits.IndexOf('.') >= 0) tol = 5.1e-3f;      // 显示只保留 2 位 ⇒ 允许半个末位的误差
                numOk = Mathf.Abs(Mathf.Abs(got) - Mathf.Abs(r.Expect)) <= tol;
                numTxt = $"{Mathf.Abs(got):0.####}={(numOk ? "" : "≠")}{Mathf.Abs(r.Expect):0.####}";
            }
            ok &= numOk;

            // ── ② 符号 ──
            bool signOk;
            string signTxt;
            if (!r.HasNumber)
            {
                signOk = true; signTxt = "—";
            }
            else if (r.Text == null)
            {
                signOk = false; signTxt = "—";
            }
            else if (!ExtractNumber(r.Text, out char sgn2, out _, out _))
            {
                signOk = false; signTxt = "无符号";
            }
            else
            {
                char want = r.Negative ? '-' : '+';
                signOk = sgn2 == want;
                signTxt = signOk ? want.ToString() : $"{sgn2}(应{want})";
            }
            ok &= signOk;

            // ── ③ 格式 ──
            bool fmtOk;
            string fmtTxt;
            if (!r.HasNumber)
            {
                fmtOk = true; fmtTxt = "—";
            }
            else if (r.Text == null)
            {
                fmtOk = false; fmtTxt = "—";
            }
            else if (!ExtractNumber(r.Text, out char sgn3, out string digits3, out float val3))
            {
                fmtOk = false; fmtTxt = "无数字";
            }
            else
            {
                fmtOk = FormatOk(sgn3, digits3, val3, out fmtTxt);
            }
            ok &= fmtOk;

            // ── ④ 位置 / 颜色 / 图标 ──
            //
            // ★ 位置的判据是"上屏坐标落在锚点周围 `ui.popupRadius`（= 0.6m）的散布圆内" ★
            //   为什么不要求"逐位相等"：`RunSession.OnFloatingText` **故意**给小圆随机落点
            //   （v48：同一目标连续弹字时不会完全重叠）✓ 那是设计，不是漂移 ✓
            //   而"事件坐标 == 角色坐标"是这条判据的**数学前提**：散布点必在事件点周围 r 内
            //   ⇒ 落在 r 内就说明事件点就在锚点上（否则散布圆会整体平移出去）✓
            //   收割那三条另有 ±0.45/+0.35 的**设计错位**（三条不重叠）⇒ 那两行给了更大的容差，
            //   并在明细里把设计错位写出来 ✓
            bool colOk = r.Text != null && Near(r.DrawColor, SpecColor(r.Kind));
            PopupIcon gotIcon = PopupStyle.IconOf(r.Kind);
            bool iconOk = gotIcon == SpecIcon(r.Kind);
            bool posOk = true;
            string posTxt = "位—";
            if (r.Text != null && r.Anchor.HasValue)
            {
                float d = Vector2.Distance(r.DrawPos, r.Anchor.Value);
                posOk = d <= r.PosTol;
                posTxt = $"位{d:0.00}≤{r.PosTol:0.00}";
            }
            bool lookOk = colOk && iconOk && posOk;
            string lookTxt = r.Text == null
                ? "**没弹**"
                : $"{ColorName(r.DrawColor)}{(colOk ? "" : "✗")}/{IconName(gotIcon)}{(iconOk ? "" : "✗")}/{posTxt}";
            ok &= lookOk;

            sb.Append("①").Append(numTxt).Append(numOk ? "✓" : "✗").Append(' ')
              .Append("②").Append(signTxt).Append(signOk ? "✓" : "✗").Append(' ')
              .Append("③").Append(fmtTxt).Append(fmtOk ? "" : "✗").Append(' ')
              .Append("④").Append(lookTxt).Append(lookOk ? "" : "✗");
            r.Fail = !ok;
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════
        //  抓取器：一次动作期间收到的所有 `FloatingTextEvent`
        // ══════════════════════════════════════════════════════════
        private sealed class Captured
        {
            public readonly List<string> Text = new List<string>();
            public readonly List<PopupKind> Kind = new List<PopupKind>();
            public readonly List<Vector2> Pos = new List<Vector2>();
            public int Count => Text.Count;
        }

        private static Captured Watch(Action action)
        {
            var cap = new Captured();
            Action<FloatingTextEvent> on = e =>
            {
                cap.Text.Add(e.Text);
                cap.Kind.Add(e.Kind);
                cap.Pos.Add(e.Pos);
            };
            Bus.Subscribe(on);
            try { action(); }
            finally { Bus.Unsubscribe(on); }
            return cap;
        }

        /// <summary>
        /// 上屏那条（`RunSession.Popups`）里找第一条属于 <paramref name="kind"/> 的字。
        /// 为什么要问上屏列表而不是事件：`PopupStyle.TextOf` 会在暴击/缺水后面补 `！`，
        /// 而**玩家看到的就是补完之后的那串** ⇒ 格式与"！"都得按上屏那条判 ✓
        /// </summary>
        private static RunSession.FloatingText Drawn(RunSession s, PopupKind kind)
        {
            for (int i = 0; i < s.Popups.Count; i++)
                if (s.Popups[i].Kind == kind) return s.Popups[i];
            return null;
        }

        private static string FirstLine<T>(List<T> list, Func<T, string> fmt)
        {
            if (list.Count == 0) return "（无）";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append('｜');
                sb.Append(fmt(list[i]));
            }
            return sb.ToString();
        }

        private static string CapsText(Captured c) => FirstLine(c.Text, x => x);

        // ══════════════════════════════════════════════════════════
        //  临时会话工厂（与 `DebugApi.TraceOne` / `ProbePlayerPopup` 同一套）
        // ══════════════════════════════════════════════════════════
        private static RunSession Fresh(ulong seed = 20240607UL)
        {
            var s = new RunSession();
            s.Start(seed);
            s.StartWave(1);
            s.World.Enemies.Clear();
            return s;
        }

        private static EnemyActor SpawnGrunt(RunSession s, Vector2 at, float hp = 1e5f)
        {
            s.World.SpawnEnemy("grunt", at, 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = at;
            e.Health.Setup(hp, 0f);
            return e;
        }

        // ══════════════════════════════════════════════════════════
        //  全表
        // ══════════════════════════════════════════════════════════
        public static List<string> AuditChunks()
        {
            TableDatabase.Load(true);
            _rows.Clear();
            SecEnemyHit();
            SecDots();
            SecPlayerHurt();
            SecGate();
            SecHarvest();
            SecHeal();
            SecMisc();
            SecNoPopup();
            return Render();
        }

        /// <summary>桥入口：`send ProbePopups ProbePopups AuditAll` ⇒ 返回全表（同时已进控制台）。</summary>
        public string AuditAll(string spec)
        {
            try
            {
                var chunks = AuditChunks();
                foreach (string c in chunks) Log.Info(c);
                return chunks.Count > 0 ? chunks[chunks.Count - 1] : "（空）";
            }
            catch (Exception e)
            {
                Log.Error("PROBEPOPUPS AuditAll CRASH " + e);
                return "PROBEPOPUPS CRASH：" + e.GetType().Name + " @ " + e.StackTrace;
            }
        }

        // ────────────────────────────── Ⅰ 敌人受伤（3 武器 × 暴击/不暴击）
        private static void SecEnemyHit()
        {
            WeaponSlot[] slots = { WeaponSlot.Hoe, WeaponSlot.Can, WeaponSlot.Fertilizer };
            string[] names = { "锄头", "水壶", "肥料" };
            for (int i = 0; i < slots.Length; i++)
            {
                for (int c = 0; c < 2; c++)
                {
                    bool crit = c == 1;
                    RunSession s = Fresh();
                    try
                    {
                        PlayerActor pl = s.World.Player;
                        pl.Pos = Vector2.zero;
                        pl.AimDir = Vector2.right;
                        pl.SwitchWindup = 0f;
                        for (int k = 0; k < pl.WeaponCds.Length; k++) pl.WeaponCds[k] = 0f;
                        s.Progress.Stats.SetBase(StatId.CritRate, crit ? 1f : 0f);
                        EnemyActor e = SpawnGrunt(s, new Vector2(1.2f, 0f));
                        s.AimWorldLast = new Vector2(1.2f, 0f);
                        float hp0 = e.Health.Hp;
                        s.Popups.Clear();

                        Captured cap = Watch(() => s.DebugAttackOnce(slots[i]));
                        float hpDelta = hp0 - e.Health.Hp;
                        DamageResult r = s.World.LastHit;

                        // 期望的 kind：暴击那档是 `Crit`（白字换成黄字 + 放大 + 叹号）
                        PopupKind wantKind = crit ? PopupKind.Crit : PopupKind.DamageToEnemy;
                        RunSession.FloatingText drawn = Drawn(s, wantKind);
                        var row = Add("Ⅰ 敌人受伤", $"{names[i]} × {(crit ? "必暴击" : "无暴击")}",
                                      $"RawValue={r.RawValue:0.####}／实际掉血 {hpDelta:0.####}",
                                      wantKind, drawn != null ? drawn.Text : null,
                                      drawn != null ? drawn.Color : Color.clear,
                                      r.RawValue, true,
                                      e.Pos, drawn != null ? drawn.Pos : default,
                                      $"事件原文[{CapsText(cap)}]｜上屏 {s.Popups.Count} 条"
                                      + (crit ? "｜暴击样式" : ""));
                        // 数字之外的核对：结算与掉血得对上（否则弹字"对"也只是跟错数对上了）
                        if (Mathf.Abs(hpDelta - r.Dealt) > 1e-4f)
                            row.Extra += $"｜⚠ 掉血 {hpDelta:0.####} ≠ Dealt {r.Dealt:0.####}";
                    }
                    finally { s.Dispose(); }
                }
            }
        }

        // ────────────────────────────── Ⅱ 三种持续伤害
        private static void SecDots()
        {
            // 灼烧：每层 1.35/秒 × 1 层 ⇒ 期望弹 `-1.35`（**故意选小数**：这条正是在验 `0.##`）
            DotCase("灼烧", DotKind.Burn, 1.35f, 1, (s, e) => s.Dots.ApplyBurn(e.Id, 1.35f, 1));
            // 减速：每层 0.8/秒 × 2 层 = 1.6 ⇒ 期望 `-1.6`
            DotCase("减速", DotKind.Slow, 0.8f, 2, (s, e) => s.Dots.ApplySlow(e.Id, 0.8f, 0.12f, 2));
            // 毒区：每层 2.5/秒 × 3 层 = 7.5 ⇒ 期望 `-7.5`
            DotCase("毒区", DotKind.PoisonZone, 2.5f, 3,
                    (s, e) => s.Dots.AddPoisonZone(e.Pos, 2.4f, 2.5f, 3));
        }

        private static void DotCase(string label, DotKind kind, float perLayer, int layers, Action<RunSession, EnemyActor> apply)
        {
            RunSession s = Fresh();
            try
            {
                s.Progress.Stats.SetBase(StatId.CritRate, 0f);
                EnemyActor e = SpawnGrunt(s, new Vector2(1.0f, 0f));
                apply(s, e);
                s.Dots.SyncSlow(s.World);
                float hp0 = e.Health.Hp;
                s.Popups.Clear();

                Captured cap = Watch(() => s.Dots.Tick(1f, s.World, s.World.Player));
                float dealt = hp0 - e.Health.Hp;

                PopupKind want = PopupStyle.DamageKindFor(kind);
                RunSession.FloatingText drawn = Drawn(s, want);
                // ★ ①的期望值取 `PerSecond`（= 每层 × 层数），不是"这一秒实际掉的血" ★
                //   两者会有千分之几的差：`Dots.Tick(1f)` 会按 1/60 子步累计，
                //   而弹字印的是表里那个 `PerSecond`（结算用的就是它）⇒ 拿"实掉"当期望会把
                //   "探针夹具的取整误差"记成"弹字错了" ✗ 实掉另列在附注里对照 ✓
                float perSecond = perLayer * layers;
                Add($"Ⅱ 持续伤害（{label}）", $"{layers} 层 × 每秒 {perLayer:0.##} ⇒ 推 1.0 秒",
                    $"PerSecond={perSecond:0.####}（这一秒实掉 {dealt:0.####}）",
                    want, drawn != null ? drawn.Text : null,
                    drawn != null ? drawn.Color : Color.clear,
                    perSecond, true,
                    e.Pos, drawn != null ? drawn.Pos : default,
                    $"事件原文[{CapsText(cap)}]｜上屏 {s.Popups.Count} 条"
                    + $"｜期望图标 {IconName(SpecIcon(want))}");
                if (Mathf.Abs(dealt - perSecond) > 1e-3f)
                    _notes.Add($"（说明）{label}：这一秒实掉 {dealt:0.####}，表里 PerSecond = {perSecond:0.####}"
                             + " —— 差在 `Dots.Tick` 的 1/60 子步累计，**弹字印的就是 PerSecond**，两边不是同一个量");
            }
            finally { s.Dispose(); }
        }

        // ────────────────────────────── Ⅲ 玩家受伤（红 + 蓝）
        private static void SecPlayerHurt()
        {
            // ① 0.7 点 / 无护盾 ⇒ 1 行红 `-0.7`
            PlayerCase("0.7 点 / 无护盾", 0.7f, 10f, 0f,
                       new[] { (PopupKind.DamageToPlayer, true) });

            // ② 0.7 点 / 护盾 2 ⇒ 盾吃满、血一点没掉
            //   ★ 期望只有**蓝**那 1 行：红行的分量是 `HpDealt = 0`，
            //     而"0 点伤害"按规则**不该弹**（弹出 `-0` 正是用户判据③禁止的东西）✓
            PlayerCase("0.7 点 / 护盾 2", 0.7f, 10f, 2f,
                       new[] { (PopupKind.DamageToPlayerShield, true) });

            // ③ 4 点 / 护盾 2 ⇒ 盾 2 + 血 2 ⇒ **红蓝两行同屏**
            PlayerCase("4 点 / 护盾 2（盾 2 + 血 2）", 4f, 10f, 2f,
                       new[] { (PopupKind.DamageToPlayer, true), (PopupKind.DamageToPlayerShield, true) });

            // ④ 致死那一击：20 点打 10 血 2 盾 ⇒ 盾 2 + 血 10，两行之和 == 实际损失 12
            PlayerCase("致死一击 20 点 / 血10 盾2", 20f, 10f, 2f,
                       new[] { (PopupKind.DamageToPlayer, true), (PopupKind.DamageToPlayerShield, true) },
                       sumMustMatchLoss: true);

            // ⑤ ★ 截断的对照 ★：20 点打只剩 3 血的玩家 ⇒ 玩家行**必须**显示被截断后的 3，不是 20
            //   （这是上一轮裁定"玩家行取分量、不取 `RawValue`"的那条；本探针把它钉住，
            //    免得这次改门槛时顺手把语义改坏 ✓）
            PlayerCase("血将被清空 20 点 / 血3 无盾", 20f, 3f, 0f,
                       new[] { (PopupKind.DamageToPlayer, true) },
                       sumMustMatchLoss: true);

            // ⑥ 敌人真实链路：基础兵蹭一下（`enemies.json` `grunt.touchDamage = 0.7`）
            RealGruntTouch();
        }

        /// <summary>
        /// ★ 这条是**用户要的那个场景**：基础兵碰玩家一下。
        /// 走的是敌人真实攻击路径（`EnemyActor.TouchTick` → `World.DealDamage`），
        /// 不是自己拼一个 0.7 的 `DamageRequest` —— 否则验的是"我能不能弹"，不是"小兵蹭一下弹不弹" ✓
        ///
        /// ⚠️ **这条在无头夹具里没跑通，如实写出来**：
        ///    三档距离（0 / 0.3 / 0.9m）各推 3 秒，`NearestAlly` 有、最近距离都在接触半径内，
        ///    **命中却都是 0 次** ⇒ `TouchTick` 一次都没走到 ⇒ 是**夹具**的问题（敌人在这套
        ///    最小世界里没被判成"可以打玩家"），不是游戏的问题 ✓
        ///    ⇒ 所以它**不再作为 pass/fail 行**（拿一个夹具故障去判产品是错的），
        ///      降级成一条**读数附注**，把三档读数原样打出来 ✓
        ///    ★ 而同一场景在**真会话**里跑通了 —— 见抓图序列 ⑦
        ///      （真敌人 / 真 `TouchTick` / 真冷却 / 真无敌帧）：
        ///       改前 `玩家 10 → 8.82（掉 1.185）｜上屏 0 条`
        ///       改后 `上屏 1~2 条「-0.46」…`
        ///      那一条才是"小兵蹭玩家一下弹不弹"的**判据** ✓
        /// </summary>
        private static void RealGruntTouch()
        {
            float[] dists = { 0f, 0.3f, 0.9f };
            var sb = new StringBuilder();
            for (int k = 0; k < dists.Length; k++)
            {
                RunSession s = Fresh();
                try
                {
                    PlayerActor pl = s.World.Player;
                    pl.Pos = Vector2.zero;
                    pl.Health.Setup(10f, 0f);           // 无护盾：全部落在血上
                    pl.InvulnRemaining = 0f;
                    EnemyActor e = SpawnGrunt(s, new Vector2(dists[k], 0f), 2.8f);
                    float touch = e.Def.TouchDamageForWave(1);
                    string ally = s.World.NearestAlly(e.Pos) != null ? "有" : "null";
                    float minDist = 999f;
                    int hits = 0;
                    s.Popups.Clear();

                    // ★ 先推 1 步"热身" ★
                    //   `Health.Setup(10f, 0f)` 把 MaxShield 设成 0，而 `PlayerActor.Tick` 会按属性
                    //   把 MaxShield 同步成 2（并补满盾）⇒ 第一个固定步里"血+盾总量"是**上升**的。
                    //   不热身的话，前几次挨打会被算成"总量还比初始高" ⇒ 计数恒 0（实测踩过）✗
                    s.World.Simulate(GameClock.FixedDt);
                    s.Popups.Clear();
                    float prevTotal = pl.Health.Hp + pl.Health.Shield;
                    float hp0 = pl.Health.Hp;

                    // ★ "掉血"必须算 **血 + 盾** ★
                    //   上一版只盯 `Hp`，于是"伤害打进护盾"那些次全被数成 0 命中 ⇒ 读数与弹字条数自相矛盾
                    //   （实测：0 命中却有 4 条弹字）✗ 现在两者一起算 ✓
                    for (int i = 0; i < 180; i++)
                    {
                        s.World.Simulate(GameClock.FixedDt);
                        minDist = Mathf.Min(minDist, Vector2.Distance(pl.Pos, e.Pos));
                        float total = pl.Health.Hp + pl.Health.Shield;
                        if (total < prevTotal - 1e-5f) { hits++; prevTotal = total; }
                    }
                    float lostHp = hp0 - pl.Health.Hp;

                    var popText = new StringBuilder();
                    for (int i = 0; i < s.Popups.Count; i++)
                        popText.Append('「').Append(s.Popups[i].Text).Append('」');
                    sb.Append($"    {dists[k]:0.0}m：touchDamage={touch:0.####}｜挨打 {hits} 次"
                            + $"｜血 -{lostHp:0.####} 盾 {pl.Health.Shield:0.##}（余）"
                            + $"｜最近 {minDist:0.00}m（接触半径 {pl.Radius + e.Radius + 0.06f:0.00}m）"
                            + $"｜NearestAlly {ally}"
                            + $"｜上屏 {s.Popups.Count} 条{(popText.Length > 0 ? "：" + popText : "")}\n");
                }
                finally { s.Dispose(); }
            }
            _notes.Add("Ⅲ 真实链路的**无头夹具**读数（仅作诊断，不作判据）：\n" + sb.ToString().TrimEnd('\n'));
        }

        private static void PlayerCase(string label, float dmg, float hp, float shield,
                                (PopupKind kind, bool negative)[] want, bool sumMustMatchLoss = false)
        {
            RunSession s = Fresh();
            try
            {
                PlayerActor pl = s.World.Player;
                pl.Pos = Vector2.zero;
                pl.Health.Setup(hp, shield);
                pl.InvulnRemaining = 0f;
                s.Popups.Clear();

                DamageResult r = default;
                Captured cap = Watch(() =>
                {
                    r = s.World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical,
                        RawValue = dmg,
                        Source = null,
                        Target = pl,
                        IgnoreDodge = true
                    });
                });

                string components = $"分量 血 {r.HpDealt:0.####} / 盾 {r.ShieldDealt:0.####}"
                                  + $"／Dealt {r.Dealt:0.####}／上屏 {s.Popups.Count} 条";

                // 每一"应出现的那一行"单独判（红行与蓝行各自一个数）
                for (int i = 0; i < want.Length; i++)
                {
                    PopupKind k = want[i].kind;
                    float expect = k == PopupKind.DamageToPlayer ? r.HpDealt : r.ShieldDealt;
                    RunSession.FloatingText drawn = Drawn(s, k);
                    // 锚点 = 角色坐标 + **规格里的固定错位**（红 +0.38 / 蓝 -0.38）
                    // ⇒ 判据是"上屏落在该点周围 0.6m（散布半径）内"，与代码用同一个 `popupRadius` 口径 ✓
                    Add($"Ⅲ 玩家受伤（{label}）",
                        $"{dmg:0.##} 点打 血{hp:0.#}/盾{shield:0.#} ⇒ 期望{KindName(k)}行",
                        k == PopupKind.DamageToPlayer ? $"HpDealt={r.HpDealt:0.####}" : $"ShieldDealt={r.ShieldDealt:0.####}",
                        k, drawn != null ? drawn.Text : null,
                        drawn != null ? drawn.Color : Color.clear,
                        expect, want[i].negative,
                        pl.Pos + SpecStack(k), drawn != null ? drawn.Pos : default,
                        components + $"｜事件原文[{CapsText(cap)}]");
                }

                // ★ 红行与蓝行的**中心距**（"两行会不会叠成一行"的可复算判据）★
                //   改前：只有 0.6m 随机散布 ⇒ 实测出现过 0.13m（图里读成 `-22`）✗
                //   改后：另有固定 ±0.38m ⇒ 中心距必须 ≥ 0.76 − 0.6 = 0.16m，实测应在 0.7m 上下 ✓
                if (want.Length == 2)
                {
                    RunSession.FloatingText red = Drawn(s, PopupKind.DamageToPlayer);
                    RunSession.FloatingText blue = Drawn(s, PopupKind.DamageToPlayerShield);
                    if (red != null && blue != null)
                    {
                        float gap = Vector2.Distance(red.Pos, blue.Pos);
                        bool ok = gap >= 0.16f;
                        _notes.Add($"{(ok ? "✓" : "✗")} {label}：红行与蓝行中心距 {gap:0.00}m"
                                 + "（改前只有 0.6m 随机散布 ⇒ 实测叠到 0.13m，图里读成 `-22`；"
                                 + "改后另有固定 ±0.38m，下界 0.16m、典型 0.7~1.0m）");
                    }
                }

                if (sumMustMatchLoss)
                {
                    float sum = 0f;
                    for (int i = 0; i < s.Popups.Count; i++)
                    {
                        RunSession.FloatingText ft = s.Popups[i];
                        if (ft.Kind != PopupKind.DamageToPlayer && ft.Kind != PopupKind.DamageToPlayerShield) continue;
                        if (ExtractNumber(ft.Text, out _, out _, out float v)) sum += Mathf.Abs(v);
                    }
                    bool ok = Mathf.Abs(sum - r.Dealt) <= 5.1e-3f;
                    _notes.Add($"{(ok ? "✓" : "✗")} 致死那一击：红蓝两行之和 {sum:0.####} {(ok ? "==" : "≠")} 实际损失 {r.Dealt:0.####}");
                }
            }
            finally { s.Dispose(); }
        }

        // ────────────────────────────── Ⅳ 门槛（极小伤害仍被挡）
        private static void SecGate()
        {
            float[] samples = { 0f, 0.04f, 0.05f, 0.7f };
            var sb = new StringBuilder();
            for (int i = 0; i < samples.Length; i++)
            {
                RunSession s = Fresh();
                try
                {
                    PlayerActor pl = s.World.Player;
                    pl.Health.Setup(10f, 0f);
                    pl.InvulnRemaining = 0f;
                    s.Popups.Clear();
                    float d = samples[i];
                    Captured cap = Watch(() =>
                    {
                        if (d > 0f)
                            s.World.DealDamage(new DamageRequest
                            {
                                Type = DamageType.Physical, RawValue = d, Target = pl, IgnoreDodge = true
                            });
                    });
                    sb.Append($"    {d:0.###} 点 ⇒ 弹字 {s.Popups.Count} 条");
                    if (s.Popups.Count > 0) sb.Append($"「{s.Popups[0].Text}」");
                    sb.Append('\n');
                }
                finally { s.Dispose(); }
            }
            _notes.Add("Ⅳ 门槛（极小伤害）逐档实测：\n" + sb.ToString().TrimEnd('\n'));
        }

        // ────────────────────────────── Ⅴ 收割 金币 / 经验
        private static void SecHarvest()
        {
            HarvestCase("幸运 0 / 倍率 1.0（基准）", 0f, 1f, 1f);
            HarvestCase("幸运 100%（必触发 X2）", 1f, 1f, 1f);
            HarvestCase("货币/经验倍率 1.5", 0f, 1.5f, 1.5f);
        }

        private static void HarvestCase(string label, float luck, float goldMult, float expMult)
        {
            RunSession s = Fresh(31415UL);
            try
            {
                StatBlock st = s.Progress.Stats;
                st.SetBase(StatId.Luck, luck);
                st.SetBase(StatId.GoldMult, goldMult);
                st.SetBase(StatId.ExpMult, expMult);
                SeedDef radish = TableDatabase.GetSeed("radish_guard");
                TuningDef t = TableDatabase.Tuning;
                FarmPlot plot = s.World.Farm.Slot(0, 0);
                plot.BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
                plot.TickGrowth(999f);
                int g0 = s.World.GoldEarned;
                float e0 = s.World.ExpEarned;
                s.Popups.Clear();

                Captured cap = Watch(() => s.World.HarvestPlot(plot));
                int gotGold = s.World.GoldEarned - g0;
                float gotExp = s.World.ExpEarned - e0;

                RunSession.FloatingText g = Drawn(s, PopupKind.GoldGain);
                RunSession.FloatingText x = Drawn(s, PopupKind.ExpGain);
                // 收割那两条的锚点是**地块中心**，而发布端故意错开了 `(-0.45,+0.35)` / `(+0.45,+0.35)`
                // （三条不重叠，见 `Actors.HarvestPlot`）⇒ 容差 = 0.57 错位 + 0.6 散布 ≈ 1.2，取 1.25 ✓
                Add("Ⅴ 收割 · 金币", label, $"实际到账 {gotGold} 金",
                    PopupKind.GoldGain, g != null ? g.Text : null,
                    g != null ? g.Color : Color.clear,
                    gotGold, false, plot.World, g != null ? g.Pos : default,
                    $"事件原文[{CapsText(cap)}]｜锚点=地块中心，发布端另有 (-0.45,+0.35) 设计错位",
                    posTol: 1.25f);
                Add("Ⅴ 收割 · 经验", label, $"实际到账 {gotExp:0.####} 经验（显示取整）",
                    PopupKind.ExpGain, x != null ? x.Text : null,
                    x != null ? x.Color : Color.clear,
                    Mathf.RoundToInt(gotExp), false, plot.World, x != null ? x.Pos : default,
                    $"事件原文[{CapsText(cap)}]｜锚点=地块中心，发布端另有 (+0.45,+0.35) 设计错位",
                    posTol: 1.25f);

                // 幸运的 ` X2` 后缀也得在（用户需求①）
                if (luck >= 1f)
                {
                    bool ok = g != null && g.Text != null && g.Text.Contains("X2")
                              && x != null && x.Text != null && x.Text.Contains("X2");
                    _notes.Add($"{(ok ? "✓" : "✗")} 幸运触发时两条收益弹字都带 ` X2`：金币「{(g != null ? g.Text : "无")}」/ 经验「{(x != null ? x.Text : "无")}」");
                }
            }
            finally { s.Dispose(); }
        }

        // ────────────────────────────── Ⅵ 回血
        private static void SecHeal()
        {
            HealCase("爱心回血 +2（`Combat.Heal`，爱心拾取走的就是它）", 8f, 2f);
            HealCase("每波回满（`StartWave` → `Heal(MaxHp)`）", 4f, 0f, startWave: true);
            // 小于 1 点：**判据是"必须不弹"**（不是"应该弹"）—— 这一档是 `Combat.Heal`
            // 自己的门槛（`gained >= 1f`），与伤害的 `Popup` 门槛是**两个不同的门**，都要钉住 ✓
            HealCase("0.4 点回血（小于 1 点）", 8f, 0.4f, expectAbsent: true);
        }

        private static void HealCase(string label, float hpNow, float heal, bool startWave = false,
                                     bool expectAbsent = false)
        {
            RunSession s = Fresh();
            try
            {
                PlayerActor pl = s.World.Player;
                pl.Pos = Vector2.zero;
                pl.Health.Setup(10f, 0f);
                pl.Health.Hp = hpNow;
                s.Popups.Clear();
                float h0 = pl.Health.Hp;
                Captured cap = Watch(() =>
                {
                    if (startWave) s.StartWave(2);
                    else pl.Health.Heal(heal);
                });
                float gained = pl.Health.Hp - h0;
                RunSession.FloatingText drawn = Drawn(s, PopupKind.Heal);

                if (expectAbsent)
                {
                    bool ok = drawn == null && cap.Count == 0 && gained < 1f;
                    _notes.Add($"{(ok ? "✓" : "✗")} {label}：实回 {gained:0.####} 点 ⇒ 弹字 {cap.Count} 条"
                             + "（`Combat.Heal` 的门槛是 `gained >= 1f`；实心的爱心 `Heal` 被 `Max(1f, HeartHeal)` 保证 ≥ 1，"
                             + "所以这个门在实战里不会吞掉一次真回血 ✓）");
                    return;
                }

                Add("Ⅵ 回血", label, $"实际回血 {gained:0.####}",
                    PopupKind.Heal, drawn != null ? drawn.Text : null,
                    drawn != null ? drawn.Color : Color.clear,
                    Mathf.RoundToInt(gained), false, pl.Pos, drawn != null ? drawn.Pos : default,
                    $"事件原文[{CapsText(cap)}]");
            }
            finally { s.Dispose(); }
        }

        // ────────────────────────────── Ⅶ 升级 / 成熟 / 缺水
        private static void SecMisc()
        {
            // 升级：`Lv N`（无数值符号，只有等级）
            {
                RunSession s = Fresh();
                try
                {
                    s.Popups.Clear();
                    Captured cap = Watch(() => s.World.AddExp(99999f));
                    RunSession.FloatingText drawn = Drawn(s, PopupKind.LevelUp);
                    Add("Ⅶ 升级", "灌经验触发升级（`OnLevelUp`）",
                        $"玩家等级 {s.Progress.Level}", PopupKind.LevelUp,
                        drawn != null ? drawn.Text : null,
                        drawn != null ? drawn.Color : Color.clear,
                        0f, false, s.World.Player.Pos, drawn != null ? drawn.Pos : default,
                        $"事件原文[{CapsText(cap)}]").HasNumber = false;
                }
                finally { s.Dispose(); }
            }
            // 成熟
            {
                RunSession s = Fresh(6161UL);
                try
                {
                    SeedDef radish = TableDatabase.GetSeed("radish_guard");
                    TuningDef t = TableDatabase.Tuning;
                    FarmPlot plot = s.World.Farm.Slots[0];
                    plot.BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
                    s.Popups.Clear();
                    int frames = Mathf.CeilToInt(radish.GrowTime / GameClock.FixedDt) + 120;
                    Captured cap = Watch(() =>
                    {
                        for (int i = 0; i < frames && plot.State == PlotState.Growing; i++)
                            s.World.Simulate(GameClock.FixedDt);
                    });
                    RunSession.FloatingText drawn = Drawn(s, PopupKind.PlantMature);
                    Add("Ⅶ 成熟", "推帧到作物成熟", $"地块 {plot.State}", PopupKind.PlantMature,
                        drawn != null ? drawn.Text : null,
                        drawn != null ? drawn.Color : Color.clear,
                        0f, false, plot.World, drawn != null ? drawn.Pos : default,
                        $"事件原文[{CapsText(cap)}]").HasNumber = false;
                }
                finally { s.Dispose(); }
            }
            // 缺水（`PopupStyle.WantsBang` 会补一个全角叹号）
            {
                RunSession s = Fresh(6161UL);
                try
                {
                    SeedDef radish = TableDatabase.GetSeed("radish_guard");
                    TuningDef t = TableDatabase.Tuning;
                    FarmPlot plot = s.World.Farm.Slots[0];
                    // 只给 1 点水 ⇒ 很快变渴（`BeginPlant` 的 waterMax 是 int，不是 float）
                    plot.BeginPlant(t.farm, radish, 1, radish.WaterDrain);
                    s.Popups.Clear();
                    RunSession.FloatingText drawn = null;
                    Captured cap = Watch(() =>
                    {
                        for (int i = 0; i < 1200 && drawn == null; i++)
                        {
                            s.World.Simulate(GameClock.FixedDt);
                            drawn = Drawn(s, PopupKind.PlantThirst);
                        }
                    });
                    Add("Ⅶ 缺水", "地块水见底（`ThirstyNotified` 只弹一次）",
                        $"地块 {plot.State}／槽内水 {plot.Water:0.####}", PopupKind.PlantThirst,
                        drawn != null ? drawn.Text : null,
                        drawn != null ? drawn.Color : Color.clear,
                        0f, false, plot.World, drawn != null ? drawn.Pos : default,
                        $"事件原文[{CapsText(cap)}]｜末尾带全角叹号 = `WantsBang`").HasNumber = false;
                }
                finally { s.Dispose(); }
            }
        }

        // ────────────────────────────── Ⅷ 按设计**没有**弹字的四类
        private static void SecNoPopup()
        {
            // ① 护盾获得（回盾 / 满盾）：直接改 `Health.Shield`，没有任何 `FloatingTextEvent`
            {
                RunSession s = Fresh();
                try
                {
                    PlayerActor pl = s.World.Player;
                    pl.Health.Setup(10f, 2f);
                    pl.Health.Shield = 0.2f;
                    s.Popups.Clear();
                    Captured cap = Watch(() => pl.Health.RefillShield());
                    _notes.Add($"{(cap.Count == 0 ? "✓" : "✗")} 护盾获得：`RefillShield()` 后弹字 {cap.Count} 条"
                             + $"（护盾 {pl.Health.Shield:0.##}/{pl.Health.MaxShield:0.##}）—— 规格是本类「—」（无弹字）");
                }
                finally { s.Dispose(); }
            }
            // ② 商店购买：走 `SetPrompt` 文本，不发弹字
            {
                RunSession s = Fresh();
                try
                {
                    s.Gold = 999;
                    s.Shop.Refresh(s);
                    s.Popups.Clear();
                    int n = s.Shop.Offers.Count;
                    string why = "";
                    Captured cap = Watch(() => s.Shop.TryBuy(0, s, out why));
                    _notes.Add($"{(cap.Count == 0 ? "✓" : "✗")} 商店购买：货位 {n} 个，买第 1 格后弹字 {cap.Count} 条"
                             + $"（结果「{why}」/ 提示行「{s.Prompt}」）—— 规格是本类「—」");
                }
                finally { s.Dispose(); }
            }
            // ③ 抽卡属性生效：写 `StatBlock`，不发弹字（面板上另有显示）
            {
                RunSession s = Fresh();
                try
                {
                    s.DrawRewardCards();
                    s.Popups.Clear();
                    string label = s.RewardCards.Count > 0 ? s.RewardCards[0].Label : "（没抽到）";
                    Captured cap = Watch(() => s.PickRewardCard(0));
                    _notes.Add($"{(cap.Count == 0 ? "✓" : "✗")} 抽卡属性生效：选「{label}」后弹字 {cap.Count} 条"
                             + " —— 规格是本类「—」");
                }
                finally { s.Dispose(); }
            }
            // ④ 击杀奖励：v167 起 `SpawnPickup` 已删 ⇒ 击杀**不再产出金币/经验**
            {
                RunSession s = Fresh();
                try
                {
                    EnemyActor e = SpawnGrunt(s, new Vector2(1f, 0f), 2.8f);
                    int defGold = e.Def.Gold;
                    float defExp = e.Def.Exp;
                    int g0 = s.World.GoldEarned;
                    float e0 = s.World.ExpEarned;
                    s.Popups.Clear();
                    Captured cap = Watch(() =>
                        s.World.DealDamage(new DamageRequest
                        {
                            Type = DamageType.Physical, RawValue = 99f,
                            Source = s.World.Player, Target = e, IgnoreDodge = true
                        }));
                    int goldDelta = s.World.GoldEarned - g0;
                    float expDelta = s.World.ExpEarned - e0;
                    int gainKinds = 0;
                    for (int i = 0; i < cap.Count; i++)
                        if (cap.Kind[i] == PopupKind.GoldGain || cap.Kind[i] == PopupKind.ExpGain) gainKinds++;
                    bool ok = gainKinds == 0 && goldDelta == 0;
                    _notes.Add($"{(ok ? "✓" : "✗")} 击杀奖励：打死基础兵（`enemies.json` gold={defGold} exp={defExp}）后"
                             + $" 金币+{goldDelta} / 经验+{expDelta:0.##}，收益类弹字 {gainKinds} 条"
                             + " —— **v167 起击杀不再产金/验**（`SpawnPickup` 已删，SelfCheck 有反向断言钉着）⇒ 本类「金/经验」由「收割」承担");
                }
                finally { s.Dispose(); }
            }
        }

        // ══════════════════════════════════════════════════════════
        //  渲染：总表 + 明细 + 汇总
        // ══════════════════════════════════════════════════════════
        private static List<string> Render()
        {
            var chunks = new List<string>();
            var sb = new StringBuilder();

            // ── 汇总行 ──
            int fail = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                Verdict(_rows[i]);
                if (_rows[i].Fail) fail++;
            }
            var bad = new List<string>();
            for (int i = 0; i < _rows.Count; i++)
                if (_rows[i].Fail) bad.Add($"{_rows[i].Cat}｜{_rows[i].Trigger}｜弹字「{_rows[i].Text ?? "（没弹）"}」");

            sb.Append("═══ 弹字全表（只读探针｜四查：① 数字 ② 符号 ③ 格式 ④ 位置/颜色/图标）═══\n");
            sb.Append($"共 {_rows.Count} 行断言｜通过 {_rows.Count - fail}｜失败 {fail}\n");
            sb.Append($"门槛读数：`Popup` 现用门槛见下 Ⅳ；`Combat.Heal` 的门槛是 `gained >= 1f`（另一处）\n");
            if (fail > 0)
            {
                sb.Append("── 失败清单 ──\n");
                for (int i = 0; i < bad.Count; i++) sb.Append("  ✗ ").Append(bad[i]).Append('\n');
            }
            chunks.Add(sb.ToString());

            // ── 总表（每行一行）──
            sb = new StringBuilder();
            sb.Append("── 总表 ──\n");
            sb.Append("类／触发｜取数源｜弹字原文｜四查\n");
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                sb.Append("  ").Append(r.Cat).Append("／").Append(r.Trigger)
                  .Append("｜").Append(r.Source)
                  .Append("｜「").Append(r.Text ?? "（没弹）").Append("」")
                  .Append("｜").Append(VerdictAgain(r)).Append('\n');
            }
            chunks.Add(sb.ToString());

            // ── 明细：每行带上事件原文与额外说明 ──
            sb = new StringBuilder();
            sb.Append("── 明细（含事件原文 / 分量 / 期望来源）──\n");
            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                sb.Append("  [").Append(r.Cat).Append("] ").Append(r.Trigger).Append('\n');
                sb.Append("     期望 kind=").Append(KindName(r.Kind))
                  .Append("（").Append(r.Kind).Append("）｜取数源 ").Append(r.Source).Append('\n');
                sb.Append("     上屏「").Append(r.Text ?? "（没弹）").Append("」｜").Append(VerdictAgain(r)).Append('\n');
                if (!string.IsNullOrEmpty(r.Extra)) sb.Append("     ").Append(r.Extra).Append('\n');
            }
            chunks.Add(sb.ToString());

            // ── 附注（门槛 / X2 / 零弹字四类 / 不一致）──
            sb = new StringBuilder();
            sb.Append("── 附注 ──\n");
            for (int i = 0; i < _notes.Count; i++) sb.Append("  ").Append(_notes[i]).Append('\n');
            chunks.Add(sb.ToString());
            return chunks;
        }

        private static string VerdictAgain(Row r) => Verdict(r);

        // ══════════════════════════════════════════════════════════
        //  抓图序列（只有 `Library/yowei_popups_shot.on` 存在时才跑）
        //
        //  ★ 为什么不用 `ScreenCapture.CaptureScreenshot(路径)` ★（上一轮踩过，实测）
        //    它把写盘排到**帧末**，而"排到了没有 / 写成功没有"没有任何返回值——
        //    连调 4 次、日志 4 行都打出来了，磁盘上只有第 3 张 ✗
        //  ⇒ 用 `CaptureScreenshotAsTexture()` + `EncodeToPNG()` + `File.WriteAllBytes`：
        //    同步拿像素、自己写、自己报字节数，还能顺手做**黑图判定**（抽样算亮度）✓
        //
        //  ★ 为什么先挪玩家、再等 0.9 秒 ★
        //    弹字按**世界坐标投影**上屏（`ImGuiHud.DrawPopups` 用 `Camera.main.WorldToScreenPoint`），
        //    而镜头是跟玩家的（插值）⇒ 玩家瞬移后立刻弹，字可能还在视野外 ✗
        // ══════════════════════════════════════════════════════════
        private static RunSession LiveSession()
        {
            var live = GameBootstrap.Live;
            if (live == null) return null;
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i].Session != null) return live[i].Session;
            return null;
        }

        private IEnumerator ShotSequence()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            RunSession s = LiveSession();
            if (s == null || s.World == null || s.World.Player == null)
            {
                Log.Warn("PROBEPOPUPS 抓图：取不到当前局会话 ⇒ 什么都不做");
                yield break;
            }
            PlayerActor pl = s.World.Player;
            s.SetPhasePublic(PhaseState.Combat);
            s.World.Enemies.Clear();
            pl.Pos = Vector2.zero;                 // 先挪到场地中心，让镜头跟上
            yield return new WaitForSecondsRealtime(0.9f);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(new Vector3(pl.Pos.x, 1.2f, pl.Pos.y));
                Log.Info($"PROBEPOPUPS 抓图：玩家在视口 ({vp.x:0.00},{vp.y:0.00}) z={vp.z:0.0}"
                         + (vp.z > 0f && vp.x > 0.1f && vp.x < 0.9f && vp.y > 0.1f && vp.y < 0.9f
                            ? " ⇒ 在画面内"
                            : " ⇒ **可能不在画面内**（弹字会看不见）"));
            }

            // ① 0.7 点 / 无护盾 ⇒ 期望**红 `-0.7` 一行**
            yield return OneHit(s, pl, 0.7f, 10f, 0f, "v191-popup-1-07-no-shield.png");

            // ② 0.7 点 / 护盾 2 ⇒ 盾吃满 ⇒ 期望**蓝 `-0.7` 一行**（红行的分量是 0，按规则不弹）
            yield return OneHit(s, pl, 0.7f, 10f, 2f, "v191-popup-2-07-with-shield.png");

            // ③ 4 点 / 护盾 2 ⇒ 盾 2 + 血 2 ⇒ **红蓝两行同屏**
            yield return OneHit(s, pl, 4f, 10f, 2f, "v191-popup-3-two-lines.png");

            // ④ 致死那一击（20 点 / 血 10 盾 2）⇒ 红 `-10` + 蓝 `-2`，之和 12 == 实际损失
            yield return OneHit(s, pl, 20f, 10f, 2f, "v191-popup-4-lethal.png");

            // ⑤ 全类别一览（`DebugPopupDemo` 是工程自带的七档演示）
            s.SetPhasePublic(PhaseState.Combat);
            pl.Pos = Vector2.zero;
            s.Popups.Clear();
            s.DebugPopupDemo();
            Log.Info("PROBEPOPUPS 抓图 ⑤ 演示弹字：" + Dump(s));
            yield return new WaitForSecondsRealtime(0.12f);
            yield return Shot("v191-popup-5-palette.png");

            // ⑥ 肥料爆炸的 `BOMB！！！`（这条由表现层 WorldView 发布 ⇒ 必须在真视图里才看得到）
            s.SetPhasePublic(PhaseState.Combat);
            s.Popups.Clear();
            s.AimWorldLast = pl.Pos + new Vector2(1.6f, 0f);
            s.DebugAttackOnce(WeaponSlot.Fertilizer);      // 真投掷入口（含前摇推完 = 真炸）
            yield return new WaitForSecondsRealtime(0.5f);
            Log.Info("PROBEPOPUPS 抓图 ⑥ 爆炸后弹字：" + Dump(s));
            yield return Shot("v191-popup-6-bomb.png");

            // ⑦ ★★ **用户原话那个场景**：基础兵走过来蹭玩家一下 ★★
            //   这条才是"事实"，因为它一条假都没掺：
            //     真会话 · 真敌人 · 真 `TouchTick` · 真冷却 · 真无敌帧 · 真渲染
            //   上面 ①~④ 是"手动打一个数"，这条是"游戏自己打出来的那一数" ✓
            s.SetPhasePublic(PhaseState.Combat);
            s.World.Enemies.Clear();
            s.Popups.Clear();
            pl.Pos = Vector2.zero;
            pl.Health.Setup(10f, 0f);              // 无护盾：0.7 全部落在血上
            pl.InvulnRemaining = 0f;
            s.World.SpawnEnemy("grunt", new Vector2(2.0f, 0f), 1);
            EnemyActor grunt = s.World.Enemies[s.World.Enemies.Count - 1];
            grunt.Pos = new Vector2(2.0f, 0f);
            float gHp0 = pl.Health.Hp;
            float touch = grunt.Def.TouchDamageForWave(1);
            Log.Info($"PROBEPOPUPS 抓图 ⑦ 放一只基础兵在 2.0m 处，touchDamage（第 1 分钟）= {touch:0.####}"
                     + $"，血 {pl.Health.Hp:0.##}/10 盾 {pl.Health.Shield:0.##} ⇒ 等它走过来蹭");

            // 每帧看一次：**第一条玩家受击弹字一出现就抓图**（不猜时间点）
            float waited = 0f;
            while (waited < 6f && Drawn(s, PopupKind.DamageToPlayer) == null
                   && Drawn(s, PopupKind.DamageToPlayerShield) == null)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            float lost = gHp0 - pl.Health.Hp;
            Log.Info($"PROBEPOPUPS 抓图 ⑦ 等了 {waited:0.00}s："
                     + $"玩家共掉 {lost:0.####}（血 {pl.Health.Hp:0.##}/10 盾 {pl.Health.Shield:0.##}）"
                     + $"｜距离 {Vector2.Distance(pl.Pos, grunt.Pos):0.00}m"
                     + $"｜上屏 {Dump(s)}");
            yield return new WaitForSecondsRealtime(0.12f);
            yield return Shot("v191-popup-7-real-grunt-touch.png");

            Log.Info("PROBEPOPUPS SHOT DONE");
        }

        private static string Dump(RunSession s)
        {
            if (s.Popups.Count == 0) return "（0 条）";
            var sb = new StringBuilder();
            for (int i = 0; i < s.Popups.Count; i++)
                sb.Append('「').Append(s.Popups[i].Text).Append('」');
            return $"{s.Popups.Count} 条：" + sb;
        }

        /// <summary>打一下 → 记读数 → 等 0.12 秒（弹字动画在峰值附近）→ 帧末抓图。</summary>
        private IEnumerator OneHit(RunSession s, PlayerActor pl, float dmg, float hp, float shield, string name)
        {
            pl.Pos = Vector2.zero;
            pl.Health.Setup(hp, shield);
            pl.InvulnRemaining = 0f;
            s.Popups.Clear();
            DamageResult r = s.World.DealDamage(new DamageRequest
            {
                Type = DamageType.Physical, RawValue = dmg, Target = pl, IgnoreDodge = true
            });
            Log.Info($"PROBEPOPUPS 抓图 {name}: 打 {dmg:0.##} 点 / 血{hp:0.#}盾{shield:0.#}"
                     + $" ⇒ 分量 血 {r.HpDealt:0.####} / 盾 {r.ShieldDealt:0.####}"
                     + $"｜上屏 {Dump(s)}"
                     + $"｜血量剩 {pl.Health.Hp:0.##}/{pl.Health.MaxHp:0.##} 盾 {pl.Health.Shield:0.##}/{pl.Health.MaxShield:0.##}");
            yield return new WaitForSecondsRealtime(0.12f);
            yield return Shot(name);
        }

        /// <summary>帧末抓后缓冲（含 OnGUI）→ 写盘 → 报字节数 + 亮度统计（黑图判定）。</summary>
        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            byte[] png = null;
            string stat = "";
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) { Log.Error("PROBEPOPUPS 抓图为空：" + name); yield break; }
                Color32[] px = tex.GetPixels32();
                int lit = 0, samples = 0, lumaMin = 255, lumaMax = 0;
                long lumaSum = 0;
                for (int i = 0; i < px.Length; i += 37)
                {
                    Color32 c = px[i];
                    int luma = (c.r * 30 + c.g * 59 + c.b * 11) / 100;
                    if (luma < lumaMin) lumaMin = luma;
                    if (luma > lumaMax) lumaMax = luma;
                    if (luma > 40) lit++;
                    lumaSum += luma;
                    samples++;
                }
                png = tex.EncodeToPNG();
                stat = $"｜{tex.width}×{tex.height}｜{png.Length} 字节"
                     + $"｜亮度 min {lumaMin} max {lumaMax} 均值 {(samples > 0 ? lumaSum / samples : 0)}"
                     + $"｜亮像素 {lit}/{samples}"
                     + (lumaMax < 12 ? "｜**判为黑图**" : "");
            }
            catch (Exception e)
            {
                Log.Error("PROBEPOPUPS 抓图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
                yield break;
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }

            try
            {
                if (!Directory.Exists(ShotDir)) Directory.CreateDirectory(ShotDir);
                string path = Path.GetFullPath(Path.Combine(ShotDir, Tag + name));
                File.WriteAllBytes(path, png);
                Log.Info($"PROBEPOPUPS 写图 {path.Replace('\\', '/')}{stat}");
            }
            catch (Exception e)
            {
                Log.Error("PROBEPOPUPS 写图失败 " + name + "：" + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
