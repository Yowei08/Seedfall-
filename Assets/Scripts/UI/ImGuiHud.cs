using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 原型 HUD（IMGUI）。只读 RunSession 快照，并把鼠标点击写成"待处理指令"，
    /// 由 GameBootstrap 在下一个固定帧并入 FrameInput。
    /// 生产替换路径：uGUI / UI Toolkit，接口不变（见架构案 A-1）。
    ///
    /// v5 布局（**所有键位说明只出现在左侧说明栏**，HUD 其它地方不提示按键）：
    ///   左上 = 生命/护盾/经验 + 金币/耕地
    ///   左侧 = 按键说明（Tab 显示/隐藏；隐藏后仍保留一行 Tab 提示）
    ///   右侧 = 武器面板（滚轮切换的可视化）
    ///   顶部 = 只显示阶段与倒计时
    ///   底部 = 种子袋（鼠标点击选中，卡片底部两个按钮：合成 → 等级+1 / 出售）+ 耕地双进度条
    ///   Esc  = 菜单（属性 + 继续游戏 + 重新开始）
    /// </summary>
    public sealed class ImGuiHud
    {
        private Font _font;
        private Texture2D _white;
        private GUIStyle _label, _small, _bold, _button, _center, _title;
        private float _virtualW = 1280f;
        private float _virtualH = 720f;
        private bool _keyHelpInit;
        private Vector2 _statScroll, _invScroll, _itemScroll, _seedScroll;
        /// <summary>v83：商品改成一行一件的左图右文后，**每行各自有滚动位置**。
        /// 旧版 4 列窄卡共用一个底部详情区，所以只需要一条滚动量；现在每行的描述框是独立的，
        /// 共用一条滚动量会导致"滑一行动四行"。</summary>
        private readonly float[] _offerScrollPerRow = new float[4];
        private readonly float[] _offerScroll = new float[4];
        /// <summary>v52：商品详情区高度（4 列卡片下方）。</summary>
        private const float OfferDetailH = 96f;
        /// <summary>v52：当前显示详情的商品序号（卡片悬停切换；卖光后为 -1）。</summary>
        private int _offerShown;
        /// <summary>v19：卡片底部给按钮预留的高度（0 = 无按钮）。</summary>
        private float ButtonReserve;
        private readonly float[] _menuSeedScroll = new float[4];
        private readonly float[] _seedScrollPerCard = new float[4];
        private readonly float[] _choiceScroll = new float[3];
        private string _itemSelected;
        private GUIStyle _right;
        private float _statContentW = 300f;

        public int PendingBuyOffer = -1;
        public bool PendingReroll;
        public int PendingInitialChoice = -1;
        public bool PendingNextWave;
        public bool PendingRestart;
        public bool PendingResume;
        public int PendingSell = -1;
        /// <summary>种子袋被点击的槽位（-1 无）。</summary>
        public int PendingSlotClick = -1;
        /// <summary>商店"合成"按钮：自动合成所有可合成的一对。</summary>
        public bool PendingMergeAll;
        /// <summary>v11 抽卡：选中的卡（-1 无）与重抽请求。</summary>
        public int PendingRewardPick = -1;
        /// <summary>v15: shop offer lock toggle (-1 none).</summary>
        public int PendingOfferLock = -1;
        public int PendingRewardSelect = -1;
        public bool PendingRewardReroll;
        /// <summary>v9：对某个槽位点"合成"（找一个相同的种子合成）。</summary>
        public int PendingSlotMerge = -1;
        /// <summary>Tab 切换：左侧按键说明是否显示。</summary>
        public bool KeyHelpVisible = true;

        /// <summary>v47：物体旁浮动文字的样式（懒创建，字号随每条提示变化）。</summary>
        private GUIStyle _popup;

        private static float UiScale
        {
            get
            {
                TuningDef t = TableDatabase.Tuning;
                return t != null && t.ui != null && t.ui.scale > 0.1f ? t.ui.scale : 1f;
            }
        }

        private static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.09f, 0.82f);
        private static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f, 0.9f);

        // ── v83：收纳格 / 卡片底色改用**暖棕** ──
        // 旧版是冷灰蓝 (0.16, 0.18, 0.22 一类)。在一整套奶油木色的界面里，
        // 那些格子像从别的游戏里抠过来的黑洞——尤其商店与菜单里成排的种子格，
        // 整片黑压压的。底色跟着主色走（木色系），界面才是一体的。
        // 明度仍然压得比面板低，保证"凹进去的收纳格"这个语义没丢。
        private static readonly Color SlotBg = new Color(0.27f, 0.20f, 0.14f, 0.98f);        // 空格
        private static readonly Color SlotBgFilled = new Color(0.21f, 0.16f, 0.11f, 0.98f);  // 有东西
        private static readonly Color CardBg = new Color(0.24f, 0.18f, 0.12f, 0.96f);        // 商品卡
        private static readonly Color IconWellBg = new Color(0.32f, 0.24f, 0.16f, 1f);       // 图标底衬
        private static readonly Color ItemTileSel = new Color(0.40f, 0.30f, 0.17f, 1f);      // 道具格·选中

        // ── v96：卷轴纸纹（需求"为棕色的框增加美术效果，增加纹理模仿卷轴效果"）──
        //
        // 贴图本身是**灰度**的（均值 238，接近白），配合 GUI.color 相乘：
        // 棕色调上去就是"有纸纹的棕"。若贴图直接画成棕色，乘完会变脏黑。
        // 生成脚本：Tools/AssetPipeline/gen_scroll_tex.py（固定随机种子，可复现）
        private static Texture2D _paperTex;
        private static bool _paperTried;

        private static Texture2D PaperTex
        {
            get
            {
                if (!_paperTried)
                {
                    _paperTried = true;   // 失败也只试一次，避免每帧重复 Resources.Load
                    _paperTex = Resources.Load<Texture2D>("Art/UI/ui_scroll_paper");
                }
                return _paperTex;
            }
        }

        /// <summary>
        /// 用卷轴纸纹填充矩形。**平铺，不是拉伸** —— 一张 256px 的纸铺满 900px 宽的卡片，
        /// 拉伸会直接把纹路糊成渐变，而卷轴纹理的价值恰恰在纹路本身。
        /// 贴图取不到时退回纯色（`White` + 当前 GUI.color），界面不会因此坏掉。
        /// </summary>
        private void PaperFill(Rect r)
        {
            Texture2D tex = PaperTex;
            if (tex == null) { GUI.DrawTexture(r, White); return; }
            const float Tile = 128f;   // 一"张"纸在屏幕上的边长（像素）
            GUI.DrawTextureWithTexCoords(r, tex,
                new Rect(0f, 0f, Mathf.Max(1f, r.width / Tile), Mathf.Max(1f, r.height / Tile)));
        }

        /// <summary>
        /// v105：把一块棕色背景画成**卷轴的形**（需求"棕色的背景增加卷轴形状和纹理图案"）。
        ///
        /// 纹理上一轮已经做了（PaperFill 平铺），缺的是"形"——
        /// 光有纸纹只是"一张旧纸"，要读成**卷轴**必须有**两端卷起来的轴**。
        ///
        /// 做法（由外到内，全部纯绘制、不改布局）：
        ///   ① 左右各一根**轴杆**：比底色略亮的竖条，两端收窄模拟卷筒的弧面
        ///   ② 轴杆内侧一道**压痕**：暗线，表示纸绕在轴上的转折
        ///   ③ 上下各一道**边缘暗线**：卷轴的两条长边
        /// 只在宽度够的时候画轴杆（窄条上画两根杆会挤掉内容）。
        /// </summary>
        private void ScrollFrame(Rect r, float alpha = 1f)
        {
            if (r.width < 120f || r.height < 18f) return;   // 太窄：退回纯色块，别硬塞装饰

            Color prev = GUI.color;
            float barW = Mathf.Clamp(r.height * 0.16f, 4f, 11f);

            // ① 轴杆：比底色亮一点，两端各收 1px 做出"卷筒"的收口
            GUI.color = new Color(0.62f, 0.48f, 0.32f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(r.x, r.y, barW, r.height), White);
            GUI.DrawTexture(new Rect(r.xMax - barW, r.y, barW, r.height), White);
            GUI.color = new Color(0.78f, 0.64f, 0.44f, 0.42f * alpha);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, barW - 2f, r.height - 2f), White);
            GUI.DrawTexture(new Rect(r.xMax - barW + 1f, r.y + 1f, barW - 2f, r.height - 2f), White);

            // ② 轴杆内侧的压痕（纸绕上去的转折）
            GUI.color = new Color(0.10f, 0.07f, 0.04f, 0.42f * alpha);
            GUI.DrawTexture(new Rect(r.x + barW, r.y, 1.5f, r.height), White);
            GUI.DrawTexture(new Rect(r.xMax - barW - 1.5f, r.y, 1.5f, r.height), White);

            // ③ 上下长边的暗线
            GUI.color = new Color(0.10f, 0.07f, 0.04f, 0.30f * alpha);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1.5f), White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), White);

            GUI.color = prev;
        }

        /// <summary>稀有度色：白 / 绿 / 蓝 / 紫 / 金（普通 / 一般 / 稀有 / 史诗 / 传奇）。
        /// 必须与 Rarity.Names 一一对应——两张表对不上号就是"传奇显示成紫色"那次的成因。</summary>
        public static Color QualityColor(int quality)
        {
            switch (quality)
            {
                case 0: return new Color(0.86f, 0.86f, 0.86f);
                case 1: return new Color(0.35f, 0.85f, 0.35f);
                case 2: return new Color(0.35f, 0.65f, 1f);
                case 3: return new Color(0.72f, 0.42f, 0.95f);
                default: return new Color(1f, 0.82f, 0.25f);
            }
        }

        private Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                return _white;
            }
        }

        private Font Font
        {
            get
            {
                if (_font != null) return _font;
                string[] candidates = { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial Unicode MS", "Malgun Gothic" };
                foreach (string name in candidates)
                {
                    Font f = Font.CreateDynamicFontFromOSFont(name, 16);
                    if (f != null) { _font = f; break; }
                }
                return _font;
            }
        }

        private GUIStyle Style(ref GUIStyle style, int size, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            if (style != null) return style;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor,
                wordWrap = false
            };
            if (Font != null) style.font = Font;
            // v102：默认文字色 白 → **墨棕**。
            //
            // 界面在 v74 前后整体改成了奶油/木色底（Panel 系列），但样式的默认色还是白色，
            // 于是所有的面板标题、属性行、种子卡信息都变成了"亮底上的白字"——
            // 能看见，但对比度极低，读起来发飘。玩家反馈的就是这个。
            //
            // 为什么敢改默认值：现在的界面**绝大部分面积是亮底**，
            // 而少数暗面（深色条、弹出文字）在代码里本来就是**逐处显式设色**的
            // （见 WrapSmall / OnLight / DrawPopups 的 style.normal.textColor 赋值）。
            // 也就是说：改默认值影响的是"没被显式指定"的那些，而那些恰好都落在亮底上。
            style.normal.textColor = new Color(0.26f, 0.19f, 0.13f);
            return style;
        }

        // ------------------------------------------------------------ v11 升级抽卡（老虎机）
        private void DrawLevelReward(RunSession s)
        {
            Color prevBg = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, _virtualW, _virtualH), White);
            GUI.color = prevBg;

            float w = Mathf.Min(_virtualW - 80f, 880f);
            float h = 420f;
            Rect r = new Rect((_virtualW - w) * 0.5f, (_virtualH - h) * 0.5f, w, h);
            Panel(r);
            GUI.Label(new Rect(r.x, r.y + 10, w, 34), "升级奖励 · 抽卡", _title);
            GUI.Label(new Rect(r.x, r.y + 48, w, 24),
                $"第 {Mathf.Min(s.RewardDrawIndex + 1, s.RewardDrawTotal)}/{Mathf.Max(1, s.RewardDrawTotal)} 次　（相同属性已融合）", _center);

            int n = Mathf.Max(1, s.RewardCards.Count);
            float cardW = Mathf.Min(240f, (w - 80f) / n - 12f);
            float totalW = n * cardW + (n - 1) * 16f;
            float x0 = r.x + (w - totalW) * 0.5f;

            // v66：发牌动画。IMGUI 里没有"每帧逻辑"的天然位置——
            // 用 EventType.Layout 保证一帧只推进一次（Draw 一帧会被 Layout/Repaint 调用多次，
            // 直接加 dt 会让动画速度变成两三倍）。
            if (Event.current != null && Event.current.type == EventType.Layout)
                _rewardT += Time.deltaTime;
            // 换了一批牌（数量或等级变了）就重放动画
            int sig = s.RewardCards.Count * 100003 + s.Progress.Level;
            if (sig != _rewardSig) { _rewardSig = sig; _rewardT = 0f; }

            for (int i = 0; i < s.RewardCards.Count; i++)
            {
                RunSession.LevelRewardCard card = s.RewardCards[i];
                Rect baseRect = new Rect(x0 + i * (cardW + 16f), r.y + 90, cardW, 200);

                // 逐张延迟入场：从下方 150px 滑入 + 淡入（缓出曲线）
                float delay = i * 0.09f;
                float k = Mathf.Clamp01((_rewardT - delay) / 0.30f);
                float ease = 1f - (1f - k) * (1f - k);
                Rect c = new Rect(baseRect.x, baseRect.y + (1f - ease) * 150f, baseRect.width, baseRect.height);
                float alpha = Mathf.Clamp01(k * 1.7f);

                Color topColor = card.Copies >= 3 ? new Color(1f, 0.82f, 0.25f)
                               : card.Copies == 2 ? new Color(0.72f, 0.42f, 0.95f)
                               : new Color(0.35f, 0.65f, 1f);

                Color pv = GUI.color;
                GUI.color = new Color(CardBg.r, CardBg.g, CardBg.b, CardBg.a * alpha);
                GUI.DrawTexture(c, White);
                GUI.color = new Color(topColor.r, topColor.g, topColor.b, alpha);
                GUI.DrawTexture(new Rect(c.x, c.y, c.width, 6), White);

                // 落位闪光：k 刚过 1 时闪一圈白，模拟"啪"地扣在桌上
                float land = Mathf.Clamp01(1f - Mathf.Abs(k - 1f) * 7f);
                if (land > 0.01f)
                {
                    GUI.color = new Color(1f, 1f, 0.95f, land * 0.55f);
                    GUI.DrawTexture(new Rect(c.x - 3f, c.y - 3f, c.width + 6f, c.height + 6f), White);
                }

                bool selected = s.RewardSelected == i;
                if (selected)
                {
                    // 选中脉冲：边框随正弦呼吸
                    float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 6f);
                    GUI.color = new Color(0.55f, 0.85f, 0.40f, pulse * alpha);
                    GUI.DrawTexture(new Rect(c.x - 5f, c.y - 5f, c.width + 10f, c.height + 10f), White);
                    GUI.color = new Color(CardBg.r, CardBg.g, CardBg.b, CardBg.a * alpha);
                    GUI.DrawTexture(c, White);
                    GUI.color = new Color(topColor.r, topColor.g, topColor.b, alpha);
                    GUI.DrawTexture(new Rect(c.x, c.y, c.width, 6), White);
                }
                GUI.color = pv;

                // 文字也按 alpha 淡入
                Color titlePrev = _title.normal.textColor;
                Color centerPrev = _center.normal.textColor;
                _title.normal.textColor = new Color(titlePrev.r, titlePrev.g, titlePrev.b, alpha);
                _center.normal.textColor = new Color(centerPrev.r, centerPrev.g, centerPrev.b, alpha);
                GUI.Label(new Rect(c.x + 10, c.y + 16, c.width - 20, 30), card.Label, _title);
                if (card.Copies > 1)
                    GUI.Label(new Rect(c.x + 10, c.y + 56, c.width - 20, 24), $"融合 ×{card.Copies}", _center);
                _title.normal.textColor = titlePrev;
                _center.normal.textColor = centerPrev;

                if (GUI.Button(new Rect(c.x + 14, c.y + 140, c.width - 28, 44), selected ? $"已选中（{i + 1}）" : $"选中（{i + 1}）"))
                    PendingRewardSelect = i;
            }

            bool hasPick = s.RewardSelected >= 0 && s.RewardSelected < s.RewardCards.Count;
            Color pc = GUI.color;
            if (!hasPick) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            if (GUI.Button(new Rect(r.x + w * 0.5f - 250, r.y + h - 62, 220, 42),
                hasPick ? "确认（空格）：" + s.RewardCards[s.RewardSelected].Label : "请先选一张卡") && hasPick)
                PendingRewardPick = s.RewardSelected;
            GUI.color = pc;

            // v13：重抽消耗金币（无限次，价格递增）
            int rerollCost = s.RewardRerollCost;
            bool canReroll = s.Gold >= rerollCost;
            Color pr = GUI.color;
            if (!canReroll) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            if (GUI.Button(new Rect(r.x + w * 0.5f + 30, r.y + h - 62, 240, 42),
                $"重抽 {rerollCost} 金币（R·金币 {s.Gold}）") && canReroll)
                PendingRewardReroll = true;
            GUI.color = pr;
        }

        // ------------------------------------------------------------ v10 商店（左：商品 / 左下：持有物资 / 右：属性）
        private void DrawShop(RunSession s)
        {
            float pad = 12f;
            float top = 84f;
            float bottom = _virtualH - 30f;
            float leftW = _virtualW * 0.50f;
            float rightX = pad + leftW + pad;
            float rightW = _virtualW - rightX - pad;
            float shopH = (bottom - top) * 0.62f;

            // 左·上：商店商品
            Rect shop = new Rect(pad, top, leftW, shopH);
            Panel(shop);
            GUI.Label(new Rect(shop.x, shop.y + 2, shop.width, 30), $"商店　金币 {s.Gold}", _bold);
            // v83：商品改**一行一件的「左图右文」**。
            //
            // 旧版是 4 列窄卡 + 下方独立详情区，有两个毛病：
            //   ① 卡片只有 ~200px 宽，名字/品质/价格被挤成三行，长名字还会被裁
            //   ② 描述放不下，只能挪到下方——于是"看商品"和"看说明"要上下扫视，
            //      而且详情区谁都能触发（悬停哪张显示哪张），玩家经常看串
            // 现在一行一件：左边图标+名字+品质+价格，中间整块宽度给描述，右边操作按钮。
            // 菜单里的种子条目也用同一套排布，界面才是一套语言。
            int offerCount = Mathf.Min(4, s.Shop.Offers.Count);
            float rowTop = shop.y + 30f;
            float rowAreaH = shop.yMax - 48f - rowTop;
            float rowH = offerCount > 0 ? rowAreaH / offerCount : 0f;
            for (int i = 0; i < offerCount; i++)
            {
                ShopOffer o = s.Shop.Offers[i];
                Rect row = new Rect(shop.x + 8f, rowTop + i * rowH, shop.width - 16f, rowH - 4f);
                Color prev = GUI.color;

                GUI.color = o.Kind == OfferKind.Sold ? new Color(0.17f, 0.14f, 0.11f, 0.95f) : CardBg;
                PaperFill(row);   // v96：卷轴纸纹
                ScrollFrame(row); // v105：卷轴的形（两端轴杆 + 压痕 + 长边暗线）
                GUI.color = QualityColor(o.Quality);
                GUI.DrawTexture(new Rect(row.x, row.y, 5f, row.height), White);   // 左侧品质色条
                GUI.color = prev;

                if (o.Kind == OfferKind.Sold)
                {
                    GUI.color = new Color(0.55f, 0.48f, 0.40f, 1f);
                    GUI.Label(new Rect(row.x + 16f, row.y + row.height * 0.5f - 12f, row.width - 32f, 24f),
                        "已售出　—　刷新商店换货", _small);
                    GUI.color = prev;
                    continue;
                }

                // ── 左：图标 + 名字 + 品质 + 价格 ──
                float iconSize = Mathf.Clamp(row.height - 16f, 34f, 56f);
                Rect icon = new Rect(row.x + 12f, row.y + (row.height - iconSize) * 0.5f, iconSize, iconSize);
                DrawOfferIcon(icon, QualityColor(o.Quality), IconResOf(o));
                float tx = icon.xMax + 10f;
                GUI.Label(new Rect(tx, row.y + 5f, 132f, 22f), o.Label(), OnDarkBold);
                GUI.Label(new Rect(tx, row.y + 26f, 132f, 20f), GradeText(o), OnDark);
                GUI.Label(new Rect(tx, row.y + 44f, 132f, 20f), $"价格 {o.Price}", OnDark);

                // ── 中：描述详情（整块宽度，可滚动）──
                float btnW = 106f;
                float descX = tx + 138f;
                float descW = row.xMax - btnW - 16f - descX;
                if (descW > 80f)
                {
                    Rect view = new Rect(descX, row.y + 5f, descW, row.height - 10f);
                    DrawInfoList(view, OfferDetailLines(s, o), i, _offerScrollPerRow, "商店详情" + i);
                }

                // ── 右：操作按钮 ──
                bool afford = s.Gold >= o.Price;
                Color pv = GUI.color;
                if (!afford) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
                if (GUI.Button(new Rect(row.xMax - btnW - 8f, row.y + row.height * 0.5f - 30f, btnW, 28f),
                        $"购买（{i + 1}）") && afford)
                    PendingBuyOffer = i;
                GUI.color = pv;
                Color pl = GUI.color;
                if (o.Locked) GUI.color = new Color(1f, 0.85f, 0.35f, 1f);
                if (GUI.Button(new Rect(row.xMax - btnW - 8f, row.y + row.height * 0.5f + 2f, btnW, 26f),
                        o.Locked ? "已锁定（⇧）" : "锁定（⇧）"))
                    PendingOfferLock = i;
                GUI.color = pl;

                if (row.Contains(Event.current.mousePosition)) _offerShown = i;
            }

            GUI.Label(new Rect(shop.x + 10, shop.yMax - 34, 200, 26), $"刷新商店　{s.Shop.RerollCost(s.T)}", _small);
            if (GUI.Button(new Rect(shop.x + 120, shop.yMax - 38, 160, 32), "刷新商店（R）")) PendingReroll = true;
            // **一键合成**：把袋里所有"同种同级"的对子一路合到 3 级（旧版合成规则的批量形式）✓
            // 单张种子卡上的"合成"是按槽位的，袋子满起来时要一个个点 ⇒ 补一个批量入口 ✓
            bool anyPair = s.Bag.HasMergeablePair();
            Color pmAll = GUI.color;
            if (!anyPair) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
            if (GUI.Button(new Rect(shop.x + 288, shop.yMax - 38, 150, 32), "一键合成（M）") && anyPair)
                PendingMergeAll = true;
            GUI.color = pmAll;
            // v154：计时制下这里不再"推进回合"，只是**解除暂停继续游戏** ✓
            // 需求："商店中下一波按钮修改为继续"
            if (GUI.Button(new Rect(shop.xMax - 200, shop.yMax - 38, 190, 32), "继续（空格）")) PendingNextWave = true;

            // 左·下：持有物资（种子 / 道具 分开）
            Rect inv = new Rect(pad, top + shopH + pad, leftW, bottom - (top + shopH + pad));
            Panel(inv);
            float half = inv.width * 0.5f - 12f;
            GUI.Label(new Rect(inv.x + 10, inv.y + 2, half, 26), $"种子（{s.Bag.OccupiedCount}/{s.Bag.SlotCount}）", _bold);
            // v16：种子格放大 + 名称下方展示功能与数值 + 滑动条（空间不足时滚动）
            float cellW = Mathf.Max(150f, half - 26f);
            float cellH = 220f;   // v19：更高，信息区与按钮各占其位
            Rect seedView = new Rect(inv.x + 8, inv.y + 28, half + 4, inv.height - 34f);
            float seedContentH = Mathf.Max(seedView.height, s.Bag.SlotCount * (cellH + 8f) + 8f);
            _seedScroll = GUI.BeginScrollView(seedView, _seedScroll, new Rect(0, 0, seedView.width - 18, seedContentH));
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot slot = s.Bag[i];
                Rect cell = new Rect(2, i * (cellH + 8f), cellW, cellH);
                Color prev = GUI.color;
                GUI.color = slot.IsEmpty ? SlotBg : SlotBgFilled;
                if (i == s.MergeSelected) GUI.color = new Color(0.42f, 0.36f, 0.16f, 0.98f);
                PaperFill(cell);   // v96：卷轴纸纹（选中态也走同一张纹，只是着色不同）
                GUI.color = prev;
                if (slot.IsEmpty)
                {
                    GUI.Label(new Rect(cell.x + 8, cell.y + 6, cell.width - 16, 24), $"第 {i + 1} 格　空", _small);
                    continue;
                }
                GUI.color = QualityColor(slot.Quality);
                GUI.DrawTexture(new Rect(cell.x, cell.y, cell.width, 5), White);
                GUI.color = prev;
                // v19：统一卡片——信息区自带滑动条，底部按钮独立成区（不再压文字）
                ButtonReserve = 28f;   // 底部按钮行的高度：合成 + 出售 并排
                SeedCardPanel(s, slot, cell, i, _seedScrollPerCard);
                ButtonReserve = 0f;

                // ── 底部按钮行：**合成** + 出售 ──
                // v44 曾把这一行删到只剩出售 ✗ 合成回归后两个按钮**并排**各占半行 ✓
                int otherSlot;
                bool canMerge = s.Bag.CanMergeWithOther(i, out otherSlot);
                bool maxed = slot.Level >= SeedSlot.MaxLevel;
                float btnY = cell.yMax - 25f;
                float btnW2 = (cell.width - 18f) * 0.5f;

                Color pm = GUI.color;
                if (!canMerge) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
                // 按钮文案直接说清"点了会变成几级"与"为什么点不动" ✗
                // 只写"合成"的话，玩家不知道要凑同种同级，只会反复点一个灰按钮 ✓
                string mergeText = canMerge ? $"合成 → {slot.Level + 1} 级"
                                 : maxed ? $"{SeedSlot.MaxLevel} 级封顶"
                                 : "合成（需同级）";
                if (GUI.Button(new Rect(cell.x + 6f, btnY, btnW2, 20f), mergeText) && canMerge)
                    PendingSlotMerge = i;
                GUI.color = pm;

                // v19：只剩 1 个种子时禁止出售
                bool canSell = s.Bag.OccupiedCount > 1;
                Color ps = GUI.color;
                if (!canSell) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
                if (GUI.Button(new Rect(cell.x + cell.width - btnW2 - 6f, btnY, btnW2, 20f),
                    canSell ? $"出售 {slot.SellValueFor(s.WaveIndex)}" : "至少保留 1 个") && canSell) PendingSell = i;
                GUI.color = ps;
            }
            GUI.EndScrollView();

            float ix = inv.x + half + 20;
            GUI.Label(new Rect(ix, inv.y + 2, half, 26), "道具", _bold);
            int shown = 0;
            foreach (var kv in s.ItemStacks)
            {
                ItemDef def = TableDatabase.GetItem(kv.Key);
                if (def == null) continue;
                GUI.Label(new Rect(ix, inv.y + 30 + shown * 24, half, 22),
                    $"{s_rarityName(def.RarityTier)} {def.Name} ×{kv.Value}", _small);
                shown++;
                if (shown >= 8) break;
            }
            if (shown == 0) GUI.Label(new Rect(ix, inv.y + 30, half, 22), "（还没有道具）", _small);

            // 右：★ v172 从"只有角色属性"改成"武器与插件 + 角色属性"★
            //
            // 用户标准（原话）："同步更新 UI 框，按照我说的标准，★ 预留更大的区域给角色武器，
            // 商店、菜单都是 ★"
            // ⇒ 商店是**买插件的地方**，而买插件时只有两件事要判断：
            //   ① 这把武器还剩几个槽（6 个格子画出来 ✓ 空槽也画 ✓）
            //   ② 这件插件装上会改哪一环（`WeaponSystem.ChannelOf` ✓ 框里的摘要行 ✓）
            // ⇒ 所以武器区是右栏的**主要部分**，属性退到下面（属性面板本身自带滚动 ✓ 矮也能用 ✓）
            //
            // 宽度为什么从 0.60 收到 0.50：右栏要给到 675px 才放得下"三把武器各一个 168px 的框"✗
            // 而左栏（货架 4 行 + 种子 / 道具）在 711px 下仍然够宽 ✓
            float statsH = 130f;
            float weaponH = bottom - top - statsH - pad;
            DrawWeaponSection(s, new Rect(rightX, top, rightW, weaponH), "我的武器与插件");
            Panel(new Rect(rightX, top + weaponH + pad, rightW, statsH));
            DrawStatsColumns(s, rightX, top + weaponH + pad, rightW, statsH, "角色属性");
        }

        /// <summary>v49：绝对值型属性 → 相对开局基准的增量（开局显示 +0）。公开以便自检直接复核。</summary>
        public static string AddFlat(float current, float baseValue)
        {
            float d = current - baseValue;
            return $"{(d >= 0f ? "+" : "")}{d:0.#}";
        }

        /// <summary>v49：比率/百分点型属性 → 差值 ×100（开局显示 +0%）。</summary>
        public static string AddPoints(float current, float baseValue)
        {
            int pct = Mathf.RoundToInt((current - baseValue) * 100f);
            return $"{(pct >= 0 ? "+" : "")}{pct}%";
        }

        /// <summary>
        /// v49：乘法成长的属性 → 相对开局的**涨幅**。
        /// 用涨幅而不是百分点差，是为了跟道具/卡面上写的百分比一致：
        /// 例如"暴击率 +5%"是 ×1.05，在 5% 的基数上只多 0.25 个百分点，按差值显示会变成 "+0%"。
        /// </summary>
        public static string AddRelative(float current, float baseValue)
        {
            if (baseValue <= 0.0001f) return AddPoints(current, baseValue);
            int pct = Mathf.RoundToInt((current / baseValue - 1f) * 100f);
            return $"{(pct >= 0 ? "+" : "")}{pct}%";
        }

        /// <summary>v49：面板改为全增量显示后，绝对值仍要可见——这里给出三把武器的实算伤害。</summary>
        private static string DamagePreviewLine(RunSession s, StatBlock st)
        {
            TuningDef t = TableDatabase.Tuning;
            // v162（A3）：三把武器各吃**自己的**伤害属性，公式 (基础 + 对应属性) × 伤害倍率
            float melee = st.Get(StatId.MeleeDamage);
            float ranged = st.Get(StatId.RangedDamage);
            float blastStat = st.Get(StatId.BlastDamage);
            float mult = st.Get(StatId.AtkMult);
            float hoe = DamageResolver.AttackFormula(t.tools.hoe.baseValue * s.ToolMult(RunSession.ToolHoe),
                melee, mult, false, 1f, 0f);
            float can = DamageResolver.AttackFormula(t.tools.can.baseValue * s.ToolMult(RunSession.ToolCan),
                ranged, mult, false, 1f, 0f);
            // v109：爆炸 = 肥料这件武器走一遍普通公式，再乘爆炸倍率（与运行时同一条路径）
            float fertN = DamageResolver.AttackFormula(t.tools.fertilizer.baseValue * s.ToolMult(RunSession.ToolFertilizer),
                blastStat, mult, false, 1f, 0f);
            float blast = DamageResolver.ExplodeFormula(fertN, st.Get(StatId.ExplodeDamage));
            return $"锄 {hoe:0.#}　壶 {can:0.#}　肥爆 {blast:0.#}";
        }

        /// <summary>主要 / 次要属性两段式展示（商店与菜单共用）。</summary>
        private void DrawStatsColumns(RunSession s, float x, float y, float w, float h, string title)
        {
            GUI.Label(new Rect(x + 12, y + 2, w - 20, 28), title, _bold);
            Rect view = new Rect(x + 4, y + 32, w - 8, h - 40);
            float contentH = 620f;
            _statContentW = Mathf.Max(120f, view.width - 24f);
            _statScroll = GUI.BeginScrollView(view, _statScroll, new Rect(0, 0, view.width - 18, contentH));
            StatBlock st = s.Progress.Stats;
            // v49：属性面板一律显示「相对开局基准的增量」——**开局全部是 +0 / +0%**。
            //      基准取自 tuning.player（开局值的唯一来源），不再在调用处写死数字：
            //      旧版把移动速度基准写成 5.0，基础移速改成 6.5 之后开局就显示成 "+150%"。
            //      乘法成长的属性按「相对涨幅」显示，这样与道具/卡面上写的 "+5%" 一致。
            PlayerBaseDef bs = TableDatabase.Tuning.player;
            float ly = 0f;
            ly = StatLine(ly, "— 主要属性 —", true);
            // v108：**最大生命 / 护盾用实际值**（需求）。
            // 其余属性是增量（"+5%"这种），但这两个是"我现在有多少血"——
            // 显示增量等于把玩家最需要的一个数藏起来，还得自己做加法。
            ly = StatLine(ly, $"最大生命　{st.Get(StatId.MaxHp):0}");
            ly = StatLine(ly, $"护盾　{st.Get(StatId.Shield):0}");
            // v162（A1/A2）：通用「攻击伤害」拆成三种独立伤害类型——锄头吃近战、水壶吃远程、肥料吃爆炸。
            // 三条各自独立成长，所以这里必须**分成三行**显示：
            // 合成一行的话，玩家买了只加近战的道具会看到"攻击伤害 +1"，
            // 然后发现水壶一点没变强——那是显示在撒谎。
            ly = StatLine(ly, $"近战伤害　{AddFlat(st.Get(StatId.MeleeDamage), bs.meleeDamage)}");
            ly = StatLine(ly, $"远程伤害　{AddFlat(st.Get(StatId.RangedDamage), bs.rangedDamage)}");
            ly = StatLine(ly, $"爆炸伤害　{AddFlat(st.Get(StatId.BlastDamage), bs.blastDamage)}");
            // v108：「攻击倍率」→「伤害倍率」（需求）。它乘的是 (基础伤害 + 攻击伤害) 整块，不只攻击那一项。
            // v162：现在乘的是 (基础伤害 + 对应伤害属性) 整块——三种武器各乘一次，语义不变。
            ly = StatLine(ly, $"伤害倍率　{AddRelative(st.Get(StatId.AtkMult), bs.atkMult)}");
            // v162（A2）：爆炸范围**上移进主要属性区**（需求）。
            //   它直接决定肥料那条主输出线的体量，与三种伤害类型同属"我打得多疼"，
            //   留在次要属性区会让玩家以为它只是边角加成。
            // ★ v173：与它同批上移的「爆炸倍率」一栏**已删**★ ——
            //   用户原话："属性删除爆炸倍率一栏 ☆ 爆炸伤害计算公式和其它是一样的" ✓
            //   ⚠️ 删掉的只是**这一栏显示**，不是那条属性 ✗：
            //     `StatId.ExplodeDamage` 仍有 4 个非 UI 读者
            //     （`RunSession.ExplodeAt` / `Projectile.Explode` / `SelfCheck` 两条断言 / `StatName` 的道具名表）⇒
            //     爆炸伤害仍然 = 肥料的普通伤害 × 爆炸倍率，只是不再在面板上单独占一行 ✓
            ly = StatLine(ly, $"爆炸范围　{AddRelative(st.Get(StatId.ExplodeRange), bs.explodeRange / 100f)}");
            ly = StatLine(ly, $"攻击速度　{AddRelative(st.Get(StatId.AtkSpeed), bs.atkSpeed)}");
            ly = StatLine(ly, $"攻击范围　{AddRelative(st.Get(StatId.AtkRange), bs.atkRange)}");
            ly = StatLine(ly, $"移动速度　{AddRelative(st.Get(StatId.MoveSpeed), bs.moveSpeed)}");
            ly = StatLine(ly, $"暴击率　{AddRelative(st.Get(StatId.CritRate), bs.critRate)}");
            ly = StatLine(ly, $"暴击伤害　{AddRelative(st.Get(StatId.CritDmg), bs.critDmg)}");
            ly = StatLine(ly, $"闪避率　{AddPoints(st.Get(StatId.Dodge), bs.dodge)}");
            // v107：幸运属性已删除（需求）
            ly = StatLine(ly, "— 次要属性 —", true);
            ly = StatLine(ly, $"击退力　{AddFlat(st.Get(StatId.Knockback), bs.knockback)}");
            ly = StatLine(ly, $"经验倍数　{AddRelative(st.Get(StatId.ExpMult), bs.expMult)}");
            ly = StatLine(ly, $"货币倍数　{AddRelative(st.Get(StatId.GoldMult), bs.goldMult)}");
            // v110：爱心掉落率是**等级制**（低/中/高/MAX），不是百分比增量。
            // 显示成"低（10%）"——玩家既要知道现在在哪一级，也要知道这一级值多少。
            {
                int hl = Mathf.RoundToInt(st.Get(StatId.HeartDropRate));
                ly = StatLine(ly, $"爱心掉落率　{TuningDef.HeartDropLevelNameOf(hl)}（{TuningDef.HeartDropRateOf(hl) * 100f:0}%）");
            }
            // v108：爱心恢复量同样用**实际值**（需求）——它是"捡一颗回几点血"，不是加成
            ly = StatLine(ly, $"爱心恢复量　{st.Get(StatId.HeartHeal):0.#}");
            // v108：删除「当前实算」整块（需求）。
            // 它原本是为了在"全屏增量"之后让绝对值仍可见而加的，
            // 但现在伤害/范围等都会在**各自的条目上直接写实算值**（见植物详情与武器行），
            // 这块就成了同一信息的第二个来源——两处数字一旦不同步，玩家不知道该信哪个。
            ly = StatLine(ly, "— 局面 —", true);
            ly = StatLine(ly, $"耕地　{s.World.Farm.PlotCount} 块");
            ly = StatLine(ly, $"耕地上限　{s.World.Farm.Capacity} 块（每升 1 级 +1）");
            // v161：计时制 —— 不再显示 "N / 20"，只显示当前是第几分钟 ✗
            ly = StatLine(ly, $"第 {s.WaveIndex} 分钟　游玩 {FmtTime(s.RunTime)}");
            ly = StatLine(ly, $"击杀　{s.Kills}");
            ly = StatLine(ly, $"金币　{s.Gold}");
            // v108：删除「生存植物」一栏（需求）—— 场上敌人数量已经能反映战况，
            // 而"生存植物/上限"这个比值玩家从不据此做决策。
            ly = StatLine(ly, $"场上敌人　{s.World.Enemies.Count}");
            ly = StatLine(ly, $"等级　{s.Progress.Level}");
            ly = StatLine(ly, $"用时　{s.RunTime:0}s");
            GUI.EndScrollView();
        }

        // ★ v173：原来这里有一个 `SeedCardLines(RunSession, SeedSlot)` —— **整段已删** ✗ ★
        //
        //   它是"商店条目 / 商店详情 / 开局选种"三处的种子卡正文，与主界面那 6 个格子走的
        //   `PlantCellInfoLines` **各写了一份** ⇒ 两份的字段集不一致：
        //   这一份 15 行（含 8 个战斗属性），那一份 7 项 ✗
        //   用户标准："植物只显示：名字、品质、等级、金币产出量、经验产出量、生长时间、特殊功能" ✓
        //   ⇒ 现在**只有一个生产者**（`PlantCellInfoLines`），两处调用直接指向它 ✓
        //      "改了一处、另一处还在显示旧属性"从此在结构上不可能发生 ✓
        //
        //   连同删除的行（按**代码符号**列，便于回溯）：
        //     伤害算式（`SeedDef.BaseValue` × 品质倍率 + `StatId.MeleeDamage`）/
        //     `SeedDef.AttackInterval` / `SeedDef.BaseCritRate` + `StatId.CritRate` /
        //     `SeedDef.BaseCritDmg` / `SeedDef.Range` × `StatId.AtkRange` / `StatId.Knockback` /
        //     `SeedDef.BaseHp` / `SeedDef.BaseShield` / `SeedDef.MoveSpeed` /
        //     攻击模式（`RunSession.AtkTypeText`）/ 功能（`SeedDef.Desc`）/
        //     卖出返还（`SeedSlot.LevelYieldMult`，与上面"收益"是同一个数）✓
        //   ★ 植物自 v165 起**不参战** ⇒ 上面这些战斗数没有一个影响它在地里的行为 ✓
        //   ⚠️ `AttackType` / `AttackKind` / `Support` **不是**死字段：`RunSession.AtkTypeText` 等
        //      三个文案函数与自检仍在读（商店副标题在用）✓

        /// <summary>
        /// v19：统一的种子卡（商店 / 菜单 / 开局选种共用）。
        /// 结构：图标区 → 名称 → 稀有度 → **独立滑动条的信息区** → 底部按钮区（按钮不压文字）。
        /// </summary>
        /// <summary>
        /// 种子条目（菜单 / 种子选择 / 库存三处共用）。
        ///
        /// v84：改成与商店条目**同一套「左图右文」**——
        ///   左：图标 + 名字 + 品质（固定宽度）
        ///   右：整块剩余宽度给描述详情（自带滑动条）
        /// 顺带把品质色条从**顶部**挪到**左侧**：商店的行是左条，
        /// 两处不统一的话，同一屏里会出现两种"品质标记位置"，玩家得重新学一遍。
        ///
        /// 为什么这次敢动这个被三处复用的函数：新布局只依赖 `card` 这个矩形本身，
        /// 不再假设"卡片一定是纵向窄条"——三处调用点各自给不同的宽高都能正确排布。
        /// 旧版把图标居中、文字居中，宽度一变就散架，这才是它一直没被统一的原因。
        /// </summary>
        private void SeedCardPanel(RunSession s, SeedSlot slot, Rect card, int scrollIndex, float[] scrollStore)
        {
            Color pv0 = GUI.color;
            GUI.color = slot.IsEmpty ? SlotBg : SlotBgFilled;
            PaperFill(card);   // v96：卷轴纸纹（种子卡 / 选种卡 / 库存卡共用）
            ScrollFrame(card); // v105：卷轴的形
            GUI.color = QualityColor(slot.Quality);
            GUI.DrawTexture(new Rect(card.x, card.y, 5f, card.height), White);   // 左侧品质条
            GUI.color = pv0;

            // ── 左：图标 + 名字 + 品质（**纵向堆叠**）──
            // ★ 名字不能和图标并排 ★
            // 第一版是"图标在左、名字在右"，但卡片宽度只有 ~350px 时
            // 左列算出来只剩 40px —— 装不下"豌豆射手"四个字，文字直接压到描述区上。
            // 纵向堆叠对窄卡天然安全：名字拿到整列宽度，与卡片宽度解耦。
            float leftW = Mathf.Clamp(card.width * 0.30f, 96f, 156f);
            float iconS = Mathf.Clamp(leftW - 36f, 36f, 74f);
            Rect icon = new Rect(card.x + (leftW - iconS) * 0.5f + 6f, card.y + 12f, iconS, iconS);
            DrawOfferIcon(icon, QualityColor(slot.Quality),
                slot.Def != null ? "icon_plant_" + slot.Def.Id : null);

            GUI.Label(new Rect(card.x + 8f, icon.yMax + 4f, leftW - 12f, 24f), slot.Label, OnDarkCenter);
            GUI.Label(new Rect(card.x + 8f, icon.yMax + 26f, leftW - 12f, 20f),
                s_rarityName(slot.Quality), OnDarkCenter);
            // 等级单独一行（品质是"哪一品级的种子"，等级是"这一个养到第几级"，两个正交维度）✗
            if (!slot.IsEmpty && slot.Level > SeedSlot.StartingLevel)
                GUI.Label(new Rect(card.x + 8f, icon.yMax + 46f, leftW - 12f, 20f),
                    $"{slot.Level} 级", OnDarkCenter);

            // ── 右：描述详情（整块剩余宽度）──
            float bottomReserve = ButtonReserve > 0f ? ButtonReserve + 8f : 8f;
            Rect descView = new Rect(card.x + leftW + 6f, card.y + 8f,
                Mathf.Max(60f, card.width - leftW - 14f), Mathf.Max(24f, card.height - bottomReserve - 16f));
            // v48/v51：统一走 DrawInfoList —— 自动换行 + 按实测行数算内容高度（超宽行会折行并计入高度）
            if (!slot.IsEmpty)
                DrawInfoList(descView, PlantCellInfoLines(slot), scrollIndex, scrollStore, "种子卡");
            else
                GUI.Label(descView, "空格", _small);
        }

        /// <summary>v89：**亮底专用**的正文样式（棕色字）。
        /// 高亮/浅色背景上不要用白字——发飘、对比度低。这一条只影响调用处显式传入的地方，
        /// 不会把深色卡面上的浅字一起改掉。</summary>
        private GUIStyle _onLight;
        private GUIStyle OnLight
        {
            get
            {
                if (_onLight == null)
                {
                    _onLight = new GUIStyle(_small);
                    _onLight.normal.textColor = new Color(0.21f, 0.15f, 0.10f);
                }
                return _onLight;
            }
        }

        /// <summary>
        /// v102：**暗面专用**的浅色文字样式。
        ///
        /// 把默认文字色改成墨棕之后（为亮底服务），画在**棕色卡片**上的那几处
        /// 变成了深棕压深棕 —— 商品行左列的名字/品质/价格、种子卡的名字几乎看不见。
        /// 这一次的教训和 v74 那次一模一样：**共用样式的默认色一改，两个方向总有一边被打坏**。
        /// 所以不再"挑一个能兼顾的颜色"，而是把两种底各给一个明确的样式：
        ///   亮底（奶油面板）→ 默认墨棕
        ///   暗底（棕色卡片）→ OnDark（暖白）
        /// 调用处**必须显式选边**，不能再依赖"默认值碰巧合适"。
        /// </summary>
        private GUIStyle _onDark;
        private GUIStyle OnDark
        {
            get
            {
                if (_onDark == null)
                {
                    _onDark = new GUIStyle(_small);
                    _onDark.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _onDark;
            }
        }

        private GUIStyle _onDarkBold;
        private GUIStyle OnDarkBold
        {
            get
            {
                if (_onDarkBold == null)
                {
                    _onDarkBold = new GUIStyle(_bold);
                    _onDarkBold.normal.textColor = new Color(0.97f, 0.94f, 0.87f);
                }
                return _onDarkBold;
            }
        }

        private GUIStyle _onDarkCenter;
        private GUIStyle OnDarkCenter
        {
            get
            {
                if (_onDarkCenter == null)
                {
                    _onDarkCenter = new GUIStyle(_center);
                    _onDarkCenter.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _onDarkCenter;
            }
        }

        /// <summary>v48：种子卡信息区专用样式——自动换行，避免长句被裁掉右半。</summary>
        private GUIStyle _wrapSmall;
        private GUIStyle WrapSmall
        {
            get
            {
                if (_wrapSmall == null)
                {
                    _wrapSmall = new GUIStyle(_small) { wordWrap = true };
                    _wrapSmall.fontSize = _small.fontSize;
                    _wrapSmall.padding = new RectOffset(0, 0, 0, 0);
                    // v74：正文框底色改成奶油木色后，正文必须是深色。
                    // 只改这一个样式——_small 在多处复用（有深色卡、有奶油面板），
                    // 全局改色必然把其中一半改成看不见。WrapSmall 恰好只用在文本框区域。
                    _wrapSmall.normal.textColor = new Color(0.24f, 0.19f, 0.14f);
                }
                return _wrapSmall;
            }
        }

        /// <summary>
        /// v51：HUD 文本溢出审计开关。由 DebugApi.HudAudit() 置真，下一次 OnGUI 会把每个信息区
        /// 的每行文本实测宽度与可用宽度比对，超宽的记进 AuditFindings 并打日志。
        /// 有了它，"哪里显示不全"就不再靠肉眼找了。
        /// </summary>
        public static bool AuditRequested;
        public static readonly System.Collections.Generic.List<string> AuditFindings =
            new System.Collections.Generic.List<string>();

        /// <summary>一行文本在给定宽度下要占几行；审计期间记录超宽行。</summary>
        private int RowsFor(string text, float availWidth, string where)
        {
            float avail = Mathf.Max(40f, availWidth);
            float need = WrapSmall != null ? WrapSmall.CalcSize(new GUIContent(text ?? "")).x : avail;
            int rows = Mathf.Max(1, Mathf.CeilToInt(need / avail));
            if (AuditRequested && need > avail + 0.5f)
                AuditFindings.Add($"{where}：需要 {need:0}px / 可用 {avail:0}px（折 {rows} 行）「{text}」");
            return rows;
        }

        /// <summary>
        /// v51：所有"信息列表"详情区的统一绘制口——自动换行、按实测行数算内容高度、带独立滚动条。
        /// 旧版每个详情区各写一遍，换行与内容高度算法不一致，于是有的地方被裁、有的滚不到底。
        /// </summary>
        private void DrawInfoList(Rect view, string[] lines, int scrollIndex, float[] scrollStore, string where)
        {
            if (lines == null || lines.Length == 0 || view.height <= 20f) return;
            const float lineH = 22f;
            float avail = Mathf.Max(40f, view.width - 16f);

            var heights = new float[lines.Length];
            float total = 0f;
            for (int k = 0; k < lines.Length; k++)
            {
                heights[k] = RowsFor(lines[k], avail, where + "#" + k) * lineH;
                total += heights[k];
            }
            float contentH = Mathf.Max(view.height, total + 8f);

            float sc = scrollStore != null && scrollIndex >= 0 && scrollIndex < scrollStore.Length
                ? scrollStore[scrollIndex] : 0f;

            // v71：正文框底。改这一处，全界面所有滚动正文区一起生效
            // （种子卡、商店详情、属性栏、菜单都走这里）。
            // 旧版是各调用点自己画一块纯色方块——颜色还不完全一致。
            UiSkin.Ensure();
            if (UiSkin.TextBoxStyle != null && UiSkin.TextBoxStyle.normal.background != null)
                GUI.Box(view, GUIContent.none, UiSkin.TextBoxStyle);

            sc = GUI.BeginScrollView(view, new Vector2(0f, sc), new Rect(0, 0, view.width - 14f, contentH)).y;
            if (scrollStore != null && scrollIndex >= 0 && scrollIndex < scrollStore.Length) scrollStore[scrollIndex] = sc;

            float y = 0f;
            for (int k = 0; k < lines.Length; k++)
            {
                GUI.Label(new Rect(2, y, avail, heights[k] - 2f), lines[k], WrapSmall);
                y += heights[k];
            }
            GUI.EndScrollView();
        }
        private static string StatName(string statId)
        {
            switch (statId)
            {
                case "MaxHp": return "最大生命";
                case "Shield": return "护盾";
                case "MeleeDamage": return "近战伤害";
                case "RangedDamage": return "远程伤害";
                case "BlastDamage": return "爆炸伤害";
                case "AtkMult": return "伤害倍率";
                // v162：ExplodeDamage 是**倍率**（初始 2.5 = 250%），不叫「爆炸伤害」——
                //       那个名字现在归 BlastDamage（加法的整数伤害）。两条同名会让玩家以为面板写重了。
                case "ExplodeDamage": return "爆炸倍率";
                case "ExplodeRange": return "爆炸范围";
                case "AtkSpeed": return "攻击速度";
                case "AtkRange": return "攻击范围";
                case "MoveSpeed": return "移动速度";
                case "CritRate": return "暴击率";
                case "CritDmg": return "暴击伤害";
                case "Knockback": return "击退力";
                case "Dodge": return "闪避率";
                case "Luck": return "幸运";
                case "ExpMult": return "经验倍数";
                case "GoldMult": return "货币倍数";
                case "HeartDropRate": return "爱心掉落率";
                case "HeartHeal": return "爱心恢复量";
                // v166：`StatId.MaxPlots` 的语义已从"耕地块数"变成"**可种植株数**"（= 田地列数 6）——
                // 因为新玩法"一株植物独占一列"，而种子袋容量 = 这个值（`RunSession:1892` 每帧强制同步）✓
                // 显示名不改的话，玩家会在发牌界面看到"最大耕地 +1"，而它其实与"几块地"无关 ✗
                case "MaxPlots": return "可种植株数";
                default: return statId;
            }
        }

        /// <summary>
        /// 稀有度名。**必须读 Rarity 表，不要在这里另写一份**。
        ///
        /// v97：这里原本是 { 普通, 稀有, 史诗, 传奇, 神话 }，而 QualityColor 画的是
        /// 白/绿/蓝/紫/金 —— 两张表对同一个编号的解释**不一致**：
        ///   quality 3 → 颜色是紫，名字却是"传奇"（应为"史诗"）
        ///   quality 2 → 颜色是蓝，名字却是"史诗"（应为"稀有"）
        /// 玩家看到的就是"传奇显示成紫色"。
        /// 现在统一走 Rarity.NameOf，与 Defs.cs 的颜色表一一对应。
        /// </summary>
        private static string s_rarityName(int q) => Rarity.NameOf(q);

        /// <summary>
        /// v100：商店商品的**类别 + 品质**（需求"商店商品增加类别描述（植物/道具）"）。
        ///
        /// 旧版这一行对道具写"武器"、对种子写稀有度 —— 可这是**两个不同的维度**，
        /// 混在同一行会让人读成"它的品质叫武器"。现在统一成「类别 · 品质」，两个维度各自可读。
        /// </summary>
        private static string GradeText(ShopOffer o)
        {
            // 旧版这里是 `Tool ? "道具" : "植物"` —— 两个名字都是错的：
            //   ① 第三类早就不是工具升级了（改成插件）
            //   ② 种子不叫"植物"（"植物"是长在地里的那一株，种子袋里的是种子）
            string cat = o.Kind == OfferKind.Seed ? "种子"
                       : o.Kind == OfferKind.Plugin ? "插件"
                       : o.Kind == OfferKind.Item ? "道具"
                       : "商品";
            return cat + " · " + s_rarityName(o.Quality);
        }

        /// <summary>v17：商品详情（卡底可滚动区域的全部说明）。</summary>
        private static string[] OfferDetailLines(RunSession s, ShopOffer o)
        {
            var list = new System.Collections.Generic.List<string>();
            if (o.Kind == OfferKind.Seed && o.Seed != null)
            {
                var tmp = new SeedSlot { Def = o.Seed, Quality = o.Quality };
                list.AddRange(PlantCellInfoLines(tmp));
            }
            else if (o.Kind == OfferKind.Item && o.Item != null)
            {
                list.Add("功能：" + o.Item.Desc);
                list.Add("道具类型：属性增益道具");
                list.Add($"效果：{StatName(o.Item.Stat.ToString())} {(o.Item.IsPercent ? "+" + o.Item.Add + "%" : "+" + o.Item.Add)}");
                list.Add($"稀有度：{s_rarityName(o.Quality)}　持有上限 {o.Item.MaxStack}");
            }
            else if (o.Kind == OfferKind.Plugin && o.Plugin != null)
            {
                // 旧版这一段是工具升级专用的（"同时耕作/播种的地块数 = 1 + 武器档位"）✗
                // 第三类改成插件后，再把工具的那句话挂上去就是在描述一个不存在的效果 ✓
                list.Add("功能：" + (string.IsNullOrEmpty(o.Plugin.description) ? "（未填写说明）" : o.Plugin.description));
                list.Add("效果：" + PluginEffectText(o.Plugin));
                list.Add("适用武器：" + PluginFitsText(o.Plugin));
                list.Add("叠加规则：" + StackRuleText(o.Plugin));
                list.Add($"稀有度：{s_rarityName(o.Quality)}");
                list.Add("购买后进入持有物，装到哪把武器由你决定");
            }
            else
            {
                list.Add(o.Subtitle);
            }
            list.Add($"—— 价格 {o.Price} 金币，购买后进入持有物");
            return list.ToArray();
        }

        /// <summary>
        /// 插件效果的**可读描述**。
        /// 插件只贡献数值、不含行为分支（`PluginEffectKind` 就是它的全部可能）⇒ 一张 switch 表就够 ✗
        /// 这里与 `WeaponSystem.Aggregate` 是**同一份枚举** ✗
        ///
        /// ★ v172：`default` 不再写"（未定义效果）" ✗ ★
        /// 插件从 9 种涨到 17 种（`Goods.cs:65` 的枚举）之后，新增的 8 种（剑气 / 命中减速 /
        /// 击杀回弹药 / 命中灼烧 / 共振连锁 / 爆炸回声 / 暴击炸裂 / 满血增伤）全部落到 `default`
        /// ⇒ 它们在商店详情里显示成"（未定义效果）"✓ —— 一个**已接线、已验证**的效果被写成不存在 ✓
        /// 现在 `default` 改读 `WeaponSystem.ChannelOf`：那一份说法是**统一说辞的唯一来源**
        /// （自检里有一条钉住"每个枚举值都有说法"✓），于是"加了枚举忘了改 UI"不再可能 ✗
        /// `case` 分支保留：它们给的是**更细的数值文案**（+N / ×(1+N%)），比通道名信息量大 ✓
        /// </summary>
        private static string PluginEffectText(PluginAsset p)
        {
            float n = p.amount;
            switch (p.effect)
            {
                case PluginEffectKind.DamageFlat: return $"伤害 +{n:0.#}";
                case PluginEffectKind.DamagePercent: return $"伤害 ×(1+{n * 100f:0.#}%)";
                case PluginEffectKind.AttackSpeed: return $"攻速 +{n * 100f:0.#}%";
                case PluginEffectKind.Range: return $"范围 +{n * 100f:0.#}%";
                // ★ v172 单位 bug ★：`CritRate` 是 **0..1 小数**，不是百分数
                // —— 单位约定写在 `Goods.cs:58-63`（`tuning.player.critRate = 0`、`caps.critRate = 1.0`，
                // 消费点是 `World.Rng.Chance(st.Get(StatId.CritRate))`）✓
                // 旧版 `{n:0.#}%` 把 0.12 显示成 "+0.1%" ✗ 应为 12% ✓
                // ★ 同一段里 `CritDamage` 那行本来就 ×100 ✓ —— 一错一对，这次只补齐错的那行 ✗
                // 按同一份约定核对过其余各档，**没有第三个**：
                //   `AttackSpeed` / `Range` / `DamagePercent` / `ExplodeRadius` 都是**倍率增量**（0.15 = +15%）⇒ ×100 ✓
                //   `DamageFlat` / `ProjectileCount` / `Knockback` 都是**绝对值**（加到某个数上）⇒ 不乘 ✓
                case PluginEffectKind.CritRate: return $"暴击率 +{n * 100f:0.#}%";
                case PluginEffectKind.CritDamage: return $"暴击伤害 +{n * 100f:0.#}%";
                case PluginEffectKind.ExplodeRadius: return $"爆炸范围 +{n * 100f:0.#}%";
                case PluginEffectKind.ProjectileCount: return $"弹道数 +{n:0.#}";
                case PluginEffectKind.Knockback: return $"击退 +{n:0.#}";
                // `None` 是"这一行没有效果"，不是"UI 不认识它" —— 两者必须分开说 ✗
                case PluginEffectKind.None: return "（无效果：不修改任何一环）";
                default: return WeaponSystem.ChannelOf(p.effect);
            }
        }

        /// <summary>
        /// ★ v172：这件插件**改的是攻击的哪一环** —— 直接读 `WeaponSystem.ChannelOf` ✗
        /// 不在这里再写一张对照表：两份说法必然分歧，而分歧的表现是"商店说改射程、武器栏说改范围"✓
        /// </summary>
        private static string PluginChannelText(PluginAsset p)
            => p == null ? "—" : WeaponSystem.ChannelOf(p.effect);

        /// <summary>插件的适用武器。空数组 = 任意武器 ✓ 与 `PluginAsset.Fits` 同一套判据 ✗</summary>
        private static string PluginFitsText(PluginAsset p)
        {
            if (p.allowedWeapons == null || p.allowedWeapons.Length == 0) return "任意武器";
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < p.allowedWeapons.Length; i++) names.Add(WeaponSlotName(p.allowedWeapons[i]));
            return string.Join(" / ", names);
        }

        private static string WeaponSlotName(WeaponSlot w)
        {
            switch (w)
            {
                case WeaponSlot.Hoe: return "锄头";
                case WeaponSlot.Can: return "水壶";
                case WeaponSlot.Fertilizer: return "肥料";
                default: return "未指定";
            }
        }

        private static string StackRuleText(PluginAsset p)
        {
            switch (p.stackRule)
            {
                case StackRule.Unique: return "唯一（只能装一个）";
                case StackRule.Multiplicative: return $"乘算叠加，上限 {p.maxStack}";
                default: return $"可叠加，上限 {p.maxStack}";
            }
        }

        /// <summary>v15：倍率类属性统一显示"增量整数%"（默认 0%）。</summary>
        private static string Pct(float value, float baseline)
        {
            int pct = Mathf.RoundToInt((value - baseline) * 100f);
            return $"{(pct >= 0 ? "+" : "")}{pct}%";
        }

        private float StatLine(float y, string text, bool header = false)
        {
            // v51：属性行也纳入溢出审计（面板宽度随窗口变化，长文案可能被裁）
            RowsFor(text, _statContentW, "属性面板");
            GUI.Label(new Rect(4, y, _statContentW, 22), text, header ? _bold : _small);
            return y + (header ? 26f : 22f);
        }
        public void Draw(RunSession s)
        {
            if (s == null || s.World == null) return;
            EnsureStyles();
            if (!_keyHelpInit)
            {
                KeyHelpVisible = TableDatabase.Tuning.ui == null || TableDatabase.Tuning.ui.keyHelpDefault;
                _keyHelpInit = true;
            }

            float scale = UiScale;
            _virtualW = Screen.width / scale;
            _virtualH = Screen.height / scale;
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // v17：非战斗界面（商店 / 抽卡 / 菜单 / 结算）不再绘制战斗 HUD，避免半透明面板互相透字
            bool combatHud = s.Phase == PhaseState.Combat || s.Phase == PhaseState.Prepare;
            if (combatHud && !s.Paused)
            {
                DrawVitals(s);
                DrawKeyHelp(s);
                DrawWeaponPanel(s);
                // ★ v172：主界面新增植物区（6 个中格子）★ —— 用户标准："缩小植物区域，改成 6 个格子"
                DrawPlantPanel(s);
                DrawWave(s);
                DrawPlotPrompt(s);
            }

            // v18：暂停时只画菜单（否则选种/商店面板会从菜单背后透出来）
            // v48：绘制顺序 = 战斗 HUD（含底部弹药条）→ 浮动文字 → 菜单面板
            //      —— 浮动文字要在战斗画面上层，但不能盖住商店/抽卡/菜单。
            if (!s.Paused) DrawAmmoBar(s);
            // v89：雨滴顶部标记已按需求**删除**。
            // 删除的是**调用**，`DrawRainMarkers` 本身留着——
            // 它是一段完整可用的实现（蓝色箭头 + 同列去重 + 只在视野外提示的判据），
            // 将来若要改成别的提示形式，从它改比从零写快。
            if (!s.Paused) DrawPopups(s);

            if (!s.Paused)
            {
                switch (s.Phase)
                {
                    case PhaseState.Prepare: DrawSeedChoice(s); break;
                    case PhaseState.Shop: DrawShop(s); break;
                    case PhaseState.LevelReward: DrawLevelReward(s); break;
                    // v165：`PhaseState.Victory` 已删除 —— 计时制唯一的结束条件是玩家死亡，
                    // 所以结算画面只有一种（见 DrawResult）。
                    case PhaseState.Defeat: DrawResult(s); break;
                }
            }
            else { DrawMenu(s); }

            // v51：审计收尾——本帧画过的所有信息区里，把超宽行一次性报出来（由 DebugApi.HudAudit 触发）
            if (AuditRequested)
            {
                AuditRequested = false;
                Log.Info(AuditFindings.Count == 0
                    ? "HUD-AUDIT 通过：本帧画出的所有文本都放得下"
                    : $"HUD-AUDIT 发现 {AuditFindings.Count} 条超宽：\n" + string.Join("\n", AuditFindings));
            }

            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// v47：屏幕像素 → HUD 虚拟坐标系（Draw 整体套了 GUI.matrix 缩放，坐标必须除以 UiScale；
        /// 同时 Unity 的屏幕原点在左下、GUI 在左上，所以 Y 要翻）。
        /// 抽成纯函数是为了让这段换算能被自检直接断言——它一旦写错，浮动文字会跑到屏幕外且不会有任何报错。
        /// </summary>
        public static Vector2 WorldToHud(Vector3 screenPoint, float uiScale, float screenHeight)
        {
            float s = Mathf.Max(0.01f, uiScale);
            return new Vector2(screenPoint.x / s, (screenHeight - screenPoint.y) / s);
        }

        /// <summary>
        /// v47：把逻辑层的浮动文字画成"物体旁边"的提示（成熟 / 缺水 / 掉血 / 回血 / 收获 / 升级）。
        /// </summary>
        private void DrawPopups(RunSession s)
        {
            if (s == null || s.Popups.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_popup == null)
            {
                _popup = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 18
                };
            }
            float scale = UiScale;
            for (int i = 0; i < s.Popups.Count; i++)
            {
                RunSession.FloatingText p = s.Popups[i];
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 1.3f, p.Pos.y));
                if (sp.z <= 0f) continue;                              // 目标在相机背后
                float k = Mathf.Clamp01(p.Ttl / Mathf.Max(0.01f, p.Total));   // 1 = 刚出现，0 = 即将消失
                float age = 1f - k;                                    // 反过来：0 = 刚出现
                Vector2 hud = WorldToHud(sp, scale, Screen.height);
                // v122：x 改成按**实测文本宽度**算（见下面 ow 处）—— 原来写死 `-120`，
                // 而框宽 320，于是右边界到了 +200：文字实际中心偏右 40px，
                // 长文本还会被硬裁。这就是"部分弹字显示不完全"。
                float y = hud.y - age * 30f - 14f;                     // 随时间上飘

                // v48：字号改读表（默认 26）
                float baseSize = TableDatabase.Tuning != null && TableDatabase.Tuning.ui != null
                    ? TableDatabase.Tuning.ui.popupFontSize : 26f;
                int size = Mathf.RoundToInt(baseSize * (p.Scale <= 0f ? 1f : p.Scale));

                // v69：三段式动画（需求：显示要"爆炸感"、消失要缩小淡出）
                //   ① 爆开 0 → 9%   ：几乎瞬间冲到 1.55 倍
                //   ② 回落 9% → 26% ：1.55 弹回 1.0（有回弹才有"炸"的力度）
                //   ③ 稳定 26% → 68%：保持 1.0 不动，让人看清字
                //   ④ 收回 68% → 100%：缩到 0.45 并淡出到 0
                float pop;
                float alpha;
                if (age < 0.09f)
                {
                    float t = age / 0.09f;
                    pop = Mathf.Lerp(0.15f, 1.55f, t * t);             // 平方曲线 = 起手极快
                    alpha = Mathf.Clamp01(t * 1.6f);
                }
                else if (age < 0.26f)
                {
                    float t = (age - 0.09f) / 0.17f;
                    pop = Mathf.Lerp(1.55f, 1.0f, t);                  // 回落
                    alpha = 1f;
                }
                else if (age < 0.68f)
                {
                    pop = 1.0f;
                    alpha = 1f;
                }
                else
                {
                    float t = (age - 0.68f) / 0.32f;
                    pop = Mathf.Lerp(1.0f, 0.45f, t);                  // 慢慢缩小
                    alpha = 1f - t;                                    // 同步淡出
                }

                int drawSize = Mathf.Max(6, Mathf.RoundToInt(size * pop));
                if (_popup.fontSize != drawSize) _popup.fontSize = drawSize;

                Color prev = _popup.normal.textColor;
                Color c = p.Color;
                c.a = alpha;

                // v122：**按实测文本宽度给框，并以世界点为中心**（修"显示不完全"）。
                //
                // 旧版写死 `x = hud.x - 120`、`ow = 320`：左 -120 右 +200，
                // 文字被居中后落在 hud.x + 40 —— 不是以弹出点为中心，而且长文本被裁。
                // 现在宽度由 `CalcSize` 实测（`_popup.fontSize` 已在上方设好），
                // 所以任何字号、任何长度的弹字都能完整显示。
                Vector2 need = _popup.CalcSize(new GUIContent(p.Text ?? ""));
                float ow = Mathf.Max(60f, need.x + 28f);
                float oh = Mathf.Max(drawSize + 14f, need.y + 8f);
                float x = hud.x - ow * 0.5f;

                // 四向描边：草甸背景全是中低明度的花草石，单阴影会糊进去
                _popup.normal.textColor = new Color(0.06f, 0.05f, 0.04f, alpha * 0.95f);
                GUI.Label(new Rect(x - 2.5f, y, ow, oh), p.Text, _popup);
                GUI.Label(new Rect(x + 2.5f, y, ow, oh), p.Text, _popup);
                GUI.Label(new Rect(x, y - 2.5f, ow, oh), p.Text, _popup);
                GUI.Label(new Rect(x, y + 2.5f, ow, oh), p.Text, _popup);
                _popup.normal.textColor = c;
                GUI.Label(new Rect(x, y, ow, oh), p.Text, _popup);
                _popup.normal.textColor = prev;
            }
        }

        /// <summary>
        /// v66：带描边的文字。用于**直接压在场景上**的文字（波次提示、中央提示、浮动文字）——
        /// 草甸背景里全是花草石，没有描边的文字会糊进去。
        /// 面板内的文字不需要它（面板本身有底）。
        /// </summary>
        private void LabelOutlined(Rect r, string text, GUIStyle style, float outlineAlpha = 0.85f)
        {
            Color prev = style.normal.textColor;
            Color c = prev;
            style.normal.textColor = new Color(0.06f, 0.05f, 0.04f, c.a * outlineAlpha);
            GUI.Label(new Rect(r.x - 1.5f, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + 1.5f, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y - 1.5f, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }

        /// <summary>v25：底部 UI 条——水壶储水量与肥料数量。</summary>
        private void DrawAmmoBar(RunSession s)
        {
            float w = 520f, h = 78f;
            Rect r = new Rect(_virtualW * 0.5f - w * 0.5f, _virtualH - h - 12f, w, h);
            Panel(r);
            float halfW = (w - 30f) * 0.5f;
            Bar(new Rect(r.x + 10f, r.y + 6f, halfW, 22f),
                s.WaterAmmo / (float)RunSession.WaterAmmoMax, new Color(0.3f, 0.62f, 0.95f, 0.95f),
                $"水壶储水 {s.WaterAmmo}/{RunSession.WaterAmmoMax}");
            Bar(new Rect(r.x + 20f + halfW, r.y + 6f, halfW, 22f),
                s.FertAmmo / (float)RunSession.FertAmmoMax, new Color(0.85f, 0.62f, 0.25f, 0.95f),
                $"肥料 {s.FertAmmo}/{RunSession.FertAmmoMax}");
            // v51：这行原本是 50+ 字的单行，固定 500px 宽必然被裁；现在按实压缩到一行内
            //      （雨滴 / 小猪的提示在左侧按键说明面板里另有完整说明）
            string hint = "水壶：激光 + 补水　｜　肥料：投掷 + 催熟　｜　小猪 → 肥料";
            RowsFor(hint, w - 20f, "底部提示");
            GUI.Label(new Rect(r.x + 10f, r.y + 30f, w - 20f, 40f), hint, WrapSmall);
        }
        private void EnsureStyles()
        {
            Style(ref _label, 17);
            Style(ref _small, 15);
            Style(ref _bold, 19, true);
            Style(ref _title, 30, true, TextAnchor.MiddleCenter);
            Style(ref _center, 17, false, TextAnchor.MiddleCenter);
            Style(ref _right, 15, false, TextAnchor.UpperRight);

            UiSkin.Ensure();

            if (_button == null)
            {
                _button = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                if (Font != null) _button.font = Font;
            }

            // 把 UI 美术的九宫格按钮装进全局皮肤。
            // 这样做的理由：HUD 里有十几处 GUI.Button(rect, text) 调用，逐个传样式既冗长又容易漏；
            // 而整个 IMGUI 界面都由本 HUD 独占绘制，改 GUI.skin.button 的作用域是可控的。
            // 资源缺失时保持 Unity 默认皮肤，界面不会因此打不开。
            if (UiSkin.Button != null && !_skinApplied)
            {
                _skinApplied = true;
                GUIStyle b = GUI.skin.button;
                b.normal.background = UiSkin.Button;
                b.hover.background = UiSkin.ButtonHover;
                b.active.background = UiSkin.ButtonPress;
                b.focused.background = UiSkin.ButtonHover;
                b.border = new RectOffset(14, 14, 14, 14);
                if (Font != null) b.font = Font;
            }
            if (UiSkin.Button != null)
            {
                _button.normal.background = UiSkin.Button;
                _button.hover.background = UiSkin.ButtonHover;
                _button.active.background = UiSkin.ButtonPress;
                _button.border = new RectOffset(14, 14, 14, 14);
            }

            // v71：滑动条样式。
            // IMGUI 的滚动条来自 GUI.skin 的 verticalScrollbar / horizontalScrollbar —— 不是
            // 由 GUI.BeginScrollView 的参数决定的，所以只能改皮肤。Unity 默认皮肤是深灰方块，
            // 在一整套奶油木色的界面里像外来物；这里换成图集里的凹槽 + 圆头滑块。
            // 注意 thumb 的 border 要跟着贴图走，否则九宫格拉伸会把圆头拉成方头。
            if (UiSkin.ScrollTrack != null && UiSkin.ScrollThumb != null)
            {
                SkinScrollTrack(GUI.skin.verticalScrollbar, vertical: true);
                SkinScrollThumb(GUI.skin.verticalScrollbarThumb, vertical: true);
                SkinScrollTrack(GUI.skin.horizontalScrollbar, vertical: false);
                SkinScrollThumb(GUI.skin.horizontalScrollbarThumb, vertical: false);
            }
        }

        /// <summary>滑块：圆头浅色条。border 必须跟贴图的圆角一致，否则九宫格拉伸会把圆头拉方。</summary>
        private static void SkinScrollThumb(GUIStyle st, bool vertical)
        {
            if (st == null) return;
            st.normal.background = UiSkin.ScrollThumb;
            st.hover.background = UiSkin.ScrollThumb;
            st.active.background = UiSkin.ScrollThumb;
            st.border = new RectOffset(7, 7, 7, 7);
            st.fixedWidth = vertical ? 14f : 0f;
            st.fixedHeight = vertical ? 0f : 14f;
        }

        /// <summary>轨道：深色凹槽，铺满整条。</summary>
        private static void SkinScrollTrack(GUIStyle st, bool vertical)
        {
            if (st == null) return;
            st.normal.background = UiSkin.ScrollTrack;
            st.hover.background = UiSkin.ScrollTrack;
            st.active.background = UiSkin.ScrollTrack;
            st.border = new RectOffset(6, 6, 6, 6);
            st.fixedWidth = vertical ? 14f : 0f;
            st.fixedHeight = vertical ? 0f : 14f;
        }

        private bool _skinApplied;

        /// <summary>v66：发牌动画计时。用 EventType.Layout 每帧推进一次，避免帧内多次累加。</summary>
        private float _rewardT;
        private int _rewardSig = -1;

        // ------------------------------------------------------------ 绘制原语
        /// <summary>
        /// 商店报价 → 图标资源名。
        /// v72：道具也有图标了，所以这里不再只认种子——返回**完整的资源名**，
        /// 调用方拿去 Resources.Load 即可。返回 null 表示该报价没有图标，走占位色块。
        /// </summary>
        private static string IconResOf(ShopOffer o)
        {
            if (o == null) return null;
            if (o.Kind == OfferKind.Seed && o.Seed != null) return "icon_plant_" + o.Seed.Id;
            if (o.Kind == OfferKind.Item && o.Item != null) return "icon_item_" + o.Item.Id;
            return null;
        }

        /// <summary>
        /// 卡片图标格：品质色打底 + 图标本体。
        ///
        /// v72：图标从"3D 渲染"换成了 **2D 手绘**（gen_icons2d.py / gen_item_icons.py）。
        /// 渲染图的问题不是不好看，是**缩到 64px 就糊成色块**，而且 16+15 张各有各的受光角度，
        /// 排在一起像 31 张不同的照片。2D 平涂 + 统一描边才像"一套"。
        /// 图标缺失时退回纯色块，界面不会因此变空。
        /// </summary>
        private void DrawOfferIcon(Rect icon, Color quality, string iconRes)
        {
            Color prev = GUI.color;
            GUI.color = IconWellBg;
            GUI.DrawTexture(icon, White);

            Texture2D tex = null;
            if (!string.IsNullOrEmpty(iconRes))
                tex = Resources.Load<Texture2D>("Art/UI/" + iconRes);

            if (tex != null)
            {
                // 品质描边：一圈品质色垫在图标后面，玩家一眼看出稀有度。
                // v71：从 2px 平边升级成**多层霓虹光晕**——由内到外逐层变淡变宽，
                //      读起来是"在发光"而不是"贴了一圈色纸"。层数固定 4 层，
                //      再多在 64px 的图标上就糊成一片了。
                for (int k = 4; k >= 1; k--)
                {
                    float e = k * 2.2f;
                    float a = 0.16f + 0.16f * (5 - k);
                    GUI.color = new Color(quality.r, quality.g, quality.b, a);
                    GUI.DrawTexture(new Rect(icon.x - e, icon.y - e, icon.width + e * 2f, icon.height + e * 2f), White);
                }
                GUI.color = new Color(quality.r * 0.7f, quality.g * 0.7f, quality.b * 0.7f, 1f);
                GUI.DrawTexture(new Rect(icon.x - 1f, icon.y - 1f, icon.width + 2f, icon.height + 2f), White);
                GUI.color = prev;
                GUI.DrawTexture(new Rect(icon.x, icon.y, icon.width, icon.height), White);   // 先垫深底
                GUI.DrawTexture(icon, tex, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.color = quality;
                GUI.DrawTexture(new Rect(icon.x + 6f, icon.y + 6f, icon.width - 12f, icon.height - 12f), White);
                GUI.color = prev;
            }
            GUI.color = prev;
        }

        /// <summary>
        /// v71：顶部雨滴标记。
        ///
        /// 需求是"调整雨滴大小（顶上增加UI标记）"。为什么要标记：雨滴是缺水时的续命资源，
        /// 但它随机落在 40×40 场地的任何位置，俯视相机未必拍得到——玩家只看到下方弹
        /// "拾取雨滴 +20"的提示，却不知道该往哪走。
        /// 做法：在**屏幕顶部边缘**按落点的世界 x 打一个小水滴标记，
        /// 于是"在哪个方向"一眼可见，而不需要把相机挪过去。
        /// </summary>
        private void DrawRainMarkers(RunSession s)
        {
            if (s == null || s.World == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            float scale = UiScale;
            _rainMarkX.Clear();          // 每帧重建去重表（同帧内多枚标记之间也要去重）
            int shown = 0;
            for (int i = 0; i < s.World.Pickups.Count && shown < 10; i++)
            {
                Pickup p = s.World.Pickups[i];
                if (!p.IsRain) continue;
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 0.6f, p.Pos.y));
                if (sp.z <= 0f) continue;
                Vector2 hud = WorldToHud(sp, scale, Screen.height);

                // v77：**始终显示**，不再"只标屏幕外的"。
                // 旧条件是 `hud.y` 落在画面内就跳过——那是按"相机贴近角色"设计的。
                // 但 v75 把相机拉远之后，雨滴基本永远在视野内，于是这个提示**一次都不会出现**，
                // 等于功能被自己的相机改动废掉了。现在改为始终在顶部按落点的世界 x 打一枚箭头：
                // 它表达的是"那个方向有雨滴"，和看不看得见无关。
                // 去重：同一列附近只留一枚，否则 10 滴雨会在顶排挤成一条。
                bool tooClose = false;
                for (int k = 0; k < _rainMarkX.Count; k++)
                    if (Mathf.Abs(_rainMarkX[k] - hud.x) < 26f) { tooClose = true; break; }
                if (tooClose) continue;

                float x = Mathf.Clamp(hud.x, 20f, _virtualW - 20f);
                Color prev = GUI.color;

                // v75：改成**蓝色下指箭头**。
                // 旧版画的是一颗水滴图标 + 一条竖线，问题是"水滴"这个形状在 20px 下
                // 和背景里的雨滴拾取物长得一样，玩家会以为屏幕顶上掉了东西下来。
                // 箭头表达的是**方向**（"在那边"），语义和"这里有个雨滴"分得开。
                Color blue = new Color(0.30f, 0.68f, 1f, 1f);
                Color deep = new Color(0.10f, 0.32f, 0.62f, 1f);

                // 三角箭头：用逐行递减宽度的横条堆出来（IMGUI 只能画矩形）
                const int rows = 12;
                for (int r = 0; r < rows; r++)
                {
                    float w = Mathf.Lerp(22f, 2f, r / (float)(rows - 1));
                    GUI.color = r < 2 ? deep : blue;
                    GUI.DrawTexture(new Rect(x - w * 0.5f, 3f + r * 2f, w, 2.4f), White);
                }
                // 箭头底下加一圈光晕，夜里也看得见
                GUI.color = new Color(0.45f, 0.80f, 1f, 0.28f);
                GUI.DrawTexture(new Rect(x - 15f, 0f, 30f, 30f), White);
                GUI.color = prev;
                _rainMarkX.Add(x);
                shown++;
            }
        }

        /// <summary>本帧已画出的雨滴标记横坐标，用于去重（同一列附近只留一枚）。</summary>
        private readonly System.Collections.Generic.List<float> _rainMarkX =
            new System.Collections.Generic.List<float>();

        /// <summary>面板底：优先用 UI 美术的九宫格贴图，资源缺失时退回纯色。
        /// 九宫格让同一张 64×64 的图能画任意大小的窗口而不变形。
        /// </summary>
        private void Panel(Rect r)
        {
            UiSkin.Ensure();
            if (UiSkin.PanelStyle != null && UiSkin.PanelStyle.normal.background != null)
            {
                GUI.Box(r, GUIContent.none, UiSkin.PanelStyle);
                return;
            }
            Color prev = GUI.color;
            GUI.color = PanelBg;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        /// <summary>暗面板（商店 / 菜单用，和 HUD 的亮面板区分层级）。</summary>
        private void PanelDark(Rect r)
        {
            UiSkin.Ensure();
            if (UiSkin.PanelDarkStyle != null && UiSkin.PanelDarkStyle.normal.background != null)
            {
                GUI.Box(r, GUIContent.none, UiSkin.PanelDarkStyle);
                return;
            }
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.06f, 0.07f, 0.92f);
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }

        /// <summary>
        /// 条：九宫格边框 + 九宫格填充。
        /// 填充放在 BeginGroup 里，超出的部分会被 Group 裁掉——这样填充条的
        /// 圆角端头不会被拉变形，同时进度可以任意比例。
        /// </summary>
        private void Bar(Rect r, float pct, Color fill, string text)
        {
            UiSkin.Ensure();
            bool skinned = UiSkin.BarFrameStyle != null && UiSkin.BarFrameStyle.normal.background != null;

            if (skinned)
            {
                GUI.Box(r, GUIContent.none, UiSkin.BarFrameStyle);
                float fw = r.width * Mathf.Clamp01(pct);
                if (fw > 1f)
                {
                    Color prevC = GUI.color;
                    GUI.color = fill;
                    GUI.BeginGroup(new Rect(r.x, r.y, fw, r.height));
                    GUI.Box(new Rect(0f, 0f, fw, r.height), GUIContent.none, UiSkin.BarFillStyle);
                    GUI.EndGroup();
                    GUI.color = prevC;
                }
            }
            else
            {
                Color prev = GUI.color;
                GUI.color = BarBg;
                GUI.DrawTexture(r, White);
                GUI.color = fill;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(pct), r.height), White);
                GUI.color = prev;
            }
            GUI.Label(new Rect(r.x + 6, r.y, r.width, r.height), text, _small);
        }

        /// <summary>图标：按名称取 UI 图集里的图标，缺失时返回 false（调用方跳过）。</summary>
        private static bool Icon(string id, Rect r)
        {
            UiSkin.Ensure();
            Texture2D t = null;
            switch (id)
            {
                case "hoe": t = UiSkin.IconHoe; break;
                case "can": t = UiSkin.IconCan; break;
                case "fertilizer": t = UiSkin.IconFert; break;
                case "coin": t = UiSkin.IconCoin; break;
                case "seed": t = UiSkin.IconSeed; break;
                case "heart": t = UiSkin.IconHeart; break;
                case "shield": t = UiSkin.IconShield; break;
                case "clock": t = UiSkin.IconClock; break;
                case "wave": t = UiSkin.IconWave; break;
                case "star": t = UiSkin.IconStar; break;
            }
            if (t == null) return false;
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
            return true;
        }

        // ------------------------------------------------------------ 左上：状态
        private void DrawVitals(RunSession s)
        {
            PlayerActor p = s.World.Player;
            Panel(new Rect(10, 10, 330, 122));
            Bar(new Rect(20, 20, 310, 24), p.Health.HpPct, new Color(0.85f, 0.25f, 0.25f),
                $"生命 {p.Health.Hp:0.#}/{p.Health.MaxHp:0.#}");
            float shieldPct = p.Health.MaxShield <= 0f ? 0f : p.Health.Shield / p.Health.MaxShield;
            float regDelay = TableDatabase.Tuning.player.shieldRegenDelay;
            string shieldState;
            if (p.IsInvulnerable) shieldState = $"无敌 {p.InvulnRemaining:0.0}s";
            else if (p.SinceDamaged >= regDelay) shieldState = "护盾回复中";
            else shieldState = $"{Mathf.Max(0f, regDelay - p.SinceDamaged):0.0}s 后回盾";
            Bar(new Rect(20, 48, 310, 22), shieldPct, new Color(0.35f, 0.6f, 0.95f),
                $"护盾 {p.Health.Shield:0.#}/{p.Health.MaxShield:0.#}  {shieldState}");
            float expPct = s.Progress.Exp / Mathf.Max(1f, s.Progress.ExpToNext);
            Bar(new Rect(20, 74, 310, 22), expPct, new Color(0.85f, 0.75f, 0.25f),
                $"Lv{s.Progress.Level}  经验 {s.Progress.Exp:0}/{s.Progress.ExpToNext:0}");
            GUI.Label(new Rect(20, 98, 320, 24),
                $"金币 {s.Gold}　　耕地 {s.World.Farm.PlotCount} 块（上限 {s.World.Farm.Capacity}）", _small);
            // v66：删掉这里原本的"水壶 N/M　肥料 N/M"一行。
            // 它是旧版 UI 的残留——底部弹药条已经在显示同样的信息，这里是重复。
            // 顺带把面板高度从 142 收到 122：收回来的空间让按键说明面板不再与它重叠
            // （旧版按键面板起点 136 < 状态栏底 152，两块面板是叠在一起的）。
        }

        // ------------------------------------------------------------ 左侧：按键说明（Tab 开关）
        private void DrawKeyHelp(RunSession s)
        {
            float w = 300f;
            // v66：起点从 136 下移到 142 —— 状态栏面板现在收到 122 高（底边 132），
            // 旧值 136 与它只差 4px、视觉上是叠着的。
            float y = 142f;

            if (!KeyHelpVisible)
            {
                // 隐藏后仍保留 Tab 键的描述
                Panel(new Rect(10, y, w, 30));
                GUI.Label(new Rect(20, y + 3, w - 20, 24), "显示按键说明（Tab）", _small);
                return;
            }

            // v16：战斗操作提示——只留战斗相关，格式统一为「功能（按键）」，删除 Q/R 与重复的 Tab 描述
            string[] lines =
            {
                "移动（WASD）",
                "攻击 / 与地块互动（鼠标左键）",
                "切换武器（鼠标滚轮）",
                "锄头：收获成熟植物",
                "水壶：激光攻击 + 给土地补水",
                "肥料：投掷炸弹 + 炸到即催熟",
                "显示 / 隐藏本说明（Tab）",
                "菜单（Esc）"
            };
            float h = 34f + lines.Length * 22f;
            Panel(new Rect(10, y, w, h));
            GUI.Label(new Rect(20, y + 4, w - 20, 26), "战斗操作（Tab 隐藏）", _bold);
            for (int i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(20, y + 30 + i * 22, w - 24, 22), lines[i], _small);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ★ v172：武器 / 插件 / 植物 —— 三个界面共享的【一套读数 + 一套画法】★
        //
        //  ★ 用户要求（原话）★
        //    "放大武器区域，扩大至三大框，显示名称、伤害、范围、攻击速度、爆炸范围、暴击、
        //      暴击伤害、击退，以及插件槽"
        //    "同步更新 UI 框；★ 预留更大的区域给角色武器，商店、菜单都是 ★"
        //
        //  ★ 为什么先抽一层，而不是三个界面各画一遍 ★
        //  本工程已经吃过"同一个量在两处有两种说法"的亏（如稀有度配色 bug：颜色对、名字表两份 ✓）。
        //  武器一旦在【主界面 / 商店 / 菜单】各写一份，必然出现"商店说射程、主界面说范围" ✓
        //  ⇒ 所以：读数只有一处（`WeaponReadoutOf`）✓ 画法只有一处（`DrawWeaponBox`）✓
        //    三个界面只是**给不同的矩形**（这与 v84 让商店行与种子卡共用一套排布是同一条路子 ✓）
        //
        //  ★ 数字必须来自运行时的同一条路 ★
        //    · 三系独立伤害 ⇒ 与 `SwingHoe` / `FireBeam` / `ExplodeAt` 的入参逐项对齐 ✓
        //      （`AtkDamage` 已随 v162 删除 ⇒ 这里读 MeleeDamage / RangedDamage / BlastDamage ✓）
        //    · 插件修正 ⇒ 只走 `RunSession.ModsFor` = `WeaponSystem.Aggregate`（唯一聚合点 ✓）
        //    · 攻击间隔 ⇒ 与 `p.WeaponCds[...] = cooldown / (AttackSpeed × (1+插件攻速))` 同一式 ✓
        //  ⇒ 框里写的数**就是**打出来的数；不是另算一份"给玩家看的数" ✓
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>一把武器在"基础 + 玩家属性 + 已装插件"之后的实际值（UI 只读）。</summary>
        private struct WeaponReadout
        {
            public string Name, KindText, DamageExpr;
            public float Damage, Range, Interval, BlastRadius, CritRate, CritDmg, Knockback;
            public bool HasBlast;
            public int SlotsUsed;
        }

        /// <summary>三把武器，按玩家切换的顺序（与 `PlayerActor.WeaponHoe/Can/Fertilizer` 对齐 ✓）。</summary>
        private static readonly WeaponSlot[] AllWeapons =
        {
            WeaponSlot.Hoe, WeaponSlot.Can, WeaponSlot.Fertilizer
        };

        private static int WeaponActorIndex(WeaponSlot w)
            => w == WeaponSlot.Hoe ? PlayerActor.WeaponHoe
             : w == WeaponSlot.Can ? PlayerActor.WeaponCan
             : PlayerActor.WeaponFertilizer;

        private static ToolDef ToolOf(RunSession s, WeaponSlot slot)
            => slot == WeaponSlot.Hoe ? s.T.tools.hoe
             : slot == WeaponSlot.Can ? s.T.tools.can
             : s.T.tools.fertilizer;

        /// <summary>
        /// 一把武器的全部展示读数。★ 三系独立伤害 ★：锄头→近战 / 水壶→远程 / 肥料→爆炸 ✗
        ///
        /// ⚠️ 两处**故意不一致**的细节，都照抄运行时，不"顺手统一"✗：
        ///   ① 工具品质倍率：锄头与肥料吃 `ToolMult`，**水壶不吃**（`FireBeam` 里 baseValue 就是 tool.baseValue ✓）
        ///   ② 击退系数：锄头 ×0.5 / 水壶 ×0.2 / 肥料 ×1.0（三条攻击路径各自的比例 ✓）
        /// 统一它们会让 UI 显示出一个**比实际更高**的数 —— 那是撒谎，比不一致更糟 ✓
        /// </summary>
        private static WeaponReadout WeaponReadoutOf(RunSession s, WeaponSlot slot)
        {
            ToolDef tool = ToolOf(s, slot);
            StatBlock st = s.Progress.Stats;
            WeaponModifiers pm = s.ModsFor(slot);      // ★ 唯一聚合点，与攻击路径同一个

            float baseValue = tool.baseValue;
            if (slot != WeaponSlot.Can) baseValue *= s.ToolMult(WeaponActorIndex(slot));
            float damageStat = slot == WeaponSlot.Hoe ? st.Get(StatId.MeleeDamage)
                            : slot == WeaponSlot.Can ? st.Get(StatId.RangedDamage)
                            : st.Get(StatId.BlastDamage);
            float atkMult = st.Get(StatId.AtkMult);
            float critRate = st.Get(StatId.CritRate);
            float critDmg = st.Get(StatId.CritDmg);
            WeaponSystem.ApplyToAttack(pm, ref baseValue, ref damageStat, ref atkMult, ref critRate, ref critDmg);

            PlayerActor p = s.World != null ? s.World.Player : null;
            float atkSpeed = p != null ? p.AttackSpeed : 1f;
            string kindText = WeaponTypeText(tool.damageType, tool.kind);

            // ★ v174（修复 A）：肥料框里的伤害必须写成【打出去的那个数】★
            //
            // 症状：框里写 `（3 ＋ 1）× 100% = 4`，而它在场上打出的是 4 × 2.5 = 10
            //       ⇒ 屏幕上的数字**低报了 2.5 倍** ✓
            //
            // 根因（`RunSession.ExplodeAt:4138-4139`，本次逐行走过）：那条路先在
            // `AttackFormula` 上算一遍**普通伤害**，**再乘一档爆炸倍率**
            // （`StatId.ExplodeDamage`，`tuning.json` 的 `player.explodeDamage = 2.5`）✓
            // 三条攻击路径确实共用同一个 `AttackFormula`（"算式一样"成立 ✓），
            // 但肥料在它之后**多一档** ✗ —— 而这一档是**设计**不是漏改：
            // 自检两条断言钉着"爆炸 = 普通伤害 × 倍率"与"倍率翻倍 → 爆炸翻倍"
            // （`SelfCheck.cs` 的 1209/1212 两条）⇒ 所以**不删那一档**，改显示 ✓
            //
            // ★ 取做法 ①（把框里的 Damage 换成 ExplodeFormula 的结果）★
            // 理由："面板上能看到真实伤害"比"三把算式形状一致"更硬 ——
            // 前者玩家可以用来做决策，后者只是排版上好看 ✓
            // 并把这一档写进算式（`× 爆炸 250%`）⇒ 三个数仍然能自己乘一遍验算，
            // 即"算式形状"也没有真的丢 ✓
            //
            // ⚠️ 锄头 / 水壶**没有**这一档（倍率恒为 1）⇒ 那两框的读数与算式**逐字不变** ✓
            // ⚠️ `ExplodeAt` 给 `AttackFormula` 传的 `minDamage` 是 0，这里传 `formula.minDamage`
            //    （0.1）—— 肥料的普通伤害 = (3 + 1) × 100% = 4 ≫ 0.1
            //    ⇒ 这个差在任何可达数值下都不显形 ✓（值相同，不去动它以免三把分家）
            bool hasBlast = slot == WeaponSlot.Fertilizer;
            float explodeMult = hasBlast ? st.Get(StatId.ExplodeDamage) : 1f;
            float normalDmg = DamageResolver.AttackFormula(baseValue, damageStat, atkMult, false, critDmg, s.T.formula.minDamage);

            var r = new WeaponReadout
            {
                Name = WeaponSlotName(slot),
                KindText = kindText,
                DamageExpr = $"（{baseValue:0.#} ＋ {damageStat:0.#}）× {atkMult * 100f:0}%"
                             + (hasBlast ? $" × 爆炸 {explodeMult * 100f:0}%" : ""),
                Damage = hasBlast ? DamageResolver.ExplodeFormula(normalDmg, explodeMult) : normalDmg,
                Range = tool.range * st.Get(StatId.AtkRange) * (1f + pm.RangePct),
                Interval = tool.cooldown / Mathf.Max(0.05f, atkSpeed * (1f + pm.AttackSpeedPct)),
                CritRate = critRate,
                CritDmg = critDmg,
                Knockback = st.Get(StatId.Knockback)
                            * (slot == WeaponSlot.Hoe ? 0.5f : slot == WeaponSlot.Can ? 0.2f : 1f)
                            + pm.KnockbackFlat,
                HasBlast = hasBlast,
                SlotsUsed = s.Goods.SlotsUsedOn(slot)
            };
            if (r.HasBlast)
                r.BlastRadius = RunSession.BlastBaseRadius(tool.baseExplodeRange) * st.Get(StatId.ExplodeRange)
                                * (1f + pm.ExplodeRadiusPct);
            return r;
        }

        /// <summary>v48：武器类型按需求显示为三个词——锄头（近战）/ 水壶（远程）/ 肥料（爆炸）。</summary>
        private static string WeaponTypeText(string damageType, string kind)
        {
            if (string.Equals(damageType, "Ailment", System.StringComparison.OrdinalIgnoreCase)) return "爆炸";
            return string.Equals(kind, "Ranged", System.StringComparison.OrdinalIgnoreCase) ? "远程" : "近战";
        }

        // ------------------------------------------------------------ v172：武器框（三处共用）
        /// <summary>暗底 / 亮底两种**换行**样式（`WrapSmall` 是墨棕字，只适合亮底 ✗）。</summary>
        private GUIStyle _onDarkWrap;
        private GUIStyle OnDarkWrap
        {
            get
            {
                if (_onDarkWrap == null)
                {
                    _onDarkWrap = new GUIStyle(_small) { wordWrap = true };
                    _onDarkWrap.padding = new RectOffset(0, 0, 0, 0);
                    _onDarkWrap.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _onDarkWrap;
            }
        }

        private GUIStyle _tiny;
        private GUIStyle Tiny
        {
            get
            {
                if (_tiny == null)
                {
                    _tiny = new GUIStyle(_small) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                    _tiny.normal.textColor = new Color(0.10f, 0.08f, 0.06f);
                }
                return _tiny;
            }
        }

        /// <summary>插槽里的短名（2 个字）：整个名字塞不进 56px 的槽 ✓ 全名在下面那行摘要里 ✓</summary>
        private static string ShortPluginName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= 2 ? name : name.Substring(0, 2);
        }

        /// <summary>
        /// ★ 6 个插件槽 ★ —— ★ 空槽也要画 ★
        ///
        /// 为什么空槽必须画：玩家买插件时唯一要判断的事就是"这把武器还剩几个位置"（用户标准原文）✓
        /// 只画已装的 ⇒ 他得自己数"装了 3 个 ⇒ 6-3=3"✓ 多一步心算就是一次误购的机会 ✗
        ///
        /// 槽的着色 = 那件插件的**品质色**（与商店、持有物同一套 `QualityColor` ✓ 不另起一套配色 ✓）
        /// 叠层的（"磨刀石 ×3"）在槽里写 `×3` —— ★ 它仍然只占 1 格 ★
        /// （`SlotsUsedOn` 数的是**实例数**不是层数，`Goods.cs:430-442` 有明确说明 ✓）
        /// </summary>
        private void DrawPluginSlots(RunSession s, WeaponSlot slot, Rect area)
        {
            float gap = 4f;
            float cw = (area.width - gap * (PlayerGoods.MaxPluginsPerWeapon - 1)) / PlayerGoods.MaxPluginsPerWeapon;
            float ch = area.height;
            int i = 0;
            foreach (PluginInstance pi in s.Goods.PluginsOn(slot))
            {
                if (i >= PlayerGoods.MaxPluginsPerWeapon) break;      // 上限 6 ⇒ 超出不会发生，防御而已
                Rect cell = new Rect(area.x + i * (cw + gap), area.y, cw, ch);
                Color pv = GUI.color;
                GUI.color = pi.Asset != null ? QualityColor((int)pi.Asset.quality) : new Color(0.5f, 0.5f, 0.5f);
                GUI.DrawTexture(cell, White);
                GUI.color = pv;
                if (pi.Asset != null)
                    GUI.Label(new Rect(cell.x, cell.y + 4f, cell.width, 18f), ShortPluginName(pi.Asset.displayName), Tiny);
                if (pi.Stacks > 1)
                    GUI.Label(new Rect(cell.x, cell.y + 4f, cell.width - 3f, 18f), "×" + pi.Stacks, Tiny);
                i++;
            }
            for (; i < PlayerGoods.MaxPluginsPerWeapon; i++)
            {
                Rect cell = new Rect(area.x + i * (cw + gap), area.y, cw, ch);
                Color pv = GUI.color;
                GUI.color = new Color(0.22f, 0.23f, 0.26f, 0.95f);
                GUI.DrawTexture(cell, White);
                GUI.color = pv;
                GUI.Label(new Rect(cell.x, cell.y + 4f, cell.width, 18f), "空", Tiny);
            }
        }

        /// <summary>
        /// "已装插件"摘要：★ 名字 · 改哪一环 ★ —— 通道名取 `WeaponSystem.ChannelOf`（唯一说法 ✓）
        /// 用户标准："还要能看到这件插件装上会改哪一环"✓ 这是**买之前**的判断依据 ✓
        /// </summary>
        private static string InstalledPluginSummary(RunSession s, WeaponSlot slot)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (PluginInstance pi in s.Goods.PluginsOn(slot))
            {
                if (pi.Asset == null) continue;
                if (n > 0) sb.Append("　");
                sb.Append(pi.Asset.displayName);
                if (pi.Stacks > 1) sb.Append('×').Append(pi.Stacks);
                sb.Append('·').Append(WeaponSystem.ChannelOf(pi.Asset.effect));
                n++;
                if (n >= PlayerGoods.MaxPluginsPerWeapon) break;
            }
            if (n == 0) return "插件槽全空（商店买插件 ⇒ 装到这把武器上）";
            int free = s.Goods.FreeSlotsOn(slot);
            return free > 0 ? sb.Append($"　（还可装 {free} 件）").ToString() : sb.Append("　（已满）").ToString();
        }

        /// <summary>
        /// 一个武器框的高度。★ 三个界面共用同一个值 ★
        /// 为什么不做"高/矮"两个变体：变体会让"商店看到的"与"主界面看到的"不是同一张卡片，
        /// 而用户的要求恰恰是"三个界面都要有、都要大"⇒ 同一种卡片出现在三处才是**一套语言** ✓
        /// 内容高度（从框顶算）：标题 27 ＋ 伤害 19 ＋ 范围/攻速/击退 19 ＋ 爆炸/暴击/暴伤 19
        ///                      ＋ 插件槽 26 ＋ 已装插件摘要 34 ＋ 上下留白 12 = 156 ≤ 168 ✓
        /// </summary>
        private const float WeaponBoxH = 168f;

        /// <summary>
        /// ★ v172：一个武器框 ★ —— 主界面 / 商店 / 菜单**三处共用同一份画法**。
        ///
        /// 用户标准（原话）："放大武器区域，扩大至三大框，显示名称、伤害、范围、攻击速度、
        /// 爆炸范围、暴击、暴击伤害、击退，以及插件槽"✓
        /// ⇒ 八项 + 6 个插件槽 + 已装插件（名字·改哪一环），一项不少 ✓
        ///
        /// ★ 伤害带算式且是"含插件后的实际值" ★：
        ///   锄头 / 水壶：（武器基础伤害 ＋ 对应类型伤害）× 伤害倍数
        ///   肥料：上文再 × 一档**爆炸倍率**（`StatId.ExplodeDamage`）★ v174 ★
        ///   锄头→近战 / 水壶→远程 / 肥料→爆炸（三系独立 ✓ `AtkDamage` 已删 ✓）
        /// 算式里的三个数都出自 `WeaponReadoutOf`，而它走 `WeaponSystem.ApplyToAttack`
        /// —— 与三条攻击路径**同一个入口** ⇒ 框里的数就是打出来的数 ✓
        ///
        /// ★ v174（修复 A）★：肥料那一档是**读到 `ExplodeAt` 里去了**才补上的 ✗
        /// 改前肥料框只算到普通伤害（= 4），而场上打出的是它乘爆炸倍率（= 10）
        /// ⇒ 玩家看到的数比实际的低 2.5 倍 ✓ 现在肥料取 `ExplodeFormula` 的结果，
        /// 倍率也写进算式 ⇒ `RunSession.ExplodeAt:4138-4139` 的每一步都能在框里对上 ✓
        /// </summary>
        private void DrawWeaponBox(RunSession s, WeaponSlot slot, Rect box, bool current)
        {
            WeaponReadout r = WeaponReadoutOf(s, slot);
            Color prev = GUI.color;
            GUI.color = current ? new Color(0.32f, 0.55f, 0.30f, 0.95f) : new Color(0.13f, 0.15f, 0.17f, 0.90f);
            GUI.DrawTexture(box, White);
            GUI.color = prev;

            // v89 的既有判据：亮绿底上不用白字（发飘、对比度反而低）⇒ 选中行用 OnLight ✓
            // 其余行是**暗底** ⇒ 用 OnDark（v102 立的规矩：亮底/暗底各给一个明确样式，不靠默认值碰巧合适 ✓）
            GUIStyle title = current ? OnLight : OnDarkBold;
            GUIStyle body = current ? OnLight : OnDark;

            float px = 10f;
            GUI.Label(new Rect(box.x + px, box.y + 3f, box.width - 108f, 24f),
                (current ? "▶ " : "　 ") + r.Name + "　" + r.KindText, title);
            GUI.Label(new Rect(box.xMax - 100f, box.y + 4f, 92f, 22f),
                $"插件 {r.SlotsUsed}/{PlayerGoods.MaxPluginsPerWeapon}", body);

            float y = box.y + 28f;
            GUI.Label(new Rect(box.x + px, y, box.width - 20f, 20f),
                "伤害 " + r.DamageExpr + $" = {r.Damage:0.#}", body);
            y += 19f;
            GUI.Label(new Rect(box.x + px, y, box.width - 20f, 20f),
                $"范围 {r.Range:0.0}m　攻速 {r.Interval:0.00}s/次　击退 {r.Knockback:0.00}", body);
            y += 19f;
            GUI.Label(new Rect(box.x + px, y, box.width - 20f, 20f),
                $"爆炸 {(r.HasBlast ? r.BlastRadius.ToString("0.0") + "m" : "—")}"
                + $"　暴击 {r.CritRate * 100f:0.#}%　暴伤 {r.CritDmg * 100f:0}%", body);
            y += 23f;

            DrawPluginSlots(s, slot, new Rect(box.x + px, y, box.width - 20f, 26f));
            y += 30f;

            GUI.Label(new Rect(box.x + px, y, box.width - 20f, 34f), InstalledPluginSummary(s, slot),
                current ? OnLight : OnDarkWrap);
        }

        /// <summary>当前武器（三处高亮同一把 ⇒ 判据只写一次 ✓）。</summary>
        private static bool IsCurrentWeapon(RunSession s, WeaponSlot slot)
            => s.World != null && s.World.Player != null && (int)slot == s.World.Player.WeaponIndex;

        /// <summary>
        /// ★ v172：一块"武器与插件"区（商店 / 菜单共用）★
        /// 三把武器各一个框 ⇒ 每框 8 项 + 6 个插件槽 + 已装插件摘要 ✓
        /// 高度不足时滚动（滚动条是既有皮肤 ✓ 不是新样式）—— 但三个界面的高度都按
        /// `WeaponBoxH × 3 + 间距` 给 ⇒ 实际不会触发 ✓
        /// </summary>
        private void DrawWeaponSection(RunSession s, Rect area, string title)
        {
            Panel(area);
            GUI.Label(new Rect(area.x + 12, area.y + 3, area.width - 24, 26), title, _bold);
            float gap = 6f;
            float need = WeaponBoxH * 3f + gap * 2f;
            Rect view = new Rect(area.x + 3f, area.y + 32f, area.width - 6f, area.height - 38f);
            _weaponScroll = GUI.BeginScrollView(view, _weaponScroll,
                new Rect(0f, 0f, view.width - 18f, Mathf.Max(view.height, need)));
            for (int i = 0; i < AllWeapons.Length; i++)
                DrawWeaponBox(s, AllWeapons[i],
                    new Rect(6f, i * (WeaponBoxH + gap), view.width - 28f, WeaponBoxH),
                    IsCurrentWeapon(s, AllWeapons[i]));
            GUI.EndScrollView();
        }

        private Vector2 _weaponScroll;

        /// <summary>
        /// ★ v172：主界面武器区 = 三大框 ★
        ///
        /// ★ 改前 → 改后（"更大"是可判定的数字，不是感觉）★
        ///   改前：`Rect(_virtualW-262-14, 150, 262, 198)`；三行各 44px；**每行只写一个名字** ✓
        ///   改后：`Rect(_virtualW-380-14, 150, 380, 558)`；三个框各 **168px**；
        ///         每框 **8 项 + 6 个插件槽 + 已装插件摘要** ✓
        ///   ⇒ 宽 262→380（×1.45）／高 198→558（×2.82）／面积 51,876→212,040（**×4.09**）✓
        ///
        /// ⚠️ 宽度为什么定在 380（而不是更宽）✗：
        ///   `DrawPlotPrompt` 的画布是 `x = 中-310, w = 620` ⇒ 虚宽 1422 时它的右边界 = 1021 ✓
        ///   武器区左边界 = 1422-380-14 = 1028 > 1021 ⇒ 两块面板**不重叠** ✓
        ///   （宽度再大 7px 就会压住耕地提示条 —— 那个数不是随手取的 ✓）
        /// </summary>
        private void DrawWeaponPanel(RunSession s)
        {
            PlayerActor p = s.World.Player;
            float w = 380f;
            float x = _virtualW - w - 14f;
            float y = 150f;
            const float gap = 6f;
            float h = 30f + WeaponBoxH * 3f + gap * 2f + 12f;

            Panel(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 12, y + 3, w - 24, 26), "武器（三把 · 含插件）", _bold);

            for (int i = 0; i < AllWeapons.Length; i++)
            {
                Rect box = new Rect(x + 8f, y + 28f + i * (WeaponBoxH + gap), w - 16f, WeaponBoxH);
                DrawWeaponBox(s, AllWeapons[i], box, i == p.WeaponIndex);
            }

            string state = s.FarmingThisFrame
                ? "耕作中（无法攻击）"
                : p.SwitchWindup > 0f
                    ? $"切换前摇 {p.SwitchWindup:0.0}s（无法攻击）"
                    : (p.IsLocked ? "锁定目标中" : "按住鼠标左键：攻击 / 互动");
            GUI.Label(new Rect(x + 12, y + h - 24f, w - 24, 22f), state, _small);
        }

        // ------------------------------------------------------------ v172：植物区（6 个中格子）
        /// <summary>这株植物有没有真机制（`None` 与空串都算"没有" ✓ 判据与自检同源 ✓）。</summary>
        private static bool HasMechanicOf(SeedDef d)
            => d != null && !string.IsNullOrEmpty(d.Mechanic)
               && !string.Equals(d.Mechanic, PlantMechanics.None, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// ★ v172：机制作用的**阶段** ★
        ///
        /// 用户标准（原话）："所有的植物机制作用于【成长或者收获阶段、或者缺水时】"✓
        /// ⇒ 机制名后面**必须带阶段**：读到"丰收"要能立刻知道它"收获时才生效" ✓
        /// 不写进 `PlantMechanics` 的理由：那是**行为解释器**（它管"算得对不对"✓）
        /// 而"这句话是给玩家看的标签"属于呈现（两者的变更理由不同 ✓）
        /// 而**措辞本身**仍然只取 `PlantMechanics.Describe` —— 唯一的说法来源 ✓
        /// </summary>
        private static string PlantMechanicStage(string mechanic)
        {
            if (string.Equals(mechanic, PlantMechanics.ThirstTolerant, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mechanic, PlantMechanics.ThirstBloom, System.StringComparison.OrdinalIgnoreCase))
                return "缺水";
            if (string.Equals(mechanic, PlantMechanics.RichHarvest, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mechanic, PlantMechanics.DewDrop, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(mechanic, PlantMechanics.ChainHarvest, System.StringComparison.OrdinalIgnoreCase))
                return "收获";
            return "成长";     // Haste / NeighborBoost / WaterSave / ElderGrowth
        }

        /// <summary>
        /// ★ v173：第 7 项"特殊功能" ★ —— 有机制就写「机制〔阶段〕+ 说明」，
        /// **没有机制就写「平平无奇」** ✓
        ///
        /// 用户标准（原话）："植物只显示：名字、品质、等级、金币产出量、经验产出量、生长时间、
        /// 特殊功能 ★（没有就写【平平无奇】）★"✓
        ///
        /// ⇒ v172 那两句**已删** ✗：「定位：纯数值（靠等级与品质拿收益）」与
        ///   「传奇：纯数值（收益最高档）」—— 它们与"平平无奇"讲的是同一件事（这株没有机制），
        ///   但前者读起来像一段**说明文案**，后者才是能一眼扫过去的结论 ✓
        /// ⇒ 于是这一行**不再按品质分两档**：没有机制就是没有机制，普通与传奇写同一句 ✓
        ///   （`quality` 仍然要传：下面「机制〔大·/小·〕」那一档还按它分 ✓）
        /// </summary>
        private static string PlantEffectLine(SeedDef d, int quality)
        {
            if (d == null) return "";
            if (!HasMechanicOf(d)) return "平平无奇";
            string text = PlantMechanics.Describe(d.Mechanic);
            string head = (quality >= 4 ? "机制〔大·" : "机制〔小·") + PlantMechanicStage(d.Mechanic) + "〕";
            return string.IsNullOrEmpty(text) ? head + d.Mechanic : head + text;
        }

        /// <summary>
        /// ★ v173：植物信息的**唯一生产者** ★ —— 主界面 6 格 / 菜单中栏 3×2 / 商店种子卡 /
        /// 商店商品详情 / 开局选种，五处全部走这一个函数 ✓
        ///
        /// 用户标准（原话）："植物只显示：名字、品质、等级、金币产出量、经验产出量、生长时间、
        /// 特殊功能"✓ ⇒ 名字与品质画在卡片抬头（`DrawPlantCell` 的名称行 / `SeedCardPanel` 的左列），
        /// 这里是剩下的 5 项 ✓ 合计 7 项，**一项不多一项不少** ✓
        ///
        /// ★ 为什么必须只有一个生产者 ★
        /// 改前是两份：这份（7 项）与 `SeedCardLines`（15 行 —— 除了状态与收益之外，
        /// 还读 `SeedDef` 的 `BaseValue` `AttackInterval` `BaseCritRate` `BaseCritDmg` `Range`
        /// `BaseHp` `BaseShield` `MoveSpeed` 这 8 个**战斗字段**）✗
        /// ⇒ 同一个植物在商店卡与主界面格子上**长得不一样**，
        ///   而用户对两处的要求是同一句 ⇒ 只改一处就等于没做 ✓
        /// 现在两份合并成一份，结构上不可能再分家 ✓
        ///
        /// ⑤⑥ 金币 / 经验与 `Actors.HarvestPlot` 的发放公式同一式：
        /// `max(1, round(price/5 × 等级倍率 × 机制倍率))` 与 `max(1, round(price/10 × 等级倍率))` ✓
        /// ★ v174（修复 B）★：那个"等级倍率"必须取**收获表**（`LevelHarvestMult`
        /// = 1.00/1.35/1.70），不是**卖出返还表**（`LevelYieldMult` = 1.00/1.50/2.00）✓
        /// 两张表都在 `RunSession.SeedSlot` 上，名字只差一个词 ⇒ 正是"看起来对、数不对"的温床 ✗
        ///
        /// ⚠️ 卡片**算不到**的两项（改前改后一样，不是这次引入的 ✗）：
        ///   ① 道具规则 `harvest_gold_pct`（`HarvestPlot` 的 `HarvestGoldMult` 字段，
        ///      由 `RunSession.SyncItemRules` 运行时写入 ⇒ 静态卡片无从得知）
        ///   ② 缺水期的机制加成（卡片给 `HarvestGoldMult` 传的 plot 是 `null`
        ///      ⇒ 取的是"不缺水"那档 ✓ 这是**保守**方向：显示 ≤ 实得 ✓）
        /// ⇒ 卡片承诺的是"这一级、按机制、常态下给多少"，实得只会**不低于**它 ✓
        /// ★ 植物自 v165 起不参战 ⇒ 这里**一个战斗属性都没有** ✓
        /// </summary>
        private static string[] PlantCellInfoLines(SeedSlot slot)
        {
            var list = new System.Collections.Generic.List<string>();
            SeedDef d = slot.Def;
            if (d == null) { list.Add("（空格）"); return list.ToArray(); }
            // ③ 等级 —— ★ 只做数值提升 ★ ⇒ 直接把"它给了多少"写在旁边
            //
            // ★ v174（修复 B′）：这一行也必须取**收获**口径，标签也照实写「收获」★
            //
            // 症状（是 B 的**同一处**没修干净 ✗）：Lv2 卡上 `金币 5` 是按 1.35 算出来的
            //   （4 × 1.35 = 5.4 → 5 ✓），而旁边那一行却写 `收益 ×1.50`（卖出返还表）✗
            //   ⇒ 同一张卡上两个倍率打架，玩家会以为金币那格算错了 ✓
            // 判据：取同一个口径 ⇒ `收获 ×1.35` 与 `金币 5` **互相对得上** ✓
            //   （只动**显示** ✗ ⑤⑥ 的计算一个字没改：金币/经验本来就对 ✓）
            // ⚠️ 标签为什么必须一起改 ✗：只换数不换词 ⇒ 玩家会照着"收益 ×1.50"去验算，
            //   而 1.50 是**卖出**那条线（`SeedSlot.LevelYieldMult`）✓
            //   卖出返还现在只在真正卖的那一刻出现（`RunSession.cs:2823` 的合成提示）
            //   与商店的「出售 N」按钮（`ImGuiHud.cs:499` = `slot.SellValueFor`）✓
            list.Add($"Lv {slot.Level}/{SeedSlot.MaxLevel}　收获 ×{slot.LevelHarvestMult:0.00}");
            // ④ 成熟时间 —— ★ 也要吃**等级速度倍率**（v174 修复 C）★
            //
            // 症状：Lv3 卡写 `成熟 18s`，而地里那株实际 **13.8s** 就熟 ✗
            // 权威（**同一式，不自己再算一遍** ✓）：
            //   `FarmGrid.BeginPlant` = `def.GrowTime / SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, Level)`
            //   （`FarmGrid.cs:253-254`；换等级时 `SetLevel` 是同一式，`:296-297`）✓
            //   自检也钉着这条：`GrowthTotal == radish.GrowTime / 1.30`（Lv3）✓
            // ⇒ 1 级 ×1.00（18s 不变）／2 级 ÷1.15 ≈ 15.7s／3 级 ÷1.30 ≈ 13.8s ✓
            float growSeconds = d.GrowTime
                                / SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, slot.Level);
            list.Add($"成熟 {growSeconds:0.#}s");
            // ⑤⑥ 金币 / 经验
            //
            // ★ v174（修复 B）：等级倍率取【收获】那一条，不是【卖出返还】那一条 ★
            //
            // 症状：Lv2 / Lv3 的卡片上金币与经验**多算一档** ✓
            // 根因：这里原来读 `SeedSlot.LevelYieldMult` —— 而它自己的文档写着
            //   "1.00 / 1.50 / 2.00。**卖出返还**用这条"（`RunSession.cs:139-142`）✗
            //   实际收获走的是**另一条表**：`SeedSlot.LevelHarvestMult` = 1.00 / 1.35 / 1.70
            //   （`RunSession.cs:144-145`）✓
            //   而 `Actors.HarvestPlot:868/871` 里发放收益时读的是 `plot.LevelYieldMult`
            //   —— 那是 `FarmPlot` 上的同名属性，指向的**正是收获表**
            //   （`FarmGrid.cs:73` = `SeedEconomy.YieldMult(SeedSlot.HarvestPerLevel, Level)`）✓
            // ⇒ 两边是**两套值**：卡片显示 ×1.50，实际给 ×1.35 ✗
            //   自检自己就有一条断言把两者分开（`SelfCheck` 的
            //   "收获 {Lv2}／卖出 {Lv2}"，1.35 与 1.50 各占一边 ✓）⇒ 拿它当基准 ✓
            //
            // ⚠️ 为什么**不是**去改 `Actors.HarvestPlot`：那里是权威 ✓
            //   错的是卡片这一次读取 —— 收获发放多少由策划定，UI 跟着它走 ✓
            // ⚠️ 这一改**会改变屏幕上的数字**（Lv2 从 ×1.50 档落到 ×1.35 档 ✓）
            //   而那是**对的**：原来那句"金币产出量/经验产出量"本来就是错的 ✓
            // ⚠️ 商店卡 / 开局选种**看不出来**：买来的种子恒为 `StartingLevel`（= 1 级，
            //   `RunSession.cs:468/482/1145`）⇒ 倍率 = 1.00，两套表在 1 级上**同值** ✓
            //   变的是主界面 6 格与菜单 3×2 里那些**真的升过级**的槽位 ✓
            float lvHarvest = slot.LevelHarvestMult;
            float gold = Mathf.Max(1f, Mathf.RoundToInt(d.Price / 5f * lvHarvest
                                                        * PlantMechanics.HarvestGoldMult(d, null)));
            float exp = Mathf.Max(1f, Mathf.RoundToInt(d.Price / 10f * lvHarvest));
            list.Add($"金币 {gold:0} · 经验 {exp:0}");
            // ⑦ 特殊功能（带阶段；没有机制就写「平平无奇」）
            list.Add(PlantEffectLine(d, slot.Quality));
            return list.ToArray();
        }

        /// <summary>
        /// ★ v173：一个植物格（主界面 6 格 / 菜单中栏 3×2 共用）★
        /// 项：① 名称 ② 品质 ③ 等级 ④ 成熟时间 ⑤ 金币 ⑥ 经验 ⑦ 特殊功能 —— 七项一项不少 ✓
        /// （内容全部来自 `PlantCellInfoLines` —— 商店卡与这里**同源** ✓）
        /// 矮格子的处置：最后一项（机制说明）**折行**（用户允许两行 ✓）
        /// 而"等级"与"特殊功能"分别在信息区的**首行与末行**⇒ 任何高度下都不会被先挤掉 ✓
        /// （那两项是 v172 新增的信息 ✓ 挤掉它们等于没做 ✓）
        /// </summary>
        private void DrawPlantCell(RunSession s, SeedSlot slot, Rect cell)
        {
            Color pv = GUI.color;
            GUI.color = slot.IsEmpty ? SlotBg : SlotBgFilled;
            PaperFill(cell);
            ScrollFrame(cell);
            GUI.color = QualityColor(slot.Quality);
            GUI.DrawTexture(new Rect(cell.x, cell.y, 5f, cell.height), White);   // 左侧品质条
            GUI.color = pv;

            float pad = 5f;
            float iconS = 30f;
            Rect icon = new Rect(cell.x + pad + 3f, cell.y + pad, iconS, iconS);
            if (!slot.IsEmpty)
                DrawOfferIcon(icon, QualityColor(slot.Quality), slot.Def != null ? "icon_plant_" + slot.Def.Id : null);

            // ① 名称　② 品质
            GUI.Label(new Rect(icon.xMax + 5f, cell.y + pad - 4f, cell.width - iconS - 18f, 20f),
                slot.IsEmpty ? "空格" : slot.Label, OnDarkBold);
            GUI.Label(new Rect(icon.xMax + 5f, cell.y + pad + 14f, cell.width - iconS - 18f, 18f),
                slot.IsEmpty ? "商店买种子后自动种下" : s_rarityName(slot.Quality), OnDark);

            if (slot.IsEmpty) return;

            // ③④⑤⑥⑦
            float ly = cell.y + pad + iconS + 4f;
            string[] lines = PlantCellInfoLines(slot);
            float textW = cell.width - pad * 2f;
            for (int i = 0; i < lines.Length; i++)
            {
                bool last = i == lines.Length - 1;
                float lh = last ? 32f : 17f;
                GUI.Label(new Rect(cell.x + pad, ly, textW, lh), lines[i], OnDarkWrap);
                ly += lh;
            }
        }

        /// <summary>
        /// ★ v172：主界面**植物区** = 6 个中格子（2 列 × 3 行）★
        ///
        /// 用户标准（原话）："缩小植物区域，改成 6 个格子，中格子，显示名称、品质、等级、成熟时间、
        /// 金币、经验、特殊效果"✓
        /// ⇒ 格数 = `Bag.SlotCount`（= `Progress.MaxPlots` = 田地列数 6 ✓ 新玩法"一株植物独占一列"✓）
        ///
        /// ★ 位置为什么是 (10, 354) ✗ ★
        ///   `DrawKeyHelp` 的底边 = 142 + 34 + 8×22 = 352 ✓
        ///   `DrawPlotPrompt` 的左边 = 中−310 = 401（虚宽 1422）✓ 本面板右边界 390 < 401 ⇒ 不叠 ✓
        ///   底边 794 < 底中弹药条的顶 710？✗ —— 弹药条横向只占 451..971，本面板只到 390 ⇒ 横向不相交 ✓
        /// </summary>
        private void DrawPlantPanel(RunSession s)
        {
            float x = 10f, y = 354f, w = 380f, h = 440f;
            Panel(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 10, y + 3, w - 20, 26),
                $"植物（{s.Bag.OccupiedCount}/{s.Bag.SlotCount}）　一株独占一列", _bold);

            float pad = 6f;
            float cw = (w - pad * 3f) / 2f;
            float ch = (h - 30f - pad * 4f) / 3f;
            for (int i = 0; i < s.Bag.SlotCount && i < 6; i++)
            {
                Rect cell = new Rect(x + pad + (i % 2) * (cw + pad), y + 30f + (i / 2) * (ch + pad), cw, ch);
                DrawPlantCell(s, s.Bag[i], cell);
            }
        }

        // ------------------------------------------------------------ 顶部：只显示阶段
        private void DrawWave(RunSession s)
        {
            float w = 640f;
            float x = _virtualW * 0.5f - w * 0.5f;
            Panel(new Rect(x, 8, w, 66));
            string phaseText = PhaseLabel(s);
            // ★ v165：顶部不再挂任何"倒计时秒数" ★
            // 删掉的两条都是旧回合制的产物：
            //   ① `剩余 {Waves.Remaining}s` —— waves.json 的 duration（30~90 秒）波次倒计时
            //   ② `击败 BOSS 剩余 Ns`      —— bossTimeLimit(90s) 倒计时，而它在计时制下**不可达**
            //      （`CombatElapsed` 每分钟被 StartWave 归零，最大 60s ⇒ 永远走不到 90s），
            //      画出来就是一个永远不会到期、每分钟重置的假倒计时 ✗
            // 玩家真正需要的时间信息在 PhaseLabel 里已经有两处**更准**的：
            //   · mm:ss 游戏计时器
            //   · ☠ BOSS mm:ss（来自 BossSchedule，是真实的"下一次刷新"倒计时）
            GUI.Label(new Rect(x, 10, w, 32), phaseText, _title);

            string tip;
            if (s.Phase == PhaseState.Combat && s.Waves.InGrace)
                tip = $"种植时间 {Mathf.CeilToInt(s.Waves.GraceLeft)}s —— 不会刷怪，先开垦播种！";
            else if (s.Waves.Wave != null && s.Phase == PhaseState.Combat)
                tip = s.Waves.Wave.Tip;
            else
                tip = "";
            RowsFor(tip, w, "波次提示");
            LabelOutlined(new Rect(x, 42, w, 24), tip, _center);
        }

        /// <summary>v154：秒 → mm:ss（计时制顶部计时器用）。</summary>
        private static string FmtTime(float seconds)
        {
            int t = Mathf.Max(0, (int)seconds);
            return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        }

        private static string PhaseLabel(RunSession s)
        {
            switch (s.Phase)
            {
                case PhaseState.Prepare: return "准备阶段 · 选择初始种子";
                // v154：**计时制** —— 顶部不再显示"第 N/20 波"，改为计时器 + Boss 倒计时
                // 需求："中间做计时器计时游玩时间，计时器实时汇报下一波 boss 的刷新时间倒计时"
                case PhaseState.Combat:
                    return $"⏱ {FmtTime(s.RunTime)}　第 {s.WaveIndex} 分钟"
                         + (s.BossCountdownText() is string bc && bc.Length > 0 ? $"　☠ {bc}" : "");
                case PhaseState.Shop:
                    return $"⏱ {FmtTime(s.RunTime)}　第 {s.WaveIndex} 分钟　商店";
                case PhaseState.Defeat: return "守园失败";
                // v165：`case PhaseState.Victory` 已删除（枚举成员不存在了）——
                // 计时制唯一的结束条件是玩家死亡。
                default: return s.Phase.ToString();
            }
        }

        // ------------------------------------------------------------ 底部中：耕地提示（两条进度条）
        private void DrawPlotPrompt(RunSession s)
        {
            if (s.Phase != PhaseState.Combat) return;
            float w = 620f;
            float x = _virtualW * 0.5f - w * 0.5f;
            float y = _virtualH - 196f;

            if (s.CurrentPlotIndex < 0)
            {
                Panel(new Rect(x, y, w, 30));
                return;
            }

            FarmPlot plot = s.World.Farm.Slots[s.CurrentPlotIndex];
            Panel(new Rect(x, y, w, 88));
            string groupText = s.ActiveGroupCount > 1 ? $"　（同时 {s.ActiveGroupCount} 块）" : "";

            // v23：一条生长进度 + 一条蓄水（无掘地/播种/洒水）
            string growText = plot.State == PlotState.Grown
                ? "已成熟 → 用锄头攻击收获"
                : (plot.Thirsty ? $"缺水停长（蓄水 0/{plot.WaterMax}）" : $"生长中 {plot.GrowthRemaining:0}s");
            Bar(new Rect(x + 10, y + 30, w - 20, 22), plot.GrowthProgress01,
                plot.State == PlotState.Grown ? new Color(0.95f, 0.78f, 0.25f, 0.95f) : new Color(0.45f, 0.75f, 0.35f, 0.95f),
                growText + groupText);
            Bar(new Rect(x + 10, y + 56, w - 20, 20), plot.WaterMax <= 0 ? 0f : (float)plot.Water / plot.WaterMax,
                new Color(0.3f, 0.6f, 0.9f, 0.95f), $"蓄水 {plot.Water}/{plot.WaterMax}（水壶攻击土地补水）");
            // v93：删除"施肥（空格）0/0 次"弹窗 —— 旧版设计的残留。
            //
            // 旧版是"站在耕地上对着它连按空格施肥若干次才催熟"；肥料后来改成了**投掷爆炸**
            // （见 RunSession 的 ArmBlast / ExplodeAt），这条"按次数"的路径就没人驱动了。
            // 留下的证据很直白：面板上显示的是 `0 / 0 次` —— FertilizeTarget 恒为 0，
            // 进度条永远空着，玩家看到的是一个没有任何作用的提示框。
            //
            // 只删呈现，不删 s.FertilizeHits / s.FertilizeTarget 两个字段：
            // 它们在 RunSession 里仍参与判定与埋点，动它们要连带改逻辑层，不属于"删旧版 UI"的范围。
            //
            // 水壶的补水提示保留在上面那行"蓄水 x/y（水壶攻击土地补水）"——那才是现行机制。

            if (!string.IsNullOrEmpty(s.Prompt))
                LabelOutlined(new Rect(x, y - 26, w, 24), s.Prompt, _center);
        }

        private static string StateLabel(FarmPlot plot)
        {
            switch (plot.State)
            {
                case PlotState.Empty: return "未开垦";
                case PlotState.Dug: return "已掘地";
                case PlotState.Growing: return "生长中";
                case PlotState.Grown: return "成株";
                default: return "锁定";
            }
        }

        private static string ActionLabel(FarmPlot plot)
        {
            switch (plot.NextAction)
            {
                case PlotAction.Dig: return "掘地 2.0s";
                case PlotAction.Sow: return "播种 1.0s";
                case PlotAction.Water: return "洒水（生长 -30%）";
                case PlotAction.Fertilize: return "施肥（生长 -50%）";
                default: return "无需操作";
            }
        }

        // ------------------------------------------------------------ 开局三选一（鼠标点击）
        private void DrawSeedChoice(RunSession s)
        {
            // v94：整屏半透明黑遮罩（需求"为背景增加半透明黑色遮罩"）。
            //
            // 目的不是"把画面调暗"，而是**把还亮着的战场隔到后面去**：
            // 选种是开局第一件事，此时场上已经有敌人、飘字、霓虹在动，
            // 玩家一边读三个种子的属性一边被那些东西分神。
            // 遮罩画在卡片**之前**，所以卡片与文字仍然清晰。
            Color maskPrev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(new Rect(0f, 0f, _virtualW, _virtualH), White);
            GUI.color = maskPrev;

            float w = 940f, h = 420f;
            Rect r = new Rect(_virtualW * 0.5f - w * 0.5f, _virtualH * 0.5f - h * 0.5f, w, h);
            Panel(r);
            GUI.Label(new Rect(r.x, r.y + 10, w, 34), "选择初始种子（鼠标点击，或按数字键 1–3）", _title);
            for (int i = 0; i < s.InitialChoices.Length; i++)
            {
                SeedDef def = s.InitialChoices[i];
                int quality = i < s.InitialChoiceQuality.Length ? s.InitialChoiceQuality[i] : 0;
                // v48：卡片 264→296、面板 880→940，配合信息区自动换行，右侧不再被截断
                Rect card = new Rect(r.x + 22 + i * 306, r.y + 62, 296, 330);
                Color prev = GUI.color;
                GUI.color = new Color(0.14f, 0.16f, 0.13f, 0.95f);
                GUI.DrawTexture(card, White);
                GUI.color = QualityColor(quality);
                GUI.DrawTexture(new Rect(card.x, card.y, card.width, 5), White);
                GUI.color = prev;
                // v19：与商店同样的种子卡（含滑动条），只是按钮是"选择"
                var slot = new SeedSlot { Def = def, Quality = quality };
                ButtonReserve = 36f;
                SeedCardPanel(s, slot, card, i, _choiceScroll);
                ButtonReserve = 0f;
                if (GUI.Button(new Rect(card.x + 10, card.yMax - 32, card.width - 20, 24), $"选择（{i + 1}）"))
                    PendingInitialChoice = i;
            }
        }

        // ------------------------------------------------------------ 商店（鼠标点击购买）
                // ------------------------------------------------------------ 结算
        /// <summary>
        /// 结算画面。v165：**只有"守园失败"一种**。
        ///
        /// 旧版是 `DrawResult(s, victory)`，而 `victory` 恒为 false
        /// （`PhaseState.Victory` 在工程里 0 命中，见 Defs.cs 的说明）✗
        ///
        /// 结算行原来写 `波次 {WaveIndex}/{TotalWaves}` —— `TotalWaves` 恒为配置里的 20，
        /// 于是第 20 分钟之后会显示"波次 21/20"这种假数据 ✗
        /// 计时制没有终点，该显示的是**坚持了多久** ✓
        /// </summary>
        private void DrawResult(RunSession s)
        {
            float w = 520f, h = 260f;
            Rect r = new Rect(_virtualW * 0.5f - w * 0.5f, _virtualH * 0.5f - h * 0.5f, w, h);
            Panel(r);
            GUI.Label(new Rect(r.x, r.y + 14, w, 40), "守园失败", _title);
            GUI.Label(new Rect(r.x + 20, r.y + 66, w - 40, 120),
                $"{s.ResultText}\n\n坚持 {FmtTime(s.RunTime)}（第 {s.WaveIndex} 分钟）　击杀 {s.Kills}\n" +
                $"金币 {s.Gold}　等级 {s.Progress.Level}\n" +
                // ★ v166：改成读**已占用的列数**（= 玩家实际种了几株）★
                // 为什么不再读植物实体数：新玩法里"一株植物"住在**一列地块**上 ✓
                // ★ T2 更新：`World.Plants` 这个列表**本身已经删除**（不只是"无生产者"）✗
                //   所以这里读 `Farm.PlantCount` 不再只是"口径更对"，而是**唯一读法** ✓
                // 玩家在结算面板上会永远看到"剩余植物 0"，而他明明种了 3 株 ✓ 那就是在撒谎 ✗
                // `Farm.PlantCount` = 有作物的列数 = 株数 ✓ `Farm.Columns` = 可种植株数上限 ✓
                $"植物 {s.World.Farm.PlantCount}/{s.World.Farm.Columns} 株　耕地 {s.World.Farm.PlotCount} 格", _center);
            if (GUI.Button(new Rect(r.x + w * 0.5f - 90, r.y + h - 56, 180, 36), "重新开始"))
                PendingRestart = true;
        }

        // ------------------------------------------------------------ v10 菜单（左：退出/重开 中：物资 右：属性）
        private void DrawMenu(RunSession s)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, _virtualW, _virtualH), White);
            GUI.color = prev;

            float pad = 16f, top = 70f, bottom = _virtualH - 40f;
            // ★ v172：左栏 240 → 380 ★ —— 武器框要放下"伤害算式 + 3 行数值 + 6 个插件槽"✓
            // 中栏（植物 3×2 中格子）拿到剩余宽度 = 598 ⇒ 每格 190px ✓ 仍然够宽 ✓
            float leftW = 380f;
            // v67：右栏**按内容定宽**，不要再吃剩余空间。
            // 旧版是 midW = 34%、右栏 = 剩下的全部（约 651px），而属性文字最长一行只有 ~300px，
            // 结果右半边整块空着，看着像面板没画完。现在：左栏固定 240、右栏固定 380、
            // 中间的植物栏吃掉剩余宽度（它才是内容最多的一栏）。
            float rightW = 380f;
            float rightX = _virtualW - pad - rightW;
            float midW = rightX - pad - (pad + leftW) - pad;
            float h = bottom - top;

            // ★ v172：左栏拆成"菜单按钮（上）＋ 武器总览（下）"★
            //
            // 用户标准（原话）："同步更新 UI 框…预留更大的区域给角色武器，★ 商店、菜单都是 ★"✓
            // ⇒ 菜单里也要**看得到武器与角色**：三把武器 + 伤害算式 + 6 个插件槽 ✓
            //   （角色属性本来就在右栏 `DrawStatsColumns` ✓ 两者合起来就是"武器与属性的总览"✓）
            float menuBtnH = 124f;
            Panel(new Rect(pad, top, leftW, menuBtnH));
            GUI.Label(new Rect(pad + 12, top + 6, leftW - 20, 30), "菜单", _bold);
            if (GUI.Button(new Rect(pad + 16, top + 40, leftW - 32, 40), "继续游戏（Esc）")) PendingResume = true;
            if (GUI.Button(new Rect(pad + 16, top + 86, leftW - 32, 32), "重新开始（Shift+R）")) PendingRestart = true;
            DrawWeaponSection(s, new Rect(pad, top + menuBtnH + 8f, leftW, h - menuBtnH - 8f),
                "武器（三把 · 伤害算式 · 插件槽）");

            // 中：持有物种（种子大格子 + 道具小格子）
            Rect mid = new Rect(pad + leftW + pad, top, midW, h);
            Panel(mid);
            GUI.Label(new Rect(mid.x + 12, mid.y + 6, mid.width - 20, 28),
                $"植物（{s.Bag.OccupiedCount}/{s.Bag.SlotCount}）　一株独占一列 · 3×2 中格子", _bold);

            // ★ v172：从"6 张 210px 的大卡竖排 + 滚动"改成"6 个中格子 3×2 一屏铺满"★
            //
            // 用户标准（原话）："缩小植物区域，改成 6 个格子，中格子，显示名称、品质、等级、成熟时间、
            // 金币、经验、特殊效果"✓
            // 改前：内容高 6×218 = 1308px 塞进 ~379px 的视口 ⇒ 一次只看得见 **1.8 张** ✓
            //       ⇒ "比较 6 株植物"这件事在物理上做不到 ✓
            // 改后：6 个格子**一屏全见** ⇒ 每格 190×186 ✓ 七项齐全（与主界面同一个 `DrawPlantCell` ✓）
            Rect seedView = new Rect(mid.x + 8, mid.y + 38, mid.width - 16, h * 0.55f);
            float gpad = 6f;
            float gw = (seedView.width - gpad * 2f) / 3f;
            float gh = (seedView.height - gpad) / 2f;
            for (int i = 0; i < s.Bag.SlotCount && i < 6; i++)
                DrawPlantCell(s, s.Bag[i],
                    new Rect(seedView.x + (i % 3) * (gw + gpad), seedView.y + (i / 3) * (gh + gpad), gw, gh));

            // v18：道具用小格子展示（图标 + 右上角 ×数量），点击查看详情
            GUI.Label(new Rect(mid.x + 12, mid.y + h * 0.55f + 44, mid.width - 20, 28), $"道具（{s.ItemStacks.Count}）", _bold);
            Rect itemView = new Rect(mid.x + 8, mid.y + h * 0.55f + 76, mid.width - 16, h * 0.45f - 88);
            var itemIds = new System.Collections.Generic.List<string>(s.ItemStacks.Keys);
            int perRow = 5;
            float tileW = (itemView.width - 24f) / perRow - 6f;
            float tileH = Mathf.Min(tileW, 56f);
            int rows = Mathf.CeilToInt(itemIds.Count / (float)perRow);
            float ih = Mathf.Max(itemView.height, rows * (tileH + 8f) + 30f);
            _itemScroll = GUI.BeginScrollView(itemView, _itemScroll, new Rect(0, 0, itemView.width - 18, ih));
            for (int i = 0; i < itemIds.Count; i++)
            {
                ItemDef def = TableDatabase.GetItem(itemIds[i]);
                if (def == null) continue;
                int col = i % perRow, row = i / perRow;
                Rect tile = new Rect(col * (tileW + 6f), row * (tileH + 8f), tileW, tileH);
                Color pv = GUI.color;
                GUI.color = _itemSelected == itemIds[i] ? ItemTileSel : SlotBg;
                GUI.DrawTexture(tile, White);
                GUI.color = QualityColor((int)def.Grade);
                GUI.DrawTexture(new Rect(tile.x, tile.y, tile.width, 4f), White);
                GUI.color = pv;
                if (GUI.Button(new Rect(tile.x + 2f, tile.y + 4f, tile.width - 4f, tile.height - 6f), ""))
                    _itemSelected = _itemSelected == itemIds[i] ? null : itemIds[i];
                GUI.Label(new Rect(tile.x + 4f, tile.y + tile.height * 0.35f, tile.width - 8f, 20f),
                    $"{s_rarityName(def.RarityTier)}", _center);
                // 右上角数量
                GUI.Label(new Rect(tile.xMax - 34f, tile.y + 2f, 30f, 18f), $"×{s.ItemStacks[itemIds[i]]}", _right);
            }
            if (itemIds.Count == 0) GUI.Label(new Rect(0, 0, itemView.width, 22f), "（还没有道具）", _small);
            GUI.EndScrollView();

            // 详情（点击道具图标后显示）
            // v51：描述与效果都可能超一行，这里用允许换行的样式并预留两行高度
            const float rowH = 20f;
            float descH = rowH * 2f;
            Rect detail = new Rect(mid.x + 8, mid.yMax - (14f + descH * 2f + 30f), mid.width - 16, 14f + descH * 2f + 30f);
            Panel(detail);
            ItemDef sel = string.IsNullOrEmpty(_itemSelected) ? null : TableDatabase.GetItem(_itemSelected);
            if (sel == null)
                GUI.Label(new Rect(detail.x + 8, detail.y + 6, detail.width - 16, 22), "点击道具图标查看详情", _small);
            else
            {
                float avail = detail.width - 16f;
                GUI.Label(new Rect(detail.x + 8, detail.y + 4, avail, 22),
                    $"{s_rarityName(sel.RarityTier)}　{sel.Name} ×{s.ItemStacks[_itemSelected]}", _bold);
                string desc = sel.Desc;
                string effect = $"效果：{StatName(sel.Stat.ToString())} {(sel.IsPercent ? "+" + sel.Add + "%" : "+" + sel.Add)}　持有上限 {sel.MaxStack}";
                RowsFor(desc, avail, "菜单道具描述");
                RowsFor(effect, avail, "菜单道具效果");
                GUI.Label(new Rect(detail.x + 8, detail.y + 28, avail, descH), desc, WrapSmall);
                GUI.Label(new Rect(detail.x + 8, detail.y + 28 + descH, avail, descH), effect, WrapSmall);
            }

            // 右：属性（主要 / 次要 分开，可滚动）
            Panel(new Rect(rightX, top, rightW, h));
            DrawStatsColumns(s, rightX, top, rightW, h, "属性");
        }
        public void ConsumeInto(ref FrameInput input)
        {
            if (PendingInitialChoice >= 0) input.InitialChoice = PendingInitialChoice;
            if (PendingBuyOffer >= 0) input.BuyOffer = PendingBuyOffer;
            if (PendingReroll) input.Reroll = true;
            if (PendingSell >= 0) input.SellSlot = PendingSell;
            if (PendingNextWave) input.Skip = true;
            if (PendingSlotClick >= 0) { input.SlotClicked = true; input.ClickedSlot = PendingSlotClick; }
            PendingInitialChoice = -1;
            PendingBuyOffer = -1;
            PendingReroll = false;
            PendingSell = -1;
            PendingNextWave = false;
            PendingSlotClick = -1;
            PendingMergeAll = false;
            if (PendingSlotMerge >= 0) { input.SlotMergeClicked = true; input.SlotMerge = PendingSlotMerge; PendingSlotMerge = -1; }
            if (PendingOfferLock >= 0) { input.OfferLockToggled = true; input.ToggleOfferLock = PendingOfferLock; PendingOfferLock = -1; }
            if (PendingRewardSelect >= 0) { input.RewardSelectSet = true; input.RewardSelect = PendingRewardSelect; PendingRewardSelect = -1; }
            if (PendingRewardPick >= 0) { input.RewardPickConfirmed = true; input.RewardPick = PendingRewardPick; PendingRewardPick = -1; }
            if (PendingRewardReroll) { input.RewardReroll = true; PendingRewardReroll = false; }
        }
    }
}
