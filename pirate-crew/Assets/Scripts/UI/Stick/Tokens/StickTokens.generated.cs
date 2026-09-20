// 由 tools/ui_sync/sync_to_unity.py 从 ui_tokens.json 自动生成，勿手改；真相源 stick-world/assets/config/ui_tokens.json

using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>stick-world UI 令牌编译期层（颜色/字号/形状/按钮变体/图标映射）。
    /// 数值与命名同 ui_tokens.json / Godot 侧保持一致，便于跨引擎对照排查；
    /// 消费端便利 API（LoadIcon 等）在手写伴生文件 StickTokens.cs。</summary>
    public static partial class StickTokens
    {
        // ---------------- 颜色（colors） ----------------
        public static readonly Color ACCENT = new Color(0.95f, 0.68f, 0.25f, 1f);
        public static readonly Color ACCENT_BG = new Color(0.95f, 0.68f, 0.25f, 0.14f);
        public static readonly Color ACCENT_TEXT = new Color(0.1f, 0.08f, 0.04f, 1f);
        public static readonly Color BORDER = new Color(1f, 1f, 1f, 0.16f);
        public static readonly Color BORDER_PANEL = new Color(1f, 1f, 1f, 0.3f);
        public static readonly Color BORDER_STRONG = new Color(1f, 1f, 1f, 0.38f);
        public static readonly Color BTN_BG = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color BTN_BG_DISABLED = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color BTN_BG_HOVER = new Color(1f, 1f, 1f, 0.15f);
        public static readonly Color BTN_BG_PRESSED = new Color(1f, 1f, 1f, 0.04f);
        public static readonly Color DANGER = new Color(0.9f, 0.34f, 0.3f, 1f);
        public static readonly Color DANGER_BG = new Color(0.9f, 0.34f, 0.3f, 0.14f);
        public static readonly Color GROOVE_BG = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color INFO = new Color(0.55f, 0.78f, 1f, 1f);
        public static readonly Color INK = new Color(0.05f, 0.04f, 0.03f, 1f);
        public static readonly Color MODAL_DIM = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color SUCCESS = new Color(0.45f, 0.8f, 0.48f, 1f);
        public static readonly Color TEXT = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color TEXT_DIM = new Color(0.93f, 0.94f, 0.96f, 0.55f);
        public static readonly Color TEXT_DISABLED = new Color(0.93f, 0.94f, 0.96f, 0.25f);
        public static readonly Color TEXT_FAINT = new Color(0.93f, 0.94f, 0.96f, 0.32f);
        public static readonly Color WARN = new Color(0.98f, 0.82f, 0.3f, 1f);
        public static readonly Color WINDOW_BG = new Color(0.012f, 0.014f, 0.02f, 0.88f);
        public static readonly Color WINDOW_BG_LIGHT = new Color(0.02f, 0.024f, 0.034f, 0.72f);

        /// <summary>内容调色板（索引即单位/阵营语义色，顺序为语义契约）。</summary>
        public static readonly Color[] ContentPalette = new Color[]
        {
            new Color(0.78f, 0.72f, 0.48f, 1f),
            new Color(0.66f, 0.76f, 0.34f, 1f),
            new Color(0.48f, 0.68f, 0.32f, 1f),
            new Color(0.33f, 0.52f, 0.28f, 1f),
            new Color(0.3f, 0.58f, 0.52f, 1f),
            new Color(0.35f, 0.62f, 0.64f, 1f),
            new Color(0.2f, 0.42f, 0.38f, 1f),
            new Color(0.42f, 0.62f, 0.8f, 1f),
            new Color(0.35f, 0.48f, 0.66f, 1f),
            new Color(0.95f, 0.68f, 0.25f, 1f),
            new Color(0.62f, 0.44f, 0.26f, 1f),
            new Color(0.45f, 0.3f, 0.18f, 1f),
            new Color(0.8f, 0.68f, 0.42f, 1f),
            new Color(0.52f, 0.42f, 0.3f, 1f),
            new Color(0.62f, 0.52f, 0.4f, 1f),
            new Color(0.62f, 0.62f, 0.58f, 1f),
            new Color(0.44f, 0.45f, 0.47f, 1f),
            new Color(0.66f, 0.36f, 0.3f, 1f),
            new Color(0.52f, 0.27f, 0.25f, 1f),
            new Color(0.48f, 0.38f, 0.52f, 1f),
        };

        /// <summary>调色板名 → ContentPalette 索引。</summary>
        public static readonly Dictionary<string, int> ContentPaletteNameToIndex = new Dictionary<string, int>
        {
            ["amber"] = 9,
            ["blood_earth"] = 18,
            ["brick"] = 17,
            ["clay"] = 14,
            ["dusk_blue"] = 8,
            ["earth"] = 13,
            ["forest"] = 3,
            ["grape"] = 19,
            ["grass"] = 2,
            ["iron"] = 16,
            ["lake"] = 5,
            ["meadow"] = 1,
            ["pine"] = 6,
            ["sand"] = 12,
            ["sky_blue"] = 7,
            ["stone"] = 15,
            ["teal_tree"] = 4,
            ["umber"] = 11,
            ["wheat"] = 0,
            ["wood"] = 10,
        };

        /// <summary>ContentPalette 索引 → 调色板名（与数组顺序对齐）。</summary>
        public static readonly string[] ContentPaletteNames = new string[]
        {
            "wheat",
            "meadow",
            "grass",
            "forest",
            "teal_tree",
            "lake",
            "pine",
            "sky_blue",
            "dusk_blue",
            "amber",
            "wood",
            "umber",
            "sand",
            "earth",
            "clay",
            "stone",
            "iron",
            "brick",
            "blood_earth",
            "grape",
        };

        // ---------------- 字号（font_sizes） ----------------
        public const float FONT_BANNER = 44f;
        public const float FONT_BODY = 15f;
        public const float FONT_DISPLAY = 36f;
        public const float FONT_HINT = 12f;
        public const float FONT_HUD = 17f;
        public const float FONT_SECTION = 14f;
        public const float FONT_TINY = 11f;
        public const float FONT_TITLE = 24f;

        // ---------------- 形状（shape） ----------------
        public const float BORDER_W = 1f;
        public const float PAD_X = 12f;
        public const float PAD_Y = 6f;
        public const float RADIUS = 3f;
        public const float RADIUS_PANEL = 6f;

        // ---------------- 控件尺寸（control_sizes） ----------------
        public const float BTN_H = 32f;
        public const float BTN_H_LG = 44f;
        public const float BTN_H_SM = 26f;
        public const float ROW_H = 36f;

        // ---------------- 布局（layout） ----------------
        public const float MODAL_MARGIN = 48f;
        public const float SCREEN_MARGIN = 12f;

        // ---------------- 动效时长（motion） ----------------
        public const float T_FADE = 0.12f;
        public const float T_PANEL = 0.18f;
        public const float T_TOAST = 3f;

        // ---------------- 字体（fonts；hand_path 为 Godot 路径不生成） ----------------
        public const string FontPrimary = "StickHand-Regular.ttf";
        public const float FontEmbolden = 0.6f;
        /// <summary>已同步到 Resources/UI/StickWorld/fonts/ 的字体文件名（保序）。</summary>
        public static readonly string[] FontFiles = new string[]
        {
            "LXGWWenKaiLite-Medium.ttf",
            "LXGWWenKaiLite-Regular.ttf",
            "StickHand-Regular.ttf",
            "ZCOOLKuaiLe-Regular.ttf",
        };

        // ---------------- sketch（手绘九宫格） ----------------
        /// <summary>全部槽位名，对应 Resources/UI/StickWorld/sketch/&lt;槽位&gt;_f&lt;帧&gt;.png
        /// （拼接器见手写伴生文件 StickTokens.SketchSlotFrame）。</summary>
        public static readonly string[] SketchSlots = new string[]
        {
            "panel",
            "panel_light",
            "groove",
            "groove_focus",
            "btn_normal",
            "btn_hover",
            "btn_pressed",
            "btn_disabled",
            "accent_normal",
            "accent_hover",
            "accent_pressed",
            "btn_primary_normal",
            "btn_primary_hover",
            "btn_primary_pressed",
            "btn_primary_disabled",
            "btn_ink_normal",
            "btn_ink_hover",
            "btn_ink_pressed",
            "btn_ink_disabled",
            "danger_normal",
            "danger_hover",
            "tab_selected",
            "tab_hover",
            "progress_bg",
            "progress_fill",
            "sep_h",
            "sep_v",
        };
        public const float SketchBoilFps = 7.5f;
        public const int SketchFrames = 3;
        public const int SketchIconInlineMaxWidth = 18;
        public const int SketchNinePatchMargin = 10;
        public const int SketchPanelPadX = 16;
        public const int SketchPanelPadY = 12;
        public const int SketchSlotCount = 27;

        // ---------------- sketch.button_variants ----------------
        /// <summary>按钮变体种类。</summary>
        public enum SketchButtonKind
        {
            Dark,
            Accent,
            Primary,
            Danger,
            Paper,
            IconSquare,
        }

        /// <summary>按钮变体令牌：五态字色（normal/hover/pressed/disabled/focus）+
        /// 四态槽位 + 描边 + 假粗体 + 图标模式。token 名与 rgba 并存处取 rgba 值；
        /// FakeBold 由 JSON 布尔映射为 1f/0f。</summary>
        public struct SketchButtonVariant
        {
            public string SlotBase;
            /// <summary>normal/hover/pressed/disabled 顺序的九宫格槽位名（自绘变体为空串占位）。</summary>
            public string[] SlotsNormalHoverPressedDisabled;
            public Color TextNormal;
            public Color TextHover;
            public Color TextPressed;
            public Color TextDisabled;
            public Color TextFocus;
            public float OutlinePx;
            public Color OutlineColor;
            public float FakeBold;
            public string IconMode;
            public float BgAlpha;
        }

        /// <summary>变体令牌总表（键为枚举，值已按 JSON 展平为 rgba）。</summary>
        public static readonly Dictionary<SketchButtonKind, SketchButtonVariant> ButtonVariants =
            new Dictionary<SketchButtonKind, SketchButtonVariant>
        {
            [SketchButtonKind.Dark] = new SketchButtonVariant
            {
                SlotBase = "btn",
                SlotsNormalHoverPressedDisabled = new string[] { "btn_normal", "btn_hover", "btn_pressed", "btn_disabled" },
                TextNormal = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextHover = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextPressed = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextDisabled = new Color(0.93f, 0.94f, 0.96f, 0.25f),
                TextFocus = new Color(0.93f, 0.94f, 0.96f, 1f),
                OutlinePx = 3f,
                OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f),
                FakeBold = 0f,
                IconMode = "BADGE_LEFT",
                BgAlpha = 1f,
            },
            [SketchButtonKind.Accent] = new SketchButtonVariant
            {
                SlotBase = "accent",
                SlotsNormalHoverPressedDisabled = new string[] { "accent_normal", "accent_hover", "accent_pressed", "accent_disabled" },
                TextNormal = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextHover = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextPressed = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextDisabled = new Color(0.93f, 0.94f, 0.96f, 0.25f),
                TextFocus = new Color(0.93f, 0.94f, 0.96f, 1f),
                OutlinePx = 0f,
                OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f),
                FakeBold = 1f,
                IconMode = "BADGE_LEFT",
                BgAlpha = 1f,
            },
            [SketchButtonKind.Primary] = new SketchButtonVariant
            {
                SlotBase = "btn_primary",
                SlotsNormalHoverPressedDisabled = new string[] { "btn_primary_normal", "btn_primary_hover", "btn_primary_pressed", "btn_primary_disabled" },
                TextNormal = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextHover = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextPressed = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextDisabled = new Color(0.93f, 0.94f, 0.96f, 0.25f),
                TextFocus = new Color(0.1f, 0.08f, 0.04f, 1f),
                OutlinePx = 0f,
                OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f),
                FakeBold = 1f,
                IconMode = "BADGE_LEFT",
                BgAlpha = 1f,
            },
            [SketchButtonKind.Danger] = new SketchButtonVariant
            {
                SlotBase = "danger",
                SlotsNormalHoverPressedDisabled = new string[] { "danger_normal", "danger_hover", "danger_pressed", "danger_disabled" },
                TextNormal = new Color(0.9f, 0.34f, 0.3f, 1f),
                TextHover = new Color(0.915f, 0.439f, 0.405f, 1f),
                TextPressed = new Color(0.93f, 0.538f, 0.51f, 1f),
                TextDisabled = new Color(0.93f, 0.94f, 0.96f, 0.25f),
                TextFocus = new Color(0.9f, 0.34f, 0.3f, 1f),
                OutlinePx = 3f,
                OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f),
                FakeBold = 0f,
                IconMode = "BADGE_LEFT",
                BgAlpha = 1f,
            },
            [SketchButtonKind.Paper] = new SketchButtonVariant
            {
                SlotBase = "btn_ink",
                SlotsNormalHoverPressedDisabled = new string[] { "btn_ink_normal", "btn_ink_hover", "btn_ink_pressed", "btn_ink_disabled" },
                TextNormal = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextHover = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextPressed = new Color(0.1f, 0.08f, 0.04f, 1f),
                TextDisabled = new Color(0.1f, 0.08f, 0.04f, 0.45f),
                TextFocus = new Color(0.1f, 0.08f, 0.04f, 1f),
                OutlinePx = 0f,
                OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f),
                FakeBold = 0f,
                IconMode = "BADGE_LEFT",
                BgAlpha = 1f,
            },
            [SketchButtonKind.IconSquare] = new SketchButtonVariant
            {
                SlotBase = "",
                SlotsNormalHoverPressedDisabled = new string[] { "", "", "", "" },
                TextNormal = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextHover = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextPressed = new Color(0.93f, 0.94f, 0.96f, 1f),
                TextDisabled = new Color(0.93f, 0.94f, 0.96f, 0.25f),
                TextFocus = new Color(0.93f, 0.94f, 0.96f, 1f),
                OutlinePx = 0f,
                OutlineColor = new Color(0.05f, 0.04f, 0.03f, 1f),
                FakeBold = 0f,
                IconMode = "CENTER",
                BgAlpha = 1f,
            },
        };

        // ---------------- icons（母题 → Resources 路径，供 Resources.Load<Sprite>） ----------------
        public static readonly Dictionary<string, string> IconMotifs = new Dictionary<string, string>
        {
            ["两本书"] = "UI/StickWorld/icons/两本书_64",
            ["传送门"] = "UI/StickWorld/icons/传送门_64",
            ["信封"] = "UI/StickWorld/icons/信封_64",
            ["医疗箱"] = "UI/StickWorld/icons/医疗箱_64",
            ["十字镐"] = "UI/StickWorld/icons/十字镐_64",
            ["印章"] = "UI/StickWorld/icons/印章_64",
            ["卷地图"] = "UI/StickWorld/icons/卷地图_64",
            ["卷轴"] = "UI/StickWorld/icons/卷轴_64",
            ["哨子"] = "UI/StickWorld/icons/哨子_64",
            ["团子串"] = "UI/StickWorld/icons/团子串_64",
            ["圆柱"] = "UI/StickWorld/icons/圆柱_64",
            ["圆环"] = "UI/StickWorld/icons/圆环_64",
            ["圆盾"] = "UI/StickWorld/icons/圆盾_64",
            ["圆锥"] = "UI/StickWorld/icons/圆锥_64",
            ["天平"] = "UI/StickWorld/icons/天平_64",
            ["头盔"] = "UI/StickWorld/icons/头盔_64",
            ["奖章"] = "UI/StickWorld/icons/奖章_64",
            ["小木船"] = "UI/StickWorld/icons/小木船_64",
            ["小炮"] = "UI/StickWorld/icons/小炮_64",
            ["小鱼干"] = "UI/StickWorld/icons/小鱼干_64",
            ["布头巾"] = "UI/StickWorld/icons/布头巾_64",
            ["布腿带"] = "UI/StickWorld/icons/布腿带_64",
            ["布衣"] = "UI/StickWorld/icons/布衣_64",
            ["帐篷"] = "UI/StickWorld/icons/帐篷_64",
            ["弓箭"] = "UI/StickWorld/icons/弓箭_64",
            ["弹弓"] = "UI/StickWorld/icons/弹弓_64",
            ["心形气球"] = "UI/StickWorld/icons/心形气球_64",
            ["战鼓"] = "UI/StickWorld/icons/战鼓_64",
            ["房屋"] = "UI/StickWorld/icons/房屋_64",
            ["手推车"] = "UI/StickWorld/icons/手推车_64",
            ["按按钮小手"] = "UI/StickWorld/icons/按按钮小手_64",
            ["提灯"] = "UI/StickWorld/icons/提灯_64",
            ["放大镜"] = "UI/StickWorld/icons/放大镜_64",
            ["斧头"] = "UI/StickWorld/icons/斧头_64",
            ["旗帜"] = "UI/StickWorld/icons/旗帜_64",
            ["星章"] = "UI/StickWorld/icons/星章_64",
            ["望远镜"] = "UI/StickWorld/icons/望远镜_64",
            ["木桶"] = "UI/StickWorld/icons/木桶_64",
            ["木锯"] = "UI/StickWorld/icons/木锯_64",
            ["木门"] = "UI/StickWorld/icons/木门_64",
            ["板条箱"] = "UI/StickWorld/icons/板条箱_64",
            ["橄榄枝"] = "UI/StickWorld/icons/橄榄枝_64",
            ["正球"] = "UI/StickWorld/icons/正球_64",
            ["水壶"] = "UI/StickWorld/icons/水壶_64",
            ["沙漏"] = "UI/StickWorld/icons/沙漏_64",
            ["法杖"] = "UI/StickWorld/icons/法杖_64",
            ["火柴人"] = "UI/StickWorld/icons/火柴人_64",
            ["爱心"] = "UI/StickWorld/icons/爱心_64",
            ["狗尾巴草"] = "UI/StickWorld/icons/狗尾巴草_64",
            ["狗骨头"] = "UI/StickWorld/icons/狗骨头_64",
            ["王冠"] = "UI/StickWorld/icons/王冠_64",
            ["皮护胫"] = "UI/StickWorld/icons/皮护胫_64",
            ["皮甲"] = "UI/StickWorld/icons/皮甲_64",
            ["皮盔"] = "UI/StickWorld/icons/皮盔_64",
            ["瞭望塔"] = "UI/StickWorld/icons/瞭望塔_64",
            ["短剑"] = "UI/StickWorld/icons/短剑_64",
            ["石料"] = "UI/StickWorld/icons/石料_64",
            ["矿石"] = "UI/StickWorld/icons/矿石_64",
            ["科技树"] = "UI/StickWorld/icons/科技树_64",
            ["立方体"] = "UI/StickWorld/icons/立方体_64",
            ["篝火"] = "UI/StickWorld/icons/篝火_64",
            ["绷带"] = "UI/StickWorld/icons/绷带_64",
            ["罗盘"] = "UI/StickWorld/icons/罗盘_64",
            ["羽毛笔"] = "UI/StickWorld/icons/羽毛笔_64",
            ["背包"] = "UI/StickWorld/icons/背包_64",
            ["苹果"] = "UI/StickWorld/icons/苹果_64",
            ["茶杯"] = "UI/StickWorld/icons/茶杯_64",
            ["药瓶"] = "UI/StickWorld/icons/药瓶_64",
            ["试管架"] = "UI/StickWorld/icons/试管架_64",
            ["账本"] = "UI/StickWorld/icons/账本_64",
            ["货运板车"] = "UI/StickWorld/icons/货运板车_64",
            ["路牌"] = "UI/StickWorld/icons/路牌_64",
            ["金币"] = "UI/StickWorld/icons/金币_64",
            ["金砂"] = "UI/StickWorld/icons/金砂_64",
            ["钥匙串"] = "UI/StickWorld/icons/钥匙串_64",
            ["钱袋"] = "UI/StickWorld/icons/钱袋_64",
            ["钻石"] = "UI/StickWorld/icons/钻石_64",
            ["铁砧"] = "UI/StickWorld/icons/铁砧_64",
            ["铁锹"] = "UI/StickWorld/icons/铁锹_64",
            ["锁子头罩"] = "UI/StickWorld/icons/锁子头罩_64",
            ["锁子护腿"] = "UI/StickWorld/icons/锁子护腿_64",
            ["锁子甲"] = "UI/StickWorld/icons/锁子甲_64",
            ["锚"] = "UI/StickWorld/icons/锚_64",
            ["锥形瓶"] = "UI/StickWorld/icons/锥形瓶_64",
            ["锻造锤"] = "UI/StickWorld/icons/锻造锤_64",
            ["长矛"] = "UI/StickWorld/icons/长矛_64",
            ["面包"] = "UI/StickWorld/icons/面包_64",
            ["饭团"] = "UI/StickWorld/icons/饭团_64",
            ["马蹄铁"] = "UI/StickWorld/icons/马蹄铁_64",
            ["高背椅"] = "UI/StickWorld/icons/高背椅_64",
            ["麦穗"] = "UI/StickWorld/icons/麦穗_64",
            ["麻袋"] = "UI/StickWorld/icons/麻袋_64",
            ["齿轮"] = "UI/StickWorld/icons/齿轮_64",
        };

        /// <summary>语义键 → 母题名（配合 LoadIcon / IconMotifs 使用）。</summary>
        public static readonly Dictionary<string, string> IconSemantic = new Dictionary<string, string>
        {
            ["res_diamond"] = "钻石",
            ["res_gold"] = "金币",
            ["res_metal"] = "铁砧",
            ["res_metal_ore"] = "铁砧",
            ["res_stone"] = "石料",
            ["res_wood"] = "板条箱",
        };
    }
}
