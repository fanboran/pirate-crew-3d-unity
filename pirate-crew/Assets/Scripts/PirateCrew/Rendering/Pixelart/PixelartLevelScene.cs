using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化路径"真实内容"试点场景**的取景口径（唯一来源）：四个样板关（1 云场 / 3 空岛 /
    /// 4·5 废弃化工厂）当前在册。八张大海域海图（关卡号 101–108）已删除待重做——它们的取景行
    /// 随数据资产一并移除，重做后按下面的规则补回（规则本身不变）。
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
    ///   <item>海图 101–108（`PixelartWorldMapPilotSetup`，当前 0 张）：`WorldMapCatalog` 的地图定义，
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
    ///   <item><b>wide / mid / close = 32 / 14 / 7 m（与样板关同一组数，不随跨度缩放）</b>：
    ///         **人物大小才是锚**——同一档在不同关卡里角色必须一样大，否则"横向比观感"这件事本身不成立
    ///         （实测踩过：把 mid 绑成 0.35×跨度时，280 m 的地图里角色只有 150 m 地图里的一半大）。
    ///         32 m 是"一处台面群落 + 若干单位"的取景，与两张样板关的 README 成图同尺度、可直接对比；
    ///         mid 取 **13.7 m**（创始人 2026-09-30 定值，原 14 m；再往前 2026-09-22 从 16 m 收紧）——
    ///         13.7 m 同时是游戏内正交档 6.85（可见高度 = 2 × OrthoSize，见 `BattleCameraDriver.RuntimeVisibleMeters`），
    ///         也就是"游戏内能滚轮滚到的那个档"，图与游戏内因此对得上。</item>
    ///   <item><b>整图总览另开一档</b>（只在海图档里出现，见 `PlayerArtCapture.LevelShots` 的 `-overview`）：
    ///         可见高度 = 0.85 × span，整张地图进画面。**为什么不把它当默认的 wide**：整图取景下场地
    ///         只占画面 8~12%（实测），单位缩到 1~2 个艺术像素 ⇒ 看不出任何观感。
    ///         0.85 而不是 0.6/1.0：1.0 太松（同样的空镜问题），0.85 是"地图对角刚好进画面"的临界。</item>
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

            /// <summary>宽机位可见高度（米）——"一处台面群落 + 若干单位"（十关统一 32，不随跨度缩放）。</summary>
            public readonly float WideVisibleMeters;

            /// <summary>中机位可见高度（米）——台面近距离（十关统一 14 = 游戏内正交档 7，见类头）。</summary>
            public readonly float MidVisibleMeters;

            /// <summary>近机位可见高度（米）——单位与边缘细节（十关统一 7）。</summary>
            public readonly float CloseVisibleMeters;

            /// <summary>
            /// 本关方位角（度）。**0 = 未覆盖，兜底用全局默认 45°**（<see cref="PixelartPilotScene.AzimuthDegrees"/>，
            /// 对称菱形）——现役取景表全部不传此参数，行为逐字节不变；要偏转某一关的构图
            /// （比如把角色转到侧面剪影看描边），在这一行加第五个参数即可，装配器与出图脚本
            /// 同一份数据（<see cref="PixelartLevelScene.AzimuthFor"/>），不会两边各写一份。
            /// </summary>
            public readonly float AzimuthDegrees;

            public View(int levelNumber, string sceneName, Vector3 target,
                float wide, float mid, float close, float azimuthDegrees = 0f)
            {
                LevelNumber = levelNumber;
                SceneName = sceneName;
                Target = target;
                WideVisibleMeters = wide;
                MidVisibleMeters = mid;
                CloseVisibleMeters = close;
                AzimuthDegrees = azimuthDegrees;
            }

            /// <summary>出图档位前缀（`pl1-wide` / `pl101-mid`…；判据脚本按它认关卡）。</summary>
            public string ShotPrefix => "pl" + LevelNumber;
        }

        /// <summary>
        /// 全部"真实内容"试点场景的取景表（样板关 1/3 在前，随后是废弃化工厂 4·5；
        /// 海图 101–108 已删除待重做，取景行随之移除，重做后按类头与表内注释的口径补回）。
        ///
        /// 【样板关竖直取值的来路】云顶中位高 4.5（`CloudFieldSpec.HeroTopY`）/ 空岛草皮面 ≈ 13.3
        /// （`PlaceIntoBattleCenter` 的根高，与关卡 3 高度场 28 块 × 0.5 = 14.0 同档）。
        /// 取"略高于台面的眼位"而不是台面本身，免得构图上把场地压在画面下缘。
        ///
        /// 【海图竖直取值 3.0 的来路（备查）】站面顶高是 0.5 m 档的离散平台，旧八图的最高站面 2.5–7.5 m
        /// （多数在 0.5–4.5），图形重心落在站面顶到站面底（视觉盒下沉 4 m，见
        /// `WorldMapComposer.BuildStandBox`）之间，约 y ≈ 0–2。取 3.0 而不是 0：
        /// 视线俯角 30°，构图中心就是画面中心——对着 0 会把站面顶压到画面下缘，
        /// 而抬到最高档 7.5 又会让低矮图（最高 2.5）的战场沉在画面下半。3.0 对旧八图都是
        /// "台面附近、略高"，与样板关取眼位同一个读法。
        ///
        /// 【宽/中/近的推导见类头；表里的数就是规则的唯一落点，装配器与出图脚本都不自己算取景。】
        /// </summary>
        static readonly View[] _views =
        {
            new View(1,   "PixelartCloud",     new Vector3(20f, 4.5f, 15f),  32f, 13.7f, 7f),
            new View(3,   "PixelartSkyIsland", new Vector3(20f, 12.5f, 15f), 30f, 13.7f, 7f),

            // 第 4 关「废弃化工厂」（**提案/待定**）：内容来自 Blender 手作总装件
            // `Assets/Art/Models/WorldKit/ChemPlant/ChemPlant_Level.fbx`（装配器
            // `PixelartChemPlantSetup`），**尚未接玩法数据**——没有高度场/编成/摆位表，故这一行
            // 只服务"这套观感用在这座场地上的实机成图"，不是可玩关卡（见
            // `docs/设计/关卡/L04-废弃化工厂.md`）。总装件原点 = 场地中心 ⇒ 场心 = 世界原点。
            // 竖直 6 的来路：场地地坪 0、最高件（烟囱 26.5 / 冷却塔 22.5）拉高剪影，
            // 质量重心在 0–15 m 之间，取 6 = "场地中低部 + 略高于人眼"，与样板关取眼位同一个读法。
            new View(4,   "PixelartChemPlant", new Vector3(0f, 6f, 0f),     32f, 13.7f, 7f),
            // 第 5 关「废弃化工厂 · 六件并行版」（**提案/待定**）：内容来自 Blender 六件并行分件式 kit
            // （`tools/blender/scene/chemplant/`）的总装件 `Assets/Art/Models/SceneKit/ChemPlant.fbx`
            // （装配器 `PixelartChemPlantTeamSetup`）——**尚未接玩法数据**，这一行只服务
            // 「这套观感用在这座 56×40 m 场地上的实机成图」。总装件原点 = 场地中心 ⇒ 场心 = 世界原点；
            // 竖直 6 的读法与第 4 关相同（地坪 0、最高件（烟囱）26 m，取 6 = 场地中低部 + 略高于人眼）。
            new View(5,   "PixelartChemPlantTeam", new Vector3(0f, 6f, 0f), 32f, 13.7f, 7f),

            // 关卡 6「纯草坪验收场」（**提案/待定**）：专门验收 t3ssel8r 口径草丛的**无地形平地**
            // （装配器 `PixelartGrassFieldSetup`：暗绿底板 + 满铺草簇，不接玩法数据）——
            // 草丛三档斑块是世界坐标噪声，只有在"除了草没别的"的场地上才能单独读出形状/尺度/连贯性。
            // 竖直 0.3 的来路：草簇高 0.26-0.5 m，取"草尖之半"的眼位，与样板关取眼位同一个读法。
            // 近机位 10（r22，创始人判 7 太贴地）：草皮尺度下 7 m 只剩斑驳，10 m 能读到簇形。
            new View(6,   "PixelartGrassField", new Vector3(20f, 0.3f, 15f), 32f, 13.7f, 10f),

            // 【海图取景行：当前 0 张】八张海图（101–108）已删除待重做，取景行随数据资产一并移除。
            // 重做时的口径（列在这里备查，规则与上面一致）：
            //   Target = (span/2, 3, span/2)；wide/mid/close = 32/14/7（**不随 span 缩放**，见类头）。
            //   span 是横纵相同的正方形（旧八图 SpanX == SpanZ；真出现长方形时按对角线取大者，
            //   两个方向的取景一起改）。竖直 3.0 的来路见本表上方的说明。
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

        /// <summary>
        /// 本关机位方位角：关卡行覆盖了就用关卡值，没覆盖兜底全局默认（45° 对称菱形）。
        /// **装配器与出图脚本都走这里**，方位角才不会两边各写一份（那坑本仓踩过）。
        /// </summary>
        public static float AzimuthFor(View view)
        {
            return view.AzimuthDegrees > 0f ? view.AzimuthDegrees : PixelartPilotScene.AzimuthDegrees;
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
