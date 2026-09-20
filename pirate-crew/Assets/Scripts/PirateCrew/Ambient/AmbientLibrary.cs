using System.Collections.Generic;
using PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Ambient
{
    /// <summary>
    /// 环境模块的网格库（运行时程序化生成；纯表现、无资产依赖）。
    ///
    /// 【为什么运行时生成而不是预制体】本模块的白名单只允许新建
    /// <c>Assets/Art/Models/Ambient/</c> 等目录，且工程纪律是"零外部素材、全部程序化"。
    /// 运行时用 <see cref="AmbientMeshFactory"/>（纯 C#）+ <see cref="CreateMesh"/> 生成，
    /// 保证"打开场景就有活物"，不依赖先跑某个编辑器脚本。
    /// <c>Assets/Editor/AmbientAssetBuilder.cs</c> 是**可选**的落盘路径（把同一份网格存成资产，
    /// 便于美术检查与跨场景复用）。
    ///
    /// 【生命周期】由 <see cref="AmbientDirector"/> 持有；<see cref="Dispose"/> 里销毁全部网格/材质。
    /// </summary>
    public sealed class AmbientMeshSet
    {
        public readonly Mesh GullBody;
        public readonly Mesh GullWing;
        public readonly Mesh CrabBody;
        public readonly Mesh CrabClaw;
        public readonly Mesh Fish;
        public readonly Mesh LanternFrame;
        public readonly Mesh LanternCore;
        public readonly Mesh Pennant;
        public readonly Mesh Post;
        public readonly Mesh CorkFloat;
        public readonly Mesh DistantBird;
        public readonly Mesh GlowQuad;

        /// <summary>全部网格的三角面合计（报告/性能自证用）。</summary>
        public int TotalTriangles { get; }

        public AmbientMeshSet()
        {
            GullBody = Create(AmbientMeshFactory.BuildGullBody(), "Ambient_GullBody");
            GullWing = Create(AmbientMeshFactory.BuildGullWing(), "Ambient_GullWing");
            CrabBody = Create(AmbientMeshFactory.BuildCrabBody(), "Ambient_CrabBody");
            CrabClaw = Create(AmbientMeshFactory.BuildCrabClaw(), "Ambient_CrabClaw");
            Fish = Create(AmbientMeshFactory.BuildFish(), "Ambient_Fish");
            LanternFrame = Create(AmbientMeshFactory.BuildLanternFrame(), "Ambient_LanternFrame");
            LanternCore = Create(AmbientMeshFactory.BuildLanternCore(), "Ambient_LanternCore");
            Pennant = Create(AmbientMeshFactory.BuildPennant(), "Ambient_Pennant");
            Post = Create(AmbientMeshFactory.BuildPost(AmbientMeshFactory.PostHeight, 0.05f), "Ambient_Post");
            CorkFloat = Create(AmbientMeshFactory.BuildCorkFloat(), "Ambient_CorkFloat");
            DistantBird = Create(AmbientMeshFactory.BuildDistantBird(), "Ambient_DistantBird");
            GlowQuad = Create(AmbientMeshFactory.BuildGlowQuad(1f), "Ambient_GlowQuad");

            TotalTriangles = Tri(GullBody) * 3      // 躯干 + 双翼（翼复用同一网格两实例）
                + Tri(CrabBody) * 1
                + Tri(CrabClaw) * 2
                + Tri(Fish) * AmbientBudget.FishPerSchool
                + Tri(LanternFrame) * AmbientBudget.MaxLanterns
                + Tri(LanternCore) * AmbientBudget.MaxLanterns
                + Tri(Pennant) * 6
                + Tri(Post) * 4
                + Tri(CorkFloat) * 3
                + Tri(DistantBird) * 3
                + Tri(GlowQuad) * AmbientBudget.MaxLanterns;
        }

        /// <summary>销毁全部网格（编辑器下用 DestroyImmediate，运行时用 Destroy）。</summary>
        public void Dispose()
        {
            Destroy(GullBody);
            Destroy(GullWing);
            Destroy(CrabBody);
            Destroy(CrabClaw);
            Destroy(Fish);
            Destroy(LanternFrame);
            Destroy(LanternCore);
            Destroy(Pennant);
            Destroy(Post);
            Destroy(CorkFloat);
            Destroy(DistantBird);
            Destroy(GlowQuad);
        }

        static int Tri(Mesh mesh)
        {
            return mesh == null ? 0 : (int)(mesh.GetIndexCount(0) / 3);
        }

        static void Destroy(Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }

        /// <summary>把纯 C# 三角面缓冲落成 Unity 网格（每面独立顶点 = 平面着色的低模块面）。</summary>
        public static Mesh CreateMesh(MeshBuffers buffers, string name)
        {
            var mesh = new Mesh { name = name };
            if (buffers == null || buffers.IsEmpty)
                return mesh;

            mesh.indexFormat = IndexFormat.UInt32;

            var vertices = new List<Vector3>(buffers.VertexCount);
            var normals = new List<Vector3>(buffers.VertexCount);
            var triangles = new List<int>(buffers.IndexCount);
            buffers.CopyTo(vertices, normals, triangles);

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Create(MeshBuffers buffers, string name)
        {
            return CreateMesh(buffers, name);
        }
    }

    /// <summary>
    /// 环境模块的材质库。全部材质**共享**（同类一个材质 → 利于 SRP Batcher / GPU Instancing）。
    ///
    /// 【材质分工】
    ///   · 海鸥/螃蟹/鱼/立柱/浮标 → <c>PirateCrew/PirateOutline</c>（本体 + #2A2A2A 描边，
    ///     与场景道具、单位同一套描边语言，满足美术风格指南 M12）；
    ///   · 灯笼金属件 → <c>PirateCrew/PirateSurface</c>（PBR 金属高光）；
    ///   · 灯笼芯/辉光片 → <c>PirateCrew/Ambient/Glow</c>（自发光，不受光照/阴影影响）；
    ///   · 远景海鸟剪影 → URP/Unlit 不透明深灰（无光照，纯剪影）。
    /// 三档基色全部标【提案】：美术风格指南只规定"环境去饱和、角色高饱和"，
    /// 未给海鸥/螃蟹/鱼的色值；这里取低饱和自然色，保证不与阵营红蓝抢注意。
    /// </summary>
    public sealed class AmbientMaterialSet
    {
        // ---- shader 名（集中一处，避免散落魔法字符串）----
        public const string WindShaderName = "PirateCrew/Ambient/Wind";
        public const string GlowShaderName = "PirateCrew/Ambient/Glow";
        public const string SurfaceShaderName = "PirateCrew/PirateSurface";
        public const string OutlineShaderName = "PirateCrew/PirateOutline";
        public const string UnlitShaderName = "Universal Render Pipeline/Unlit";

        // ---- 生物/道具色（【提案】）----
        const string GullBodyHex = "#F2F0E6";
        const string GullWingHex = "#D6D6CC";
        const string CrabShellHex = "#B8482F";
        const string CrabClawHex = "#D2603C";
        const string FishHex = "#7FC7D9";
        const string PostHex = "#8A6234";
        const string CorkHex = "#A67B42";
        const string DistantBirdHex = "#5A6470";
        const string LanternMetalHex = "#6E6A63";
        const string LanternCoreHex = "#FFD9A0";
        const string LanternGlowHex = "#FFB347";
        const string PennantHex = "#D4A76A";
        const string FoliageLightHex = "#5FA83C";
        const string FoliageDarkHex = "#33672A";

        public readonly Material GullBody;
        public readonly Material GullWing;
        public readonly Material CrabShell;
        public readonly Material CrabClaw;
        public readonly Material Fish;
        public readonly Material LanternMetal;
        public readonly Material LanternCore;
        public readonly Material LanternGlow;
        public readonly Material DistantBird;
        public readonly Material Post;
        public readonly Material CorkFloat;

        /// <summary>顶点风摆材质模板：既有植被（棕榈叶/灌木/草）。</summary>
        public readonly Material WindFoliage;

        /// <summary>顶点风摆材质模板：红队旗。</summary>
        public readonly Material WindFlagRed;

        /// <summary>顶点风摆材质模板：蓝队旗。</summary>
        public readonly Material WindFlagBlue;

        /// <summary>顶点风摆材质模板：帆布/垂片（本模块自建的燕尾旗）。</summary>
        public readonly Material WindCloth;

        /// <summary>是否有 shader 缺失（缺 shader 时回落 URP/Unlit，画面对但少描边/风摆）。</summary>
        public bool HasMissingShaders { get; private set; }

        public AmbientMaterialSet()
        {
            // ---- 描边生物材质 ----
            GullBody = CreateOutline("Ambient_GullBody", GullBodyHex, 0.0040f);
            GullWing = CreateOutline("Ambient_GullWing", GullWingHex, 0.0035f);
            CrabShell = CreateOutline("Ambient_CrabShell", CrabShellHex, 0.0040f);
            CrabClaw = CreateOutline("Ambient_CrabClaw", CrabClawHex, 0.0035f);
            Fish = CreateOutline("Ambient_Fish", FishHex, 0.0025f);
            Post = CreateOutline("Ambient_Post", PostHex, 0.0035f);
            CorkFloat = CreateOutline("Ambient_CorkFloat", CorkHex, 0.0030f);

            // ---- 灯笼 ----
            LanternMetal = CreateSurface("Ambient_LanternMetal", LanternMetalHex, 0.55f, 0.55f);
            LanternCore = CreateGlow("Ambient_LanternCore", AmbientTimeOfDayCatalog.Hex(LanternCoreHex), 2.4f, 0.06f);
            LanternGlow = CreateGlow("Ambient_LanternGlow", AmbientTimeOfDayCatalog.Hex(LanternGlowHex), 1.5f, 0.5f);

            // ---- 远景剪影 ----
            DistantBird = CreateUnlit("Ambient_DistantBird", DistantBirdHex, 1f);

            // ---- 风摆模板 ----
            WindFoliage = CreateWind("Ambient_WindFoliage",
                SceneArtPalette.Hex(FoliageLightHex), SceneArtPalette.Hex(FoliageDarkHex),
                weightDirection: 1f, anchorY: 0f, height: 1.6f, floor: WindRules.DefaultSwayFloor,
                strength: 0.085f, speed: WindRules.BaseSpeed, density: 0.35f, flutter: 0.30f, emission: 0f);

            WindFlagRed = CreateWind("Ambient_WindFlagRed",
                SceneArtPalette.Hex(SceneArtPalette.TeamRed), SceneArtPalette.Hex("#8C1B12"),
                weightDirection: -1f, anchorY: 3.2f, height: 1.0f, floor: 0.15f,
                strength: 0.10f, speed: WindRules.BaseSpeed * 1.35f, density: 0.20f, flutter: 0.55f, emission: 0f);

            WindFlagBlue = CreateWind("Ambient_WindFlagBlue",
                SceneArtPalette.Hex(SceneArtPalette.TeamBlue), SceneArtPalette.Hex("#1B338C"),
                weightDirection: -1f, anchorY: 3.2f, height: 1.0f, floor: 0.15f,
                strength: 0.10f, speed: WindRules.BaseSpeed * 1.35f, density: 0.20f, flutter: 0.55f, emission: 0f);

            WindCloth = CreateWind("Ambient_WindCloth",
                SceneArtPalette.Hex(PennantHex), SceneArtPalette.Hex(SceneArtPalette.WoodMid),
                weightDirection: -1f, anchorY: 1.55f, height: 0.35f, floor: 0.10f,
                strength: 0.075f, speed: WindRules.BaseSpeed * 1.6f, density: 0.55f, flutter: 0.65f, emission: 0f);
        }

        /// <summary>销毁全部材质。</summary>
        public void Dispose()
        {
            Material[] all =
            {
                GullBody, GullWing, CrabShell, CrabClaw, Fish,
                LanternMetal, LanternCore, LanternGlow, DistantBird, Post, CorkFloat,
                WindFoliage, WindFlagRed, WindFlagBlue, WindCloth,
            };

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(all[i]);
                else
                    Object.DestroyImmediate(all[i]);
            }
        }

        /// <summary>取 shader，找不到时记警告并返回 null（调用方负责回落）。</summary>
        public static Shader FindShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
                global::PirateCrew.Core.Log.Warn("[Ambient] 找不到 shader " + name
                    + "（可能未编译或未进构建）。请先在有渲染路径的编辑器里 read_console 确认 shader 无编译错误。");
            return shader;
        }

        // ------------------------------------------------------------------
        // 材质工厂
        // ------------------------------------------------------------------

        /// <summary>描边材质（本体色 + #2A2A2A 描边），与 SceneArtBuilder 的道具材质同一写法。</summary>
        Material CreateOutline(string name, string bodyHex, float outlineWidth)
        {
            Shader shader = FindShader(OutlineShaderName);
            if (shader == null)
            {
                HasMissingShaders = true;
                return CreateUnlit(name, bodyHex, 1f);
            }

            var m = new Material(shader) { name = name };
            Color body = SceneArtPalette.Hex(bodyHex);
            SetColor(m, "_BaseColor", body);

            Color outline = SceneArtPalette.Hex(SceneArtPalette.Outline, 1f);
            SetColor(m, "_OutlineColor", outline);
            SetColor(m, "_OutlineColorHover", outline);
            SetColor(m, "_OutlineColorSelected", outline);
            SetFloat(m, "_OutlineWidth", outlineWidth);
            SetFloat(m, "_OutlineWidthHover", outlineWidth);
            SetFloat(m, "_OutlineWidthSelected", outlineWidth);
            SetFloat(m, "_OutlineState", 0f);
            SetFloat(m, "_OutlineAlpha", 1f);
            SetFloat(m, "_OutlineExpandMode", 0f);
            SetFloat(m, "_OutlineDistanceAttenuation", 0.4f);
            SetFloat(m, "_DebugMode", 0f);
            return m;
        }

        /// <summary>PBR 表面材质（PirateSurface 三档色阶；这里把同一色压成三档）。</summary>
        Material CreateSurface(string name, string hex, float smoothness, float metallic)
        {
            Shader shader = FindShader(SurfaceShaderName);
            if (shader == null)
            {
                HasMissingShaders = true;
                return CreateUnlit(name, hex, 1f);
            }

            var m = new Material(shader) { name = name };
            Color c = SceneArtPalette.Hex(hex);
            SetColor(m, "_BaseColorA", c * 0.72f);
            SetColor(m, "_BaseColorB", c);
            SetColor(m, "_BaseColorC", c * 1.12f);
            SetFloat(m, "_Metallic", metallic);
            SetFloat(m, "_Smoothness", smoothness);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_DebugMode", 0f);
            return m;
        }

        /// <summary>自发光辉光材质（PirateCrew/Ambient/Glow）。</summary>
        Material CreateGlow(string name, Color color, float intensity, float radius)
        {
            Shader shader = FindShader(GlowShaderName);
            if (shader == null)
            {
                HasMissingShaders = true;
                return CreateUnlit(name, "#FFB347", 1f);
            }

            var m = new Material(shader) { name = name };
            SetColor(m, "_BaseColor", color);
            SetFloat(m, "_Intensity", intensity);
            SetFloat(m, "_Radius", radius);
            SetFloat(m, "_FalloffPower", 2f);
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>不透明 unlit（远景剪影）。</summary>
        Material CreateUnlit(string name, string hex, float alpha)
        {
            Shader shader = FindShader(UnlitShaderName);
            if (shader == null)
            {
                shader = FindShader("Standard");
                HasMissingShaders = true;
            }

            var m = new Material(shader) { name = name };
            Color c = SceneArtPalette.Hex(hex, alpha);
            SetColor(m, "_BaseColor", c);
            SetColor(m, "_Color", c);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetFloat(m, "_Surface", 0f);
            SetFloat(m, "_ZWrite", 1f);
            m.renderQueue = (int)RenderQueue.Geometry;
            return m;
        }

        /// <summary>顶点风摆材质（PirateCrew/Ambient/Wind）。返回的材质**默认不自带描边**，
        /// 因为它是给整组合并网格当"表面"用的（换掉原材质会同时失去原描边，属已知取舍，见报告）。</summary>
        Material CreateWind(string name, Color baseColor, Color darkColor,
            float weightDirection, float anchorY, float height, float floor,
            float strength, float speed, float density, float flutter, float emission)
        {
            Shader shader = FindShader(WindShaderName);
            if (shader == null)
            {
                HasMissingShaders = true;
                return CreateUnlit(name, "#5FA83C", 1f);
            }

            var m = new Material(shader) { name = name };
            SetColor(m, "_BaseColor", baseColor);
            SetColor(m, "_BaseColorDark", darkColor);
            SetFloat(m, "_AmbientStrength", 1f);
            SetFloat(m, "_Emission", emission);

            // _WindDirection 是 Vector 属性，必须走 SetVector（SetFloat 只会写 x 分量）。
            if (m.HasProperty("_WindDirection"))
                m.SetVector("_WindDirection", new Vector4(0.92f, 0.39f, 0f, 0f));
            SetFloat(m, "_WindStrength", strength);
            SetFloat(m, "_WindSpeed", speed);
            SetFloat(m, "_WindHeight", height);
            SetFloat(m, "_WindAnchorY", anchorY);
            SetFloat(m, "_WindWeightDirection", weightDirection);
            SetFloat(m, "_WindDensity", density);
            SetFloat(m, "_WindFlutter", flutter);
            SetFloat(m, "_WindFloor", floor);
            SetFloat(m, "_DebugMode", 0f);
            return m;
        }

        // ------------------------------------------------------------------
        // 属性写入辅助（属性不存在时静默跳过，避免换 shader 后报错）
        // ------------------------------------------------------------------

        internal static void SetColor(Material m, string property, Color value)
        {
            if (m != null && m.HasProperty(property))
                m.SetColor(property, value);
        }

        internal static void SetFloat(Material m, string property, float value)
        {
            if (m != null && m.HasProperty(property))
                m.SetFloat(property, value);
        }
    }
}
