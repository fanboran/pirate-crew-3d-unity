using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>色身份：一条同色相的明暗阶梯。容器/按钮/条状件全部由这 7 条 tone 派生。</summary>
    public enum PixelTone
    {
        /// <summary>主面板（深板岩）。</summary>
        Frame = 0,
        /// <summary>内容片/列表行（比面板低一档）。</summary>
        Dense = 1,
        /// <summary>暖白牌（标题板 / 浅按钮 / Toast）。</summary>
        Light = 2,
        /// <summary>海图（小地图 / 航海容器）。</summary>
        Sea = 3,
        /// <summary>主行动点（黄铜，深字）。</summary>
        Primary = 4,
        /// <summary>危险动作（红）。</summary>
        Danger = 5,
        /// <summary>警告/冷却（暖橙）。</summary>
        Warn = 6,
    }

    /// <summary>容器结构：凸起块 / 凹槽 / 页签。</summary>
    public enum PixelPiece
    {
        Plate = 0,
        Track = 1,
        /// <summary>页签：底边无带（border 下=0），底边贴宿主面板顶边连成一体。</summary>
        Tab = 2,
        /// <summary>面板（对话框外层，Asepite dark 语法）：**直角**（对照参考截图裁决——
        /// 圆角只属于按钮），黑环 1 格 + 上/左受光唇 + 下/右背光唇 + 平涂主体。</summary>
        Panel = 3,
        /// <summary>带标题栏窗体（theme window 3/7/3 × 15/4/5）：顶部 15u 标题带
        /// （tone 亮档）+ 带底暗线 + 窗体面；内容须避开标题带（15u）。</summary>
        Window = 4,
    }

    /// <summary>状态：常态 / 悬停（整条色阶上抬一档）/ 按压（高光阴影对调）。</summary>
    public enum PixelState
    {
        Normal = 0,
        Hovered = 1,
        Pressed = 2,
    }

    /// <summary>填充条色身份（画在 Track 内容区上的那一层）。</summary>
    public enum PixelFillKind
    {
        Red = 0,
        Blue = 1,
        Warn = 2,
        Sea = 3,
        Neutral = 4,
    }

    /// <summary>
    /// Beveled Pixel 皮肤的运行时取用层：全部 UI 贴图/取色从这里走，别处不许
    /// 自己 LoadAssetAtPath / Resources.Load 像素件（槽位散落是上一版换皮难的根因）。
    ///
    /// 【几何口径】u（基本单位）= <see cref="Unit"/>，当前 = 2：贴图按设计格 ×Unit 落盘
    /// （如按钮模板 14×16 格 → 28×32 纹素），画布为 960×540 低清栈（1080p ÷ 2，整数 ×2 显示）。
    /// 【沿革】2026-09-22 起为 3（640×360 RT 口径）；2026-09-24 深夜低清画布栈切换改 2
    /// 并同时是烘焙倍率与画布除数（详见交接档《像素UI与字阶》§三语义澄清——×2 语义
    /// 定夺仍是创始人待验收悬案）。
    /// 改 Unit 必须同步渲染器资产 renderHeightPixels（判据双源）与 Editor 侧
    /// <c>BeveledPixelSpriteBuilder</c> 的判据——判据会读 URP 渲染器资产里的
    /// renderHeightPixels 反向锁这条；<see cref="PirateCrew.Rendering.Pixelart.PixelartPilotScene.PixelScale"/>
    /// 与本值断言相等。
    /// 装配侧尺寸纪律：可见包边件的 width/height 不低于九宫格切片和（见 <see cref="PlateMinRender"/>），
    /// anchoredPosition 至少取整——分数像素会让色带糊宽。
    /// </summary>
    public static class PixelSkin
    {
        /// <summary>tone 数（与调色板侧 tone 表同长，改一处必须同步烘焙器）。</summary>
        public const int ToneCount = 7;

        /// <summary>填充色数。</summary>
        public const int FillCount = 5;

        /// <summary>
        /// 基本单位（当前语义：设计格 → 贴图像素的烘焙倍率，兼作画布参考分辨率的除数）：
        /// 外环/斜面/内暗线各 1u 厚。现值 2。
        /// </summary>
        public const int Unit = 2;

        /// <summary>
        /// 36×36 家族（页签/投影）的九宫格切片边框 = 2u。**Plate 按钮件不走本值**——
        /// 按钮按 Aseprite button 口径烘（14×16 模板，切片 左/右 4u、下 6u、上 4u，
        /// 见 Editor 侧 <c>BeveledPixelSpriteBuilder</c> 目标表与贴图 meta）。
        /// </summary>
        public const int PlateBorder = 2 * Unit;

        /// <summary>低于它装配就不能用九宫格（角会切进内容区），装配侧应断言。
        /// = 新按钮模板的上下切片和（4u + 6u）。</summary>
        public const int PlateMinRender = 10 * Unit;

        /// <summary>
        /// 按压态元素位移：右下 **1 艺术像素**（= Unit，与投影同距；贴图里不烘位移，
        /// 烘了九宫格切片错位）。原值 (1,-1) 只有 1 屏幕像素——比一个艺术像素还小，
        /// 既看不出"沉下去"、又把件挪出了像素栅格（创始人 2026-09-22 走查"按压太不明显"）。
        /// </summary>
        public static readonly Vector2 PressOffset = new Vector2(Unit, -Unit);

        /// <summary>面板投影相对面板本体的偏移：右下 1u（投影是独立剪影件）。</summary>
        public static readonly Vector2 ShadowOffset = new Vector2(Unit, -Unit);

        const string AssetPath = "UI/PixelSkin";

        static PixelSkinAsset s_asset;
        static bool s_loaded;

        /// <summary>图集资产（首次取用时从 Resources 惰性加载；缺资产只报一次错）。</summary>
        public static PixelSkinAsset Asset
        {
            get
            {
                if (!s_loaded)
                {
                    s_loaded = true;
                    s_asset = Resources.Load<PixelSkinAsset>(AssetPath);
                    if (s_asset == null)
                        Debug.LogError("[PixelSkin] 图集资产缺失：Resources/" + AssetPath
                            + ".asset（Editor 侧跑 PirateCrew/UI/重烘焙 Beveled Pixel 九宫格 重新生成）。");
                }
                return s_asset;
            }
        }

        /// <summary>凸起块（面板/按钮/列表行）。九宫格，尺寸 ≥ <see cref="PlateMinRender"/>。</summary>
        public static Sprite Plate(PixelTone tone, PixelState state = PixelState.Normal)
        {
            return SpriteAt(Asset != null ? Asset.plates : null, ((int)tone) * 3 + (int)state,
                "Plate/" + tone + "/" + state);
        }

        /// <summary>凹槽（条状件空槽底）。</summary>
        public static Sprite Track(PixelTone tone)
        {
            return SpriteAt(Asset != null ? Asset.tracks : null, (int)tone, "Track/" + tone);
        }

        /// <summary>面板（直角，对话框外层/卡片底）。Aseprite 参照：圆角只属于按钮。</summary>
        public static Sprite Panel(PixelTone tone)
        {
            return SpriteAt(Asset != null ? Asset.panels : null, (int)tone, "Panel/" + tone);
        }

        /// <summary>页签（底边无带，底边贴宿主面板顶边；未选中页签用 Dense，选中用内容 tone）。</summary>
        public static Sprite Tab(PixelTone tone)
        {
            return SpriteAt(Asset != null ? Asset.tabs : null, (int)tone, "Tab/" + tone);
        }

        /// <summary>填充条（红=血量 / 蓝=魔法 / 暖橙=冷却 / 海蓝=航行 / 暖白=中性进度）。</summary>
        public static Sprite Fill(PixelFillKind kind)
        {
            return SpriteAt(Asset != null ? Asset.fills : null, (int)kind, "Fill/" + kind);
        }

        /// <summary>选人圈（暖金方环；徽章外环/战场标记）。</summary>
        public static Sprite Ring { get { return Single("Ring", Asset != null ? Asset.ring : null); } }

        /// <summary>键盘焦点框（蓝白方环；选中态包在控件外沿，别再用乘色）。</summary>
        public static Sprite Focus { get { return Single("Focus", Asset != null ? Asset.focus : null); } }

        /// <summary>位点（页点/队伍槽指示）：on=黄铜宝石 / off=中性暗。</summary>
        public static Sprite Pip(bool on)
        {
            PixelSkinAsset a = Asset;
            return Single("Pip(" + on + ")", a == null ? null : on ? a.pipOn : a.pipOff);
        }

        /// <summary>蚀刻分隔线。</summary>
        public static Sprite Separator(bool horizontal)
        {
            PixelSkinAsset a = Asset;
            return Single("Separator(" + horizontal + ")",
                a == null ? null : horizontal ? a.separatorH : a.separatorV);
        }

        /// <summary>面板投影（INK 剪影；垫在面板下按 <see cref="ShadowOffset"/> 错开）。</summary>
        public static Sprite ShadowSprite
        {
            get { return Single("Shadow", Asset != null ? Asset.shadow : null); }
        }

        // ---------- Aseprite dark 全部件搬皮（2026-09-25；theme.xml 精确复刻） ----------

        /// <summary>带标题栏窗体（theme window：顶 15u 标题带；内容须避开标题带）。</summary>
        public static Sprite Window(PixelTone tone)
        {
            return SpriteAt(Asset != null ? Asset.windows : null, (int)tone, "Window/" + tone);
        }

        /// <summary>窗体标题带高（theme window h1=15 设计格）——内容区从带底往下排。</summary>
        public const int WindowTitleBand = 15 * Unit;

        /// <summary>窗控钮（theme window_button 9×11；常态/悬停/按压）。</summary>
        public static Sprite WindowButton(PixelState state)
        {
            return SpriteAt(Asset != null ? Asset.windowButtons : null, (int)state,
                "WindowButton/" + state);
        }

        /// <summary>窗控图标索引（与图集 windowIcons 同序）。</summary>
        public enum WindowIcon { Close = 0, Help, Play, Stop, Center }

        /// <summary>窗控图标（5×6，乘色换染；<see cref="Theme.Text"/> 是 theme 常态色）。</summary>
        public static Sprite WindowIconSprite(WindowIcon icon)
        {
            return SpriteAt(Asset != null ? Asset.windowIcons : null, (int)icon, "WindowIcon/" + icon);
        }

        /// <summary>复选框（theme check 8×8；selected = ✓）。</summary>
        public static Sprite Check(bool selected)
        {
            return SpriteAt(Asset != null ? Asset.checks : null, selected ? 1 : 0,
                "Check/" + selected);
        }

        /// <summary>单选钮（theme radio 8×8；selected = 中心点）。</summary>
        public static Sprite Radio(bool selected)
        {
            return SpriteAt(Asset != null ? Asset.radios : null, selected ? 1 : 0,
                "Radio/" + selected);
        }

        /// <summary>复选/单选焦点框（theme check_focus 2/6/2）。</summary>
        public static Sprite WidgetFocus
        {
            get { return Single("WidgetFocus", Asset != null ? Asset.widgetFocus : null); }
        }

        /// <summary>凹槽（theme sunken：textedit/列表底；focused = 蓝环）。</summary>
        public static Sprite Sunken(bool focused)
        {
            return SpriteAt(Asset != null ? Asset.sunken : null, focused ? 1 : 0,
                "Sunken/" + focused);
        }

        /// <summary>滑条空槽（theme slider_empty；focused = 蓝环）。</summary>
        public static Sprite SliderEmpty(bool focused)
        {
            return SpriteAt(Asset != null ? Asset.sliderEmpty : null, focused ? 1 : 0,
                "SliderEmpty/" + focused);
        }

        /// <summary>滑条充满段（theme slider_full 金色；focused = 蓝环）。</summary>
        public static Sprite SliderFull(bool focused)
        {
            return SpriteAt(Asset != null ? Asset.sliderFull : null, focused ? 1 : 0,
                "SliderFull/" + focused);
        }

        /// <summary>滑条拇指（theme mini_slider_thumb 5×4）。</summary>
        public static Sprite SliderThumb
        {
            get { return Single("SliderThumb", Asset != null ? Asset.sliderThumb : null); }
        }

        /// <summary>滚动条（theme scrollbar：bg=底 / thumb=滑块，宽 16）。</summary>
        public static Sprite Scrollbar(bool thumb)
        {
            return SpriteAt(Asset != null ? Asset.scrollbars : null, thumb ? 1 : 0,
                "Scrollbar/" + thumb);
        }

        /// <summary>气泡底（theme tooltip 蓝底）。</summary>
        public static Sprite Tooltip
        {
            get { return Single("Tooltip", Asset != null ? Asset.tooltip : null); }
        }

        /// <summary>组合框下拉箭头（常态/选中/禁用）。</summary>
        public static Sprite ArrowDown(PixelState state)
        {
            return SpriteAt(Asset != null ? Asset.arrowsDown : null,
                state == PixelState.Pressed ? 2 : (int)state, "ArrowDown/" + state);
        }

        /// <summary>theme.xml 精确色（Aseprite dark 权威配色；搬皮件的唯一取色源）。</summary>
        public static class Theme
        {
            /// <summary>text / button_normal_text / textbox_text。</summary>
            public static readonly Color32 Text = new Color32(0xC0, 0xC0, 0xC0, 0xFF);
            /// <summary>button_selected_text / link_hover 基。</summary>
            public static readonly Color32 TextSelected = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
            /// <summary>face / window_face。</summary>
            public static readonly Color32 Face = new Color32(0x2C, 0x2C, 0x30, 0xFF);
            /// <summary>background / window_titlebar_face / listitem_normal_face。</summary>
            public static readonly Color32 Background = new Color32(0x41, 0x44, 0x4A, 0xFF);
            /// <summary>disabled / editor_face。</summary>
            public static readonly Color32 Disabled = new Color32(0x20, 0x21, 0x25, 0xFF);
            /// <summary>check/radio_hot_face。</summary>
            public static readonly Color32 HotFace = new Color32(0x57, 0x5B, 0x61, 0xFF);
            /// <summary>selected / listitem_selected_face（选中金）。</summary>
            public static readonly Color32 Selected = new Color32(0xE1, 0xB8, 0x5F, 0xFF);
            /// <summary>selected_text（选中底上的字）。</summary>
            public static readonly Color32 SelectedText = new Color32(0x41, 0x44, 0x4A, 0xFF);
            /// <summary>separator_label / link_text（蓝分组字）。</summary>
            public static readonly Color32 SeparatorLabel = new Color32(0x6E, 0x9A, 0xDB, 0xFF);
            /// <summary>tooltip_face（气泡蓝底）。</summary>
            public static readonly Color32 TooltipFace = new Color32(0x40, 0x69, 0xC2, 0xFF);
            /// <summary>tab_normal_text（未选页签灰）。</summary>
            public static readonly Color32 TabNormalText = new Color32(0x7D, 0x7D, 0x7D, 0xFF);
            /// <summary>status_bar_face / workspace。</summary>
            public static readonly Color32 StatusFace = new Color32(0x33, 0x33, 0x33, 0xFF);
            /// <summary>status_bar_text。</summary>
            public static readonly Color32 StatusText = new Color32(0x63, 0x6D, 0x79, 0xFF);
        }

        /// <summary>tone 亮档（S4）——深底上的文字/线条色，取同族色别自己调。</summary>
        public static Color32 LightOf(PixelTone tone) { return ColorAt(tone, 0); }

        /// <summary>tone 中档（S3 = Plate 底色）。</summary>
        public static Color32 MidOf(PixelTone tone) { return ColorAt(tone, 1); }

        /// <summary>tone 暗档（S2）。</summary>
        public static Color32 DarkOf(PixelTone tone) { return ColorAt(tone, 2); }

        /// <summary>墨色（浅底上的文字/线条）。</summary>
        public static Color32 Ink { get { return Asset != null ? Asset.ink : new Color32(0x14, 0x12, 0x16, 255); } }

        /// <summary>暖白（深底上的最亮文字）。</summary>
        public static Color32 PaperWhite { get { return Asset != null ? Asset.paperWhite : Color.white; } }

        /// <summary>
        /// 某 tone 底上的正文字色（创始人 2026-09-23 裁决"背景深色就白色文字，否则才黑色"）：
        /// 中档亮度高于阈值 = 浅底 → 墨字；否则 → <see cref="PaperWhite"/> 暖白。
        /// 【旧口径已废】"本 tone 亮档字"会让 Danger 钮出红底红字、Dense 钮出灰底灰字——
        /// 文字和背景同色相，对比度崩（走查实拍）。
        /// 与烘焙器的"光来自左上"同一套感知口径（简单 sRGB 亮度，够用且可复算）。
        /// </summary>
        public static Color32 TextColorOn(PixelTone tone)
        {
            Color32 mid = MidOf(tone);
            float lum = (0.2126f * mid.r + 0.7152f * mid.g + 0.0722f * mid.b);
            return lum > 140f ? Ink : PaperWhite;
        }

        static Sprite SpriteAt(Sprite[] array, int index, string what)
        {
            if (array != null && index >= 0 && index < array.Length && array[index] != null)
                return array[index];
            Debug.LogError("[PixelSkin] 图集缺 " + what + "——重烘焙 PirateCrew/UI/重烘焙 Beveled Pixel 九宫格。");
            return null;
        }

        static Sprite Single(string what, Sprite sprite)
        {
            if (sprite == null)
                Debug.LogError("[PixelSkin] 图集缺 " + what + "——重烘焙 PirateCrew/UI/重烘焙 Beveled Pixel 九宫格。");
            return sprite;
        }

        static Color32 ColorAt(PixelTone tone, int shade)
        {
            PixelSkinAsset a = Asset;
            int i = (int)tone * 3 + shade;
            if (a != null && i < a.toneColors.Length)
                return a.toneColors[i];
            Debug.LogError("[PixelSkin] 图集缺 tone " + tone + " 的取色令牌。");
            return new Color32(255, 0, 255, 255);
        }
    }
}
