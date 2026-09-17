using System.Collections.Generic;
using PirateCrew.PirateCrew.Water;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PirateCrew.PirateCrew.Battle.WorldMaps
{
    /// <summary>
    /// 世界地图运行时组装：站面 box → BoxCollider + 分带地形材质视觉。
    ///
    /// 【站面平直机制（§4.2）】碰撞不读网格：每个 <see cref="WorldStandBox"/> 直接生成一个
    /// 顶面严格水平的 BoxCollider——「视觉自由、碰撞平直」两解耦。kit FBX 的网格视觉由
    /// <see cref="WorldMapAssetSet"/>（Editor 生成、Battle 场景持有引用）接入后叠加在站面之上；
    /// 资产表缺件或未配置时仅保留站面（测试与捕图不中断）。
    ///
    /// 【站面配色（批次 E：PirateTerrain 化）】按顶面高度带分三档（+0.5~+1.5 沙、+2~+3 草、
    /// +3.5 以上岩），材质用 <c>PirateCrew/PirateTerrain</c>（与旧瓦片路径同款，参数口径抄
    /// BattleSceneLighting.BuildTerrainMaterial / ApplySandWetParameters），拿到其沙/草/岩
    /// 高度坡度混合、逐面块面感、域扭曲 fBm 细节噪声、湿沙潮痕带全套能力；三色基色与原
    /// 灰盒色板同源（沙 #C4A76A / 草 #4A8C4A / 岩 #8C7B6A）。
    ///
    /// 【本批次的取舍（光照链边界）】PirateTerrain 不采样 SSAO（shader 无
    /// _SCREEN_SPACE_OCCLUSION 分支，见 docs/审计/视觉审计报告.md §三批次 C 的已知边界），
    /// 站面换此 shader 后**不再吃环境遮蔽**，换来的是细节噪声/块面感/湿沙潮痕；
    /// kit 道具（FBX 甲板、植被、船件）仍是 URP/Lit + SSAO——站面与道具的光照响应
    /// 因此有差异，这是本次材质升级的明确代价，实拍轮若发现站面"浮"于道具之上再议。
    /// </summary>
    public static class WorldMapComposer
    {
        /// <summary>碰撞体向下延伸深度（锚进海床，防穿底/隧穿）。</summary>
        const float ColliderDepth = 4f;

        // ------------------------------------------------------------------
        // 站面地形材质（批次 E）：shader 名与回退目标
        // ------------------------------------------------------------------

        /// <summary>站面材质 shader（与旧瓦片路径 BattleSceneLighting.TerrainShaderName 同名）。</summary>
        const string TerrainShaderName = "PirateCrew/PirateTerrain";

        /// <summary>shader 缺失时的回退目标（原灰盒路径用的 URP/Lit 纯色）。</summary>
        const string FallbackLitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>细节噪声贴图目录（口径 = MaterialNoiseBuilder.TextureFolder，Editor 侧唯一产出点）。</summary>
        const string DetailTextureFolder = "Assets/Art/Textures/Materials";

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
            var collisionRoot = new GameObject("Stands");
            collisionRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < standBoxes.Count; i++)
                BuildStandBox(collisionRoot.transform, standBoxes[i], assetSet, i + map.LevelNumber * 7);

            var kitRoot = new GameObject("Kit");
            kitRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < map.Terrain.Count; i++)
                BuildKitPlacement(kitRoot.transform, "Terrain", map.Terrain[i], assetSet);
            for (int i = 0; i < map.Horizon.Count; i++)
                BuildKitPlacement(kitRoot.transform, "Horizon", map.Horizon[i], assetSet);
            for (int i = 0; i < map.Props.Count; i++)
            {
                WorldPropPlacement prop = map.Props[i];
                GameObject prefab = assetSet != null ? assetSet.Get(prop.Asset) : null;
                if (prefab == null)
                    continue;
                var go = Object.Instantiate(prefab, kitRoot.transform);
                go.name = "Prop_" + prop.Asset;
                // 落地吸附：道具 y 一律吸附到所在 (x,z) 的地表顶高（目录里的 y 只是提示），
                // 水面件（浮标等）吸附到水面 —— 消掉「摆错高度」这一整类手工错误。
                go.transform.position = new Vector3(
                    prop.Position.x,
                    WorldMapRules.HeightAtWorld(standBoxes, new Vector2(prop.Position.x, prop.Position.z)),
                    prop.Position.z);
                go.transform.rotation = Quaternion.Euler(0f, prop.YawDeg, 0f);
                go.isStatic = true;
                // 批次 F：水面件（浮标等）吸附后 y = WaterSurfaceY，资格判定用**吸附后**的最终 y——
                // "贴水"与否以落点为准，目录里的提示 y 不参与（Props 路径本来也不读它）。
                ApplyFloatingViewIfEligible(go, go.transform.position.y);
            }

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

        static void BuildStandBox(Transform root, in WorldMapRules.WorldBox box,
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
            // 【有限色板纪律】站面只用分带共享材质（3 档，PirateTerrain 化见类头批次 E 段），
            // 不做逐块抖动——低多边形的色彩层次来自色板本身 + 植被散布 + 光照，不是随机扰动
            // （行业实践；逐块实例化还会爆材质预算）。
            renderer.sharedMaterial = BandMaterial(box.TopY);

            ScatterDecorations(go.transform, box, assetSet, seed);
        }

        /// <summary>
        /// 程序化散布：每块站面按确定性哈希撒 2-4 个植被/碎岩装饰（高度带决定种类：
        /// 低=草丛/碎岩/蕨、中=蕨/斜棕榈/中岩/草、高=岩），位置在 box 内缩 3u 的矩形里抖动，
        /// y 吸附站面顶。密度由代码保证，不再依赖手摆。
        /// </summary>
        static void ScatterDecorations(Transform parent, in WorldMapRules.WorldBox box,
            WorldMapAssetSet assetSet, int seed)
        {
            if (assetSet == null || box.Size.x < 3f || box.Size.y < 3f)
                return;
            uint h = (uint)(seed * 2654435761u);
            int count = 2 + (int)((h >> 17) % 3u);
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
                // 世界位置：站面中心 + 旋转偏移；y = 站面顶（道具原点在落地面）。
                go.transform.localPosition = new Vector3(
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
        /// PirateTerrain 缺失时回退 URP/Lit 纯色（原灰盒路径），不让海面变品红。
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
        // 档位划分与高度阈值（纯函数，供 <see cref="WorldMapStandMaterialTests"/> 钉值）
        // ------------------------------------------------------------------

        /// <summary>顶面高度 → 档位（边界 1.5 / 3.0，与装饰散布池的划分同式同值）。</summary>
        public static int BandOf(float topY) => topY <= 1.5f ? 0 : (topY <= 3.0f ? 1 : 2);

        /// <summary>
        /// 各档的沙→草高度阈值 _HeightSandGrass。
        ///
        /// 【为什么不是统一用旧路径默认 0.6】PirateTerrain 按**世界高度**逐片元混色，阈值 0.6/3.0
        /// 是为旧瓦片路径的连续高度梯度调的（BattleSceneLighting.cs:511-513 自述「抬升半格以上转草」）；
        /// 而站面是 0.5 档**离散平台**，档位边界（1.5/3.0）与旧阈值不重合：沿用 0.6 时 band0 的
        /// TopY=1.0/1.5 站面草权重 ≈0.9~1.0（整面变草），同档站面半沙半草——破坏「三档有限色板、
        /// 同档同观感」纪律。故改为**每档一份材质、把阈值抬/降到本档权重平台区**（三档本就共享
        /// 缓存各持一份，独立阈值不增加材质预算）：
        ///   band 0 = 2.5：档内最大顶面 1.5 + 噪声抖动 0.35 + 过渡带 0.5 = 2.35 ≤ 2.5 → 顶面恒纯沙
        ///   band 1/2 = 0.6（旧路径值）：档内最小顶面 2.0 - 0.35 - 0.5 = 1.15 ≥ 1.1 → 顶面恒纯草
        /// 平台性推导由测试钉住（WorldMapStandMaterialTests），材质值改了测试会叫。
        /// </summary>
        public static float HeightSandGrassOf(int band) => band == 0 ? 2.5f : 0.6f;

        /// <summary>
        /// 各档的草→岩高度阈值 _HeightGrassRock（推导理由同 <see cref="HeightSandGrassOf"/>）：
        ///   band 0/1 = 4.5：4.5 - 0.5 ≥ 档内最大顶面 3.0 + 抖动 0.35 → 顶面恒不出岩；
        ///   band 2 = 2.0：2.0 + 0.5 ≤ 档内最小顶面 3.0 - 抖动 0.35 → 顶面恒纯岩
        ///   （TopY&gt;3.0 开区间，即使将来出现 3.05 的站面也成立）。
        /// 岩的来源交给**坡度项**（_SlopeRockStart/End）：站面侧壁 |normal.y|=0 → 坡度 1 ≥ 0.72
        /// → 侧壁恒岩——与旧路径 Cube 地形「台地侧壁=岩壁」的读法一致，观感连续。
        /// </summary>
        public static float HeightGrassRockOf(int band) => band == 2 ? 2.0f : 4.5f;

        // ------------------------------------------------------------------
        // PirateTerrain 材质生成（参数口径逐项对照 BattleSceneLighting，来源行号见注释）
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成一档站面材质；shader 缺失返回 null（由调用方回退 Lit 纯色）。
        /// 【口径来源】共享参数逐值抄 <c>BattleSceneLighting.BuildTerrainMaterial</c>
        /// （BattleSceneLighting.cs:532-568），湿沙带抄 <c>ApplySandWetParameters</c>
        /// （BattleSceneLighting.cs:252-265），保证站面沙滩与旧瓦片路径观感连续。
        /// </summary>
        static Material CreateTerrainMaterial(int band)
        {
            Shader shader = Shader.Find(TerrainShaderName);
            if (shader == null)
            {
                Debug.LogError("[WorldMapComposer] Shader.Find 找不到 " + TerrainShaderName
                    + "（被剔除/未编译？），站面回退 " + FallbackLitShaderName + " 纯色。");
                return null;
            }

            var mat = new Material(shader)
            {
                name = "WorldMapStand_Band" + band + (band == 0 ? "_Sand" : band == 1 ? "_Grass" : "_Rock"),
            };

            // ---- 三色（GDD §10.4 中档；= 旧路径 BattleSceneLighting.cs:537-539 = 原灰盒色板）----
            mat.SetColor("_SandColor", new Color(0.769f, 0.655f, 0.416f));   // #C4A76A
            mat.SetColor("_GrassColor", new Color(0.290f, 0.549f, 0.290f));  // #4A8C4A
            mat.SetColor("_RockColor", new Color(0.549f, 0.482f, 0.416f));   // #8C7B6A

            // ---- 高度 / 坡度混合（高度阈值按站面档位重定，理由见两个 Of() 函数；
            //      坡度阈值与过渡带抄旧路径 BattleSceneLighting.cs:542-544）----
            mat.SetFloat("_HeightSandGrass", HeightSandGrassOf(band));
            mat.SetFloat("_HeightGrassRock", HeightGrassRockOf(band));
            mat.SetFloat("_SlopeRockStart", 0.45f);
            mat.SetFloat("_SlopeRockEnd", 0.72f);
            mat.SetFloat("_BlendSoftness", 0.5f);

            // ---- 程序化噪声 / 块面（抄旧路径 BattleSceneLighting.cs:545-551）----
            mat.SetFloat("_NoiseScale", 2f);
            mat.SetFloat("_NoiseStrength", 0.35f);
            mat.SetFloat("_BlockSize", 1f);
            mat.SetFloat("_BlockTintStrength", 0.042f);   // r5：块缘亮度差降 30% 后的值
            mat.SetFloat("_FacetStrength", 0.6f);
            mat.SetFloat("_BlockWarp", 0.4f);

            // ---- 格缝暖灰（抄旧路径 BattleSceneLighting.cs:553-555；r5 口径）----
            mat.SetColor("_SeamColor", new Color(0.486f, 0.459f, 0.416f));   // #7C756A
            mat.SetFloat("_SeamStrength", 0.18f);
            mat.SetFloat("_SeamWidth", 0.12f);

            // ---- 近竖直面暖偏置（r6 口径；旧路径未覆写，这里显式写 shader 默认值自证口径）----
            mat.SetColor("_VerticalWarmColor", new Color(0.5412f, 0.4588f, 0.3608f)); // #8A755C
            mat.SetFloat("_VerticalWarmStrength", 0.12f);
            mat.SetFloat("_VerticalWarmPower", 1.5f);

            // ---- 粗糙度分区（抄旧路径 BattleSceneLighting.cs:558-560；沙/草/岩两两差 ≥0.15。
            //      注意：取代原灰盒的 _Smoothness 0.18/0.22 两档——分档语义改由高度权重承担，
            //      三档站面共用同一组分区值，与旧路径沙滩的高光响应连续）----
            mat.SetFloat("_Smoothness", 0.25f);        // 沙族基准
            mat.SetFloat("_GrassSmoothness", 0.40f);
            mat.SetFloat("_RockSmoothness", 0.55f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_AmbientStrength", 1f);

            // ---- 描边兼容关闭（写实方向；抄旧路径 BattleSceneLighting.cs:563-566）----
            mat.SetColor("_EdgeColor", new Color(0.165f, 0.165f, 0.165f));   // #2A2A2A
            mat.SetFloat("_EdgeStrength", 0f);
            mat.SetFloat("_EdgePower", 3f);
            mat.SetFloat("_DebugMode", 0f);

            // ---- 湿沙带（抄 ApplySandWetParameters，BattleSceneLighting.cs:252-265）----
            // 【_WaterLevelY 用常量而非旧路径的硬编码 -0.2】旧路径注释声称与 LevelGeometry 同源，
            //   实际常量已是 -0.4（LevelGeometry.cs:197）——硬编码已漂移；站面以玩法常量为准，
            //   水位调整时自动联动。shader 其余湿参数默认值与旧路径写入值一致，仍显式写出自证。
            mat.SetFloat("_WaterLevelY", LevelGeometry.WaterSurfaceY);
            mat.SetFloat("_WetBandWidth", 0.45f);         // 水线以上过渡到全干的世界单位宽
            mat.SetColor("_WetSandColor", new Color(0.659f, 0.553f, 0.361f)); // #A88D5C【AI 提案色，同旧路径】
            mat.SetFloat("_WetDarken", 0.3f);
            mat.SetFloat("_WetSmoothnessBoost", 0.45f);   // 旧路径值（shader 默认 0.25，不覆写会露差异）
            mat.SetFloat("_WetLineMin", 0.10f);           // 潮痕残留线下界
            mat.SetFloat("_WetLineMax", 0.22f);           // 上界
            mat.SetFloat("_WetLineDarkening", 0.25f);     // 旧路径未覆写，沿用 shader 默认
            mat.SetFloat("_RippleScale", 16f);            // 沙纹 λ ≈ 0.39 世界单位
            mat.SetFloat("_RippleStrength", 0.10f);
            mat.SetFloat("_RippleDistort", 0.35f);
            // 【当前数据的边界说明】现役站面最低顶面 TopY=0.5，高于湿带上界
            //   WaterSurfaceY+0.45=+0.05 → 湿沙潮痕带在**现役站面顶面不触发**（侧壁又因坡度
            //   转岩、sandWeight=0，同样不染湿沙）。参数仍对齐旧口径：将来接入滩涂级低站面
            //   （TopY≤0.05，如浅滩 kit）时潮痕带自动生效，无需再改这里。【提案/待定】

            // ---- 细节噪声贴图（三族 albedo+法线；仅编辑器内接线，见函数内注释）----
#if UNITY_EDITOR
            ApplyTerrainDetailTextures(mat);
#endif
            return mat;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 给站面材质挂三族细节噪声贴图（沙/草/岩各 albedo+法线，512² 程序化资产，
        /// 由 MaterialNoiseBuilder 产出）。世界尺度与强度逐值抄旧路径
        /// <c>BattleSceneLighting.ApplyTerrainDetailTextures</c>（BattleSceneLighting.cs:597-616）。
        ///
        /// 【为什么包在 #if UNITY_EDITOR】本类是运行时程序集，MaterialNoiseBuilder 在 Editor
        /// 程序集不可引用；运行时同步读资产只能走 AssetDatabase（仅编辑器可用）。
        /// 打包构建下走不到这里：两条强度保持 shader 默认 0 → 退回「纯色 + 程序化噪声」，
        /// 与旧路径「贴图缺失→强度置 0」的降级语义一致（BattleSceneLighting.cs:578-581）。
        /// </summary>
        static void ApplyTerrainDetailTextures(Material m)
        {
            m.SetFloat("_NoiseWarpStrength", 0.18f);      // 打断贴图与 1 单位格子的轴向对齐
            // r5 世界尺度：沙 0.05（20m 平铺）/ 草 0.04（25m）/ 岩 0.10（10m）。
            m.SetFloat("_SandNoiseWorldScale", 0.05f);
            m.SetFloat("_GrassNoiseWorldScale", 0.04f);
            m.SetFloat("_RockNoiseWorldScale", 0.10f);

            // 强度口径同旧路径：albedo 三族 1.0；法线 = 贴图自带 RMS 斜率的倍率 沙1.0/草1.1/岩1.2。
            // &= 非短路：六张全部尝试加载，缺任何一张汇总一条警告。
            bool ok = true;
            ok &= AssignDetailTexture(m, "Noise_Sand_Albedo",  "_SandNoiseMap",  "_SandDetailAlbedoStrength", 1f);
            ok &= AssignDetailTexture(m, "Noise_Grass_Albedo", "_GrassNoiseMap", "_GrassDetailAlbedoStrength", 1f);
            ok &= AssignDetailTexture(m, "Noise_Rock_Albedo",  "_RockNoiseMap",  "_RockDetailAlbedoStrength", 1f);
            ok &= AssignDetailTexture(m, "Noise_Sand_Normal",  "_SandBumpMap",   "_SandBumpScale",  1.0f);
            ok &= AssignDetailTexture(m, "Noise_Grass_Normal", "_GrassBumpMap",  "_GrassBumpScale", 1.1f);
            ok &= AssignDetailTexture(m, "Noise_Rock_Normal",  "_RockBumpMap",   "_RockBumpScale",  1.2f);

            if (!ok)
                Debug.LogWarning("[WorldMapComposer] 站面材质细节噪声贴图缺失（先跑菜单 PirateCrew/渲染/"
                    + "生成程序化材质噪声贴图）：对应强度已置 0，站面退回纯色+程序化噪声。");
        }

        /// <summary>赋一张细节贴图并打开对应强度；返回 false = 缺失（强度保持 0，行为退回改动前）。
        /// 先把强度写 0：保证「上次跑过、这次贴图没了」时不残留旧的开启状态（同旧路径纪律）。</summary>
        static bool AssignDetailTexture(Material m, string fileName,
            string textureProperty, string strengthProperty, float strength)
        {
            m.SetFloat(strengthProperty, 0f);
            if (!m.HasProperty(textureProperty))
                return false;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                DetailTextureFolder + "/" + fileName + ".png");
            if (texture == null)
                return false;

            m.SetTexture(textureProperty, texture);
            m.SetFloat(strengthProperty, strength);
            return true;
        }
#endif

        /// <summary>
        /// shader 缺失时的回退材质：原灰盒 URP/Lit 纯色三档（色板与明度纪律原样保留）。
        /// 只在 PirateTerrain 不可用时出现，正常路径不应看到这批纯色面。
        /// </summary>
        static Material CreateFallbackLitMaterial(int band)
        {
            // SceneArtPalette 中间调：沙 #C4A76A / 草 #4A8C4A / 岩 #8C7B6A（docs/美术风格指南.md §2.1）
            var color = band == 0
                ? new Color(0.769f, 0.655f, 0.416f)
                : band == 1
                    ? new Color(0.290f, 0.549f, 0.290f)
                    : new Color(0.549f, 0.482f, 0.416f);
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
