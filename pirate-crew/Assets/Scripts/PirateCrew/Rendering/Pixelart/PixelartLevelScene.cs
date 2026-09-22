using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化路径的三个关卡试点场景**的取景口径（唯一来源）。
    ///
    /// 【这一族场景在回答什么】<see cref="PixelartPilotScene"/>（图元几何）证明的是"机制对不对"；
    /// 这三个场景换成**三个样板关的真实内容**，回答的是"这套观感用在真关卡上是什么样"，
    /// 同时给 README 的宣传图提供"同一套口径"的成图。
    ///
    /// 【内容从哪来（不许自己编）】装配器 `PixelartLevelPilotSetup` 逐关读**关卡资产**：
    /// 烘焙陈设的摆位（`ShowcaseLevels.BakedPlacements`，与主战斗场景 `RuntimeSceneArt` 同一张表）、
    /// 出生表与逻辑高度场（`units` + `TileTerrainGrid.SurfaceWorldY`）。
    /// 第 3 关的空岛没有烘焙件（它由 `FloatingIslandShowcaseMenu.Place` 程序化合成进场景），
    /// 装配器按同一入口合成，摆位沿用 <c>PlaceIntoBattleCenter</c> 的 (20, 13.3, 15)。
    ///
    /// 【构图中心为什么都是 (20, ·, 15)】本工程的格 → 世界换算（`LevelGeometry.TileToWorld`，
    /// 1 格 = 2 世界单位）把 20×15 格的竞技场映射到世界 [0,40]×[0,30]，场心 = (20, ·, 15)。
    /// 三个场景共用这一条，差别只在**看多高**（每关内容的高度不同，见下表）。
    ///
    /// 【这三个数不许各自漂】装配时会用 `LevelGeometry` 现场推一遍场心、并用内容的实测包围盒
    /// 核对"镜头确实对着内容"（见 `PixelartLevelPilotSetup.AssertFraming`），对不上就报错。
    ///
    /// 【俯角/方位/机位距离沿用 <see cref="PixelartPilotScene"/>】那是同一份定义
    /// （30° = 规则像素阶梯：横移 2 像素 / 下降 1 像素）。两处各写一份的坑本仓踩过。
    /// </summary>
    public static class PixelartLevelScene
    {
        /// <summary>一个关卡试点的取景口径（纯数据；装配器与出图脚本共用同一份）。</summary>
        public readonly struct View
        {
            public readonly int LevelNumber;

            /// <summary>场景名（Build Settings 里的名字，也是装配器保存的资产名）。</summary>
            public readonly string SceneName;

            /// <summary>构图中心（世界坐标；XZ 恒 = 竞技场心）。</summary>
            public readonly Vector3 Target;

            /// <summary>宽机位可见高度（米）——整片场地进画面。</summary>
            public readonly float WideVisibleMeters;

            /// <summary>中机位可见高度（米）——主平台 + 单位群。</summary>
            public readonly float MidVisibleMeters;

            /// <summary>近机位可见高度（米）——单位与边缘细节。</summary>
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

            /// <summary>出图档位前缀（`pl1-wide` / `pl2-mid`…；判据脚本按它认关卡）。</summary>
            public string ShotPrefix => "pl" + LevelNumber;
        }

        /// <summary>
        /// 三个样板关的取景表（关卡号顺序；与 `LevelAssetLibrary` 的关卡号一致）。
        ///
        /// 【竖直方向取值的来路】云顶中位高 4.5（`CloudFieldSpec.HeroTopY`）/ 碎岛台面 0.5
        /// （关卡 2 高度场 max 1 块 × 0.5）/ 空岛草皮面 ≈ 13.3（`PlaceIntoBattleCenter` 的根高，
        /// 与关卡 3 高度场 28 块 × 0.5 = 14.0 同档）。取"略高于台面的眼位"而不是台面本身，
        /// 免得构图上把场地压在画面下缘。
        /// </summary>
        static readonly View[] _views =
        {
            new View(1, "PixelartCloud",     new Vector3(20f, 4.5f, 15f),  32f, 16f, 7f),
            new View(3, "PixelartSkyIsland", new Vector3(20f, 12.5f, 15f), 30f, 16f, 7f),
        };

        /// <summary>全部关卡试点场景名（装配器与 Build Settings 登记共用）。</summary>
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
    }
}
