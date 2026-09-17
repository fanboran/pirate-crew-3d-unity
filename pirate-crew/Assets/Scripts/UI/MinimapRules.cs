using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗小地图的换算与表现规则（纯 C# 静态类，不引用 MonoBehaviour，可在无头验证台断言）。
    ///
    /// 【原版依据】`docs/参考游戏逆向-海盗军团抢宝藏-静态.md`
    ///   · §2.4 / §8.1：<c>Map.as</c>（117 行）＝ 小地图，<c>dotSize=3</c> 的点阵；
    ///       实心瓦片 alpha 50 / 空瓦片 alpha 20；宝箱黄色 <c>0xFFFF00</c>；
    ///       红队 <c>0xFF3A29</c>、蓝队 <c>0x3366FF</c>，<c>alpha = mapVisibility*100</c>；
    ///       角色死亡后 <c>mapVisibility -= 0.1/帧</c> 淡出。
    ///   · §2.3：小地图容器 <c>mapHolder</c> 挂在 (20,20)（= 屏幕左上角，视口 550×400）。
    ///   · §3.1 主循环：每帧调 <c>map.drawActive()</c> 刷新「活动点」（= 活着的单位位置）。
    ///
    /// 【原版没有给出的部分（本实现为**提案/待定**）】
    ///   · 原版是 2D 侧视关卡，小地图直接复用关卡像素布局；本工程把 Flash 的
    ///     (gridX, gridY) 重投影成 XZ 平面（见 <c>docs/M2-3D空间模型对齐.md</c> §1），
    ///     所以「竞技场世界坐标 → 小地图归一化坐标」的映射是本工程自定的：
    ///       u = x / width（右为 +u）；v = 1 - z / depth（+Z 朝相机，落在小地图下方）。
    ///     这样小地图方向与屏幕上方 = 远处（-Z）一致。
    ///   · 原版点阵按瓦片有无绘制（实心/空 alpha 两档）。本工程已落地瓦片地形
    ///     （<c>Battle/Terrain/</c>：Flash 瓦片行 → XZ 抬升块），故按 §8.1 补回两档 alpha 点阵；
    ///     地形未转写的关卡退化为全空点阵（铺基础地面色）。
    /// </summary>
    public static class MinimapRules
    {
        // ------------------------------------------------------------------
        // 原版常量（出处见类头；不要臆改）
        // ------------------------------------------------------------------

        /// <summary>原版小地图点尺寸 `dotSize=3`（§8.1）。</summary>
        public const float FlashDotSizePixels = 3f;

        /// <summary>原版实心瓦片 alpha（0–100 刻度，§8.1）。</summary>
        public const float FlashSolidTileAlpha = 50f;

        /// <summary>原版空瓦片 alpha（0–100 刻度，§8.1）。</summary>
        public const float FlashEmptyTileAlpha = 20f;

        // ------------------------------------------------------------------
        // 瓦片点阵（§8.1 两档 alpha；地形落地后补上）
        // ------------------------------------------------------------------

        /// <summary>
        /// 瓦片点 alpha（0–1）：实心 <c>50/100 = 0.5</c>、空 <c>20/100 = 0.2</c>（§8.1，
        /// 原版 alpha 是 AS2 的 0–100 刻度，故除以 100）。
        /// </summary>
        public static float TileAlpha(bool solid)
        {
            return (solid ? FlashSolidTileAlpha : FlashEmptyTileAlpha) / 100f;
        }

        /// <summary>实心瓦片色（地形块；**提案/待定**：原版点阵未给瓦片色，取中性岩土色）。</summary>
        public static readonly Color SolidTileColor = new Color(0.72f, 0.66f, 0.52f, 1f);

        /// <summary>空瓦片色（基础地面 / 水域背景；**提案/待定**）。</summary>
        public static readonly Color EmptyTileColor = new Color(0.28f, 0.34f, 0.4f, 1f);

        /// <summary>瓦片点颜色：按实心/空取色并套 <see cref="TileAlpha"/>。</summary>
        public static Color TileColor(bool solid)
        {
            Color c = solid ? SolidTileColor : EmptyTileColor;
            c.a = TileAlpha(solid);
            return c;
        }

        /// <summary>
        /// 一颗瓦片点在小地图上的直径（px）。原版 <c>dotSize=3</c> 是「一颗瓦片 = 3px」，
        /// 本工程把瓦片放大到 <paramref name="pixelsPerTile"/>，故点 = 整格铺满（**提案/待定**）。
        /// </summary>
        public static float TileDotSizePixels(float pixelsPerTile)
        {
            return Mathf.Max(1f, pixelsPerTile);
        }

        /// <summary>原版红队色 `0xFF3A29`（§8.1）。</summary>
        public static readonly Color RedTeamColor = new Color(1f, 58f / 255f, 41f / 255f, 1f);

        /// <summary>原版蓝队色 `0x3366FF`（§8.1）。</summary>
        public static readonly Color BlueTeamColor = new Color(51f / 255f, 102f / 255f, 1f, 1f);

        /// <summary>死亡淡出速度：`mapVisibility -= 0.1/帧`（§8.1）。</summary>
        public const float DeadFadePerFrame = 0.1f;

        /// <summary>折算到秒：0.1 × 25fps = 2.5/s（即约 0.4s 淡完）。帧率取自 <see cref="LevelGeometry.FrameRate"/>。</summary>
        public const float DeadFadePerSecond = DeadFadePerFrame * LevelGeometry.FrameRate;

        // ------------------------------------------------------------------
        // 本工程映射（提案/待定，见类头）
        // ------------------------------------------------------------------

        /// <summary>
        /// 竞技场世界位置（XZ）→ 小地图归一化坐标（0–1，UGUI 锚点口径）。
        /// <c>u = x / width</c>，<c>v = 1 - z / depth</c>（+Z 朝相机 → 小地图下方）。
        /// 越界不裁剪，由 <see cref="ClampNormalized"/> 处理。
        /// </summary>
        public static Vector2 ArenaToNormalized(float worldX, float worldZ, float width, float depth)
        {
            float u = width > 1e-6f ? worldX / width : 0f;
            float v = depth > 1e-6f ? 1f - worldZ / depth : 0f;
            return new Vector2(u, v);
        }

        /// <summary>把归一化坐标裁进小地图范围（单位被抛出竞技场时点位会短暂越界）。</summary>
        public static Vector2 ClampNormalized(Vector2 normalized)
        {
            return new Vector2(Mathf.Clamp01(normalized.x), Mathf.Clamp01(normalized.y));
        }

        /// <summary>
        /// 小地图面板像素尺寸：一颗瓦片画 <paramref name="pixelsPerTile"/> 像素。
        /// 某关 <c>widthTiles × depthTiles</c>（1 瓦片 = 1 世界单位）→ 面板保持与竞技场同比例，不拉伸变形。
        /// </summary>
        public static Vector2 PanelSizePixels(int widthTiles, int depthTiles, float pixelsPerTile)
        {
            float ppt = pixelsPerTile > 0f ? pixelsPerTile : 1f;
            return new Vector2(widthTiles * ppt, depthTiles * ppt);
        }

        /// <summary>
        /// 单位点位直径（px）。原版 <c>dotSize=3</c> 是「一颗瓦片 = 3px」口径，
        /// 本实现把瓦片放大到 <paramref name="pixelsPerTile"/>，故点也同比放大并略大于一格以便辨认（**提案/待定**）。
        /// </summary>
        public static float DotSizePixels(float pixelsPerTile)
        {
            return Mathf.Max(4f, pixelsPerTile * 1.25f);
        }

        /// <summary>
        /// 死亡淡出推进一帧：<c>visibility -= 2.5 × dt</c>，夹在 [0,1]。
        /// 对应 §8.1 的 <c>mapVisibility -= 0.1/帧</c>（25fps）。
        /// </summary>
        public static float AdvanceVisibility(float visibility, float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return Mathf.Clamp01(visibility);
            return Mathf.Clamp01(visibility - DeadFadePerSecond * deltaSeconds);
        }

        /// <summary>
        /// 单位点颜色：按队取原版色（§8.1），alpha = <paramref name="visibility"/>
        /// （对应原版 <c>alpha = mapVisibility*100</c>）。
        /// </summary>
        public static Color DotColor(int teamIndex, float visibility)
        {
            Color c = teamIndex == 0 ? RedTeamColor : BlueTeamColor;
            c.a = Mathf.Clamp01(visibility);
            return c;
        }

        /// <summary>小地图底色（原版为瓦片点阵，本工程平坦竞技场用单色底；**提案/待定**）。</summary>
        public static readonly Color BackgroundColor = new Color(0.06f, 0.09f, 0.13f, 0.72f);

        /// <summary>面板边框色（**提案/待定**）。</summary>
        public static readonly Color BorderColor = new Color(0.55f, 0.62f, 0.7f, 0.9f);

        // ------------------------------------------------------------------
        // 俯视海图（M4 世界地图模式；方案 D 裁决：海图是全图级武器的瞄准 UI）
        // ------------------------------------------------------------------

        /// <summary>
        /// 世界系站面 box（旋转矩形）→ 海图归一化矩形（UGUI 锚点口径 Rect(minU, minV, w, h)）。
        /// u/v 口径与 <see cref="ArenaToNormalized"/> 一致（v 翻转：+Z 朝相机 → 图下方）。
        /// 旋转矩形取其水平 AABB（海图是示意图，不逐角绘制）。
        /// </summary>
        public static Rect WorldBoxToChartRect(
            Vector2 center, Vector2 size, float yawDeg, float width, float depth)
        {
            float rad = yawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Abs(Mathf.Cos(rad)), sin = Mathf.Abs(Mathf.Sin(rad));
            float halfX = (cos * size.x + sin * size.y) * 0.5f;
            float halfZ = (sin * size.x + cos * size.y) * 0.5f;

            float uMin = width > 1e-6f ? (center.x - halfX) / width : 0f;
            float uMax = width > 1e-6f ? (center.x + halfX) / width : 0f;
            float vMax = depth > 1e-6f ? 1f - (center.y - halfZ) / depth : 0f;
            float vMin = depth > 1e-6f ? 1f - (center.y + halfZ) / depth : 0f;
            return Rect.MinMaxRect(uMin, vMin, uMax, vMax);
        }

        /// <summary>海图岛层色（站面示意；**提案/待定**：取实心瓦片同系沙色、提高不透明度便于瞄准读图）。</summary>
        public static Color WorldChartIslandColor => new Color(
            SolidTileColor.r, SolidTileColor.g, SolidTileColor.b, 0.85f);
    }
}
