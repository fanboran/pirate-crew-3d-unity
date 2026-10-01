using System.Collections.Generic;
using PirateCrew.Rendering.Pixelart;
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
    /// 【材质分工（像素化路径）】生物/道具/灯笼/风摆模板一律走**像素化物体 shader**
    /// （配方唯一来源 = <see cref="PixelartMaterialFactory"/>，色带/描边发生在低分辨率域着色趟里，
    /// 材质不再携带描边壳与 PBR 参数）；远景海鸟剪影 → URP/Unlit 不透明深灰（无光照，纯剪影）。
    /// 旧的反壳描边（PirateOutline）、PBR 表面（PirateSurface）、顶点风摆（Ambient/Wind）与
    /// 加法辉光（Ambient/Glow）建材质路径已随 PBR 根除退役——Wind/Glow 的 shader 本体由清扫波删除。
    /// 生物三档基色全部标【提案】：美术风格指南只规定"环境去饱和、角色高饱和"，
    /// 未给海鸥/螃蟹/鱼的色值；这里取低饱和自然色，保证不与阵营红蓝抢注意。
    /// </summary>
    public sealed class AmbientMaterialSet
    {
        // ---- shader 名（集中一处，避免散落魔法字符串）----
        // 【PirateOutline 的 shader 仍被船员描边链使用，常量保留；PirateSurface 常量已随
        //   PBR 根除清扫波删除（shader 本体同步退役）】
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

        /// <summary>像素风摆模板：既有植被（棕榈叶/灌木/草）。</summary>
        public readonly Material WindFoliage;

        /// <summary>像素风摆模板：红队旗。</summary>
        public readonly Material WindFlagRed;

        /// <summary>像素风摆模板：蓝队旗。</summary>
        public readonly Material WindFlagBlue;

        /// <summary>像素风摆模板：帆布/垂片（本模块自建的燕尾旗）。</summary>
        public readonly Material WindCloth;

        /// <summary>是否有 shader 缺失（缺 shader 时回落 URP/Unlit，画面对但少描边/风摆）。</summary>
        public bool HasMissingShaders { get; private set; }

        public AmbientMaterialSet()
        {
            // ---- 生物 / 道具（像素化路径：平色 + 工厂默认色带档数）----
            GullBody = CreatePixel("Ambient_GullBody", GullBodyHex);
            GullWing = CreatePixel("Ambient_GullWing", GullWingHex);
            CrabShell = CreatePixel("Ambient_CrabShell", CrabShellHex);
            CrabClaw = CreatePixel("Ambient_CrabClaw", CrabClawHex);
            Fish = CreatePixel("Ambient_Fish", FishHex);
            Post = CreatePixel("Ambient_Post", PostHex);
            CorkFloat = CreatePixel("Ambient_CorkFloat", CorkHex);

            // ---- 灯笼（金属壳 + 发光芯同走本路径；加法辉光随旧 Glow shader 退役，
            //      构包里的观感与此一致——旧链下 Glow shader 本就被剥离成 Unlit 回落）----
            LanternMetal = CreatePixel("Ambient_LanternMetal", LanternMetalHex);
            LanternCore = CreatePixel("Ambient_LanternCore", AmbientTimeOfDayCatalog.Hex(LanternCoreHex));
            LanternGlow = CreatePixel("Ambient_LanternGlow", AmbientTimeOfDayCatalog.Hex(LanternGlowHex));

            // ---- 远景剪影 ----
            DistantBird = CreateUnlit("Ambient_DistantBird", DistantBirdHex, 1f);

            // ---- 风摆模板（换装模板：顶点风摆 shader 退役后为像素平色；
            //      AmbientWindBinder 的绑定照常执行，材质无风属性时摆动自然空转）----
            WindFoliage = CreatePixel("Ambient_WindFoliage", FoliageLightHex);
            WindFlagRed = CreatePixel("Ambient_WindFlagRed", SceneArtPalette.TeamRed);
            WindFlagBlue = CreatePixel("Ambient_WindFlagBlue", SceneArtPalette.TeamBlue);
            WindCloth = CreatePixel("Ambient_WindCloth", PennantHex);
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

        /// <summary>
        /// 取 shader，找不到时记警告并返回 null（调用方负责回落）。
        /// 走 <see cref="PixelartShaders.Find"/> 取用口（URP Unlit / Standard 这类内置 shader
        /// 不持有于 Resources）——摸不到时取用口已响亮报错，这里保留带修复指引的警告。
        /// </summary>
        public static Shader FindShader(string name)
        {
            Shader shader = PixelartShaders.Find(name);
            if (shader == null)
                global::PirateCrew.Core.Log.Warn("[Ambient] 找不到 shader " + name
                    + "（可能未编译或未进构建）。请先在有渲染路径的编辑器里 read_console 确认 shader 无编译错误。");
            return shader;
        }

        // ------------------------------------------------------------------
        // 材质工厂
        // ------------------------------------------------------------------

        /// <summary>像素化路径材质（hex 入口；配方唯一来源 = <see cref="PixelartMaterialFactory"/>，
        /// 色带档数/描边走工厂默认值）。</summary>
        Material CreatePixel(string name, string hex)
        {
            return CreatePixel(name, SceneArtPalette.Hex(hex));
        }

        /// <summary>像素化路径材质（Color 入口，供时段染色等已解析色调用）。
        /// 物体 shader 缺失时回落 URP/Unlit 平色（不洋红），并置 <see cref="HasMissingShaders"/>。</summary>
        Material CreatePixel(string name, Color color)
        {
            Material m = PixelartMaterialFactory.Create(name, color);
            if (m != null)
                return m;

            HasMissingShaders = true;
            return CreateUnlit(name, color);
        }

        /// <summary>不透明 unlit（远景剪影）。两个 shader 都拿不到时返回 null
        /// （调用方把该物体空着材质渲染，不在 <c>new Material(null)</c> 上炸掉整个材质库构造）。</summary>
        Material CreateUnlit(string name, string hex, float alpha)
        {
            return CreateUnlit(name, SceneArtPalette.Hex(hex, alpha));
        }

        Material CreateUnlit(string name, Color color)
        {
            Shader shader = FindShader(UnlitShaderName);
            if (shader == null)
            {
                shader = FindShader("Standard");
                HasMissingShaders = true;
            }

            if (shader == null)
            {
                // 回落链走空（URP/Unlit 与 Standard 都被剥离/编译失败）：
                // 返回 null 让 Dispose/渲染路径各自容错，比抛异常保住"其余材质照常生成"。
                global::PirateCrew.Core.Log.Warn("[Ambient] CreateUnlit(" + name + ") 回落链走空："
                    + UnlitShaderName + " 与 Standard 都找不到，该材质不创建（对应物体不渲染）。");
                return null;
            }

            var m = new Material(shader) { name = name };
            SetColor(m, "_BaseColor", color);
            SetColor(m, "_Color", color);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            SetFloat(m, "_Surface", 0f);
            SetFloat(m, "_ZWrite", 1f);
            m.renderQueue = (int)RenderQueue.Geometry;
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
