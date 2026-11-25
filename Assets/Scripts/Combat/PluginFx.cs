using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **一次攻击 / 一次命中画成什么样** —— 插件与特效之间的唯一数据。
    ///
    /// ★ 为什么必须有这个东西 ★
    /// 规格：**任何一个插件装上之后，那把武器的特效必须发生变化或被增加**。
    /// 而在它之前，特效那一层是全工程最"聋"的一层：
    ///   · `MeleeSwingEvent` / `BeamEvent` / `FertilizerBlastEvent` 只带**几何**
    ///     （起点、终点、半径、张角），**一个颜色字段都没有** ✓
    ///   · `WorldView.OnMeleeSwing` 里写死 `new Color(1f, 0.96f, 0.62f)`，
    ///     `OnBeam` 里写死 `new Color(0.4f, 0.95f, 1f)`，
    ///     `OnFertBlast` 里写死 `new Color(1f, 0.62f, 0.2f)` ✓
    ///   ⇒ 于是"装不装插件"在画面上**必然一模一样**，无论插件改了多少数值 ✗
    ///
    /// 本类的职责就一条：把 `WeaponModifiers`（插件聚合结果，17 个字段）
    /// 翻译成一组**表现层真的会去读**的参数 ✓
    ///
    /// ★ 设计约束（三条，缺一条就会退化回"看不见"）★
    ///   ① **空槽必须等于改动前的样子** —— `Base()` 的三组颜色就是原来写死的那三组，
    ///      所以"装插件前后"的差异**只可能来自插件**，不掺任何美术改版 ✓
    ///   ② **每一种 effect 都必须至少改一个"看得见"的通道** ——
    ///      颜色 / 尺寸 / 发光 / 火花数 / 环数 / 拖尾 / 震屏，七选一 ✓
    ///      只改"数值"而不改这七个的话，玩家眼里就没有发生任何事 ✗
    ///   ③ **纯函数、无状态** —— 同一份 `WeaponModifiers` 永远算出同一个 `FxSpec`，
    ///      因此可以离线断言、可以进确定性重放 ✓
    ///
    /// ③ 也是"可自动化验证"的基础：`DebugApi.PluginFxAudit()` 逐个插件算一遍，
    /// 直接拿 `Diff(空槽, 装插件)` 是否为空来判"这个插件有没有特效" ✓
    /// </summary>
    public struct FxSpec
    {
        /// <summary>`false` = 这份规格**没被写过**（旧调用点 / 默认构造）⇒ 表现层退回 `Base()`。
        /// 没有这个开关的话，一个零初始化的 `Color` 就是**纯黑**，
        /// 忘填一处就会让那一路特效整个变黑 ✗</summary>
        public bool Valid;

        /// <summary>本体色。</summary>
        public Color Core;
        /// <summary>霓虹边缘色（`Seedfall/Vfx` 的 `_RimColor`）。</summary>
        public Color Rim;

        /// <summary>尺寸倍率（1 = 基础）。爆炸半径插件、范围插件都体现在这里。</summary>
        public float Scale;
        /// <summary>发光强度倍率（照明半径与亮度）。</summary>
        public float Glow;
        /// <summary>附加火花个数（碎片粒子）。</summary>
        public int Sparks;
        /// <summary>附加冲击环个数（回声 / 连锁 / 暴击炸裂的第二圈、第三圈）。</summary>
        public int Rings;
        /// <summary>拖尾强度（0 = 无拖尾，1.4 ≈ 满长度残影）。</summary>
        public float Trail;
        /// <summary>震屏倍率（1 = 基础）。</summary>
        public float Shake;

        /// <summary>这份规格命中了哪些 `PluginEffectKind`（位掩码，`1 &lt;&lt; (int)kind`）。给自证用。</summary>
        public uint Kinds;
        /// <summary>来自几件插件（0 = 空槽）。</summary>
        public int Sources;
        /// <summary>把上面全部可见通道量化后算出的签名。给自证用：签名不同 ⇒ 画出来必然不同。</summary>
        public uint Signature;
    }

    /// <summary>
    /// **插件 → 特效** 的映射表。**12 种** `PluginEffectKind` 各有一条独立的外观贡献，
    /// 且 12 条的「通道组合」两两不同（见每条上面的签名）⇒
    /// 同一把武器上的 4 件插件不可能画出同一个样子 ✓
    ///
    /// ★ 阶段 3：17 条 → 12 条 ★
    /// 上一版的 12 条数值类（`DamageFlat` 之流）随枚举一起删除 —— 它们的外观贡献本来就是
    /// "偏白 / 变大 / 更亮"，而新版插件改的是**攻击的形状**，形状变化必须画得出来 ✓
    /// 每一条的信号（签名）都由规格指定，写在下面每一段的注释里 ✓
    /// </summary>
    public static class PluginFx
    {
        /// <summary>`PluginEffectKind` 里除 `None` 外的档数。改枚举时这个必须同步 —— 自检钉着它。</summary>
        public const int KindCount = 26;   // ★ 本轮：28 → 26（删掉「余温」「锋利」两件 ⇒ 去掉两档）

        public static uint Bit(PluginEffectKind k) => 1u << (int)k;

        // ────────────────────────────────────────────────────────────────
        // ① 武器基础外观 —— **就是改动前写死的那三组颜色**
        //
        // 这一条是"对比截图有意义"的前提：空槽必须画成旧样子，
        // 否则截图上的差异就分不清是"插件生效"还是"美术顺手改了配色" ✗
        // ────────────────────────────────────────────────────────────────

        /// <summary>某把武器在**没装任何插件**时的样子（等于改动前的硬编码外观）。</summary>
        public static FxSpec Base(WeaponSlot slot)
        {
            FxSpec f = default;
            f.Valid = true;
            switch (slot)
            {
                case WeaponSlot.Hoe:            // 原 `OnMeleeSwing` 写死值
                    f.Core = new Color(1f, 0.96f, 0.62f, 0.92f);
                    f.Rim = new Color(1f, 0.85f, 0.35f, 0.70f);
                    break;
                case WeaponSlot.Can:            // 原 `OnBeam` 写死值
                    f.Core = new Color(0.40f, 0.95f, 1f, 0.95f);
                    f.Rim = new Color(0.35f, 0.85f, 1f, 1f);
                    break;
                case WeaponSlot.Fertilizer:     // 原 `OnFertBlast` 写死值（冲击环）
                    f.Core = new Color(1f, 0.62f, 0.20f, 0.90f);
                    f.Rim = new Color(1f, 0.45f, 0.10f, 0.80f);
                    break;
                default:
                    f.Core = new Color(1f, 0.92f, 0.55f, 0.92f);
                    f.Rim = new Color(1f, 0.80f, 0.30f, 0.70f);
                    break;
            }
            f.Scale = 1f;
            f.Glow = 1f;
            f.Trail = 0f;
            f.Shake = 1f;
            f.Signature = Hash(in f);
            return f;
        }

        /// <summary>表现层的安全入口：没写过的规格退回基础外观，**永不返回纯黑**。</summary>
        public static FxSpec Or(in FxSpec f, WeaponSlot slot) => f.Valid ? f : Base(slot);

        // ────────────────────────────────────────────────────────────────
        // ② 插件 → 外观：唯一的分派点
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 把一份插件修正翻译成外观。
        ///
        /// ★ 叠加方式统一为"累加 / 连乘"，不取 max ★
        /// 取 max 的话"再装一件同色的插件"会**没有任何变化**，玩家会以为插件坏了 ——
        /// 与 `WeaponSystem.Aggregate` 里同类累加是同一条理由 ✓
        ///
        /// ★ 12 条的签名（规格给定，两两不同）★
        ///   剑气 Rings+1, Trail 0.55｜灼烧 Core 橙红, Glow×1.4, Sparks 2
        ///   双击 Shake×1.3, Scale 1.1｜吸血 Rim 血红, Trail 0.4
        ///   多柱 Rings+1, Sparks 1｜减速 Core 冰蓝, Trail 0.32
        ///   加粗 Scale 1.25, Glow×1.15｜自充 Rim 水绿, Glow×1.3
        ///   子爆 Rings+2, Scale 0.9｜毒素 Core 毒绿, Rim 黄绿, Sparks 3
        ///   免耗 Glow×1.5, Rim 金｜连环 Rings+1, Shake×1.5, Trail 0.45
        /// </summary>
        public static FxSpec For(in WeaponModifiers m, WeaponSlot slot)
        {
            FxSpec f = Base(slot);
            if (m.SourceCount <= 0) return f;      // 空槽 ⇒ 与改动前完全一致

            uint kinds = 0;

            // ═════════ ★ v253：六个全新机制（六个新维度，不与上面 12 条同族）═════════

            // 回响：**同一下的账晚点再结** ⇒ 两圈余波 + 一条紫拖尾
            // 签名：Rings+2, Trail 0.25, Rim 紫
            if (m.EchoPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.Echo);
                f.Rim = Mix(f.Rim, new Color(0.72f, 0.48f, 1f, f.Rim.a), 0.62f);
                f.Rings += 2;
                f.Trail += 0.25f;
            }
            // 弹射：**跳到下一个目标** ⇒ 大量碎火花 + 银蓝边
            // 签名：Sparks+4, Trail 0.20, Rim 银蓝
            if (m.RicochetPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.Ricochet);
                f.Rim = Mix(f.Rim, new Color(0.78f, 0.92f, 1f, f.Rim.a), 0.55f);
                f.Sparks += 4;
                f.Trail += 0.20f;
            }
            // 催熟：**作用在田地上** ⇒ 本体转叶绿、略收一点（像是"灌进去"）
            // 签名：Core 叶绿, Scale×0.95
            if (m.RipenSeconds > 0f)
            {
                kinds |= Bit(PluginEffectKind.Ripen);
                f.Core = Mix(f.Core, new Color(0.55f, 1f, 0.55f, f.Core.a), 0.66f);
                f.Scale *= 0.95f;
            }
            // 收束：**越打越准** ⇒ 刀身更大更稳、轻微震屏
            // 签名：Shake×1.2, Scale×1.15, Trail 0.15
            if (m.FocusEvery > 0)
            {
                kinds |= Bit(PluginEffectKind.Focus);
                f.Shake *= 1.2f;
                f.Scale *= 1.15f;
                f.Trail += 0.15f;
            }
            // 潮汐：**把敌人推开** ⇒ 更大的尺寸 + 一点震动（像一记浪头）
            // 签名：Scale×1.30, Shake×1.10
            if (m.TidePush > 0f)
            {
                kinds |= Bit(PluginEffectKind.Tide);
                f.Scale *= 1.30f;
                f.Shake *= 1.10f;
            }

            // ═════════ ★ v255：肥料三件（各占一组没人用过的通道）═════════

            // 伏雷：**埋在土里** ⇒ 本体压暗、多一圈土环、几乎不发光（"收着的能量"）
            // 签名：Core 土褐, Glow×0.7, Rings+1
            if (m.MineHitPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.Mine);
                f.Core = Mix(f.Core, new Color(0.46f, 0.34f, 0.22f, f.Core.a), 0.62f);
                f.Glow *= 0.70f;
                f.Rings += 1;
            }
            // 护壤：**给自己上盾** ⇒ 青白霓虹边 + 更亮（像一层结壳）
            // 签名：Rim 青白, Glow×1.45, Sparks+1
            if (m.WardShield > 0f)
            {
                kinds |= Bit(PluginEffectKind.Ward);
                f.Rim = Mix(f.Rim, new Color(0.62f, 1f, 0.94f, f.Rim.a), 0.66f);
                f.Glow *= 1.45f;
                f.Sparks += 1;
            }
            // 灼印：**烙印** ⇒ 焦土橙本体 + 大红边 + 四颗火星
            // 签名：Core 焦橙, Rim 朱红, Sparks+4
            if (m.MarkBonusPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.Mark);
                f.Core = Mix(f.Core, new Color(1f, 0.52f, 0.10f, f.Core.a), 0.70f);
                f.Rim = Mix(f.Rim, new Color(1f, 0.18f, 0.12f, f.Rim.a), 0.62f);
                f.Sparks += 4;
            }

            // ═════════ 锄头 ═════════

            // 剑气：**一道冷白的月牙**，与暖金挥击弧分得清 ⇒ 多一圈 + 长拖尾
            // 签名：Rings+1, Trail 0.55
            if (m.SwordWaveCount > 0)
            {
                kinds |= Bit(PluginEffectKind.SwordWave);
                f.Core = Mix(f.Core, new Color(0.80f, 0.98f, 1f, f.Core.a), 0.64f);
                f.Rim = Mix(f.Rim, new Color(0.55f, 0.95f, 1f, f.Rim.a), 0.56f);
                f.Rings += 1;
                f.Trail += 0.55f;
                f.Glow += 0.35f;
            }
            // 灼烧：炭火橙红 + 火星 + 更亮
            // 签名：Core 橙红, Glow×1.4, Sparks 2
            if (m.BurnLayersPerHit > 0)
            {
                kinds |= Bit(PluginEffectKind.BurnOnHit);
                f.Core = Mix(f.Core, new Color(1f, 0.30f, 0.10f, f.Core.a), 0.68f);
                f.Glow *= 1.4f;
                f.Sparks += 2;
            }
            // 双击：**两刀**的重量感 ⇒ 震屏更重、刀身更大
            // 签名：Shake×1.3, Scale 1.1
            if (m.ExtraSwings > 0)
            {
                kinds |= Bit(PluginEffectKind.DoubleStrike);
                f.Shake *= 1.3f;
                f.Scale *= 1.1f;
            }
            // 吸血：**血**才是它的语义 ⇒ 血红霓虹边 + 一条回流拖尾
            // 签名：Rim 血红, Trail 0.4
            if (m.LifestealPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.Lifesteal);
                f.Rim = Mix(f.Rim, new Color(0.92f, 0.10f, 0.18f, f.Rim.a), 0.70f);
                f.Trail += 0.4f;
            }

            // ═════════ 水壶 ═════════

            // 多柱：**多出来的那一道**要看得见 ⇒ 环 + 火花
            // 签名：Rings+1, Sparks 1
            if (m.ExtraJets > 0)
            {
                kinds |= Bit(PluginEffectKind.MultiJet);
                f.Rings += 1;
                f.Sparks += 1;
            }
            // 减速：**冰蓝**是本工程里最容易被认出的元素色 ⇒ 本体转冰蓝 + 拖尾
            // 签名：Core 冰蓝, Trail 0.32
            if (m.SlowLayersPerHit > 0)
            {
                kinds |= Bit(PluginEffectKind.SlowOnHit);
                f.Core = Mix(f.Core, new Color(0.48f, 0.86f, 1f, f.Core.a), 0.68f);
                f.Trail += 0.32f;
            }
            // 加粗：柱**更粗** ⇒ 尺寸 + 发光（不改长度 —— 长度是射程的事）
            // 签名：Scale 1.25, Glow×1.15
            if (m.BeamWidthPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.ThickJet);
                f.Scale *= 1.25f;
                f.Glow *= 1.15f;
            }
            // 自充：回的是**水** ⇒ 水绿霓虹边 + 更亮
            // 签名：Rim 水绿, Glow×1.3
            if (m.AutoChargePerSecond > 0f)
            {
                kinds |= Bit(PluginEffectKind.AutoCharge);
                f.Rim = Mix(f.Rim, new Color(0.45f, 1f, 0.70f, f.Rim.a), 0.58f);
                f.Glow *= 1.3f;
            }

            // ═════════ 肥料 ═════════

            // 子爆：**多两圈**（子爆是旁落的小爆）⇒ 环 +2、本体略小
            // 签名：Rings+2, Scale 0.9
            if (m.SubBurstCount > 0)
            {
                kinds |= Bit(PluginEffectKind.SubBurst);
                f.Rings += 2;
                f.Scale *= 0.9f;
            }
            // 毒素：**毒绿**本体 + 黄绿边 + 三颗毒火星
            // 签名：Core 毒绿, Rim 黄绿, Sparks 3
            if (m.PoisonLayers > 0)
            {
                kinds |= Bit(PluginEffectKind.PoisonZone);
                f.Core = Mix(f.Core, new Color(0.36f, 1f, 0.28f, f.Core.a), 0.70f);
                f.Rim = Mix(f.Rim, new Color(0.82f, 1f, 0.24f, f.Rim.a), 0.62f);
                f.Sparks += 3;
            }
            // 免耗：**省下来了** ⇒ 金边 + 炽亮
            // 签名：Glow×1.5, Rim 金
            if (m.FreeBurstChance > 0f)
            {
                kinds |= Bit(PluginEffectKind.FreeBurst);
                f.Rim = Mix(f.Rim, new Color(1f, 0.86f, 0.22f, f.Rim.a), 0.66f);
                f.Glow *= 1.5f;
            }
            // 连环：**第二炸** ⇒ 环 + 更重的震屏 + 拖尾
            // 签名：Rings+1, Shake×1.5, Trail 0.45
            if (m.ChainBurstCount > 0)
            {
                kinds |= Bit(PluginEffectKind.ChainBurst);
                f.Rings += 1;
                f.Shake *= 1.5f;
                f.Trail += 0.45f;
            }

            // ═════════ ★ v290：本批 7 件（每条至少改一个**看得见**的通道，签名两两不同）═════════
            //
            //  通道组合（与上面 21 条、以及彼此之间都不重复）：
            //   感电   Core 电青, Sparks+3            ｜狂暴   Core 赤红, Shake×1.25
            //   巨型   Scale×1.35, Rings+1            ｜臭水沟 Core 沼绿, Rim 黄绿, Glow×0.75
            //   海浪   Scale×1.20, Trail 0.30, Shake×1.15
            //   锋利   Glow×1.30, Rings+2            ｜黑洞   Core 暗紫, Glow×0.55, Rings+1
            //  ★ 为什么每件都必须改"颜色 / 尺寸 / 发光 / 拖尾 / 震屏"里的至少一个 ★
            //    `DebugApi.PluginFxAudit` 用 `Diff(空槽, 只装这一件)` 非空来判"这件插件有没有特效" ✓
            //    只改新字段而不改这七条可见通道 ⇒ 那条审计会（正确地）报红 ✓

            // 感电：电流**窜出去** ⇒ 电青本体 + 三颗电弧火星
            if (m.ShockTargets > 0)
            {
                kinds |= Bit(PluginEffectKind.Shock);
                f.Core = Mix(f.Core, new Color(0.62f, 0.92f, 1f, f.Core.a), 0.66f);
                f.Sparks += 3;
            }
            // 狂暴：**对残血的敌人**更狠 ⇒ 赤红本体 + 更重的震屏
            if (m.FrenzyPct > 0f)
            {
                kinds |= Bit(PluginEffectKind.Frenzy);
                f.Core = Mix(f.Core, new Color(0.94f, 0.24f, 0.20f, f.Core.a), 0.58f);
                f.Shake *= 1.25f;
            }
            // 巨型：**整圈判定**是更大的一击 ⇒ 尺寸 +1 圈
            if (m.GiantRing)
            {
                kinds |= Bit(PluginEffectKind.Giant);
                f.Scale *= 1.35f;
                f.Rings += 1;
            }
            // 臭水沟：**浑浊沼绿** ⇒ 本体转沼绿、霓虹边黄绿、**压暗发光**（脏水不发光）
            if (m.SewerEvery > 0)
            {
                kinds |= Bit(PluginEffectKind.Sewer);
                f.Core = Mix(f.Core, PluginExtra2.SewerCore, 0.78f);
                f.Rim = Mix(f.Rim, PluginExtra2.SewerRim, 0.62f);
                f.Glow *= 0.75f;
            }
            // 海浪：**一道更宽的浪** ⇒ 尺寸 + 拖尾 + 一点震动
            if (m.WaveRadius > 0f)
            {
                kinds |= Bit(PluginEffectKind.Wave);
                f.Scale *= 1.20f;
                f.Trail += 0.30f;
                f.Shake *= 1.15f;
            }
            // 黑洞：**光被吸走** ⇒ 本体压成暗紫、发光大减、一圈内收环
            if (m.BlackHole)
            {
                kinds |= Bit(PluginEffectKind.BlackHole);
                f.Core = Mix(f.Core, new Color(0.24f, 0.16f, 0.34f, f.Core.a), 0.72f);
                f.Glow *= 0.55f;
                f.Rings += 1;
            }

            f.Kinds = kinds;
            f.Sources = m.SourceCount;
            f.Signature = Hash(in f);
            return f;
        }

        // ────────────────────────────────────────────────────────────────
        // ③ 自证工具：两个规格**画出来一样吗**
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 两份规格在"画面上"的差异通道。返回 `""` = **画出来完全一样**。
        /// 阈值取"肉眼能分"的量级：颜色 2%、尺寸 1%、发光 1%、拖尾 1%、震屏 1%，
        /// 火花/环数按整数比 ✓
        /// </summary>
        public static string Diff(in FxSpec a, in FxSpec b)
        {
            var sb = new System.Text.StringBuilder();
            if (ColorGap(a.Core, b.Core) > 0.020f) sb.Append("Core ");
            if (ColorGap(a.Rim, b.Rim) > 0.020f) sb.Append("Rim ");
            if (Mathf.Abs(a.Scale - b.Scale) > 0.010f) sb.Append("Scale ");
            if (Mathf.Abs(a.Glow - b.Glow) > 0.010f) sb.Append("Glow ");
            if (a.Sparks != b.Sparks) sb.Append("Sparks ");
            if (a.Rings != b.Rings) sb.Append("Rings ");
            if (Mathf.Abs(a.Trail - b.Trail) > 0.010f) sb.Append("Trail ");
            if (Mathf.Abs(a.Shake - b.Shake) > 0.010f) sb.Append("Shake ");
            return sb.ToString().TrimEnd();
        }

        /// <summary>一行摘要，给日志/调试入口/报告直接用。</summary>
        public static string Describe(in FxSpec f)
        {
            if (!f.Valid) return "(未设置)";
            var sb = new System.Text.StringBuilder(96);
            sb.Append("Core#").Append(Hex(f.Core))
              .Append(" Rim#").Append(Hex(f.Rim))
              .Append(" 尺寸×").Append(f.Scale.ToString("0.00"))
              .Append(" 发光×").Append(f.Glow.ToString("0.00"))
              .Append(" 火花").Append(f.Sparks)
              .Append(" 环").Append(f.Rings)
              .Append(" 拖尾").Append(f.Trail.ToString("0.00"))
              .Append(" 震屏×").Append(f.Shake.ToString("0.00"))
              .Append(" 签名").Append(f.Signature.ToString("X8"))
              .Append("（").Append(f.Sources).Append(" 件）");
            return sb.ToString();
        }

        /// <summary>`0xRRGGBB`。</summary>
        public static string Hex(Color c)
            => ((uint)(Mathf.Clamp01(c.r) * 255f) << 16
              | (uint)(Mathf.Clamp01(c.g) * 255f) << 8
              | (uint)(Mathf.Clamp01(c.b) * 255f)).ToString("X6");

        /// <summary>两种颜色在三通道上的最大差（0..1）。</summary>
        private static float ColorGap(Color a, Color b)
            => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

        /// <summary>往目标色靠，保留 alpha。</summary>
        private static Color Mix(Color from, Color target, float t)
            => Color.Lerp(from, target, Mathf.Clamp01(t));

        /// <summary>
        /// 把全部可见通道量化后算 FNV-1a。**只用于自证与日志**，不参与任何判定 ✓
        /// 量化的意义：`Signature` 相同 ⇒ 画出来必然相同（不可能"签名一样但看起来不一样"）。
        /// </summary>
        private static uint Hash(in FxSpec f)
        {
            uint h = 2166136261u;
            h = Step(h, (uint)(Mathf.Clamp01(f.Core.r) * 255f));
            h = Step(h, (uint)(Mathf.Clamp01(f.Core.g) * 255f));
            h = Step(h, (uint)(Mathf.Clamp01(f.Core.b) * 255f));
            h = Step(h, (uint)(Mathf.Clamp01(f.Rim.r) * 255f));
            h = Step(h, (uint)(Mathf.Clamp01(f.Rim.g) * 255f));
            h = Step(h, (uint)(Mathf.Clamp01(f.Rim.b) * 255f));
            h = Step(h, (uint)Mathf.RoundToInt(f.Scale * 100f));
            h = Step(h, (uint)Mathf.RoundToInt(f.Glow * 100f));
            h = Step(h, (uint)Mathf.Max(0, f.Sparks));
            h = Step(h, (uint)Mathf.Max(0, f.Rings));
            h = Step(h, (uint)Mathf.RoundToInt(f.Trail * 100f));
            h = Step(h, (uint)Mathf.RoundToInt(f.Shake * 100f));
            return h;
        }

        private static uint Step(uint h, uint v)
        {
            h ^= v;
            return h * 16777619u;
        }
    }

    /// <summary>
    /// ★ 用户需求④：**一颗水球的起飞**（`RunSession.FireWaterBalls` 投递，`WorldView.OnWaterBall` 画）★
    ///
    /// ★ 为什么这个事件定义在**插件特效**这个文件里，而不是 `Combat.cs` 的"事件区" ★
    /// 水球是 `can_autocharge` 这一件插件**带来**的东西（没装它就没有水球）⇒
    /// 它与 `FxSpec`（插件 → 特效的唯一数据）是同一类东西：**插件外观通道的一部分** ✓
    /// 而 `Combat.cs` 里那一组是"武器本身"的事件（挥击 / 激光 / 爆炸 / 剑气），
    /// 它们在没有插件的版本里就存在 —— 水球不在那个集合里 ✓
    ///
    /// ★ 为什么是一条**直线**（没有 `TargetId`）★
    /// 逻辑层按"发射那一刻最近敌人所在的位置"定方向、按那条直线飞行并判定；
    /// 表现层拿到的 `Dist / Speed` 与逻辑层**同一对数**（见 `RunSession.WaterBallSpeed`）
    /// ⇒ "看得见的落点 = 判定打到的落点"，与 v185 剑气那条纪律一致 ✓
    /// 若表现层自己拐弯（追踪），慢放抓图里就会出现"画到了、判定没到"的假证据 ✗
    /// </summary>
    public struct WaterBallEvent
    {
        /// <summary>出膛位置（世界 XZ）。</summary>
        public Vector2 From;
        /// <summary>飞行方向（单位向量）。</summary>
        public Vector2 Dir;
        /// <summary>到落点的距离（米）。</summary>
        public float Dist;
        /// <summary>飞行速度（米/秒）。与逻辑层同一个常数。</summary>
        public float Speed;
        /// <summary>这一发是本轮第几颗（0 起）与共几颗 —— 表现层据此错开起手位置，避免叠成一颗。</summary>
        public int Index, Count;
        /// <summary>外观（吃水壶这一路的插件：自充的水绿边、多柱的环、加粗的尺寸…）。</summary>
        public FxSpec Fx;
    }

    /// <summary>
    /// ★ 用户需求④：**一颗水球打中了谁**（`RunSession.TickWaterBalls` 投递，`WorldView.OnWaterBallHit` 画）★
    /// 与 `WaterBallEvent` 同一处定义（理由同上）。这里的 `Damage` 只用于表现
    /// （溅射圈的大小与亮度随它缩放），**不参与任何判定** ✓
    /// </summary>
    public struct WaterBallHitEvent
    {
        /// <summary>命中点（世界 XZ）。</summary>
        public Vector2 Pos;
        /// <summary>这一颗的伤害（= 水壶命中伤害 × 0.50）。</summary>
        public float Damage;
        /// <summary>外观。</summary>
        public FxSpec Fx;
    }
}
