using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// v69：浮动文字的类型。
    ///
    /// 为什么要有这个枚举：旧版是**每个发射点各自写颜色和字号**——
    /// 同一个"掉血"在玩家和敌人身上是两处硬编码，改一次要翻五个文件，而且已经开始不一致了。
    /// 现在发射点只声明"这是什么事件"，颜色 / 字号 / 要不要加感叹号
    /// 全部由 <see cref="PopupStyle"/> 一处决定。改规则只改那一处。
    /// </summary>
    public enum PopupKind
    {
        /// <summary>敌人受击掉血 → 白色</summary>
        DamageToEnemy,
        /// <summary>玩家受击掉血 → 红色</summary>
        DamageToPlayer,
        /// <summary>
        /// ★ v186：玩家**护盾**被扣 → 蓝色 ★（需求⑤原话："扣血 = 红色 ✗ 扣护盾 = 蓝色"）
        ///
        /// 为什么要与 `DamageToPlayer` 分成两个枚举值，而不是"同一行换个颜色"：
        /// 一次受击常常**同时**有盾和血（盾先扣、扣完才掉血）⇒ 玩家需要一眼看出
        /// "这 3 点是打在盾上还是打在身上"——前者可恢复、后者不可 ✓
        /// ⇒ 两行字、两种颜色，各自显示各自的分量（分量来自 `DamageResult.ShieldDealt/HpDealt`）✓
        /// </summary>
        DamageToPlayerShield,
        /// <summary>回血 → 绿色</summary>
        Heal,
        /// <summary>植物成熟 → 黄色</summary>
        PlantMature,
        /// <summary>植物缺水 → 红色放大 + 感叹号</summary>
        PlantThirst,
        /// <summary>收获 → 绿色</summary>
        Harvest,
        /// <summary>v164：收割获得金币 → **黄色**</summary>
        GoldGain,
        /// <summary>v164：收割获得经验 → **绿色**</summary>
        ExpGain,
        /// <summary>暴击 → 黄色放大 + 感叹号</summary>
        Crit,
        /// <summary>升级 → 金色</summary>
        LevelUp,
        /// <summary>肥料爆炸拟声词 → 红色粗体大字</summary>
        Bomb,
        /// <summary>其他信息</summary>
        Info,
        // ══════════════════════════════════════════════════════════════════
        // ★ v188：三种**持续伤害**的弹字（用户需求：各自带专属图标前缀）★
        //
        // 为什么必须是**三个新枚举值**、而不是"沿用 `DamageToEnemy` 再加个前缀字符串"：
        //   ① 颜色要跟着种类走（火苗橙 / 水滴蓝 / 骷髅绿）——
        //      而颜色**只由 `PopupStyle.ColorOf` 一处决定**（本文件 `:67`），
        //      没有种类就没有这一档颜色 ✗
        //   ② 图标颜色 = 弹字颜色（用户原话"图标颜色跟各自弹字色"）⇒ 颜色与图标
        //      必须来自**同一个 kind**，一个来源、不是两份表 ✓
        //   ③ 追加在枚举**末尾**：既有 13 个值的编号一个不动，
        //      任何按 `(int)Kind` 存过的东西都不会错位 ✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>灼烧持续伤害 → 橙（前缀：小火苗）</summary>
        DamageBurn,
        /// <summary>减速持续伤害 → 蓝（前缀：水滴）</summary>
        DamageSlow,
        /// <summary>毒区持续伤害 → 绿（前缀：骷髅头）</summary>
        DamagePoison,
        // ══════════════════════════════════════════════════════════════════
        // ★ v197：**第一种「文字型」弹字** —— 玩家闪避成功 → 灰色「闪避」★
        //
        // 用户原话："闪避成功为玩家加上虚化特效，并触发灰色弹字『闪避』"。
        //
        // ★ 为什么它必须是一个新枚举值，而不是"借 `DamageToPlayer` 换个文案"★
        //   ① 颜色要**灰**：而那 8 个既有档位的配色各有语义（红=我在掉血 / 白=敌人在掉血 /
        //      绿=回血 / 黄=成熟与暴击）—— 灰是这一档**独有**的语义"这一下没打中我" ✓
        //   ② 它**不带数字**：既有 16 档全部由 `Actor.Popup`（吃一个 float）发出 ⇒
        //      数字是它们的信息本体 ✗ 而闪避没有数可报，蓝字 `-0` 正是用户明令禁止的那种串 ✓
        //      ⇒ 所以它走另一条出口 `Actor.PopupText`（见那里的注释），
        //        而"哪几档是文字型"只在 `PopupStyle.IsTextKind` **一处**声明 ✓
        //   ③ 追加在枚举**末尾**：既有 16 个值的编号一个不动（与 v188 那条同一个理由）✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>玩家闪避成功 → 灰（文字型：显示「闪避」，不带数字）</summary>
        Evade,
        // ══════════════════════════════════════════════════════════════════
        // ★ v282：**富豪（`tycoon_coin` / `gold_damage`）的额外伤害** ★
        //
        // 用户原话："弹字表现为**敌人身上冒出一个金币样式图案加伤害，金色**"。
        //
        // ★ 为什么必须是新的枚举值，而不是沿用 `GoldGain` ✗
        //   ① 语义相反：`GoldGain` 是"我**进账**了钱"（正数、带 `+`、绿黄），
        //      这一条是"我把钱换成了**伤害**"（金色、只有数、没有 `+`）✓
        //   ② 颜色要单独一档 `BonusGold (1.00,0.85,0.30)`：用户点名"金色"，
        //      而 `GoldGain` 用的 `Yellow (1.00,0.88,0.22)` 偏柠檬，
        //      压在草地上读起来像"收获了钱"而不是"这一下打得更重" ✓
        //   ③ 图标要「硬币」：`IconOf` 是**按 kind 分派**的，没有 kind 就没有图标 ✓
        //   ④ 追加在枚举**末尾**：既有 18 个值的编号一个不动（与 v188/v197 同一条纪律）✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>富豪：金币转成的额外伤害 → 金（硬币图标 + 纯数字，无 `+`）</summary>
        GoldBonus,
    }

    /// <summary>
    /// ★ v188：弹字**前缀图标**的三种形状 ★
    ///
    /// ★ 为什么是"三种形状的枚举"，而不是往文本里塞 emoji ★
    ///   `🔥`(U+1F525) / `💧`(U+1F4A7) / `💀`(U+1F480) 属于 **emoji 平面**，
    ///   而本工程的 HUD 字体是 `Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16)`
    ///   （`ImGuiHud.cs:492`）——**雅黑没有 emoji 字形**，
    ///   IMGUI 的动态字体也**不认彩色 emoji 字形表**（COLR/CBDT）⇒ 画出来是豆腐块 ✗
    ///   ⇒ 所以图标走**贴图**：形状在这里声明，`ImGuiHud` 一次性生成小纹理并缓存 ✓
    ///   （与 emoji 无关、与系统字体是否装了 Segoe UI Emoji 无关、与打包平台无关）
    /// </summary>
    public enum PopupIcon
    {
        /// <summary>无前缀（绝大多数弹字）</summary>
        None = 0,
        /// <summary>小火苗 —— 灼烧</summary>
        Flame = 1,
        /// <summary>水滴 —— 减速</summary>
        Drop = 2,
        /// <summary>骷髅头 —— 中毒</summary>
        Skull = 3,
        // ══════════════════════════════════════════════════════════════════
        // ★ v282：**插件弹字换成图标**（用户原话"剑气……用一个剑加伤害的形式"、
        //           "以及回响等，全部加入图标"）★
        //
        // ★ 为什么这些图标挂在 `PopupIcon` 上，而 `PopupStyle.IconOf` 仍只按 kind 分派 ★
        //   插件伤害的**弹字档位是同一个**（敌人掉血），"是哪个插件打的"由
        //   `RunSession.FloatingText.Plugin`（`PluginPopupStyle`）携带 ⇒
        //   绘制侧先问 `PluginHitFx.IconOf(plugin)`，问不到再回落到 `IconOf(kind)`。
        //   两套来源**共用这一张枚举表与同一个纹理缓存** ⇒ 不存在"两套图标系统" ✓
        // ★ 编号从 4 起、每次追加在末尾：既有 3 个值不动（与 v188 同一条纪律）✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>硬币 —— 富豪的金币额外伤害（圆环 + 中间竖线）</summary>
        Coin = 4,
        /// <summary>箭头 —— 弹射（原来那一个字面后缀 `↗`）</summary>
        Arrow = 5,
        /// <summary>靶心 —— 收束（原来那一个字面后缀 `◎`）</summary>
        Target = 6,
        /// <summary>剑 —— 剑气（原来那一个字面后缀 `/`）</summary>
        Sword = 7,
        /// <summary>回声波纹 —— 回响</summary>
        Echo = 8,
        /// <summary>闪电 —— 伏雷（原来那一个字面后缀 `↯`）</summary>
        Bolt = 9,
        /// <summary>星火 —— 灼印</summary>
        Spark = 10,
        /// <summary>心形 —— 吸血（原来那一个字面后缀 `♥`）</summary>
        Heart = 11,
        // ══════════════════════════════════════════════════════════════════
        // ★ v290：本批三张。建图方式与上面 8 张**逐字相同**：
        //   `ImGuiHud.MaskToTexture` 吃 11×11 的 `string[]`、`PopupIconTex` 按枚举分派 ⇒
        //   没有第二套图标系统，也不依赖字体里有没有那个字符 ✓
        //   （用 Unicode 字符会包含“雅黑没有字形 ⇒ 豆腐块”这个已知坑，见本枚举头那段）✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>分叉闪电 —— 感电（`hoe_shock`）。与伏雷那张 `Bolt`（单道折线）故意不同形</summary>
        ShockArc = 12,
        /// <summary>污水溅射 —— 臭水沟（`can_sewer`）</summary>
        Sewage = 13,
        /// <summary>三道水纹 —— 海浪（`can_wave`）</summary>
        WaveRing = 14,
    }

    /// <summary>
    /// v69：浮动文字的统一样式表。
    /// 规则（来自需求）：玩家受击红、敌人受击白、回血绿、成熟黄、
    /// 缺水红放大加叹号、暴击黄放大加叹号。
    /// </summary>
    public static class PopupStyle
    {
        private static readonly Color Red = new Color(1.00f, 0.24f, 0.22f);
        private static readonly Color White = new Color(1.00f, 0.98f, 0.96f);
        private static readonly Color Green = new Color(0.42f, 1.00f, 0.48f);
        private static readonly Color Yellow = new Color(1.00f, 0.88f, 0.22f);
        private static readonly Color Gold = new Color(1.00f, 0.92f, 0.45f);
        /// <summary>v186：护盾被扣的蓝（需求⑤）—— 与既有五种同表，不散写 RGB ✓</summary>
        private static readonly Color Blue = new Color(0.36f, 0.68f, 1.00f);
        // ══════════════════════════════════════════════════════════════════
        // ★ v188：三种持续伤害的弹字色（**只在这里定义一次**）★
        //
        // 取色的依据不是"好看"，是**和场上那三种表现对上**：
        //   `DamageBurn`   → 与 `PluginFx.For(锄头, 灼烧)` 的炭火橙同族（`1.00,0.30,0.10` 那一档）
        //                    ⇒ 玩家在身上冒火星的敌人身上看到的红，与弹字的橙是同一族 ✓
        //   `DamageSlow`   → 与场上"蓝色向下箭头"同一档蓝（`ArrowSlow` 直接用这个常量）✓
        //   `DamagePoison` → 与地面毒区的毒绿同族（`PoisonCore/PoisonRim`）✓
        // ★ 为什么 `BurnOrange` 不复用 `Red`（玩家受击色）★
        //   红是"我在掉血"，橙是"敌人在烧" —— 两种语义共用一个色会让玩家误读方向 ✗
        // ══════════════════════════════════════════════════════════════════
        /// <summary>灼烧弹字的橙（火苗）</summary>
        private static readonly Color BurnOrange = new Color(1.00f, 0.62f, 0.16f);
        /// <summary>减速弹字的蓝（水滴）</summary>
        private static readonly Color SlowBlue = new Color(0.42f, 0.78f, 1.00f);
        /// <summary>中毒弹字的绿（骷髅头）</summary>
        private static readonly Color PoisonGreen = new Color(0.46f, 1.00f, 0.42f);
        /// <summary>
        /// ★ v197：闪避弹字的**灰**（用户原话"灰色弹字『闪避』"）★
        ///
        /// 取 0.74 这一档浅灰而不是中灰(0.5)：弹字压在**草地/泥地上**画，中灰在白天几乎读不出来
        /// （与 `White` 的对比度只剩 1.4:1）✗ 0.74 的亮度比地面高、又明确低于白字
        /// ⇒ 既不抢"敌人在掉血"的白，也不会糊进背景 ✓
        /// 三个通道**故意不相等**（R/B 略高、G 略低）：纯等值灰在暖色草甸上会偏绿 ✗
        /// </summary>
        private static readonly Color EvadeGray = new Color(0.78f, 0.76f, 0.74f);
        /// <summary>
        /// ★ v282：**富豪「金币转伤害」那一档的金**（用户原话"金色"）★
        ///
        /// 为什么不复用 `Yellow`（收获金币用的 1.00,0.88,0.22）✗
        ///   `Yellow` 偏柠檬，它是"进账"的颜色；这一条是"钱打在人身上"，
        ///   要用更厚、更铜的金 ⇒ 红分量满、绿压到 0.85、蓝抬到 0.30（亮度仍 &gt; 0.62 兜底线）✓
        /// ⚠️ 不能写 `new Color(1,0.84,0)`：纯金黄在草地上会有明显色带，且与 `Gold`（升级金）撞色 ✓
        /// </summary>
        private static readonly Color BonusGold = new Color(1.00f, 0.85f, 0.30f);

        public static Color ColorOf(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageToPlayer: return Red;
                // v186：护盾 -N 走蓝（需求⑤），颜色只在这里定义一次
                case PopupKind.DamageToPlayerShield: return Blue;
                case PopupKind.DamageToEnemy: return White;
                case PopupKind.Heal: return Green;
                case PopupKind.Harvest: return Green;
                // v164：需求"金币+ 黄色 / 经验值+ 绿色" ✓
                // 黄色沿用既有的 `Yellow`（与"成熟"同色）✗ 不新建一种黄 ✓
                case PopupKind.GoldGain: return Yellow;
                case PopupKind.ExpGain: return Green;
                case PopupKind.PlantMature: return Yellow;
                case PopupKind.PlantThirst: return Red;
                case PopupKind.Crit: return Yellow;
                case PopupKind.LevelUp: return Gold;
                case PopupKind.Bomb: return Red;
                // v188：三种持续伤害各自的色（图标也从同一个 kind 取色 ⇒ 不会分家）
                case PopupKind.DamageBurn: return BurnOrange;
                case PopupKind.DamageSlow: return SlowBlue;
                case PopupKind.DamagePoison: return PoisonGreen;
                // v197：唯一的灰 —— 闪避（"这一下没打中我"）
                case PopupKind.Evade: return EvadeGray;
                // v282：富豪的金币额外伤害 —— 用户点名的"金色"
                case PopupKind.GoldBonus: return BonusGold;
                default: return White;
            }
        }

        /// <summary>
        /// ★ v188：某一档弹字要带哪个图标前缀 ★
        ///
        /// 唯一的分派点 —— `ImGuiHud.DrawPopups` 只问这一个函数，
        /// 不在绘制代码里写 `if (kind == XXX)`。加第四种伤害时只改这里 ✓
        /// </summary>
        public static PopupIcon IconOf(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageBurn: return PopupIcon.Flame;
                case PopupKind.DamageSlow: return PopupIcon.Drop;
                case PopupKind.DamagePoison: return PopupIcon.Skull;
                // v282：富豪的金币额外伤害 → 硬币图标（用户："金币样式图案加伤害"）
                case PopupKind.GoldBonus: return PopupIcon.Coin;
                default: return PopupIcon.None;
            }
        }

        /// <summary>
        /// ★ v188：`DotKind`（谁在持续伤害）→ `PopupKind`（弹字该画成哪一档）★
        ///
        /// 放在 `PopupStyle` 里的理由：这张映射是**样式**的一部分（颜色/图标都挂在结果上），
        /// 而不是持续效果表的职责 —— 表只管结算，不该知道"骷髅头"这种表现细节 ✓
        /// </summary>
        public static PopupKind DamageKindFor(DotKind dot)
        {
            switch (dot)
            {
                case DotKind.Burn: return PopupKind.DamageBurn;
                case DotKind.Slow: return PopupKind.DamageSlow;
                case DotKind.PoisonZone: return PopupKind.DamagePoison;
                default: return PopupKind.DamageToEnemy;
            }
        }

        /// <summary>
        /// ★ v191 补丁：玩家**红/蓝两行的固定上下错位**（单位：米，世界 XZ 平面）★
        ///
        /// ── 为什么需要它（实测抓图看到的问题）✗ ──
        ///   需求⑤把玩家受击拆成"扣血红 / 扣护盾蓝"两行，但两行的锚点是**同一个坐标**
        ///   （`Actor.Pos`），而唯一的错位来自 `RunSession.OnFloatingText` 那个
        ///   **半径 0.6m 的随机散布**（v48，目的只是"别完全重合"）✗
        ///   ⇒ 散布是随机的 ⇒ **两行经常落在几乎同一点**，蓝字盖在红字上，
        ///     一张 `盾 2 + 血 2` 的受击在图里看着像 **`-22`** ✗
        ///   （证据：`Docs/captures/zoom-BEFORE-two-lines.png` /
        ///     `zoom-AFTER-two-lines.png` —— 改前两张都叠着）
        ///
        /// ── 为什么是"固定 ±0.38m 垂直"而不是"加大散布半径" ✗ ──
        ///   ① 加大半径只会**降低**重合概率，不能**保证**不重合（随机的东西保证不了）✓
        ///   ② 0.38 × 2 = **0.76m** 的间距在俯视相机下约 **43px**，
        ///      而弹字本体高约 30px ⇒ 两行**永远**分得开 ✓
        ///   ③ 上下分而不是左右分：本作镜头是俯视"，上下 = 世界 Z 方向，
        ///      与"字往上升"的动画同轴，读起来像"两行字往上飘" ——
        ///      左右分（像收割的金币/经验）会和血条 HUD 的方向撞 ✓
        ///   ④ 红在上、蓝在下：红是"不可恢复的损失"，先被读到 ✓
        ///
        /// ⚠️ 这个错位是**表现层规则**，不是结算法则：`DamageResult` 的分量语义一个字没动 ✓
        /// </summary>
        public static Vector2 StackOffsetOf(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.DamageToPlayer: return new Vector2(0f, 0.38f);
                case PopupKind.DamageToPlayerShield: return new Vector2(0f, -0.38f);
                // ★ v197：闪避的「闪避」两个字要**抬离角色身体**再往上一档 ★
                //   为什么需要它（用户要求"两次连出会叠 ⇒ 套用现有的 StackOffsetOf"）：
                //   ① 闪避成功的锚点就是 `Actor.Pos`（= 角色中心），而角色身上同时还有
                //      虚化残影（`DrawEvadeGhost`）⇒ 灰字压在残影正中会读成"残影上有个斑点" ✗
                //   ② 抬到 +0.62m：比玩家受击的红字（+0.38）更高一档 ⇒ 同一时刻若真有一击打中，
                //      红字与「闪避」各有各的高度，不会互相盖住 ✓
                //   ⚠️ 同一种 kind 连出两次**不能靠固定错位分开**（两枚字拿到的是同一个偏移）。
                //      那件事由 `RunSession.OnFloatingText` 里的 `VisualRng` 随机散布（半径
                //      `ui.popupRadius`）负责 —— 那是全工程"同一目标连发不叠"的既有机制 ✓
                //      本行只负责"把闪避抬到它该在的高度"✓
                case PopupKind.Evade: return new Vector2(0f, 0.62f);
                default: return Vector2.zero;
            }
        }

        public static float ScaleOf(PopupKind k)
        {
            switch (k)
            {
                // ★ 本轮（目标像素口径）：1.75 → **1.00** ⇒ 与普通弹字同号 20px（用户："缺水 20"）
                case PopupKind.PlantThirst: return 1.00f;   // 缺水与普通同号：靠橙色/感叹号区分，不再靠字号
                // ★ 上一轮：1.90 → 1.80 ⇒ 36px（"普通暴击维持"）
                // ★ 本轮（用户给的是**目标像素**）：1.80 → **1.25** ⇒ 19.994 × 1.25 = 24.99 ⇒ **25px**
                //   （用户原话：「暴击 25」）
                //   ⚠️ 1.25 同时是"伏雷/印/收束"三段相乘的第一段（见 `PluginHitFx.ScaleOf`）——
                //     那三档本轮要 20px，它们的插件倍率已按 `1.25 × 发布侧 × 本表 = 1.0003` 反解过 ✓
                //   ★ 本轮（用户裁决了上一轮报上去的三处翻转：「字体按照我的要求改」）★
                //     判据 = 用户给的那三个数**就是权威**，整个层级服从它，不允许别的档反超：
                //       bomb 30（唯一最大）> 暴击 25（唯一第二）> 其余一切 ≤ 20（= 普通伤害）
                //     ⇒ 「升级 / 成熟 / 收获 / 金币额外 / 闪避」全部降到 1.00（= 20px），
                //       「大伤害」的 1.35 ⇒ 1.00（见 `Actor.Popup` 那一处）、
                //       「天气」（借 `PlantMature` + 发布侧 1.2）也一并落回 20px（见 `ProgMechanics`）✓
                //     ⚠️ 本档 1.25 **不动**：它是伏雷/印/收束三段相乘的第一段，
                //       那三档已按 `1.25 × 发布侧 × 插件表 = 1.0003` 反解过 ⇒ 动本档会把它们一起推离 20px ✗
                case PopupKind.Crit: return 1.25f;
                // ★ v3xx：2.75 → **2.20**（用户原话「收束、印、伏雷、bomb 这几个的弹字大小缩减，
                //   但大于其它弹字」）★
                //   bomb 是**唯一**只靠本表一档就定死的弹字（没有插件倍率叠在上面）
                //   ⇒ 它缩小 → 直接乘 0.80；改后仍是全场最大的一档（`PluginHitFx.ScaleOf` 里
                //   伏雷 1.50 / 印 1.50 / 收束 1.50 都排在它（2.05）下面）✓
                //   ⚠️ 2.20 与旧值 2.75 的比值 = 0.80，与 `PopupFontScale` 的"整体 ×0.85"是两个数：
                //     前者是**这一档的相对关系**，后者是**全体基准** ⇒ 两者相乘才是上屏字号 ✓
                // ★ 上一轮（目标像素口径）：2.20 → 2.05 ⇒ 41px（"bomb 维持"）
                // ★ 本轮（目标像素口径）：2.05 → **1.50** ⇒ 19.994 × 1.50 = 29.99 ⇒ **30px**
                //   （用户原话：「bomb 30」）；bomb 是**唯一最大**的一档：
                //   下一档是「暴击」25px，再往下**全部 ≤ 20px**（本轮把层级拉平后的完整表见
                //   `PluginHitFx.ScaleOf` 的头注释）✓
                case PopupKind.Bomb: return 1.50f;   // 2.05→1.50 ⇒ 19.994 × 1.50 = 29.99 ⇒ 30px（唯一最大）
                // ★ 本轮（拉平）：1.30 → **1.00** ⇒ 20px（用户："除 bomb/暴击 外一律 ≤ 20"）
                //   改前 26px **反超**暴击 25px —— 那是上一轮报上去的三处翻转之一，本轮按用户裁决抹平 ✓
                //   这一档的文字（「Lv 3」）靠金色区分，不再靠字号 ✓
                case PopupKind.LevelUp: return 1.00f;
                // ★ 本轮（拉平）：1.25 → **1.00** ⇒ 20px（改前 25px = 与暴击**同号**，撞第二档）✓
                //   ⚠️ 天气弹字（`ProgMechanics` 的「大晴天！/下雨了！」）借的就是本档，
                //     它发布侧还乘 1.2 ⇒ 改前 = 1.25 × 1.2 = 1.50 ⇒ **30px**（与 bomb 并列最大）；
                //     本轮把发布侧那 1.2 也收回 1.00 ⇒ 1.00 × 1.00 = 20px ✓
                case PopupKind.PlantMature: return 1.00f;
                // ★ 本轮（拉平）：1.15 → **1.00** ⇒ 20px
                //   ⚠️ 这一档其实**不上屏**（`RunSession.OnFloatingText` 对 `Harvest` 提前返回，
                //     用户 v181："删除'收获'弹字"）⇒ 本值只为"表内口径一致"与探针/调试用 ✓
                case PopupKind.Harvest: return 1.00f;
                // v164：收益弹字要**比植物名更显眼** ✗
                // 玩家收割时最想知道的是"我赚了多少" ✓ 而不是"我收了哪一株" ✓
                // 所以收益比植物名大一号：1.35 vs 1.15 ✓（★ 本轮已改为 1.00，见下一行）
                // ★ 本轮（目标像素口径）：1.35 → **1.00** ⇒ 20px（用户："金币、经验 20"）
                //   收益不再比植物名大一号：金币/经验与普通弹字同号，靠金色/硬币图标区分 ✓
                case PopupKind.GoldGain: return 1.00f;
                case PopupKind.ExpGain: return 1.00f;
                // v188：持续伤害每秒都会跳一次，弹字**要比普通受击小一号** ✗
                // 理由：一秒一跳 × 3 层灼烧 = 屏幕上持续有字；用普通字号会盖住敌人本体，
                // 而它的信息量（"这条持续效果还在生效"）本来就比一次出手低 ✓
                // 0.88 是"看得见、但不抢"，配合图标前缀仍能一眼读出「是哪种」✓
                // ★ 本轮（目标像素口径）：0.88 → **0.85** ⇒ 19.994 × 0.85 = 16.99 ⇒ **17px**（用户："持续伤害 17"）
                //   仍比普通弹字小一号（17 < 20）：一秒一跳的字不该抢镜 ✓
                case PopupKind.DamageBurn:
                case PopupKind.DamageSlow:
                case PopupKind.DamagePoison: return 0.85f;
                // v197：闪避比普通受击略大一号（1.10）—— 它一秒里最多出现一两次，
                //   而玩家最该读到的正是"这一下我躲掉了"（挨打与躲开的反馈必须分得开）✓
                //   但**不放大到暴击那一档**：闪避不是"我打得更狠" ✓
                // ★ 本轮（拉平）：1.10 → **1.00** ⇒ 20px（改前 22px > 20，必须收进 ≤20 那一档）✓
                //   与普通弹字同号不影响辨识：这一档是**灰色 + 纯文字「闪避」**（无数字），
                //   与红的 `-4` / 白的 `-4.55` 在颜色和文案上就分得开 ✓
                //   ⚠️ `ProgMechanics` 的「格挡」也走本档（发布侧 0.9）⇒ 1.00 × 0.9 = 0.90 ⇒ **18px** ✓
                case PopupKind.Evade: return 1.00f;
                // v282：金币额外伤害要比普通受击大一号 —— 它是**独立的第二条数**，
                //   与那一刀的总伤害字并排出现，同号会读成"打了两下" ✗
                //   1.15 = v197 `PopupGoldBonus` 当年传的 `Scale`（口径不变，只搬家）✓
                // ★ 本轮（拉平）：1.15 → **1.00** ⇒ 20px（改前 23px > 20）✓
                //   它仍然与那一刀的总伤害分得开：金色（`BonusGold`）+ 硬币图标，两个通道都独立于字号 ✓
                case PopupKind.GoldBonus: return 1.00f;
                default: return 1.00f;
            }
        }

        /// <summary>这些类型要求以感叹号收尾（需求：缺水 / 暴击 / 爆炸拟声）。</summary>
        public static bool WantsBang(PopupKind k)
        {
            return k == PopupKind.PlantThirst || k == PopupKind.Crit;
        }

        /// <summary>把"类型 + 裸文本"整理成最终要画的字符串。</summary>
        public static string TextOf(PopupKind k, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (!WantsBang(k)) return raw;
            return raw.EndsWith("！") || raw.EndsWith("!") ? raw : raw + "！";
        }

        /// <summary>
        /// ★ v197：**这一档弹字是不是"文字型"（文案自带语义，不带数字）** ★
        ///
        /// ── 为什么需要这条判据，而不是"发的时候小心点"✗ ──
        ///   在它之前，全工程的弹字**只有一条出口** `Actor.Popup(float amount, …)`：
        ///   它把 float 印成 `-4` / `-4.55`，**数字就是信息本体** ✓
        ///   闪避没有数可报 ⇒ 若从那条出口走，最省事的写法是 `Popup(0, Evade)`
        ///   ⇒ 屏幕上会出现 **`-0`** —— 那正是用户明令禁止的一串（见 `PopupMinAmount` 的③）✗
        ///   ⇒ 所以文字型弹字走**另一条出口** `Actor.PopupText`，而"哪几档属于文字型"
        ///     在这里**声明一次**：新增文字型弹字只改这一处，绘制侧不必知道 ✓
        ///   ⚠️ 它不改变任何绘制行为（颜色/字号/图标仍由上面三个函数分派）——
        ///      它存在的意义是让"这条通道是文字"这件事**可被自检断言**，而不是只写在注释里 ✓
        /// </summary>
        public static bool IsTextKind(PopupKind k)
        {
            switch (k)
            {
                case PopupKind.Evade: return true;
                default: return false;
            }
        }
    }

    /// <summary>v47：物体旁边的浮动文字（掉血 / 回血 / 成熟 / 缺水 / 收获）。
    /// 逻辑层只管发，表现层（ImGuiHud）把它投影到屏幕上画出来。
    /// v69：颜色与字号不再由发射点决定，改为只传 <see cref="Kind"/>。</summary>
    public struct FloatingTextEvent
    {
        public Vector2 Pos;
        public string Text;
        public PopupKind Kind;
        /// <summary>额外的字号倍率（在 PopupStyle 的基础上再乘）。0 表示用类型默认值。</summary>
        public float Scale;
    }

    /// <summary>所有角色的基类。位置用 (x, z) 平面表达为 Vector2。</summary>
    public abstract class Actor
    {
        /// <summary>
        /// 受击半径的显示补偿系数（v75）。
        ///
        /// 模型为了在俯视相机下看得清被放大了（WorldView.CharScale 1.75 → 2.60），
        /// 但碰撞半径是玩法数据、没跟着动——于是出现"看着打到了、其实没打到"。
        /// 这里按显示放大的**相对倍率**给受击半径补上：2.60 / 1.75 ≈ 1.486。
        ///
        /// ★ 这是玩法数值改动，不是纯表现 ★
        /// 判定框变大 = 玩家更容易打中敌人，**敌人也更容易打中玩家**，双向的。
        /// 要回到原手感就把这个值调回 1。
        /// </summary>
        public const float HitRadiusScale = 1.486f;

        /// <summary>按显示倍率补偿后的受击半径。</summary>
        public static float ScaledRadius(float baseRadius) => baseRadius * HitRadiusScale;

        public int Id;
        public Vector2 Pos;
        public Vector2 KnockbackVel;
        public float Radius = 0.45f;
        public Team Team;
        public readonly HealthPool Health = new HealthPool();

        protected Actor()
        {
            // v47：让生命池知道自己是哪个角色——治疗时才能在他旁边弹出 "+N"
            Health.Owner = this;
            // ★★ v196：血量纲由**池的归属**决定（用户原话"玩家伤害不再转换为整数，允许小数"）★★
            //   玩家池 ⇒ 只收整数血点（敌人→玩家那一侧，与改前逐位相同）
            //   敌人池 ⇒ 小数原样落账（玩家→敌人：武器 / 剑气流 / 灼烧溅射 / 毒火破冰）
            // `this is PlayerActor` 在基类构造里是**类型判断**，不是虚调用 ⇒ 安全 ✓
            // 唯一写者就是这一行（理由见 `HealthPool.IntegerOnly` 的注释）✓
            Health.IntegerOnly = this is PlayerActor;
        }

        private bool _alive = true;

        /// <summary>
        /// 存活 = 未被显式回收 且 生命 &gt; 0。
        /// 由生命值派生，避免"HP 归零但 Alive 仍为 true"导致的尸体积压。
        /// </summary>
        public bool Alive
        {
            get => _alive && Health.Hp > 0f;
            set => _alive = value;
        }

        public abstract void Tick(float dt, World w);

        /// <summary>死亡判定：所有索敌、伤害、结算都必须用它。</summary>
        public bool IsDead => !Alive;

        /// <summary>受击无敌帧剩余时间（玩家与植物帮手使用；敌人不用）。</summary>
        public float InvulnRemaining;
        /// <summary>受击闪白剩余时间（表现层读取）。</summary>
        public float HitFlash;

        /// <summary>是否享受受击无敌帧。敌人不享受（否则异常跳伤会被吃掉）。</summary>
        public virtual bool UsesInvulnFrames => false;
        public bool IsInvulnerable => InvulnRemaining > 0f;

        /// <summary>受击成功后的统一处理：进无敌帧 + 触发闪白 + 弹出掉血数字。
        /// v87：crit 为真时弹**暴击样式**（黄色 / 放大 / 带叹号），而不是普通白字。
        /// ★ v186：入参改成**一整个 `DamageResult`**（用户需求③："三处同步，以参数形式传"）★
        ///   弹字用到的数**只有一个来源**：
        ///     · 敌人 —— `r.RawValue`（v187 起；= 这一击算出来的伤害 = 武器卡上那个数）
        ///     · 玩家 —— `r.HpDealt` 红 / `r.ShieldDealt` 蓝（需求⑤）
        ///
        /// ★ v187：**敌人那条从 `Dealt` 改成 `RawValue`**（用户报"暴击后伤害无变化"＋"弹字与计算不符"）★
        ///   病灶（实测）：`Dealt` 是**真正扣掉的量** = `min(剩余血, RawValue)` ✗
        ///     · 基础兵只有 2.8 血（`enemies.json` `grunt.baseHp`），玩家一下 4 点、暴击 8 点
        ///       ⇒ 两种情况的 `Dealt` **都是 2.8** ⇒ 弹字都是 `-3`
        ///       ⇒ 暴击只剩颜色变了，**数字一点没变**（"暴击后伤害无变化"）✗
        ///     · 而武器卡算的是 4 / 8 ⇒ 弹字与卡面从此永远对不上（"弹字与计算不符"）✗
        ///   现在弹字 = `RawValue` = 武器卡用的那条式子 ⇒ 卡面 / `RawValue` / 弹字三方同一个数
        ///   （自检 `ProbeDamageTrace` 与探针 `ProbeBlow` 都钉住这一条）✓
        ///   ⚠️ `Dealt` 字段**一个字都没改**：它仍然表示"真正扣掉的血盾量"，
        ///      `DamagedByEnemies` / 击杀判定 / 自检都还在读它 ⇒ 只是**弹字不再用它** ✓
        ///
        /// ★ 玩家那两条**故意不改**（本轮做的判断，理由三条）★
        ///   ① 需求⑤ 给的语义是"这一下**扣了我的盾多少 / 血多少**"——两条是**分量**，
        ///      必须 `红 + 蓝 == 这一击实际损失`。把 `RawValue` 塞进血行会让两条之和对不上总量
        ///      （致死那一击最刺眼：20 点打脸、血只剩 10 ⇒ 弹 20 就是"多出来的 10 去哪了"）✗
        ///   ② `Dealt == ShieldDealt + HpDealt` 是 `Combat.cs` 里写明的不变量 +
        ///      自检在验的性质 ⇒ 在界面上破坏它 = 让弹字与结算互相矛盾 ✗
        ///   ③ 玩家开局 10 血、基础兵 0.7 点/次 ⇒ 截断**只在致死那一击**发生，
        ///      而那一击之后就是结算界面 ⇒ 换成 `RawValue` 的收益 ≈ 0 ✓
        ///   敌人那侧没有这个问题：它只有**一行**、没有"分量相加"，
        ///   所以既该、也能显示"这一击算出来多少" ✓
        /// </summary>
        public virtual void OnHitLanded(in DamageResult r, World w)
        {
            HitFlash = 0.12f;
            if (UsesInvulnFrames)
            {
                float inv = w.Tuning.player.invulnTime;
                if (inv > 0f) InvulnRemaining = Mathf.Max(InvulnRemaining, inv);
            }
            // 需求⑤：玩家受击 **拆两行** —— 扣血红 / 扣护盾蓝（分量来自结算结果，不是再算一遍）
            // ★ v187：**保持分量语义**（理由见上方三条）；这里一旦换成 `RawValue`，"红+蓝"就不再等于损失 ✗
            if (Team == Team.Player)
            {
                Popup(r.HpDealt, PopupKind.DamageToPlayer);
                Popup(r.ShieldDealt, PopupKind.DamageToPlayerShield);
                return;
            }
            // 敌人 / 其他：一行，暴击走 `Crit` 样式（v69/v87 的既有规则不变）
            // ★ v187：取 `RawValue`（这一击算出来的伤害），不是被血条截断的 `Dealt` ★
            //
            // ★ v188：持续伤害走**三种专属档**（带图标前缀）★
            //   `DotPopupHint` 由 `PluginDotTable.Hit` 在**同一个调用栈**里立好并清掉
            //   （为什么不能用 `DamageRequest.Type` 分辨，见 `DotPopupHint` 的注释）✓
            //   优先级：持续伤害 > 暴击 > 普通受击。
            //   为什么持续伤害压过暴击：DoT 一律 `IgnoreDodge` 且**不掷暴击**
            //   （`DamageRequest.Crit` 默认 false），两者不会同时成立 ✓
            //   但把 DoT 放在前面写是有意的：万一将来给 DoT 开了暴击，
            //   玩家更该看到的是"这一下是毒"，而不是"这一下暴击了" ✓
            PopupKind kind = PopupKind.DamageToEnemy;
            if (DotPopupHint.Kind.HasValue) kind = PopupStyle.DamageKindFor(DotPopupHint.Kind.Value);
            else if (r.Crit) kind = PopupKind.Crit;
            Popup(r.RawValue, kind, r.Crit);
        }

        /// <summary>
        /// ★ v191 补丁：弹字**门槛**（用户原话："重新补上角色受击弹字"）★
        ///
        /// ── 病灶（上一轮查到、这一轮实测确认）✗ ──
        ///   旧门槛是 `if (amount &lt; 1f) return;`，而本作里**单次伤害几乎全都小于 1**：
        ///     · `enemies.json` 的接触伤害：基础兵 `touchDamage 0.7`／远程兵 `0.28`／
        ///       精英 `0.56`／BOSS `0.84`，再乘伤害成长曲线（第 1 分钟系数 0.65）
        ///       ⇒ 第 1 分钟实测每次 **0.455**（`TouchDamageForWave(1)`）✗
        ///     · 远程兵的弹丸 `projectileDamage 0.4667`、精英的砸地 `jumpDamage 0.9333` ✗
        ///   ⇒ 玩家被小兵蹭、被弹丸打，**红蓝两行一条都不出**（血条在掉，屏幕上什么都没写）✗
        ///   实测（`ProbePopups` 抓图 ⑦，真会话真小兵）：玩家 10 → 8.82（掉了 1.185），**上屏 0 条** ✗
        ///
        /// ── 为什么取 **0.05**（不是 0.1、不是 0）──
        ///   ① 必须 **&lt; 0.18**：第 1 分钟远程兵接触伤害 = 0.28 × 0.65 ≈ 0.18，
        ///      这是全表里**最小的一次真实伤害** ⇒ 门槛比它低，才不会漏掉任何一次真的挨打 ✓
        ///   ② 必须 **&gt; float 噪声**：`Popup` 也被**持续伤害**调用（每秒一跳），
        ///      而 `Dots.Tick` 按 1/60 子步累计，残量能到 1e-6 量级 ⇒ 门槛为 0 会弹一堆 `-0` ✗
        ///   ③ ★ 0.05 让 **`-0` 在数学上不可能出现** ★：
        ///      能过门槛的数 ≥ 0.05，而 `Mathf.Max(1, RoundToInt(x))` 对任何 x ≥ 0.05 都 ≥ 1
        ///      ⇒ 印出来最短是 `-1`，永远不会出现 `-0`（用户判据③明令禁止的那一串）✓
        ///      （v194 起**所有弹字都印整数**，这条下限就是「不可能印出 0」的机器保证 ✓）
        ///      ★ v196 补 ★ 小数档（`0.##`）下这条门槛**同样是**「不可能印出 0」的保证：
        ///      `0.##` 把 |x| &lt; 0.005 印成 `0` / `-0`，而门槛 0.05 比 0.005 大一个数量级
        ///      ⇒ 印出来最短是 `-0.05` ✓ 两个数是一对，谁把门槛降到 0.005 以下这条立刻失效 ✗
        ///   ④ 语义上 0.05 = "1/14 次小兵接触"：低于它的不是"挨打"，是数值残留 ✓
        ///
        /// ⚠️ 这是**所有弹字的公共入口**（敌人受击 / 玩家扣血 / 玩家扣盾 / 三种持续伤害都走它）
        ///    ⇒ 门槛一降，**敌人侧的 0.4 点伤害也会开始弹** —— 那是对的（口径一致）✓
        ///    本轮据此复核了所有"数弹字条数"的断言，见报告里的期望值改动清单 ✓
        /// </summary>
        public const float PopupMinAmount = 0.05f;

        /// <summary>弹一条掉血字。颜色/字号一律由 `PopupStyle` 决定。
        ///
        /// ★ v187：**小数不再被四舍五入掉** ★
        ///   旧写法 `Text = "-" + Mathf.RoundToInt(amount)`：
        ///     肥料 `(5+0)×100%×250% = 12.5` 会弹成 **`-12`**
        ///     （`Mathf.RoundToInt(12.5f)` 是**银行家舍入** ⇒ 12，不是 13）✗
        ///   而武器卡印的是 `12.5`（`{shown:0.#}`）⇒ 弹字与卡面差一格 ✓
        ///
        /// ★ v194：**格式收敛为整数**（用户原话"弹字自动取整数"）★
        ///   判据：整数、不带小数点（`-4` / `-46` / `-600`）；且 `Mathf.Max(1, …)` 保证
        ///   永不出现 `-0` ✓ 具体理由与代价写在下面 `Popup` 的实现注释里 ✓
        ///   ⚠️ 武器卡（`ImGuiHud` 的伤害行）仍然是 `0.#` —— 那是"这把武器的伤害"的设计读数，
        ///     与"这一下实际扣了多少"分属两处，不强行统一 ✓
        ///
        /// ★★ v196：**分两档**（用户原话"玩家伤害不再转换为整数，允许小数"）★★
        ///   · **玩家打敌人** ⇒ `0.##`（`-4.55` / `-18.75` / `-800`）—— 与血池里真的扣掉的小数同口径
        ///   · **敌人打玩家** ⇒ 仍是整数（`-4` / `-46`）—— 因为玩家血池本来就只收整数血点
        ///     （`HealthPool.IntegerOnly`），弹出来的数必须与"真的掉了多少"同口径 ✓
        ///   判据与血池那一侧**同一条**：由**被打的是谁**决定，不由呼叫点决定 ✓
        /// </summary>
        private void Popup(float amount, PopupKind kind, bool crit = false)
        {
            if (amount < PopupMinAmount) return;
            // ════════════════════════════════════════════════════════════════════
            // ★★ v196：**两档格式**（用户原话"玩家伤害不再转换为整数，允许小数"）★★
            //
            // ── 分档判据 ──
            // `Team == Team.Player`（= 这一击打在玩家身上）⇒ 整数档；否则 ⇒ 小数档。
            // 与 `OnHitLanded` 拆"扣血红 / 扣护盾蓝"用的是**同一个字段**、同一处判断 ✓
            // 为什么不用"传进来的 amount 是不是整数"来判断：那是**数据驱动**的判据 ——
            // 一个整数伤害（比如正好 4.0 的 DoT）会被判成整数档，而它其实是小数侧的数 ✗
            // 判据必须是**来源**（谁在挨打），不是数值长相 ✓
            //
            // ── 整数档（敌人 → 玩家）── 行为与 v194/v195 **逐位相同** ✓
            // `Mathf.Max(1, RoundToInt(x))`：能过门槛的数 ≥ 0.05 ⇒ 印出来最短是 `-1` ⇒ `-0` 不可能 ✓
            //
            // ── 小数档（玩家 → 敌人）── 用 `0.##`
            // `0.##` = 最多两位小数、**不补零**（4.55 → `4.55`、4.5 → `4.5`、4.0 → `4`）✓
            //
            // ── 为什么小数档也**不会**印出 `-0` ──
            // `0.##` 会把任意 x 四舍五入到两位小数；能印出 `0` / `-0` 的只有 |x| < 0.005 ✗
            // 而入口门槛是 `PopupMinAmount = 0.05`，比 0.005 **大一个数量级**
            // ⇒ 任何进得来的数都 ≥ 0.05 ⇒ 印出来最短是 `-0.05` ⇒ **`-0` 在数学上不可能出现** ✓
            // （门槛与"两位小数"是**一对**：谁把门槛降到 0.005 以下，这条保证立刻失效 ✗
            //   所以门槛的注释里写着"必须 > float 噪声" —— 那一句现在也管着这条 ✓）
            //
            // ⚠️ CultureInfo.InvariantCulture：不跟着系统区域变 ✓（小数点是 `.` 不是 `,`）
            // ════════════════════════════════════════════════════════════════════
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            float shown;
            string text;
            if (Team == Team.Player)
            {
                int whole = Mathf.Max(1, Mathf.RoundToInt(amount));
                shown = whole;
                text = "-" + whole.ToString(inv);
            }
            else
            {
                shown = amount;
                text = "-" + amount.ToString("0.##", inv);
            }
            Bus.Publish(new FloatingTextEvent
            {
                Pos = Pos,
                Text = text,
                Kind = kind,
                // 大伤害额外放大，保留数值反馈的层次（暴击已经在 PopupStyle 里放大了，不再叠）
                // ★ v194：门槛 8 → **80** ★ —— 它是"这一下算大伤害"的视觉判据，必须跟着
                //   伤害刻度一起 ×10；不改的话新刻度下**每一次**普攻都会放大（0.455 → 8 时代
                //   只有肥料那种 12.5 会放大）⇒ 视觉层次会被抹平 ✗
                // ★ v196：`shown` 改成 float（小数档要留住 4.55 而不再截成 4）⇒ 比较仍用 80f ✓
                // ★★ 本轮（拉平字号）：1.35 → **1.00** ⇒ 20px（用户："除 bomb/暴击 外一律 ≤ 20"）★★
                //   ── 为什么"大伤害"这档必须收掉（改前它是 27px，**反超**暴击 25px）──
                //   ① 上屏乘积里它是**独立的一段**：`26 × 0.769 × PopupStyle.ScaleOf(kind) × 本值`
                //      ⇒ 本值 1.35 让**每一条**非暴击大伤害都到 1.35 ⇒ 27px，越过 25px 那条线 ✗
                //   ② 更糟的是它还会与插件表**相乘**：改前 1.35 × 剑气 0.95 = 1.2825 ⇒ **26px**、
                //      1.35 × 污水 1.05 = 1.4175 ⇒ **28px** ⇒ 凡是"大伤害 + 插件"的弹字都顶到
                //      bomb/暴击 之上 ✗（改后 1.00 × 各自 ≤1.00 ⇒ 全部落回 ≤ 20px ✓）
                //   ── 代价与为什么可接受 ──
                //   收成 1.00 之后"大伤害"与普通伤害**同号 20px** ⇒ 这一档的字号不再有层次。
                //   但 `scale` 只能是 ≤ 1.0003（否则就 > 20px），而 1.0003 与 1.00 在像素上同值
                //   ⇒ "比普通更大"这件事在本轮约束下**无解**，字号这一通道只能放弃 ✓
                //   80 这个门槛**保留不动**：它仍然决定走不走这一支（将来若用户允许 > 20px，
                //   改回一个 > 1.00 的数即可，判据不必重写）✓
                Scale = (crit || shown < 80f) ? 0f : 1.00f
            });
        }

        protected void TickCommonTimers(float dt)
        {
            InvulnRemaining = Mathf.Max(0f, InvulnRemaining - dt);
            HitFlash = Mathf.Max(0f, HitFlash - dt);
        }

        /// <summary>
        /// ★ v197：**文字型弹字的唯一出口**（与 `Popup` 并列的第二条通道）★
        ///
        /// 与 `Popup` 的分工：
        ///   · `Popup(float …)`  —— **数字**弹字：菜单里印 `-4` / `-4.55`，门的门槛是 `PopupMinAmount`
        ///   · `PopupText(string)` —— **文字**弹字：文案自带语义（「闪避」），**没有数**
        ///
        /// ★ 为什么不开成"给 `Popup` 加个 string 重载" ✗
        ///   `Popup` 里的**每一个分支**（两档格式、门槛、按 `shown` 决定是否放大）都以"有个数"为前提；
        ///   加一个 string 重载等于在它里面写一排 `if (是文字型) 跳过上面全部` ——
        ///   那条路一走，门槛与 `-0` 的机器保证（`PopupMinAmount` 的③）就有了一个**旁门** ✓
        ///   两条**互斥**的出口比一条出口里塞两个模式更难写错 ✓
        ///
        /// ⚠️ 它仍然走**同一条** `Bus.Publish(FloatingTextEvent)` ⇒ 上屏漏斗
        ///   （`RunSession.OnFloatingText`）、颜色/字号分派（`PopupStyle`）、绘制（`ImGuiHud.DrawPopups`）
        ///   三处都与数字弹字**共用**，不存在"第二套弹字系统" ✓
        /// </summary>
        public void PopupText(string text, PopupKind kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            Bus.Publish(new FloatingTextEvent
            {
                Pos = Pos,
                Text = text,
                Kind = kind,
                Scale = 0f      // 0 = 用 `PopupStyle` 里该档的默认字号（1.10）
            });
        }

        public virtual void OnDamaged(float amount, Actor source, World w) { }
        public virtual void OnDeath(World w) { }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v282：`PopupGoldBonus(float)` **已删除**（用户报「金币+出现在玩家身上」）★★
        //
        // ── 它当年做错了什么（这就是那个 bug 的**唯一**来源）──
        //   `Pos = Pos` —— 它是 `Actor` 的实例方法，而唯一调用者是
        //   `RunSession.WithGoldDamage`，传进来的是 `PlayerActor p` ⇒
        //   **额外伤害弹字永远挂在玩家脚下**，而不是被击中的敌人身上 ✗
        //   文案还是 `"金+" + …`、`Kind = PopupKind.GoldGain` ⇒ 玩家读到的是
        //   「金+12」＝"我赚了 12 块"，而实际发生的是"敌人多挨了 12 点" ✗
        //
        // ── 为什么删掉、而不是把 `Pos` 改成一个参数 ✗ ──
        //   它内部走 `Bus.Publish`，而新的发布点就在同一个漏斗
        //   `RunSession.OnFloatingText` **里面**（那条栈是 Bus 在派发中途）⇒
        //   从处理器里再 `Publish` 一次等于**递归进同一个处理器** X
        //   ⇒ 新做法是"漏斗里直接 `Popups.Add` 一条 `FloatingText`"
        //     （见 `RunSession.EmitGoldBonusPopup`：不走总线、不递归、零新增订阅）✓
        //   ⚠️ 口径没有丢：金色 `PopupStyle.ColorOf(GoldBonus)`、硬币图标
        //     `PopupStyle.IconOf(GoldBonus)`、字号 1.15 全部搬到了那一档枚举上 ✓
        // ══════════════════════════════════════════════════════════════════════
    }

    public sealed class PlayerActor : Actor
    {
        public const int WeaponHoe = 0;
        public const int WeaponCan = 1;
        public const int WeaponFertilizer = 2;

        public PlayerProgress Progress;
        public Vector2 MoveInput;
        public Vector2 AimDir = Vector2.up;

        /// <summary>三把武器各自独立冷却（切换不重置已有冷却）。</summary>
        public readonly float[] WeaponCds = new float[3];
        /// <summary>换武器前摇：&gt;0 时无法攻击。</summary>
        public float SwitchWindup;
        public int WeaponIndex;
        /// <summary>G 键锁定的目标 id（-1 = 未锁定，此时按鼠标方向攻击）。</summary>
        public int LockTargetId = -1;
        /// <summary>距上次受伤的时间（护盾回复判定用）。</summary>
        public float SinceDamaged = 999f;

        public override bool UsesInvulnFrames => true;

        /// <summary>受击结算：进入无敌帧 + 闪白 + 重置护盾回复计时。</summary>
        public override void OnHitLanded(in DamageResult r, World w)
        {
            base.OnHitLanded(r, w);
            SinceDamaged = 0f;
        }

        public float MoveSpeed => Progress.Stats.Get(StatId.MoveSpeed);
        public float AttackSpeed => Progress.Stats.Get(StatId.AtkSpeed);
        public float RangeMult => Progress.Stats.Get(StatId.AtkRange);
        public bool IsLocked => LockTargetId >= 0;
        public bool CanAttack => SwitchWindup <= 0f && WeaponCds[WeaponIndex] <= 0f;

        // ══════════════════════════════════════════════════════════════════════
        // ★ v197：**闪避虚化**（用户原话"闪避成功为玩家加上虚化特效，并触发灰色弹字『闪避』"）★
        //
        // ── 它是什么（读法写在字段上，免得下一个人以为是"无敌帧"）──
        //   一个**纯表现**的倒计时：闪避判成的那一刻置满，之后线性衰减到 0。
        //   **不参与任何结算** —— 它既不加无敌帧、也不改闪避率（`Combat.Resolve` 里
        //   在 `Chance(dodge)` 成功时调一次 `OnEvaded()`，返回的仍是一个"打空"结果）✓
        //
        // ── 为什么虚化必须"由表现层读一个时间戳"，而不是这里直接改材质 ✗ ──
        //   ① `Actors.cs` 是**逻辑层**：它连 `Renderer` 都不该碰（本文件一个 UnityEngine 渲染 API 都没用）✓
        //   ② 玩家模型（`ModelView` / `WorldView`）**不在本轮的改动范围**里，
        //      而"逐实例改 alpha"又恰恰是**不该做**的那条路（会与描边/受击闪白抢同一个材质）✗
        //   ⇒ 逻辑层只报"我在虚化、还剩多久"，绘制由 `ImGuiHud.DrawEvadeGhost` 用 `GUI.color`
        //     在**屏幕层**画一层半透明残影 ⇒ 不动模型材质、不动别人正在改的 `WorldView` ✓
        //   ⚠️ 代价（如实记）：它是**叠加**在模型上的一层半透明轮廓，不是"模型本身变透明" ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>虚化剩余时间（秒）。0 = 不在虚化。**表现层只读**，逻辑层不读它做任何判断。</summary>
        public float EvadeGhost;
        /// <summary>一次虚化持续多久（秒）。</summary>
        public const float EvadeGhostSeconds = 0.40f;

        /// <summary>
        /// 闪避判成时调一次：**点亮虚化** + 弹灰色「闪避」。
        ///
        /// 两件事绑在同一个方法里，因为它们由**同一个事件**（"这一下没打中我"）触发 ——
        /// 分开写就一定会有一天只改了其中一件 ✓
        /// </summary>
        public void OnEvaded()
        {
            EvadeGhost = EvadeGhostSeconds;
            PopupText("闪避", PopupKind.Evade);
        }

        /// <summary>
        /// 滚轮切换武器：delta &gt; 0 向后滚（下一个），&lt; 0 向前滚（上一个）。
        /// 这是唯一的切换方式（HUD 右侧面板只是可视化）。
        /// </summary>
        public void CycleWeapon(int delta, World w)
        {
            if (delta == 0) return;
            int count = 3;
            int next = ((WeaponIndex + delta) % count + count) % count;
            SwitchWeapon(next, w);
        }

        public string WeaponName
        {
            get
            {
                switch (WeaponIndex)
                {
                    case WeaponCan: return "水壶";
                    case WeaponFertilizer: return "肥料";
                    default: return "锄头";
                }
            }
        }

        /// <summary>换武器：切换瞬间进入前摇（前摇期间无法攻击）。</summary>
        public void SwitchWeapon(int index, World w, bool force = false)
        {
            int clamped = Mathf.Clamp(index, 0, 2);
            if (!force && clamped == WeaponIndex) return;
            WeaponIndex = clamped;
            SwitchWindup = Mathf.Max(0f, w.Tuning.tools.switchWindup);
        }

        public override void Tick(float dt, World w)
        {
            StatBlock st = Progress.Stats;

            // 属性变化同步到生命池（升级 / 买道具都会走这里）
            float targetMaxHp = st.Get(StatId.MaxHp);
            float targetMaxShield = st.Get(StatId.Shield);
            if (!Mathf.Approximately(Health.MaxHp, targetMaxHp))
            {
                float delta = targetMaxHp - Health.MaxHp;
                Health.MaxHp = targetMaxHp;
                // ★★ v306 纯插入守卫：训练场里"最大生命 +N"**不再顺带补血**（用户原话："不要出现回血"）★★
                //
                //  ── 这一句为什么也算"回血" ──
                //   上面的 `targetMaxHp` 来自 `StatBlock`（升级 / 买道具都会改它）。
                //   改前：最大生命涨 20 ⇒ `Hp` 当场也涨 20 ⇒ 训练场里**装一件 +生命道具就回一次血** ✗
                //
                //  ── 守卫为 false（= 正常战斗）时，行为**逐字节不变** ──
                //    判据从 `delta > 0f` 变成 `delta > 0f && !TrainingMode.NoHeal`；
                //    `TrainingMode.NoHeal` 的初值是 false，唯一的写者是 `TrainingMode.Enable()`
                //    （只被训练场景的 `TrainingBootstrap.Awake` 调用）⇒ 战斗模式下
                //    `!false == true` ⇒ 判据化简回 `delta > 0f` ⇒ 与改前**完全同一个分支** ✓
                //
                //  ── 守卫为 true（训练场）时 ──
                //    `delta > 0f` 走 `else` 分支 = `Hp = Mathf.Min(Hp, MaxHp)`；
                //    而 `MaxHp` 刚刚被抬高 ⇒ `Hp` **一个点都不涨**（正是"不回血"）✓
                //    掉血侧（`delta < 0f`）本来就走 `else`，一行都没变 ✓
                if (delta > 0f && !TrainingMode.NoHeal) Health.Hp = Mathf.Min(Health.MaxHp, Health.Hp + delta);
                else Health.Hp = Mathf.Min(Health.Hp, Health.MaxHp);
            }
            if (!Mathf.Approximately(Health.MaxShield, targetMaxShield))
            {
                float shieldDelta = targetMaxShield - Health.MaxShield;
                Health.MaxShield = targetMaxShield;
                if (shieldDelta > 0f) Health.Shield = Mathf.Min(Health.MaxShield, Health.Shield + shieldDelta);
                else Health.Shield = Mathf.Min(Health.Shield, Health.MaxShield);
            }

            // v15：恢复"生命回复"——按 regen 随时间自动回血（数值由属性/道具/抽卡控制）
            float regen = 0f;
            if (regen > 0f) Health.Heal(regen * dt);

            // 护盾回复：未受伤时每秒回 max(最大护盾×百分比, 下限)
            PlayerBaseDef cfg = w.Tuning.player;
            SinceDamaged += dt;
            if (Health.MaxShield > 0f && SinceDamaged >= cfg.shieldRegenDelay && Health.Shield < Health.MaxShield)
            {
                float rate = Mathf.Max(Health.MaxShield * cfg.shieldRegenPct, cfg.shieldRegenMin);
                Health.Shield = Mathf.Min(Health.MaxShield, Health.Shield + rate * dt);
            }

            if (MoveInput.sqrMagnitude > 0.0001f)
            {
                Vector2 dir = MoveInput.normalized;
                // ★ v290：**海浪**（`can_wave`）的移速加成走这一格。
                //   为什么是"乘在这里"而不是去改 `StatId.MoveSpeed`：
                //   属性块是全局面板（升级 / 道具 / 商店都在读写），"临时 +50% 1 秒"
                //   塞进去就必须有人负责**还原** —— 而漏还原是静默的（玩家永久变快）✗
                //   这里读的是一个**只读的即时倍率**（`MoveSpeedScale`）：计时器归零它就自己回 1 ✓
                //   ★ 确定性：它只是一个 float 乘数，不消费随机数、不读写世界状态 ⇒ 钉种子的回放不变 ✓
                //   没装海浪时该属性恒为 1 ⇒ `MoveSpeed × 1` 与改前**逐位相同** ✓
                Pos += dir * (MoveSpeed * PluginExtra2.MoveSpeedScale) * dt;
            }
            if (KnockbackVel.sqrMagnitude > 0.0001f)
            {
                Pos += KnockbackVel * dt;
                KnockbackVel = Vector2.Lerp(KnockbackVel, Vector2.zero, 1f - Mathf.Exp(-9f * dt));
            }
            Pos = World.ClampToArena(Pos, Radius, w.Tuning.farm.arenaHalf);

            InvulnRemaining = Mathf.Max(0f, InvulnRemaining - dt);
            TickCommonTimers(dt);
            // v197：闪避虚化的倒计时（纯表现，见 `EvadeGhost` 的注释）
            EvadeGhost = Mathf.Max(0f, EvadeGhost - dt);
            SwitchWindup = Mathf.Max(0f, SwitchWindup - dt);
            for (int i = 0; i < WeaponCds.Length; i++) WeaponCds[i] = Mathf.Max(0f, WeaponCds[i] - dt);
        }
    }

    // ★ T2（2026-10）：`PlantAlly` 类**已整条删除** ✗ ★
    //
    // 它是"植物参战"这条子树的**类型根**。删它的判据：
    //   · 生产者：`SpawnPlantFromPlot`（已删）⇒ 工程里**再也没有 `new PlantAlly`** ✓
    //   · 推进者：`PlantAlly.Tick` 的调用点已在 v173 随"植物 tick 闸门"删除
    //     （见 `World.Simulate` 第 3 段）⇒ 它的 `override Actor.Tick` 早已无人调用 ✓
    //     ⚠️ 这一点很要紧：因为是 `override`，**编译器不会报"未使用"** ⇒ 只能靠审计发现 ✓
    //   · 状态根：`World.Plants`（已删）✓
    //   · 全部读者：敌人 3 处（跳跃场 / BOSS 冲锋 / `TickEnemyShot`）、`World.NearestAlly`、
    //     `World.FindPlantOnPlot`、`World.Cleanup` 的尸体清理、`WorldView.SyncPlants`、
    //     `DebugApi` 转储、`RunSession`（遥测 + 释放地块 + 铲除幼苗）、`SelfCheck` 若干断言
    //     ⇒ **全部已在下线范围内处理完毕** ✓
    //
    // ⚠️ 随类一起消失的成员（它们**不需要**逐个删 —— 类型没了，成员自然没了）：
    //     `Tick`（override）· `DamageMultiplier` · `Fire` · `ApplyAilmentHit` · `AilmentOf` ·
    //     `Heal` · `OnDeath`（含 `volatile` 分支）· `SetGrown` · `FreeFromPlot` · `IsGuard` /
    //     `IsHealer` / `IsAttacker` / `Label` · 以及 `AttackValue` / `FullHp` / `DetectM` 等战斗属性 ✓
    //
    // ★ 关于 `OnDeath` 的 `volatile` 分支（上一轮审计要求"停下并报告"的那一条）★
    //   它在**删之前就已经不可达**，所以删它不丢任何东西 ✓
    //   证据：`OnDeath` 的判据是 `MutationId == "volatile"` ✓ 而 `MutationId` 的
    //   **唯一写者**是 `SpawnPlantFromPlot`，它开头第一句就是 `mutationId = null;`（硬置空）✓
    //   ⇒ 全工程 `PlantAlly.MutationId` **恒为 null** ⇒ 该分支恒假 ✓
    //   ⇒ 它对应的 `PlantDiedEvent`（→ `RunSession` 的 `plant_death` 遥测）也是**从来没发过**的 ✓
    //
    // ★ 新玩法里"植物"没有消失 ★ —— 它换了个载体：
    //     `FarmPlot`（长在地块上）+ `PlantMechanics`（成长/收获/缺水三阶段）+
    //     `Farm.PlantCount`（存活植株 = 占着的列数）✓
    //   消失的只是"会移动、会索敌、会被打、会死亡的植物**实体**" ✓


        // ★ T2：以下是随 `PlantAlly` 一起删除的成员 —— `DamageMultiplier` / `Fire` /
        //   `ApplyAilmentHit` / `AilmentOf` / `Heal` / `OnDeath` ✓
        //   保留这段说明是为了交代两件事：
        //   ① `AilmentOf` 的处置正是上一轮写在它自己的文档注释里的计划
        //      （"它随'植物参战'整条下线时一起删"）—— 这次执行了 ✓
        //   ② `AilmentKind` **枚举本身保留**（插件线的灼烧直接用枚举成员，
        //      与植物无关）⇒ 只有"字符串 → 枚举"的映射随植物一起消失 ✓

        // ★ T2：`Heal(...)` 与 `OnDeath(...)` 随 `PlantAlly` 一起删除（说明见上方类级注释）✗
        //   补充两条"为什么它们不值得单独留"：
        //   · `Heal` —— 它治疗的对象是"别的植物 / 玩家"，而"别的植物"已不存在 ✓
        //     玩家回血走 `Pickup`（爱心）与 `Health.Heal`，与本方法无关 ✓
        //   · `OnDeath` —— 它的 `volatile` 分支恒假（`MutationId` 恒 null，见类级注释）✓
        //     它的末尾 `Bus.Publish(PlantDiedEvent)` 是那个死事件的唯一发布点 ✓


    public sealed class Projectile
    {
        public Vector2 Pos, Vel;
        public float Damage;
        /// <summary>
        /// v114：这发子弹**出膛时是否暴击**。
        /// 暴击是"每次攻击"决定的，而子弹飞出去之后才结算伤害 ——
        /// 所以标记必须跟着子弹走，不能在命中时再掷（那样同一发子弹每次结算结果都不同）。
        /// </summary>
        public bool Crit;
        public float AilmentValue;
        public AilmentKind Ailment;
        public float Radius = 0.25f;
        public float Life = 3f;
        public float TravelLimit = 9f;
        /// <summary>v23：飞行结束（到末端）时爆炸（肥料炸弹）。</summary>
        /// <summary>
        /// v114 核查结论：**这个字段全工程从未被赋值**，所以下面的 `Explode(w)` 是**当前不可达**的。
        ///
        /// 我原本打算给它加一个"来源武器"字段，好让弹道爆炸也能读逐武器的基础爆炸范围——
        /// 查证之后发现那个问题**不存在**：没有子弹会爆炸，就没有来源要传。
        /// 差点给一段死代码加了一整套管线。
        ///
        /// 游戏里**真实的爆炸只剩一个来源**（v172 之后）：
        ///   ① 肥料炸弹  —— RunSession.ExplodeAt（按落点直接炸，不是飞行物）
        ///   ② ~~炭花死亡 —— PlantAlly 的 DeathExplode 机制~~ ★ v172 已删除 ✗ ★
        ///      （它随"植物不参战"失去前提 ⇒ `World.DealDamage` 里那条分支已整段移除 ✓）
        /// 仍然吃到 `baseExplodeRange` 的是 ①  ✓
        ///
        /// 保留这个字段而不是删掉：它看起来是为"溅射类子弹"预留的设计位。
        /// 真要启用时，需要同时补上「谁给它赋值」与「来源武器的基础爆炸范围」两件事。
        /// </summary>
        public bool ExplodeOnEnd;
        public float Traveled;
        public Team OwnerTeam;
        public Actor Source;
        // ★ T2：`FromPlant` 字段**已删除** ✗ ★
        //   写者：`PlantShot`（已删）+ `PlantAlly.OnDeath`（随类删除）✓
        //   读者：`TickPlayerShot` 里把它抄进 `DamageRequest.FromPlant`（也随之删除 ✓）
        //   ⇒ 零写者、零读者 ⇒ 它不是"保留的设计位"，而是旧玩法的一条残线 ✓
        public int Pierce;
        public int Bounce;
        public string Special;
        public float Knockback;
        public bool Dead;
        public Color Tint = Color.white;

        // ★ T2：`Projectile.PlantShot(...)` 工厂**已删除** ✗ ★
        //   它是"植物开火"的唯一弹道构造口 ✓ 调用者只有 `PlantAlly.Fire()` 两处（随类删除）✓
        //   ⇒ 删掉它之后，`OwnerTeam = Team.Player` 的子弹**只剩玩家武器那一条来源** ✓
        //   ⚠️ 玩家的弹道不走这里：走 `RunSession` 的武器开火路径（`new Projectile { ... }` 直接构造）✓
        //   ⚠️ **查证过的连带影响**（我原来以为"武器还在用这三个字段"，grep 后不成立 ✗）：
        //      `PlantShot` 是 `Pierce` / `Bounce` / `Special` 的**唯一写者** ✓
        //      （查证方式：搜 `new Projectile` → 全工程只剩下面 `EnemyShot` 一处；
        //        搜 `Pierce =` / `Bounce =` / `Special =` → 全工程 0 处）✓
        //      ⇒ 删掉它之后这三个字段变成**静态无写者**（此前是"写者不可达"——弱一档）✓
        //      ⇒ 它们的读者是 `TickPlayerShot` 里三条**恒假**的 `if`
        //        （`Special == "Splash"` / `Bounce > 0` / `Pierce > 0`）✓
        //      ★ 本轮**保留**这三个字段与那三条分支，只在此记录 ★ —— 它们属于
        //        "玩家弹道"的地界（`PlayerActor` 的武器在 v162 之后走近战/激光/爆炸，
        //        已经不再构造 Player 阵营的 `Projectile`）⇒ 是**独立于植物**的另一笔死代码 ✗

        public static Projectile EnemyShot(Actor source, Vector2 from, Vector2 dir, float damage, float speed, Color tint)
        {
            return new Projectile
            {
                Pos = from,
                Vel = dir.normalized * speed,
                Damage = damage,
                Radius = 0.3f,
                OwnerTeam = Team.Enemy,
                Source = source,
                Tint = tint,
                TravelLimit = 22f,
                Life = 4f
            };
        }

        public static Color GradeColor(SeedGrade g)
        {
            switch (g)
            {
                case SeedGrade.D: return new Color(0.75f, 0.78f, 0.80f);
                case SeedGrade.C: return new Color(0.45f, 0.85f, 0.45f);
                case SeedGrade.B: return new Color(0.35f, 0.65f, 1.00f);
                case SeedGrade.A: return new Color(0.85f, 0.55f, 1.00f);
                default: return new Color(1.00f, 0.82f, 0.30f);
            }
        }

        public void Tick(float dt, World w)
        {
            Vector2 step = Vel * dt;
            Pos += step;
            Traveled += step.magnitude;
            Life -= dt;
            if (Life <= 0f || Traveled > TravelLimit)
            {
                if (ExplodeOnEnd) Explode(w);
                Dead = true;
                return;
            }

            if (OwnerTeam == Team.Player) TickPlayerShot(w);
            else TickEnemyShot(w);
        }

        /// <summary>v23：肥料炸弹爆炸。v45：伤害 =（爆炸伤害 + 攻击伤害）× 攻击倍率，半径受爆炸范围影响。</summary>
        private void Explode(World w)
        {
            if (!ExplodeOnEnd) return;
            StatBlock st = w.Player != null ? w.Player.Progress.Stats : null;
            // ★ v195：爆炸倍率已删除 ⇒ 这一发自爆打的就是**它自己算好的普通伤害** ✓
            //   （改前这里乘 `Stats.ExplodeDamage`，那是"肥料倍率"泄漏到投射物上的第二条路径）
            float dmg = st != null ? Damage : 0f;
            // v111：基础半径走统一入口（缺省 3 米）。
            // 这里暂时拿不到"发射它的那件武器"的定义——子弹没存来源 Def。
            // 要用逐武器的基础爆炸范围，需要给 Projectile 加一个来源字段（本次未做）。
            // v197：**半径钳 0**（`ExplodeRange` 的下限已放开 ⇒ 负倍数会让半径变负）。
            //   负半径在下面的距离判定里等价于"谁都炸不到"（这本身就是用户要的负面效果 ✓），
            //   但**表现层**（爆炸环 / 预警圈）拿到负数会被画成翻转的圈 ⇒ 生产点就钳住 ✓
            float radius = Mathf.Max(0f,
                RunSession.BlastBaseRadius(0) * (st != null ? st.Get(StatId.ExplodeRange) : 1f));
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null || e.IsDead) continue;
                if (Vector2.Distance(e.Pos, Pos) > radius) continue;
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Ailment, RawValue = dmg, Source = Source, Target = e, Knockback = Knockback
                });
            }
            Bus.Publish(new EnemyAttackFieldEvent { Pos = Pos, Radius = radius });
            w.MatureAndHarvestInRadius(Pos, radius);   // v45/v47：催熟 + 收割直接作用于本世界（不再靠全局事件广播）
            // v176：投射物自爆**不是玩家的肥料**（它拿不到玩家的插件修正）⇒ 显式给基础外观。
            // 不给的话这里就是一份零初始化的 `FxSpec`（纯黑），而 `WorldView` 会退回基础色 ——
            // 但"显式写出来"比"靠兜底"好：一眼能看出这一路**有意**不吃插件 ✓
            Bus.Publish(new FertilizerBlastEvent
            {
                Pos = Pos, Radius = radius, Fx = PluginFx.Base(WeaponSlot.Fertilizer)
            });   // 仅表现层：WorldView 画爆炸环
        }

        private void TickPlayerShot(World w)
        {
            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (!e.Alive) continue;
                if (Vector2.Distance(e.Pos, Pos) > e.Radius + Radius) continue;

                if (Special == "Splash")
                {
                    for (int j = 0; j < w.Enemies.Count; j++)
                    {
                        EnemyActor other = w.Enemies[j];
                        if (!other.Alive) continue;
                        if (Vector2.Distance(other.Pos, e.Pos) > 1.8f) continue;
                        Hit(other, w);
                    }
                }
                else
                {
                    Hit(e, w);
                }

                if (Bounce > 0)
                {
                    Bounce--;
                    EnemyActor next = w.NearestEnemyToExcept(Pos, 6f, e);
                    if (next != null)
                    {
                        Damage *= 0.7f;
                        if (AilmentValue > 0f) AilmentValue *= 0.7f;
                        Vector2 dir = (next.Pos - Pos).normalized;
                        Vel = dir * Vel.magnitude;
                        Traveled = 0f;
                        TravelLimit = Mathf.Max(3f, Vector2.Distance(Pos, next.Pos) + 2f);
                        return;
                    }
                }
                if (Pierce > 0) { Pierce--; return; }
                Dead = true;
                return;
            }
        }

        private void Hit(EnemyActor e, World w)
        {
            w.DealDamage(new DamageRequest
            {
                Type = Ailment == AilmentKind.None ? DamageType.Magic : DamageType.Ailment,
                RawValue = Damage,
                Source = Source,
                Target = e,
                Knockback = Knockback,
                Ailment = Ailment,
                AilmentValue = AilmentValue,
                Crit = Crit     // v114：出膛时定下的暴击，命中时照用
            });
        }

        private void TickEnemyShot(World w)
        {
            PlayerActor player = w.Player;
            if (player != null && player.Alive && Vector2.Distance(player.Pos, Pos) <= player.Radius + Radius)
            {
                w.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = Damage, Source = Source, Target = player
                });
                Dead = true;
                return;
            }
            // ★ T2：**敌人子弹打植物**的整段已删除 ✗ ★
            //   它遍历 `w.Plants`（恒空）+ 按 `PlantAlly` 类型取 `p.Growing`：
            //     · 循环体**永远不会执行**（没有生产者 ⇒ 列表恒空）✓
            //     · 而 `Plants` / `PlantAlly` 本身已随本次下线删除 ⇒ 不删它也编译不过 ✓
            //   ⇒ 敌人的子弹现在只打玩家（就是本方法上半段那条 ✓）—— 这正是"列表为空"时的既有行为 ✓
        }
    }

    public sealed class Pickup
    {
        public Vector2 Pos;
        public int Gold;
        public float Exp;
        /// <summary>v9：爱心掉落物——拾取后回血（不是金币/经验）。</summary>
        /// <summary>v23：雨滴（拾取给水壶 +20 弹药）。</summary>
        public bool IsRain;
        public bool IsHeart;
        /// <summary>v32：经验球——击杀敌人掉落，拾取同时获得金币与经验。</summary>
        public bool IsExpOrb;
        public float Heal;
        public bool Dead;
        public float Life;

        /// <summary>
        /// ★ v199（用户原话："水分掉落后 5 秒没被拾取就会消失"）★ 雨滴的**存活上限**（秒）。
        ///
        /// ⚠️ 只管**雨滴**：爱心（`IsHeart`）的 `Life` 仍然只是个没人读的计数器，
        /// 它的消失规则不在这条需求里 ⇒ 一个字没动 ✓
        /// </summary>
        // ★ v206（用户原话：「水滴消失时间修改为10s」）：5f → 10f ★
        //   · 掉落率 **一个字没动** —— 用户说「提高到 10%」，而 `RainDropChancePerKill` 早在 v202 就是 `0.10f` ✓
        //     （v184 曾从 25% 降到 10%，v202 又从 5% 回到 10%）⇒ 本条只有**寿命**变了 ✓
        //   · 寿命 10s ⇒ 相对 `RainFadeSeconds`（1.2s 淡出窗口）更短 ⇒ 前 8.8s 不淡、最后 1.2s 收尾 ✓
        //   · 唯一读取点就是下面的 `Dead` 判定与两条表现曲线（`RainFade01` / `RainShrink01`）✓
        public const float RainLifeSeconds = 10f;

        /// <summary>★ v199 ★ 最后这几秒**缩着淡出**（只改缩放 ⇒ 不逐实例换材质/alpha）。</summary>
        public const float RainFadeSeconds = 1.2f;

        /// <summary>★ v199 ★ 雨滴剩余寿命（1 = 刚落地，0 = 该消失了）；非雨滴恒为 1。</summary>
        public float RainLifeLeft01
            => IsRain ? Mathf.Clamp01((RainLifeSeconds - Life) / RainLifeSeconds) : 1f;

        /// <summary>★ v199 ★ 表现层的**淡出缩放**：最后 `RainFadeSeconds` 秒从 1 缩到 0.2。</summary>
        public float RainFadeScale
            => IsRain ? 0.2f + 0.8f * Mathf.Clamp01((RainLifeSeconds - Life) / RainFadeSeconds) : 1f;

        public void Tick(float dt, World w)
        {
            Life += dt;
            // ★ v199：雨滴落地 5 秒没被捡 ⇒ 消失（`Cleanup()` 里按 `Dead` 从列表摘掉）★
            if (IsRain && Life >= RainLifeSeconds) { Dead = true; return; }
            PlayerActor player = w.Player;
            if (player == null || !player.Alive) return;

            float dist = Vector2.Distance(player.Pos, Pos);
            if (dist <= w.Tuning.waves.pickupRadius)
            {
                if (IsRain)
                {
                    // v23：雨滴 → 水壶 +20 弹药
                    Bus.Publish(new RainPickupEvent { Ammo = 20 });
                }
                else if (IsHeart)
                {
                    player.Health.Heal(Heal);
                    Bus.Publish(new HeartPickupEvent { Heal = Heal });
                }
                else
                {
                    // v32：经验球 → 同时获得金币与经验
                    // ★ v167：**这个分支现在不可达** ★
                    //  `SpawnPickup` 已删除 ⇒ 没有任何地方再创建 `IsExpOrb` 的拾取物 ✓
                    //  （雨滴与爱心各自在上一层 if 里 return，走不到这儿）✓
                    //  保留它是为了不改动两个本次不能碰的文件：
                    //    · `Pickup.IsExpOrb` 被 `SelfCheck.cs` 的旧断言读取 ✓
                    //    · `ExpOrbPickupEvent` / `PickupEvent` 的订阅在 `RunSession.cs` ✓
                    w.AddGold(Gold);
                    w.AddExp(Exp);
                    Bus.Publish(new ExpOrbPickupEvent { Gold = Gold, Exp = Exp });
                    Bus.Publish(new PickupEvent { Gold = Gold, Exp = Exp });
                }
                Dead = true;
                return;
            }
            if (dist <= w.Tuning.waves.dropAttractRadius)
            {
                Vector2 dir = (player.Pos - Pos).normalized;
                Pos += dir * (6f * dt);
            }
        }
    }

    public struct PickupEvent { public int Gold; public float Exp; }
    public struct HeartPickupEvent { public float Heal; }
    // v166：`HealOrbEvent` 已删除（光球链是死机制，只发不收）
    /// <summary>v23：雨滴落地。</summary>
    public struct RainDropEvent { public Vector2 Pos; }
    /// <summary>v37：肥料炸弹爆炸（用于催熟范围内的土地）。</summary>
    /// <remarks>
    /// v176：加了 `Fx`。爆炸的**颜色/尺寸/环数/火花/震屏/照明**从此由「插件」决定（见 `PluginFx`），
    /// 不再写死在 `WorldView.OnFertBlast` 里 —— 否则装不装插件画面上完全一样 ✗
    /// </remarks>
    public struct FertilizerBlastEvent { public Vector2 Pos; public float Radius; public FxSpec Fx; }

    /// <summary>
    /// v91：肥料爆炸的**前摇预告**。爆炸不再是"按下即炸"，而是先亮 0.5 秒的区域标记再炸。
    /// 为什么要这个：肥料是范围伤害 + 催熟收割，按下就炸的话玩家来不及看清覆盖了哪些地。
    /// 前摇给了"这一下能炸到几块田"的判断时间。
    /// 与 FertilizerBlastEvent 分开：一个是"要炸了"，一个是"炸了"，表现层要画得完全不同。
    /// </summary>
    /// <remarks>v176：`Fx` —— 前摇标记与真正的爆炸读**同一份**插件外观，
    /// 所以「装增压药罐后标记圈变大、变热」与「真炸的圈也变大、变热」不可能分家 ✓</remarks>
    public struct FertilizerTelegraphEvent
    {
        public Vector2 Pos; public float Radius; public float Duration; public FxSpec Fx;
    }
    /// <summary>
    /// v32：拾取经验球（金币 + 经验）。
    /// ★ v167：**死事件** ✗ —— 敌人已不再掉经验球，"敌人只掉水"⇒ 它永远不会被发布 ✓
    ///   留着是因为 `RunSession.cs` 仍在 Subscribe/Unsubscribe 它（那个文件本次不能改）✓
    ///   清理时机：与 RunSession 一起做，不要单独删这里 ✓
    /// </summary>
    public struct ExpOrbPickupEvent { public int Gold; public float Exp; }
    /// <summary>
    /// v34：经验球掉落（遥测用）。
    /// ★ v167：**死事件** ✗ —— 唯一的发布点 `World.SpawnPickup()` 已删除 ✓
    ///   理由同上：`RunSession.OnExpOrbDropped` 还挂着它 ✓
    /// </summary>
    public struct ExpOrbDroppedEvent { public Vector2 Pos; public int Gold; public float Exp; }
    /// <summary>v23：水壶激光（表现层画一条线）。</summary>
    /// <remarks>
    /// v176：`Fx` —— 激光的**颜色/粗细/拖尾/火花**由插件决定。
    /// 这个结构有**三个发布者**（水壶激光 / 锄头剑气 / 插件连锁），三处各自填自己的 `Fx`：
    ///   · 水壶 → `PluginFx.For(水壶修正, Can)`
    ///   · 剑气 → `PluginFx.For(锄头修正, Hoe)`（所以剑气是冷白月牙，与暖金挥击弧分得清）
    ///   · 连锁 → 敌人位置之间的**紫电弧**，与玩家武器无关
    /// </remarks>
    /// ★★ v200：`HalfWidth` 是**判定半宽**（已含加粗），`ThickenPct` 是**加粗比例本身**
    /// （`can_thickjet` 的 `BeamWidthPct`，0.5 = +50%、1.0 = +100%）★★
    ///
    /// ── 为什么必须多带一个字段（v200 修的是"判定变粗了、画面没变"）✗ ──
    ///   表现层原来这么算可视宽度：`visualW = Clamp(HalfWidth × 2 × 0.16, 0.07, 0.30)`
    ///   ⇒ 基础半宽 1.4 时 `1.4×2×0.16 = 0.448`，**已经撞在 0.30 这个上限上** ✗
    ///   ⇒ 加粗把它推成 `2.8×2×0.16 = 0.896`，**照样被夹回 0.30** ⇒ 画面宽度一个像素都没变 ✓
    ///   （判定宽度是真变粗了，所以症状是"打得更宽、看着一样粗"——
    ///     玩家说的"加粗没有产生效果、要肉眼可见"就是这个。）
    ///   ⇒ 可视宽度必须从**比例**而不是从已经撞上限的值推出来：
    ///     `visualW × (1 + ThickenPct)`，见 `WorldView.OnBeam` ✓
    /// ⚠️ 只由水壶激光填（`RunSession.FireBeam`）；剑气与连锁填 0 ⇒ 它们不受加粗影响 ✓
    public struct BeamEvent
    {
        public Vector2 From;
        public Vector2 To;
        public float HalfWidth;
        /// <summary>加粗比例（0 = 没装 / 不是水壶激光）。只影响**表现宽度**，判定口径仍是 `HalfWidth`。</summary>
        public float ThickenPct;
        public FxSpec Fx;
        /// <summary>
        /// ★★ v307：这一道是**这一轮喷射里的第几道**（0 = 主柱；多柱时 0..JetCount−1）★★
        ///
        /// 为什么必须带这个字段：表现层的几个节流器（`AttackVfx` 的末端水花、
        /// `WorldView` 的命中火花与地面湿痕）原来各自只有一个计时变量，
        /// 而 `FireBeam` 是**同一帧连发 N 个 `BeamEvent`**（`Time.unscaledTime` 完全相同）
        /// ⇒ 只有第 0 道能通过节流，其余道**被静默跳过** —— 用户报的
        /// 「多柱…只有第一个水柱有特效」就是这个。带上方序号，表现层才能**每道各算一份** ✓
        /// 默认 0 ⇒ 剑气 / 共振连锁这两个非多柱来源的行为**逐字不变** ✓
        /// </summary>
        public int Slot;
        /// <summary>
        /// ★★ v307：这一轮喷射一共有几道（多柱时 = `JetCount`；非多柱来源默认 0 = 未知）★★
        ///
        /// 为什么不能只给"第几道"：表现层的节流器**必须知道总共几道**才能守住池账。
        ///   只看 `Slot` 的话，每道各按 `SplashInterval` 出水花 ⇒ N 道就是 **N 倍**水花：
        ///     3 道 × (1/0.18s) × 17 颗 × 0.35s ≈ **100 颗常驻**，而 `Bit` 池只有 **64** 格
        ///     ⇒ 刀光的火星与地面环会被水花挤空（"池被冲空"是 v290 已有一次的事故）✗
        ///   带上总道数之后，表现层按 `SplashInterval × SlotCount` 逐道分摊 ⇒
        ///     **每道都有自己的水花**（用户要的），而**总量与单道时完全相同**（池账要的）✓
        /// </summary>
        public int SlotCount;
    }
    /// <summary>v23：雨滴被拾取（给水壶 +20 弹药）。</summary>
    public struct RainPickupEvent { public int Ammo; }
    // ★ v172：`PlantHealedEvent` 已删除 ✗ —— 它唯一的发布点是 `HealAura` 机制（已删）✓
    //   与 `ExpOrbDroppedEvent` 那条不同：那个还有 `RunSession` 订阅着，所以必须留 ✓
    //   这个**全工程 0 个 `Bus.Subscribe`**（查证方式：搜 `PlantHealedEvent`）⇒ 可以整条删 ✓
    /// <summary>v14：刷怪预警（闪烁 2 秒后真正生成）。</summary>
    public struct SpawnWarningEvent { public Vector2 Pos; public float Seconds; }
    // ★ v166 删除 `TreeProduceEvent` ★ —— 它的唯一发布者 `ProduceFromTree` 已删除，无人订阅 ✓
    // ⚠️ `TreeMatureEvent` **保留**：它是"一格作物成熟"的提示，`World.Simulate` 在每次成熟时发它，
    //    `WorldView.OnPlotMature` 订阅它做落地弹字 ⇒ 与新玩法无关，仍然活着 ✓
    public struct TreeMatureEvent { public int PlotIndex; }

    // v166：`HealOrb` 类已删除（"溢出植物化成治疗光球"—— 植物已不参战/不受伤，不需要治疗）
    // ★ T2：`PlantDiedEvent` 结构**已删除** ✗ ★
    //   它的唯一发布点是 `PlantAlly.OnDeath`（随类删除），唯一订阅点是 `RunSession.OnPlantDied`
    //   （已连订阅一并删除）⇒ 收发两边都死 ⇒ 结构体本身也没有存在理由 ✓

    /// <summary>单帧可变状态的唯一聚合点。所有玩法状态都在这里，无隐藏引擎状态。</summary>
    public sealed class World
    {
        public Pcg32 Rng;
        public Pcg32 VisualRng;
        public TuningDef Tuning;
        public FarmGrid Farm;
        public PlayerActor Player;

        /// <summary>
        /// ★ v191：本局种子 —— **只给派生哈希用**，不是 `Rng` 的内部状态 ★
        ///
        /// ★ 为什么幸运判定不能消费 `World.Rng` ★
        /// 收割是玩家每几秒就做一次的动作。若它从**逻辑随机流**里取一个数，
        /// 同一种子下整条随机序列就会**整体位移**（刷怪点、掉落、暴击、瞄准散布全换轨迹）✗
        /// ⇒ 所有"同种子 ⇒ 同结果"的既有读数（自检 439 条里的一大批，
        ///    以及 `RunSimulator.VerifyDeterminism` / 多种子分布）全部会变 ✓
        /// ⇒ 幸运走**派生哈希**：`seed ＋ 地块索引 ＋ 收割序号` ⇒ 纯函数、可复现、
        ///    与 `Rng` **零交互**（既不读也不写它的状态）✓
        /// </summary>
        public readonly ulong Seed;

        /// <summary>v191：收割序号（每次 `HarvestPlot` 进一次 ++）。派生哈希的第三项 ——
        /// 没有它，同一格反复收割会永远得到同一个结果（要么永远触发、要么永远不触发）✗</summary>
        private ulong _harvestSerial;

        // ★ T2：`List<PlantAlly> Plants` **已删除** ✗ ★
        //   它是"植物参战"子树的**状态根**：生产者（`SpawnPlantFromPlot`）已删、
        //   推进者（`PlantAlly.Tick`，v173 已删）、全部读者（敌人 3 处 / Heal / NearestAlly /
        //   Cleanup / FindPlantOnPlot / WorldView / DebugApi / RunSession / SelfCheck）已全部下线 ✓
        //   ★ 新玩法里"一株植物"住在**一列地块**上 ⇒ 存活植株数请用 `Farm.PlantCount` ★ ✓
        public readonly List<EnemyActor> Enemies = new List<EnemyActor>();
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<Pickup> Pickups = new List<Pickup>();

        public int WaveIndex = 1;
        public float Time;
        public int NextId = 1;
        public int KillCount;
        public int GoldEarned;
        public float ExpEarned;
        public float DamagedByEnemies;

        private readonly List<Actor> _pendingDeaths = new List<Actor>();

        public World(TuningDef tuning, ulong seed)
        {
            Tuning = tuning;
            Seed = seed;
            Rng = new Pcg32(seed, 1);
            VisualRng = new Pcg32(seed, 0xF00D);
            Farm = new FarmGrid(tuning);
        }

        // ──────────────────────────────────────────────────────────────
        // ★ v191：派生哈希 —— "掷一次签，但不碰 `Rng`" ★
        //
        // 算法是 SplitMix64 的 finalizer（`Mix`）：一个 64 位输入的雪崩混合，
        // 输出每一位都受输入影响 ⇒ 相邻的 `(plot, serial)` 得到互不相关的签 ✓
        // 取**高 24 位**换算成 [0,1) —— 与 `Pcg32.NextFloat` 同一套口径（24 位定点）✓
        //
        // 为什么不 `Rng.Fork()`：`Fork` 读的是 `Rng._state`，而 `_state` 每次取数都会变 ⇒
        //   派生流本身确实不消耗主流水位，但它**依赖**主流走过的步数 ⇒
        //   "同种子 + 不同数量的其它随机调用"会得到不同的签 ✗ 那是隐蔽的顺序耦合 ✓
        //   用**构造时就不变的** `Seed` 做种子 ⇒ 签只取决于 (seed, plot, serial) ✓
        // ──────────────────────────────────────────────────────────────
        private static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>
        /// ★ v191：**幸运判定**（用户需求①）★
        ///
        /// 掷一次"这一格这次收割是否触发 ×2"，概率 = `Luck`（0..1 小数）。
        /// 输入只有 `(Seed, plot.Index, 收割序号)` ⇒ 纯函数、可复现、**不消费 `World.Rng`** ✓
        /// 越界/边角：`p ≤ 0` 恒不触发、`p ≥ 1` 恒触发（与 `Pcg32.Chance` 同一套边界语义）✓
        /// </summary>
        public bool RollHarvestLuck(FarmPlot plot)
        {
            float p = Player != null ? Player.Progress.Stats.Get(StatId.Luck) : 0f;
            if (p <= 0f) return false;
            if (p >= 1f) return true;
            ulong serial = ++_harvestSerial;
            ulong h = Mix(Seed ^ Mix((ulong)(plot != null ? plot.Index : -1) + Mix(serial)));
            uint u = (uint)(h >> 40);                      // 高 24 位
            return u * (1f / 16777216f) < p;
        }


        public static Vector2 ClampToArena(Vector2 p, float radius, float half)
        {
            float lim = Mathf.Max(0.1f, half - radius);
            return new Vector2(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim));
        }

        public int NextActorId() => NextId++;

        /// <summary>
        /// v45/v47：把半径内的"生长中"地块**立即催熟并收割**（肥料爆炸的作用），返回被处理的块数。
        /// 从事件回调搬进 World：Bus 是**跨会话的静态总线**，旧写法由 RunSession 订阅
        /// FertilizerBlastEvent 来做催熟，结果同一进程里的另一局（自检 / 无头模拟 / 机器人）
        /// 也会收到广播并催熟自己世界里的地块。逻辑不该依赖全局广播，事件只留给表现层。
        /// v47：不再只催熟——直接连收割一起做完（地块重新生长），玩家不需要再补一锄头。
        /// ★ v166：注释里的"呼叫植物帮手"已删 —— 收割不再产生帮手，收益是金币与经验 ✓
        /// </summary>
        /// <summary>
        /// **爆炸（肥料）的"催熟 + 收割"**：半径内所有 `Growing` / `Grown` 的地块，先催熟再收割 ✓
        ///
        /// ★★ v192：**这条路的收益低于正常收割**（用户需求③）；v195 从 1/3 改 **1/4** ★★
        /// ★★ v200：**1/4 只归这条爆炸路径**（用户原话："只有爆炸操作造成的收割才只获得 1/4 效果，
        ///    正常锄头的攻击按正常结算"）★★
        ///
        /// ── v200 之前是什么（就是这个 bug 的形状）✗ ──
        ///   写法是"把 `World.HarvestYieldScale` 改成 1/4、跑完在 `finally` 里恢复 1"✓
        ///   它是一个**世界级的可变字段**，于是"只有爆炸吃到 1/4"这句话成立的**唯一依据**
        ///   是"没人会忘记恢复"。那正是最难查的一类缺陷：恢复漏一条路径，或者将来某条新代码
        ///   在 `try` 里多调一次收割，手挥锄头就**静默变成 1/4** —— 而全表**没有任何断言会红** ✗
        ///
        /// ── 现在是什么 ✓ ──
        ///   比例**当参数传**：`HarvestPlot(plot, ratio)`。单参重载 `HarvestPlot(plot)` 恒等价于
        ///   `HarvestPlot(plot, 1f)` ⇒ 锄头单点 / 锄头扫掠 / 剑气 / 水枪 / 连锁收获
        ///   **在类型层面就拿不到 1/4**（它们连第二个参数都传不了，也没有地方"读到一个脏值"）✓
        ///   可变字段 `HarvestYieldScale` 已删除 ⇒ 这个缺陷无法以任何调用顺序复现 ✓
        /// 判据（探针 `ProbeV200`）：同一块地、同一个种子 ⇒ 手收 金/验 = 全额，炸收 = 全额 ×0.25，
        ///   且**炸完之后立刻手收**仍然是全额（没有残留系数）✓
        /// </summary>
        public int MatureAndHarvestInRadius(Vector2 center, float radius)
        {
            if (Farm == null) return 0;
            float ratio = Tuning.farm != null ? Tuning.farm.blastHarvestRatio : 1f;
            // ★ v195：回落值同步改成 1/4（与表里写的是同一个数 ⇒ "表里漏键"不会静默改平衡）
            if (ratio <= 0f || ratio > 1f) ratio = 1f / 4f;
            int n = 0;
            for (int i = 0; i < Farm.Slots.Length; i++)
            {
                FarmPlot plot = Farm.Slots[i];
                if (plot == null) continue;
                // v91：**已成熟的也要收割**。
                // 旧写法是 `if (plot.State != PlotState.Growing) continue;` —— 只处理"生长中"，
                // 于是已经长熟的地反而被跳过：玩家看着一片成熟的田扔肥料，什么也没发生。
                // 现在两种状态都收，只是"生长中"的需要先催熟一步。
                if (plot.State != PlotState.Growing && plot.State != PlotState.Grown) continue;
                if (Vector2.Distance(plot.World, center) > radius + 1.0f) continue;
                if (plot.State == PlotState.Growing) plot.TickGrowth(999f);   // 未成熟的先催熟
                if (HarvestPlot(plot, ratio)) n++;
            }
            return n;
        }

        /// <summary>
        /// 收割一块成熟土地——**发放金币与经验** + 地块重新生长。
        /// 这里是收割逻辑的**唯一实现**（锄头 / 锄头扫掠 / 肥料都走它），RunSession 只负责提示与埋点。
        /// ★ v166：注释里的"召唤植物帮手"已删 —— v165 起收割不再产生战斗植物 ✓
        /// 收益按**格**算（每格 price/5 金币与 price/10 经验）⇒ 一列 N 格就是 N 倍 ✓
        ///
        /// ★★ v200：这是**正常结算（全额）**的入口 —— 等价于 `HarvestPlot(plot, 1f)` ★★
        /// 锄头单点（`RunSession.HarvestPlot` → 这里）/ 锄头扫掠（`HarvestRipeInMeleeBox`）/
        /// 剑气（`HarvestRipeInSwordWave`）/ 水枪（`SharpWaterStacks`）/ 连锁收获
        /// —— 它们全部只调这个重载 ⇒ **都拿不到爆炸那条 1/4 系数**（那个比例只能由
        /// `HarvestPlot(plot, yieldScale)` 的第二个参数传进来）✓
        /// </summary>
        public bool HarvestPlot(FarmPlot plot) => HarvestPlot(plot, 1f);

        /// <summary>
        /// 收割的**唯一实现**。`yieldScale` = **这一次收割的收益系数**（1 = 全额；爆炸 = 1/4）。
        ///
        /// ★ 为什么是参数而不是世界字段（v200 修的就是这件事）★
        ///   可变字段要求"记得恢复"，而恢复遗漏是静默的；参数是**栈上**的 ——
        ///   调用栈一退，1/4 就跟着消失，没有可泄漏的公共状态 ✓
        ///   详见 `MatureAndHarvestInRadius` 的注释。
        ///
        /// ★ 为什么它乘在**基数**上（`goldBase` / `expBase`）★
        ///   乘在基数上 ⇒ 幸运 ×2、等级倍率、道具 `harvest_gold_pct`、`Stats.GoldMult`
        ///   **全部自动跟着缩**，而弹字印的是**到账值** ⇒ 弹字自动变、且只有一处乘 ✓
        ///
        /// `yieldScale` 在方法开头夹到 **[0,1]**：负数不该出现（配错表就会"收割倒贴钱"✗），
        /// 大于 1 也不该出现（这条系数只负责削减；加成走各自那几条乘区）✓
        /// </summary>
        public bool HarvestPlot(FarmPlot plot, float yieldScale)
        {
            yieldScale = Mathf.Clamp01(yieldScale);
            if (plot == null || !plot.IsRipe) return false;
            // ★ v166：收益按**这一格上长的作物**算，而不是"驱动这一列的种子"★
            // 一列里的每一格都是同一株的一份作物（"相当于每个植物多种 1 个"）⇒
            // 每一格独立结算一次 price/5 金币与 price/10 经验 ⇒ **收益随行数线性增长** ✓
            // 若读 plot.SeedId，除列首以外的格子会拿不到 def（按设计它们不登记 SeedId）
            // ⇒ 全部退化成 Mathf.Max(1, ...) 的保底 1 金币 ✗ 那就不是"多种 1 个"了 ✗
            // ★ v168：**作物的唯一真相是地块自己持有的定义**（`plot.CropDef`）★
            //
            // 改动前这里是 `TableDatabase.GetSeed(plot.CropSeedId)` —— 按 id **回查真表** ✗
            // 而 `GetSeed` 对**不存在的 id 抛 `KeyNotFoundException`**（只有**空** id 才返回 null ✓）
            // ⇒ 任何"地块上有定义、但那个 id 不在表里"的情况都会在**收割路径上**抛异常 ✗
            //   而收割是玩家每一帧都在走的路径 ⇒ 撞上就是挥一次锄头崩一次 ✓
            //
            // 这与本会话早先修过的那条是**同一个形状**：
            //   那时是 `plot.SeedId` 只有列首有值 ⇒ `GetSeed(null)` ⇒ ArgumentNullException ✓
            //   修法是"改读 `CropSeedId`"✓ —— 但那只把"读错字段"换成了"读对字段"，
            //   **"收割路径按 id 回查表"这条隐式契约还在** ✗ 于是这次被自造 id 的测试夹具绊倒 ✓
            //
            // 现在把契约去掉：`FarmPlot.CropDef` 就是 `BeginPlant` 时传进来的那份定义 ✓
            //   · 生产里它**就是** `TableDatabase` 里的那个对象 ⇒ `Price` 与旧实现**逐位相同** ✓
            //   · 它每格都有（不是只有列首）⇒ 同列任意一行都能正确结算 ✓
            //   · 它不需要集合查找，也不会因为"表里没这个 id"而改变行为 ✓
            SeedDef crop = plot.CropDef;
            if (crop == null)
            {
                // 不静默：地块没有携带定义只可能来自直接构造 `FarmPlot` 的调试路径 ✓
                // （玩家路径不可能 —— 能收割就说明 `BeginPlant` 跑过了 ✓）
                Log.Error($"收割时地块 {plot.Index} 没有作物定义（CropSeedId={plot.CropSeedId ?? "null"}）⇒ 收益退回保底 1");
            }

            // ★ v165：**删掉"收割即变成战斗植物"** ★
            //
            // 这里原来是：
            //     PlantAlly helper = SpawnPlantFromPlot(plot, def, null, false, 0);
            //     helper.FreeFromPlot();  helper.SetGrown();   // "解放：不再绑定地块 / 立刻参战"
            //
            // 那是**旧玩法的核心** ✗ —— 收割的奖励是"多一个战斗单位" ✓
            // 而新玩法里植物**完全不战斗** ⇒ 这条规则必须消失 ✓
            //
            // ★ 这是审计查出来的残留，而我自己没发现 ★
            // 我只改了"植物停战"的 Tick 门 ✗ 却没注意**收割还在生产新的战斗单位** ✓
            // ⇒ 于是"停战"是假的：植物不再行动，但玩家每收割一次就多一个"参战"的植物 ✓
            //
            // 现在收割的奖励改成**金币与经验**（见下）✓ 这才是新玩法的定义 ✓

            Vector2 at = plot.World;

            // ★ v168：收获阶段的三条机制，全部在这里接线 ★
            //
            // ① `DewDrop` —— 收获**返还蓄水**。必须在 `plot.Harvest` **之前**加：
            //    `Harvest` 里有一句 `if (Water >= WaterMax) NeedFullRefill = false;`
            //    先加后收 ⇒ 凝露足够多时该格会直接进入下一轮生长（这就是它的机制价值）✓
            //    反过来写的话，返还只影响"下一轮浇水的次数"，差别很微弱 ✓
            // ★ 两条来源合并成一次补水 ★
            //   · `PlantMechanics.HarvestDew(crop)` —— **植物**机制 DewDrop（生命之泉）
            //   · `HarvestDew` 字段                 —— **道具**规则 harvest_dew（露水壶），由 RunSession 写入
            // 合并而不是各写一次 `AddWater`：两次调用会**分别**被 WaterMax 钳一次，
            // 于是"植物 12 + 道具 4"在接近满水时可能只加出一部分，而且看不出是谁被截断 ✓
            int dew = PlantMechanics.HarvestDew(crop) + Mathf.Max(0, HarvestDew);
            if (dew > 0) plot.AddWater(dew);

            plot.Harvest(Tuning.farm);   // 地块回到"生长中"

            // ★ v165：**真正发放收益** ★
            //
            // 审计指出：这里只发了两条弹字，**一分钱都没给** ✗
            // ⇒ 弹字在撒谎 ✓ 而"金币与经验只从收割来"这句新玩法的核心定义**完全没接线** ✓
            //
            // ★ 数值来源：v165 用 `price` 推导；**v179 起改为 `goldYield` / `expYield`** ★
            //   v165 的理由是"`price` 已是这株值多少的既有一切依据，再开两个字段会让买价与收益打架"✓
            //   v179 用户要求**同一档内金币与经验可以反向分配**（金型 70/30、验型 30/70、
            //   重型 50/50 ×2 周期、快型 50/50 ×0.5 周期）✗
            //   而 `price/5` 与 `price/10` 是同一个数按两个固定比例缩放 ⇒ 金验比恒为 2:1
            //   ⇒ **同档内反向分配在 `price` 单旋钮下数学上不可能** ✓
            //   ⇒ 拆成两个字段；`price` 保留为**种子售价**（商店定价仍读它），与收益脱钩 ✓
            //   ★ 防"两个旋钮打架"的判据不是"两数相等"，而是**不能出现"更便宜还更强"**：
            //     由 `SelfCheck` 的 `SEED-TIER-RATE`（同档每秒总价值差 ≤ 2%）守住 ✓
            //
            // ★ v168：乘区补完 —— 等级 × 植物机制 × 道具机制 ★
            //   ① `plot.LevelYieldMult` = ×1.00 / ×1.35 / ×1.70（合成等级）
            //   ② `PlantMechanics.HarvestGoldMult` = ×1.30（丰收）/ ×1.60（水分低于 50% 时的焦渴绽放）
            //   ③ `HarvestGoldMult` 字段 = 道具规则 `harvest_gold_pct`（由 `RunSession.SyncItemRules` 写入）
            // 三个乘区各自只有一个来源 ✓ **v179 只把基数从 `price/5`、`price/10` 换成
            // `SeedEconomy.CellGold/Exp`，这三条乘区一条都没动** ✓
            float goldMult = (crop != null ? plot.LevelYieldMult : 1f)
                             * PlantMechanics.HarvestGoldMult(crop, plot)
                             // ★ v253：另外四条收获侧机制（前三条是纯函数，第四条记账）★
                             //   · `FarmHarvestGoldMult` —— 夜露（读 `Time` 经昼夜循环）+ 田园成片（数已解锁耕地）
                             //   · `PlantStreakGoldMult` —— 连收递增（账本在 `ProgMechanics`，同一格逐次 +6%）
                             //   两条都**只对有该机制的品种**返回非 1 ⇒ 既有 39 株读数不变 ✓
                             * PlantMechanics.FarmHarvestGoldMult(crop, Farm, Time)
                             * ProgMechanics.PlantStreakGoldMult(plot, crop, Time)
                             * Mathf.Max(0f, HarvestGoldMult);
            float expMult = crop != null ? plot.LevelYieldMult : 1f;
            // ★★ v192：**收益系数**（用户需求③"爆炸收割 = 原来的 1/3"；v195 改 1/4）★★
            // ★ v200：改成**入参**（`yieldScale`）而不是世界字段 `HarvestYieldScale` —— 见方法头。
            //   普通收割恒为 1；只有 `MatureAndHarvestInRadius` 会传 `farm.blastHarvestRatio` ✓
            // 乘在**基数**上（不是乘到账数字）⇒ 幸运 ×2 / 等级倍率 / 道具 / GoldMult 全自动跟着缩，
            // 而弹字印的是到账值 ⇒ 弹字自动变 ✓
            // ★ v195：**收割金币全局系数**（`farm.harvestGoldScale`，需求"植物金币收获量调整"）★
            //   只乘金币、不乘经验 —— 用户点名的是"金币收获量"；经验曲线本轮没动 ✓
            //   位置与 `yieldScale` 同层（乘在**基数**上）⇒ 幸运/等级/道具/GoldMult 全自动跟着缩 ✓
            float goldScale = Tuning.farm != null && Tuning.farm.harvestGoldScale > 0f
                ? Tuning.farm.harvestGoldScale : 1f;
            int goldBase = crop != null ? Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(crop) * goldMult * yieldScale * goldScale)) : 1;
            int expBase = crop != null ? Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellExp(crop) * expMult * yieldScale)) : 1;

            // ────────────────────────────────────────────────────────────
            // ★ v191 ①：**幸运判定**（用户需求①）★
            //   "幸运值可以让植物被收割时概率获得金币和经验值 ×2 效果，触发时弹字后加 X2"
            //
            // 位置：乘在**基础收益**上，而不是乘在到账数字上 ——
            //   两种写法的最终到账相同（全乘区都是乘法），但"乘在基础值上"能让
            //   下面的 `AddGold/AddExp` **只做一次四舍五入**（`RoundToInt(20 × 2)` = 40，
            //   而不是 `RoundToInt(20) × 2` 那种看起来一样、调了数值才分叉的第二条路径）✓
            //
            // ★★ 用派生哈希，绝不消费 `World.Rng` ★★（`RollHarvestLuck` 的注释写了原因）★
            // ────────────────────────────────────────────────────────────
            bool lucky = RollHarvestLuck(plot);
            int luckFactor = lucky ? 2 : 1;
            string luckTag = lucky ? " X2" : "";

            // ★ v191 ④：**属性倍率在这里生效**（用户需求④）★
            //
            // 用户原话："确认货币倍率和经验值倍率生效，效果是让植物收割时实际获得量
            //           为基础数值 × 倍率，记得弹字同步更新"
            //
            // ★ 现状与需求差在哪（查证过的，不是我猜的）★
            //   `StatId.GoldMult` / `ExpMult` **确实**已经在收割上生效 ——
            //   但它生效的地点是 `AddGold` / `AddExp` 内部（`base × Stats.Get(GoldMult)`），
            //   而**弹字印的是乘之前那个数** ✗
            //   ⇒ 症状：货币倍数 150% 时，到账 30 金，弹字写"金币+20" ✗ 弹字在撒谎 ✓
            //   ⇒ 所以本轮**不改乘区实现**（改了会二次相乘），而是让
            //     `AddGold/AddExp` **返回实际到账值**，弹字印返回值 ✓
            //
            // ★ 两个乘子各自保留语义（不合并成一个）★
            //   · `HarvestGoldMult`（道具规则 `harvest_gold_pct`）= "这一下收成多值钱"
            //   · `Stats.Get(GoldMult)`（属性/卡牌/道具）= "全局货币倍率"
            //   前者进 `goldBase`，后者在 `AddGold` 里 —— 顺序相乘、来源各自唯一 ✓
            int goldGain = AddGold(goldBase * luckFactor);          // 返回**实际到账**（已含 GoldMult）
            float expCredited = AddExp(expBase * luckFactor);       // 返回**实际到账**（已含 ExpMult）
            int expShown = Mathf.RoundToInt(expCredited);

            Bus.Publish(new FloatingTextEvent
            {
                Pos = at,
                Text = "收获 " + (crop != null ? crop.Name : "植物"),
                Kind = PopupKind.Harvest
            });

            // 需求"植物成熟被收割后，弹字显示金币+、经验值+，分别为黄色和绿色" ✓
            //
            // 为什么要【两条独立弹字】而不是拼成一条 ✗
            // 因为颜色不同 ✓ 而一条弹字只有一个颜色 ✓
            // 拼成"金币+3 经验+5"就只能二选一染色 ⇒ 违背需求 ✓
            //
            // 为什么要【错开位置】✗
            // 三条弹字同点发出会完全重叠 ⇒ 只看得到最后一条 ✓
            Vector2 goldOffset = new Vector2(-0.45f, 0.35f);
            Vector2 expOffset = new Vector2(0.45f, 0.35f);

            // ★ v191：弹字印**到账数字**，触发幸运时后缀 ` X2`（用户需求①④）★
            //   判据（自检钉着）：同一块地、`GoldMult` 1.0 vs 1.5 ⇒ 到账 ×1.5 **且弹字跟着变** ✓
            Bus.Publish(new FloatingTextEvent
            {
                Pos = at + goldOffset,
                Text = "金币+" + goldGain + luckTag,   // v165：带上实际数值 ✓ 弹字不该撒谎
                Kind = PopupKind.GoldGain      // 黄色 ✓
            });
            Bus.Publish(new FloatingTextEvent
            {
                Pos = at + expOffset,
                Text = "经验值+" + expShown + luckTag,
                Kind = PopupKind.ExpGain       // 绿色 ✓
            });

            // ★ v168：收获阶段的第四条机制 —— `ChainHarvest`（圣树守卫）★
            //
            // 语义：收割**一格**成熟作物 ⇒ **同列其它已经成熟的格一并收获** ✓
            // ⇒ 玩家挥一次锄头就把整列收完，收益按格照常结算（每格各自 ×等级 ×机制）✓
            //
            // ★ 递归保护：`_inChainHarvest` ★
            // 被连锁收掉的那一格自己也是 `ChainHarvest`（同列同一株 ⇒ 机制相同），
            // 它收完又会去找同列的成熟格 ⇒ 没有守卫就是**无限递归** ✗
            // 守卫放在 `HarvestPlot` 内部而不是调用方：调用方有三个（锄头扇形 / 肥料半径 / 单点），
            // 靠调用方自觉是拦不住的 ✓（`_chaining` 在伤害连锁上原本就是同一套做法 ——
            // v172：那个 `_chaining` 已随植物连锁闪电一起删除，但这条守卫的理由**与它无关**：✗
            // 守卫保护的是"同列机制自己递归自己"，是本机制的固有属性 ✓）
            if (!_inChainHarvest && PlantMechanics.ChainsHarvest(crop) && Farm != null)
            {
                _inChainHarvest = true;
                try
                {
                    int col = plot.Column;
                    for (int r = 1; r < Farm.Rows; r++)
                    {
                        FarmPlot sib = Farm.Slot(col, r);
                        if (sib == null || sib == plot || !sib.IsRipe) continue;
                        // ★ v200：把**这一次收割的系数**沿递归带下去 ✗
                        //   若不带：肥料炸出来的那一格有 `ChainHarvest`（圣树守卫）时，
                        //   它同列的兄弟格会按**全额**结算 ⇒ 同一个爆炸里一半 1/4、一半全额，
                        //   玩家看到的是"炸收的收益忽高忽低"✓ 带下去才是"一次爆炸一个口径"✓
                        HarvestPlot(sib, yieldScale);       // 递归一层，守卫保证不再往下展开
                    }
                }
                finally
                {
                    _inChainHarvest = false;
                }
            }
            return true;
        }

        /// <summary>
        /// ★ v168：本期**收获金币倍率**（道具规则 `harvest_gold_pct` 的消费点）★
        ///
        /// 由 `RunSession.SyncItemRules()` 写入；没买任何道具时恒为 1 ✓
        /// 与 `HarvestDew` 一样，它是"表里的 rule → 一个已算好的系数 → 结算处读它"这条链的中间一环 ✓
        /// 结算处**不再遍历道具表**：那会是第二份实现，也会让 `World` 反向依赖 `RunSession` ✗
        /// </summary>
        public float HarvestGoldMult = 1f;

        /// <summary>★ v168：每次收获**额外返还的蓄水**（道具规则 `harvest_dew` 的消费点）★</summary>
        public int HarvestDew;

        // ★★ v200：**`HarvestYieldScale` 这个字段已删除** ★★
        //
        // 它原来是 v192/v195 的"爆炸收割 1/4"载体：`MatureAndHarvestInRadius` 临时把它改成 1/4、
        // 跑完在 `finally` 里恢复 1。**它就是那个 bug 的形状** —— 一个世界级的可变字段，
        // "只有爆炸吃到 1/4"这句话成立的全部依据是"没人会忘记恢复"；
        // 而全表**一条断言都没钉它**，所以"手挥锄头也变成 1/4"这种状态可以长期存在而不报红 ✗
        //
        // 现在比例是 `HarvestPlot(plot, yieldScale)` 的**入参**：
        //   · 单参重载 `HarvestPlot(plot)` = `HarvestPlot(plot, 1f)` ⇒ 锄头/剑气/水枪/连锁 = 全额 ✓
        //   · 爆炸在 `MatureAndHarvestInRadius` 里传 `farm.blastHarvestRatio` ✓
        // 没有公共状态 ⇒ 没有可泄漏的系数 ⇒ 这个缺陷无法以任何调用顺序复现 ✓
        // 判据：探针 `ProbeV200` 的"手收 / 炸收 / 炸完再手收"三个金币与经验读数 ✓

        /// <summary>连锁收获的递归守卫（见 `HarvestPlot` 末尾）。</summary>
        private bool _inChainHarvest;

        // ------------------------------------------------------------ 生成
        public void SpawnEnemy(string enemyId, Vector2 pos, int wave, int bossIndex = 1)
        {
            EnemyDef def = TableDatabase.GetEnemy(enemyId);
            EnemyActor e;
            switch (def.Kind)
            {
                case EnemyKind.Shooter: e = new ShooterEnemy(); break;
                case EnemyKind.Jumper: e = new JumperEnemy(); break;
                case EnemyKind.Boss: e = new BossEnemy(); break;
                case EnemyKind.Wander: e = new WanderEnemy(); break;
                case EnemyKind.Grunt: e = new GruntEnemy(); break;
                // v45：映射必须穷举。历史上这里漏了 Wander，小猪被 default 静默降级成追击玩家的 GruntEnemy。
                default:
                    throw new ArgumentOutOfRangeException(nameof(def.Kind), def.Kind,
                        $"未实现该 kind 的敌人：{def.Id}");
            }
            e.Id = NextActorId();
            e.Def = def;
            e.Team = Team.Enemy;
            e.Pos = World.ClampToArena(pos, def.Radius, Tuning.farm.arenaHalf);
            e.Radius = Actor.ScaledRadius(def.Radius);      // v75：与放大后的模型体量对齐
            e.WaveIndex = wave;
            e.BossIndex = bossIndex;
            e.Health.Setup(def.HpForWave(wave, bossIndex), 0f);
            // v45：小猪在**生成瞬间**取一个随机初始方向（同一 seed 必然同方向），之后只被墙壁反射改写
            if (e is WanderEnemy wander) wander.InitHeading(Rng);
            Enemies.Add(e);
        }

        // ★ T2（2026-10）：`SpawnPlantFromPlot` **整段删除** ✗ ★
        //
        // 它是"植物参战"这条子树**最后一个产出点**：全工程唯一能造出 `PlantAlly` 实体的方法 ✓
        //
        // 删它的判据（不是"少几行"，而是"这条子树不存在"）：
        //   · 调用者：**5 → 0** ✓ —— T2 的 ① 先删掉了 SelfCheck 的 5 个夹具
        //     （旧行号 354 / 508 / 2084 / 3187 / 3188）⇒ 删完立刻成为**零调用者孤儿** ✓
        //   · 删掉之后 `Plants.Add(...)` 再无写者 ⇒ **`World.Plants` 可证明恒空** ✓
        //     ⇒ 树上所有 `Plants` 遍历（敌人跳跃 `Impact` / BOSS 冲锋 / `TickEnemyShot` /
        //       `NearestAlly` / `Heal` / `Cleanup` / `FindPlantOnPlot`）在运行时**全部空转** ✓
        //   · 配合 v173 已删的"植物 tick"闸门（见上方 `Simulate` 第 3 段）
        //     ⇒ "植物索敌 / 移动 / 攻击"这条链**从入口到推进全部不存在** ✓
        //
        // ★ 为什么只删到这里（这是 T2 的边界，不是半途而废）★
        //   `PlantAlly` **类本身** / `World.Plants` 列表 / `Projectile.PlantShot`
        //   三个**不能**跟着删 ✗ —— 因为它们被"敌人打植物"的循环**按类型引用**着：
        //     · `Enemies.cs:273`  `PlantAlly p = w.Plants[i];`（跳跃场 `Impact`）
        //     · `Enemies.cs:329`  同上（BOSS 冲锋）
        //     · `Actors.cs:906`   同上（`Projectile.TickEnemyShot`）
        //     · `Actors.cs:1648`  同上（`World.NearestAlly` —— **活的敌方索敌**）
        //   那几处是**上一轮审计裁决"保留"**的（理由：让"存活 / 死亡"这条链还在）✓
        //   ⇒ 保留它们 ⇒ 类型必须存在 ⇒ 类与列表必须存在 ✓
        //   ⇒ 所以 T2 把它删到"**没有任何东西能造出植物**"为止；再往下就是改那条裁决了 ✗
        //
        // ⚠️ 连带：`PlantSpawnedEvent`（同文件 :1912）**失去唯一发布点** ⇒ 它现在是**死事件** ✓
        //    而 `RunSession.cs:1559 / :1572` 仍在 Subscribe / Unsubscribe 它 ✓
        //    ⇒ 与 `ExpOrbDroppedEvent` 同一个先例：**结构体留着、永不被发布** ✓
        //      （删结构体会让 RunSession 编译不过 = 越界 ✗）

        // ── v10：树的产出源——玩家拥有的所有种子（由 RunSession 同步进来）

        /// <summary>
        /// v23：回合进度 0..1（旧"夜战"机制用）。
        /// ★ v168：`NightBoost` 早已随"植物不参战"一起失效，所以新设计**没有**任何机制依赖它 ——
        /// 与其再造一个"靠回合进度"的机制、赌它真的被写入，不如用确定被写入的 `World.Time` ✓
        ///
        /// ★ v172：它的**最后一个读者**（`PlantAlly.NightBoosted`，夜战植物伤害翻倍）已删除 ✗
        /// ⇒ 现在它**全工程 0 个读者、0 个写者**（查证方式：搜 `WaveProgress01`）✓
        /// ⚠️ 我没有顺手删掉它：它是"**回合**进度"，属于回合组的地界（本轮 `RunSession` 正在动回合）✓
        ///    ⇒ 留给回合组连"回合概念"一起清理 ✓ 在这里写明状态，避免下一个人以为它还活着 ✗
        /// </summary>
        public float WaveProgress01;

        // ★ v172：`AttractBonus`（掉落物吸附半径加成 / 生命之泉的 `Magnet` 机制）已删除 ✗
        //   它是**只写不读**的字段：全工程只有两处写（都在 `HealAura/Magnet` 分支里，已删），
        //   没有任何读者（查证方式：搜 `AttractBonus`）✓
        //   ⇒ 旧机制要复活就得**重新发明一个消费者**，而不是调一个现成的 API ✓

        // ★ v168：`HasWaterSave(plot)` 已删除 ✗ —— 它只认一条机制 `"WaterSave"` ✓
        //    机制从 1 条涨到 9 条之后，判据全部收敛到 `PlantMechanics`（机制名 → 效果 → 消费点）✓
        //    `World` 侧只保留三个问句：`WaterCostScale` / `GrowSpeedMult` / `ThirstyGrowMult` ✓

        public readonly List<SeedDef> OwnedSeeds = new List<SeedDef>();
        public readonly List<int> OwnedQualities = new List<int>();
        public readonly List<string> OwnedMutations = new List<string>();

        /// <summary>玩家拥有的种子变化时由 RunSession 调用（树按这份清单产出）。</summary>
        public void SyncOwnedSeeds(SeedBag bag)
        {
            OwnedSeeds.Clear();
            OwnedQualities.Clear();
            OwnedMutations.Clear();
            for (int i = 0; i < bag.SlotCount; i++)
            {
                SeedSlot slot = bag[i];
                if (slot.IsEmpty) continue;
                if (OwnedSeeds.Contains(slot.Def)) continue;   // 同种只产一株
                OwnedSeeds.Add(slot.Def);
                OwnedQualities.Add(slot.Quality);
                OwnedMutations.Add(slot.MutationId);
            }
        }

        // ★ v166 删除的死代码 `ProduceFromTree` + `SpawnPlantAt` ★
        //
        // 这是 v10"树成熟后按拥有的种子持续产出植物"的整条链 ✓
        // 它全工程**无调用者**：`TreeMatureEvent` 的唯一订阅者是 `WorldView.OnPlotMature`（纯表现），
        // 没有任何地方调用 `ProduceFromTree`；`SpawnPlantAt` 的唯一调用者又是 `ProduceFromTree` ✓
        // 而它的入口 `FarmPlot.BeginTree` 同样无调用者（已在 FarmGrid 删除）⇒ 整条链是死的 ✓
        //
        // 为什么必须删（不只是"清理"）：
        //   它是 **`SpawnPlantFromPlot` 的第二个"生产参战植物"调用点** ✗
        //   与 `RunSession.InteractPlotByAttack` 里刚删掉的那处是同一类残留 ✓
        //   留着它 ⇒ 将来谁复活"树"模型，"植物不战斗"就会再一次变成假的 ✓
        //   这正是本会话反复出现的"共享规则有多个副本、只删了一个" ✓
        //
        // 连带 `TreeProduceEvent`（同文件下方）也失去唯一发布者 ⇒ 一并删除 ✓
        // ⚠️ `SpawnPlantFromPlot` 本身**保留** —— 它的调用者只剩 SelfCheck 的 **5 个**夹具 ✓
        //    （v172 核对的真实行号：SelfCheck.cs **354 / 508 / 2084 / 3187 / 3188** ✓
        //      —— 旧注释写的 331/485/1953/2427/2428 是**过期行号**，已按 grep 更正 ✗）
        //
        // ★ v172 调查结论：那 5 个夹具测的【全部是已删除的机制】★ ✓
        //    · :354  "幼苗免疫伤害 / 成株可被伤害"      ⇒ 植物作为**可被攻击的战斗单位**
        //    · :508  "植物受击进入无敌帧"               ⇒ 同上（玩家那套另有断言覆盖）
        //    · :2084 "植物有判定范围且大于攻击范围"      ⇒ **植物索敌 AI**（DetectM）
        //    · :3187 "稀有度不影响植物生命 / 攻击"       ⇒ 植物**攻击力**这条战斗属性
        //    ⇒ 没有一个是"收割路径"✓ 而它们之所以还是绿的，是因为代码仍能跑（只是玩法上不可达）✓
        //    ⇒ 它们一删，本方法就变成**零调用者的孤儿** ⇒ 那时应连 `PlantAlly` / `World.Plants`
        //       一起下线（要动 RunSession / WorldView / DebugApi，不在本轮范围内）✓
        //    ⇒ 本轮**不删夹具**：只删夹具、留着工厂 = 更糟（孤儿工厂没人测，仍可被接回）✓

        // ★ v166 删除的死代码：**治疗光球整条链** ★
        //
        // 删掉的是：`HealOrbs` 列表 / `SpawnHealOrb` / `NearestAllyForOrb` / `TickHealOrbs`
        //           以及 `HealOrb` 类与 `HealOrbEvent`（结构体在文件下方一并删）
        //
        // 判定依据（两条，任一条都足够）：
        //   ① **`SpawnHealOrb` 全工程 0 个调用者** ⇒ 光球永远不会生成 ⇒ `TickHealOrbs` 在遍历一个空列表 ✓
        //      （`HealOrbEvent` 也**没有任何 `Bus.Subscribe`** —— 只发不收 ✓）
        //   ② 语义已经**不成立**：它的定义是"植物溢出 → 化成治疗光球 → 飞向最近的植物给它回血" ✗
        //      而新玩法里植物**不参战、不被攻击、不死亡** ⇒ **它们根本不需要治疗** ✓
        //      ⇒ 留着它 = 留一条永远跑不到、且语义已废的通路 ✓
        //
        // ⚠️ 与 `World.Plants` 的处置**不同**：`Plants` 本体保留（还有 5 处 SelfCheck 夹具），
        //    而光球链是**独立的死机制**，可以现在就整条删掉 ✓
        // ⚠️ `WavesDef.overflowOrbDelay` 配置字段暂时保留（`Defs.cs`）—— 现在没有读者，
        //    但它属于"波次配置表"，与波次组的地界重叠 ⇒ 留给回合组一起清理 ✓

        public void SpawnProjectile(Projectile p)
        {
            Projectiles.Add(p);
        }

        // ★ v167：`SpawnPickup(pos, gold, exp)` 已删除 ✗
        //
        // 删除理由（**它再无调用点**）✓
        //   · 全工程唯一调用点是敌人死亡处（`Cleanup()`）✓ 已随"敌人只掉水"一起移除 ✓
        //   · 其余位置**只读** `Pickup.IsExpOrb`，没有任何地方再调它 ✓
        //     （查证方式：全 Assets 搜 `SpawnPickup` / `new Pickup` / `IsExpOrb`）
        //   · 留着一个"唯一金币/经验来源"的工厂方法是**反向的**：
        //     谁调它谁就能把金币/经验变回怪物掉落，恰好破坏这次的需求 ✓
        //
        // ★ 但这三个东西**必须留着**（不是我忘了清）★
        //   · `ExpOrbDroppedEvent` / `ExpOrbPickupEvent`
        //       ⇒ `RunSession.cs` 仍在订阅它们（Start/Dispose 各两处）✓
        //         删掉结构体 = 那个文件编译不过 = 本次硬约束禁止改它 ✓
        //         ⇒ 现在的状态是"事件还在，但永远不会被发布"（死事件）✓
        //   · `Pickup.IsExpOrb`
        //       ⇒ 被 `SelfCheck.cs` 的旧断言读取 ✓ 删掉字段同样会让它编译不过 ✓
        //   · `Pickup.Tick` 里发金币/经验的那个 `else` 分支
        //       ⇒ 现在不可达（见该处注释）✓ 保留是为了不改动上面两处 ✓

        /// <summary>v9：爱心掉落物（拾取回血）。</summary>
        /// <summary>v23：从天上随机掉落一个雨滴（拾取 +20 水壶弹药）。</summary>
        private float _rainTimer;

        /// <summary>
        /// v164：**自然降雨已停用** ✗
        ///
        /// 需求："雨滴修改为只有敌人会掉落，**不再自然掉落**" ✓
        ///
        /// ★ 为什么保留这个方法体而不是删掉它 ★
        /// 因为"停用"和"删除"的验证成本不同 ✗
        /// 停用 = 一条 `return` + 明确注释 ⇒ 一眼可查、可回退 ✓
        /// 删除 = 要同时清掉 `_rainTimer` 字段、`SpawnRain` 的调用链、
        ///        以及 `RainDropEvent` 的订阅 —— 而 `SpawnRain` **现在被敌人掉落复用了** ✓
        /// 所以删除是**错的**：那个方法还在用 ✓ 只有"天空落雨"这个**触发源**要停 ✓
        ///
        /// 雨滴现在唯一的来源是 `Cleanup()` 里敌人死亡的那一处 ✓
        /// </summary>
        private void TickRain(float dt)
        {
            return;   // ★ 自然降雨已停用 —— 见上方注释，不是遗漏 ★

            // 以下为旧版的"天空落雨"逻辑，保留以便回退 ✗
            // 若将来要恢复，删掉上面那行 `return;` 即可 ✓
#pragma warning disable CS0162
            if (Player == null || !Player.Alive) return;
            _rainTimer -= dt;
            if (_rainTimer > 0f) return;
            _rainTimer = Rng.Range(1.5f, 3f);   // v25：雨量大幅增加
            float a = Rng.Range(0f, 6.2831853f);
            float r = Rng.Range(2f, 9f);
            Vector2 rainPos = ClampToArena(Player.Pos + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                0.5f, Tuning.farm.arenaHalf);
            SpawnRain(rainPos);
#pragma warning restore CS0162
        }

        /// <summary>
        /// v167：**每个敌人死亡时掉落雨滴的概率** = 0.25。
        ///
        /// ★ 它现在是**水分的唯一来源**（自然降雨已停用，见 `TickRain`）★
        /// 所以这个常数直接决定：水壶能开几枪、几块地能活着 ✓
        ///
        /// ★ 算式（先取证再定值，两步分开算）★
        ///
        /// 第 1 步 · 每波击杀数（实测，不是估的）
        ///   无头回归 `DebugApi.RunRegression`（seed 20240607，Balanced）：
        ///   报告 Docs/reports/sim-20261016-194309.txt ＝ 击杀 21 / 波次 2 ⇒ **≈10.5 只/波** ✓
        ///   生成侧交叉验证：刷怪拍数 ≈ 0.3466×波长（interval 4→2 秒的积分 (D/2)·ln2）✓
        ///   每拍数量 = 2+⌊0.8W⌋+⌊2·progress⌋+⌊3.67√(W−1)⌋，实际被**击杀速率**卡住 ✓
        ///   ⇒ 取 **10~15 只/波**（前期）✓
        ///   ⚠️ 旧注释的"一次普通刷怪约 3~4 只"**不成立**（差约 3 倍）✓
        ///
        /// 第 2 步 · 期望掉落 = 击杀 × p（每具尸体**恰好掷一次**，雨滴无第二个来源）
        ///   0.15 ⇒ 1.58 滴/波 ｜ **0.25 ⇒ 2.63 滴/波** ｜ 0.30 ⇒ 3.15 滴/波
        ///   0.45 ⇒ 4.73 滴/波 ｜ 0.55 ⇒ 5.78 滴/波
        ///
        /// 第 3 步 · 用**需求侧**定值（这才是判据，不是掉落条数）
        ///   供给：每波开局 `min(100, +50)`；每滴 +20 ⇒
        ///     0.15 ⇒ 82 弹药/波 ✗ 低于浇水需求（见下）✓
        ///     0.25 ⇒ 50 + 2.63×20 = **103 ⇒ 撞 100 上限** ✓
        ///     0.30 / 0.45 / 0.55 同样撞上限 ⇒ **它们是同一档**，不是三档 ✓
        ///   支出：地面补水现在也扣弹药（见 `RunSession.WaterPerAmmo`）⇒
        ///     · 2 块地 ≈ 54 弹药/波 ⇒ 一边打一边浇**前期可行** ✓
        ///     · 4 块地 ≈ 108 弹药/波 ⇒ 与供给持平 ⇒ 地全保就打不动枪 ✓
        ///     · 8 块地 ≈ 216 弹药/波 ⇒ 供给的两倍 ⇒ **必须弃地** ✓
        ///   ⇒ 0.25 是"**刚好够把地浇住**"的那一档 ⇒ 取舍成立、又不卡死 ✓
        ///
        /// ★ v184：**0.25 → 0.10**（用户需求⑦："降低敌人掉落雨滴概率，突出自动恢复插件的作用"）★
        ///
        /// 为什么必须连同"自动恢复插件"一起看：
        /// 0.25 时每波击杀 10~15 只 ⇒ **2.6~3.8 滴/波**（每滴 +20 水 = 52~75 水/波），
        /// 再加上每波开局 `min(100, +50)` ⇒ 水量基本**不靠玩家决策就够用**，
        /// 于是"自充"（`can_autocharge`）这种"用插件换水量"的东西**没有存在的理由** ✗
        /// 0.10 时 ⇒ **1.0~1.5 滴/波**（20~30 水/波）⇒ 水量重新变成要规划的资源 ✓
        /// 这时自充的每秒回水才是"一个值得花插槽买的东西" ✓
        /// 同期自充从 5/秒 提到 **12/秒**（`plugins.json`），满水 100 需要 8.3 秒 ——
        /// 两条改动一起，才构成"少捡、多充"的取舍，而不是单纯变难 ✓
        /// ★★ v199：**0.10 → 0.05**（用户原话："提高水分掉落几率（每个敌人5%）"）★★
        ///
        /// ⚠️ **如实记一笔**：用户说的是"**提高**"，但表里改前的现值是 **10%**（v184 从 25% 降下来的），
        ///    5% 相对 10% 是**降低** ⇒ 本轮按用户给的**数字**（5%）执行，只把这件事写在这里，
        ///    不去猜他想说的是哪一个 ✓
        ///
        /// 顺带：雨滴现在有 **5 秒寿命**（见 `Pickup.RainLifeSeconds`）⇒
        /// 掉得少了、躺在地上的时间也短了，捡水这件事第一次变成要**主动跑过去**的动作 ✓
        ///
        /// ★★ v202：**0.05 → 0.10**（用户原话："调整敌人掉落雨滴概率为10%，5秒不拾取就会消失"）★★
        ///   5 秒寿命**不动**（`Pickup.RainLifeSeconds = 5f`，v199 加的，用户本轮明确要求保留）✓
        ///   期望掉落回到 v184 的 1.0~1.5 滴/波 → 换成 10% 即 **1.0~1.5 滴/波 → 1.0~1.5**：
        ///   `0.10 × 10~15 只 = 1.0~1.5 滴/波`（20~30 水/波）✓
        ///   ⚠️ v199 的注释把方向记成"用户说提高、而现值 10% ⇒ 5% 是降低" —— 本轮用户直接给
        ///      **10%**，所以 0.05 → 0.10 是**升高**，那条历史备注保留不动（它记的是 v199 当时的事）✓
        ///
        /// ★★ v209：**0.10 → 0.20**（用户原话："水滴掉落率修改为20%"）★★
        ///   只动这一个常数。**`RainLifeSeconds = 10f` 一个字没动** ——
        ///   10 秒是 v206（用户原话「水滴消失时间修改为10s」）定的寿命，与掉落率是两件事，
        ///   本轮既不跟着改、也没有被 20% 反推着改 ✓（v199→v206 那两次前科正是"两个数被一起动"，
        ///   所以这里单独记一笔：寿命仍是 10s，淡出窗口仍是 `RainFadeSeconds = 1.2f`）✓
        ///   期望掉落：每波 10~15 只 ⇒ `0.20 × 10~15 = 2.0~3.0 滴/波`（每滴 +20 水 ⇒ 40~60 水/波），
        ///   相对 v202 的 0.10（1.0~1.5 滴/波、20~30 水/波）**翻倍** ✓
        /// ⚠️ 自检 `v167：击杀会掉雨滴，概率 ≈ RainDropChancePerKill` 的**期望值与容差都引用本常量**
        ///   ⇒ 期望值自动跟随（不用改它），但容差 `±0.06` 的统计意义变了：
        ///   `Binomial(400, 0.20)` 的 σ = √(400×0.20×0.80) = 8 滴 ⇒ 比率 σ ≈ 0.020
        ///   ⇒ ±0.06 从 p=0.10 时的 ±4.0σ 收紧到 **±3.0σ**（假红 ≈ 0.27%），仍然够稳 ✓
        /// </summary>
        public const float RainDropChancePerKill = 0.20f;

        /// <summary>v23：生成一个雨滴拾取物。</summary>
        public void SpawnRain(Vector2 pos)
        {
            SpawnHeart(pos, 0f);   // 复用拾取物容器，标记为雨滴
            if (Pickups.Count > 0) Pickups[Pickups.Count - 1].IsRain = true;
            Bus.Publish(new RainDropEvent { Pos = pos });
        }
        public void SpawnHeart(Vector2 pos, float heal)
        {
            Pickups.Add(new Pickup { Pos = pos, IsHeart = true, Heal = heal });
        }

        // ★ T2：`World.FindPlantOnPlot(int)` **已删除** ✗ ★
        //   它按 `PlotIndex` 在 `Plants` 里找回植物实体 ✓
        //   两个调用点（`RunSession` 的"释放多余地块"与"铲除幼苗"）都已改成**只调 `plot.RemoveGrowing()`** ✓
        //   —— 因为 `FindPlantOnPlot` 在没有生产者之后**恒返回 null**，那两处的 `Kill()` 本来就是空转 ✓
        //   ⚠️ "一株植物 ↔ 一列地块"的对应关系**没有消失**：它在 `FarmGrid`（`ColumnHead` / `PlantCount`）里 ✓

        // ------------------------------------------------------------ 查询
        public EnemyActor NearestEnemyTo(Vector2 p, float maxRange)
        {
            EnemyActor best = null;
            float bestD = maxRange <= 0f ? float.MaxValue : maxRange * maxRange;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.IsDead) continue;
                float d = (e.Pos - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = e; }
            }
            return best;
        }

        /// <summary>按 id 找敌人（G 键锁定用）；找不到返回 null。</summary>
        public EnemyActor FindEnemyById(int id)
        {
            if (id < 0) return null;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.Id == id && !e.IsDead) return e;
            }
            return null;
        }

        public EnemyActor NearestEnemyToExcept(Vector2 p, float maxRange, EnemyActor except)
        {
            EnemyActor best = null;
            float bestD = maxRange * maxRange;
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.IsDead || e == except) continue;
                float d = (e.Pos - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = e; }
            }
            return best;
        }

        // ★ v172：`TauntPlantNear(from, radius)` 与 `SlowFieldFactor(pos)` 两个方法**已整条删除** ✗
        //
        // 为什么是"删方法"而不是"只删里面的分支" ✓
        //   两个方法体在删掉 `HasMechanic("Taunt") / ("SlowField")` 判据之后，
        //   剩下的就只是「遍历 `Plants`（恒空）并返回 null / 1f」—— 一个**纯空操作** ✓
        //   ⇒ 按本工程对死机制的判据（"删完只剩遍历空列表"），方法本身就该消失 ✓
        //   ⇒ 它们不是"待用的查询接口"，而是旧机制的两个消费点 ✓
        //
        // ⚠️ 调用者已同步处理：`Enemies.cs` 的 `GruntEnemy` / `ShooterEnemy` 原来先问
        //    "6 格内有没有嘲讽植物"，没有才打玩家 ⇒ 现在**直接打玩家** ✓
        //    （嘲讽恒为 null ⇒ 这条改动**不改变任何既有行为**，只是把死掉的前半段删掉 ✓）
        //
        // ⚠️ `SlowFieldFactor` 的调用者：全工程 0 个（查证方式：搜 `SlowFieldFactor`）✗
        //    它原本挂在敌人移速计算上，而那条线已经先被删了 ⇒ 它是**孤儿** ✓

        /// <summary>
        /// **敌人的目标**：玩家（活着的话）。
        ///
        /// ★ T2：它原来是"玩家 **或最近的植物**"（`playerOnly` 参数用来限定只看玩家）✗ ★
        ///   植物那一支已随 `PlantAlly` / `World.Plants` 删除 ⇒ 现在只剩玩家这一支 ✓
        ///   ★ 删之前按规矩**先读了两个调用点**（`Enemies.cs:137` GruntEnemy / `:152` ShooterEnemy）★：
        ///     两处都是 `Actor target = w.NearestAlly(Pos); if (target == null) return;` 然后追/射 ✓
        ///     两处都**不传** `playerOnly`（即走默认 false）⇒ 它们本来就接受"植物优先" ✓
        ///   ⇒ 而"没有植物"时的返回值一直是玩家 ⇒ 这次改动**不改变任何既有行为** ✓
        ///     （与 v172 删 `TauntPlantNear` 是同一条推理：嘲讽恒 null ⇒ 行为不变 ✓）
        ///   ⚠️ `playerOnly` 参数**一并删除**：两个调用点都用默认值 ⇒ 它从来没有被传过 true ✓
        ///     （删参数后 `w.NearestAlly(Pos)` 两个调用点**无需改动** ✓）
        /// </summary>
        public Actor NearestAlly(Vector2 p)
        {
            return Player != null && !Player.IsDead ? Player : null;
        }

        // ------------------------------------------------------------ 结算
        /// <summary>
        /// ★ v184：**最近一次伤害结算的完整读数** ★
        ///
        /// 需求③要求"敌人受到伤害 / 武器实际伤害 / UI 弹字"三处是**同一个值**。
        /// 三处各自的物理位置是：
        ///   · 敌人受到伤害 —— `HealthPool.ApplyDamage` 的返回值（`DamageResult.Dealt`）
        ///   · 武器实际伤害 —— `DamageRequest.RawValue`（武器侧算完的那个数，`DamageResult.RawValue`）
        ///   · UI 弹字       —— `Actor.OnHitLanded` 里的 `Mathf.RoundToInt(amount)`
        /// 后两者都由 `Resolve` 在一次调用里写出 ⇒ 读这一个字段就能把三处对齐验证 ✓
        /// 它**只写不读于玩法**（没有任何分支依赖它），因此不影响确定性 ✓
        /// </summary>
        public DamageResult LastHit;

        public DamageResult DealDamage(in DamageRequest req)
        {
            // ★ v186：**BOSS 入场期免伤** ★ —— 判据放在**结算层**，因为这里是"每一下伤害都必经"的主干 ✓
            //
            // 为什么不做在每一个攻击方（锄头 / 水壶 / 肥料 / 剑气 / 持续效果 / 弹道）：
            //   本工程有**六条**打伤害的路，各写一遍就是六份真相；
            //   而"入场期打不进去"是一条**对所有伤害来源一视同仁**的规则 ⇒ 它属于结算层 ✓
            // 为什么不做在 `HealthPool`：那是通用池子（玩家/护盾也在用），
            //   它不认识 BOSS，也不该认识 ✓
            //
            // 语义：返回 `default`（`Dealt = 0`）—— 与"闪避"读到的形状一致：
            //   调用方看"掉了多少血" ⇒ 0 ⇒ 弹字不弹、吸血不回、持续效果不叠 ✓
            // ⚠️ 只挡**入场那 1.2 秒**（`BossEnemy.EntranceSeconds`），之后一切照旧 ✓
            if (req.Target is BossEnemy enteringBoss && enteringBoss.InEntrance) return default;

            // ★★ v255 / v286：**灼印**的唯一取用点 ★★
            //   放在这里（唯一伤害闸门）而不是三条攻击路径各写一次：
            //   灼印的全部意义是"**任何来源**的下一次伤害都吃印" ⇒ 只有闸门能保证 ✓
            //   没印时只做一次字典查找并返回 1f ⇒ 没装这件插件时零副作用 ✓
            // ★ 入参是 `in DamageRequest`（只读）⇒ 这里持一份**局部可变副本**，
            //   改副本、用副本结算（结构体复制，零分配）✓
            //
            // ── ★★ v286：为什么拆成"看 → 结算 → 再决定摘不摘" ★★ ──
            // 用户报的「灼印与毒区 / 子爆 / 伏雷配合时不生效」，根因是改前**一步走完**：
            //   `ConsumeMark` 既读倍率又立刻 `Remove` ⇒ 谁先打到就谁吃掉。
            //   而肥料线里排在**下一次主爆之前**的恰好是两种量最小的伤害：
            //     · 毒区 tick（`PluginDotTable.Hit`，每秒一跳、量 = 这一炮 50%×层数）
            //     · 子爆（`BlastDamageAt(mainBlast:false)`，量 = 主爆 × 0.5）
            //   ⇒ 印记永远被最弱的一下用掉，主爆的弹字一点没变 ⇒ 玩家读成"不生效" ✓
            //
            // 现在三条规则，全部落在**这一个闸门**里（所以对全部伤害源一致）：
            //   ① **所有来源都吃倍率**（近战 / 激光 / 剑气 / 主爆 / 子爆 / 毒区 tick / 灼烧 tick）
            //      —— 它们一一都走本方法 ⇒ 全都读同一个 `MarkMultiplier` ✓
            //   ② **只有直接伤害才摘印**：调用方用 `DamageRequest.NoMarkConsume` 声明
            //      （持续伤害 tick 与子爆写 true；主爆 / 近战 / 激光 / 剑气保持默认 false）✓
            //   ③ **只有真打中了才摘印**（`r.Dealt > 0`）：被闪避 / 被格挡 / BOSS 入场免伤
            //      都不许白吃一颗印 —— 改前连"闪掉了"也照样删除 ✓
            DamageRequest local = req;
            EnemyActor marked = local.Target as EnemyActor;
            float markMul = marked != null ? PluginField.MarkMultiplier(marked.Id) : 1f;
            // ①：只乘、先不说；倍率为 1 时一个字节都不动（没装插件时零副作用）
            if (markMul != 1f) local.RawValue *= markMul;
            // ★★ v290：**7 个新插件里"闸门组"的唯一执行点**（感电 / 狂暴）★★
            //   位置与「灼印」逐字同源（就在上面那一行）：这里是"每一下伤害都必经"的主干 ⇒
            //   "按**目标血量**加伤"与"这一刀再电周围"只有放在这里才跨得住来源
            //   （近战 / 激光 / 剑气 / 水球 / 主爆 / 子爆 / 毒区 tick 一一都走本方法）✓
            //   ① 结算前：目标残血 ⇒ 改 `local.RawValue`；并记下"这一刀要电谁"
            //   ② 结算后：发那笔感电伤害（内部有重入闸 ⇒ 电到的人不会再电别人，深度恒为 1）
            //   没装这两件时两次调用各自只做一次空判 ⇒ 零副作用 ✓
            PluginExtra2.BeforeResolve(ref local);
            DamageResult r = DamageResolver.Resolve(local, this);
            LastHit = r;
            PluginExtra2.AfterResolve(local, r);
            // ② ③：这一路有资格摘印、并且这一下真的打进了血/盾里
            if (marked != null && markMul != 1f && !local.NoMarkConsume && r.Dealt > 0f)
            {
                PluginField.ConsumeMark(marked.Id);
                // 「印」弹字只在这一下**真的把印用掉**时发一次 —— 与改前同一个节奏
                // （一次印一条弹字）。改成"每种伤害都弹"就会让每秒一跳的毒区把弹字刷屏 ✗
                //
                // ★★ v308：文本 = 「印 -<这一下真正扣掉的血>」★★
                //   数字取 `r.Dealt`（= 上面 `DamageResolver.Resolve` 写出的"真正扣掉的量"），
                //   与旁边那条普通伤害字（印 `RawValue`）**故意不同口径**：
                //     · 普通那条回答"这一击算出来多少"（所以暴击时数字要变大）
                //     · 「印」这条回答"这一下真的扣掉多少"（用户对特殊弹字的判据）✓
                //   走到这里的条件里已经有 `r.Dealt > 0f` ⇒ `DamageLabel` 必定返回非 null；
                //   仍然按 null 处理（`if` 里发字）是为了让"零伤害不发"这条规则只有一处实现 ✓
                string markText = PluginHitFx.DamageLabel("印", r.Dealt);
                if (markText != null)
                {
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = marked.Pos, Text = markText, Kind = PopupKind.Crit, Scale = 1.0f,
                    });
                }
            }
            if (req.Target is PlayerActor && r.Dealt > 0f) DamagedByEnemies += r.Dealt;

            // ★ v172：v23「植物机制统一挂点」整段已删除 ✗ —— 它是本任务里最要紧的一处 ✓
            //
            // 删掉的是四条机制分支（全部以 `HasMechanic` 为判据、全部以 `PlantAlly` 为前置）：
            //   · `HealOnKill`（圣树守卫击杀 → 玩家 +1 生命）—— 连带 `lethal` 局部变量
            //   · `Thorns`（荆棘南瓜受击 → 反弹 50%）
            //   · `Chain`（星藤连锁闪电，弹 3 个敌人）—— 连带 `_chaining` 递归守卫
            //   · `DeathExplode`（炭花阵亡 → 范围内爆炸）
            //
            // ★ 为什么这四条必须删、而不是"留着反正跑不到"★
            //   这里是**结算层**：`DealDamage` 是每一下伤害都必经的主干 ✓
            //   把机制挂在这一层，等于把"植物参战"接在主干上 ✗
            //   ⇒ 只要 `Plants` 里出现**一个**元素，这四条立刻同时生效 ✓
            //   ⇒ 判据不是"它现在跑不跑"，而是"删完之后这条路还在不在" ✓
            //
            // ⚠️ `EnemyAttackFieldEvent` 结构**保留** ✓ —— 跳跃精英的落地冲击还在发它
            //    （`Enemies.cs` 的 `Impact()`），这里只是少了炭花那一个发布点 ✓
            return r;
        }

        /// <summary>
        /// ★ v191：**返回"实际到账"的金额**（而不是 void）★
        ///
        /// 乘区没变（`baseGold × Stats.Get(GoldMult)`，属性倍率的唯一消费点就在这里），
        /// 变的是**回调值**：以前调用方拿不到"乘完之后是多少"，
        /// 于是收割弹字只能印**乘之前**的基数 ⇒ 货币倍数 150% 时到账 30 却弹"金币+20" ✗
        /// 现在调用方直接印返回值 ⇒ 弹字与到账**必然是同一个数**（同一个变量，不是两次计算）✓
        ///
        /// `void` → `int` 对既有调用点（`Pickup.Tick` 等）是**源码兼容**的：忽略返回值的语句照旧编译 ✓
        /// </summary>
        public int AddGold(int baseGold)
        {
            // ★ v197：**结算点兜底**（`StatBlock.FloorOf` 已放开 `GoldMult` 的下限）★
            //   用户要求"其它数值可以为负数并造成负面效果"⇒ 货币倍数 −x 是合法的减益；
            //   但**到账量不能为负**：那会让 `GoldEarned` 变小、`Gold < price` 判定反转，
            //   把一个"减益"变成"买一次货反而加钱"的荒谬状态 ✗
            //   ⇒ 减益照旧生效（负倍数 = 这一茬**一分钱不给**），只是不会倒扣 ✓
            int g = Mathf.Max(0, Mathf.RoundToInt(baseGold * Player.Progress.Stats.Get(StatId.GoldMult)));
            GoldEarned += g;
            Bus.Publish(new GoldChangedEvent { Delta = g, Total = GoldEarned });
            return g;
        }

        /// <summary>
        /// ★ v191：**返回"实际到账"的经验**（理由同 `AddGold`）★
        /// 经验是 float（等级曲线容许小数）⇒ 返回 float；显示处自己取整 ✓
        /// </summary>
        public float AddExp(float baseExp)
        {
            // ★ v197：**结算点兜底**（同 `AddGold`）★
            //   负的 `ExpMult` 是合法减益 ⇒ 这一茬**一点经验都不长**；
            //   但**不允许负经验**：`PlayerProgress.AddExp` 会把 `Exp` 往负方向拉，
            //   而升级判定是 `Exp >= need` ⇒ 负经验只会让升级永远不发生（看着像卡死）✗
            float e = Mathf.Max(0f, baseExp * Player.Progress.Stats.Get(StatId.ExpMult));
            ExpEarned += e;
            if (Player.Progress.AddExp(e, out int levels) && levels > 0)
            {
                Farm.SetMaxPlots(Player.Progress.MaxPlots);
                Bus.Publish(new LevelUpEvent { Level = Player.Progress.Level, MaxPlots = Player.Progress.MaxPlots });
            }
            return e;
        }

        // ------------------------------------------------------------ 单帧推进
        public void Simulate(float dt)
        {
            Time += dt;

            // 0) v166：**田地按存活时间加排**（每 farm.rowGrowthSeconds 一排，封顶 farm.maxRows）。
            //    放在这里而不是 RunSession：`Time` 是本类累加的世界时间，
            //    只在世界真正推进时增长 ⇒ 暂停（商店/菜单）不算"存活"✓
            //    实现是"绝对时间的纯函数"（见 FarmGrid.SyncSurvival）⇒
            //    调用多少次、掉多少帧，结果都一样 ✓
            Farm.SyncSurvival(Time);

            // 1) 耕地：v23 自动生长（缺水停长；成熟后进入"待收获"，需锄头收获）
            for (int i = 0; i < Farm.Slots.Length; i++)
            {
                FarmPlot plot = Farm.Slots[i];
                if (plot.State != PlotState.Growing) continue;
                float speed = Mathf.Max(0.05f, Player != null ? Player.AttackSpeed : 1f);
                // ★ v166：WaterSave（铁根巨像）—— 搬到地块层，并且**实现它的字面语义** ★
                //
                // 作用域：从"全场任一植物 ⇒ 全场受益"改成**只看这一格的作物** ✓
                //   为什么读 `CropDef`：一列里只有列首登记 `SeedId`（1:1 契约），而 `CropDef` 每格都有 ✓
                //   为什么"逐格"等于"逐列"：一列 = 一株植物，同列每格 `CropDef` 相同 ✓
                //
                // ★ 语义修正（这条比作用域更关键）★
                //   旧实现：`TickGrowth2(dt * 2)` ⇒ 生长与漏水**一起** ×2
                //           ⇒ 每秒水耗**翻倍**、长熟总水量不变 ✗ **与名字 WaterSave 方向相反** ✗
                //   新实现：`TickGrowth2(dt, 0.5f)` ⇒ 生长不变、**该列水耗真的减半** ✓
                //   ⇒ 玩家有理由为"省水"而种它 ✓ 它才是一条真实的经济机制 ✓
                //
                // ★ v168：这里成为**成长阶段机制的唯一消费点** ★
                //   四个成长类机制全部在这三行里生效：
                //     `WaterCostScale`  —— WaterSave（漏水时钟 ×0.5）
                //     `GrowSpeedMult`   —— Haste（×1.25）/ ElderGrowth（随存活时间 +4%/分钟，≤+60%）
                //     `NeighborGrowSpeedMult` —— NeighborBoost（左右相邻列 ×1.12）
                //     `ThirstyGrowMult` —— ThirstTolerant（缺水时仍以 40% 速度生长）
                //   一律读 `plot.CropDef`（每格都有）而不是 `plot.SeedId`（只有列首有）✓
                SeedDef crop = plot.CropDef;
                // ★ v190：全局水耗系数 `farm.waterFactor` 的唯一消费点 ★
                //
                // 需求（用户③）："加快所有耕地水分消耗速度 2 倍" ⇒ 表里 `farm.waterFactor` = 2 ✓
                //
                // ★ 为什么乘在这里，而不是在 `FarmPlot.TickGrowth2` 里读表 ★
                //   `FarmPlot` 刻意**不依赖 `TableDatabase`**（见 `CropDef` 的注释）——
                //   地块是纯数据 + 纯推进，表只在外层（本类）被读 ✓
                //   ⇒ 系数在唯一调用点乘进 `waterCostScale`，`FarmPlot` 一个字不用改 ✓
                //
                // ★ 语义边界（刻意不乘的两处）★
                //   ① 水壶命中补水（`waterPerHit`）与弹药消耗（`WaterAmmo`）—— 那是"玩家的花销"，
                //      不是"地里的蒸发"，乘它等于顺手削弱水壶 ✗
                //   ② 生长推进（`TickGrowth(dt)`）—— 需求只说"水分消耗速度"，
                //      让作物同时长快一倍就是另一件事了 ✗
                //   `TickGrowth2` 里只有 `_drainAcc += dt × waterCostScale` 这一行受影响 ⇒ 只有水 ✓
                //
                // ★ 与 WaterSave 机制的叠加 ★
                //   `PlantMechanics.WaterCostScale` 给的是该品种的系数（省水 = 0.5）
                //   ⇒ 铁根巨像现在是 0.5 × 2 = 1.0，即"回到改动前的全场速度" ✓ 方向仍然正确（相对别人省一半）✓
                float waterCost = PlantMechanics.WaterCostScale(crop) * Mathf.Max(0f, Tuning.farm.waterFactor);
                float growSpeed = PlantMechanics.GrowSpeedMult(crop, Time)
                                  * PlantMechanics.NeighborGrowSpeedMult(Farm, plot)
                                  // ★ v197：大晴天（植物机制 4）⇒ 全场生长速度 ×1.10。
                                  //   乘在**这一个点**上，理由与上面 `waterCost` 那条完全相同：
                                  //   `FarmPlot` 不依赖任何机制解释器，系数只在唯一调用点乘进去 ✓
                                  //   天气不是晴天时 `GrowMult` 恒为 1.0 ⇒ 对既有玩法零影响 ✓
                                  * ProgMechanics.GrowMult
                                  // ★ v253：血勇（植物机制 6）—— 主人生命低于 40% 时该株生长 ×1.30。
                                  //   与上面三条同一处乘进去，理由相同：`FarmPlot` 不认识机制解释器 ✓
                                  * PlantMechanics.LowHpGrowMult(crop, Player != null ? Player.Health.HpPct : 0f);
                float thirstGrow = PlantMechanics.ThirstyGrowMult(crop);
                bool ripe = plot.TickGrowth2(dt * speed * growSpeed, waterCost, thirstGrow);
                if (ripe)
                {
                    Bus.Publish(new TreeMatureEvent { PlotIndex = plot.Index });
                    // v47：成熟提示——在土地旁边弹出
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = plot.World, Text = "成熟", Kind = PopupKind.PlantMature
                    });
                }
                // v47：缺水提示每次"从有水变成无水"只弹一次，补水后重置（见 FarmPlot.AddWater）
                else if (plot.Thirsty && !plot.ThirstyNotified)
                {
                    plot.ThirstyNotified = true;
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = plot.World, Text = "缺水", Kind = PopupKind.PlantThirst
                    });
                }
            }

            // 2) 玩家（移动 / 再生 / 冷却衰减 / 生命池与属性同步）
            if (Player != null && Player.Alive) Player.Tick(dt, this);

            // 3) 植物
            // ★ v173：原来这里那段"植物 tick"【已整体删除】✗ —— 连开关都不剩 ✓ ★
            //    v162 曾用一个 `GameplayMode.PlantsFight` 开关把"植物索敌 / 移动 / 攻击"停用 ✓
            //    但 v172 删掉那 9 条机制之后，开关后面**已经没有东西可以开关** ✓
            //    ⇒ v173 把开关与这个闸门**一起删除** ✓
            //    ⇒ "植物参战"不再是一个选项：它**不是被关掉，而是不存在** ✓
            //      （想回退也没有拉杆 ✓ 而机制本身在 v172 已无从重写 ✓）
            //    `Plants` 列表本身保留 —— 收割 / 成长 / 浇水仍在使用（整条下线见待办 T2）✓
            //    植物不索敌、不移动、不攻击 ⇒ 没有状态需要推进 ⇒ 这里不 tick 是正确的 ✓

            // 4) 敌人
            for (int i = 0; i < Enemies.Count; i++)
            {
                EnemyActor e = Enemies[i];
                if (e.Alive) e.Tick(dt, this);
            }

            TickRain(dt);
            // v166：`TickHealOrbs(dt)` 已删除 —— 光球链是死机制（`SpawnHealOrb` 无调用者、语义已废），
            //       详见本文件"治疗光球整条链"那段注释 ✓

            // 5) 弹道
            for (int i = 0; i < Projectiles.Count; i++)
            {
                Projectile p = Projectiles[i];
                if (!p.Dead) p.Tick(dt, this);
            }

            // 6) 掉落
            for (int i = 0; i < Pickups.Count; i++)
            {
                Pickup p = Pickups[i];
                if (!p.Dead) p.Tick(dt, this);
            }

            // 7) 清理与死亡结算
            Cleanup();
        }

        private void Cleanup()
        {
            for (int i = Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor e = Enemies[i];
                if (!e.IsDead) continue;
                e.Alive = false;
                KillCount++;
                if (!e.NoDrop)
                {
                    // ★ v167：**敌人不再掉经验球** ✗（v39 的 1~3 / 3~5 球整段删除）
                    //
                    // 需求："敌人只掉水" ✓
                    // 判据（新玩法的定义）：植物是金币与经验的**产出形式**
                    //                    ⇒ 金币与经验**只从收割来** ✓
                    // 发放点唯一：`HarvestPlot()` —— 金币 = 种子价/5、经验 = 种子价/10 ✓
                    //
                    // 所以这里**不能**再 SpawnPickup ✗
                    // 那等于给金币/经验开第二条来源 ⇒ 与"收割是唯一产出"直接冲突 ✓
                    //
                    // ⚠️ 副作用（预期，不是回归）：删除同时移除了
                    //    `Rng.RangeInt` ×2 与 `Rng.InsideCircle` ×orbs 的取数 ✓
                    //    ⇒ 同种子的跑局结果会变（RNG 流位置变了）✓
                    //    确定性承诺的是"同代码 + 同种子 ⇒ 同结果"，**不是**跨版本同结果 ✓
                    // v9：按"爱心掉落率"概率掉一颗爱心（拾取回血）
                    // v110：爱心掉落率是**等级**（0~3），要换算成实际概率再掷。
                    // 直接拿等级当概率用会得到"300% 掉落率"这种东西。
                    int heartLv = Mathf.RoundToInt(Player.Progress.Stats.Get(StatId.HeartDropRate));
                    float heartRate = TuningDef.HeartDropRateOf(heartLv);
                    if (heartRate > 0f && Rng.Chance(heartRate))
                        SpawnHeart(e.Pos + Rng.InsideCircle(0.6f), Mathf.Max(1f, Player.Progress.Stats.Get(StatId.HeartHeal)));

                    // ★ v167：**雨滴判定移进 `if (!e.NoDrop)` 里面** ★
                    //
                    // 原来它在那个 if 的**外面** ⇒ 被标 `NoDrop` 的敌人**照样掉雨** ✗
                    // 谁会被标 NoDrop：BOSS 出场清场（`EnemyActor.ClearField` 给满场小怪打标）✓
                    //   清场是**演出性清空**，规则上明确"不给掉落"✓
                    //   而雨滴就是掉落物 ⇒ 它必须一起吃这条规则 ✓
                    // 不改的后果：BOSS 一出场就白送一批水（每只 10%，一波 5~10 只 ≈ +0.5~1 滴）✗
                    //   （v184：概率已从 25% 降到 10%，见 `RainDropChancePerKill` 的说明）
                    //
                    // 设计意义：雨滴只从敌人身上掉 ⇒ 水分【必须靠打怪】✓
                    //   ⇒ "浇水"与"战斗"被强制绑在一起 ✓ 而水壶的弹药也是水 ⇒
                    //      **打怪 → 拿水 → 要么浇地、要么当弹药** ✓ 这才是核心取舍 ✓
                    // 概率与数量：沿用旧版拾取时的 +20 弹药 ✗ 只把【来源】从天空改成敌人 ✓
                    if (Rng.Chance(RainDropChancePerKill))
                        SpawnRain(e.Pos + Rng.InsideCircle(0.7f));
                }

                Bus.Publish(new EnemyKilledEvent { EnemyId = e.Def.Id, Pos = e.Pos, Gold = e.Def.Gold, Exp = e.Def.Exp });
                // ★ v253：猎杀亢奋（植物机制 4）—— 场上种了它才点火，效果走 `StatId.AtkMult` ✓
                ProgMechanics.OnEnemyKilled(this);
                Enemies.RemoveAt(i);
            }

            // ★ T2：**植物尸体清理**整段已删除 ✗ ★
            //   它做三件事（整列回空地 / 从列表移除 / 置 `Alive=false`），全部以 `Plants` 为状态根 ✓
            //   而现在：没有生产者 ⇒ 列表恒空 ⇒ 循环体从不执行 ⇒ 删它不改变行为 ✓
            //   ⚠️ 连带：`FarmPlot.OnPlantDied()`（`FarmGrid.cs:434`）**失去唯一调用者** ⇒ 现在是 0 调用者 ✗
            //      ★ 已【报告】而不是顺手删 ★ —— 它是 `FarmGrid` 的公开 API，
            //        且其实现与仍在用的 `RemoveGrowing()` 等价（都是 `ClearColumn` + `ResetToEmpty`）✓
            //      ⇒ 要不要留它属于"耕地清理"的地界，不在本轮"植物参战"的范围内 ✓
            for (int i = Projectiles.Count - 1; i >= 0; i--)
                if (Projectiles[i].Dead) Projectiles.RemoveAt(i);

            for (int i = Pickups.Count - 1; i >= 0; i--)
                if (Pickups[i].Dead) Pickups.RemoveAt(i);
        }

        /// <summary>
        /// ★ v186：**`DespawnAllEnemies` 被调用过几次**（只给探针读）★
        ///
        /// 需求里"部分波次出现异常清除所有敌人"要能**证明它没有在跑**，光说"这条代码不可达"不够 ✓
        /// 整个工程里"一次性把场上敌人整批拿走"只有本方法这一条路
        /// （BOSS 那处 `ClearField` 是另一条，v186 已删；`JumpToWave` 是调试入口）⇒
        /// 计这一个数就够回答"20 分钟里有没有发生过整场清除" ✓
        /// </summary>
        public int DespawnAllCalls;

        /// <summary>
        /// 强制结束波次用：清掉场上残留敌人（不给掉落，避免"挂机刷钱"）。
        /// 返回被清掉的数量。
        /// </summary>
        public int DespawnAllEnemies()
        {
            DespawnAllCalls++;
            int n = Enemies.Count;
            for (int i = 0; i < Enemies.Count; i++) Enemies[i].Alive = false;
            Enemies.Clear();
            for (int i = Projectiles.Count - 1; i >= 0; i--)
                if (Projectiles[i].OwnerTeam == Team.Enemy) Projectiles.RemoveAt(i);
            return n;
        }

        // ★ 2026-10：`SettleWaveEnd()` 已删除 ✗
        //
        // 它做三件事（场上掉落吸附入账 / 全体植物回血 30% / 玩家护盾填满），
        // 唯一的调用方是 `RunSession.TickCombat` 里那段"波末结算 → 商店/抽卡"——
        // 而那段在计时制下**永不触发**，2026-10 已整块删除 ✓
        // 于是它变成【0 调用者】：留着的唯一效果就是"看着像还有波末结算" ✗
        //
        // 注：`DespawnAllEnemies()`（上面那个）**仍在用** ✓ —— 分钟硬上限与"分钟走完清残敌"
        // 两条路都会调它，与回合概念无关（它按分钟收尾，不按波次）✓

        // ★ v165：`ResetFarmForNewWave()` 已删除 ✗
        //
        // 它的唯一调用方是 `RunSession.TickCombat` 里那句
        // `if (T.farm.resetOnWaveEnd) World.ResetFarmForNewWave();` ——
        // 也就是"每回合结束把全部植物清空、所有地块刷回未开垦"。
        //
        // 计时制下这就是**每分钟抹掉一次玩家的产出** ✗ 而需求是"植物是产出形式" ✓
        // 调用方与配置字段 `farm.resetOnWaveEnd` 一起删除，所以这里不留一个没人调用的方法。
        // （自检里那两条"回合结束清空植物 / 田地回到未开垦"的旧断言也一并删掉，
        //   换成了反向断言"跨分钟不再清空植物"——见 SelfCheck。）
    }

    // ★ T2：`PlantSpawnedEvent` 结构**已删除** ✗ ★
    //   它的唯一发布点是 `SpawnPlantFromPlot`（已删），唯一订阅点是 `RunSession.OnPlantSpawned`
    //   （已连订阅一并删除）⇒ 收发两边都死 ✓
    //   ⚠️ 与 `ExpOrbDroppedEvent` 的处置**不同**：那个是"只发不收"（订阅方在 RunSession 里，
    //      当时不能改 RunSession）⇒ 只能留结构体 ✓ 而本次授权了改 RunSession ⇒ 这次能收干净 ✓

    public struct GoldChangedEvent { public int Delta; public int Total; }
}
