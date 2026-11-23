using System.Collections.Generic;
using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步海水/水洼/玻璃的运行时换装 + 水波动画（挂场景件 prefab 根；
    /// CloudSeaAssetBaker 烘焙时 AddComponent，运行时实例化后自动生效）。
    ///
    /// 【做什么】Awake 一次性把子物体里的水槽位换成 <c>CloudSeaWater.shader</c>
    /// （内部名 = <see cref="CloudSeaPath.WaterShaderName"/>）的材质实例，分流/取色
    /// 决策全在 <see cref="CloudSeaWaterMaterial"/>（纯逻辑，无头可测）：
    ///   · 水面按对象名 <see cref="CloudSeaPath.SeaSurfaceObjectName"/> 找，恒换；
    ///   · 水壁/水洼按材质名 Cloud_Water / Cloud_WaterDeep 找，受 <c>enablePuddles</c> 总闸；
    ///   · 玻璃按材质名 Cloud_Glass 找，恒换。
    /// 颜色/alpha 从原材质 _Color/_BaseColor 解析（缺色或实心 alpha 时按档兜底——
    /// FBX 内嵌色 alpha 恒 1，海水永不定格成不透明，口径与 CloudSeaAssetBaker 同源）；
    /// _MainTex 留白，水色不靠贴图。
    ///
    /// 【动画只做一件事】水波相位走 shader 内 _Time.y × _WaveSpeed（零每帧属性写）；
    /// Update 只把 _WaveStrength 按 sin 呼吸调制 0.8–1.2×。属性 ID 全部
    /// Shader.PropertyToID 常驻缓存——每帧路径零字符串、零 new Material。
    ///
    /// 【两条链都看得见】shader 自带 LightMode=PixelartTransparent（CellShade 像素化域
    /// 收）与 UniversalForward（PostLine 主相机收）两趟，换完材质链路无感、不用换装两次。
    ///
    /// 【静默失效防线】shader 摸不到响一次错并停用本组件（不抛异常拖死装配链）；
    /// 子物体里一个水对象都找不到时响亮告警——对象/材质名契约拼错是静默失效家族，
    /// 不能只靠肉眼发现水面没动。
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
        static readonly int WaterLabHoleId = Shader.PropertyToID("_WaterLabHole");

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
        }

        void Update()
        {
            PushPixelDomain(force: false);
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
        /// 域闸门：CellShade（rig 在场）只让 PixelartTransparent 趟画水；PostLine / 普通 URP
        /// 只让 UniversalForward 趟画水。两趟同时画会把 alpha 叠两次（实拍深青板 + 水下压暗）。
        /// </summary>
        void PushPixelDomain(bool force)
        {
            bool pixelDomain = RenderStyleState.Current == RenderStyle.CellShade
                && PixelartPath.ActiveRig != null;
            if (!force && pixelDomain == _lastPixelDomain)
                return;
            _lastPixelDomain = pixelDomain;
            Shader.SetGlobalFloat(PixelDomainGateId, pixelDomain ? 1f : 0f);
            global::PirateCrew.Core.Log.Info("[CloudSeaWater] 域闸门 → "
                + (pixelDomain ? "1（CellShade 像素化趟）" : "0（PostLine 前向趟）")
                + "，style=" + RenderStyleState.Current + "，换装记录 " + records.Count + " 条。");
        }

        void OnDestroy()
        {
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
                bool isSurface = target.gameObject.name == CloudSeaPath.SeaSurfaceObjectName;

                // 同一渲染器按档共用一个实例（浅/深/玻璃三档，None 不换）。
                var byKind = new Material[4];
                Material[] swapped = null;
                for (int s = 0; s < shared.Length; s++)
                {
                    Material source = shared[s];
                    if (source == null)
                        continue;

                    CloudSeaWaterMaterial.SlotKind rawKind = CloudSeaWaterMaterial.KindOf(source.name);
                    if (!CloudSeaWaterMaterial.ShouldSwap(isSurface, rawKind, enablePuddles))
                        continue;

                    CloudSeaWaterMaterial.SlotKind kind = CloudSeaWaterMaterial.EffectiveKind(isSurface, rawKind);
                    if (byKind[(int)kind] == null)
                    {
                        Material instance = new Material(shader)
                        {
                            name = source.name + " (CloudSeaWater)"
                        };
                        Color resolved = ResolveSourceColor(source, kind);
                        instance.SetColor(ColorId, resolved);
                        instance.SetFloat(WaveStrengthId, waveStrength);
                        instance.SetFloat(WaveSpeedId, waveSpeed);
                        // 海面专用：WaterLab 域内整片裁掉简版水（ocean 接管）；水洼/玻璃恒 0。
                        // new Material(shader) 只带 shader 默认值，烘焙材质上的 _WaterLabHole
                        // 不会跟过来——运行时实例必须显式写（2026-11-21：挖洞曾因此静默失效）。
                        instance.SetFloat(WaterLabHoleId, isSurface ? 1f : 0f);
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
                Debug.LogWarning("[CloudSeaWater] 子物体里没找到任何水对象（水面按名 "
                    + CloudSeaPath.SeaSurfaceObjectName + "；水壁/水洼按材质 "
                    + CloudSeaPath.WaterMaterialName + "/" + CloudSeaPath.WaterDeepMaterialName
                    + "；玻璃 " + CloudSeaPath.GlassMaterialName
                    + "）——核对 FBX 对象/材质名契约，水面不会出现。");
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
                Debug.LogError("[CloudSeaWater] 海水 shader 摸不到：Resources/"
                    + CloudSeaWaterMaterial.ShaderResourcePath + " 与 Shader.Find(\""
                    + CloudSeaPath.WaterShaderName + "\") 都落空——水将保持原材质。");
            }
            return shader;
        }
    }
}
