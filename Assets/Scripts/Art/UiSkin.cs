using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// UI 皮肤：把 Resources/Art/UI 下的九宫格贴图装成 GUIStyle。
    ///
    /// IMGUI 的九宫格靠 <c>GUIStyle.border</c> 描述"四边不参与拉伸的像素宽度"，
    /// 中间区域随控件尺寸拉伸——所以同一张 64×64 的面板能画任意大小的窗口而不变形，
    /// 不需要为每种尺寸各出一张图。
    ///
    /// 皮肤是懒加载的：HUD 在没有这些资源时也能正常工作（退回纯色底），
    /// 这样"美术资源缺失"不会变成"界面打不开"。
    /// </summary>
    public static class UiSkin
    {
        public static Texture2D Panel, PanelDark, Card, TextBox;
        public static Texture2D Button, ButtonHover, ButtonPress;
        public static Texture2D BarFrame, BarWhite;
        public static Texture2D ScrollTrack, ScrollThumb;
        public static Texture2D IconHoe, IconCan, IconFert, IconCoin, IconSeed;
        public static Texture2D IconHeart, IconShield, IconClock, IconWave, IconStar;

        public static GUIStyle PanelStyle, PanelDarkStyle, CardStyle, TextBoxStyle;
        public static GUIStyle ButtonStyle, ButtonHoverStyle, ButtonPressStyle;
        public static GUIStyle BarFrameStyle, BarFillStyle;
        public static GUIStyle ScrollTrackStyle, ScrollThumbStyle;

        public static bool Ready { get; private set; }

        private static bool _loaded;

        public static void Ensure()
        {
            if (_loaded) return;
            _loaded = true;

            Panel = Load("panel");
            PanelDark = Load("panel_dark");
            Card = Load("card");
            TextBox = Load("textbox");
            ScrollTrack = Load("scroll_track");
            ScrollThumb = Load("scroll_thumb");
            Button = Load("button");
            ButtonHover = Load("button_hover");
            ButtonPress = Load("button_press");
            BarFrame = Load("bar_frame");
            BarWhite = Load("bar_white");
            IconHoe = Load("icon_hoe");
            IconCan = Load("icon_can");
            IconFert = Load("icon_fertilizer");
            IconCoin = Load("icon_coin");
            IconSeed = Load("icon_seed");
            IconHeart = Load("icon_heart");
            IconShield = Load("icon_shield");
            IconClock = Load("icon_clock");
            IconWave = Load("icon_wave");
            IconStar = Load("icon_star");

            // 面板 border=18（贴图 64×64，四边各留 18px 不拉伸）
            PanelStyle = Nine(Panel, 18, 6);
            PanelDarkStyle = Nine(PanelDark, 18, 6);
            CardStyle = Nine(Card, 16, 4);
            ButtonStyle = Nine(Button, 14, 4);
            ButtonHoverStyle = Nine(ButtonHover, 14, 4);
            ButtonPressStyle = Nine(ButtonPress, 14, 4);
            BarFrameStyle = Nine(BarFrame, 6, 0);
            BarFillStyle = Nine(BarWhite, 6, 0);
            // v71：文本框与滑动条
            TextBoxStyle = Nine(TextBox, 10, 2);
            ScrollTrackStyle = Nine(ScrollTrack, 6, 0);
            ScrollThumbStyle = Nine(ScrollThumb, 7, 0);

            Ready = PanelStyle.normal.background != null;
        }

        private static Texture2D Load(string name)
        {
            return Resources.Load<Texture2D>("Art/UI/" + name);
        }

        private static GUIStyle Nine(Texture2D tex, int border, int padding)
        {
            var st = new GUIStyle();
            if (tex != null) st.normal.background = tex;
            st.border = new RectOffset(border, border, border, border);
            st.padding = new RectOffset(padding, padding, padding, padding);
            st.alignment = TextAnchor.MiddleCenter;
            return st;
        }
    }
}
