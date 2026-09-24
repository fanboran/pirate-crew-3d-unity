// 历史生成物:原由 tools/ui_sync/sync_to_unity.py 从 stick-world ui_tokens.json 生成。
// 生成器与真相源均已不存在,本文件自清理波起转为手工维护的遗留层——
// 仅保留仍有活引用的成员,失活令牌(字号表/形状/槽位/图标映射等)已删,勿再引用本层新起炉灶。

using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>stick-world UI 令牌遗留层（颜色/按钮变体）。
    /// 像素皮切换后仅剩装配器文字色与 SketchButton 变体表仍在消费；
    /// 待重构波将样式源收编到单一来源后整体退役。</summary>
    public static partial class StickTokens
    {
        // ---------------- 颜色（colors） ----------------
        public static readonly Color ACCENT = new Color(0.95f, 0.68f, 0.25f, 1f);
        public static readonly Color GROOVE_BG = new Color(0f, 0f, 0f, 0.45f);
        public static readonly Color INFO = new Color(0.55f, 0.78f, 1f, 1f);
        public static readonly Color INK = new Color(0.05f, 0.04f, 0.03f, 1f);
        public static readonly Color MODAL_DIM = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color SUCCESS = new Color(0.45f, 0.8f, 0.48f, 1f);
        public static readonly Color TEXT = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color TEXT_DIM = new Color(0.93f, 0.94f, 0.96f, 0.55f);
        public static readonly Color TEXT_FAINT = new Color(0.93f, 0.94f, 0.96f, 0.32f);
        public static readonly Color WARN = new Color(0.98f, 0.82f, 0.3f, 1f);
        public static readonly Color WINDOW_BG = new Color(0.012f, 0.014f, 0.02f, 0.88f);
        public static readonly Color WINDOW_BG_LIGHT = new Color(0.02f, 0.024f, 0.034f, 0.72f);

        // ---------------- 布局（layout） ----------------
        public const float PAD_X = 12f;
        public const float SCREEN_MARGIN = 12f;

        // ---------------- 字体（fonts） ----------------
        public const float FontEmbolden = 0.6f;

        // ---------------- sketch（手绘九宫格） ----------------
        public const int SketchPanelPadX = 16;
        public const int SketchPanelPadY = 12;

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
    }
}
