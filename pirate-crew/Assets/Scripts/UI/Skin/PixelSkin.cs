using System.Collections.Generic;
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

    /// <summary>
    /// Beveled Pixel 皮肤的运行时取用层：全部 UI 贴图/取色从这里走，别处不许
    /// 自己 LoadAssetAtPath / Resources.Load 像素件（槽位散落是上一版换皮难的根因）。
    ///
    /// 【几何口径（×1 终局，2026-09-25 创始人裁决）】1 设计格 = 1 贴图像素 = 1 画布像素，
    /// 贴图与布局零倍率、零压缩。theme 控件件由 Editor 侧 BeveledPixelSpriteBuilder
    /// 从 sheet.png 直切（九宫格切片 = theme.xml 声明值），运行时经 <see cref="Ase"/> 取。
    /// 【tone 贴图件族已退役（W3）】Plate/Track/Fill/Panel/Tab/Ring/Pip/Separator/Shadow
    /// 九族程序化贴图不再烘焙；面板/窗体皮一律走 Ase 直切件（<c>"menu"</c> / <c>"window"</c>）。
    /// tone 只余**取色令牌**（<see cref="LightOf"/> / <see cref="MidOf"/> / <see cref="TextColorOn"/>
    /// 与调色板 <c>toneColors</c>），供结算星/金色武器名等平涂取色。
    /// <see cref="Unit"/> 只余画布密度语义（CanvasScaler scaleFactor，画布 = 屏幕 ÷ 2，
    /// 恒定像素密度栈），不再参与贴图/布局换算。
    /// 装配侧尺寸纪律：可见包边件的 width/height 不低于九宫格切片和，anchoredPosition
    /// 至少取整——分数像素会让色带糊宽。
    /// </summary>
    public static class PixelSkin
    {
        /// <summary>tone 数（与调色板侧 tone 表同长，改一处必须同步烘焙器）。</summary>
        public const int ToneCount = 7;

        /// <summary>
        /// 画布密度（CanvasScaler scaleFactor）：画布 = 屏幕 ÷ Unit（1080p → 960×540，
        /// 显示端整数 ×2）。【×1 终局】不再兼作烘焙倍率——贴图/布局链路禁用本值。
        /// </summary>
        public const int Unit = 2;

        /// <summary>Track 族九宫格切片 = 2（黑环 1 + 唇边 1，×1 设计格）。按钮不走本值。</summary>
        public const int PlateBorder = 2;

        /// <summary>低于它装配就不能用九宫格（角会切进内容区），装配侧应断言。
        /// = theme button 上下切片和（4 + 6）。</summary>
        public const int PlateMinRender = 10;

        const string AssetPath = "UI/PixelSkin";

        static PixelSkinAsset s_asset;
        static bool s_loaded;
        static Dictionary<string, int> s_aseIndex;   // Ase 直切件名 → aseParts 下标（见 Ase 内失效口径）

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

        /// <summary>【存件·零调用方（UI 重构 W4 登记）】旧焦点框——现役键盘焦点一律
        /// <see cref="WidgetFocus"/>（= Ase check_focus）。保留仅为烘焙器存件，新代码禁用。</summary>
        public static Sprite Focus { get { return Single("Focus", Asset != null ? Asset.Focus : null); } }

        // ---------- Aseprite dark 直切件（×1 全量对齐波；sheet.png 直切，theme.xml <parts> 表） ----------

        /// <summary>
        /// Aseprite dark 主题直切件通用出口（part id = theme.xml &lt;parts&gt; 原名，如
        /// "button_focused" / "menu" / "tooltip_arrow"）。Editor 装配与运行时同源；
        /// 缺件红灯（重烘焙补件），绝不静默白块。
        /// </summary>
        public static Sprite Ase(string partId)
        {
            PixelSkinAsset a = Asset;
            if (a == null || a.AsePartNames == null || a.AseParts == null)
            {
                Debug.LogError("[PixelSkin] 图集缺 aseParts 表——重烘焙 PirateCrew/UI/重烘焙 Beveled Pixel 九宫格。");
                return null;
            }
            if (s_aseIndex == null)
            {
                // 名 → 下标惰性建一次：345 件全表线性扫落在装配热路径（首帧几十次 Ase 调用）
                // 是 O(n²)。【失效口径】s_asset 本就是进程级单例（Asset 惰性加载后不重载，
                // 运行期无换肤/重建资产的路径），静态缓存与其同生命周期；域重载（进出播放/
                // 脚本重编译）静态字段整体归零，缓存随之自动重建，无需另设失效开关。
                var index = new Dictionary<string, int>(a.AsePartNames.Count, System.StringComparer.Ordinal);
                for (int i = 0; i < a.AsePartNames.Count; i++)
                    if (!index.ContainsKey(a.AsePartNames[i]))
                        index.Add(a.AsePartNames[i], i);   // 同名取首个，与旧线性扫命中语义一致
                s_aseIndex = index;
            }
            if (s_aseIndex.TryGetValue(partId, out int idx) && a.AseParts[idx] != null)
                return a.AseParts[idx];
            Debug.LogError("[PixelSkin] 图集缺 Aseprite 直切件 \"" + partId
                + "\"——全量迁移由烘焙器枚举 theme.xml <parts>（345 件），重烘焙 PirateCrew/UI/重烘焙 Beveled Pixel 九宫格。");
            return null;
        }

        /// <summary>【存件·零调用方（UI 重构 W4 登记）】tone 族窗体皮——现役窗体一律
        /// <see cref="Ase("window")"/> 直切件（EnsureWindow）。保留仅为烘焙器存件，
        /// 新代码禁用；连同烘焙器程序化残段一并清退时再删。</summary>
        public static Sprite Window(PixelTone tone)
        {
            return SpriteAt(Asset != null ? Asset.Windows : null, (int)tone, "Window/" + tone);
        }

        /// <summary>窗体标题带高（theme window h1=15 设计格，×1 即 15 画布像素）——内容区从带底往下排。</summary>
        public const int WindowTitleBand = 15;

        /// <summary>窗控钮（theme window_button 9×11；UGUI 态映射：Normal→normal、Hovered→hot、Pressed→selected）。</summary>
        public static Sprite WindowButton(PixelState state)
        {
            string id;
            switch (state)
            {
                case PixelState.Hovered: id = "window_button_hot"; break;
                case PixelState.Pressed: id = "window_button_selected"; break;
                default: id = "window_button_normal"; break;
            }
            return Ase(id);
        }

        /// <summary>窗体图标索引（theme window_*_icon 直切件同序）。</summary>
        public enum WindowIcon { Close = 0, Help, Play, Stop, Center }

        /// <summary>窗控图标（5×6，乘色换染；<see cref="Theme.Text"/> 是 theme 常态色）。</summary>
        public static Sprite WindowIconSprite(WindowIcon icon)
        {
            switch (icon)
            {
                case WindowIcon.Help: return Ase("window_help_icon");
                case WindowIcon.Play: return Ase("window_play_icon");
                case WindowIcon.Stop: return Ase("window_stop_icon");
                case WindowIcon.Center: return Ase("window_center_icon");
                default: return Ase("window_close_icon");
            }
        }

        /// <summary>复选框（theme check 8×8；selected = ✓）。</summary>
        public static Sprite Check(bool selected)
        {
            return Ase(selected ? "check_selected" : "check_normal");
        }

        /// <summary>单选钮（theme radio 8×8；selected = 中心点）。</summary>
        public static Sprite Radio(bool selected)
        {
            return Ase(selected ? "radio_selected" : "radio_normal");
        }

        /// <summary>复选/单选焦点框（theme check_focus 2/6/2）。</summary>
        public static Sprite WidgetFocus
        {
            get { return Ase("check_focus"); }
        }

        /// <summary>凹槽（theme sunken：textedit/列表底；focused = 蓝环）。</summary>
        public static Sprite Sunken(bool focused)
        {
            return Ase(focused ? "sunken_focused" : "sunken_normal");
        }

        /// <summary>滑条空槽（theme slider_empty；focused = 蓝环）。</summary>
        public static Sprite SliderEmpty(bool focused)
        {
            return Ase(focused ? "slider_empty_focused" : "slider_empty");
        }

        /// <summary>滑条充满段（theme slider_full：内芯 **#41444A**，比空槽内芯 #575B61 更暗——
        /// 库里"充满"是压暗语法，不是彩色；focused 变体同件换焦点态）。</summary>
        public static Sprite SliderFull(bool focused)
        {
            return Ase(focused ? "slider_full_focused" : "slider_full");
        }

        /// <summary>滑条拇指（theme mini_slider_thumb 5×4）。</summary>
        public static Sprite SliderThumb
        {
            get { return Ase("mini_slider_thumb"); }
        }

        /// <summary>滚动条（theme scrollbar：bg=底 / thumb=滑块）。</summary>
        public static Sprite Scrollbar(bool thumb)
        {
            return Ase(thumb ? "scrollbar_thumb" : "scrollbar_bg");
        }

        /// <summary>气泡底（theme tooltip 蓝底；带箭头变体见 Ase("tooltip_arrow")）。</summary>
        public static Sprite Tooltip
        {
            get { return Ase("tooltip"); }
        }

        /// <summary>组合框下拉箭头（theme combobox_button 图标态）：常态 → normal、
        /// 弹开（UGUI Pressed 近似）→ selected、禁用 → disabled（enum 无 Disabled 档，暂不表达）；
        /// **悬停不换图标**——theme 只给按钮底换 hot 皮，图标在 mouse 态不动
        /// （旧版把 Pressed 映到 disabled 件、Hovered 映到 selected 件，都是自造）。</summary>
        public static Sprite ArrowDown(PixelState state)
        {
            switch (state)
            {
                case PixelState.Pressed: return Ase("combobox_arrow_down_selected");
                default: return Ase("combobox_arrow_down");
            }
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
        public static Color32 Ink { get { return Asset != null ? Asset.Ink : new Color32(0x14, 0x12, 0x16, 255); } }

        /// <summary>暖白（深底上的最亮文字）。</summary>
        public static Color32 PaperWhite { get { return Asset != null ? Asset.PaperWhite : Color.white; } }

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

        // 数组走 IReadOnlyList 只读视图（图集资产运行时只读），判空/下标读语义与数组一致
        static Sprite SpriteAt(IReadOnlyList<Sprite> array, int index, string what)
        {
            if (array != null && index >= 0 && index < array.Count && array[index] != null)
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
            if (a != null && i < a.ToneColors.Count)
                return a.ToneColors[i];
            Debug.LogError("[PixelSkin] 图集缺 tone " + tone + " 的取色令牌。");
            return new Color32(255, 0, 255, 255);
        }
    }
}
