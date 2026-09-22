using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化路径"真实内容"试点场景**的取景口径（唯一来源）：两个样板关（1 云场 / 3 空岛）
    /// + 八张大海域海图（关卡号 101–108）。
    ///
    /// 【这一族场景在回答什么】<see cref="PixelartPilotScene"/>（图元几何）证明的是"机制对不对"；
    /// 这一族换成**真实关卡内容**，回答的是"这套观感用在真关卡上是什么样"，
    /// 同时给 README 的宣传图提供"同一套口径"的成图。
    ///
    /// 【内容从哪来（不许自己编）】两套装配器各自读**它那一侧的权威数据**，本表只管取景：
    /// <list type="bullet">
    ///   <item>样板关 1/3（`PixelartLevelPilotSetup`）：关卡资产的烘焙摆位（`ShowcaseLevels.BakedPlacements`，
    ///         与主战斗场景 `RuntimeSceneArt` 同一张表）、出生表与逻辑高度场
    ///         （`units` + `TileTerrainGrid.SurfaceWorldY`）；第 3 关的空岛按 `FloatingIslandShowcaseMenu.Place`
    ///         同一入口合成，摆位沿用 <c>PlaceIntoBattleCenter</c> 的 (20, 13.3, 15)。</item>
    ///   <item>海图 101–108（`PixelartWorldMapPilotSetup`）：`WorldMapCatalog` 的地图定义，
    ///         内容由 `WorldMapComposer.Build` 合成（站面/装饰/kit/道具/礁石），出生点按
    ///         `WorldMapRules.HeightAtWorld` 落在站面顶高上。</item>
    /// </list>
    ///
    /// 【构图中心：两族的规则不同，原因在坐标系的来源不同】
    /// <list type="bullet">
    ///   <item>样板关：本工程的格 → 世界换算（`LevelGeometry.TileToWorld`，1 格 = 2 世界单位）
    ///         把 20×15 格的竞技场映射到世界 [0,40]×[0,30]，场心 = (20, ·, 15)。两关共用这一条，
    ///         差别只在**看多高**（每关内容的高度不同，见下表）。</item>
    ///   <item>海图：地图定义自己声明占据 [0..SpanX]×[0..SpanZ]（`WorldMapCatalog` 的坐标契约），
    ///         场心 = (SpanX/2, ·, SpanZ/2)。**八张图的跨度差 150–280 m**（对角的竞技场是 40×30 的固定尺寸），
    ///         所以取景**按 span 派生**（规则见下），不能像样板关那样写死一组米数。</item>
    /// </list>
    ///
    /// 【三档取景的规则（海图）】一个艺术像素的世界尺寸 = 可见高度 ÷ 参考画布高，
    /// 可见高度是"美术锚"、与分辨率解耦（口径同 `PixelartPilotScene`）：
    /// <list type="bullet">
    ///   <item><b>wide = 0.6 × span</b>：地图主体 + 周围一圈海。**不是 1.0×span**——实测过：
    ///         1.0·span 时整帧只有 **6.6~7.9%** 的像素是场地（`judge_pixelart_pilot.py` 的"场地占比"判据），
    ///         地图成了海中央的一小块，观感上是空镜。30° 俯角/45° 方位下正方形地图的屏幕足印
    ///         ≈ 0.71·span 高 × 1.41·span 宽，0.6·span 的可见高度配合 16:9 让地图占满约 1/4 画面。</item>
    ///   <item><b>mid = 0.35 × span</b>：一座岛/一处村落 —— 出生群与其周边地形可辨（实测场地占比 25~40%）。</item>
    ///   <item><b>close = 0.12 × span</b>：看单位与身边陈设（单位身高 1.85 m 不随地图变，故这一档退化为
    ///         "按比例取一小块"）。**注意**：近机位对准的是**地图几何中心**，中心若恰好是开阔水面，
    ///         这一张就是纯海面（实测 106 的 10 m 档即如此）——所以海图的观感图以 wide/mid 为准。</item>
    /// </list>
    ///
    /// 【这三个数不许各自漂】装配时会用地图定义现场推一遍场心、并用内容的实测包围盒
    /// 核对"镜头确实对着内容"（见 `PixelartWorldMapPilotSetup.AssertFraming`），对不上就报错。
    ///
    /// 【俯角/方位沿用 <see cref="PixelartPilotScene"/>】那是同一份定义
    /// （30° = 规则像素阶梯：横移 2 像素 / 下降 1 像素）。机位距离见 <see cref="CameraDistanceFor"/>。
    /// 两处各写一份的坑本仓踩过。
    /// </summary>
    public static class PixelartLevelScene
    {
        /// <summary>一个关卡试点的取景口径（纯数据；装配器与出图脚本共用同一份）。</summary>
        public readonly struct View
        {
            public readonly int LevelNumber;

            /// <summary>场景名（Build Settings 里的名字，也是装配器保存的资产名）。</summary>
            public readonly string SceneName;

            /// <summary>构图中心（世界坐标）。XZ 恒 = 场心：样板关 = 竞技场心 (20, ·, 15)；
            /// 海图 = (SpanX/2, ·, SpanZ/2)。Y 是"看多高"，与内容高度同档（见下表）。</summary>
            public readonly Vector3 Target;

            /// <summary>宽机位可见高度（米）——整片场地进画面（海图 = 1.0 × span）。</summary>
            public readonly float WideVisibleMeters;

            /// <summary>中机位可见高度（米）——主平台 + 单位群（海图 = 0.35 × span）。</summary>
            public readonly float MidVisibleMeters;

            /// <summary>近机位可见高度（米）——单位与边缘细节（海图 = 固定 10）。</summary>
            public readonly float CloseVisibleMeters;

            public View(int levelNumber, string sceneName, Vector3 target,
                float wide, float mid, float close)
            {
                LevelNumber = levelNumber;
                SceneName = sceneName;
                Target = target;
                WideVisibleMeters = wide;
                MidVisibleMeters = mid;
                CloseVisibleMeters = close;
            }

            /// <summary>出图档位前缀（`pl1-wide` / `pl101-mid`…；判据脚本按它认关卡）。</summary>
            public string ShotPrefix => "pl" + LevelNumber;
        }

        /// <summary>
        /// 全部"真实内容"试点场景的取景表（样板关 1/3 在前，海图 101–108 按关卡号升序在后）。
        ///
        /// 【样板关竖直取值的来路】云顶中位高 4.5（`CloudFieldSpec.HeroTopY`）/ 碎岛台面 0.5
        /// （关卡 2 高度场 max 1 块 × 0.5）/ 空岛草皮面 ≈ 13.3（`PlaceIntoBattleCenter` 的根高，
        /// 与关卡 3 高度场 28 块 × 0.5 = 14.0 同档）。取"略高于台面的眼位"而不是台面本身，
        /// 免得构图上把场地压在画面下缘。
        ///
        /// 【海图竖直取值 3.0 的来路】站面顶高是 0.5 m 档的离散平台，八张图的最高站面 2.5–7.5 m
        /// （多数在 0.5–4.5），图形重心落在站面顶到站面底（视觉盒下沉 4 m，见
        /// `WorldMapComposer.BuildStandBox`）之间，约 y ≈ 0–2。取 3.0 而不是 0：
        /// 视线俯角 30°，构图中心就是画面中心——对着 0 会把站面顶压到画面下缘，
        /// 而抬到最高档 7.5 又会让低矮图（最高 2.5）的战场沉在画面下半。3.0 对八张图都是
        /// "台面附近、略高"，与样板关取眼位同一个读法。
        ///
        /// 【宽/中/近的推导见类头；表里的数就是规则的唯一落点，装配器与出图脚本都不自己算取景。】
        /// </summary>
        static readonly View[] _views =
        {
            new View(1,   "PixelartCloud",     new Vector3(20f, 4.5f, 15f),  32f, 16f, 7f),
            new View(3,   "PixelartSkyIsland", new Vector3(20f, 12.5f, 15f), 30f, 16f, 7f),

            // 海图：Target = (span/2, 3, span/2)；wide = span、mid = 0.35 × span、close = 10。
            // 【span 是横纵相同的正方形】（八张图 SpanX == SpanZ，`WorldMapCatalog` 契约里没有"必须相等"
            // 的约束——真出现长方形时本表要按对角线取大者，届时两个方向的取景一起改）。
            new View(101, "PixelartMap101",   new Vector3(75f, 3f, 75f),  90f, 52.5f, 18f),
            new View(102, "PixelartMap102",   new Vector3(95f, 3f, 95f),  114f, 66.5f, 22.8f),
            new View(103, "PixelartMap103",   new Vector3(110f, 3f, 110f),  132f, 77f, 26.4f),
            new View(104, "PixelartMap104",   new Vector3(120f, 3f, 120f),  144f, 84f, 28.8f),
            new View(105, "PixelartMap105",   new Vector3(90f, 3f, 90f),  108f, 63f, 21.6f),
            new View(106, "PixelartMap106",   new Vector3(130f, 3f, 130f),  156f, 91f, 31.2f),
            new View(107, "PixelartMap107",   new Vector3(100f, 3f, 100f),  120f, 70f, 24f),
            new View(108, "PixelartMap108",   new Vector3(140f, 3f, 140f),  168f, 98f, 33.6f),
        };

        /// <summary>全部试点场景的取景口径（装配器与 Build Settings 登记共用）。</summary>
        public static View[] All => (View[])_views.Clone();

        /// <summary>按关卡号取口径；未知关卡号返回 false（不静默兜底到别的关）。</summary>
        public static bool TryGet(int levelNumber, out View view)
        {
            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i].LevelNumber == levelNumber)
                {
                    view = _views[i];
                    return true;
                }
            }

            view = default;
            return false;
        }

        /// <summary>把"可见多少米高"换算成一个艺术像素的世界尺寸（米）。口径与试点场景同源。</summary>
        public static float WorldPerPixel(float visibleMeters)
        {
            return PixelartPilotScene.WorldPerPixel(visibleMeters);
        }

        /// <summary>
        /// 机位到构图中心的距离（米）。
        ///
        /// 【为什么不能一律用基准 60】正交相机的近平面会切掉"比相机更靠近观察者"的内容：
        /// 机位距离必须大于**内容沿视线方向的半跨度**，否则近侧那半张图落在相机背后被裁掉
        /// （宽机位最明显：画面里"下半张地图整片消失"，且不报错）。海图是正方形、跨度 150–280 m，
        /// 30° 俯角 / 45° 方位下它的半对角沿视线只投 0.71·span（= 0.71 × wide），
        /// 而 60 m 的基准只够罩住样板关（40×30，半跨度 28 m）。
        ///
        /// 【规则】取 1.1 × 宽机位可见高度：1.1·span &gt; 0.71·span，近角在相机前还有 ~39% 余量。
        /// 样板关算出来 33–35 m &lt; 基准 60 m，取值不变 ⇒ <b>两关老场景逐字节不变</b>。
        /// 装配器与出图脚本共用本函数，机位距离才不会两边各写一份。
        /// </summary>
        public static float CameraDistanceFor(View view)
        {
            return Mathf.Max(PixelartPilotScene.CameraDistance, view.WideVisibleMeters * 1.1f);
        }
    }
}
