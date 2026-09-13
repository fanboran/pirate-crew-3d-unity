using System;
using System.IO;
using PirateCrew.PirateCrew.Fx;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 特效程序化贴图与材质的**编辑器落盘器**（幂等）。
    ///
    /// 【为什么需要落盘】运行时（`FxTextures` / `FxMaterials`）走内存生成，保证"开箱即用、不依赖资产"；
    /// 但一个正经游戏项目里特效贴图必须作为**可见、可审查、可被美术覆盖**的资产存在于工程里，
    /// 并且**把 FX shader 带进构建**（Unity 只把被引用的 shader 编进包）。本类就是那一步。
    ///
    /// 【单一算法来源】贴图像素来自运行时同一份 <see cref="FxTextureRules.CreatePixels"/>，
    /// 材质参数来自同一张 <see cref="FxMaterials"/> 规格表 —— 编辑器生成结果与运行时内存结果**逐像素一致**，
    /// 不会出现"编辑器里调好的和跑起来的不一样"。
    ///
    /// 【幂等】再次执行时：
    ///   · 贴图：字节与磁盘上一致则**不重写文件**（避免无谓的重新导入），但仍会校正 TextureImporter 设置；
    ///   · 材质：已存在的 .mat 就地更新属性（不新建、不删除，保持 GUID 稳定）。
    /// 因此本脚本可以放心反复跑，也可以作为 CI 的资产校验步骤。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Fx/生成特效贴图与材质（幂等）
    ///         PirateCrew/Fx/强制重建特效贴图与材质
    ///   无头: -batchmode -nographics -quit
    ///         -executeMethod PirateCrew.EditorTools.FxAssetBuilder.BuildAll
    ///
    /// 【产物】
    ///   Assets/Art/Textures/Fx/*.png        8 张（SoftCircle/Spark/Star4/Smoke/Droplet/WoodShard/Ring/FineSpark）
    ///   Assets/Art/Materials/Fx/*.mat       16 个（见 FxMaterials 规格表）
    ///   ProjectSettings/GraphicsSettings    把两个 FX shader 加进 Always Included Shaders（尽力而为）
    ///
    /// 【导入设置写死在哪里】见 <see cref="ConfigureImporter"/>：形状/噪声类 sRGB + alphaIsTransparency +
    /// Clamp + 无 mipmap + Uncompressed；过滤方式按 <see cref="FxTextureRules.DefaultFilter"/>
    /// （火花/木屑用 Point，其余 Bilinear）。
    /// </summary>
    public static class FxAssetBuilder
    {
        const string TextureFolder = "Assets/Art/Textures/Fx";
        const string MaterialFolder = "Assets/Art/Materials/Fx";

        // ------------------------------------------------------------------
        // 爆炸亮度压制（r4 工单 P1-3：explosion-moment 死白）
        // ------------------------------------------------------------------
        //
        // 【问题】r4 实测 explosion-moment 里 V>0.99 的死白像素占 5.2%（108,209px），读作「糊掉的 bloom」，
        // 看不出火球结构。根因是爆炸 core/fire 都是加法混合（Additive）、Tint 已接近白/亮橙，
        // 再叠 >1 的 _Intensity 后峰值远超 Bloom threshold(1.05)，整块过曝成白。
        // 【改法】生成 .mat 时把 core/fire 的 Tint 与 _Intensity 同步乘一个 <1 的系数，让峰值落回
        // Bloom threshold 附近；两层相对关系保持不变——core 系数 0.60（−40%）、fire 系数 0.70（−30%），
        // 于是 core 仍是「实心亮核」、fire 仍是「略暗外环」，不会糊成一片。
        //   旧 core：Tint #FFD9A8 × 1.70 → 新：× 0.60（Tint 各通道 ×0.60、Intensity 1.70→1.02）
        //   旧 fire：Tint #FF7A1A × 1.15 → 新：× 0.70（Tint 各通道 ×0.70、Intensity 1.15→0.81）
        //
        // 【⚠ 边界（重要）】本工程运行期特效材质**不走这批 .mat**，而是
        // <c>FxMaterials.Get()</c> 按 <c>FxMaterials.Specs</c> 表在内存里新建（Specs 才是运行期唯一来源，
        // 见 FxMaterials 类头）。本类只负责"资产可见/可审查 + 把 FX shader 带进构建"。
        // 因此本次改动**只改了烘焙出的 .mat 资产**；要让 r4 截图真正变暗，还须在
        // <c>Assets/Scripts/PirateCrew/Fx/FxMaterials.cs</c> 的 Specs 表里给这两档同步乘同样的系数
        // —— 该文件不在本波次文件域内，已在交付报告「未尽事项」登记。
        //
        // 【两层结构】「实心核（亮）+ 外环（暗）」在 ExplosionFx.Play 里已由两个粒子系统承载
        // （ExplosionCore = 短/亮/内层，ExplosionFire = 长/广/外层）；本轮只调亮度不改系统结构
        // （FxParticles 生成参数不在 FxAssetBuilder 可调范围，见报告）。
        // ------------------------------------------------------------------

        /// <summary>爆炸核心材质亮度系数（−40%）：保证 core 仍比 fire 亮，维持「亮核 + 暗环」。</summary>
        const float ExplosionCoreBrightnessScale = 0.60f;

        /// <summary>爆炸外焰材质亮度系数（−30%）。</summary>
        const float ExplosionFireBrightnessScale = 0.70f;

        // ------------------------------------------------------------------
        // 菜单入口
        // ------------------------------------------------------------------

        [MenuItem("PirateCrew/Fx/生成特效贴图与材质（幂等）", priority = 40)]
        public static void BuildAll()
        {
            Build(force: false);
        }

        [MenuItem("PirateCrew/Fx/强制重建特效贴图与材质", priority = 41)]
        public static void RebuildAll()
        {
            Build(force: true);
        }

        // ------------------------------------------------------------------
        // 主流程（-executeMethod 目标）
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成/校正全部特效资产。`-executeMethod` 用的就是这个方法。
        /// 任何单个资产失败只记 Warning、继续处理其余资产，最后统一 Refresh 并汇总日志，
        /// 不抛异常中断 batchmode（否则 -quit 会以非 0 退出码结束，难以在 CI 里判读）。
        ///
        /// 【刻意不用 StartAssetEditing/StopAssetEditing】该区间会**延迟导入**，而本类需要逐张
        /// 调 <c>TextureImporter.SaveAndReimport()</c> 把导入设置立刻落实（否则下次读 importer 拿到的
        /// 还是旧设置）。24 个两三 KB 的小资产，导入耗时可忽略，正确性优先。
        /// </summary>
        static void Build(bool force)
        {
            int textureWritten = 0;
            int materialWritten = 0;
            int failed = 0;

            EnsureFolder("Assets/Art");
            EnsureFolder("Assets/Art/Textures");
            EnsureFolder(TextureFolder);
            EnsureFolder("Assets/Art/Materials");
            EnsureFolder(MaterialFolder);

            for (int i = 0; i < FxTextureRules.All.Length; i++)
            {
                if (GenerateTexture(FxTextureRules.All[i], force))
                    textureWritten++;
                else
                    failed++;
            }

            for (int i = 0; i < FxMaterials.Count; i++)
            {
                if (GenerateMaterial((FxMaterial)i))
                    materialWritten++;
                else
                    failed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int shadersRegistered = RegisterAlwaysIncludedShaders();

            Debug.Log("[FxAssetBuilder] 特效资产生成完成：贴图 " + textureWritten + " / 材质 " + materialWritten
                + " 张，失败 " + failed + " 项，Always Included Shaders 新登记 " + shadersRegistered + " 个。"
                + (force ? "（强制重建）" : "（幂等：与磁盘一致的文件未重写）"));
        }

        // ------------------------------------------------------------------
        // 贴图
        // ------------------------------------------------------------------

        /// <summary>生成一张贴图；返回 true = 成功。已存在且字节一致时不重写文件。</summary>
        static bool GenerateTexture(FxTextureKind kind, bool force)
        {
            string assetPath = TextureFolder + "/" + kind + ".png";
            string absolutePath = ToAbsolutePath(assetPath);

            int size = FxTextureRules.DefaultSize(kind);
            byte[] png;
            try
            {
                Color32[] pixels = FxTextureRules.CreatePixels(kind, size);
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FxAssetBuilder] 生成贴图 " + kind + " 失败：" + e.Message);
                return false;
            }

            if (png == null || png.Length == 0)
            {
                Debug.LogWarning("[FxAssetBuilder] 贴图 " + kind + " 编码结果为空。");
                return false;
            }

            if (force || !BytesEqual(absolutePath, png))
            {
                File.WriteAllBytes(absolutePath, png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            ConfigureImporter(assetPath, kind, size);
            return true;
        }

        /// <summary>
        /// 写死导入设置。要点：
        ///   · <c>alphaIsTransparency</c>：避免 Unity 对带 alpha 的图做"边缘变黑"的预乘处理；
        ///   · <c>mipmapEnabled = false</c>：特效是屏占比很大的近景贴片，mip 只会让火花发糊；
        ///   · <c>wrapMode = Clamp</c>：粒子贴图绝不能平铺（边缘会出现接缝亮点）；
        ///   · <c>Uncompressed</c>：全部 8 张合计 &lt; 200KB，用 BC 压缩会给出块状伪影；
        ///   · 过滤方式按种类：硬边（火花/木屑）用 Point，软边用 Bilinear。
        /// </summary>
        static void ConfigureImporter(string assetPath, FxTextureKind kind, int size)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning("[FxAssetBuilder] 无法读取 TextureImporter：" + assetPath
                    + "，导入设置未生效（贴图本身已生成）。");
                return;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FxTextureRules.DefaultFilter(kind);
            importer.anisoLevel = 0;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(size));
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;

            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------
        // 材质
        // ------------------------------------------------------------------

        /// <summary>生成/就地更新一个材质；返回 true = 成功。</summary>
        static bool GenerateMaterial(FxMaterial material)
        {
            FxMaterialSpec spec = FxMaterials.SpecOf(material);
            if (string.IsNullOrEmpty(spec.Name))
                return false;

            Shader shader = Shader.Find(spec.Additive
                ? FxMaterials.AdditiveShaderName
                : FxMaterials.AlphaShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[FxAssetBuilder] 找不到 shader："
                    + (spec.Additive ? FxMaterials.AdditiveShaderName : FxMaterials.AlphaShaderName)
                    + "（材质 " + spec.Name + " 跳过）。请确认 Assets/Art/Shaders/Fx/ 下 shader 编译无错。");
                return false;
            }

            string assetPath = MaterialFolder + "/" + spec.Name + ".mat";
            var asset = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            bool created = false;
            if (asset == null)
            {
                asset = new Material(shader);
                created = true;
            }

            if (asset.shader != shader)
                asset.shader = shader;

            asset.name = spec.Name;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                TextureFolder + "/" + spec.Texture + ".png");
            if (texture != null)
                FxMaterials.ApplyTexture(asset, texture);

            // 爆炸 core/fire 额外乘亮度压制系数（见类头「爆炸亮度压制」注释）。
            float brightness = ExplosionBrightnessScaleOf(spec.Name);
            Color tint = spec.Tint;
            float intensity = spec.Intensity;
            if (brightness < 1f)
            {
                tint = new Color(tint.r * brightness, tint.g * brightness, tint.b * brightness, tint.a);
                intensity *= brightness;
            }

            FxMaterials.ApplyTint(asset, tint);
            if (asset.HasProperty("_Intensity"))
                asset.SetFloat("_Intensity", intensity);

            asset.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (created)
                AssetDatabase.CreateAsset(asset, assetPath);
            else
                EditorUtility.SetDirty(asset);

            return true;
        }

        // ------------------------------------------------------------------
        // Always Included Shaders（让 Shader.Find 在构建后的播放器里也能找到 FX shader）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把两个 FX shader 登记进 Graphics Settings 的 Always Included Shaders。
        /// 【为什么】运行时材质是 <c>Shader.Find</c> 建的，若没有任何场景/Resources 引用这两个 shader，
        /// 构包时会被剥离 → 播放器里特效整体消失。登记后即便美术忘了引用也不会丢。
        /// 失败只告警（例如 ProjectSettings 只读），不影响资产生成。
        /// </summary>
        static int RegisterAlwaysIncludedShaders()
        {
            int added = 0;
            try
            {
                UnityEngine.Object[] settings =
                    AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
                if (settings == null || settings.Length == 0)
                    return 0;

                var so = new SerializedObject(settings[0]);
                SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
                if (list == null || !list.isArray)
                    return 0;

                string[] names = { FxMaterials.AdditiveShaderName, FxMaterials.AlphaShaderName };
                for (int i = 0; i < names.Length; i++)
                {
                    Shader shader = Shader.Find(names[i]);
                    if (shader == null || AlreadyInList(list, shader))
                        continue;

                    int index = list.arraySize;
                    list.InsertArrayElementAtIndex(index);
                    list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
                    added++;
                }

                if (added > 0)
                    so.ApplyModifiedProperties();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FxAssetBuilder] 登记 Always Included Shaders 失败（不影响特效运行，"
                    + "但构包后可能找不到 FX shader，请手动加入）：" + e.Message);
            }

            return added;
        }

        static bool AlreadyInList(SerializedProperty list, Shader shader)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static bool BytesEqual(string absolutePath, byte[] candidate)
        {
            try
            {
                if (!File.Exists(absolutePath))
                    return false;
                byte[] existing = File.ReadAllBytes(absolutePath);
                if (existing.Length != candidate.Length)
                    return false;

                for (int i = 0; i < existing.Length; i++)
                {
                    if (existing[i] != candidate[i])
                        return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string ToAbsolutePath(string assetPath)
        {
            return Path.Combine(Application.dataPath,
                assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            // CreateFolder 不建中间层级：调用方若直接传 "Assets/A/B/C"，父目录不存在就会静默失败。
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>爆炸材质亮度压制系数（非爆炸材质返回 1 = 不压制）。按 .mat 资产名匹配，见类头注释。</summary>
        static float ExplosionBrightnessScaleOf(string materialName)
        {
            switch (materialName)
            {
                case "Fx_ExplosionCore": return ExplosionCoreBrightnessScale;
                case "Fx_ExplosionFire": return ExplosionFireBrightnessScale;
                default: return 1f;
            }
        }
    }
}
