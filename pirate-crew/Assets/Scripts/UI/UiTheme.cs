using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// UI 设计 Token（配色 + 字号 + 间距 + 圆角）。
    ///
    /// 【出处】docs/UI-UX与中文本地化规范.md §1.3 配色 Token 表、§1.4 字号表、§1.7 形状/间距。
    /// 【为什么集中定义】规范要求「禁止散落魔法色值」——所有 UI 代码只许引用本类，
    /// 调色板一律以 docs/美术风格指南.md §2.3 UI 调色板为准（暖金棕：木板 / 羊皮纸 / 黄铜）。
    /// 【换装（Beveled Pixel）】木/纸/铜族色值已改经 <see cref="PixelSkin"/> 的 tone 取档
    /// （见「配色 Token」段落说明）；成员名与语义不变，调用点零改动。玩法语义色
    /// （队色 / 成功）不迁移。
    /// 【可测性】本类只含 <see cref="Color"/> 常量与整数常量，不触碰 GameObject，可在无头验证台断言。
    /// </summary>
    public static class UiTheme
    {
        // ------------------------------------------------------------------
        // 配色 Token（§1.3；色值出处见规范表内「出处」列）
        //
        // 【换装（Beveled Pixel）】木/纸/铜旧皮的字面色值不再写死：一律经 <see cref="PixelSkin"/>
        // 的同族 tone 取档（同一色调的亮/中/暗阶梯由调色板派生），**语义名与成员名保持不变**
        // （`UiTheme.Parchment` 等调用点零改动）。像素图集缺失（未烘焙）时回落原字面值，
        // 绝不出现洋红占位。
        //
        // 【为什么是属性而不是 static readonly 字段】PixelSkin 首次取用会走 Resources.Load，
        // 不能在类型初始化（静态字段）阶段就跑——无头验证台 / 编辑器装配都可能先摸到本类。
        // 属性按需求值，且像素图集一旦缺失只告警一次（PixelSkin 内部缓存）。
        // ------------------------------------------------------------------

        /// <summary>正文底、名册行底（浅档 tone = Light 的中档）。</summary>
        public static Color Parchment => PixelOr(PixelTone.Light, 1, Rgb(0xE8D5A3));

        /// <summary>面板外框、深底（Frame tone 的暗档）。</summary>
        public static Color WoodDark => PixelOr(PixelTone.Frame, 2, Rgb(0x6B4C28));

        /// <summary>按钮正常态底（Dense tone 的中档）。</summary>
        public static Color WoodMid => PixelOr(PixelTone.Dense, 1, Rgb(0xA67B42));

        /// <summary>按钮 hover 底 / 亮木条（Dense tone 的亮档）。</summary>
        public static Color WoodLight => PixelOr(PixelTone.Dense, 0, Rgb(0xD4A76A));

        /// <summary>金描边、分隔线、星级（Primary tone 的中档）。</summary>
        public static Color Brass => PixelOr(PixelTone.Primary, 1, Rgb(0xC9A227));

        /// <summary>当前回合 / 关键数值高亮（Primary tone 的亮档）。</summary>
        public static Color BrassLight => PixelOr(PixelTone.Primary, 0, Rgb(0xF2D06B));

        /// <summary>深/浅底上统一的正文墨色（像素皮单一墨色令牌）。</summary>
        public static Color Ink => PixelSkin.Asset != null ? (Color)PixelSkin.Ink : Rgb(0x2A2A2A);

        /// <summary>深底上的正文（像素皮暖白）。</summary>
        public static Color TextLight => PixelSkin.Asset != null ? (Color)PixelSkin.PaperWhite : Rgb(0xF5E8C8);

        /// <summary>红队标识 / 血条（逆向 §8.1；玩法语义色，不随换装迁移）。</summary>
        public static readonly Color TeamRed = Rgb(0xFF3A29);

        /// <summary>蓝队标识 / 血条（逆向 §8.1；玩法语义色，不随换装迁移）。</summary>
        public static readonly Color TeamBlue = Rgb(0x3366FF);

        /// <summary>危险态（不可投掷 / 落水；Danger tone 的中档）。</summary>
        public static Color Danger => PixelOr(PixelTone.Danger, 1, Rgb(0xCC2222));

        /// <summary>破坏性操作按钮底（退出游戏 / 放弃本局；Danger tone 的暗档）。</summary>
        public static Color DangerDark => PixelOr(PixelTone.Danger, 2, Rgb(0x8A1F1F));

        /// <summary>选中描边（UI 与 3D 同源；同屏只出现一处，§6.3）。取 Sea tone 亮档（青系）。</summary>
        public static Color Select => PixelOr(PixelTone.Sea, 0, Rgb(0x49D9D6));

        /// <summary>成功 / 已通关（玩法语义色，不随换装迁移）。</summary>
        public static readonly Color Success = Rgb(0x4A8C4A);

        /// <summary>装饰性海面 / 进度槽（Sea tone 的中档）。</summary>
        public static Color Sea => PixelOr(PixelTone.Sea, 1, Rgb(0x2B7AB8));

        /// <summary>面板底（Frame tone 的中档，保留 0.92 不透明度语义）。</summary>
        public static Color PanelWood => WithAlpha(PixelOr(PixelTone.Frame, 1, Rgb(0x3A2A1E)), 0.92f);

        /// <summary>像素图集可用 → 取 tone 的第 shade 档（0=亮 / 1=中 / 2=暗）；否则回落旧字面值。</summary>
        static Color PixelOr(PixelTone tone, int shade, Color fallback)
        {
            if (PixelSkin.Asset == null)
                return fallback;
            switch (shade)
            {
                case 0: return PixelSkin.LightOf(tone);
                case 2: return PixelSkin.DarkOf(tone);
                default: return PixelSkin.MidOf(tone);
            }
        }

        /// <summary>禁用态整体透明度（§1.3）。</summary>
        public const float DisabledAlpha = 0.55f;

        /// <summary>不可用文字透明度。</summary>
        public const float DisabledTextAlpha = 0.4f;

        // ------------------------------------------------------------------
        // 字号 Token（§1.4，1080p 基准 px）
        //
        // 【双轨口径（UI 审计 P2-6 收口）】全项目字号有两套，裁决后的基准是
        //   <c>MenuUiBuilder.FontScale</c>（用户裁决「整体下调一档」：Hud 18 / Body 15 / Tiny 13），
        //   HUD 与全部新装配一律走它；本段旧档常量是 M3 菜单场景的既有取值，暂不迁移
        //   （SceneSetup / M3SceneSetup 等构建器还在传这些数字）。两套并不冲突：
        //   旧档数字经 <c>MenuUiBuilder.CreateText</c> 的 ScaleLegacyFont 映射后，
        //   **渲染出来的同样是 FontScale 新档**——差异只在调用入口，不在最终字号。
        //   规范 §1.4 旧的「正文 ≥20 硬约束」已按同一裁决改为「正文 ≥16、辅助 ≥14、角标 ≥12」；
        //   新代码请直接引用 MenuUiBuilder.FontScale（Editor 侧）或按其档位取值，勿再新增本段引用。
        // ------------------------------------------------------------------

        /// <summary>主菜单游戏名（渲染落 FontScale.Display 48）。</summary>
        public const int FontDisplay = 64;

        /// <summary>结算横幅「胜利 / 失败」（渲染落 FontScale.Banner 36）。</summary>
        public const int FontBanner = 48;

        /// <summary>界面标题（渲染落 FontScale.Title 26）。</summary>
        public const int FontTitle = 36;

        /// <summary>区块小标题（渲染落 FontScale.Section 20）。</summary>
        public const int FontSection = 24;

        /// <summary>HUD 常读信息（回合、存活、武器名；渲染落 FontScale.Hud 18）。</summary>
        public const int FontHud = 24;

        /// <summary>正文 / 按钮 / 行文本（渲染落 FontScale.Body 15）。</summary>
        public const int FontBody = 20;

        /// <summary>辅助提示、错误说明（渲染落 FontScale.Hint 14）。</summary>
        public const int FontHint = 18;

        /// <summary>角标、页码、占位符（渲染落 FontScale.Tiny 13）。</summary>
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
