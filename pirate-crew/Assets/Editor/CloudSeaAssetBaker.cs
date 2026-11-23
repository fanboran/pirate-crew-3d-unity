using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.EditorTools.BuildSystem;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.SceneArt.CloudSea;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 云端漫步整场件（<c>CloudWalk.fbx</c> → **自包含** <c>CloudWalk.prefab</c>）的编辑器烘焙器：
    /// FBX 内嵌材质按名换装成本路径材质资产 → prefab 根挂 <c>CloudSea</c> 运行时三件 →
    /// 把 prefab 接进 <see cref="RuntimeSceneArt"/> 的 <c>cloudFieldPrefab</c>，一次收口。
    /// 材质在 prefab 里已换好，运行时实例化不再依赖 <c>PixelartContentConverter</c> 补扫。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/烘焙/云端漫步场景件
    ///   无头: -batchmode -nographics -quit -executeMethod PirateCrew.EditorTools.CloudSeaAssetBaker.BuildAll
    ///
    /// 【前置】两样必须先在，缺一会编译不过/报错退出：
    ///   ① <c>tools/blender/scene/cloudsea/assemble_cloudsea.py</c> 出库的
    ///      <c>Assets/Art/Models/SceneKit/CloudWalk.fbx</c>（内嵌材质名 = style_tokens.py 的
    ///      <c>Cloud_*</c> 段）；② <c>PirateCrew.SceneArt.CloudSea</c> 命名空间下的
    ///      CloudSeaWater / CloudSeaWeather / CloudSeaLife 三个运行时组件（本文件直接
    ///      <c>AddComponent</c> 引用，不走反射）。
    ///
    /// 【材质规则（按源材质名分流，落 <see cref="MaterialFolder"/>）】
    ///   · 普通不透明：<see cref="PixelartMaterialFactory"/> 配方（物体 shader、色带 4 档、
    ///     抖动 0.21、法线内墨线、队列 2000），色取源材质 `_BaseColor`/`_Color`；
    ///   · `Cloud_Brass` / `Cloud_Gold`：在配方之上加金属件参数（`_Metallic` 0.8 /
    ///     `_Smoothness` 1 / 暖金边缘光）；大平面高光翻档的取舍见工厂 `Configure` 头注释——
    ///     金属件显式给非零值正是该口径的例外侧；
    ///   · `Cloud_Water` / `Cloud_WaterDeep` / `Cloud_Glass`：透明趟材质
    ///     （`PixelartPath.TransparentShaderName`）。透明趟是"覆盖层"语义：不吃色带/描边、
    ///     按透明队列画在合成结果之上（`PixelartTransparentFeature` 收件区间
    ///     [TransparentQueueMin, TransparentQueueMax]）——水面/玻璃要的是连续 alpha 混合，
    ///     不适合打成色带档，这是有意为之，不是漏配。alpha 取源材质色（Blender token 只有
    ///     hex、内嵌色 alpha 恒 1，故按名兜底 0.35 / 0.55 / 0.25）。
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

        /// <summary>烘焙产物（自包含 prefab：材质已换好、根挂 CloudSea 三件）。</summary>
        const string PrefabPath = "Assets/Art/Models/SceneKit/CloudWalk.prefab";

        /// <summary>云端漫步材质资产目录（按名一材质一文件，重跑复用/更新，幂等）。</summary>
        const string MaterialFolder = "Assets/Art/Materials/CloudSea";

        /// <summary>Battle 折叠载体（RuntimeSceneArt 现居其中；ScenePrefabCollapse 的折叠目标）。</summary>
        const string BattleRigPath = "Assets/Prefabs/PirateCrew/Battle/BattleRig.prefab";

        // ---- WaterLab 复刻水（2026-11-21 创始人裁决：海面换 water-lab 预研水）----
        /// <summary>模拟核 compute（实验室装配器同路径）。</summary>
        const string WaterLabComputePath = "Assets/Art/Shaders/WaterLab/WaterLabSim.compute";

        /// <summary>云海版双趟 ocean shader（Resources 持有，内部名取 Find）。</summary>
        const string WaterLabOceanShaderName = "PirateCrew/CloudSea/WaterLabOcean";

        /// <summary>ocean 网格资产（512²/30 m，与实验室装配同口径；prefab 不能内嵌网格）。</summary>
        const string OceanMeshPath = "Assets/Art/Models/SceneKit/CloudWalkOcean.asset";

        /// <summary>ocean 材质（细节法线接原作提取贴图）。</summary>
        const string OceanMaterialPath = "Assets/Art/Materials/CloudSea/Cloud_WaterLabOcean.mat";

        /// <summary>原作细节法线提取贴图（WaterLabSetup.EnsureRefNormalAsset 入库件）。</summary>
        const string DetailNormalPath = "Assets/Art/Materials/WaterLab/WaterNormal2D_Ref.png";

        /// <summary>ocean 子物体名（VerifySavedPrefab 按名放行异物 shader）。</summary>
        const string OceanObjectName = "Cloud_WaterLabOcean";

        /// <summary>
        /// 简版海面对象（mod_sea 的 Cloud_Sea_Surface）专用材质：与 Cloud_Water 同 shader
        /// 同色，但 `_WaterLabHole = 1`——WaterLab 域内整片 clip，让 ocean 全权接管水面。
        /// 2026-11-21 实拍：简版水与 ocean 同高，透明排序把大平面简版水排在 ocean 之后，
        /// 多轮 WaterLab 效果（焦散/折射/云影）全被它盖住；水洼/玻璃仍用 Cloud_Water。
        /// </summary>
        const string SurfaceObjectName = "Cloud_Sea_Surface";
        const string SurfaceMaterialPath = MaterialFolder + "/Cloud_WaterSurface.mat";

        /// <summary>透明趟材质（按名分流；PostLineMaterialSwapper 只认物体 shader 材质，这三件天然不在换装列）。</summary>
        static readonly HashSet<string> TransparentNames = new HashSet<string>
        {
            "Cloud_Water", "Cloud_WaterDeep", "Cloud_Glass",
            // 海面专用挖洞材质（Cloud_Sea_Surface 在域内让位给 WaterLab ocean）
            "Cloud_WaterSurface",
        };

        /// <summary>金属件（配方之上加金属度/光滑度/暖金边缘光；其余材质保持工厂默认的非金属无边缘光）。</summary>
        static readonly HashSet<string> MetallicNames = new HashSet<string>
        {
            "Cloud_Brass", "Cloud_Gold",
        };

        /// <summary>
        /// 鱼专属色卡（配方之上开**背光边缘光**）：水下鱼体的着色落在 4 档量化的暗档
        /// （侧面法线背光 ⇒ band0 环境光），暗档里色相几乎不参与——换多亮的 albedo 都会被
        /// 60% 青水洗成灰青（首轮实拍）。边缘光趟（方向 = −主光）在鱼体轮廓勾一圈暖边，
        /// 色相从暗档里读出来（创始人 2026-11-21"针对每种物品一套色卡"的落地件）。
        /// </summary>
        static readonly HashSet<string> FishGlowNames = new HashSet<string>
        {
            "Cloud_FishOrange", "Cloud_FishYellow",
        };

        /// <summary>
        /// 透明件的 alpha 兜底表（源材质读不到 &lt;1 的 alpha 时按名取值；源材质显式带了
        /// alpha 则尊重源）。Blender 侧 style_tokens.py 只有 hex/roughness，FBX 内嵌色
        /// alpha 恒 1——这三个值就是透明度的定值口径，改透明度改这里。
        /// </summary>
        static readonly Dictionary<string, float> TransparentAlphaFallback = new Dictionary<string, float>
        {
            // 水面/水洼 0.60（2026-11-22 复核：WaterLab 域内水面接管后，域外简版水保持
            // 0.60 才与域内青染+焦散的平均色对齐；0.32 会让 30 m 域界读成一块深青方池）。
            // 棋盘感的根因是水波幅度，已由 waveStrength 0.22→0.06 去掉。
            { "Cloud_Water", 0.60f },
            { "Cloud_WaterDeep", 0.60f },
            { "Cloud_Glass", 0.25f },
        };

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
            Shader objectShader = null;
            Shader transparentShader = null;
            if (problems.Count == 0)
            {
                model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                if (model == null)
                    problems.Add("缺 " + ModelPath
                        + "（先跑 tools/blender/scene/cloudsea/assemble_cloudsea.py 出库）。");

                objectShader = PixelartShaders.Load(PixelartPath.ObjectShaderName);

                // 海水/水洼/玻璃走**双趟** shader：CellShade 有 PixelartTransparent 趟、
                // PostLine 有标准前向透明趟。不能再用单趟 PixelartTransparent——PostLine
                // 渲染器没有那个 LightMode 趟，水会整片消失（2026-11-11 烘焙器审计）。
                // 文件名是 CloudSeaWater.shader、内部名末段是 Water，不走 PixelartShaders.Load
                // 的“文件名=末段”约定；显式 Resources 路径 + Shader.Find 兜底。
                transparentShader = Resources.Load<Shader>(CloudSeaWaterMaterial.ShaderResourcePath);
                if (transparentShader == null)
                    transparentShader = PixelartShaders.Find(CloudSeaPath.WaterShaderName);
                if (objectShader == null)
                    problems.Add("物体 shader 摸不到：" + PixelartPath.ObjectShaderName + "，不透明件无法换装。");
                if (transparentShader == null)
                    problems.Add("水/玻璃 shader 摸不到：Resources/"
                        + CloudSeaWaterMaterial.ShaderResourcePath + " 与 "
                        + CloudSeaPath.WaterShaderName + " 都落空。");
            }

            var stats = new Stats();
            bool prefabSaved = false;
            if (problems.Count == 0)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
                root.name = "CloudWalk";

                SwapMaterials(root, objectShader, transparentShader, problems, stats);
                EnsureCloudSeaComponents(root, stats);

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
        // 材质换装
        // ------------------------------------------------------------------

        /// <summary>
        /// 逐 renderer 逐槽换装：按源材质名 Ensure（同源材质名只落一次盘），换过的 renderer 记数。
        /// 逐槽而不是只看第 0 槽：多材质 renderer 的其余子网格留在内嵌材质上就是"旧 shader +
        /// 未定义 G-buffer"的暗坑（PixelartContentConverter 同一条教训）。
        /// </summary>
        static void SwapMaterials(GameObject root, Shader objectShader, Shader transparentShader,
            List<string> problems, Stats stats)
        {
            EnsureFolder(MaterialFolder);

            // 源材质 → 烘焙材质（同源材质多 renderer 共享，只走一次 Ensure/落盘）
            var baked = new Dictionary<Material, Material>();

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                stats.Renderers++;
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null)
                        continue;

                    Material target;
                    if (renderer.gameObject.name == SurfaceObjectName && source.name == "Cloud_Water")
                    {
                        // 海面专用挖洞材质（域内让位给 WaterLab ocean，域外仍画简版水）。
                        target = EnsureSurfaceWaterMaterial(source, transparentShader, problems, stats);
                    }
                    else if (!baked.TryGetValue(source, out target))
                    {
                        target = EnsureMaterial(source, objectShader, transparentShader, problems, stats);
                        baked[source] = target;
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
        /// 按源材质名新建/就地更新云端漫步材质（幂等：重跑覆盖参数，源材质取色是唯一入色口）。
        /// 取色失败**先报错后落盘**——半配置的材质资产不落盘，下次跑干净重来。
        /// </summary>
        static Material EnsureMaterial(Material source, Shader objectShader, Shader transparentShader,
            List<string> problems, Stats stats)
        {
            string name = source.name;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Cloud_"))
            {
                problems.Add("FBX 内嵌材质 \"" + name + "\" 不带 Cloud_ 前缀"
                    + "（assemble_cloudsea.py 的 token 口径是 Cloud_*）——不换装，请核对出库脚本。");
                return null;
            }

            // 先取色（失败即返回，不落半配置资产）；后建/载资产。
            if (!PixelartMaterialFactory.TryGetAlbedo(source, out Color color))
            {
                problems.Add("Cloud 材质 \"" + name + "\" 取不到源色（_BaseColor/_Color 都没有）——本件保持未换装。");
                return null;
            }

            bool transparent = TransparentNames.Contains(name);
            string path = MaterialFolder + "/" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(transparent ? transparentShader : objectShader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
                stats.MaterialsCreated++;
            }
            else
            {
                stats.MaterialsUpdated++;
            }
            mat.name = name;

            if (transparent)
                ConfigureTransparent(mat, name, color, transparentShader);
            else
                ConfigureOpaque(mat, name, color, objectShader);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 不透明件配方：工厂 <see cref="PixelartMaterialFactory.Configure"/> 一份到底
        /// （色带 4 档 / 抖动 0.21 / 法线内墨线 / 队列 2000 / 像素吸附），逐物体参数只在
        /// 配方之上做加法。显式再写 `_MainLightLevel`/`_DitherStrength` 与工厂默认同值——
        /// 本烘焙器的定值口径不随工厂默认漂移而静默变化（工厂改默认必须在这里同步裁决）。
        /// </summary>
        static void ConfigureOpaque(Material mat, string name, Color albedo, Shader objectShader)
        {
            mat.shader = objectShader;

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

            if (FishGlowNames.Contains(name))
            {
                mat.SetColor("_RimLightColor", new Color(1f, 0.55f, 0.25f, 1f));
            }
        }

        /// <summary>
        /// 透明件配方：透明趟 shader + 队列落透明区间（`PixelartTransparentFeature` 的收件
        /// 区间下界即 TransparentQueueMin；复用了旧不透明资产时队列必须显式扳回来）。
        /// 有效乘色是 `_Color`（含 alpha），`_BaseColor` 同值镜像（见类头）。
        /// </summary>
        static void ConfigureTransparent(Material mat, string name, Color color, Shader transparentShader)
        {
            mat.shader = transparentShader;

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
        /// 海面专用材质（Cloud_Sea_Surface）：取色/透明配置与 Cloud_Water 同源，
        /// 额外写 `_WaterLabHole = 1`——shader 在 WaterLab 域内整片裁掉简版水，
        /// 水面由 ocean 全权渲染；域外照常画简版水兜底。幂等（资产复用、重跑覆盖参数）。
        /// </summary>
        static Material EnsureSurfaceWaterMaterial(Material source, Shader transparentShader,
            List<string> problems, Stats stats)
        {
            if (!PixelartMaterialFactory.TryGetAlbedo(source, out Color color))
            {
                problems.Add("海面材质 Cloud_Water 取不到源色——Cloud_Sea_Surface 无法换装。");
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(SurfaceMaterialPath);
            if (mat == null)
            {
                mat = new Material(transparentShader) { name = "Cloud_WaterSurface" };
                AssetDatabase.CreateAsset(mat, SurfaceMaterialPath);
                stats.MaterialsCreated++;
            }
            else
            {
                stats.MaterialsUpdated++;
            }

            ConfigureTransparent(mat, "Cloud_Water", color, transparentShader);
            mat.SetFloat("_WaterLabHole", 1f);
            UnityEditor.EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------
        // 运行时组件
        // ------------------------------------------------------------------

        /// <summary>
        /// prefab 根挂 CloudSea 运行时三件（水/天气/生态；脚本另在
        /// PirateCrew.SceneArt.CloudSea 命名空间）。GetComponent 判重 → 幂等；组件挂在
        /// **prefab 根**而不是 FBX 子物体上，FBX 重导入不触碰本 prefab 的组件层。
        /// </summary>
        static void EnsureCloudSeaComponents(GameObject root, Stats stats)
        {
            stats.ComponentsAdded += EnsureComponent<CloudSeaWater>(root);
            stats.ComponentsAdded += EnsureComponent<CloudSeaWeather>(root);
            stats.ComponentsAdded += EnsureComponent<CloudSeaLife>(root);
            stats.ComponentsAdded += EnsureWaterLabOcean(root);
        }

        /// <summary>
        /// WaterLab 复刻水三件套（幂等；2026-11-21 水体接入）：根上挂
        /// <see cref="CloudSeaWaterLab"/>（海床地形源 + 域配置）与
        /// <see cref="WaterLabSimulation"/>（模拟核，terrainProvider 指向前者、
        /// domainCenter 同步），加 90 m ocean 网格子物体（<see cref="OceanMeshPath"/>
        /// 网格 + <see cref="OceanMaterialPath"/> 材质，摆在水面上）。
        /// 旧件退役口径：域外简版水（Cloud_Sea_Surface）**保留** everywhere——ocean 90 m 网格
        /// 盖住整张可见海面（α 0.78 半透），简版水只做 90 m 外的远景兜底。
        /// </summary>
        static int EnsureWaterLabOcean(GameObject root)
        {
            // 焦散体绑定是幂等的，必须在「已接好就跳过」之前做：已有 prefab 的 ocean
            // 材质资产也要补绑（材质是复用资产，不随 prefab 重建）。
            BindOceanCaustics();

            if (root.GetComponent<global::PirateCrew.WaterLab.WaterLabSimulation>() != null)
                return 0;   // 已接好：不重建（网格/材质资产更新走手工重烘）。

            var waterLab = root.AddComponent<CloudSeaWaterLab>();

            var sim = root.AddComponent<global::PirateCrew.WaterLab.WaterLabSimulation>();
            sim.computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(WaterLabComputePath);
            if (sim.computeShader == null)
                throw new System.InvalidOperationException(LogTag + " 摸不到模拟核 compute："
                    + WaterLabComputePath + "（water-lab 移植件未入库？）。");
            sim.terrainProvider = waterLab;
            sim.domainCenter = waterLab.DomainCenter;
            sim.enablePhytoplankton = false;   // 战斗海面无夜光浮游生物；不注入也不报错
            sim.tideAmplitudeScale = 0.25f;    // 浅海水柱 0.35–0.75 m：潮位收幅，水位稳定（场景适配）

            // ---- ocean 网格（512² / **90 m** 见方，盖住相机可见海面 ±43 m 足迹）----
            // 模拟域仍是 30 m（WaterLabRules.DomainSize，复刻参数不动）：网格超出域的部分
            // 由 shader 钳位采样域缘 η/地形 → 平水延展；焦散/云影按世界坐标继续铺。
            // 这样整个可见海面都是同一张 WaterLab 水，不再有 30 m 方形域界。
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OceanMeshPath);
            if (mesh == null)
            {
                const int segments = 512;
                const float oceanHalfSize = 45f;   // 90 m 见方
                int verts = segments + 1;
                float step = oceanHalfSize * 2f / segments;
                mesh = new Mesh { name = "CloudWalkOcean" };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var vertices = new Vector3[verts * verts];
                for (int z = 0; z < verts; z++)
                    for (int x = 0; x < verts; x++)
                        vertices[x + z * verts] = new Vector3(
                            -oceanHalfSize + x * step, 0f,
                            -oceanHalfSize + z * step);
                var triangles = new int[segments * segments * 6];
                int t = 0;
                for (int z = 0; z < segments; z++)
                {
                    for (int x = 0; x < segments; x++)
                    {
                        int i0 = x + z * verts;
                        int i1 = i0 + 1;
                        int i2 = i0 + verts;
                        int i3 = i2 + 1;
                        triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                        triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                    }
                }
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, OceanMeshPath);
            }

            // ---- ocean 材质（细节法线 = 原作提取贴图）----
            var material = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(WaterLabOceanShaderName);
                if (shader == null)
                    throw new System.InvalidOperationException(LogTag + " 摸不到 ocean shader："
                        + WaterLabOceanShaderName + "（Resources/PixelartShaders/CloudSeaWaterLabOcean.shader 未导入？）。");
                material = new Material(shader) { name = "Cloud_WaterLabOcean" };
                var detail = AssetDatabase.LoadAssetAtPath<Texture2D>(DetailNormalPath);
                if (detail != null)
                    material.SetTexture("_WaterLabDetailNormal", detail);
                else
                    Debug.LogWarning(LogTag + " 原作细节法线贴图缺失（" + DetailNormalPath
                        + "），水纹细节退化为灰常数——先跑 WaterLabSetup 自愈再重烘。");
                AssetDatabase.CreateAsset(material, OceanMaterialPath);
            }

            // 焦散体（库内 Demo 的 3D 焦散，WaterLabSand 同源）：ocean shader 把它加到
            // 折射底图（水下沙面）上——在方法开头已统一补绑，见 BindOceanCaustics()。

            var ocean = new GameObject(OceanObjectName);
            ocean.transform.SetParent(root.transform, false);
            ocean.transform.localPosition = new Vector3(
                waterLab.DomainCenter.x, waterLab.OceanMeshWorldY, waterLab.DomainCenter.y);
            ocean.AddComponent<MeshFilter>().sharedMesh = mesh;
            var oceanRenderer = ocean.AddComponent<MeshRenderer>();
            oceanRenderer.sharedMaterial = material;
            oceanRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 水面不投影（实验室装配同口径）
            return 2;   // 组件：CloudSeaWaterLab + WaterLabSimulation（子物体不计）
        }

        /// <summary>
        /// ocean 材质补绑库内 3D 焦散体（WaterLabSand 同源资产，由
        /// <see cref="global::PirateCrew.EditorTools.WaterLabCausticsImport"/> 自愈生成）。
        /// 幂等：资产已存在就不重建，材质已绑就不写盘。
        /// </summary>
        static void BindOceanCaustics()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(OceanMaterialPath);
            if (material == null)
                return;
            var caustics = global::PirateCrew.EditorTools.WaterLabCausticsImport.EnsureCausticsAsset();
            if (caustics != null && material.GetTexture("_CausticsTex") != caustics)
            {
                material.SetTexture("_CausticsTex", caustics);
                UnityEditor.EditorUtility.SetDirty(material);
            }

            // 沙地噪声图（原作 sandMap，WaterLabSetup 已自愈落盘）：焦散体采样的低频相位源。
            const string sandNoisePath = "Assets/Art/Textures/WaterLab/SandNoise_Ref.png";
            var sandMap = AssetDatabase.LoadAssetAtPath<Texture2D>(sandNoisePath);
            if (sandMap != null && material.GetTexture("_WaterLabSandMap") != sandMap)
            {
                material.SetTexture("_WaterLabSandMap", sandMap);
                UnityEditor.EditorUtility.SetDirty(material);
            }
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
        /// 对保存后的 prefab 资产复检每个材质槽：透明件必须走透明趟 shader 且队列落透明区间，
        /// 其余槽必须是物体 shader（不进 G-buffer 的槽 = 该带像素内容未定义，混进来花屏且无报错，
        /// PixelartStageKit.AssertObjectShaderOnly 同一条硬门禁）。
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
            foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
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
                    if (renderer.name == OceanObjectName)
                    {
                        // WaterLab ocean 子物体：双趟透明 shader（overlay/前向收件），
                        // 不进 Cast G-buffer（透明队列被 Cast 渲染器的 opaque 过滤挡住）。
                        if (shaderName == "PirateCrew/CloudSea/WaterLabOcean")
                            transparent++;
                        else
                            problems.Add("ocean 子物体 \"" + renderer.name + "\" shader 口径不对（"
                                + shaderName + "），应为 PirateCrew/CloudSea/WaterLabOcean。");
                    }
                    else if (TransparentNames.Contains(mat.name))
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
                }
            }

            Debug.Log(LogTag + " 口径复检：" + total + " 个材质槽（物体 shader " + opaque
                + " / 透明趟 " + transparent + "）。");
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
