using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 设计 Token（配色 + 字号 + 间距 + 圆角）。
    ///
    /// 【出处】docs/UI-UX与中文本地化规范.md §1.3 配色 Token 表、§1.4 字号表、§1.7 形状/间距。
    /// 【为什么集中定义】规范要求「禁止散落魔法色值」——所有 UI 代码只许引用本类，
    /// 调色板一律以 docs/美术风格指南.md §2.3 UI 调色板为准（暖金棕：木板 / 羊皮纸 / 黄铜）。
    /// 【可测性】本类只含 <see cref="Color"/> 常量与整数常量，不触碰 GameObject，可在无头验证台断言。
    /// </summary>
    public static class UiTheme
    {
        // ------------------------------------------------------------------
        // 配色 Token（§1.3；色值出处见规范表内「出处」列）
        // ------------------------------------------------------------------

        /// <summary>正文底、名册行底（沙地亮阶）。</summary>
        public static readonly Color Parchment = Rgb(0xE8D5A3);

        /// <summary>面板外框、深底（木材暗阶）。</summary>
        public static readonly Color WoodDark = Rgb(0x6B4C28);

        /// <summary>按钮正常态底（木材中阶）。</summary>
        public static readonly Color WoodMid = Rgb(0xA67B42);

        /// <summary>按钮 hover 底 / 亮木条（木材亮阶）。</summary>
        public static readonly Color WoodLight = Rgb(0xD4A76A);

        /// <summary>金描边、分隔线、星级。</summary>
        public static readonly Color Brass = Rgb(0xC9A227);

        /// <summary>当前回合 / 关键数值高亮。</summary>
        public static readonly Color BrassLight = Rgb(0xF2D06B);

        /// <summary>羊皮纸上的正文（场景描边同源深灰）。</summary>
        public static readonly Color Ink = Rgb(0x2A2A2A);

        /// <summary>深底上的正文。</summary>
        public static readonly Color TextLight = Rgb(0xF5E8C8);

        /// <summary>红队标识 / 血条（逆向 §8.1）。</summary>
        public static readonly Color TeamRed = Rgb(0xFF3A29);

        /// <summary>蓝队标识 / 血条（逆向 §8.1）。</summary>
        public static readonly Color TeamBlue = Rgb(0x3366FF);

        /// <summary>危险态（不可投掷 / 落水）。</summary>
        public static readonly Color Danger = Rgb(0xCC2222);

        /// <summary>破坏性操作按钮底（退出游戏 / 放弃本局）。</summary>
        public static readonly Color DangerDark = Rgb(0x8A1F1F);

        /// <summary>选中描边（UI 与 3D 同源；同屏只出现一处，§6.3）。</summary>
        public static readonly Color Select = Rgb(0x49D9D6);

        /// <summary>成功 / 已通关。</summary>
        public static readonly Color Success = Rgb(0x4A8C4A);

        /// <summary>装饰性海面 / 进度槽。</summary>
        public static readonly Color Sea = Rgb(0x2B7AB8);

        /// <summary>面板底（木板深棕，美术风指南 §2.3；不透明度 0.92）。</summary>
        public static readonly Color PanelWood = new Color(0x3A / 255f, 0x2A / 255f, 0x1E / 255f, 0.92f);

        /// <summary>禁用态整体透明度（§1.3）。</summary>
        public const float DisabledAlpha = 0.55f;

        /// <summary>不可用文字透明度。</summary>
        public const float DisabledTextAlpha = 0.4f;

        // ------------------------------------------------------------------
        // 字号 Token（§1.4，1080p 基准 px；正文下限 20 为硬约束）
        // ------------------------------------------------------------------

        /// <summary>主菜单游戏名。</summary>
        public const int FontDisplay = 64;

        /// <summary>结算横幅「胜利 / 失败」。</summary>
        public const int FontBanner = 48;

        /// <summary>界面标题。</summary>
        public const int FontTitle = 36;

        /// <summary>区块小标题。</summary>
        public const int FontSection = 24;

        /// <summary>HUD 常读信息（回合、存活、武器名）。</summary>
        public const int FontHud = 24;

        /// <summary>正文 / 按钮 / 行文本（下限）。</summary>
        public const int FontBody = 20;

        /// <summary>辅助提示、错误说明。</summary>
        public const int FontHint = 18;

        /// <summary>角标、页码、占位符。</summary>
        public const int FontTiny = 16;

        // ------------------------------------------------------------------
        // 间距 / 形状（§1.7；间距只取 4/8/12/16/24 五档）
        // ------------------------------------------------------------------

        /// <summary>外安全边距（角落 HUD 件离屏边至少此值）。</summary>
        public const float Safe = 24f;

        /// <summary>面板内边距。</summary>
        public const float PanelPadding = 16f;

        /// <summary>行高基准（沿用既有 <c>CrewManagementController.RowHeight</c>）。</summary>
        public const float RowHeight = 44f;

        /// <summary>面板圆角。</summary>
        public const float RadiusPanel = 8f;

        /// <summary>按钮圆角。</summary>
        public const float RadiusButton = 4f;

        /// <summary>UI 内描边宽度。</summary>
        public const float StrokeInner = 2f;

        /// <summary>外框描边宽度。</summary>
        public const float StrokeOuter = 4f;

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        /// <summary>0xRRGGBB → 不透明 <see cref="Color"/>。</summary>
        public static Color Rgb(int hex)
        {
            return new Color(
                ((hex >> 16) & 0xFF) / 255f,
                ((hex >> 8) & 0xFF) / 255f,
                (hex & 0xFF) / 255f,
                1f);
        }

        /// <summary>把颜色整体乘上 alpha（保留原 RGB）。</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>按钮 hover 提亮（§6.1：提亮 8%）。</summary>
        public static Color Hover(Color baseColor)
        {
            return new Color(
                Mathf.Min(1f, baseColor.r * 1.08f),
                Mathf.Min(1f, baseColor.g * 1.08f),
                Mathf.Min(1f, baseColor.b * 1.08f),
                baseColor.a);
        }

        /// <summary>按钮 pressed 压暗（§6.1：压暗 10%）。</summary>
        public static Color Pressed(Color baseColor)
        {
            return new Color(baseColor.r * 0.9f, baseColor.g * 0.9f, baseColor.b * 0.9f, baseColor.a);
        }

        /// <summary>禁用态去饱和 + 55% 透明（§6.1）。</summary>
        public static Color Disabled(Color baseColor)
        {
            float gray = baseColor.r * 0.299f + baseColor.g * 0.587f + baseColor.b * 0.114f;
            return new Color(
                Mathf.Lerp(baseColor.r, gray, 0.75f),
                Mathf.Lerp(baseColor.g, gray, 0.75f),
                Mathf.Lerp(baseColor.b, gray, 0.75f),
                baseColor.a * DisabledAlpha);
        }

        /// <summary>队伍色：teamIndex 0 = 红队，其余 = 蓝队。</summary>
        public static Color TeamColor(int teamIndex)
        {
            return teamIndex == 0 ? TeamRed : TeamBlue;
        }

        /// <summary>队伍中文名：teamNumber 1 = 红队，2 = 蓝队。</summary>
        public static string TeamName(int teamNumber)
        {
            return teamNumber == 2 ? UiStrings.TeamBlue : UiStrings.TeamRed;
        }
    }
}
