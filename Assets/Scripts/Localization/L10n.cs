using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 界面语言。**顺序即 `PlayerPrefs` 里存的整数**，不要重排。
    ///
    /// ★ v242：只剩 `zh / en / ja` 三门 ★
    ///   原来还有 `ur / ru / ko`，但那三门各只填了 19 条样例、其余 966 条全靠回退
    ///   ⇒ 用户切过去看到的是英文（有时候是中文），比"没有这门语言"更糟 ✗
    ///   ⇒ 干脆删掉：少 3 份词表、少 1932 条待翻、设置面板从 6 颗按钮变 3 颗 ✓
    ///   ⚠️ 老存档里若存的是 3~5（旧编号），`EnsurePrefs` 会判成越界 ⇒ 回落到系统语言 ✓
    /// </summary>
    public enum Lang
    {
        Zh = 0,
        En = 1,
        Ja = 2,
    }

    /// <summary>
    /// ★ 本地化核心 ★（`Assets/Resources/Data/l10n_*.json` 是它的唯一数据源）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么要两套查表（`T(key)` 与 `S(原文)`）
    /// ══════════════════════════════════════════════════════════════════════
    /// 工程里玩家可见的字幕有 493 条，散在 `TownHud` / `ImGuiHud` / `LoginHud` /
    /// `BossRewardPopup` / `TownBuildings` / `RunSession` 六万个字符里。逐条把
    /// `"金币"` 改成 `L10n.T("hud.gold")` 要动几百个调用点、且**每加一条新文本都要再动一次**。
    /// 所以这里同时提供两条路：
    ///
    ///   ① `T(key)`      —— 有名字的键（`hud.gold`、`ui.settings.title`…）。设置面板、
    ///                       主城金币条、死亡界面这些"点名要管"的地方走这条，语义清楚 ✓
    ///   ② `S(原文)`     —— **拿中文原文当键**。它建在"形状表"上：中文原文里的每个数字和
    ///                       每个 `{插值}` 都归一成占位符 `§`，于是
    ///                       `金币 1234`、`金币 7` 都会命中同一条 `金币 §`。
    ///                       绘制漏斗（`TownHud.Text` / `ImGuiHud.Txt` / …）只要在每个
    ///                       出口调一次 `S()`，**全工程的字就都过表了**，不用改调用点 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、回退链（缺哪条就用下一档）
    /// ══════════════════════════════════════════════════════════════════════
    ///   当前语言 → En → Zh（=原文自身） → 原样返回
    /// ⇒ `l10n_ja.json` 若某条还没翻，就落到英文/中文，**不会出现空白字** ✓
    ///   （`ur / ru / ko` 三门已删 —— 它们各只填 19 条、其余全靠回退，
    ///     用户切过去看到的是英文，比没这门语言更糟 ✗）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、切语言之后"当前 UI 立即重建"靠什么
    /// ══════════════════════════════════════════════════════════════════════
    /// IMGUI 每帧都重新跑 `OnGUI`，文字本身不需要重建；**需要重建的是字体**：
    /// `ImGuiHud` / `TownHud` / `LoginHud` / `LoginLoading` / `BossRewardPopup`
    /// 都缓存了 `Font.CreateDynamicFontFromOSFont` 的结果，而 `Microsoft YaHei`
    /// 没有日文字形的完整覆盖 ⇒ 日文要换 `Yu Gothic / Meiryo`。
    /// 所以 `Set()` 会 `FontGeneration++`，各 HUD 每帧比一位、不同就重建字体 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、必须自己知道的边界（如实写在这里，也是交付说明里那一节）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · `ur / ru / ko` **已删除**（连 `IsRtl` 一起）：这三门没有译文，留着只会让
    ///     用户以为"切了语言但没生效"。要恢复得先把 `Tools/L10nGen.py` 的 `DATA_*` 补齐。
    ///   · 由 `+` 拼起来的多段字面量（`"（上限 " + x + " 倍）"`）**不在表里**，维持原中文。
    ///   · `Resources/Data/items.json` 等数据表的 `name` / `description` 不在本表内
    ///     （那是**数据**不是界面字，要另开一份数据本地化）。
    ///     ★ 后续已按 `name.<id>` / `desc.<id>` 进表（见 `Tools/L10nGen.py` 的 `DATA_CARDS`）。
    ///     ⚠️ **长文本待翻**：`items.json` 那批 4 段式技术说明（"触发/效果/数值/限制"，每条 3~10 个 `§`）
    ///        的 `en` 目前**留空** —— 它们会按 当前→En→Zh 回退到中文。那是**待翻**，不是坏了。
    ///        要补的时候只动 `Tools/l10n_data.py` 的 `DATA_EN['desc.<id>']`，代码一行不用改 ✓
    ///   · 由 `+` 拼起来的多段字面量（`"（上限 " + x + " 倍）"`）**不在表里**，维持原中文。
    ///     但"数字 + 单位"那一类已经用**单位形状**覆盖：`§ 金` / `§ 颗` / `§ 级` / `§ 件` / `§ 秒`
    ///     （键名 `unit.gold/seeds/level/items/seconds`）⇒ `price + " 金"` 这种拼接整体能翻 ✓
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、★ 接线须知（下一个接手的人照这几条走，不用读别的文件）★
    /// ══════════════════════════════════════════════════════════════════════
    /// 这一版**只交机制、没接 UI**：`L10n` 与 6 份词表已就位，屏幕上的字仍是原中文。
    /// 接线两件事，路线 A / B 二选一或都做：
    ///
    ///   ▌路线 A（推荐：改 6~9 处、不动任何调用点）——在**绘制漏斗**上加一行
    ///     每个 HUD 的字都从下面这几个方法出去 ⇒ 在它们的第一行加 `L10n.S(...)` 即可
    ///     全工程过表（以后新加的中文文本只要进了 `l10n_zh.json` 就自动被翻）：
    ///
    ///        `Assets/Scripts/Town/TownHud.cs`       `Text()` / `TextWrap()` / `Button()`
    ///        `Assets/Scripts/UI/ImGuiHud.cs`        `Txt(Rect, string, GUIStyle)`（战斗 HUD 唯一出口）
    ///        `Assets/Scripts/Login/LoginHud.cs`     `Wrap()` / `Button()`
    ///        `Assets/Scripts/UI/BossRewardPopup.cs` `Text()`
    ///        `Assets/Scripts/Login/LoginLoading.cs` 它那一行 `GUI.Label`
    ///        `Assets/Scripts/Town/TownBuildings.cs` 世界名字标签
    ///        `Assets/Scripts/Flow/RunSession.cs`    `OnFloatingText()`（**所有飘字的唯一漏斗**）
    ///        `Assets/Scripts/Bootstrap/WorldView.cs` `MakeBossBarText()`（BOSS 血条的 3D 名字）
    ///
    ///     写法（以 `TownHud.Text` 为例，**只加这一行**）：
    ///         public static void Text(Rect vr, string text, int size, Color color, ...)
    ///         {
    ///             text = L10n.S(text);          // ← 就这一行
    ///             GUIStyle st = Style(size, anchor, bold);
    ///             ...
    ///         }
    ///     `S()` 对中文（源语言）**原样返回** ⇒ 接上之后中文表现逐像素不变；
    ///     找不到词条也原样返回 ⇒ **接线本身不会让界面变空** ✓
    ///
    ///   ▌路线 B（有点名要管的地方）：把字面量换成具名键
    ///         Text(r, "关闭 (E)", ...)          →  Text(r, L10n.T("hud.close"), ...)
    ///         Text(r, $"金币 {s.Coins}", ...)   →  Text(r, L10n.T("hud.goldFmt", s.Coins), ...)
    ///     具名键写在 `l10n_*.json` 的 `k` 里（本轮已有 37 个 `ui.*` / `hud.*` / `combat.*`）。
    ///
    ///   ▌字体（**接线的第二步，别漏**）：`Microsoft YaHei` 没有 Hangul、没有 Arabic ⇒
    ///     韩文/乌尔都文不换字体就是一片空白。每个 HUD 缓存字体的地方照这个改：
    ///         if (_font == null || _fontGen != L10n.FontGeneration)
    ///         {
    ///             _fontGen = L10n.FontGeneration;
    ///             _font = null;
    ///             foreach (string name in L10n.Fonts) { /* CreateDynamicFontFromOSFont */ }
    ///             /* 同时清掉按字号缓存的 GUIStyle —— 它们手里握着旧 Font */
    ///         }
    ///     `Set()` 会让 `FontGeneration++` ⇒ 下一帧重建 ⇒ **当前已显示的界面立刻换字**
    ///     （IMGUI 每帧重跑 `OnGUI`，文字本身不需要额外重建）✓
    ///
    ///   ▌UI 入口（**本版未做**）：设置/暂停面板里那 6 颗语言按钮还没加。
    ///     加的时候只要在按钮回调里调 `L10n.Set(Lang.Ja)`（或 `L10n.Cycle()` 循环下一门），
    ///     其余（写 `PlayerPrefs`、清缓存、抛 `OnChanged`、字体代数 +1）`Set()` 全包了。
    ///
    ///   ▌验收（一条命令）：`L10n.SelfTest()` 会**把 6 门语言各切一次**，把同一个键的
    ///     实际输出拼成一行 —— `Debug.Log(L10n.SelfTest())` 就能取到读数。
    /// </summary>
    public static class L10n
    {
        // ★ v252：词表在这一轮有改动（新增 8 道具 + 4 插件 + 4 植物的 name/desc、难度面板 13 条、
        //   以及两条"译文太长挤坏版面"的修正：ja `hud.gold` ゴールド → 金、难度面板底部说明改短）。
        //   改这里只是为了**强制一次域重载**，让运行期把新 JSON 读进来（`Values` 与 `_tablesBuilt`
        //   都是静态缓存，Play 进出不会清）✓ 这些值本身没有语义，删掉也安全。
        internal const int TableRevision = 253;

        // ── 对外常量 ────────────────────────────────────────────────────
        public const string PrefsKey = "seedfall.l10n.lang";
        /// <summary>数字占位符（U+00A7）。`l10n_*.json` 里同一串占位符与原文一一对应。</summary>
        public const char Placeholder = '\u00a7';

        /// <summary>切语言时触发。UI 组件订阅或不订阅都行（IMGUI 每帧重画，一般不需要）。</summary>
        public static event Action OnChanged;

        /// <summary>字体代数：`Set()` 加一。各 HUD 缓存字体时连这个数一起记，不同就重建。</summary>
        public static int FontGeneration { get; private set; }

        // ── 表结构（字段名必须与 JSON 逐字一致：JsonUtility 按名匹配）──
        [Serializable]
        public sealed class Row
        {
            public string k;   // 键
            public string z;   // 中文形状（只有 zh 那份填了；它是形状索引的来源）
            public string v;   // 本语言的值（空 = 缺，走回退链）
        }

        [Serializable]
        public sealed class Table
        {
            public string lang;
            public int version;
            public int count;
            public Row[] entries;
        }

        // ── 语言元数据（**三张表必须同长度、同顺序**，与 `enum Lang` 对齐）──────
        private static readonly string[] Codes = { "zh", "en", "ja" };
        private static readonly string[] NativeNames = { "中文", "English", "日本語" };
        private static readonly string[] ShortNames = { "中文", "EN", "日本語" };

        // 各语言的系统字体候选（工程里没有任何 .ttf，只能用系统动态字体）
        private static readonly string[] FontZh = { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial Unicode MS" };
        private static readonly string[] FontEn = { "Segoe UI", "Arial", "Microsoft YaHei", "Arial Unicode MS" };
        private static readonly string[] FontJa = { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Microsoft YaHei" };

        public static int LangCount { get { return Codes.Length; } }
        public static Lang At(int index)
        {
            if (index < 0) return Lang.Zh;
            if (index >= Codes.Length) return Lang.Ja;
            return (Lang)index;
        }
        public static string Native(Lang lang) { return NativeNames[Clamp((int)lang)]; }
        public static string Short(Lang lang) { return ShortNames[Clamp((int)lang)]; }
        public static string Code(Lang lang) { return Codes[Clamp((int)lang)]; }
        private static int Clamp(int i) { return i < 0 ? 0 : (i >= Codes.Length ? Codes.Length - 1 : i); }

        /// <summary>当前语言该用的系统字体候选（`CreateDynamicFontFromOSFont` 的名字数组）。</summary>
        public static string[] Fonts
        {
            get
            {
                EnsurePrefs();
                return _current == Lang.Ja ? FontJa : (_current == Lang.En ? FontEn : FontZh);
            }
        }

        // ── 状态 ────────────────────────────────────────────────────────
        private static bool _prefsLoaded;
        private static bool _tablesBuilt;
        /// <summary>`SelfTest()` 切语言时不写 `PlayerPrefs`（自检不该改玩家的设置）。</summary>
        private static bool _suppressSave;
        private static Lang _current = Lang.Zh;

        private static readonly Dictionary<Lang, Dictionary<string, string>> Values =
            new Dictionary<Lang, Dictionary<string, string>>(8);
        private static readonly Dictionary<string, string> ShapeToKey = new Dictionary<string, string>(1024);
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(2048);
        private static readonly List<string> ScratchNums = new List<string>(8);
        private static readonly System.Text.StringBuilder SbShape = new System.Text.StringBuilder(128);
        private static readonly System.Text.StringBuilder SbOut = new System.Text.StringBuilder(128);

        public static Lang Current
        {
            get { EnsurePrefs(); return _current; }
        }

        /// <summary>启动时把上次的选择读回来（"下次启动仍生效"这一步）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            EnsurePrefs();
        }

        private static void EnsurePrefs()
        {
            if (_prefsLoaded) return;
            _prefsLoaded = true;
            int saved = PlayerPrefs.GetInt(PrefsKey, -1);
            if (saved >= 0 && saved < Codes.Length)
            {
                _current = (Lang)saved;
                return;
            }
            _current = DetectSystem();
        }

        /// <summary>没存过选择时看系统语言：中文系统给中文、日文系统给日文，其余给英文。</summary>
        private static Lang DetectSystem()
        {
            string sys = Application.systemLanguage.ToString();
            if (string.IsNullOrEmpty(sys)) return Lang.Zh;
            if (sys.StartsWith("Chinese", StringComparison.Ordinal)) return Lang.Zh;
            if (sys.StartsWith("Japanese", StringComparison.Ordinal)) return Lang.Ja;
            // 韩文 / 俄文系统 ⇒ 现在没有这两门词表了，统一落到英文（不再是"半门语言"）
            return Lang.En;
        }

        // ── 切语言 ──────────────────────────────────────────────────────

        /// <summary>
        /// 切到 <paramref name="lang"/>：写 `PlayerPrefs`、清解析缓存、`FontGeneration++`、
        /// 抛 <see cref="OnChanged"/>。**同值时直接返回**（不会白重建字体）。
        /// </summary>
        public static void Set(Lang lang)
        {
            EnsurePrefs();
            if (!_suppressSave)
            {
                PlayerPrefs.SetInt(PrefsKey, (int)lang);
                PlayerPrefs.Save();
            }
            if (_current == lang)
            {
                EnsureTables();
                return;
            }
            _current = lang;
            FontGeneration++;
            Cache.Clear();
            Dictionary<string, string> ignore;
            if (!Values.TryGetValue(lang, out ignore) || ignore == null || ignore.Count == 0)
            {
                Values.Remove(lang);          // 没载过 → 让 EnsureTables 去载
                _tablesBuilt = false;
            }
            Action h = OnChanged;
            if (h != null)
            {
                try { h(); }
                catch (Exception e) { Debug.LogWarning("[L10n] OnChanged 回调抛异常（已吞，不影响切换）：" + e); }
            }
        }

        /// <summary>0→1→…→5→0，设置面板里的"下一门语言"用不到也算留个口子。</summary>
        public static Lang Next()
        {
            return At(((int)Current + 1) % Codes.Length);
        }

        /// <summary>
        /// 切到**下一门**语言并返回它。给"设置面板里的那颗按钮"用：
        /// 一颗按钮循环 6 门语言，比排 6 颗省地方；要 6 颗就直接 `Set(Lang.Ja)`。
        /// </summary>
        public static Lang Cycle()
        {
            Lang n = Next();
            Set(n);
            return n;
        }

        // ── 载表 ────────────────────────────────────────────────────────

        private static void EnsureTables()
        {
            EnsurePrefs();
            if (_tablesBuilt) return;
            _tablesBuilt = true;

            // ① zh 那份是所有形状（和"原文→键"索引）的来源，必须先载
            Table zh = Load(Lang.Zh);
            BuildShapeIndex(zh);

            // ② 回退链要用的那几份
            LoadValues(Lang.En);
            LoadValues(Lang.Zh);
            if (_current != Lang.Zh && _current != Lang.En) LoadValues(_current);
        }

        private static Table Load(Lang lang)
        {
            string path = "Data/l10n_" + Codes[Clamp((int)lang)];
            TextAsset ta = Resources.Load<TextAsset>(path);
            if (ta == null)
            {
                Debug.LogWarning("[L10n] 缺词表 Resources/" + path + ".json ⇒ 该语言全部走回退");
                return null;
            }
            try
            {
                Table t = JsonUtility.FromJson<Table>(ta.text);
                if (t == null || t.entries == null)
                {
                    Debug.LogWarning("[L10n] " + path + ".json 解析结果为空");
                    return null;
                }
                return t;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[L10n] " + path + ".json 解析失败：" + e.Message);
                return null;
            }
        }

        private static void BuildShapeIndex(Table zh)
        {
            if (zh == null || zh.entries == null) return;
            for (int i = 0; i < zh.entries.Length; i++)
            {
                Row r = zh.entries[i];
                if (r == null || string.IsNullOrEmpty(r.k)) continue;
                if (!string.IsNullOrEmpty(r.z) && !ShapeToKey.ContainsKey(r.z))
                    ShapeToKey[r.z] = r.k;
            }
        }

        private static void LoadValues(Lang lang)
        {
            if (Values.ContainsKey(lang)) return;
            Table t = Load(lang);
            var map = new Dictionary<string, string>(t != null && t.entries != null ? t.entries.Length : 16);
            if (t != null && t.entries != null)
            {
                for (int i = 0; i < t.entries.Length; i++)
                {
                    Row r = t.entries[i];
                    if (r == null || string.IsNullOrEmpty(r.k)) continue;
                    if (!string.IsNullOrEmpty(r.v)) map[r.k] = r.v;
                }
            }
            Values[lang] = map;
        }

        private static string ValueOf(Lang lang, string key)
        {
            Dictionary<string, string> map;
            if (!Values.TryGetValue(lang, out map) || map == null) return null;
            string v;
            return map.TryGetValue(key, out v) ? v : null;
        }

        // ── ① 具名键 ────────────────────────────────────────────────────

        /// <summary>`T("hud.gold")` —— 按名字取。缺当前语言 → En → Zh → 返回键名本身。</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            EnsureTables();
            string v = ValueOf(_current, key);
            if (string.IsNullOrEmpty(v)) v = ValueOf(Lang.En, key);
            if (string.IsNullOrEmpty(v)) v = ValueOf(Lang.Zh, key);
            return string.IsNullOrEmpty(v) ? key : v;
        }

        /// <summary>`T("hud.goldFmt", 12)` —— 值里用 `{0}` 占位。</summary>
        public static string T(string key, params object[] args)
        {
            string fmt = T(key);
            if (args == null || args.Length == 0) return fmt;
            try { return string.Format(fmt, args); }
            catch (FormatException) { return fmt; }
        }

        // ── ② 原文当键（绘制漏斗用这一条）────────────────────────────────

        /// <summary>
        /// 把**屏幕上真正要画的那串字**换成当前语言。
        /// 中文本身就是源语言 ⇒ `Current == Zh` 时原样返回（零开销、零风险）。
        /// 找不到就原样返回 —— **绝不会因为缺词而让界面变空**。
        /// </summary>
        public static string S(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            EnsureTables();
            if (_current == Lang.Zh) return source;

            string hit;
            if (Cache.TryGetValue(source, out hit)) return hit;

            string res = Resolve(source);
            if (Cache.Count < 4096) Cache[source] = res;
            return res;
        }

        private static string Resolve(string source)
        {
            if (source.Length > 400) return source;          // 整段 dump 之类，不在表里

            string shape = Shape(source);
            string key;
            if (!ShapeToKey.TryGetValue(shape, out key)) return source;

            string tpl = ValueOf(_current, key);
            if (string.IsNullOrEmpty(tpl)) tpl = ValueOf(Lang.En, key);
            if (string.IsNullOrEmpty(tpl)) return source;     // Zh 回退 = 原文自身

            if (ScratchNums.Count == 0) return tpl;

            // 数字个数必须对得上，否则宁可显示原文也不要把数字塞错位置
            int marks = 0;
            for (int i = 0; i < tpl.Length; i++) if (tpl[i] == Placeholder) marks++;
            if (marks != ScratchNums.Count) return source;

            SbOut.Length = 0;
            int n = 0;
            for (int i = 0; i < tpl.Length; i++)
            {
                char c = tpl[i];
                if (c == Placeholder) SbOut.Append(ScratchNums[n++]);
                else SbOut.Append(c);
            }
            return SbOut.ToString();
        }

        /// <summary>有没有数字（绝大多数纯文字标签走这条快路，不进 `StringBuilder`）。</summary>
        private static bool HasDigit(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') return true;
            }
            return false;
        }

        /// <summary>
        /// 运行时归一：每个数字（含 `1,234` / `3.5`）换成一个 `§`，并把原数字按顺序记进
        /// <see cref="ScratchNums"/>。**与 `Tools/L10nGen.py` 里生成 `z` 的归一规则必须一致**。
        /// </summary>
        private static string Shape(string text)
        {
            ScratchNums.Clear();
            if (!HasDigit(text)) return text;

            SbShape.Length = 0;
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c < '0' || c > '9') { SbShape.Append(c); i++; continue; }

                int start = i;
                while (i < n && text[i] >= '0' && text[i] <= '9') i++;
                // 小数 / 千分位 / 计时（`3.5` `1,234` `12:07`）：只有后面真的跟数字才算同一个数
                while (i + 1 < n && (text[i] == '.' || text[i] == ',' || text[i] == ':')
                       && text[i + 1] >= '0' && text[i + 1] <= '9')
                {
                    i += 2;
                    while (i < n && text[i] >= '0' && text[i] <= '9') i++;
                }
                ScratchNums.Add(text.Substring(start, i - start));
                SbShape.Append(Placeholder);
            }
            return SbShape.ToString();
        }

        // ── 交付/验收用的读数 ───────────────────────────────────────────

        /// <summary>键总数（6 份表同一组键）。</summary>
        public static int KeyCount()
        {
            EnsureTables();
            return ShapeToKey.Count;
        }

        /// <summary>某语言**真有译文**的条数（空值 = 缺，运行时走回退）。</summary>
        public static int Coverage(Lang lang)
        {
            EnsureTables();
            if (!Values.ContainsKey(lang) && lang != _current) LoadValues(lang);
            Dictionary<string, string> map;
            return Values.TryGetValue(lang, out map) && map != null ? map.Count : 0;
        }

        /// <summary>某语言缺哪些键（按字母序，最多 <paramref name="max"/> 条）。</summary>
        public static string[] MissingKeys(Lang lang, int max)
        {
            EnsureTables();
            if (!Values.ContainsKey(lang)) LoadValues(lang);
            Dictionary<string, string> map;
            Values.TryGetValue(lang, out map);
            var list = new List<string>();
            foreach (var kv in ShapeToKey)
            {
                string v;
                if (map == null || !map.TryGetValue(kv.Value, out v) || string.IsNullOrEmpty(v))
                {
                    list.Add(kv.Value);
                    if (max > 0 && list.Count >= max) break;
                }
            }
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        /// <summary>
        /// 一次性把六份表的覆盖情况打成一行日志（自动化验收读它）。
        /// </summary>
        public static string Report()
        {
            EnsureTables();
            var sb = new System.Text.StringBuilder();
            sb.Append("[L10n] keys=").Append(KeyCount()).Append(" current=").Append(Codes[Clamp((int)_current)]);
            for (int i = 0; i < Codes.Length; i++)
            {
                sb.Append(" | ").Append(Codes[i]).Append('=').Append(Coverage((Lang)i)).Append('/').Append(KeyCount());
            }
            return sb.ToString();
        }

        /// <summary>
        /// ★ 验收读数生成器 ★ —— **把 6 门语言各切一次**，打印同一个键 / 同一句话的实际输出，
        /// 最后切回原来的语言。交付报告里那张"同一 key 在 2 种语言下的输出"表就是它打出来的：
        ///
        ///     Debug.Log(L10n.SelfTest());
        ///
        /// 三条探针各有分工：
        ///   · `T(hud.gold)`  —— 具名键，看**人工译文**（`ja` 有 19 条人工，所以有值）
        ///   · `S(守园失败)`  —— 原文当键、无数字，看**形状表命中**
        ///   · `S(第 3 分钟　游玩 1:23)` —— 带数字，看**占位符回填**（数字必须原样出现在译文里）
        /// </summary>
        public static string SelfTest()
        {
            EnsureTables();
            Lang old = _current;
            _suppressSave = true;             // ★ 自检不许改玩家的设置
            var sb = new System.Text.StringBuilder(1024);
            try
            {
                const string keyProbe = "hud.gold";
                const string srcProbe = "守园失败";
                const string numProbe = "第 3 分钟\u3000游玩 1:23";
                sb.Append("[L10n.SelfTest] keys=").Append(KeyCount());
                for (int i = 0; i < Codes.Length; i++)
                {
                    Set((Lang)i);
                    sb.Append(" | ").Append(Codes[i])
                      .Append(" T(hud.gold)=").Append(T(keyProbe))
                      .Append(" S(守园失败)=").Append(S(srcProbe))
                      .Append(" S(第3分钟)= ").Append(S(numProbe))
                      .Append(" cov=").Append(Coverage((Lang)i));
                }
            }
            finally
            {
                Set(old);
                _suppressSave = false;
            }
            return sb.ToString();
        }
    }
}
