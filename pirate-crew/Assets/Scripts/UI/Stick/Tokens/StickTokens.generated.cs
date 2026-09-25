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

        // ---------------- sketch（手绘九宫格） ----------------
        public const int SketchPanelPadX = 16;
        public const int SketchPanelPadY = 12;

        // ---------------- 布局（layout） ----------------
        public const float PAD_X = 12f;
        public const float SCREEN_MARGIN = 12f;

        // ---------------- 字体（fonts） ----------------
        public const float FontEmbolden = 0.6f;

    }
}
