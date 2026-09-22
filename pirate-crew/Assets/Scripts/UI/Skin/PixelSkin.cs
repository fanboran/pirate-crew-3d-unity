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
    /// 【几何口径】u（基本单位）= 3 屏幕像素 = **1 个 3D 像素块**（640×360 RT 最近邻放大回
    /// 1080p 的块大小）——UI 颗粒度与 3D 渲染 1:1 对齐（创始人 2026-09-22 要求）。改 RT 档
    /// 必须同步改 <see cref="Unit"/>；Editor 侧 <c>BeveledPixelSpriteBuilder</c> 的判据
    /// 会读 URP 渲染器资产里的 renderHeightPixels 反向锁这条。
    /// 装配侧尺寸纪律：可见包边件的 width/height 取 <see cref="Unit"/> 的整数倍，
    /// anchoredPosition 至少取整——分数像素会让 3px 的带糊成 4px。
    /// </summary>
    public static class PixelSkin
    {
        /// <summary>tone 数（与调色板侧 tone 表同长，改一处必须同步烘焙器）。</summary>
        public const int ToneCount = 7;

        /// <summary>填充色数。</summary>
        public const int FillCount = 5;

        /// <summary>
        /// 基本单位（UI 像素 = 屏幕像素）：外环/斜面/内暗线各 1u 厚。**= 3 = 1080p 下
        /// 一个 3D 像素块（1080 ÷ RT 高 360）**。
        /// </summary>
        public const int Unit = 3;

        /// <summary>Plate/Track 九宫格切片边框 = 3 层带 + 1u 内容余量。</summary>
        public const int PlateBorder = 4 * Unit;

        /// <summary>低于它装配就不能用九宫格（角会切进内容区），装配侧应断言。</summary>
        public const int PlateMinRender = 2 * PlateBorder;

        /// <summary>按压态元素位移：右下 1px（贴图里不烘位移，烘了九宫格切片错位）。</summary>
        public static readonly Vector2 PressOffset = new Vector2(1f, -1f);

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
        /// 某 tone 底上的正文字色：中档亮度高于阈值 = 浅底 → 墨字；否则 → 本 tone 亮档字。
        /// 与烘焙器的"光来自左上"同一套感知口径（简单 sRGB 亮度，够用且可复算）。
        /// </summary>
        public static Color32 TextColorOn(PixelTone tone)
        {
            Color32 mid = MidOf(tone);
            float lum = (0.2126f * mid.r + 0.7152f * mid.g + 0.0722f * mid.b);
            return lum > 140f ? Ink : LightOf(tone);
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
