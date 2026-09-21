using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 弹体脚底「接触阴影面片」（blob shadow）的**运行时**装配工具（纯静态，无状态）。
    ///
    /// 【它解决什么】本工程实时阴影全关（等距像素卡通口径），落地感只能由脚下半透明径向渐变面片补偿。
    /// 船员侧已由 <c>CrewVisualPrefabBuilder.AddContactShadow</c> 在**编辑器期**烘进预制体
    /// （见 <see cref="ContactShadowDecal"/>）；而武器弹体全部由
    /// <see cref="BattleController.CreateProjectile"/> **运行时程序化创建**
    /// （<c>projectilePrefab</c> 场景引用为空、走 BuildFallbackVisual 图元兜底），
    /// 预制体里没有烘好的面片，故必须有一个运行时版。
    ///
    /// 【为什么只给放置/常驻类挂】判据 <see cref="ShouldAttach"/>（= <see cref="ProjectileProfile.IsPersistent"/>）：
    ///   · 放置类（mine / gunpowderBarrel / woodenCrate / cannon）跨回合摆在地上不动，**一直**压着那块地面，
    ///     是最需要"贴地读作实体"的一类；
    ///   · 飞行弹体一帧掠过一个像素宽的落点、落地即爆，面片既跟不上也没机会被看到，挂了只是浪费。
    ///
    /// 【为什么面片是"出生即烘好、零逐帧同步"】
    ///   面片是弹体根的**子物体**：弹体的位移/朝向自动带走它，无需每帧同步。
    ///   贴地高度也不必逐帧对齐地面——放置类出生时就被摆在瞄准落点（kinematic、不受重力），
    ///   其根 y 减去半高即弹体底面；故只需要在装配时算一次局部 y。
    ///   这与 <see cref="ContactShadowDecal"/> 类头记的项目既定取舍一致：
    ///   "面片残留一帧无关观感，不值得为它引入每帧射线/地形查询"。
    ///   **已知取舍**：mine 是弹弓抛出的（出生在空中、落地后才常驻），飞行那一小段其面片会跟着悬空；
    ///   补偿它需要给弹体加"着地后显影"的逐帧状态（要改 <c>WeaponProjectile</c>，超出本次改动范围），
    ///   且 mine 的绝大多数生命周期是静止摆在地上的，故本次**不做**。
    ///
    /// 【为什么不复用 CrewContactShadow.mat / .png 资产】它们是 `Assets/Art/…` 下的普通资产，
    /// 不在 Resources 目录里，运行时无法按路径加载；要复用只能走"序列化引用"（给 BattleController 加字段），
    /// 那是**新的序列化依赖**且需要重装场景。工程既有惯例正是"运行时自建"：
    /// <see cref="PirateCrew.Fx.FxMaterials"/>/<c>FxTextures</c> 就是 Shader.Find + 内存贴图/材质，
    /// 这里沿用同一套（<see cref="BuildFallbackVisual"/> 的 `new Material(shader)` 也是这个路子）。
    /// **构建安全**：URP/Unlit 被本工程 M2 场景与 CrewContactShadow.mat 引用着，不会被 shader 剥离，
    /// 故 Shader.Find 在构建包里同样能找到。
    ///
    /// 【参数口径】直径/抬高/中心不透明度全部对齐船员版（<see cref="ContactShadowDecal"/> 的常量），
    /// 只有"直径怎么由弹体推出来"按弹体尺寸换算（见 <see cref="DiameterFor"/>）。
    ///
    /// 【ECall 边界】本类触碰 <c>Mesh</c>/<c>Texture2D</c>/<c>Material</c>（ECall），只能在 Unity 运行时/编辑器里用；
    /// 纯算术的 <see cref="DiameterFor"/> / <see cref="ShouldAttach"/> 可在无头验证台断言。
    /// </summary>
    public static class ProjectileContactShadow
    {
        /// <summary>
        /// 直径系数：面片直径 = 弹体最大全尺寸 × 1.6（世界单位）。
        ///
        /// 【出处】<see cref="ContactShadowDecal.DefaultDiameter"/> 的注释："0.6 ≈ 单位碰撞足迹（0.375）的 1.6 倍"
        /// ——即**全尺寸（碰撞体的宽/高/深）的 1.6 倍**，本处沿用同一比例，只把"单位足迹"换成"弹体足迹"。
        /// 【为什么取 1.6】面片要比弹体本体略大一圈才读得出"压在地上"（等大或略小会缩进轮廓里看不出）。
        /// 【已知副作用】放置类沿 x 铺开的间距恰好 = 碰撞体全宽（<c>ProjectileSpawnPlanner.PlanPlaceables</c>
        /// 的 2×HalfWidth，箱体即 1.0），而面片直径 1.6 &gt; 1.0，故 3 个木箱并排时相邻面片会叠一小段；
        /// 该处两片各 α≈0.32 → 叠合压暗≈0.53（单片的中心是 0.45），读起来是"两件东西都贴着地"，
        /// 不是破面。若要彻底消掉这处叠暗，得让铺开间距随面片直径走（会动放置手感），本次不改。
        /// </summary>
        public const float DiameterToExtentRatio = 1.6f;

        /// <summary>贴地抬高（世界单位）：复用船员版（0.02，防 z-fight 且肉眼仍视为贴地）。</summary>
        public const float GroundOffset = ContactShadowDecal.DefaultGroundOffset;

        /// <summary>中心不透明度：复用船员版（0.45，中心压暗 45%、边缘渐隐无硬边）。</summary>
        public const float CenterAlpha = ContactShadowDecal.DefaultCenterAlpha;

        /// <summary>径向渐变贴图边长（与船员版 CrewContactShadow.png 同为 64²）。</summary>
        const int RadialTextureSize = 64;

        /// <summary>面片子物体名（与船员预制体同名，便于两侧统一识别/排除拾取）。</summary>
        const string ObjectName = "ContactShadow";

        const string QuadMeshName = "ProjectileContactShadowQuad";
        const string MaterialName = "ProjectileContactShadow";
        const string TextureName = "ProjectileContactShadow";

        /// <summary>URP/Unlit（与 CrewContactShadow.mat 同 shader：面片不该被主光照亮第二次）。</summary>
        const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        /// <summary>找不到 URP/Unlit 时的兜底（内置透明 unlit，至少不是粉红方块）。</summary>
        const string UnlitFallbackShaderName = "Unlit/Transparent";

        /// <summary>面片基色（近黑，与 CrewVisualPrefabBuilder 的 ContactShadowColor 同值），alpha 取中心 0.45。</summary>
        static readonly Color ShadowColor = new Color(0.015f, 0.015f, 0.020f, CenterAlpha);

        // ---- 运行时缓存（全弹体共用一份网格/贴图/材质，避免每个弹体各建一份）----

        static Mesh _quadMesh;
        static Texture2D _radialTexture;
        static Material _material;
        static bool _warnedNoShader;

        /// <summary>
        /// 该弹体是否该挂接触阴影 —— 判据 = <see cref="ProjectileProfile.IsPersistent"/>
        /// （即 <c>!WeaponStats.LimitedToTurn</c>，见 <see cref="ProjectileProfile"/> 的 `IsPersistent` 定义行）。
        ///
        /// 【为什么用这个 API 而不是另外两个候选】
        ///   §5.2 里 `limitedToTurn=false` 的恰好就是本次点名的四件放置/常驻物：
        ///   mine / gunpowderBarrel / woodenCrate / cannon（ProjectileProfile.cs:122 原话）。
        ///   · <see cref="ProjectileProfile.IsPlaceable"/>（PlaceableCount &gt; 0）**漏两件**：
        ///     只有 gunpowderBarrel(2) / woodenCrate(3) 计数非 0，mine 与 cannon 的 PlaceableCount 是 0
        ///     （WeaponCatalog.cs:157 / :215）。
        ///   · <see cref="ProjectileSpawn.Kinematic"/>（出生即静止）**漏 mine**：它覆盖
        ///     woodenCrate/gunpowderBarrel/cannon，但 mine 是弹弓抛出去的（出生 kinematic=false，
        ///     落地后才常驻），恰好被这个判据漏掉。
        /// 故"跨回合常驻"是唯一同时覆盖四件、又精确排除飞行弹体的口径。
        ///
        /// 【飞行弹体为什么不挂】cannonball / cherryBomb / dynamite / banana / parachuteBomb / rumBottle /
        ///   piecesOfEight / boulder 全部 limitedToTurn=true（本回合用完即掉/即爆），
        ///   落点与本体几乎同时消失，面片没有任何可被看到的窗口。
        /// </summary>
        public static bool ShouldAttach(in ProjectileProfile profile)
        {
            return profile.IsPersistent;
        }

        /// <summary>
        /// 面片直径（世界单位）= 弹体碰撞体三个全尺寸里的**最大值** × <see cref="DiameterToExtentRatio"/>。
        ///
        /// 换算式：<c>diameter = 1.6 × max(ColliderWidth, ColliderHeight, ColliderDepth)</c>。
        /// 【为什么取"最大全尺寸"】放置类的观感是"这块地被我占了"：以最长边算，面片才裹得住弹体投影
        /// （箱体 gunpowderBarrel/woodenCrate 是 1.0 × 0.94，取 1.0 → 直径 1.6；球形 mine 是 0.875 → 1.4）。
        /// 用半尺寸（半径）会小一半、缩进轮廓里看不出；用最小边则箱体的长边会超出面片。
        /// </summary>
        public static float DiameterFor(in ProjectileProfile profile)
        {
            float maxExtent = Mathf.Max(
                profile.ColliderWidth,
                Mathf.Max(profile.ColliderHeight, profile.ColliderDepth));
            return maxExtent * DiameterToExtentRatio;
        }

        /// <summary>
        /// 给弹体根挂上脚下接触阴影面片。返回挂上的标记组件；未挂（无 shader / 参数非法 / 已存在）返回 null。
        ///
        /// 【规格】
        ///   · 面片绕 X 转 −90° 水平贴地（1×1 XY quad 的法线 +Z 被转到 +Y，正面朝上）；
        ///   · 直径 = <see cref="DiameterFor"/>（世界单位，靠 localScale 反算，抵消弹体根缩放）；
        ///   · 贴地抬高 = 弹体底面 y + <see cref="GroundOffset"/>（底面 = 根 y − 半高，故局部 y 取 −HalfHeight + 0.02）；
        ///   · 中心不透明度 = <see cref="CenterAlpha"/>（写在材质的 _BaseColor.a，径向则由贴图 α 给出）；
        ///   · castShadow off / receiveShadows off（面片是叠加的"压暗"，不参与光照链路，否则逆光会变成亮斑）。
        /// </summary>
        /// <param name="projectileRoot">弹体根（BoxCollider 所在的那一层，其原点 = 碰撞体中心）。</param>
        /// <param name="profile">弹体运行参数（尺寸从它拿）。</param>
        public static ContactShadowDecal Attach(Transform projectileRoot, in ProjectileProfile profile)
        {
            if (projectileRoot == null)
                return null;

            // 幂等：弹体走 projectilePrefab 分支时，预制体可能已经烘好一个面片（船员侧就是这么做的），
            // 直接复用，避免同一弹体脚下叠两张半透明面片（叠出来会明显更黑）。
            ContactShadowDecal existing = projectileRoot.GetComponentInChildren<ContactShadowDecal>(true);
            if (existing != null)
                return existing;

            Material material = GetMaterial();
            if (material == null)
                return null;   // 找不到 shader：宁可不挂面片，也不留一个渲染异常的方块

            var go = new GameObject(ObjectName);
            go.transform.SetParent(projectileRoot, false);

            // 弹体根可能带缩放（预制体分支未知）；面片按世界单位给尺寸，故用根的实际缩放反算局部值。
            // 平面两轴都取 X 的缩放：quad 绕 X 转 −90° 后面内两轴分别映射到世界 X / Z，
            // 同一个系数才能保证"仍是正圆"（若根的 X/Z 缩放不一致，会得到椭圆——见类头"已知取舍"的同类说明）。
            Vector3 rootScale = projectileRoot.lossyScale;
            float planarScale = Mathf.Max(0.0001f, Mathf.Abs(rootScale.x));
            float verticalScale = Mathf.Max(0.0001f, Mathf.Abs(rootScale.y));

            // 贴地抬高：底面 = 根 y − 半高（BoxCollider 是中心对齐的），再抬 0.02 防与地形顶面 z-fight。
            go.transform.localPosition = new Vector3(
                0f, (-profile.HalfHeight + GroundOffset) / verticalScale, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            float localDiameter = DiameterFor(profile) / planarScale;
            go.transform.localScale = new Vector3(localDiameter, localDiameter, 1f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = GetQuadMesh();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // 面片自身不投影、不接收阴影/探针：它只是一层"压暗"叠加，不参与光照链路。
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // 标记组件是"这是接触阴影面片"的唯一身份：UnitOutlineBinder 靠它排除描边/阵营染色，
            // 其 OnValidate 还会在编辑器里对"直径/抬高/alpha 明显不对"给出提醒。
            // 注意：它的三个序列化留档字段（diameter/groundOffset/centerAlpha）这里改不到（运行时无 SerializedObject），
            // 真值在 Transform 与材质里；留档值只作参数提示，与船员预制体（编辑器期写实值）略有差别，不影响渲染。
            var decal = go.AddComponent<ContactShadowDecal>();

            return decal;
        }

        // ------------------------------------------------------------------
        // 资源（运行时自建 + 缓存）
        // ------------------------------------------------------------------

        /// <summary>1×1 的 XY 面片网格（法线 +Z），与船员预制体的 CrewContactShadowQuad 同构。</summary>
        static Mesh GetQuadMesh()
        {
            if (_quadMesh != null)
                return _quadMesh;

            var mesh = new Mesh { name = QuadMeshName, hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };   // 逆时针 → 正面朝 +Z（转 −90° 后朝 +Y）
            mesh.RecalculateBounds();

            _quadMesh = mesh;
            return _quadMesh;
        }

        /// <summary>
        /// 运行时自建 64² 径向渐变贴图（白 RGB + 中心 α=1 → 边缘 α=0 的 smoothstep），
        /// 算法就地重写自 <c>CrewVisualPrefabBuilder.BuildContactShadowTexture</c>（不入库资产、零序列化依赖）。
        /// </summary>
        static Texture2D GetRadialTexture()
        {
            if (_radialTexture != null)
                return _radialTexture;

            int size = RadialTextureSize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f;
                    float v = (y + 0.5f) / size - 0.5f;
                    float d = Mathf.Clamp01(new Vector2(u, v).magnitude / 0.5f);   // 0 = 中心, 1 = 外接圆
                    float t = 1f - d;
                    float alpha = t * t * (3f - 2f * t);                           // smoothstep 径向渐变
                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
                }
            }

            // linear: true 与落盘资产的 sRGBTexture=false 同口径：它是 α 遮罩，RGB 恒为白，
            // 走 sRGB 会让渐变边缘变硬。Clamp + 无 mip：贴地小面片不需要 mip，Clamp 防边缘采样外溢。
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = TextureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            _radialTexture = texture;
            return _radialTexture;
        }

        /// <summary>
        /// 取（或创建）面片材质：URP/Unlit 的透明档 + 径向渐变贴图 + 近黑半透色。
        /// 参数与 <c>CrewVisualPrefabBuilder.BuildContactShadowMaterial</c> 同源，只有"资产 vs 内存"的区别。
        /// 找不到 shader 时返回 null（调用方跳过挂载）。
        /// </summary>
        public static Material GetMaterial()
        {
            if (_material != null)
                return _material;

            Shader shader = Shader.Find(UnlitShaderName);
            if (shader == null)
                shader = Shader.Find(UnlitFallbackShaderName);
            if (shader == null)
            {
                if (!_warnedNoShader)
                {
                    _warnedNoShader = true;
                    global::PirateCrew.Core.Log.Warn("[ProjectileContactShadow] 未找到 shader "
                        + UnlitShaderName + " / " + UnlitFallbackShaderName
                        + "，放置类弹体将没有脚下接触阴影。");
                }
                return null;
            }

            var material = new Material(shader) { name = MaterialName, hideFlags = HideFlags.DontSave };

            Texture2D radial = GetRadialTexture();
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", radial);
            else if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", radial);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", ShadowColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", ShadowColor);

            // URP/Unlit 的透明档（等价于 Inspector 里 Surface Type = Transparent、Blending = Alpha）。
            material.SetOverrideTag("RenderType", "Transparent");
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Back);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.renderQueue = (int)RenderQueue.Transparent;

            _material = material;
            return _material;
        }
    }
}
