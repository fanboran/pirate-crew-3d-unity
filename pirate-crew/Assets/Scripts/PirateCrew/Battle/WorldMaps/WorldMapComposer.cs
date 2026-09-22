using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.Water;
using UnityEngine;

namespace PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图运行时组装：站面 box → BoxCollider + 分带地形材质视觉。
    ///
    /// 【站面平直机制（§4.2）】碰撞不读网格：每个 <see cref="WorldStandBox"/> 直接生成一个
    /// 顶面严格水平的 BoxCollider——「视觉自由、碰撞平直」两解耦。kit FBX 的网格视觉由
    /// <see cref="WorldMapAssetSet"/>（Editor 生成、Battle 场景持有引用）接入后叠加在站面之上；
    /// 资产表缺件或未配置时仅保留站面（测试与捕图不中断）。
    ///
    /// 【站面配色（像素化路径）】按顶面高度带分三档（+0.5~+1.5 沙、+2~+3 草、+3.5 以上岩），
    /// 每档一份**平色**材质，颜色与色带档数全部取自 <see cref="PixelartMaterialFactory"/>
    /// （本路径的唯一配方源）——调用点不再写 hex、不再写档数，改配方只改工厂一处。
    ///
    /// 【为什么放弃旧 Terrain 口径】旧实现用 <c>PirateCrew/PirateTerrain</c> 按世界高度/坡度
    /// 混合沙/草/岩三色，另叠块面噪声、湿沙潮痕、细节噪声贴图与三档粗糙度（参数口径逐值抄
    /// BattleSceneLighting.BuildTerrainMaterial / ApplySandWetParameters）。本路径的物体 shader
    /// （<see cref="PixelartPath.ObjectShaderName"/>）**根本没有这些属性**：它只写
    /// albedo / 法线 / 物理 / 形状一组 G-buffer，色带与描边发生在低分辨率域那几趟着色里。
    /// 旧口径的参数照抄进新 shader 只会全部落空（静默无效），因此站面的沙/草/岩改由
    /// 「每档一份平色材质」承担——这是本路径的定义而非功能倒退。
    ///
    /// 【明确的代价】台面侧壁与顶面同色（旧链靠坡度项把侧壁读成岩壁），海图试点场景已按同口径
    /// 记录；kit 道具/植被仍是 URP/Lit（各自材质），与站面的光照响应差异属本路径的既有安排。
    /// </summary>
    public static class WorldMapComposer
    {
        /// <summary>碰撞体向下延伸深度（锚进海床，防穿底/隧穿）。</summary>
        const float ColliderDepth = 4f;

        // ------------------------------------------------------------------
        // 站面材质：回退目标
        // ------------------------------------------------------------------

        /// <summary>物体 shader 缺失时的回退目标（原灰盒路径用的 URP/Lit 纯色）。
        /// 只在 <see cref="PixelartPath.ObjectShaderName"/> 不在包里时出现，正常路径不应看到。</summary>
        const string FallbackLitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>构建整图（碰撞 + 灰盒 + 已配置的 kit 视觉）。返回根 Transform。</summary>
        public static Transform Build(Transform parent, WorldMapDefinition map, WorldMapAssetSet assetSet)
        {
            // 换图重建 = 旧图已随场景卸载销毁的时点：上一张图缓存的三档站面材质已无引用者，
            // 在此销毁释放（static 字典跨场景常驻，不 here 释放会在整个会话里越积越占）。
            // 每张图重建 3 个材质的成本可忽略，换来「缓存不跨图滞留」的明确生命周期。
            ReleaseBandMaterialCache();

            var root = new GameObject("WorldMap_" + map.Id);
            if (parent != null)
                root.transform.SetParent(parent, false);

            var standBoxes = WorldMapRules.AllStandBoxes(map);

            // 【层级顺序】三个根按「站面 → 装饰 → kit」建立：装饰的父级必须是**未缩放**的根
            // （理由见 ScatterDecorations 的坐标系注释），所以不能挂在站面方块下。
            var collisionRoot = new GameObject("Stands");
            collisionRoot.transform.SetParent(root.transform, false);
            var decorRoot = new GameObject("Decor");
            decorRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < standBoxes.Count; i++)
                BuildStandBox(collisionRoot.transform, decorRoot.transform, standBoxes[i], assetSet,
                    i + map.LevelNumber * 7);

            var kitRoot = new GameObject("Kit");
            kitRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < map.Terrain.Count; i++)
                BuildKitPlacement(kitRoot.transform, "Terrain", map.Terrain[i], assetSet);
            for (int i = 0; i < map.Horizon.Count; i++)
                BuildKitPlacement(kitRoot.transform, "Horizon", map.Horizon[i], assetSet);

            // 手摆道具（叙事件：篝火/炮位/宝箱堆/遗迹…）——落水即跳过并告警，见 BuildProp。
            var propRoot = new GameObject("Props");
            propRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < map.Props.Count; i++)
                BuildProp(propRoot.transform, map, standBoxes, assetSet, map.Props[i]);

            // 死水礁石场（规则层确定性摆放，纯视觉、无碰撞）：紧凑化后 86–96% 的图面是空海，
            // 这一层把它读成"有礁、有浅滩、有残骸的海"。与手摆 Props 走同一个实例化出口。
            var reefRoot = new GameObject("ReefField");
            reefRoot.transform.SetParent(root.transform, false);
            var reefField = ReefFieldRules.Place(map);
            for (int i = 0; i < reefField.Count; i++)
                BuildProp(reefRoot.transform, map, standBoxes, assetSet, reefField[i]);

            // M4 远景特征接线：HorizonSeed/HorizonFeatures 的唯一消费点。规则层（纯 C#，可测）
            // 产出确定性环带摆放，这里走与手摆 Horizon 完全相同的 kit 接入路径（BuildKitPlacement：
            // 同一张资产表、同样的缺件静默跳过与静态标记）——不发明第二套实例化机制。
            // 特征件在玩法区外（环带 200–280u > 地图对角半径），无碰撞、isStatic、随 root 销毁。
            var horizonFeatures = HorizonFeatureRules.Place(map);
            for (int i = 0; i < horizonFeatures.Count; i++)
            {
                HorizonFeatureRules.HorizonFeaturePlacement feature = horizonFeatures[i];
                BuildKitPlacement(kitRoot.transform, "HorizonFeature",
                    new WorldKitPlacement("Horizon", feature.Asset,
                        feature.Position.x, feature.Position.z, feature.YawDeg, feature.Position.y),
                    assetSet);
            }
            return root.transform;
        }

        /// <summary>
        /// 实例化一个道具（手摆 Props 与规则层礁石场共用出口）：y 一律吸附到所在 (x,z) 的地表顶高
        /// （目录里的 y 只是提示，无站面时吸附到静水面）——消掉「摆错高度」这一整类手工错误。
        ///
        /// 【陆地道具不许落水】吸附结果是水面 = 该 xz 没有站面，说明坐标在紧凑化/重摆后漂到海里。
        /// 若该资产又不在 <see cref="ReefFieldRules.AllowedOnOpenWater"/> 白名单里（石质件/浮件），
        /// 就**不生成**并在编辑器/开发构建里点名报警：浮在浪上的篝火/木箱是最刺眼的一类穿帮，
        /// 宁可缺件也要把问题推回数据侧修。规则见 WorldMapPropPlacementTests（无头可测）。
        /// </summary>
        static void BuildProp(Transform root, WorldMapDefinition map,
            List<WorldMapRules.WorldBox> standBoxes, WorldMapAssetSet assetSet, in WorldPropPlacement prop)
        {
            GameObject prefab = assetSet != null ? assetSet.Get(prop.Asset) : null;
            if (prefab == null)
                return;

            float groundY = WorldMapRules.HeightAtWorld(
                standBoxes, new Vector2(prop.Position.x, prop.Position.z));
            if (groundY <= LevelGeometry.WaterSurfaceY + 0.001f
                && !ReefFieldRules.AllowedOnOpenWater(prop.Asset))
            {
                global::PirateCrew.Core.Log.Warn(string.Format(
                    "[WorldMapComposer] {0} 的道具 {1} 落在 ({2:F1},{3:F1}) 无站面的水面上，已跳过——"
                    + "请把它挪到站面 box 上（布局探针见 external/layout-report.txt）。",
                    map.Id, prop.Asset, prop.Position.x, prop.Position.z));
                return;
            }

            var go = Object.Instantiate(prefab, root);
            go.name = "Prop_" + prop.Asset;
            go.transform.position = new Vector3(prop.Position.x, groundY, prop.Position.z);
            go.transform.rotation = Quaternion.Euler(0f, prop.YawDeg, 0f);
            go.isStatic = true;
            // 批次 F：水面件（浮标等）吸附后 y = WaterSurfaceY，资格判定用**吸附后**的最终 y——
            // "贴水"与否以落点为准，目录里的提示 y 不参与（Props 路径本来也不读它）。
            ApplyFloatingViewIfEligible(go, go.transform.position.y);
        }

        static void BuildStandBox(Transform root, Transform decorParent, in WorldMapRules.WorldBox box,
            WorldMapAssetSet assetSet, int seed)
        {
            var go = new GameObject(string.Format("Stand_{0:F0}_{1:F0}_{2:F1}",
                box.Center.x, box.Center.y, box.TopY));
            go.transform.SetParent(root, false);
            // box.Center 为平面 (X, Z)；摆放根在海平面 y=0，顶面 = TopY。
            float height = box.TopY + ColliderDepth;
            // 【M4 实拍修正】视觉顶面下沉 0.06：kit FBX 的甲板与站面同高，同高会 z-fighting
            // （整图白模闪烁的元凶）；下沉后 kit 表面赢，灰盒只在 kit 缺位处可见。
            const float visualDrop = 0.06f;
            go.transform.localPosition = new Vector3(
                box.Center.x, box.TopY - visualDrop - height * 0.5f, box.Center.y);
            go.transform.localRotation = Quaternion.Euler(0f, box.YawDeg, 0f);
            // 站面视觉 = 按 box 尺寸缩放的立方体 + 分带地形材质（kit FBX 接入后作为其上的表皮/兜底）。
            go.transform.localScale = new Vector3(box.Size.x, height, box.Size.y);

            var collider = go.AddComponent<BoxCollider>();
            collider.size = Vector3.one;
            collider.center = new Vector3(0f, visualDrop * 0.5f, 0f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = CubeMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            // 【有限色板纪律】站面只用分带共享材质（3 档平色，见类头），不做逐块抖动——
            // 低多边形的色彩层次来自色板本身 + 植被散布 + 光照，不是随机扰动
            // （行业实践；逐块实例化还会爆材质预算）。
            renderer.sharedMaterial = BandMaterial(box.TopY);

            ScatterDecorations(decorParent, box, assetSet, seed);
        }

        /// <summary>
        /// 程序化散布：每块站面按确定性哈希撒 4-6 个植被/碎岩装饰（高度带决定种类：
        /// 低=草丛/碎岩/蕨、中=蕨/斜棕榈/中岩/草、高=岩），位置在 box 内缩 3u 的矩形里抖动，
        /// y 吸附站面顶。密度由代码保证，不再依赖手摆。
        ///
        /// 【坐标系铁律：装饰的父级必须是未缩放的根】站面方块自己带着
        /// <c>localScale = (Size.x, 顶高+4, Size.y)</c>（用单位立方体拉伸成 box 的实现），
        /// 把装饰挂成它的子物体、再写 <c>localPosition</c> 会被父级缩放**再乘一遍**——
        /// 实测 50~147 件装饰全部被甩到图外 3000~12000u 处，实拍里"散布几乎不可见"
        /// （docs/审计/地图设计审计报告.md §二.6）的根因就是这个。故装饰一律挂 <c>Decor</c> 根、
        /// 写世界坐标；散布位置只有一处数学（下面的 lx/lz → 世界），不再经过父变换。
        /// </summary>
        static void ScatterDecorations(Transform parent, in WorldMapRules.WorldBox box,
            WorldMapAssetSet assetSet, int seed)
        {
            if (assetSet == null || box.Size.x < 3f || box.Size.y < 3f)
                return;
            uint h = (uint)(seed * 2654435761u);
            // 密度 4-6 件/站面（交接 §20 既定方向：原 2-4 件在 150-280u 的大图上看不出层次）。
            int count = 4 + (int)((h >> 17) % 3u);
            string[] lowPool = { "GrassTuft", "RockS", "FernClump" };
            string[] midPool = { "FernClump", "PalmLean", "RockM", "GrassTuft" };
            string[] highPool = { "RockM", "RockS" };
            var pool = box.TopY <= 1.5f ? lowPool : (box.TopY <= 3.0f ? midPool : highPool);

            float rad = box.YawDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            for (int i = 0; i < count; i++)
            {
                h = h * 1664525u + 1013904223u;
                float fx = ((h >> 8) % 1000u) / 1000f - 0.5f;
                h = h * 1664525u + 1013904223u;
                float fz = ((h >> 8) % 1000u) / 1000f - 0.5f;
                h = h * 1664525u + 1013904223u;
                float yaw = ((h >> 8) % 1000u) / 1000f * 360f;

                string asset = pool[(int)((h >> 9) % (uint)pool.Length)];
                GameObject prefab = assetSet.Get(asset);
                if (prefab == null)
                    continue;

                float lx = fx * (box.Size.x - 3f);
                float lz = fz * (box.Size.y - 3f);
                var go = Object.Instantiate(prefab, parent);
                go.name = "Decor_" + asset;
                // 世界位置：站面中心 + 按 box 总转角旋转的局部偏移；y = 站面顶（道具原点在落地面）。
                go.transform.position = new Vector3(
                    box.Center.x + lx * cos - lz * sin,
                    box.TopY,
                    box.Center.y + lx * sin + lz * cos);
                go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                go.isStatic = true;
            }
        }

        static void BuildKitPlacement(Transform root, string group, in WorldKitPlacement placement,
            WorldMapAssetSet assetSet)
        {
            GameObject prefab = assetSet != null ? assetSet.Get(placement.Asset) : null;
            if (prefab == null)
                return; // 站面灰盒即地形本体；kit 件缺位时静默跳过

            var go = Object.Instantiate(prefab, root);
            go.name = group + "_" + placement.Asset;
            go.transform.position = placement.Position;
            go.transform.rotation = Quaternion.Euler(0f, placement.YawDeg, 0f);
            go.isStatic = true;
            // 批次 F：地形/远景件将来若新增水面 kit（浮标、锚地小船…），放置即自动获得起伏。
            ApplyFloatingViewIfEligible(go, placement.Position.y);
        }

        /// <summary>
        /// 批次 F：给命中漂浮白名单的道具挂 <see cref="FloatingPropView"/>（纯表现的水面起伏，
        /// 不参与任何玩法判定）。资格判据在 <see cref="FloatingPropView.EligibleForFloatingView"/>
        /// （纯函数，Tests/Water 钉值）：名字含 "Buoy"，或含 "Boat"/"Ship" 且不含 "Beached"；
        /// 且放置 y 与 <see cref="LevelGeometry.WaterSurfaceY"/> 距离 ≤ 1.0u。
        /// 装配点有两处：kit 路径（本函数调用方）与 Props 路径（Build 内）——现役唯一漂浮件
        /// BuoyRing 走 Props 路径，两处共用同一判据，后续加资产自动受益。
        ///
        /// 【为什么命中要 go.isStatic = false】Unity 静态合批把 isStatic 物体的顶点在构建期
        /// 烘进合并大网格，之后运行时改 transform **不再生效**——起伏会被整批吞掉，浮标
        /// 永远钉死在原高度。置 false 让它退出静态合批（单独 DrawCall，现役仅个位数件，
        /// 批次预算可忽略），LateUpdate 的位移才能落到画面上。
        /// </summary>
        static void ApplyFloatingViewIfEligible(GameObject go, float placementY)
        {
            if (!FloatingPropView.EligibleForFloatingView(go.name, placementY))
                return;
            go.AddComponent<FloatingPropView>();
            go.isStatic = false;
        }

        // ------------------------------------------------------------------
        // 共享网格与材质
        // ------------------------------------------------------------------

        static Mesh _cube;

        static Mesh CubeMesh()
        {
            if (_cube == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(temp.GetComponent<Collider>());
                temp.hideFlags = HideFlags.HideAndDontSave;
                _cube = temp.GetComponent<MeshFilter>().sharedMesh;
            }
            return _cube;
        }

        static readonly Dictionary<int, Material> _bandMaterials = new Dictionary<int, Material>();

        /// <summary>
        /// 释放三档共享站面材质缓存并清空字典（换图重建时由 <see cref="Build"/> 开头调用）。
        /// static 字典跨场景常驻，材质是运行时 new 出来的对象、不随场景卸载销毁——不 here 释放
        /// 会在整个编辑器会话/进程里无人认领地滞留。Object.Destroy 延迟到帧末也不影响正确性：
        /// 旧图的站面已无引用，字典同步清空即视为释放完成。
        /// </summary>
        public static void ReleaseBandMaterialCache()
        {
            foreach (var pair in _bandMaterials)
            {
                if (pair.Value != null)
                    Object.Destroy(pair.Value);
            }
            _bandMaterials.Clear();
        }

        /// <summary>
        /// 站面分带材质（三档共享缓存，不按 box 实例化——有限色板/材质预算纪律不变）。
        /// band 0（TopY≤1.5，沙）/ band 1（≤3.0，草）/ band 2（>3.0，岩）。
        /// 物体 shader 缺失时回退 URP/Lit 纯色（原灰盒路径），不让站面变品红/不可见。
        /// </summary>
        static Material BandMaterial(float topY)
        {
            int band = BandOf(topY);
            if (_bandMaterials.TryGetValue(band, out Material mat))
                return mat;

            mat = CreateTerrainMaterial(band) ?? CreateFallbackLitMaterial(band);
            _bandMaterials[band] = mat;
            return mat;
        }

        // ------------------------------------------------------------------
        // 档位划分（纯函数，供 <see cref="WorldMapStandMaterialTests"/> 钉值）
        // ------------------------------------------------------------------

        /// <summary>
        /// 顶面高度 → 档位（边界 1.5 / 3.0，与装饰散布池的划分同式同值）。
        /// 【三档如何变成颜色】band 0/1/2 → <see cref="PixelartMaterialFactory.StandSand"/> /
        /// <see cref="PixelartMaterialFactory.StandGrass"/> / <see cref="PixelartMaterialFactory.StandRock"/>，
        /// 映射写在 <see cref="CreateTerrainMaterial"/>（那里有测试的接缝，见测试类头）。
        /// </summary>
        public static int BandOf(float topY) => topY <= 1.5f ? 0 : (topY <= 3.0f ? 1 : 2);

        // ------------------------------------------------------------------
        // 像素化站面材质生成（唯一配方源 = PixelartMaterialFactory）
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成一档站面材质（像素化路径）：band 0/1/2 → 沙/草/岩三档**平色**，色带档数 3、
        /// 描边 1 艺术像素（档数/描边都走工厂默认值，不在这里重复写）。
        ///
        /// 【材质名逐字保持】`WorldMapStand_Band{N}_{Sand|Grass|Rock}` 是海图试点装配器的
        /// **显式映射键**（<c>PixelartWorldMapPilotSetup.Band*Name</c>）：名字改一个字符，
        /// 试点场景的站面换装就落空。本方法只在这里拼这一个名字，改名必须同时改那边。
        ///
        /// 【返回 null 的语义】物体 shader 不在包里（构建被剔除/编译失败）时工厂已报错，
        /// 这里补一句定性并让调用方回退 <see cref="FallbackLitShaderName"/> 纯色——
        /// **这是打包问题，不是合成逻辑错**：站面几何/碰撞都正常，只是没有像素化着色。
        /// </summary>
        static Material CreateTerrainMaterial(int band)
        {
            Color albedo = band == 0
                ? PixelartMaterialFactory.StandSand
                : band == 1
                    ? PixelartMaterialFactory.StandGrass
                    : PixelartMaterialFactory.StandRock;

            Material mat = PixelartMaterialFactory.Create(BandMaterialNameOf(band), albedo);
            if (mat != null)
                return mat;

            global::PirateCrew.Core.Log.Error("[WorldMapComposer] 站面 band" + band
                + " 的像素化材质未创建（物体 shader \"" + PixelartPath.ObjectShaderName
                + "\" 不在包里），已回退 " + FallbackLitShaderName + " 纯色。"
                + "这是**构建包丢 shader 资产**（未被任何已引资产带进包 / Graphics Settings 未加 Always Included），"
                + "不是合成逻辑错误——把该 shader 加进 Always Included Shaders 即可。");
            return null;
        }

        /// <summary>
        /// 站面材质名：`WorldMapStand_Band{N}_{Sand|Grass|Rock}`（试点装配器的显式映射键，
        /// 见 <see cref="CreateTerrainMaterial"/> 的说明）。
        /// </summary>
        static string BandMaterialNameOf(int band)
        {
            return "WorldMapStand_Band" + band + (band == 0 ? "_Sand" : band == 1 ? "_Grass" : "_Rock");
        }

        /// <summary>
        /// shader 缺失时的回退材质：原灰盒 URP/Lit 纯色三档（明度纪律原样保留）。
        /// 只在物体 shader 不在包里时出现，正常路径不应看到这批纯色面。
        /// 颜色取自 <see cref="PixelartMaterialFactory"/> 的站面三档（同一份色，不在回退路径里再写一遍）。
        /// </summary>
        static Material CreateFallbackLitMaterial(int band)
        {
            Color color = band == 0
                ? PixelartMaterialFactory.StandSand
                : band == 1
                    ? PixelartMaterialFactory.StandGrass
                    : PixelartMaterialFactory.StandRock;
            var shader = Shader.Find(FallbackLitShaderName);
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", band == 0 ? 0.18f : 0.22f);
            return mat;
        }
    }
}
