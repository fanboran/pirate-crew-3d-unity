using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步云台水洼 / 水壁 / 玻璃的运行时换装 + 水波动画（挂场景件 prefab 根；
    /// CloudSeaAssetBaker 烘焙时 AddComponent，运行时实例化后自动生效）。
    ///
    /// 【做什么】Awake 一次性把子物体里的水槽位换成 <c>CloudSeaWater.shader</c>
    /// （内部名 = <see cref="CloudSeaPath.WaterShaderName"/>）的材质实例，分流 / 取色
    /// 决策全在 <see cref="CloudSeaWaterMaterial"/>（纯逻辑，无头可测）：
    ///   · 水洼 / 水壁按材质名 Cloud_Water / Cloud_WaterDeep 找，受 <c>enablePuddles</c> 总闸；
    ///   · 玻璃按材质名 Cloud_Glass 找，恒换。
    /// 颜色 / alpha 从原材质 _Color/_BaseColor 解析（缺色或实心 alpha 时按档兜底——
    /// FBX 内嵌色 alpha 恒 1，水洼永不定格成不透明，口径与 CloudSeaAssetBaker 同源）；
    /// _MainTex 留白，水色不靠贴图。
    ///
    /// 【开阔海面不归这里】Cloud_Sea_Surface 由烘焙器直接落清澈水材质
    /// （<see cref="CloudSeaPath.ClearWaterShaderName"/>，带岛影 / 折射 / 碎金），材质名不在
    /// 本组件认的三件里，原样保留；本组件只读它上面的岛影起伏参数，每帧下发成全局
    /// （<see cref="PushSurfaceGlobals"/>）。
    ///
    /// 【动画只做一件事】水波相位走 shader 内 _Time.y × _WaveSpeed（零每帧属性写）；
    /// Update 只把 _WaveStrength 按 sin 呼吸调制 0.8–1.2×。属性 ID 全部
    /// Shader.PropertyToID 常驻缓存——每帧路径零字符串、零 new Material。
    ///
    /// 【静默失效防线】shader 摸不到响一次错并停用本组件（不抛异常拖死装配链）；
    /// 子物体里一个水对象都找不到时响亮告警——对象 / 材质名契约拼错是静默失效家族，
    /// 不能只靠肉眼发现水洼没动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudSeaWater : MonoBehaviour
    {
        [Header("兜底色（源材质缺色时按档取 rgb；alpha 缺失/实心时按档取 a）")]
        [SerializeField] Color shallowColor = new Color(0.31f, 0.83f, 0.85f, 0.60f);
        [SerializeField] Color deepColor = new Color(0.12f, 0.44f, 0.48f, 0.60f);

        [Header("水波（相位走 shader 内 _Time.y × _WaveSpeed；本组件只做幅度呼吸）")]
        [SerializeField] float waveStrength = 0.06f;
        [SerializeField] float waveSpeed = 0.6f;
        [SerializeField] bool enablePuddles = true;

        // 属性 ID 常驻缓存（禁每帧字符串查询）。
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int WaveStrengthId = Shader.PropertyToID("_WaveStrength");
        static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        static readonly int PixelDomainGateId = Shader.PropertyToID("_CloudSeaPixelDomain");

        // 岛影交界线起伏的全局（名字契约见 CloudSeaCommon.hlsl）：水面色 / 海床 / 水下生物各是各的材质，
        // 只能靠全局共用同一个波场。折射只用材质上的值，不下发。
        static readonly int ShadowWaveId = Shader.PropertyToID("_CloudSeaShadowWave");
        static readonly int ShadowWaveDirId = Shader.PropertyToID("_CloudSeaShadowWaveDir");
        static readonly int MatRefractPixelsId = Shader.PropertyToID("_RefractionPixels");
        static readonly int MatWaveDirectionId = Shader.PropertyToID("_WaveDirection");
        static readonly int MatShadowWaveAmplitudeId = Shader.PropertyToID("_ShadowWaveAmplitude");
        static readonly int MatShadowWaveLengthId = Shader.PropertyToID("_ShadowWaveLength");
        static readonly int MatShadowWaveSpeedId = Shader.PropertyToID("_ShadowWaveSpeed");
        static readonly int MatShadowWaveFpsId = Shader.PropertyToID("_ShadowWaveFps");

        Material _surfaceMaterial;
        bool _surfaceGlobalsPushed;

        /// <summary>上一帧的域闸门（只在切换时 SetGlobalFloat，避免每帧冗余下发）。</summary>
        bool _lastPixelDomain;

        /// <summary>一条换装记录：渲染器 + 原共享材质组（OnDestroy 恢复）+ 换上的实例组。</summary>
        sealed class SwapRecord
        {
            public Renderer Target;
            public Material[] OriginalShared;
            public readonly List<Material> Instances = new List<Material>();
        }

        readonly List<SwapRecord> records = new List<SwapRecord>();

        void Awake()
        {
            Shader shader = LoadShader();
            if (shader == null)
            {
                // 契约断裂（shader 不在 Resources/包里）：响一次错、停用组件避免每帧空转。
                enabled = false;
                return;
            }

            SwapAll(shader);
            PushPixelDomain(force: true);
            CaptureSurface();
            PushSurfaceGlobals();
        }

        void Update()
        {
            PushPixelDomain(force: false);
            PushSurfaceGlobals();
            float strength = CloudSeaWaterMaterial.WaveStrengthModulated(waveStrength, Time.time);
            for (int r = 0; r < records.Count; r++)
            {
                List<Material> instances = records[r].Instances;
                for (int i = 0; i < instances.Count; i++)
                {
                    Material material = instances[i];
                    if (material == null)
                        continue;   // 材质先于本组件销毁（场景卸载竞态）：跳过即可。
                    material.SetFloat(WaveStrengthId, strength);
                    material.SetFloat(WaveSpeedId, waveSpeed);
                }
            }
        }

        /// <summary>
        /// 找开阔海面（<see cref="CloudSeaPath.SeaSurfaceObjectName"/>）的材质与水面高度。海面材质由烘焙器
        /// 直接落、不经本组件换装（见类头），这里只读它的折射与岛影起伏参数。
        /// </summary>
        void CaptureSurface()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer target = renderers[r];
                if (target.gameObject.name != CloudSeaPath.SeaSurfaceObjectName)
                    continue;
                Material material = target.sharedMaterial;
                if (material == null || !material.HasProperty(MatRefractPixelsId))
                    continue;
                _surfaceMaterial = material;
                // 水面是一张平网（顶点微起伏只有厘米级），包围盒中心即水面高度。
                Shader.SetGlobalFloat(PixelartPath.WaterYId, target.bounds.center.y);
                return;
            }

            Debug.LogWarning("[CloudSeaWater] 子物体里没找到海面 " + CloudSeaPath.SeaSurfaceObjectName
                + "（或其材质没有 _RefractionPixels）——岛影起伏与水面高度的全局不下发。");
        }

        /// <summary>
        /// 海面参数每帧下发成全局（材质在 Inspector 里改了也立即跟上；每帧只有几次按 ID 取值 + 写全局）；
        /// 本组件退场时清零。波场的两个向量由米制参数换算：波数 = 2π / 波长，角速度 = 波数 × 相速度。
        /// </summary>
        void PushSurfaceGlobals()
        {
            if (_surfaceMaterial == null)
                return;
            _surfaceGlobalsPushed = true;
            Material material = _surfaceMaterial;
            float waveK = 2f * Mathf.PI / Mathf.Max(material.GetFloat(MatShadowWaveLengthId), 0.5f);
            Shader.SetGlobalVector(ShadowWaveId, new Vector4(
                waveK,
                waveK * material.GetFloat(MatShadowWaveSpeedId),
                material.GetFloat(MatShadowWaveAmplitudeId),
                material.GetFloat(MatShadowWaveFpsId)));
            Vector4 direction = material.GetVector(MatWaveDirectionId);
            Vector2 flat = new Vector2(direction.x, direction.z);
            float magnitude = flat.magnitude;
            Shader.SetGlobalVector(ShadowWaveDirId, magnitude > 1e-5f
                ? new Vector4(flat.x / magnitude, flat.y / magnitude, 0f, 0f)
                : new Vector4(1f, 0f, 0f, 0f));
        }

        void ClearSurfaceGlobals()
        {
            if (!_surfaceGlobalsPushed)
                return;
            _surfaceGlobalsPushed = false;
            Shader.SetGlobalFloat(PixelartPath.WaterYId, 0f);
            Shader.SetGlobalVector(ShadowWaveId, Vector4.zero);
        }

        /// <summary>
        /// 域闸门：shader 自带两条透明趟（已退役的 CellShade 像素化趟 + 标准前向趟），两趟同时画
        /// 会把 alpha 叠两次。PostLine 是唯一生效风格后闸门恒 0（只走前向趟）；随 CellShade
        /// 线退役一并删除。
        /// </summary>
        void PushPixelDomain(bool force)
        {
            bool pixelDomain = RenderStyleState.Current == RenderStyle.CellShade
                && PixelartPath.ActiveRig != null;
            if (!force && pixelDomain == _lastPixelDomain)
                return;
            _lastPixelDomain = pixelDomain;
            Shader.SetGlobalFloat(PixelDomainGateId, pixelDomain ? 1f : 0f);
            Log.Info("[CloudSeaWater] 域闸门 → "
                + (pixelDomain ? "1（CellShade 像素化趟）" : "0（PostLine 前向趟）")
                + "，换装记录 " + records.Count + " 条。");
        }

        void OnDestroy()
        {
            ClearSurfaceGlobals();
            for (int r = 0; r < records.Count; r++)
            {
                SwapRecord record = records[r];
                if (record.Target != null)
                    record.Target.sharedMaterials = record.OriginalShared;
                for (int i = 0; i < record.Instances.Count; i++)
                {
                    if (record.Instances[i] != null)
                        Destroy(record.Instances[i]);
                }
            }
            records.Clear();
        }

        // ------------------------------------------------------------------
        // 换装（仅 Awake 一次；每帧路径只有 Update 的 SetFloat）
        // ------------------------------------------------------------------

        void SwapAll(Shader shader)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer target = renderers[r];
                Material[] shared = target.sharedMaterials;

                // 同一渲染器按档共用一个实例（浅/深/玻璃三档，None 不换）。
                var byKind = new Material[4];
                Material[] swapped = null;
                for (int s = 0; s < shared.Length; s++)
                {
                    Material source = shared[s];
                    if (source == null)
                        continue;

                    CloudSeaWaterMaterial.SlotKind kind = CloudSeaWaterMaterial.KindOf(source.name);
                    if (!CloudSeaWaterMaterial.ShouldSwap(kind, enablePuddles))
                        continue;

                    if (byKind[(int)kind] == null)
                    {
                        Material instance = new Material(shader)
                        {
                            name = source.name + " (CloudSeaWater)"
                        };
                        instance.SetColor(ColorId, ResolveSourceColor(source, kind));
                        instance.SetFloat(WaveStrengthId, waveStrength);
                        instance.SetFloat(WaveSpeedId, waveSpeed);
                        byKind[(int)kind] = instance;
                    }

                    if (swapped == null)
                        swapped = (Material[])shared.Clone();
                    swapped[s] = byKind[(int)kind];
                }

                if (swapped == null)
                    continue;

                var record = new SwapRecord { Target = target, OriginalShared = shared };
                for (int k = 1; k < byKind.Length; k++)
                {
                    if (byKind[k] != null)
                        record.Instances.Add(byKind[k]);
                }

                target.sharedMaterials = swapped;
                records.Add(record);
            }

            if (records.Count == 0)
            {
                Debug.LogWarning("[CloudSeaWater] 子物体里没找到任何水洼 / 水壁 / 玻璃（按材质名 "
                    + CloudSeaPath.WaterMaterialName + "/" + CloudSeaPath.WaterDeepMaterialName
                    + "/" + CloudSeaPath.GlassMaterialName
                    + "）——核对 FBX 材质名契约，云台上的水洼不会出现。");
            }
        }

        /// <summary>源材质取色：优先 _Color（透明趟的有效乘色），退 _BaseColor（家族取色口径），都没有按档兜底。</summary>
        Color ResolveSourceColor(Material source, CloudSeaWaterMaterial.SlotKind kind)
        {
            bool hasColor = false;
            Color sourceColor = Color.white;
            if (source.HasProperty(ColorId))
            {
                sourceColor = source.GetColor(ColorId);
                hasColor = true;
            }
            else if (source.HasProperty(BaseColorId))
            {
                sourceColor = source.GetColor(BaseColorId);
                hasColor = true;
            }
            return CloudSeaWaterMaterial.ResolveColor(kind, hasColor, sourceColor, shallowColor, deepColor);
        }

        /// <summary>
        /// 取 shader：主路径 Resources.Load（本 shader 落 Assets/Resources/PixelartShaders/），
        /// 未命中按内部名 Shader.Find 回退（双保险同构 PixelartShaders.Load——但不走那个
        /// 帮助类：它按"Resources 文件名 = 内部名末段"推路径，本 shader 文件名是
        /// CloudSeaWater、内部名末段是 Water，推出来必然落空并响无谓警告）。
        /// </summary>
        static Shader LoadShader()
        {
            Shader shader = Resources.Load<Shader>(CloudSeaWaterMaterial.ShaderResourcePath);
            if (shader != null)
                return shader;

            shader = Shader.Find(CloudSeaPath.WaterShaderName);
            if (shader == null)
            {
                Debug.LogError("[CloudSeaWater] 水洼 shader 摸不到：Resources/"
                    + CloudSeaWaterMaterial.ShaderResourcePath + " 与 Shader.Find(\""
                    + CloudSeaPath.WaterShaderName + "\") 都落空——水洼将保持原材质。");
            }
            return shader;
        }
    }
}
