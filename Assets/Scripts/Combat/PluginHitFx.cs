using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 插件「特殊伤害」的**弹字变体 + 命中特效**（本轮新增，独立文件）★
    ///
    /// ── 为什么单开一个文件，而不是改 `PopupStyle` ──
    ///   ① `PopupStyle`（`Actors.cs`）是"**哪一档弹字长什么样**"的唯一分派点，它按
    ///      `PopupKind`（掉血 / 回血 / 暴击 / 持续伤害 …）分档，**不认识"是哪个插件打的"** ✓
    ///      本轮要的恰恰是**同一档弹字（敌人掉血）按插件再分叉** ⇒ 维度不同，硬塞进去会
    ///      把那张表变成"种类 × 插件"的二维表，而插件是**动态装配**的（`WeaponModifiers`）✗
    ///   ② 命中特效与弹字共用**同一份颜色与同一份样式判定** ⇒ 放在一起才不会出现
    ///      "字是青色、火花是橙色"这种两套配色 ✓
    ///   ③ 侵入面最小：全工程只有**一处**调用本类（`RunSession.OnFloatingText` 的漏斗），
    ///      再加两处"这一下是谁打的"标签（剑气 / 吸血，见 `Tag` 的注释）✓
    ///
    /// ── 样式怎么跨层传递（关键设计，v282 已换过一次载体）──
    ///   弹字在逻辑层是 `RunSession.FloatingText`（只有 Pos/Text/Color/Kind/Scale/Ttl），
    ///   绘制在表现层 `ImGuiHud.DrawPopups`（IMGUI，`GUI.Label`）。
    ///   要让"数字弹跳 / 从大缩小 / 抖动 / 重影"这些**逐帧动画**发生，绘制侧必须知道样式。
    ///   ★ v282 做法：**加一个字段** `RunSession.FloatingText.Plugin` ★
    ///     · 生成侧 `Apply` 把判定出来的样式写进去；绘制侧直接读它
    ///     · 图标由 `IconOf(style)` 分派（`ImGuiHud.PopupIconTex` 一次性生成贴图并缓存）✓
    ///   ★ 为什么废掉了 v279 那个"用字面后缀字符带样式"的做法（`↗ ◎ / ↯ ♥`）★
    ///     ① 用户这一轮的原话：「剑气的弹字**取消 `/` 符号**，用一个剑加伤害的形式」
    ///        「以及回响等，**全部加入图标**」⇒ 那些字符必须从屏幕上消失 ✓
    ///     ② 那些字符之所以能显示，全靠**雅黑恰好有字形**：
    ///        emoji 平面（`⚡💧`）没有字形会画成豆腐块
    ///        （`ImGuiHud` 里 v188 那段注释已踩过这个坑）✗
    ///        换一个符号、换一个系统字体、换一个打包平台就会挂 ✓
    ///     ③ 加了图标之后，'字符 + 图标' 会同时出现 ⇒ 一条弹字两个符号 ✓
    ///   ⚠️ 保留下来的判据：**中文文案标签**（「弹射」「收束」「回响」「伏雷」「印」）
    ///      —— 它们由 `RunSession.PluginPostHit` / `PluginMech.Tick` / `PluginField.Tick` /
    ///      `Actors.DealDamage` 发出，**不在本轮可改范围**（别的任务在写）
    ///      ⇒ 这些标签继续当"样式线索"用（`StyleOfText`），只是**不再追加任何新字符** ✓
    ///
    /// ── 颜色从哪来（工程纪律）──
    ///   一律从插件外观规格 `FxSpec.Core` / `FxSpec.Rim`（`PluginFx.For`）**派生**，
    ///   不写死 RGB；只有"弹字必须比背景亮"这条**兜底**会掺白（见 `Derive`）✓
    /// </summary>
    public enum PluginPopupStyle
    {
        /// <summary>不是插件特殊伤害 ⇒ 走原来的 `PopupStyle` 外观，本类一个像素都不碰。</summary>
        None = 0,
        /// <summary>弹射（水壶 `ricochet`）：数字弹跳入场 + 偏青蓝 + 箭头图标</summary>
        Ricochet,
        /// <summary>收束（锄头 `focus`）：从大缩小 1.6→1.0 + 金 + 靶心图标</summary>
        Focus,
        /// <summary>剑气（锄头 `swordwave`）：斜切入场 + 白 + **剑图标**（v282：`/` 已取消）</summary>
        SwordWave,
        /// <summary>回响（锄头 `echo`）：延后一次重影 + 紫灰 + 波纹图标</summary>
        Echo,
        /// <summary>伏雷（肥料 `mine`）：抖动 0.12s + 电蓝 + 闪电图标</summary>
        Chain,
        /// <summary>灼印（肥料 `mark`）：偏橙红 + 上浮更快 + 星火图标</summary>
        Mark,
        /// <summary>吸血（锄头 `lifesteal`）：偏猩红 + 心形图标 + 同一帧一个小号绿回复字</summary>
        Lifesteal,
        // ═════════ ★ v290：本批 7 件里要特殊弹字的三件 ═════════
        //   另外四件（狂暴 / 巨型 / 锋利 / 黑洞）**发的是普通数字弹字** ——
        //   它们改的是数值与形状，不是“另一种伤害”；给它们加样式反而会让
        //   “这一下到底是主伤害还是派生伤害”读不出来 ✗
        // ═══════════════════════════════════════════════════════
        /// <summary>**感电**（锄头 `hoe_shock`）：抖动入场 + 电青 + **两道分叉闪电图标**</summary>
        Shock,
        /// <summary>**污水**（水壶 `can_sewer`）：向下沉 + 沼绿 + **污水溅射图标**</summary>
        Sewer,
        /// <summary>**海浪**（水壶 `can_wave`）：上浮 + 海蓝 + **三道水纹图标**</summary>
        Wave,
        // ═════════ ★ v304：两档**纯几何**样式（用户需求③④）═════════
        //   ⚠️ 这两档**不会**出现在任何弹字上（`StyleOfText` / `IconOf` / `Apply` 都不产出它们），
        //      它们只是把"效果层"接上 `PluginHitFx` 已有的那条 `LineRenderer` 池 ——
        //      那条池本来就是画折线/线段的地方（`Chain` 的折线闪电已经在用），
        //      为两条连线再开一套池 = 两份池账、两处泄漏风险 ✗
        // ═══════════════════════════════════════════════════════════
        /// <summary>**感电的电流弹射**（锄头 `hoe_shock`）：主目标 → 每个被电到的敌人，5~7 段抖动折线</summary>
        ShockArc,
        /// <summary>**水壶弹射的小激光**（水壶 `can_ricochet`）：原目标 → 弹射目标，比主水柱细得多的亮线</summary>
        RicochetLaser,
    }

    /// <summary>
    /// 插件特殊伤害的弹字变体与命中特效。**无状态入口 + 一个对象池**。
    /// 运行期**不 Instantiate / 不 Destroy**（池在第一次出特效时一次建好，之后只复用）。
    /// </summary>
    public static class PluginHitFx
    {
        /// <summary>
        /// ★★ **全部弹字的统一字号基准倍率** —— 当前值 **0.85**（= "正常大小" × 0.85）★★
        ///
        /// ── 为什么只有这一个数 ──
        ///   弹字的屏幕字号 = `tuning.ui.popupFontSize`(26) × **本值** × `FloatingText.Scale`
        ///   （见 `ImGuiHud.DrawPopups` 的 `baseSize` 那一行）。
        ///   `FloatingText.Scale` 里已经含"按种类/按插件/按数值"的逐档倍率
        ///   （`PopupStyle.ScaleOf` × `PluginHitFx.ScaleOf`），那些是**相对关系**、不该动；
        ///   要"整体缩一点 / 缩回来"就必须乘在这里 —— 乘进去只改一处，
        ///   所有弹字（普通伤害 / 暴击 / 持续伤害 / 回血 / 金币 / 经验 / 植物 / bomb /
        ///   玩家受击 / 插件特殊伤害 / 训练场）**同时**等比变化，相对大小关系一个都不变 ✓
        ///   全工程**只有** `ImGuiHud.DrawPopups` 一处读本值 ⇒ 只要弹字走那条 `GUI.Label`
        ///   路径就必然吃到它（世界坐标投影 -> `WorldToHud` -> 这里），不存在第二套字号口径 ✓
        ///
        /// ── 取值史（每一轮都**只改这一个数**，改法从不逐个弹字去调）──
        ///   · **1.00** —— 最初的"正常"基准：普通伤害 = `26 × 1.00` = **26 号**
        ///   · **0.85** —— v304 首次统一缩小：普通 **22 号**
        ///   · **0.72** —— v305「所有弹字大小减小」：普通 **19 号**
        ///   · **0.504**（= 0.72 × 0.70）—— v308「按比例减小 30%」：普通 **13 号**
        ///     ⚠️ 这一档**压过头了**：普通字 13 号已是 1080p 下的下限附近，
        ///     插件里最小的几档（回响 / 感电 / 吸血那行小号绿字）掉到 10~11 号 ✗
        ///   · ★ **0.85**（回退并重定基准）★ 用户原话：
        ///       「所有弹字恢复正常大小，然后以原本正常的大小标准，所有弹字大小降低 15%」
        ///     ⇒ 这句是**两步**：
        ///        ① "恢复正常大小" = 先回到 1.00 那档的**字面量 26 号**（不是回到 0.72）
        ///        ② "以正常标准降低 15%" = 在 26 号之上乘 **0.85**
        ///     ⇒ 本值 = **0.85** ⇒ 普通伤害 = `26 × 0.85 = 22.1` ⇒ **22 号** ✓
        ///     ⚠️ 数值上它恰好等于"历史上那版首版的 0.85"，但**口径不同**：
        ///       这一轮它是"正常 ×0.85"的绝对定义，不是"从 0.504 往上调一格" ✓
        ///   · **0.7225** —— 用户原话：「所有弹字大小整体减小 15%」
        ///     ⇒ 在**当前基准 0.85** 之上再乘 **0.85** ⇒ `0.85 × 0.85 = 0.7225` ✓
        ///     ⇒ 普通伤害 = `26 × 0.7225 = 18.785` ⇒ **19 号**（旧 22 号 ⇒ −13.6%，
        ///       差额来自 `RoundToInt` 的取整；未取整的比值正好是 ×0.85）
        ///     ⚠️ 为什么是"乘在 0.85 上"而不是"改成 0.85 − 0.15"：
        ///       "减小 15%" 是**相对当前**的百分比，不是减 15 个绝对百分点 ——
        ///       减绝对值得 0.70（−17.6%），与用户说的 15% 对不上 ✓
        ///     ⚠️ 本轮用户同时还要求"收束/印/伏雷/bomb 四档额外缩减但仍大于其它弹字"
        ///       ⇒ 那四档落在 `ScaleOf`（**相对**倍率），与这里的**绝对**基准分属两个数 ✓
        ///   · ★ **0.769**（当前）★ 用户原话：「字号修改，bomb 维持，普通暴击维持，伏雷、印、收束、统一 30，
        ///     金币、经验、缺水、普通伤害 20，持续伤害 17」—— 用户给的是**目标像素**，反解本基准 ✓
        ///     取点：让"普通伤害 = 20px"（它只吃本基准 × 1.00）⇒ `26 × 0.769 = 19.994` ⇒ 19/20 号取整得 **20** ✓
        ///     ⚠️ 为什么是 0.769 而不是 20/26 = 0.7692…：0.769 × 26 = 19.994 与 20 的差 0.006px，
        ///       远小于 `RoundToInt` 的半个像素；写成 0.7692 也一样出 20，但 0.769 更短且够用 ✓
        ///     ⇒ 其余各档随本基准一起小幅变化（30 号那三档已在 `ScaleOf` 里按乘积反解过）✓
        ///   · **0.769 保持不变** —— 之后几轮用户改的都是**单档目标像素**（暴击 / bomb / 伏雷 / 印 / 收束），
        ///     那些落在 `PopupStyle.ScaleOf` 与下面的 `ScaleOf`（**相对**倍率）上，本基准一个字没动 ✓
        ///     当前口径（基准 19.994）：普通伤害 20px、暴击 25px、bomb 30px、伏雷/印/收束 20px ✓
        ///
        /// ── 与"收束回归正常弹字大小"那条的分工（别混成一个数）──
        ///   "收束"（`PluginPopupStyle.Focus`）的相对倍率是**相对关系**的事
        ///   （见下面的 `ScaleOf`）；本值是**绝对基准**。两个要求落在两个不同的数上，
        ///   改一个不替另一个完成 ✓
        ///
        /// ⚠️ 图标尺寸（`iconPx`）与描边宽度都从 `drawSize` 派生 ⇒ 它们**自动跟着缩/放**，
        ///   不需要第二个数（有第二个数就会出现"字小了、图标没小"的分家）✓
        /// </summary>
        public const float PopupFontScale = 0.769f;

        // ══════════════════════════════════════════════════════════════════
        //  ① 样式识别：后缀（生成侧写入）与文案（既有发布点已带的标签）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v282：**这一档样式在数字左边配哪个图标**（取代旧的 `SuffixOf` 字面后缀）★
        ///
        /// 唯一的分派点 —— `ImGuiHud.DrawPopups` 只问这一个函数，
        /// 不在绘制代码里写 `if (style == XXX)`；加第八种插件时只改这里 ✓
        /// 返回 `PopupIcon`（= `Actors.cs` 那张表）而不是另开一个枚举：
        /// 纹理缓存/生成器/染色/描边**全部复用** `PopupIconTex` 那一套
        /// ⇒ 不可能出现"插件图标有描边、DoT 图标没描边"这种两套画法 ✓
        /// </summary>
        public static PopupIcon IconOf(PluginPopupStyle s)
        {
            switch (s)
            {
                case PluginPopupStyle.Ricochet: return PopupIcon.Arrow;    // 弹射 → 箭头
                case PluginPopupStyle.Focus: return PopupIcon.Target;      // 收束 → 靶心
                case PluginPopupStyle.SwordWave: return PopupIcon.Sword;   // 剑气 → 剑
                case PluginPopupStyle.Echo: return PopupIcon.Echo;         // 回响 → 声波
                case PluginPopupStyle.Chain: return PopupIcon.Bolt;        // 伏雷 → 闪电
                case PluginPopupStyle.Mark: return PopupIcon.Spark;        // 灼印 → 星火
                case PluginPopupStyle.Lifesteal: return PopupIcon.Heart;   // 吸血 → 心形
                // ★ v290：本批三档（均为 11×11 位掩码，见 `ImGuiHud.PopupIconTex`）
                case PluginPopupStyle.Shock: return PopupIcon.ShockArc;     // 感电 → 分叉闪电
                case PluginPopupStyle.Sewer: return PopupIcon.Sewage;       // 污水 → 污水溅射
                case PluginPopupStyle.Wave: return PopupIcon.WaveRing;      // 海浪 → 水纹
                default: return PopupIcon.None;
            }
        }

        /// <summary>
        /// 文案 → 样式。**生成侧与绘制侧共用这一个函数**（唯一的样式判定点）。
        ///
        /// 先查后缀（本类自己加的，最权威），再查既有发布点自带的中文标签：
        ///   · 「弹射」 `RunSession.PluginPostHit`（弹射那一下）
        ///   · 「收束」 同上（收束处决）
        ///   · 「回响」 `PluginMech.Tick`（延迟结算）
        ///   · 「伏雷」/「伏雷！」 `PluginField.Tick`
        ///   · 「印」    `World.DealDamage` 的灼印取用点
        /// ★ 剑气与吸血**没有文案标签**（它们印的是纯数字 `-12`）⇒ 由结算处立 `Tag` 补上
        ///   然后本函数读到的是**已经加了后缀**的文本 ✓
        ///
        /// ★ v308：这些标签后面现在带一个数值（`DamageLabel` 拼出的 `收束 -37.5` /
        ///   `伏雷！ -27` / `印 -18！`）⇒ 本函数只判**开头**（`StartsWith` / 首字符），
        ///   与数值、与漏斗补的叹号**都无关** ✓ 加数值不会让任何一档样式失效。
        /// </summary>
        public static PluginPopupStyle StyleOfText(string text)
        {
            if (string.IsNullOrEmpty(text)) return PluginPopupStyle.None;
            // ── 既有中文标签 ──
            if (text.StartsWith("弹射")) return PluginPopupStyle.Ricochet;
            if (text.StartsWith("收束")) return PluginPopupStyle.Focus;
            if (text.StartsWith("回响")) return PluginPopupStyle.Echo;
            if (text.StartsWith("伏雷")) return PluginPopupStyle.Chain;
            // ★ v280 修：灼印的标签**永远不会**是裸的 "印" ★
            //   `Actors.DealDamage` 发的是 `Text = "印"`、`Kind = PopupKind.Crit`，
            //   而漏斗 `RunSession.OnFloatingText` 会把它过一遍 `PopupStyle.TextOf`，
            //   那里对 `Crit` 这一档**补一个「！」**（`Actors.cs:290` 的 `WantsBang`）⇒
            //   到达本函数时文本是 "印！"，于是旧写法 `text == "印"` **恒为假** ✗
            //   后果：灼印的弹字变体（橙红 / 额外上浮）与「5 颗小火星」**一次都没跑过**，
            //   而且本类的池化路径整条是死路（见 `Spawn` 的注释）。
            //   判据改成"首个字符"（「印」是唯一以它开头的标签），与后缀/补标点无关 ✓
            if (text[0] == '印') return PluginPopupStyle.Mark;
            // ★ v290：本批三个标签。它们都由 `PluginExtra2` 直接发（“派生伤害”的那一下），
            //   不拼接任何后缀字符 —— 与“取消 `/⇗ ◎` 那一批字面后缀”同一条纪律 ✓
            if (text.StartsWith("感电")) return PluginPopupStyle.Shock;
            if (text.StartsWith("污水")) return PluginPopupStyle.Sewer;
            if (text.StartsWith("海浪")) return PluginPopupStyle.Wave;
            return PluginPopupStyle.None;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ①-b ★★ v308：特殊伤害弹字的**统一文本构造**（唯一一处）★★
        //
        //  用户原话：「海浪、污水等所有特殊伤害弹字，统一后续必须增加实际造成的伤害数值」
        //  ⇒ 改前那 8 个标签（污水 / 海浪 / 感电 / 弹射 / 收束 / 回响 / 伏雷 / 印）**只有名字**，
        //    玩家看得见"这一下是什么"，看不见"打了多少" ✗
        //
        //  ── 统一格式：`<名字> -<数值>`（例：`污水 -18` / `收束 -37.5`）──
        //    为什么带一个 `-` 前缀，而不是 `污水 18` / `污水·18`：
        //      本作**所有**伤害弹字都印成 `-4.55`（`Actor.Popup` 的 `"-" + …`），
        //      `-` 在这里已经是一个**玩家学过的语义**（"从我这儿扣掉的"）✓
        //      名字后面直接跟同一个记号 ⇒ 标签与它旁边那条普通伤害字读起来是**同一族**，
        //      不需要玩家再学一套"插件伤害不带减号"的例外 ✗
        //
        //  ── 数值口径：`DamageResult.Dealt`（**真正扣掉的血/盾量**），不是 `RawValue` ──
        //    用户明确要求"是这一下真实掉的血，不是这一刀的伤害"（扣掉护盾/减伤/闪避之后）✓
        //    ⚠️ 两者在**残血敌人**身上会不一样：普通伤害弹字印的是 `RawValue`（`Actor.OnHitLanded`
        //      的 v187 决定，为的是"暴击时数字要变大"），而这些标签印的是 `Dealt`
        //      ⇒ 敌人只剩 2.8 血时，屏幕上会同时出现 `-8`（普通那条）与 `收束 -2.8`（标签那条）。
        //      这不是矛盾：前者是"这一击算出来多少"，后者是"这一下真的扣掉多少" ✓
        //
        //  ── 被闪避 / 零伤害 ⇒ **不发这条弹字**（返回 null，调用方据此跳过）──
        //    用户原话："被闪避（Dealt=0）就不要发这条弹字（没造成伤害，弹一条 0 是噪音）"✓
        //    门槛直接复用 `Actor.PopupMinAmount`（0.05）：与普通伤害弹字**同一个门槛**，
        //    同时保住"不可能印出 `-0`"这条机器保证（`0.##` 把 |x| < 0.005 印成 `0`，
        //    而 0.05 比它大一个数量级）✓
        //
        //  ── 每帧分配 ≈ 0 ──
        //    本函数**只在发布那一刻调一次**（结算点），字符串拼完就存进 `FloatingText.Text`；
        //    绘制侧（`ImGuiHud.DrawPopups`）逐帧只读这个已存好的字符串，**绝不逐帧拼** ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 特殊伤害弹字的统一文本：`名字 + " -" + Dealt`（例如 `污水 -18`）。
        /// 返回 <c>null</c> 表示**这一下没造成伤害**（闪避 / 入场免伤 / 0 点伤害）⇒ 调用方不要发。
        /// </summary>
        /// <param name="name">标签（污水 / 海浪 / 感电 / 弹射 / 收束 / 回响 / 伏雷 / 印）</param>
        /// <param name="dealt">这一下**真正扣掉**的血/盾量（`DamageResult.Dealt`）</param>
        public static string DamageLabel(string name, float dealt)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // `NaN` 也在这里被挡掉（`!(NaN >= x)` 为真）⇒ 一个坏数据不会印出 "污水 -NaN"
            if (!(dealt >= Actor.PopupMinAmount)) return null;
            return name + " -" + dealt.ToString("0.##",
                                               System.Globalization.CultureInfo.InvariantCulture);
        }

        // ══════════════════════════════════════════════════════════════════
        //  ② 标签（"这一下是谁打的"）—— 给**没有文案标签**的两种伤害用
        //
        //  ★ 为什么需要它 ★
        //    剑气的伤害是 `World.DealDamage` 打到敌人身上，弹字由 `Actor.OnHitLanded`
        //    发出来的 **纯数字** `-5.5`，与普通近战一模一样 ⇒ 从文案上**无法分辨** ✗
        //    吸血的回血弹字同理（`HealthPool.Heal` 发的是纯数字）。
        //  ⇒ 在结算处立一个一次性标签，漏斗消费一次就清掉。
        //  ★ 与 `DotPopupHint`（`PluginDots.cs`）同一套做法与同一条纪律 ★
        //    ① 只在**同一个调用栈**内有效：结算 → 弹字发布 → 漏斗消费，全同步 ✓
        //    ② `Clear` 放在 `finally`：任何提前返回 / 异常都不会把标签漏给下一条弹字 ✓
        //       （漏了就是"普通弹字被染成剑气的白字"—— 最难查的一类串味）
        // ══════════════════════════════════════════════════════════════════

        private static PluginPopupStyle _tag = PluginPopupStyle.None;

        /// <summary>立标签：接下来**一条**弹字按这个样式处理。</summary>
        public static void Tag(PluginPopupStyle s) { _tag = s; }

        /// <summary>清标签（放在 `finally` 里）。</summary>
        public static void Clear() { _tag = PluginPopupStyle.None; }

        private static PluginPopupStyle TakeTag()
        {
            PluginPopupStyle s = _tag;
            _tag = PluginPopupStyle.None;
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ③ 唯一入口：改弹字 + 生成命中特效
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 让一条弹字变成"插件特殊伤害"的样子，并在命中点放一发小特效。
        /// 不是特殊伤害时**立即返回**，一个字段都不改（既有弹字逐位不变 ✓）。
        /// </summary>
        /// <param name="ft">漏斗刚建好的那条弹字（引用类型，就地改）</param>
        /// <param name="pos">命中点（世界坐标 XZ；Y 由表现层自己取高度）</param>
        /// <param name="s">会话（读 `Goods` 算插件外观规格；可为 null）</param>
        /// <param name="sink">弹字队列（"吸血那一发小号绿字"往这里追加一条）</param>
        public static void Apply(RunSession.FloatingText ft, Vector2 pos, RunSession s,
                                 List<RunSession.FloatingText> sink)
        {
            if (ft == null) return;
            // ① 先看标签（剑气 / 吸血），再看文案（其余五种）
            PluginPopupStyle style = TakeTag();
            if (style == PluginPopupStyle.None) style = StyleOfText(ft.Text);
            if (style == PluginPopupStyle.None) return;

            // ② 插件外观规格 —— 颜色的唯一来源
            FxSpec fx = SpecFor(style, s);

            // ③ 吸血：红字带心形图标，同一帧再补一条**小号绿色回复字**
            //    为什么是"再补一条"而不是把回血字直接改成红的 ✗
            //      回血字（`PopupKind.Heal`）的绿是**全局约定**（`PopupStyle.ColorOf`），
            //      改成红的就等于在漏斗里偷偷改了回血的语义 ⇒ 以后没人敢信那个绿 ✓
            if (style == PluginPopupStyle.Lifesteal && ft.Kind == PopupKind.Heal)
            {
                Color healGreen = ft.Color;          // 原回血绿（`PopupStyle.ColorOf(PopupKind.Heal)`）
                if (sink != null)
                {
                    RunSession.FloatingText green = new RunSession.FloatingText
                    {
                        Pos = ft.Pos,
                        Text = ft.Text,                    // ★ v282：不再拼后缀字符，图标由 `Plugin` 带
                        Kind = PopupKind.Heal,
                        Color = healGreen,                 // ★ 这条**保持绿**：它是"回复"那一半
                        Ttl = ft.Ttl,
                        Total = ft.Total,
                        Scale = ft.Scale * 0.80f,          // "小号"
                        Plugin = style,                    // ★ 心形图标（与红字同一个样式）
                    };
                    sink.Add(green);
                }
            }

            // ④ ★★ v282：**样式写在字段上，文本一个字都不加** ★★
            //   改前这里是 `ft.Text += SuffixOf(style)`（`↗ ◎ / ↯ ♥`）——
            //   用户这一轮明确要求取消（剑气那条 ` / ` 是他点名的）。
            //   现在文本保持发射端的原样（纯数字 `-5.5` 或既有中文标签），
            //   图标由绘制侧读 `ft.Plugin` → `IconOf` → 硬币/箭头/靶心/剑/闪电/心 贴图 ✓
            ft.Plugin = style;

            // ⑤ 颜色与字号 —— 从 `fx.Core/fx.Rim` 派生
            ft.Color = Derive(ft.Color, in fx, style, out float _);
            ft.Scale = ScaleOf(style) * (ft.Scale > 0f ? ft.Scale : 1f);

            // ⑥ 命中特效（≤12 个渲染体、≤0.35s、走池）
            Spawn(style, pos, in fx, s);
        }

        /// <summary>
        /// 各档弹字的基础字号倍率（在 `PopupStyle.ScaleOf` 的基准上再乘）。
        ///
        /// ★★ v3xx：**收束 / 印 / 伏雷 三档（＋`PopupKind.Bomb`，见 `Actors.PopupStyle.ScaleOf`）
        ///   额外缩减，但四档仍然都大于普通弹字** ★★
        ///   用户原话：「收束、印、伏雷、bomb 这几个的弹字大小缩减，但大于其它弹字」。
        ///
        /// ── 判据是**上屏的实际乘积**，不是本表里的单个数字 ──
        ///   `size = 26 × PopupFontScale(0.769) × PopupStyle.ScaleOf(kind) × (发布侧 Scale) × 本表`
        ///   ⇒ 四档的"实际倍率"（= 除基准与 26 之外的全部因子之积，上一轮的对照值）：
        ///     档位        组成                                                改前    改后
        ///     bomb        `Bomb`                                              2.7500  2.2000
        ///     伏雷(踩中)  `Crit × 发布 1.15 × Chain`（`PluginField.TickMines`） 2.4035  1.9665
        ///     印          `Crit × 发布 1.00 × Mark`（`Actors.DealDamage`）     1.9000  1.6150
        ///     收束        `Crit × 发布 0.90 × Focus`（`RunSession.PluginPostHit`）1.7100 1.3680
        ///     普通        `DamageToEnemy`（= 1.00，对照）                      1.0000  1.0000
        ///   ⇒ 顺序 `bomb > 伏雷 > 印 > 收束 > 普通` 逐条不变，且四档都**小于各自旧值** ✓
        ///
        /// ── 为什么本表取 0.80 / 0.90 / 0.85（而不是把四档都写成同一个数）──
        ///   每一档的"旧值"本来就不同（2.75 / 2.40 / 1.90 / 1.71），若四档都乘同一个系数，
        ///   高档位（bomb）的绝对减幅会过大、低档位（收束）几乎看不出变化。
        ///   这里让**每一档各自缩到旧值的 0.80~0.85**：
        ///     · bomb 2.75 → 2.20（×0.80）
        ///     · 伏雷 Chain 1.10 → **0.90** ⇒ 2.4035 × 0.818 = 1.9665
        ///     · 印   Mark 1.00 → **0.85** ⇒ 1.90 × 0.85  = 1.6150
        ///     · 收束 Focus 1.00 → **0.80** ⇒ 1.71 × 0.80  = 1.3680
        ///   ⚠️ **收束不能再走"1.08"那条路**：`收束` 的 Kind 是 `PopupKind.Crit`（1.90）、
        ///     发布侧还乘 0.90 ⇒ 它在**上屏口径上从来就不等于普通弹字**（旧值 1.71 = 普通的 1.71 倍）。
        ///     若按"介于 1.00 与 1.15 之间"取 1.08，它反而会从 1.71 **涨到** 1.8468（是放大，不是缩减），
        ///     并且会顶到「印」（0.85 时为 1.615）之上 ⇒ 撞掉 `bomb > 伏雷 > 印 > 收束` 的顺序 ✗
        ///     ⇒ 用户那条"1.08"是按插件表里的单个数说的，与本表的乘积口径不同；这里按**乘积**取 0.80 ✓
        ///   ⚠️ `SwordWave / Echo / Shock / Sewer / Wave / Ricochet / Lifesteal` 一个字没动 ✓
        ///
        /// ── ★ 本轮（用户给的仍是**目标像素**，但目标降了）★ ──
        ///   用户原话：「伏雷、收束、印的大小也下降到 20；暴击 25，bomb 30」
        ///   ⇒ 三者上屏值都必须是 **1.0003**（= `20 / 19.994`），而 `PopupStyle.Crit` 同时降到 1.25
        ///     ⇒ 三段相乘的**第一段变了** ⇒ 本表必须按新乘积**重新反解**（不能沿用上一轮的 0.7246/0.8333/0.9259）：
        ///     档位    `PopupStyle.Crit` × 发布侧 × 本表        上屏乘积        反解算式                 本表
        ///     伏雷    `1.25 × 1.15 × 0.696`                    1.0005 ⇒ 20px  `1.0003 / (1.25×1.15)`  0.696
        ///     印      `1.25 × 1.00 × 0.800`                    1.0000 ⇒ 20px  `1.0003 / (1.25×1.00)`  0.800
        ///     收束    `1.25 × 0.90 × 0.889`                    1.0001 ⇒ 20px  `1.0003 / (1.25×0.90)`  0.889
        ///   （发布侧三处本轮**逐处核对过、一个数都没动**：
        ///     伏雷 `PluginField.TickMines` 的 `stepped ? 1.15f : 0.9f`、
        ///     印 `Actors.World.DealDamage` 印那一支的 `Scale = 1.0f`、
        ///     收束 `RunSession.PluginPostHit` 收束那一支的 `Scale = 0.9f`）✓
        ///   ⇒ 本表**不能直接写 1.0003**（那会把发布侧系数重复计入）；反解值如上 ✓
        ///   ⚠️ 若将来改了上面三处发布侧系数或 `PopupStyle.Crit`，本表必须按**新的乘积**重新反解，
        ///     否则三条会各自漂到 20px 之外（判据恒为 `PopupStyle.Crit × 发布侧 × 本表 = 1.0003`）✓
        ///   ⇒ 伏雷/印/收束 从上一轮的"明显大于其它弹字"变成"**正好等于普通弹字** 20"——
        ///     这正是用户给的目标值，不是口径丢失 ✓
        ///
        /// ── ★★ 本轮：**整个层级按用户给的那三个数拉平**（用户："字体按照我的要求改"）★★ ──
        ///   上一轮报上去的三处翻转（升级 26 / 大伤害 27 / 成熟 25 反超暴击 25）本轮全部抹平，
        ///   判据 = 用户给的那三个数**就是权威**，其它档一律服从：
        ///     · bomb        = 30px（**唯一最大**，`PopupStyle.Bomb = 1.50`，本表不参与）
        ///     · 普通暴击    = 25px（**唯一第二**，`PopupStyle.Crit = 1.25`，本表不参与）
        ///     · 其余一切    ≤ 20px（= 普通伤害那一档）
        ///   ⚠️ 本表的三个反解值（0.889 / 0.696 / 0.800）**一个都没动** ——
        ///     它们的判据 `PopupStyle.Crit × 发布侧 × 本表 = 1.0003` 里只有 `Crit`，
        ///     而 `Crit` 保持 1.25 ⇒ 伏雷/印/收束 仍是 20px ✓
        ///
        ///   ── 拉平后的**完整字号表**（基准 `26 × 0.769 = 19.994`，×2 字号的整数量化）──
        ///     档位                          组成                                      乘积      上屏
        ///     bomb                          `Bomb 1.50`                               1.5000    **30px**
        ///     普通暴击                      `Crit 1.25`                               1.2500    **25px**
        ///     —— 以上为仅有的两档 > 20px ——
        ///     金币/经验/缺水/普通/回血/护盾  `1.00`                                     1.0000    20px
        ///     大伤害(非暴击,≥80)            `1.00 × 发布 1.00`（本轮 1.35→1.00）       1.0000    20px
        ///     升级                          `LevelUp 1.00`（本轮 1.30→1.00）           1.0000    20px
        ///     成熟                          `PlantMature 1.00`（本轮 1.25→1.00）       1.0000    20px
        ///     天气「大晴天！/下雨了！」      `PlantMature 1.00 × 发布 1.00`（1.2→1.0）   1.0000    20px
        ///     收获                          `Harvest 1.00`（本轮 1.15→1.00，且不上屏）  1.0000    20px
        ///     金币额外 GoldBonus            `1.00`（本轮 1.15→1.00）                   1.0000    20px
        ///     闪避 Evade                    `1.00`（本轮 1.10→1.00）                   1.0000    20px
        ///     格挡（借 Evade）              `Evade 1.00 × 发布 0.90`                    0.9000    18px
        ///     伏雷(踩中)                    `Crit 1.25 × 1.15 × 0.696`                 1.0005    20px
        ///     印                            `Crit 1.25 × 1.00 × 0.800`                 1.0000    20px
        ///     收束                          `Crit 1.25 × 0.90 × 0.889`                 1.0001    20px
        ///     吸血红字                      `Heal 1.00 × 1.00`                          1.0000    20px
        ///     剑气·暴击                     `Crit 1.25 × 0.800`（本轮 0.95→0.800）     1.0000    20px
        ///     污水                          `1.00 × 1.00 × 1.00`（本轮 1.05→1.00）     1.0000    20px
        ///     海浪                          `1.00 × 发布 1.00 × 1.00`（发布 1.05→1.00）1.0000    20px
        ///     剑气·非暴击                   `1.00 × 0.800`                              0.8000    16px
        ///     DoT(灼烧/减速/中毒)           `0.85`                                      0.8500    17px
        ///     弹射 Ricochet                 `1.00 × 发布 0.85`                          0.8500    17px
        ///     感电 Shock                    `0.95 × 发布 0.90`                          0.8550    17px
        ///     回响 Echo                     `0.95 × 发布 0.85`                          0.8075    16px
        ///     吸血绿字                      `Heal 1.00 × 1.00 × 0.80`                   0.8000    16px
        ///     伏雷(超时)                    `1.00 × 0.90 × 0.696`                       0.6264    13px
        ///   ⇒ **唯一最大 bomb 30 / 唯一第二 暴击 25 / 其余全部 ≤ 20**，三条硬约束逐条成立 ✓
        ///
        ///   ── 本轮改了的那几档，以及**复合档的方案**（为什么这么取）──
        ///     · 剑气 `0.95 → 0.800`：它是 `Crit(1.25) × 本值` 的乘积 ⇒ 要让**暴击剑气** ≤ 20px，
        ///       本值上限就是 `1.0003 / 1.25 = 0.8002` ⇒ 取 0.800（暴击剑气 = 1.0000 ⇒ 20px）。
        ///       ⚠️ 副作用：**非暴击剑气** = `1.00 × 0.800 = 0.800` ⇒ **16px**（改前 19px）。
        ///       **选了"保证暴击剑气 20、非暴击剑气 16"这一个解**，理由：
        ///         ① 硬约束里"除 bomb/暴击外一律 ≤ 20px"是**唯一不可让**的一条，
        ///            而一个单值倍率**无法**让 1.25 与 1.00 两条乘积同时落在 20px
        ///            （要让非暴击 = 20px 就得本值 = 1.0003，那暴击剑气会到 25px ✗）
        ///         ② 16px 落在"**小的更小**"那一组的区间里（回响 16 / 吸血绿字 16 / DoT 17 / 弹射 17 /
        ///            感电 17），而剑气是**多发**弹字（一层一道、层数越多同屏越多）⇒ 小一号反而更该 ✓
        ///         ③ 不动 `PopupKind.Crit`（1.25）是硬前提：伏雷/印/收束 三个反解值全挂在它上面 ✓
        ///     · 污水 `1.05 → 1.00`：`1.00(普通) × 1.00(发布) × 本值` ⇒ 改前 1.05 ⇒ 21px。
        ///       只动本表这一处就够（发布侧 `PluginExtra2` 那一支的 `Scale = 1.0f` 不动）✓
        ///     · 海浪：本表本来就是 `1.00`，**21px 全部来自发布侧的 `Scale = 1.05f`**
        ///       （`PluginExtra2` 的浪扫到人那一支）⇒ 本轮把**发布侧**收回 1.00（见那个文件），
        ///       本表一个字不动 ✓
        /// </summary>
        private static float ScaleOf(PluginPopupStyle s)
        {
            switch (s)
            {
                case PluginPopupStyle.Ricochet: return 1.00f;
                // ★ 本轮（目标像素口径）：0.9259 → **0.889** ⇒ 上屏乘积 1.25 × 0.90 × 0.889 = **1.0001** ⇒ 20px
                //   反解：`1.0003 / (1.25 × 0.90)` = 0.8892（发布侧 = `RunSession.PluginPostHit` 收束那一支的 `Scale = 0.9f`）
                case PluginPopupStyle.Focus: return 0.889f;
                // ★ 本轮（拉平）：0.95 → **0.800** ⇒ 暴击剑气 1.25 × 0.800 = **1.0000** ⇒ 20px
                //   反解：`1.0003 / (Crit 1.25)` = 0.8002 ⇒ 取 0.800（本档发布侧恒为 1.00，无第三段）
                //   ⚠️ 复合档：**非暴击剑气** = `DamageToEnemy(1.00) × 0.800` = 0.800 ⇒ **16px**（改前 19px）
                //     选这个解而不是"让非暴击 20"：后者要求本值 ≥ 1.0003 ⇒ 暴击剑气到 25px，
                //     直接破掉"除 bomb/暴击外一律 ≤ 20px"这条硬约束 ✗（完整权衡见本函数头注释）✓
                case PluginPopupStyle.SwordWave: return 0.800f;
                case PluginPopupStyle.Echo: return 0.95f;
                // ★ 本轮：0.7246 → **0.696** ⇒ 上屏乘积 1.25 × 1.15 × 0.696 = **1.0005** ⇒ 20px（踩中那档）
                //   反解：`1.0003 / (1.25 × 1.15)` = 0.6959（发布侧 = `PluginField.TickMines` 的 `stepped ? 1.15f : 0.9f`）
                //   ⚠️ 超时那档（`stepped = false`）走的是 `PopupKind.DamageToEnemy`(1.00) × 0.90 × 本值
                //     ⇒ 0.6264 ⇒ **13px**，与改前（13px）一致，不在本次目标里 ✓
                case PluginPopupStyle.Chain: return 0.696f;
                // ★ 本轮：0.8333 → **0.800** ⇒ 上屏乘积 1.25 × 1.00 × 0.800 = **1.0000** ⇒ 20px
                //   反解：`1.0003 / (1.25 × 1.00)` = 0.8003（发布侧 = `Actors.World.DealDamage` 印那一支的 `Scale = 1.0f`）
                case PluginPopupStyle.Mark: return 0.800f;
                case PluginPopupStyle.Lifesteal: return 1.00f;
                case PluginPopupStyle.Shock: return 0.95f;   // 感电：数量多（最多 9 个人）⇒ 字小一点不刷屏
                // ★ 本轮（拉平）：1.05 → **1.00** ⇒ 1.00(普通) × 1.00(发布) × 1.00 = 1.0000 ⇒ 20px（改前 21px）
                //   只在**本表**收：污水那一条发布侧是 `PluginExtra2` 的 `Scale = 1.0f`，
                //   本来就是 1.00 ⇒ 动它没有意义，也容易与"海浪"那条混淆（海浪的问题在发布侧）✓
                case PluginPopupStyle.Sewer: return 1.00f;   // 污水：与普通同号，靠沼绿 + 污水溅射图标区分
                // ★ 本表不动（1.00）：海浪上一轮的 21px 全部来自**发布侧** `Scale = 1.05f`
                //   （`PluginExtra2` 浪扫到人那一支）⇒ 本轮把那一处收回 1.00，
                //   于是 `1.00(普通) × 1.00(发布) × 1.00(本表)` = 1.0000 ⇒ 20px ✓
                case PluginPopupStyle.Wave: return 1.00f;
                default: return 1f;
            }
        }

        /// <summary>
        /// 这一档样式该看**哪把武器**的插件外观。
        /// 弹射在水壶上、伏雷/灼印在肥料上、其余（收束/剑气/回响/吸血）在锄头上 ——
        /// 与 `Goods.cs` 里各插件的 `Slot` 声明一致 ✓
        /// </summary>
        private static WeaponSlot SlotOf(PluginPopupStyle s)
        {
            switch (s)
            {
                case PluginPopupStyle.Ricochet: return WeaponSlot.Can;
                // ★ v290：感电在锄头、污水与海浪在水壶（与 `Goods.cs` 各自的宿主一致）
                case PluginPopupStyle.Sewer:
                case PluginPopupStyle.Wave: return WeaponSlot.Can;
                case PluginPopupStyle.Chain:
                case PluginPopupStyle.Mark: return WeaponSlot.Fertilizer;
                // ★ v304：感电在锄头、弹射激光在水壶（与 `Goods.cs` 各自的宿主一致）
                case PluginPopupStyle.ShockArc: return WeaponSlot.Hoe;
                case PluginPopupStyle.RicochetLaser: return WeaponSlot.Can;
                default: return WeaponSlot.Hoe;
            }
        }

        private static FxSpec SpecFor(PluginPopupStyle s, RunSession session)
        {
            WeaponSlot slot = SlotOf(s);
            if (session == null) return PluginFx.Base(slot);
            // `WeaponSystem.Aggregate` 是"插件 → 数值修正"的唯一聚合点；
            //   `PluginFx.For` 再把它翻成外观 ⇒ 与武器挥击/爆炸/水柱**同一份颜色** ✓
            try
            {
                return PluginFx.For(WeaponSystem.Aggregate(session.Goods, slot), slot);
            }
            catch
            {
                return PluginFx.Base(slot);   // 兜底：绝不 `default(FxSpec)`（那会是纯黑，见 FxSpec.Valid）
            }
        }

        /// <summary>
        /// 从 `FxSpec` 派生弹字色。
        ///   `wCore/wRim` 决定取本体色还是边缘色（霓虹边在亮背景上更醒目，本体色更"实"）。
        /// ★ 唯一掺白的地方是下面的**亮度兜底**：弹字压在草地/农田上，太暗就等于消失 ✗
        ///   这条兜底也解释了为什么"颜色不能直接照搬 `fx.Core` 原值" ——
        ///   伏雷的 `Core` 被 `PluginFx.For` 压成了土褐（0.46,0.34,0.22），
        ///   直接拿来写字在草地上读不出来 ⇒ 兜底把它提亮（色相不变）✓
        /// </summary>
        private static Color Derive(Color fallback, in FxSpec fx, PluginPopupStyle s, out float lift)
        {
            lift = 0f;
            Color c;
            if (!fx.Valid)
            {
                // 没有外观规格 ⇒ 用**该插件自己的默认色族**（见下面每档的常量注释）
                c = fallback;
            }
            else
            {
                float wRim;
                switch (s)
                {
                    case PluginPopupStyle.Ricochet: wRim = 0.55f; break;  // 银蓝边 → 偏青蓝
                    case PluginPopupStyle.Focus: wRim = 0.15f; break;     // 金色本体为主
                    case PluginPopupStyle.SwordWave: wRim = 0.35f; break; // 冷白
                    case PluginPopupStyle.Echo: wRim = 0.75f; break;      // 紫边为主（紫灰）
                    case PluginPopupStyle.Chain: wRim = 0.60f; break;     // 电蓝边
                    case PluginPopupStyle.Mark: wRim = 0.70f; break;      // 朱红边 → 橙红偏红
                    case PluginPopupStyle.Lifesteal: wRim = 0.85f; break; // 血红边 → 猩红
                    case PluginPopupStyle.Shock: wRim = 0.40f; break;     // 电青本体为主
                    case PluginPopupStyle.Sewer: wRim = 0.75f; break;     // 沼绿边 → 脏绿
                    case PluginPopupStyle.Wave: wRim = 0.55f; break;      // 海蓝
                    // ★ v304：两条连线
                    case PluginPopupStyle.ShockArc: wRim = 0.40f; break;        // 电青本体为主
                    case PluginPopupStyle.RicochetLaser: wRim = 0.30f; break;   // 更亮（用户："更亮"）
                    default: wRim = 0.5f; break;
                }
                // 本体色打底、边缘色上色 ⇒ 两色都在里面，改插件配色弹字自动跟着变 ✓
                c = Color.Lerp(fx.Core, fx.Rim, wRim);
                // 与"该档原本的弹字色"掺一点点，保住弹字族的一致性（不至于离谱）
                c = Color.Lerp(c, fallback, 0.28f);
            }
            c.a = 1f;
            // ── 亮度兜底（唯一的掺白）──
            float lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            const float MinLum = 0.62f;      // 弹字压在中低明度背景上仍然读得出的下限
            if (lum < MinLum && lum < 0.999f)
            {
                lift = (MinLum - lum) / (1f - lum);
                c = Color.Lerp(c, Color.white, lift * 0.80f);
                c.a = 1f;
            }
            return c;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ④ 弹字逐帧动画（**绘制侧唯一插入点**在 `ImGuiHud.DrawPopups`）
        //
        //  ★ v279 边界：这里只算"这一条字此刻长什么样"，**不推进任何东西**。
        //    命中特效的推进在 `Tick`（由 `PluginHitFxHost.Update` 每帧调），
        //    与"屏幕上有没有弹字"无关 ⇒ 弹字队列空了也不会让特效停在半途 ✓
        //
        //  `age`：0 = 刚出现，1 = 即将消失（与 `DrawPopups` 里已有的 `age` 同一个量）
        //  `pop`：字号倍率（就地乘 —— 与既有"爆开→回落"曲线叠加，不替换它）
        //  `dx/dy`：像素位移（HUD 虚拟像素，与 `tr` 同一口径）
        //  `ghost`：>0 表示"再画一遍同样的字"的 alpha（回响的重影）
        // ══════════════════════════════════════════════════════════════════

        /// ★ v282：**参数从"文本"换成"样式字段"** ★
        ///   改前是 `Animate(string text, …)`，内部再 `StyleOfText(text)` 反查 ——
        ///   那个"反查"依赖一个**必须出现在文本里**的后缀字符（`↗ ◎ / ↯ ♥`）。
        ///   用户要求取消那些字符之后，反查就再也命中不了，动画会**整条静默失效** ✗
        ///   ⇒ 现在直接吃 `ft.Plugin`（`Apply` 写进去的那个），与图标**同一个来源** ✓
        ///   `None` 时立即返回：非插件弹字的 `pop`/`dx`/`dy` 逐位不变 ✓
        public static void Animate(PluginPopupStyle s, float age, ref float pop, ref float dx, ref float dy,
                                   out float ghost)
        {
            ghost = 0f;
            if (s == PluginPopupStyle.None) return;
            age = Mathf.Clamp01(age);
            switch (s)
            {
                // 弹射：**Y 方向两次弹跳、逐次衰减**（落地一样地弹两下）
                case PluginPopupStyle.Ricochet:
                {
                    const float T1 = 0.26f;     // 第一次弹跳周期
                    float t = Mathf.Min(age / T1, 1f);
                    float bump = Mathf.Sin(t * Mathf.PI);                 // 0→1→0
                    float second = age > T1 * 0.55f
                        ? Mathf.Sin(Mathf.Clamp01((age - T1 * 0.55f) / (T1 * 0.9f)) * Mathf.PI) * 0.45f
                        : 0f;
                    dy -= (bump + second) * 22f * (1f - age * 0.5f);
                    break;
                }
                // 收束：**从大缩小** 1.6 → 1.0（前 32% 完成），像"对准了"
                case PluginPopupStyle.Focus:
                {
                    float t = Mathf.Clamp01(age / 0.32f);
                    pop *= Mathf.Lerp(1.60f, 1.00f, t);
                    break;
                }
                // 剑气：**斜切入场**（X 位移 + 轻微下沉），像被一刀带出来的
                case PluginPopupStyle.SwordWave:
                {
                    float t = Mathf.Clamp01(age / 0.22f);
                    float k = 1f - t;
                    dx += k * 28f;
                    dy -= k * 10f;
                    pop *= 0.92f + 0.08f * t;
                    break;
                }
                // 回响：**延迟一次重影**（0.18~0.52 之间出现，偏移 4px、α0.35）
                case PluginPopupStyle.Echo:
                {
                    if (age > 0.18f && age < 0.52f)
                    {
                        float u = Mathf.InverseLerp(0.18f, 0.52f, age);
                        ghost = 0.35f * (1f - u);      // 由浓到淡 —— "余波散掉"
                        dx += 0f;
                    }
                    break;
                }
                // 伏雷：**前 0.12s 抖动**（电打的震颤）
                case PluginPopupStyle.Chain:
                {
                    if (age < 0.12f)
                    {
                        float amp = 3.2f * (1f - age / 0.12f);
                        dx += Mathf.Sin(age * 210f) * amp;
                        dy += Mathf.Cos(age * 173f) * amp * 0.6f;
                    }
                    break;
                }
                // 灼印：**上浮更快**（额外的向上位移，与既有的 `-age*30` 叠加）
                case PluginPopupStyle.Mark:
                {
                    dy -= age * 16f;
                    break;
                }
                // 吸血：一记轻微下压再回位（"吸"的那一下）
                case PluginPopupStyle.Lifesteal:
                {
                    float t = Mathf.Clamp01(age / 0.20f);
                    dy += Mathf.Sin(t * Mathf.PI) * 6f;
                    break;
                }
                // ═══ ★ v290：本批三档（各一条独有动作，与上面七档都不同）═══
                // 感电：**前 0.15s 高频抖动**，振幅比伏雷稍大（电压更高）、频率也不同 ⇒
                //   两张"闪电"图标即使同时出现，动起来也读得出是两种东西 ✓
                case PluginPopupStyle.Shock:
                {
                    if (age < 0.15f)
                    {
                        float amp = 4.0f * (1f - age / 0.15f);
                        dx += Mathf.Sin(age * 260f) * amp;
                        dy += Mathf.Cos(age * 197f) * amp * 0.5f;
                    }
                    break;
                }
                // 污水：**向下沉**（像被泼在地上）—— 唯一一档"往下走"的弹字
                case PluginPopupStyle.Sewer:
                {
                    dy += age * 12f;
                    break;
                }
                // 海浪：**先上浮再回落**（一波推上去又退回来）
                case PluginPopupStyle.Wave:
                {
                    dy -= Mathf.Sin(Mathf.Clamp01(age / 0.7f) * Mathf.PI) * 9f;
                    break;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  ⑤ 命中特效：对象池 + 逐帧推进
        //
        //  ★ 纪律（照 `AttackVfx` 的池化思路写，但**不改那个文件**）★
        //    · 渲染体：每个特效 1~6 个 `LineRenderer`（≤ 12 ✓）
        //    · 寿命：≤ 0.35s ✓
        //    · 运行期零 `Instantiate` / 零 `Destroy` ✓（池在第一次出特效时一次建满）
        //    · 材质：**一份共享材质**（`spawn` 时 `Shader.Find` 一条兜底链，只建一次）
        //      ⇒ 与 `TownKit` / `AttackVfx` 同一条纪律：不逐实例 `new Material` ✓
        //    · 颜色与弹字同源：`Derive` 算出来的那个 `Color`
        // ══════════════════════════════════════════════════════════════════

        /// <summary>池容量（渲染体个数）。6 个特效同时在场也用不到 30，够用且不撑批。</summary>
        private const int PoolSize = 30;

        private struct Live
        {
            public bool On;
            public PluginPopupStyle Style;
            public float Age, Life;
            /// <summary>★ 出生时刻（`Time.unscaledTime`）——**墙钟**寿命兜底的唯一依据。
            /// 它不参与任何动画；只用来回答"这东西在屏幕上待了多久"。
            /// 为什么必须有它：`Age` 只在 `Tick` 里长，而 `Tick` 本身可能因为宿主缺失 /
            /// 顿帧（`dt <= 0`）/ 推进路径被误挂到别处而**一次都没跑** ⇒ 只看 `Age` 的实现
            /// 会把特效**永久**留在屏幕上（本文件 v279 修的就是这个事故）✓</summary>
            public float SpawnRealtime;
            public Vector3 Origin;
            public Vector3 Toward;        // 吸血用：玩家位置
            public Vector3 Dir;           // 弹射：折返方向
            /// <summary>★ v304：连线类（`ShockArc` / `RicochetLaser`）的两端。
            /// 旧样式不读它、默认零向量 ⇒ 对它们**恒等** ✓</summary>
            public Vector3 From, To;
            public Color Color;
            public int First, Count;      // 占用的池区间
        }

        private static GameObject _root;
        private static Material _mat;
        private static LineRenderer[] _pool;
        private static Live[] _live = new Live[8];
        private static Vector2 _seed = new Vector2(12.9898f, 78.233f);
        private static int _frame = -1;

        /// <summary>该样式要几个渲染体。</summary>
        private static int CostOf(PluginPopupStyle s)
        {
            switch (s)
            {
                case PluginPopupStyle.Ricochet: return 2;   // 去 + 折返两段弧
                case PluginPopupStyle.Focus: return 6;      // 六条向心线
                case PluginPopupStyle.SwordWave: return 1;  // 一条细长斩痕
                case PluginPopupStyle.Chain: return 1;      // 一条折线闪电
                case PluginPopupStyle.Mark: return 5;       // 五颗小火星
                case PluginPopupStyle.Lifesteal: return 4;  // 四滴血
                // ★ v304：两条连线各只占 1 个 LineRenderer（多段折线在同一个渲染器里）
                // ★ v307：弹射激光从 1 变 **2** —— 去程一段 + 折返的一段（两个渲染体）
                //   （感电的支流仍然挤在**同一个**渲染体里 ⇒ 它还是 1）✓
                case PluginPopupStyle.ShockArc: return 1;
                case PluginPopupStyle.RicochetLaser: return 2;
                default: return 0;
            }
        }

        private static float LifeOf(PluginPopupStyle s)
        {
            switch (s)
            {
                case PluginPopupStyle.Ricochet: return 0.24f;
                case PluginPopupStyle.Focus: return 0.28f;
                case PluginPopupStyle.SwordWave: return 0.22f;
                case PluginPopupStyle.Chain: return 0.20f;
                case PluginPopupStyle.Mark: return 0.35f;
                case PluginPopupStyle.Lifesteal: return 0.32f;
                // ★ v304：用户给的两条数（0.15s / 0.12s）
                case PluginPopupStyle.ShockArc: return 0.15f;
                case PluginPopupStyle.RicochetLaser: return 0.12f;
                default: return 0.2f;
            }
        }

        private static void EnsurePool()
        {
            if (_pool != null) return;
            _root = new GameObject("PluginHitFxPool");
            _root.hideFlags = HideFlags.DontSave;      // 不写进场景、不进存档
            // ★ v279：池根也要 `DontDestroyOnLoad` ★
            //   宿主（`PluginHitFxHost`）是跨场景的，而池根原来是**普通场景对象** ⇒
            //   换场景（战斗 → 主城）时池根被销毁，而静态 `_pool` 数组还指着那批
            //   已经销毁的 `LineRenderer` ⇒ 下一帧 `Draw`/`Recycle` 会抛 `MissingReferenceException`。
            //   推进从"跟着弹字偶尔跑"变成"每帧必跑"之后，这条路径会被**每帧**踩到 ⇒ 必须一起修 ✓
            Object.DontDestroyOnLoad(_root);
            // 材质：与 `AttackVfx` / `TownDust` 同一条兜底链（都是读**顶点色**的 shader）
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            _mat = sh != null ? new Material(sh) { name = "PluginHitFxShared" } : null;

            _pool = new LineRenderer[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Line" + i);
                go.transform.SetParent(_root.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.positionCount = 8;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.widthMultiplier = 0.07f;
                if (_mat != null) lr.sharedMaterial = _mat;
                lr.enabled = false;
                _pool[i] = lr;
            }
        }

        /// <summary>在命中点放一发小特效（池满时回收最老的那一发）。</summary>
        private static void Spawn(PluginPopupStyle s, Vector2 pos, in FxSpec fx, RunSession session)
        {
            int cost = CostOf(s);
            if (cost <= 0) return;
            EnsurePool();
            if (_pool == null) return;

            int first = Alloc(cost);
            if (first < 0) return;

            // 颜色与弹字同源：用同一份 `Derive`（会"提亮到读得出"），特效比字暗一档更好看
            Color c = Derive(Color.white, in fx, s, out float _);
            c = Color.Lerp(c, Color.white, 0.15f);

            Live lv = default;
            lv.On = true;
            lv.Style = s;
            lv.Age = 0f;
            lv.Life = LifeOf(s);
            lv.SpawnRealtime = Time.unscaledTime;             // ★ 墙钟出生时刻（寿命兜底用）
            lv.Origin = new Vector3(pos.x, 1.05f, pos.y);     // 与弹字同高（`DrawPopups` 用 1.3f）
            lv.Color = c;
            lv.First = first;
            lv.Count = cost;
            // 弹射的折返方向：用确定性哈希从命中点取一个水平方向（不进 World.Rng ✓）
            float a = Mathf.Repeat(pos.x * 12.9898f + pos.y * 78.233f, 1f) * Mathf.PI * 2f;
            lv.Dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            // 吸血的"血往玩家飘"：拿玩家位置（没有就往上飘）
            PlayerActor p = session != null && session.World != null ? session.World.Player : null;
            lv.Toward = p != null ? new Vector3(p.Pos.x, 1.05f, p.Pos.y) : lv.Origin + Vector3.up * 0.9f;

            for (int i = 0; i < _live.Length; i++)
            {
                if (_live[i].On) continue;
                _live[i] = lv;
                return;
            }
            // 8 个活跃槽都满了 —— 覆盖最老的（不扩容、不分配）
            //
            // ★ v280 修：**覆盖前必须先 `Recycle`** ★
            //   改前只有一句 `_live[oldest] = lv;` ⇒ 被顶掉那一发占用的渲染体（灼印 5 个 /
            //   收束 6 个）**没有被关掉**：`enabled` 留着、形状停在最后一帧，
            //   而 `Recycle` 只认槽位里**当前**的 `First/Count` ⇒ 那批渲染体**再也没人能关** ✗
            //   症状与 v279 修掉的"爆炸后画面定格"是同一类（常驻 `LineRenderer` 钉在屏幕上），
            //   只是这次是"越打越多"：池子 30 个被一路吃光 ⇒ `Alloc` 恒返回 -1 ⇒ 新特效出不来了 ✓
            int oldest = 0;
            float best = -1f;
            for (int i = 0; i < _live.Length; i++)
                if (_live[i].Age > best) { best = _live[i].Age; oldest = i; }
            Recycle(oldest);
            _live[oldest] = lv;
        }

        // ══════════════════════════════════════════════════════════════════
        //  ★ v304：两个**连线**入口（用户需求③④）
        //
        //  为什么是"另开两个 public 方法"而不是往 `Apply` 里加分支 ✗
        //    `Apply` 是"**一条弹字**变成什么样子"的入口（它需要 `FloatingText`）；
        //    而这两条连线**没有弹字**：它们由效果层（`PluginExtra2`）在"真的发生了电击 /
        //    真的发生了弹射"的那一刻直接点名两端点。硬塞进 `Apply` 会变成
        //    "为了连线先造一条假弹字"，那是本末倒置 ✗
        //
        //  池账与生命周期：完全复用本文件已有的那 30 个 `LineRenderer`
        //    · 感电：每个被电到的敌人 1 个渲染体、寿命 0.15s
        //      （3 层 9 个目标 ⇒ 9 个；`Alloc` 找不到连续空位就这一次不画，**不扩容**）
        //    · 弹射：1 个渲染体、寿命 0.12s
        //  ⇒ 两者都**只在真的发生时**才被调用（见 `PluginExtra2.ShockChain` 与
        //    `RunSession.PluginPostHit` 的弹射分支里的 `if (second != null)`）✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>★ v304：**感电的折线电流** —— 主目标 → 被电到的敌人。</summary>
        public static void SpawnBolt(Vector3 from, Vector3 to, in FxSpec fx)
            => SpawnLink(PluginPopupStyle.ShockArc, from, to, in fx);

        /// <summary>★ v304：**水壶弹射的小激光** —— 原目标 → 弹射目标。</summary>
        public static void SpawnLaser(Vector3 from, Vector3 to, in FxSpec fx)
            => SpawnLink(PluginPopupStyle.RicochetLaser, from, to, in fx);

        /// <summary>两条连线共用的一段：颜色与弹字同源（`Derive`），池满就放弃这一次（不扩容）。</summary>
        private static void SpawnLink(PluginPopupStyle s, Vector3 from, Vector3 to, in FxSpec fx)
        {
            if (!Attached) return;
            int cost = CostOf(s);
            if (cost <= 0) return;
            EnsurePool();
            if (_pool == null) return;
            int first = Alloc(cost);
            if (first < 0) return;

            Color c = Derive(Color.white, in fx, s, out float _);
            c = Color.Lerp(c, Color.white, 0.30f);     // 电流/激光要**比字更亮**（用户："更亮"）

            Live lv = default;
            lv.On = true;
            lv.Style = s;
            lv.Age = 0f;
            lv.Life = LifeOf(s);
            lv.SpawnRealtime = Time.unscaledTime;
            lv.From = from;
            lv.To = to;
            lv.Origin = (from + to) * 0.5f;
            lv.Dir = to - from;
            lv.Color = c;
            lv.First = first;
            lv.Count = cost;

            for (int i = 0; i < _live.Length; i++)
            {
                if (_live[i].On) continue;
                _live[i] = lv;
                Draw(in lv, 0f);                        // 立刻画第一帧（不然会有一帧空）
                return;
            }
            // 8 个活跃槽都满了 —— 覆盖最老的**活体**（先 Recycle 再占，见 `Spawn` 的 v280 注释）
            int oldest = -1;
            float best = -1f;
            for (int i = 0; i < _live.Length; i++)
                if (_live[i].On && _live[i].Age > best) { best = _live[i].Age; oldest = i; }
            if (oldest < 0) return;
            Recycle(oldest);
            _live[oldest] = lv;
            Draw(in lv, 0f);
        }

        /// <summary>池建好了没有（= `_pool` 非空）。连线入口用它挡第一次调用前的空引用。</summary>
        private static bool Attached => _pool != null && _pool.Length > 0;

        /// <summary>从池里找一段连续的 `n` 个空闲渲染体。</summary>
        private static int Alloc(int n)
        {
            for (int i = 0; i + n <= _pool.Length; i++)
            {
                bool free = true;
                for (int k = 0; k < n; k++)
                {
                    if (_pool[i + k].enabled) { free = false; break; }
                }
                if (!free) continue;
                for (int k = 0; k < n; k++) _pool[i + k].enabled = true;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// ★ 墙钟寿命上限（秒）：**不看 `Age`、不看 `dt`**，只看它出生到现在过了多久。
        /// 任何一发特效超过这个值一律强制回收，哪怕推进逻辑一次都没跑到。
        ///
        /// 口径为什么是 2.0s：最长的一档 `Mark` 只有 0.35s，即使整段落在顿帧里
        /// （`timeScale = 0.05` × 0.32s）也只是把墙钟拉到 ~1s 量级 ⇒ 2.0s 既不会误杀
        /// 正常特效，又能在"没人推进"时给出一个**确定的**回收时刻 ✓
        /// </summary>
        private const float MaxRealtimeLife = 2.0f;

        /// <summary>
        /// ★ v280：一次 `Tick` 最多推进几发（兜底）。
        /// 槽位 8 个，取 2× 留出"推进中途又 Spawn 占了一个新槽"的余量；
        /// 目的是让任何一个极端帧的推进量有**硬上限**，不会退化成同一帧里的长循环 ✓
        /// </summary>
        private const int MaxAdvancesPerTick = 16;

        /// <summary>
        /// ★★ 每帧推进（v279：**由常驻宿主 `PluginHitFxHost.Update` 每帧调用**）★★
        ///
        /// ── 为什么必须换宿主（事故复盘，改前是 `Pump`，由 `ImGuiHud.DrawPopups` 调）──
        ///   `DrawPopups` 的第一行是 `if (s == null || s.Popups.Count == 0) return;`
        ///   ⇒ **弹字一消失，推进就停**。而本类的渲染体是**常驻 `LineRenderer`**（池里的
        ///   GameObject 一直在场景里，只是 `enabled` 开关）⇒ 推进一停，它们就停在最后一帧的
        ///   形状和 alpha 上**一直画下去**：玩家看到的是"爆炸的光弧钉在屏幕上不动"，
        ///   而 `Time.time`、玩法、攻击全都正常 ⇒ 用户报的"画面定格但时间在走"就是这个 ✓
        ///   ⇒ 推进会话**必须与"屏幕上有没有弹字"完全解耦**：IMGUI 只负责画字，
        ///     本函数由 MonoBehaviour 的 `Update` 驱动，弹字队列空不空都照跑 ✓
        ///
        /// ── `dt <= 0` 不再 `return`（第二处"永远不清理"）──
        ///   改前 `if (dt <= 0f) return;` 在**清理之前**返回 ⇒ 顿帧/暂停期间特效既不动、
        ///   也永远不回收。改后：`dt <= 0` 只表示"这一帧不积分、不重画"，
        ///   墙钟兜底（`MaxRealtimeLife`）**照常执行** ⇒ 最多 2s 一定收干净 ✓
        ///
        /// ★ 仍保留"一帧只跑一次"的闸（`Time.frameCount`）：宿主理论上每帧一次，
        ///   但域重载被关掉时可能出现第二个宿主，闸保证同一帧不会被推进两次 ★
        /// </summary>
        public static void Tick(float dt)
        {
            if (_live == null || _pool == null) return;
            // ★ v279：池根万一还是被销毁了（切场景 / 手动 Destroy），静态数组会指向
            //   **已销毁**的 `LineRenderer`。这里用 Unity 的伪 null 判一次，认出来就整体作废、
            //   等下一次 `Spawn` 重建 —— 绝不对着已销毁的对象设 `enabled` ✓
            if (_pool.Length == 0 || _pool[0] == null)
            {
                _pool = null; _root = null; _mat = null; _live = new Live[8];
                return;
            }
            if (Time.frameCount == _frame) return;
            _frame = Time.frameCount;

            // ★ v280：**单帧推进量上限**（兜底）★
            //   槽位只有 8 个，正常一次 Tick 最多推进 8 发；上限取 2× 槽位是为了留出
            //   "推进中途 `Spawn` 又占了一个新槽"的余量。
            //   它挡的是"槽位表被外部改坏 / 某一路在 Tick 里反复 Spawn"这类极端情况：
            //   那种情况下没有上限就是同一帧里跑不完的循环 = 主线程卡死 ✓
            int advanced = 0;

            // dt <= 0（顿帧 / timeScale 被压低到 0 / 暂停）：跳过运动积分，但仍检查寿命
            bool frozen = dt <= 0f;
            if (dt > 0.1f) dt = 0.1f;                     // 卡顿过后的那一帧不让特效瞬移

            for (int i = 0; i < _live.Length; i++)
            {
                if (!_live[i].On) continue;
                Live lv = _live[i];

                // ① 墙钟兜底 —— **先于**任何积分/绘制，且与 `dt` 无关
                if (Time.unscaledTime - lv.SpawnRealtime >= MaxRealtimeLife)
                {
                    Recycle(i);
                    continue;
                }

                // ② 冻结帧：不积分、不重画（保留上一帧形状即可），但寿命已在 ① 里被检查过
                if (frozen) continue;

                // ③ 正常推进
                lv.Age += dt;
                float t = Mathf.Clamp01(lv.Age / lv.Life);
                if (t >= 1f)
                {
                    Recycle(i);
                    continue;
                }
                Draw(in lv, t);
                _live[i] = lv;
                if (++advanced >= MaxAdvancesPerTick) break;     // ★ 单帧上限
            }
        }

        /// <summary>回收一发：关掉它占用的**全部**渲染体，把槽位清空（不 Destroy）。</summary>
        private static void Recycle(int i)
        {
            Live lv = _live[i];
            for (int k = 0; k < lv.Count; k++)
            {
                int idx = lv.First + k;
                if (idx < 0 || idx >= _pool.Length) continue;
                _pool[idx].enabled = false;
            }
            _live[i] = default;
        }

        /// <summary>把一发特效画成当前的形状（每帧重算点的位置，不做粒子）。</summary>
        private static void Draw(in Live lv, float t)
        {
            Color c = lv.Color;
            c.a = 1f - t;                                  // 整体淡出
            float w = Mathf.Lerp(0.075f, 0.02f, t);        // 越到后面越细
            switch (lv.Style)
            {
                // 弹射：**短弧线折返** —— 先向外抛一段弧，再折回来
                case PluginPopupStyle.Ricochet:
                {
                    SetArc(lv.First + 0, lv.Origin, lv.Dir, 0.55f * t, 0.42f, 6, c, w);
                    SetArc(lv.First + 1, lv.Origin, -lv.Dir, 0.35f * t, 0.30f, 5, c, w * 0.8f);
                    break;
                }
                // 收束：**一点向心收拢的线** —— 六条从外向内收到中心
                case PluginPopupStyle.Focus:
                {
                    for (int k = 0; k < 6; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 6f;
                        var d = new Vector3(Mathf.Cos(ang), 0.35f * Mathf.Sin(ang * 2f), Mathf.Sin(ang));
                        float r = Mathf.Lerp(0.62f, 0.05f, t);
                        SetSeg(lv.First + k, lv.Origin + d * r, lv.Origin + d * (r + 0.16f), c, w);
                    }
                    break;
                }
                // 剑气：**细长斩痕** —— 一条斜线，先长后收
                case PluginPopupStyle.SwordWave:
                {
                    var dir = new Vector3(lv.Dir.x, 0.28f, lv.Dir.z).normalized;
                    float len = Mathf.Lerp(0.95f, 0.25f, t);
                    SetSeg(lv.First, lv.Origin - dir * len * 0.5f, lv.Origin + dir * len * 0.5f, c, w * 1.4f);
                    break;
                }
                // 伏雷：**短折线闪电** —— 七个点每帧抖动（12Hz 刷新，读起来在"闪"）
                case PluginPopupStyle.Chain:
                {
                    LineRenderer lr = _pool[lv.First];
                    lr.positionCount = 7;
                    lr.startColor = c;
                    lr.endColor = c;
                    lr.widthMultiplier = w * 1.2f;
                    int step = Mathf.FloorToInt(lv.Age * 12f);           // 12Hz 重画
                    for (int k = 0; k < 7; k++)
                    {
                        float u = k / 6f;
                        float jx = Noise(step * 7 + k, _seed.x) * 0.16f;
                        float jy = Noise(step * 7 + k, _seed.y) * 0.16f;
                        Vector3 b = lv.Origin + lv.Dir * (u - 0.5f) * 0.7f;
                        lr.SetPosition(k, new Vector3(b.x + jx, b.y + jy + 0.05f, b.z + jx));
                    }
                    break;
                }
                // 灼印：**小火星** —— 五颗向外飞散的小点（用极短线段画）
                case PluginPopupStyle.Mark:
                {
                    for (int k = 0; k < 5; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 5f + 0.4f;
                        var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        float r = 0.10f + 0.30f * t;
                        Vector3 p = lv.Origin + d * r + Vector3.up * (0.10f + 0.22f * t);
                        SetSeg(lv.First + k, p, p + Vector3.up * 0.05f, c, 0.05f);
                    }
                    break;
                }
                // 吸血：**向玩家飘的血滴** —— 四小段从命中点往玩家方向漂
                case PluginPopupStyle.Lifesteal:
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float ph = k * 0.25f;
                        float u = Mathf.Clamp01((t - ph) / (1f - ph));
                        Vector3 p = Vector3.Lerp(lv.Origin, lv.Toward, u);
                        p.x += Mathf.Sin((u + k) * 6.2f) * 0.10f;        // 一点漂移，不是直线
                        SetSeg(lv.First + k, p, p + Vector3.up * 0.06f, c, 0.055f);
                    }
                    break;
                }
                // ═══ ★ v304：两条**连线**（感电的电流弹射 / 水壶弹射的小激光）═══
                // 感电：**5~7 段抖动折线**（用户点名"不是直线"）。抖动按 `12Hz` 重画 ⇒ 读作"在跳火"。
                //   段数取 6（用户给的 5~7 的中点）；振幅 `BoltJitter`；两端**钉死在端点上**
                //   （`sin(πu)` 包络 ⇒ `u=0/1` 处抖动为 0，线不会飘离目标）✓
                case PluginPopupStyle.ShockArc:
                {
                    LineRenderer lr = _pool[lv.First];
                    lr.startColor = c;
                    lr.endColor = c;
                    lr.widthMultiplier = BoltWidth * (1f - t * 0.35f);
                    int step = Mathf.FloorToInt(lv.Age * 12f);           // 12Hz 重画
                    Vector3 d = lv.To - lv.From;
                    float len = d.magnitude;
                    var fwd = len > 1e-4f ? d / len : Vector3.forward;
                    // 两条互相垂直的横向基：抖动在**三维**里都发生（不然俯视看是一条直线）
                    var right = Vector3.Cross(fwd, Vector3.up);
                    if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                    right.Normalize();
                    var upv = Vector3.Cross(right, fwd).normalized;

                    // ── ① 主折线：与改前**逐位相同**（抖动种子、包络、段数都没动）✓
                    for (int k = 0; k < BoltSegs; k++)
                    {
                        float u = k / (float)(BoltSegs - 1);
                        float env = Mathf.Sin(u * Mathf.PI);             // 两端 0 ⇒ 钉死端点
                        float jx = Noise(step * 31 + k, _seed.x);       // `Noise` 已经是 −1…+1
                        float jy = Noise(step * 31 + k, _seed.y);
                        _boltPts[k] = Vector3.Lerp(lv.From, lv.To, u)
                                    + (right * jx + upv * jy) * (BoltJitter * env);
                    }

                    // ══════════════════════════════════════════════════════════
                    // ── ② ★ v307 升级：**分叉支流** ──
                    //   用户：「升级成**主电流 + 2~3 条分叉支流**（从主折线的中段斜向分出去），
                    //   读作'电流在跳'」。
                    //
                    //   为什么写成"根点 → 支尖 → 根点"（去而复返）：
                    //     `LineRenderer` 画的是**一条连续的折线**，它不会分叉 ——
                    //     要在一根渲染体上画出支流，唯一的办法是让折线**出去再原路回来**。
                    //     往返回的那一段与去程完全重合 ⇒ 屏幕上读作**一条支流**（只是亮度略高，
                    //     那正好是电流该有的样子）✓
                    //     反过来说：如果把支流点直接接在主折线**末尾**，就会从终点拉一条
                    //     跨越整条电流的杂线 ✗ 所以必须**插在中段的那个点上**。
                    //
                    //   代价：`positionCount` 从 6 涨到 `6 + 2×2 = 10`（每根还是只占 1 个渲染体）。
                    //   ⇒ 池账**完全不变**，只是同一个 `LineRenderer` 多了 4 个点 ✓
                    // ══════════════════════════════════════════════════════════
                    int n = BoltSegs;
                    for (int b = 0; b < BoltBranches && n + 2 < _boltPts.Length; b++)
                    {
                        int root = 2 + b;                                // 主折线第 2、3 点 = 中段
                        if (root >= BoltSegs) break;
                        int sd = root * 31 + 977 + b * 13;
                        float bx = Noise(step * 7 + sd, _seed.y);        // −1…+1
                        float by = Noise(step * 7 + sd + 1, _seed.x);
                        float bl = len * BoltBranchLen;                  // 支流长度（按电流长度比例）
                        // 斜向：主方向 45% + 横向 + 上下 ⇒ 是"岔出去"，不是"垂直于主轴的分叉线"
                        Vector3 tip = _boltPts[root]
                                    + fwd * (bl * 0.45f)
                                    + right * (bx * bl)
                                    + upv * (by * bl + 0.08f);
                        _boltPts[n++] = _boltPts[root];                  // 根点
                        _boltPts[n++] = tip;                             // 支尖
                        _boltPts[n++] = _boltPts[root];                  // 原路回来
                    }

                    lr.positionCount = n;
                    for (int k = 0; k < n; k++) lr.SetPosition(k, _boltPts[k]);
                    break;
                }
                // 弹射：**一条细小的亮激光**（用户："比主水柱细很多、更亮"）——
                //   主水柱（`AttackVfx.SpawnBeam`）宽 0.14m 起、颜色只掺 0.35 本体色；
                //   这里 2 点直连、宽 `LaserWidth`=0.028m（≈ 主水柱的 1/5）、掺白更多 ⇒ 明显更细更亮 ✓
                case PluginPopupStyle.RicochetLaser:
                {
                    // ══════════════════════════════════════════════════════════
                    // ★ v307 升级（用户：「光线在两点之间**折返** + **第二段更细更亮** +
                    //   **落点一圈小涟漪**」）
                    //
                    // ── 改前为什么读不出"弹过去了" ──
                    //   只有一根 From→To 的静止细线（`LaserWidth`=0.028m、0.12s）。
                    //   一根直线在屏幕上与"剑气""连锁""激光"全都同构 ⇒ 玩家看不出它**跳**了 ✗
                    // ── 现在两段 ──
                    //   ① **去程**（渲染体 `lv.First`）：From → To，宽度随时间收；
                    //   ② **回程**（渲染体 `lv.First+1`）：To → 往回 42% 处，**更细（0.55×）、更亮**
                    //      （掺白 45%）⇒ 读作"光打过去又折回来"，这就是"折返"的全部视觉依据 ✓
                    //   时间上故意错开：`t < 0.35` 只画去程 ⇒ 先看到"打过去"，再看到"折回来"，
                    //   两个动作分得开（同时出现会读成"一条更粗的线"）✗
                    //   ③ 落点那圈小涟漪不在这里 —— 在 `RunSession.PluginPostHit` 里
                    //      `AttackVfx.SpawnRipple(second.Pos, …)`（那里才知道落点的世界坐标）✓
                    // ⚠️ 池账：`CostOf(RicochetLaser)` 因此从 1 变成 **2**（两个连续渲染体，
                    //    仍是那 30 格的池 ⇒ 同屏最多 15 条弹射激光，实际远低于此）✓
                    // ══════════════════════════════════════════════════════════
                    Color c2 = Color.Lerp(c, Color.white, 0.45f);        // 第二段更亮
                    SetSeg(lv.First, lv.From, lv.To, c, LaserWidth * (1f - t * 0.45f));
                    float back = Mathf.Clamp01((t - 0.35f) / 0.65f) * 0.42f;
                    if (back > 0.002f)
                        SetSeg(lv.First + 1, lv.To, Vector3.Lerp(lv.To, lv.From, back),
                               c2, LaserWidth * 0.55f);
                    else
                        SetSeg(lv.First + 1, lv.To, lv.To, c2, 0.0001f);  // 还没轮到回程
                    break;
                }
            }
        }

        /// <summary>★ v304：折线电流的段数（用户给 5~7，取中点）。</summary>
        private const int BoltSegs = 6;
        /// <summary>★ v307：**分叉支流条数**（用户："2~3 条"）。每条占 3 个折线点（去·尖·回），
        /// 都在**同一个** `LineRenderer` 里 ⇒ 池账不变 ✓</summary>
        private const int BoltBranches = 2;
        /// <summary>★ v307：支流长度（相对主电流长度的比例）。0.34 在 3.5m 距离上约 1.2m，
        /// 明显"岔得出去"，又不会长到看起来像第二条主电流 ✓</summary>
        private const float BoltBranchLen = 0.34f;
        /// <summary>★ v307：折线点复用缓冲（主折线 6 + 分叉 2×3 = 12 ⇒ 取 24 留余量）。
        /// `Vector3[]` 不是 Unity 对象 ⇒ 可以放在静态字段初始化器里（不违反"不许 new Unity 对象"）✓</summary>
        private static readonly Vector3[] _boltPts = new Vector3[24];
        /// <summary>折线电流的横向抖动振幅（米）—— 0.22m 在 3.5m 距离上读作"明显是折的"。</summary>
        private const float BoltJitter = 0.22f;
        /// <summary>折线电流的线宽（米）。</summary>
        private const float BoltWidth = 0.045f;
        /// <summary>弹射小激光的线宽（米）—— 主水柱的 `HalfWidth` 是 0.14 ⇒ 这里约是它的 1/5。</summary>
        private const float LaserWidth = 0.028f;

        /// <summary>一段弧（用 3~6 个点，仍然只占 1 个渲染体）。</summary>
        private static void SetArc(int idx, Vector3 origin, Vector3 dir, float reach, float height, int pts,
                                   Color c, float w)
        {
            LineRenderer lr = _pool[idx];
            lr.positionCount = pts;
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = w;
            var right = new Vector3(-dir.z, 0f, dir.x);
            for (int k = 0; k < pts; k++)
            {
                float u = k / (float)(pts - 1);
                // 抛物线：沿 dir 前进 reach*u，同时上抛再落下 height*sin(πu)
                Vector3 p = origin + dir * (reach * u) + right * (0.10f * Mathf.Sin(u * Mathf.PI))
                          + Vector3.up * (height * Mathf.Sin(u * Mathf.PI));
                lr.SetPosition(k, p);
            }
        }

        private static void SetSeg(int idx, Vector3 a, Vector3 b, Color c, float w)
        {
            LineRenderer lr = _pool[idx];
            lr.positionCount = 2;
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = w;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }

        /// <summary>确定性噪声（不碰 `World.Rng`：表现随机绝不进逻辑流 ✓）。</summary>
        private static float Noise(int i, float salt)
        {
            float v = Mathf.Sin(i * 12.9898f + salt) * 43758.5453f;
            return (v - Mathf.Floor(v)) * 2f - 1f;
        }

        /// <summary>清场（换局 / 收尾）。只关不再用，不 Destroy。</summary>
        public static void Reset()
        {
            _live = new Live[8];
            if (_pool == null) return;
            for (int i = 0; i < _pool.Length; i++) if (_pool[i] != null) _pool[i].enabled = false;
        }

        /// <summary>自检读数：现在有几发命中特效在场（0 = 池是干净的）。</summary>
        public static int LiveCount
        {
            get
            {
                if (_live == null) return 0;
                int n = 0;
                for (int i = 0; i < _live.Length; i++) if (_live[i].On) n++;
                return n;
            }
        }

        /// <summary>自检读数：池里还有多少个渲染体是**开着**的（0 = 屏幕上不该有残留）。</summary>
        public static int EnabledRenderers
        {
            get
            {
                if (_pool == null) return 0;
                int n = 0;
                for (int i = 0; i < _pool.Length; i++) if (_pool[i] != null && _pool[i].enabled) n++;
                return n;
            }
        }

        /// <summary>最近一次 `Tick` 的帧号（自检用：判断推进到底有没有在跑）。</summary>
        public static int LastTickFrame => _frame;
    }

    /// <summary>
    /// ★★ 命中特效的**常驻推进宿主**（v279 新增）★★
    ///
    /// ── 它存在的唯一理由 ──
    ///   改前 `PluginHitFx.Pump` 是被 `ImGuiHud.DrawPopups` 调的，而那个方法在
    ///   **弹字队列为空时第一行就 `return`** ⇒ 推进随弹字一起消失，特效停在中途、
    ///   常驻的 `LineRenderer` 一直画 ⇒ "画面定格但时间在走"。
    ///   ⇒ 现在推进挂在这个宿主的 `Update` 上：**与 IMGUI、与弹字、与暂停菜单全都无关**，
    ///     每帧必跑一次 ✓（`ImGuiHud` 那边只保留 `Animate`（弹字自己的入场动画），不再推进特效）
    ///
    /// ── 自举方式（照 `AttackVfxHost` / `GlobalJuiceFxHost` 的现成做法）──
    ///   `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + `DontDestroyOnLoad`
    ///   ⇒ 不依赖场景里有没有人挂它，不动任何场景资源 ✓
    ///   `DefaultExecutionOrder(31010)`：排在 `AttackVfxHost`(31000) 之后，两套特效互不干扰 ✓
    ///
    /// ── 三重防重入（域重载被关掉时不建第二个）──
    ///   ① 静态 `_host` 已在 ⇒ 直接返回；② 场景里已有同名对象 ⇒ 认领；
    ///   ③ `Awake` 里再比一次，晚了就自我销毁 ✓
    /// </summary>
    [DefaultExecutionOrder(31010)]
    public sealed class PluginHitFxHost : MonoBehaviour
    {
        private static PluginHitFxHost _host;

        /// <summary>进 play 模式后（首场景加载完）自动建宿主。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_host != null) return;                                   // ①
            GameObject existing = GameObject.Find("PLUGIN_HIT_FX");       // ②
            if (existing != null)
            {
                var c = existing.GetComponent<PluginHitFxHost>();
                if (c != null) { _host = c; return; }
            }
            var go = new GameObject("PLUGIN_HIT_FX");
            DontDestroyOnLoad(go);
            go.AddComponent<PluginHitFxHost>();
        }

        private void Awake()
        {
            if (_host != null && _host != this)                          // ③
            {
                Destroy(gameObject);
                return;
            }
            _host = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>每帧推进命中特效。`Time.deltaTime`（缩放时间）⇒ 顿帧时特效跟着慢下来，
        /// 但**寿命兜底是墙钟的**（见 `MaxRealtimeLife`）⇒ 再慢也不会停在屏幕上 ✓</summary>
        private void Update()
        {
            if (!Application.isPlaying) return;
            PluginHitFx.Tick(Time.deltaTime);
            RestoreStuckTimeScale();
        }

        private void OnDisable() { RestoreStuckTimeScale(); }

        private void OnDestroy()
        {
            if (_host == this) _host = null;
            PluginHitFx.Reset();          // 只关渲染体、不 Destroy（与 `Reset` 的约定一致）
            RestoreStuckTimeScale();
        }

        /// <summary>
        /// `timeScale` 残留兜底：只在"仍停在顿帧那一档**且顿帧计时已经走完**"时写回 1。
        ///
        /// ★ 为什么必须同时看 `HitStopLeft` ★
        ///   `GlobalJuice.RestoreTimeScaleFallback()` 只看 `timeScale ≈ HitStopScale`，
        ///   每帧调它会把**正在生效**的顿帧当场打断（0.32s 的"顿"变成 1 帧）✗
        ///   ⇒ 这里多一个 `HitStopLeft <= 0` 的合取：顿帧本身不动，
        ///     只处理"计时已经结束、但 scale 因为宿主提前销毁而没被写回"的那一种残局 ✓
        /// </summary>
        private static void RestoreStuckTimeScale()
        {
            if (!Application.isPlaying) return;
            if (!Mathf.Approximately(Time.timeScale, CombatFeedback.HitStopScale)) return;
            if (CombatFeedback.HitStopLeft > 0f) return;      // 真在顿帧：不碰
            Time.timeScale = 1f;
        }
    }
}
