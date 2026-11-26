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
        ///   · Underwater（海草 / 岩石 / 珊瑚 / 沉积物 / 气泡 / 水下生物 Cloud_Creature_*）：水下生态 shader
        ///     （平面两档卡通色；不描边——没有深度 / 法线通道，PostLine 的描边判据看不到它们）。
    ///   同一个源材质名被多条路线共用时（珊瑚色既是海底的珊瑚也是海鸥的喙），默认路线保留原名、
    ///   其余路线的资产名加路线后缀（<c>Cloud_Coral_Underwater</c>）。
    ///
    /// 【海面族材质的参数归谁】海床 / 水下 / 海面三支 shader 的材质只由本烘焙器写「shader +
    /// 固有色 + 路线开关」，其余参数（沙纹、焦散、水色、摆幅……）是材质资产上的手调值，重跑
    ///   不覆盖——调参在材质上改，不必改这里。
    ///
    /// 【海床高度格】从 <c>Cloud_Sea_Bed</c> 网格顶点烘两张方格（<see cref="BedGridCells"/>）
    ///   写进 <see cref="CloudSeaLife"/>：运行时游泳的生物查净空格贴着沙丘起伏定深，贴底的生物查
    ///   贴地格落脚（<see cref="CloudSeaBedGrid"/>）。
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
        /// 活物的「不能穿线」宁可高一线。可视区里的水下生物锚点都在覆盖范围内。
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
        /// 水下会摇曳的植物材质：开薄片双面受光 + 梢部摆幅。这些材质的网格带「离根高度比」UV（Blender 侧
        /// kit_common.Mesher 的逐顶点辅助量）。水下生物的网格另有三套 UV 携带肢体动画数据（CloudSeaAnim.hlsl）；
        /// 没有任何 UV 的水下网格（岩石 / 珊瑚）位移恒 0。
        /// </summary>
        static readonly HashSet<string> SwayingPlantNames = new HashSet<string>
        {
            "Cloud_Seaweed", "Cloud_Plant",
        };

        /// <summary>
        /// 水下两档色的乘色（sRGB）：这是口径值、不跟手调走——受光面近原色略暖，背光面偏冷不压死，
        /// 影区再乘 shader 的影区乘色。鳍 / 眼白 / 瞳孔这类纯平件把背光面乘色也配成受光面的值。
        /// </summary>
        static readonly Color UnderwaterHighlight = new Color(1.04f, 1.02f, 0.96f, 1f);
        static readonly Color UnderwaterShadow = new Color(0.68f, 0.74f, 0.92f, 1f);

        /// <summary>
        /// 材质名前缀 → 水下材质的薄片 / 纯平口径（Blender 侧 style_tokens 的槽名）：
        /// `Cloud_Fin` 鳍类薄片：双面受光（深度法线通道里写朝上法线，折缝处不出棱线）+ 纯平色；
        /// `Cloud_Eye` / `Cloud_Ink` 眼白 / 瞳孔 / 花纹描边：纯平色。
        /// </summary>
        const string FinMaterialPrefix = "Cloud_Fin";
        static readonly string[] FlatMaterialPrefixes = { "Cloud_Fin", "Cloud_Eye", "Cloud_Ink" };

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
                VerifyCreatureAxes(root, problems);
                VerifyCreatureJoints(root, problems);

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
        /// 水下生态材质：固有色 = 源色；两档乘色是口径值，每次重写。会摇曳的植物开薄片
        /// 双面受光与梢部摆幅；鳍 / 眼按名走薄片 / 纯平口径（见 <see cref="FinMaterialPrefix"/>）。
        /// </summary>
        static void ConfigureUnderwater(Material mat, string sourceName, Color color, Shaders shaders)
        {
            mat.shader = shaders.Underwater;
            color.a = 1f;
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_HighlightColor", UnderwaterHighlight);
            mat.SetColor("_ShadowColor", UnderwaterShadow);
            mat.SetFloat("_BandCut", 0.5f);
            mat.SetFloat("_Wrap", 0.25f);
            mat.SetFloat("_TwoSidedLight", 0f);
            mat.SetFloat("_SwayAmplitude", 0f);
            mat.renderQueue = -1;   // 跟 shader 标签走（Geometry）

            if (SwayingPlantNames.Contains(sourceName))
            {
                mat.SetFloat("_TwoSidedLight", 1f);
                mat.SetFloat("_SwayAmplitude", 0.10f);
            }
            if (sourceName.StartsWith(FinMaterialPrefix, System.StringComparison.Ordinal))
                mat.SetFloat("_TwoSidedLight", 1f);
            foreach (string prefix in FlatMaterialPrefixes)
            {
                if (sourceName.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    mat.SetColor("_ShadowColor", UnderwaterHighlight);
                    break;
                }
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
                    + "——海床高度格没烘，水下生物会退回固定深度带（可能扎进沙丘）。");
                return;
            }

            Matrix4x4 toRoot = root.transform.worldToLocalMatrix * bed.localToWorldMatrix;
            Vector3[] local = mesh.vertices;
            for (int i = 0; i < local.Length; i++)
                local[i] = toRoot.MultiplyPoint3x4(local[i]);

            float[] heights = CloudSeaBedGrid.Build(local, BedGridCells, BedGridHalfSize);
            float[] ground = CloudSeaBedGrid.BuildGround(local, BedGridCells, BedGridHalfSize);
            root.GetComponent<CloudSeaLife>().SetBedHeightGrid(heights, BedGridCells, BedGridHalfSize, ground);
        }

        /// <summary>
        /// 水下生物的变换口径（硬门禁）：本地旋转必须是**纯偏航**、缩放必须是**等比**。
        /// 关节矩阵按「物体空间 x = 前、y = 上、z = 侧、米」写，运行时也直接用「绕 Y 的欧拉角」给它定
        /// 朝向——导入器一旦在对象上留下别的轴向后（比如改了 bakeAxisConversion），鱼会侧着躺、
        /// 鳍绕着错的方向拍，而且不报错。所以在这里拦。顺带打印一行变换抽样，核对导入后的实际数值用。
        /// </summary>
        static void VerifyCreatureAxes(GameObject root, List<string> problems)
        {
            int checkedCount = 0;
            foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (!tr.name.StartsWith(CloudSeaPath.CreaturePrefix, System.StringComparison.Ordinal))
                    continue;
                checkedCount++;
                Vector3 up = tr.localRotation * Vector3.up;
                Vector3 scale = tr.localScale;
                bool yawOnly = up.y > 0.999f && Mathf.Abs(up.x) < 0.05f && Mathf.Abs(up.z) < 0.05f;
                bool uniform = Mathf.Abs(scale.x - scale.y) < 1e-3f && Mathf.Abs(scale.x - scale.z) < 1e-3f;
                if (!yawOnly || !uniform)
                {
                    problems.Add("水下生物 \"" + tr.name + "\" 的变换不是「纯偏航 + 等比缩放」（欧拉角 "
                        + tr.localEulerAngles + "，缩放 " + scale + "）——顶点着色器的肢体形变与运行时的朝向"
                        + "都按物体空间 x 前 / y 上 / z 侧写，这样的变换会让它们作用在错的轴上。");
                }
                if (checkedCount == 1)
                {
                    MeshFilter filter = tr.GetComponent<MeshFilter>();
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    MeshRenderer sample = tr.GetComponent<MeshRenderer>();
                    string channels = mesh == null
                        ? "无网格"
                        : (mesh.uv.Length > 0 ? "uv0" : "") + (mesh.uv2.Length > 0 ? " uv2" : "")
                            + (mesh.uv3.Length > 0 ? " uv3" : "");
                    Debug.Log(LogTag + " 生物变换抽样 " + tr.name + "：欧拉角 " + tr.localEulerAngles
                        + "，缩放 " + scale + "，位置 " + tr.localPosition + "，世界包围盒尺寸 "
                        + (sample != null ? sample.bounds.size.ToString() : "无渲染器")
                        + "，网格 UV 通道 " + channels + "。");
                }
            }
            Debug.Log(LogTag + " 水下生物变换核对 " + checkedCount + " 个。");
        }

        /// <summary>
        /// 水下生物骨架的硬门禁（Blender ↔ Unity 的蒙皮契约，creature_kit.py ↔ CloudSeaRig）：
        ///   · 每只生物对象下要有命名 <c>J&lt;关节号&gt;_…</c> 的关节空物体，位置 = 米级的静止位置、无旋转缩放；
        ///   · 网格三套 UV 齐全：uv / uv2 是（关节号, 权重），uv3.x 是蒙皮标记；每个顶点的关节号必须是这只
        ///     生物自己的关节、权重和为 1；
        ///   · 全场景的关节号不能有两只生物重号（缓冲按关节号寻址）；父关节（名字里的 <c>P&lt;号&gt;</c>）
        ///     必须是同一只生物里排在它之前的关节。
        /// 运行时按同一套读法解骨架——这里不拦，错了的表现是鱼头和身体分家、或者两只生物互相带动。
        /// </summary>
        static void VerifyCreatureJoints(GameObject root, List<string> problems)
        {
            var owners = new Dictionary<int, string>();
            int creatures = 0, joints = 0, weighted = 0;
            foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (!tr.name.StartsWith(CloudSeaPath.CreaturePrefix, System.StringComparison.Ordinal))
                    continue;
                creatures++;

                var own = new List<KeyValuePair<int, Transform>>();
                foreach (Transform child in tr)
                {
                    if (!child.name.StartsWith("J", System.StringComparison.Ordinal))
                        continue;
                    int underscore = child.name.IndexOf('_');
                    int index;
                    if (underscore <= 1
                        || !int.TryParse(child.name.Substring(1, underscore - 1), out index) || index < 0)
                    {
                        problems.Add("水下生物 " + tr.name + " 身下的关节空物体名字不是 J<关节号>_…：" + child.name);
                        continue;
                    }
                    own.Add(new KeyValuePair<int, Transform>(index, child));
                }
                if (own.Count == 0)
                {
                    problems.Add("水下生物 " + tr.name + " 身下没有关节空物体（J<关节号>_…）——Blender 侧没给"
                        + "它登记关节（creature_kit.bind_joints），它的骨架驱动不了。");
                    continue;
                }
                own.Sort((a, b) => a.Key.CompareTo(b.Key));

                var seen = new HashSet<int>();
                foreach (KeyValuePair<int, Transform> joint in own)
                {
                    int index = joint.Key;
                    Transform empty = joint.Value;
                    string owner;
                    if (owners.TryGetValue(index, out owner) && owner != tr.name)
                        problems.Add("关节号 " + index + " 被 " + owner + " 和 " + tr.name + " 重号（蒙皮缓冲按关节号寻址）。");
                    owners[index] = tr.name;
                    seen.Add(index);
                    joints++;

                    string rest = empty.name.Substring(empty.name.IndexOf('_') + 1);
                    if (rest.StartsWith("P", System.StringComparison.Ordinal))
                    {
                        int next = rest.IndexOf('_');
                        int parent;
                        if (next <= 1 || !int.TryParse(rest.Substring(1, next - 1), out parent) || !seen.Contains(parent))
                            problems.Add("水下生物 " + tr.name + " 的关节 " + empty.name + " 的父关节不是同一只生物里"
                                + "排在它之前的关节。");
                    }
                    if (Quaternion.Angle(empty.localRotation, Quaternion.identity) > 0.01f
                        || (empty.localScale - Vector3.one).magnitude > 0.001f || empty.localPosition.magnitude > 64f)
                        problems.Add("水下生物 " + tr.name + " 的关节 " + empty.name + " 的变换不是「纯平移、米级」："
                            + "位置 " + empty.localPosition + "，欧拉角 " + empty.localEulerAngles + "，缩放 " + empty.localScale
                            + "——Blender 导出嵌套 / 带旋转缩放的空物体会换一套单位约定。");
                }

                MeshFilter filter = tr.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                    continue;
                Vector2[] a = mesh.uv;
                Vector2[] b = mesh.uv2;
                Vector2[] marker = mesh.uv3;
                if (a.Length != mesh.vertexCount || b.Length != mesh.vertexCount || marker.Length != mesh.vertexCount)
                {
                    problems.Add("水下生物 " + tr.name + " 的网格三套 UV 不齐（uv " + a.Length + " / uv2 "
                        + b.Length + " / uv3 " + marker.Length + "，顶点 " + mesh.vertexCount + "）。");
                    continue;
                }
                int faults = 0;
                for (int i = 0; i < mesh.vertexCount && faults < 3; i++)
                {
                    string fault = null;
                    int ia = Mathf.RoundToInt(a[i].x);
                    int ib = Mathf.RoundToInt(b[i].x);
                    float wa = a[i].y;
                    float wb = b[i].y;
                    if (marker[i].x < 0.5f)
                        fault = "没有蒙皮标记";
                    else if (Mathf.Abs(a[i].x - ia) > 0.01f || Mathf.Abs(b[i].x - ib) > 0.01f)
                        fault = "关节号不是整数（" + a[i].x + ", " + b[i].x + "）——导入时 UV 精度丢了";
                    else if (!seen.Contains(ia) || (wb > 1e-4f && !seen.Contains(ib)))
                        fault = "引用了不属于这只生物的关节（" + ia + ", " + ib + "）";
                    else if (wa < -1e-3f || wb < -1e-3f || Mathf.Abs(wa + wb - 1f) > 0.02f)
                        fault = "权重和不为 1（" + wa + " + " + wb + "）";
                    if (fault != null)
                    {
                        faults++;
                        problems.Add("水下生物 " + tr.name + " 的第 " + i + " 个顶点" + fault + "。");
                    }
                }
                weighted += mesh.vertexCount;
            }
            Debug.Log(LogTag + " 水下生物骨架核对：" + creatures + " 只、" + joints + " 个关节、" + weighted
                + " 个带权顶点。");
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
