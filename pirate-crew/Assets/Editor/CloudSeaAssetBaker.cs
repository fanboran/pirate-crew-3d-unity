using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.EditorTools.BuildSystem;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.SceneArt.CloudSea;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 云端漫步整场件（<c>CloudWalk.fbx</c> → **自包含** <c>CloudWalk.prefab</c>）的编辑器烘焙器：
    /// FBX 内嵌材质按「对象 → 着色路线」换装成本路径材质资产 → prefab 根挂 CloudSea 运行时组件
    /// → 烘一张海床高度格写进 <see cref="CloudSeaLife"/> → 把 prefab 接进 <see cref="RuntimeSceneArt"/>
    /// 的 <c>cloudFieldPrefab</c>，一次收口。材质在 prefab 里已换好，运行时实例化不再依赖
    /// <c>PixelartContentConverter</c> 补扫。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/烘焙/云端漫步场景件
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.CloudSeaAssetBaker.BuildAll
    ///
    /// 【前置】两样必须先在，缺一会编译不过/报错退出：
    ///   ① <c>tools/blender/scene/cloudsea/assemble_cloudsea.py</c> 出库的
    ///      <c>Assets/Art/Models/SceneKit/CloudWalk.fbx</c>（内嵌材质名 = style_tokens.py 的
    ///      <c>Cloud_*</c> 段）；② <c>PirateCrew.SceneArt.CloudSea</c> 命名空间下的运行时组件
    ///      CloudSeaWater / CloudSeaWeather / CloudSeaLife / CloudSeaSunShadow（本文件直接
    ///      <c>AddComponent</c> 引用，不走反射）。
    ///
    /// 【着色路线（判据在 <see cref="CloudSeaMaterialRoute"/>，落 <see cref="MaterialFolder"/>）】
    ///   · Default（水上件）：<see cref="PixelartMaterialFactory"/> 配方的物体 shader（色带 4 档、
    ///     抖动 0.21、法线内墨线、队列 2000），色取源材质 <c>_BaseColor</c>/<c>_Color</c>；
    ///     其中 <c>Cloud_Water</c> / <c>Cloud_WaterDeep</c> / <c>Cloud_Glass</c> 是云台水洼 /
    ///     水壁 / 玻璃的透明 shader 材质（<see cref="PixelartPath.TransparentShaderName"/> 的
    ///     同族 <c>CloudSeaWater</c>；连续 alpha 混合，不打色带档）；
    ///   · Seabed（<c>Cloud_Sea_Bed</c>）：程序沙地 + 焦散的海床 shader；
    ///   · ClearWater（<c>Cloud_Sea_Surface</c>）：清澈透明海面 shader，专用资产
    ///     <c>Cloud_WaterSurface.mat</c>；
    ///   · Underwater（海草 / 岩石 / 珊瑚 / 沉积物 / 鱼群 / 螃蟹 / 气泡）：水下生态 shader。
    ///   同一个源材质名被多条路线共用时（珊瑚色既是海底的珊瑚也是海鸥的喙），默认路线保留原名、
    ///   其余路线的资产名加路线后缀（<c>Cloud_Coral_Underwater</c>）。
    ///
    /// 【海面族材质的参数归谁】海床 / 水下 / 海面三支 shader 的材质只由本烘焙器写「shader +
    /// 固有色 + 路线开关」，其余参数（沙纹、焦散、水色、摆幅……）是材质资产上的手调值，重跑
    ///   不覆盖——调参在材质上改，不必改这里。
    ///
    /// 【海床高度格】从 <c>Cloud_Sea_Bed</c> 网格顶点烘一张方格（<see cref="BedGridCells"/>）
    ///   写进 <see cref="CloudSeaLife"/>：运行时鱼群 / 螃蟹查它贴着沙丘起伏定深
    ///   （<see cref="CloudSeaBedGrid"/>）。
    ///
    /// 【金属件与鱼色卡】<c>Cloud_Brass</c> / <c>Cloud_Gold</c>：在配方之上加金属件参数
    ///   （`_Metallic` 0.8 / `_Smoothness` 1 / 暖金边缘光）；大平面高光翻档的取舍见工厂
    ///   <c>Configure</c> 头注释——金属件显式给非零值正是该口径的例外侧。
    ///
    /// 【为什么同时写 `_Color` 与 `_BaseColor`】透明 shader 的有效乘色属性是 `_Color`
    /// （贴图 × 顶点色 × `_Color` 后 alpha 混合）；`_BaseColor` 是同值镜像，对齐"本仓材质
    /// 取色一律认 `_BaseColor`"的家族口径（未来换色/调参工具按 `_BaseColor` 找色不断链）。
    ///
    /// 【`_UseDither` 为什么写】它是 PostLine 链（`PostLineToonLit`）的抖动开关拼写；本路径
    /// 物体 shader 没有这个属性（抖动恒开、幅度即 `_DitherStrength`）。写它只是把"该开抖动"
    /// 的意图随材质落盘——`PostLineMaterialSwapper` 换装时按源材质 `_DitherStrength` 换算
    /// `_UseDither`（>0 ⇒ 1），两边口径一致。
    ///
    /// 【接线写在哪：折叠态契约】Battle 场景是「场景 = BattleRig.prefab 纯实例」的折叠态
    /// （Assets/Scenes/README.md §三），`RuntimeSceneArt` 居 prefab 内——接线写
    /// <c>BattleRig.prefab</c> 本体（LoadPrefabContents → 改 → SaveAsPrefabAsset → 读回验证），
    /// **不往场景实例写覆盖**：往实例写字段等于把接线知识从 prefab 散回场景文件，正是折叠
    /// 要消灭的状态。只有 prefab 里找不到组件（折叠前旧形态）才回退为打开 Battle 场景接线。
    ///
    /// 【与 SceneArtBaker 的关系】两者写同一路径 prefab；本类是云端漫步重做
    /// （Blender cloudsea kit，材质名 `Cloud_*`）的收口器，收口归属以装配链
    /// （BattleScenePipeline 步③）最终接的是谁为准。
    /// </summary>
    public static class CloudSeaAssetBaker
    {
        const string LogTag = "[CloudSeaAssetBaker]";

        /// <summary>Blender cloudsea kit 出库件（assemble_cloudsea.py 的 FBX 落点）。</summary>
        const string ModelPath = "Assets/Art/Models/SceneKit/CloudWalk.fbx";

        /// <summary>烘焙产物（自包含 prefab：材质已换好、根挂 CloudSea 运行时组件）。</summary>
        const string PrefabPath = "Assets/Art/Models/SceneKit/CloudWalk.prefab";

        /// <summary>云端漫步材质资产目录（按名一材质一文件，重跑复用/更新，幂等）。</summary>
        const string MaterialFolder = "Assets/Art/Materials/CloudSea";

        /// <summary>Battle 折叠载体（RuntimeSceneArt 现居其中；ScenePrefabCollapse 的折叠目标）。</summary>
        const string BattleRigPath = "Assets/Prefabs/PirateCrew/Battle/BattleRig.prefab";

        /// <summary>海面专用材质（Cloud_Sea_Surface；清澈透明水 shader，水色在材质上直接配）。</summary>
        const string SurfaceMaterialName = "Cloud_WaterSurface";
        const string SurfaceMaterialPath = MaterialFolder + "/Cloud_WaterSurface.mat";

        /// <summary>
        /// 海床高度格：1 单位格距、覆盖 ±20 单位。格点取周围半格内最高的顶点，所以比沙面略高——
        /// 活物的「不能穿线」宁可高一线。可视区里的鱼群 / 螃蟹锚点都在覆盖范围内。
        /// </summary>
        const int BedGridCells = 40;
        const float BedGridHalfSize = 20f;

        /// <summary>海面材质队列 = Transparent + 1：URP 标准透明档的收件下界实测是 3001（CloudSeaRain.shader 头注）。</summary>
        const int TransparentQueueStandard = 3001;

        /// <summary>
        /// 透明趟材质（按源材质名分流；PostLineMaterialSwapper 只认物体 shader 材质，这三件天然不在换装列）。
        /// </summary>
        static readonly HashSet<string> TransparentNames = new HashSet<string>
        {
            "Cloud_Water", "Cloud_WaterDeep", "Cloud_Glass",
        };

        /// <summary>金属件（配方之上加金属度/光滑度/暖金边缘光；其余材质保持工厂默认的非金属无边缘光）。</summary>
        static readonly HashSet<string> MetallicNames = new HashSet<string>
        {
            "Cloud_Brass", "Cloud_Gold",
        };

        /// <summary>
        /// 水下会摇曳的植物材质：开薄片双面受光 + 梢部摆幅。这些材质的网格带「离根高度比」UV
        /// （Blender 侧 kit_common.Mesher 的逐顶点辅助量），其余水下网格没有 UV、位移恒 0。
        /// </summary>
        static readonly HashSet<string> SwayingPlantNames = new HashSet<string>
        {
            "Cloud_Seaweed", "Cloud_Plant",
        };

        /// <summary>
        /// 鱼身墨线壳的材质名（Blender 侧 mod_sea._fish 的反向外壳，挂在鱼群对象上、走水下路线）。
        /// 壳的固有色由 <see cref="InkLineColor"/> 在这里定值，不从源材质取色。
        /// </summary>
        const string InkLineMaterialName = "Cloud_FishLine";

        /// <summary>
        /// 墨线壳固有色（sRGB，深靛）：隔一层水色后仍比水下沙面暗一大截——阳光区约 (43,118,131)
        /// 对沙面 (183,201,136)，岛影区约 (22,33,72) 对 (108,114,76)。
        /// </summary>
        static readonly Color InkLineColor = new Color(0.13f, 0.12f, 0.26f, 1f);

        /// <summary>
        /// 透明件的 alpha 兜底表（源材质读不到 &lt;1 的 alpha 时按名取值；源材质显式带了
        /// alpha 则尊重源）。Blender 侧 style_tokens.py 只有 hex/roughness，FBX 内嵌色
        /// alpha 恒 1——这三个值就是透明度的定值口径，改透明度改这里。
        /// </summary>
        static readonly Dictionary<string, float> TransparentAlphaFallback = new Dictionary<string, float>
        {
            { "Cloud_Water", 0.60f },
            { "Cloud_WaterDeep", 0.60f },
            { "Cloud_Glass", 0.25f },
        };

        /// <summary>本烘焙器用到的五支 shader（BuildAll 一次性取齐）。</summary>
        sealed class Shaders
        {
            public Shader Object;
            public Shader Transparent;
            public Shader ClearWater;
            public Shader Seabed;
            public Shader Underwater;
        }

        /// <summary>烘焙统计（日志收口用；数值只进日志，不作契约）。</summary>
        sealed class Stats
        {
            public int Renderers;
            public int Swapped;
            public int MaterialsCreated;
            public int MaterialsUpdated;
            public int ComponentsAdded;
        }

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/烘焙/云端漫步场景件")]
        public static void BuildAll()
        {
            // Blender 出库后编辑器未必已导入（交互路径）；batchmode 启动自刷，这一步幂等兜底。
            AssetDatabase.Refresh();

            var problems = new List<string>();

            EnsureModelImporter(problems);

            GameObject model = null;
            Shaders shaders = null;
            if (problems.Count == 0)
            {
                model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null)
                    problems.Add("缺 " + ModelPath
                        + "（先跑 tools/blender/scene/cloudsea/assemble_cloudsea.py 出库）。");

                shaders = LoadShaders(problems);
            }

            var stats = new Stats();
            bool prefabSaved = false;
            if (problems.Count == 0)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
                root.name = "CloudWalk";

                SwapMaterials(root, shaders, problems, stats);
                EnsureCloudSeaComponents(root, problems, stats);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out prefabSaved);
                Object.DestroyImmediate(root);
                if (!prefabSaved)
                    problems.Add("prefab 保存失败：" + PrefabPath);
            }

            // prefab 一旦覆盖落盘就必须复检 + 接线（材质阶段的软问题不拦这两步）：
            // 覆盖保存后根 fileID 可能变，跳过接线反倒会把 BattleRig 的引用留成断链。
            string wiredTo = null;
            if (prefabSaved)
            {
                VerifySavedPrefab(problems);
                wiredTo = WireRuntimeSceneArt(problems);
            }

            AssetDatabase.SaveAssets();

            Debug.Log(LogTag + " 烘焙完成：renderer " + stats.Renderers + " 个（换装 " + stats.Swapped
                + "）；材质 新建 " + stats.MaterialsCreated + " / 复用更新 " + stats.MaterialsUpdated
                + " → " + MaterialFolder + "/；prefab → " + PrefabPath
                + "；CloudSea 组件新增 " + stats.ComponentsAdded + " 个；场景引用落点："
                + (wiredTo ?? "未写（见上方报错）") + "。");

            if (problems.Count > 0)
            {
                for (int i = 0; i < problems.Count; i++)
                    Debug.LogError(LogTag + " " + problems[i]);
                // 抛异常让无头 -executeMethod 拿到非零退出码（BattleScenePipeline 同口径）。
                // 全限定：引 System 命名空间会让 Object 与 UnityEngine.Object 歧义。
                throw new System.InvalidOperationException(LogTag + " 带 " + problems.Count
                    + " 个问题收口（见上方报错），修完重跑。");
            }
        }

        // ------------------------------------------------------------------
        // FBX 导入参数（幂等显式化）
        // ------------------------------------------------------------------

        /// <summary>
        /// FBX 导入参数显式化（幂等），与岛体件同口径：ImportStandard（None 读不到内嵌材质，
        /// 换装取不了色）、useFileScale（吃 FBX 根单位层）、bakeAxisConversion（Blender Z-up
        /// → Unity Y-up；不设模型躺倒）。
        /// </summary>
        static void EnsureModelImporter(List<string> problems)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                problems.Add("FBX 缺失或不可导入：" + ModelPath
                    + "（先跑 tools/blender/scene/cloudsea/assemble_cloudsea.py 出库）。");
                return;
            }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard
                || !importer.useFileScale || !importer.bakeAxisConversion)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------
        // shader 取用
        // ------------------------------------------------------------------

        /// <summary>
        /// 取齐五支 shader，缺哪支记一条问题。海面族三支（清澈水 / 海床 / 水下）走
        /// <see cref="PixelartShaders.Load"/>（Resources 持有，文件名 = 内部名末段）；云台水洼的
        /// <c>CloudSeaWater</c> 文件名与内部名末段不一致，显式 Resources 路径 + 按内部名兜底。
        /// </summary>
        static Shaders LoadShaders(List<string> problems)
        {
            var shaders = new Shaders();
            shaders.Object = PixelartShaders.Load(PixelartPath.ObjectShaderName);
            shaders.Transparent = Resources.Load<Shader>(CloudSeaWaterMaterial.ShaderResourcePath);
            if (shaders.Transparent == null)
                shaders.Transparent = PixelartShaders.Find(CloudSeaPath.WaterShaderName);
            shaders.ClearWater = PixelartShaders.Load(CloudSeaPath.ClearWaterShaderName);
            shaders.Seabed = PixelartShaders.Load(CloudSeaPath.SeabedShaderName);
            shaders.Underwater = PixelartShaders.Load(CloudSeaPath.UnderwaterShaderName);

            if (shaders.Object == null)
                problems.Add("物体 shader 摸不到：" + PixelartPath.ObjectShaderName + "，不透明件无法换装。");
            if (shaders.Transparent == null)
                problems.Add("水/玻璃 shader 摸不到：Resources/"
                    + CloudSeaWaterMaterial.ShaderResourcePath + " 与 "
                    + CloudSeaPath.WaterShaderName + " 都落空。");
            if (shaders.ClearWater == null)
                problems.Add("海面 shader 摸不到：" + CloudSeaPath.ClearWaterShaderName
                    + "（Resources/PixelartShaders/CloudSeaClearWater.shader 未编译或未导入？）。");
            if (shaders.Seabed == null)
                problems.Add("海床 shader 摸不到：" + CloudSeaPath.SeabedShaderName
                    + "（Resources/PixelartShaders/CloudSeaSeabed.shader 未编译或未导入？）。");
            if (shaders.Underwater == null)
                problems.Add("水下生态 shader 摸不到：" + CloudSeaPath.UnderwaterShaderName
                    + "（Resources/PixelartShaders/CloudSeaUnderwater.shader 未编译或未导入？）。");
            return shaders;
        }

        // ------------------------------------------------------------------
        // 材质换装
        // ------------------------------------------------------------------

        /// <summary>
        /// 逐 renderer 逐槽换装：先按对象名定路线，再按（源材质, 路线）建/取材质，同源多槽只走
        /// 一次 Ensure/落盘。逐槽而不是只看第 0 槽：多材质 renderer 的其余子网格留在内嵌材质上
        /// 就是"旧 shader + 未定义 G-buffer"的暗坑（PixelartContentConverter 同一条教训）。
        /// </summary>
        static void SwapMaterials(GameObject root, Shaders shaders, List<string> problems, Stats stats)
        {
            EnsureFolder(MaterialFolder);
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);

            // 第一遍：每个源材质名出现在几条路线上——被多条路线共用的名字要派生后缀资产。
            var routesPerName = new Dictionary<string, HashSet<CloudSeaRoute>>();
            for (int r = 0; r < renderers.Length; r++)
            {
                CloudSeaRoute route = CloudSeaMaterialRoute.Of(renderers[r].gameObject.name);
                foreach (Material source in renderers[r].sharedMaterials)
                {
                    if (source == null)
                        continue;
                    if (!routesPerName.TryGetValue(source.name, out HashSet<CloudSeaRoute> routes))
                        routesPerName[source.name] = routes = new HashSet<CloudSeaRoute>();
                    routes.Add(route);
                }
            }

            // 第二遍：建/取材质并换装。
            var baked = new Dictionary<(Material source, CloudSeaRoute route), Material>();
            for (int r = 0; r < renderers.Length; r++)
            {
                MeshRenderer renderer = renderers[r];
                stats.Renderers++;
                CloudSeaRoute route = CloudSeaMaterialRoute.Of(renderer.gameObject.name);
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null)
                        continue;

                    if (!baked.TryGetValue((source, route), out Material target))
                    {
                        target = EnsureMaterial(source, route, routesPerName[source.name].Count, shaders,
                            problems, stats);
                        baked[(source, route)] = target;
                    }

                    if (target == null || ReferenceEquals(slots[i], target))
                        continue;

                    slots[i] = target;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = slots;
                    stats.Swapped++;
                }
            }
        }

        /// <summary>
        /// 按源材质名 + 路线新建/就地更新材质（幂等：重跑覆盖「shader + 固有色 + 路线开关」，
        /// 源材质取色是唯一入色口；其余参数留在资产上不碰）。
        /// 取色失败**先报错后落盘**——半配置的材质资产不落盘，下次跑干净重来。
        /// </summary>
        static Material EnsureMaterial(Material source, CloudSeaRoute route, int routeCount, Shaders shaders,
            List<string> problems, Stats stats)
        {
            string name = source.name;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Cloud_"))
            {
                problems.Add("FBX 内嵌材质 \"" + name + "\" 不带 Cloud_ 前缀"
                    + "（assemble_cloudsea.py 的 token 口径是 Cloud_*）——不换装，请核对出库脚本。");
                return null;
            }

            // 海面：专用资产 + 清澈水 shader。水色是材质上手配的，不从源材质取色。
            if (route == CloudSeaRoute.ClearWater)
            {
                Material surface = AssetDatabase.LoadAssetAtPath<Material>(SurfaceMaterialPath);
                if (surface == null)
                {
                    surface = new Material(shaders.ClearWater) { name = SurfaceMaterialName };
                    AssetDatabase.CreateAsset(surface, SurfaceMaterialPath);
                    stats.MaterialsCreated++;
                }
                else
                {
                    stats.MaterialsUpdated++;
                }
                surface.shader = shaders.ClearWater;
                surface.renderQueue = TransparentQueueStandard;
                EditorUtility.SetDirty(surface);
                return surface;
            }

            // 先取色（失败即返回，不落半配置资产）；后建/载资产。
            if (!PixelartMaterialFactory.TryGetAlbedo(source, out Color color))
            {
                problems.Add("Cloud 材质 \"" + name + "\" 取不到源色（_BaseColor/_Color 都没有）——本件保持未换装。");
                return null;
            }

            bool transparent = route == CloudSeaRoute.Default && TransparentNames.Contains(name);
            string assetName = CloudSeaMaterialRoute.AssetName(name, route, routeCount);
            string path = MaterialFolder + "/" + assetName + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(ShaderOf(route, transparent, shaders)) { name = assetName };
                AssetDatabase.CreateAsset(mat, path);
                stats.MaterialsCreated++;
            }
            else
            {
                stats.MaterialsUpdated++;
            }
            mat.name = assetName;

            switch (route)
            {
                case CloudSeaRoute.Seabed:
                    ConfigureSeabed(mat, color, shaders);
                    break;
                case CloudSeaRoute.Underwater:
                    ConfigureUnderwater(mat, name, color, shaders);
                    break;
                default:
                    if (transparent)
                        ConfigureTransparent(mat, name, color, shaders);
                    else
                        ConfigureOpaque(mat, name, color, shaders);
                    break;
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Shader ShaderOf(CloudSeaRoute route, bool transparent, Shaders shaders)
        {
            switch (route)
            {
                case CloudSeaRoute.Seabed:
                    return shaders.Seabed;
                case CloudSeaRoute.Underwater:
                    return shaders.Underwater;
                default:
                    return transparent ? shaders.Transparent : shaders.Object;
            }
        }

        /// <summary>
        /// 不透明件配方：工厂 <see cref="PixelartMaterialFactory.Configure"/> 一份到底
        /// （色带 4 档 / 抖动 0.21 / 法线内墨线 / 队列 2000 / 像素吸附），逐物体参数只在
        /// 配方之上做加法。显式再写 `_MainLightLevel`/`_DitherStrength` 与工厂默认同值——
        /// 本烘焙器的定值口径不随工厂默认漂移而静默变化（工厂改默认必须在这里同步裁决）。
        /// </summary>
        static void ConfigureOpaque(Material mat, string name, Color albedo, Shaders shaders)
        {
            mat.shader = shaders.Object;

            albedo.a = 1f;
            PixelartMaterialFactory.Configure(mat, albedo, PixelartMaterialFactory.DefaultBandCount);
            mat.SetFloat("_MainLightLevel", 4f);
            mat.SetFloat("_DitherStrength", PixelartMaterialFactory.DefaultDitherStrength);
            // PostLine 链的抖动开关拼写；本 shader 无此属性（恒开），落盘作换装口径锚（见类头）。
            mat.SetFloat("_UseDither", 1f);

            // 【云体内部描边线（2026-11-20，创始人要"非纯黑的内部描边线"）】法线边加成在
            // 面转折处把色带往暗档压——线色 = 材质自身暗档（云上是蓝灰），不是墨线全局黑。
            // 工厂默认阈值 1.0（夹角 >60° 才触发），平滑云体永远够不着 ⇒ 整场没有内线；
            // 云体槽降到 0.3，让大曲面的缓梯度也出线。海床/海水不参与（平顶要干净、水是透明趟）。
            if (CloudSeaPath.IsCloudBodyMaterial(name))
            {
                mat.SetFloat("_NormalEdgeThreshold", 0.30f);
                mat.SetFloat("_NormalEdgeLevel", -0.6f);
            }

            if (MetallicNames.Contains(name))
            {
                mat.SetFloat("_Metallic", 0.8f);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetColor("_RimLightColor", new Color(0.85f, 0.78f, 0.5f, 1f));
            }
        }

        /// <summary>
        /// 透明件配方：透明 shader + 队列落透明区间。
        /// 有效乘色是 `_Color`（含 alpha），`_BaseColor` 同值镜像（见类头）。
        /// </summary>
        static void ConfigureTransparent(Material mat, string name, Color color, Shaders shaders)
        {
            mat.shader = shaders.Transparent;

            float alpha = color.a;
            if (alpha > 0.999f)
                alpha = TransparentAlphaFallback[name];
            color.a = alpha;

            mat.SetColor("_Color", color);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_DepthTest", 1f);
            // 水网格没有 COLOR 通道（vcol=0），Unity 给顶点色输入 0——直接乘会把整片水
            // 乘成 alpha 0。权重显式落 0，只有将来真烘了顶点渐变的水件才置 1。
            mat.SetFloat("_VertexColorWeight", 0f);
            mat.renderQueue = PixelartPath.TransparentQueueMin;
        }

        /// <summary>
        /// 海床材质：沙色基色 = 源色。海床网格的第二个槽（远景的平整深色外框，源色
        /// <c>Cloud_SeabedDeep</c>）关掉全部图案，只留一深一浅两档色——它在可视区之外，
        /// 只求与近处海床衔接处不跳色。
        /// </summary>
        static void ConfigureSeabed(Material mat, Color color, Shaders shaders)
        {
            mat.shader = shaders.Seabed;
            // 源色（调色板 Sand）偏灰白，透过青色水后读成薄荷绿。乘一组暖沙增益让它在水下仍读作沙：
            // 取色口给出的数值 r × 1.11、g × 1.07、b × 0.53（与材质上实拍的值同源，改这里等于改所有海床材质）。
            color = new Color(color.r * 1.11f, color.g * 1.07f, color.b * 0.53f, 1f);
            mat.SetColor("_BaseColor", color);
            mat.renderQueue = -1;   // 跟 shader 标签走（Geometry）

            if (mat.name == "Cloud_SeabedDeep")
            {
                mat.SetColor("_LowColor", new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 1f));
                mat.SetFloat("_PatchEnable", 0f);
                mat.SetFloat("_RippleEnable", 0f);
                mat.SetFloat("_AlgaeEnable", 0f);
                mat.SetFloat("_PebbleEnable", 0f);
                mat.SetFloat("_CausticEnable", 0f);
            }
        }

        /// <summary>
        /// 水下生态材质：固有色 = 源色；会摇曳的植物开薄片双面受光与梢部摆幅。
        /// </summary>
        static void ConfigureUnderwater(Material mat, string sourceName, Color color, Shaders shaders)
        {
            mat.shader = shaders.Underwater;
            // 鱼身墨线壳的固有色在这里定值：墨线是「隔水可读」设计的一部分，不跟源材质取色走。
            if (sourceName == InkLineMaterialName)
                color = InkLineColor;
            color.a = 1f;
            mat.SetColor("_BaseColor", color);
            mat.renderQueue = -1;   // 跟 shader 标签走（Geometry）

            if (SwayingPlantNames.Contains(sourceName))
            {
                mat.SetFloat("_TwoSidedLight", 1f);
                mat.SetFloat("_SwayAmplitude", 0.10f);
            }
        }

        // ------------------------------------------------------------------
        // 运行时组件
        // ------------------------------------------------------------------

        /// <summary>
        /// prefab 根挂 CloudSea 运行时组件（水 / 天气 / 生态 / 主光阴影；脚本另在
        /// PirateCrew.SceneArt.CloudSea 命名空间）。GetComponent 判重 → 幂等；组件挂在
        /// **prefab 根**而不是 FBX 子物体上，FBX 重导入不触碰本 prefab 的组件层。
        /// 海床高度格与气泡的投影开关每次都重写（它们随 FBX 网格变，不是手调值）。
        /// </summary>
        static void EnsureCloudSeaComponents(GameObject root, List<string> problems, Stats stats)
        {
            stats.ComponentsAdded += EnsureComponent<CloudSeaWater>(root);
            stats.ComponentsAdded += EnsureComponent<CloudSeaWeather>(root);
            stats.ComponentsAdded += EnsureComponent<CloudSeaLife>(root);
            stats.ComponentsAdded += EnsureComponent<CloudSeaSunShadow>(root);

            BakeBedGrid(root, problems);
            SetBubbleShadows(root);
        }

        /// <summary>
        /// 从 <c>Cloud_Sea_Bed</c> 的网格顶点烘海床高度格，写进 <see cref="CloudSeaLife"/>。
        /// 顶点先变换到 prefab 根的本地系（根的旋转缩放以后怎么变都不影响格子）。
        /// </summary>
        static void BakeBedGrid(GameObject root, List<string> problems)
        {
            MeshRenderer bed = FindRendererByName(root, CloudSeaPath.SeaBedObjectName);
            Mesh mesh = bed != null ? bed.GetComponent<MeshFilter>()?.sharedMesh : null;
            if (mesh == null)
            {
                problems.Add("找不到海床网格 " + CloudSeaPath.SeaBedObjectName
                    + "——海床高度格没烘，鱼群 / 螃蟹会退回固定深度带（可能扎进沙丘）。");
                return;
            }

            Matrix4x4 toRoot = root.transform.worldToLocalMatrix * bed.localToWorldMatrix;
            Vector3[] local = mesh.vertices;
            for (int i = 0; i < local.Length; i++)
                local[i] = toRoot.MultiplyPoint3x4(local[i]);

            float[] heights = CloudSeaBedGrid.Build(local, BedGridCells, BedGridHalfSize);
            root.GetComponent<CloudSeaLife>().SetBedHeightGrid(heights, BedGridCells, BedGridHalfSize);
        }

        /// <summary>
        /// 气泡柱不投影：水下物开了真投影之后，一串气泡会在海床上撒一圈黑点。
        /// 写在渲染器上（随 prefab 落盘），不靠运行时脚本每局去关。
        /// </summary>
        static void SetBubbleShadows(GameObject root)
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.gameObject.name.StartsWith(CloudSeaPath.BubblePrefix, System.StringComparison.Ordinal))
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static MeshRenderer FindRendererByName(GameObject root, string objectName)
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.gameObject.name == objectName)
                    return renderer;
            }
            return null;
        }

        static int EnsureComponent<T>(GameObject root) where T : Component
        {
            if (root.GetComponent<T>() != null)
                return 0;
            root.AddComponent<T>();
            return 1;
        }

        // ------------------------------------------------------------------
        // 口径复检（保存后的 prefab 资产）
        // ------------------------------------------------------------------

        /// <summary>
        /// 对保存后的 prefab 资产复检每个材质槽：槽上的 shader 必须是其对象的着色路线该有的那支
        /// （判据与换装同一份 <see cref="CloudSeaMaterialRoute"/>）。异物 shader = 那带像素内容
        /// 未定义 / 不进对应通道，混进来花屏且不报错（PixelartStageKit.AssertObjectShaderOnly
        /// 同一条硬门禁）。
        /// </summary>
        static void VerifySavedPrefab(List<string> problems)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                problems.Add("prefab 保存后读不回：" + PrefabPath);
                return;
            }

            int total = 0;
            int opaque = 0;
            int transparent = 0;
            int clearWater = 0;
            int seabed = 0;
            int underwater = 0;
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                CloudSeaRoute route = CloudSeaMaterialRoute.Of(renderer.name);
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material mat = slots[i];
                    if (mat == null)
                    {
                        problems.Add("prefab 里有空材质槽：" + renderer.name + "（槽 " + i + "）。");
                        continue;
                    }

                    total++;
                    string shaderName = mat.shader != null ? mat.shader.name : string.Empty;
                    switch (route)
                    {
                        case CloudSeaRoute.ClearWater:
                            if (shaderName == CloudSeaPath.ClearWaterShaderName && mat.renderQueue >= TransparentQueueStandard)
                                clearWater++;
                            else
                                problems.Add("海面 \"" + renderer.name + "\" 的材质 \"" + mat.name
                                    + "\" 口径不对（shader=" + shaderName + "，队列=" + mat.renderQueue + "），应为 "
                                    + CloudSeaPath.ClearWaterShaderName + " 且队列 ≥ " + TransparentQueueStandard + "。");
                            break;

                        case CloudSeaRoute.Seabed:
                            if (shaderName == CloudSeaPath.SeabedShaderName)
                                seabed++;
                            else
                                problems.Add("海床 \"" + renderer.name + "\" 的材质 \"" + mat.name
                                    + "\" 口径不对（" + shaderName + "），应为 " + CloudSeaPath.SeabedShaderName + "。");
                            break;

                        case CloudSeaRoute.Underwater:
                            if (shaderName == CloudSeaPath.UnderwaterShaderName)
                                underwater++;
                            else
                                problems.Add("水下件 \"" + renderer.name + "\" 的材质 \"" + mat.name
                                    + "\" 口径不对（" + shaderName + "），应为 " + CloudSeaPath.UnderwaterShaderName + "。");
                            break;

                        default:
                            if (TransparentNames.Contains(mat.name))
                            {
                                if (shaderName == CloudSeaPath.WaterShaderName
                                    && mat.renderQueue >= PixelartPath.TransparentQueueMin)
                                    transparent++;
                                else
                                    problems.Add("透明件 \"" + mat.name + "\" 口径不对（shader=" + shaderName
                                        + "，队列=" + mat.renderQueue + "），应为 " + CloudSeaPath.WaterShaderName
                                        + " 且队列 ≥ " + PixelartPath.TransparentQueueMin + "。");
                            }
                            else if (shaderName == PixelartPath.ObjectShaderName)
                            {
                                opaque++;
                            }
                            else
                            {
                                problems.Add("异物 shader：\"" + renderer.name + "\" 挂 \"" + mat.name
                                    + "\"（" + shaderName + "）——不进 G-buffer，那带像素内容未定义。");
                            }
                            break;
                    }
                }
            }

            Debug.Log(LogTag + " 口径复检：" + total + " 个材质槽（物体 shader " + opaque
                + " / 透明趟 " + transparent + " / 海面 " + clearWater + " / 海床 " + seabed
                + " / 水下生态 " + underwater + "）。");
        }

        // ------------------------------------------------------------------
        // 场景接线（折叠态写 BattleRig.prefab 本体；展开态回退 Battle 场景）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把新 prefab 写进 <see cref="RuntimeSceneArt"/> 的 <c>cloudFieldPrefab</c>。
        /// 返回接线落点描述（日志用）；失败时问题进 <paramref name="problems"/>、返回 null。
        /// </summary>
        static string WireRuntimeSceneArt(List<string> problems)
        {
            GameObject cloudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (cloudPrefab == null)
            {
                problems.Add("接线失败：prefab 读不回 " + PrefabPath);
                return null;
            }

            // ① 折叠态（现行契约）：组件居 BattleRig.prefab——写 prefab 本体，
            //    场景保持纯实例、不长覆盖（见类头【接线写在哪】）。
            GameObject rig = AssetDatabase.LoadAssetAtPath<GameObject>(BattleRigPath);
            RuntimeSceneArt rigArt = rig != null ? rig.GetComponentInChildren<RuntimeSceneArt>(true) : null;
            if (rigArt != null)
            {
                // 幂等：已指向则不改写（免得每次重跑都把整只 BattleRig.prefab 重新序列化一遍）。
                // prefab 覆盖保存后根 fileID 若变了，旧引用读出来是 null，这里自然走改写分支。
                if (ReadCloudFieldRef(rigArt) == cloudPrefab)
                    return BattleRigPath + " / RuntimeSceneArt.cloudFieldPrefab（已指向，未改写）";

                var contents = PrefabUtility.LoadPrefabContents(BattleRigPath);
                try
                {
                    RuntimeSceneArt inside = contents.GetComponentInChildren<RuntimeSceneArt>(true);
                    if (inside == null)
                    {
                        problems.Add("LoadPrefabContents(" + BattleRigPath + ") 里找不到 RuntimeSceneArt。");
                        return null;
                    }

                    var so = new SerializedObject(inside);
                    if (!SetPrefabRef(so, cloudPrefab, problems))
                        return null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(contents, BattleRigPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }

                // 读回验证：接线这类"默默不生效"的活，不读回等于没做。
                RuntimeSceneArt verify = AssetDatabase.LoadAssetAtPath<GameObject>(BattleRigPath)
                    .GetComponentInChildren<RuntimeSceneArt>(true);
                if (ReadCloudFieldRef(verify) != cloudPrefab)
                {
                    problems.Add("BattleRig.prefab 接线读回验证失败：cloudFieldPrefab 未指向 " + PrefabPath);
                    return null;
                }
                return BattleRigPath + " / RuntimeSceneArt.cloudFieldPrefab（已改写并读回验证）";
            }

            // ② 回退：展开态（折叠前的旧形态）——打开 Battle 场景找组件接线后存盘。
            string battleScenePath = BuildScenes.PathOf("Battle");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(battleScenePath) == null)
            {
                problems.Add("找不到接线承接体：" + BattleRigPath + " 与 "
                    + battleScenePath + " 都没有 RuntimeSceneArt（先跑 BattleSceneSetup.BuildAll）。");
                return null;
            }

            var scene = EditorSceneManager.OpenScene(battleScenePath, OpenSceneMode.Single);
            RuntimeSceneArt sceneArt = Object.FindObjectOfType<RuntimeSceneArt>();
            if (sceneArt == null)
            {
                problems.Add("Battle 场景里没有 RuntimeSceneArt（先跑 BattleSceneSetup.BuildAll）。");
                return null;
            }

            if (ReadCloudFieldRef(sceneArt) == cloudPrefab)
                return battleScenePath + " / RuntimeSceneArt.cloudFieldPrefab（已指向，未改写）";

            var sceneSo = new SerializedObject(sceneArt);
            if (!SetPrefabRef(sceneSo, cloudPrefab, problems))
                return null;
            sceneSo.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return battleScenePath + " / RuntimeSceneArt.cloudFieldPrefab（已改写并存盘）";
        }

        static bool SetPrefabRef(SerializedObject so, GameObject prefab, List<string> problems)
        {
            SerializedProperty prop = so.FindProperty("cloudFieldPrefab");
            if (prop == null)
            {
                problems.Add("RuntimeSceneArt 找不到序列化字段 cloudFieldPrefab（字段名漂移？）。");
                return false;
            }
            prop.objectReferenceValue = prefab;
            return true;
        }

        static Object ReadCloudFieldRef(RuntimeSceneArt sceneArt)
        {
            if (sceneArt == null)
                return null;
            var so = new SerializedObject(sceneArt);
            SerializedProperty prop = so.FindProperty("cloudFieldPrefab");
            return prop != null ? prop.objectReferenceValue : null;
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            // CreateFolder 不建中间层级：逐级补（父目录一定已在 Assets/ 下）。
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }
    }
}
