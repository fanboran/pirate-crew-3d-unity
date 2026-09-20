using UnityEngine;

namespace PirateCrew.SceneArt.Showcase
{
    /// <summary>
    /// 空岛（Floating Island）的材质槽位（纯 C#，无头可测）。
    ///
    /// 【为什么用"槽位"而不是"每块石头一个材质"】与 <see cref="ScenePropBuffers"/> 同一思路：
    /// 同一槽位的全部三角面在构建期合并成 **1 个网格 + 1 个材质 = 1 个 DrawCall**。
    /// 整座空岛（约 2 万面、上百个零件）因此只有 15 个 DrawCall。
    ///
    /// 【槽位命名的语义】槽位名 = "读起来是什么"，不绑定具体颜色：
    /// 颜色/粗糙度/噪声明暗全部集中在 <see cref="IslandMaterialCatalog"/>，
    /// 几何层只负责"这块面是岩层还是草皮"。
    ///
    /// 【设计裁决（AI 自有风格，本工程无既有空岛资产）】空岛是**独立于战斗场景的美术展示件**，
    /// 不走竞技场调色板的三档沙/草/岩（那是"荒岛战场"的语义）；但为了与本工程风格化写实
    /// （GDD §10.4）同源，岩/草/木三族的**中档**仍取 <see cref="SceneArtPalette"/> 既有色值，
    /// 只在其上派生出暗/亮档，使空岛放进 Battle 场景时不与竞技场"脱色"。
    /// </summary>
    public enum IslandMaterial
    {
        /// <summary>岩层亮档：受光的阶地面、岩鳍上缘。</summary>
        RockLight,

        /// <summary>岩层中档：主体崖壁。</summary>
        RockMid,

        /// <summary>岩层暗档：背光崖壁、岛底锥根、石笋。</summary>
        RockDark,

        /// <summary>草皮亮档：穹顶受光面、草簇。</summary>
        GrassLight,

        /// <summary>草皮中档：树冠主体、灌木。</summary>
        GrassMid,

        /// <summary>草皮暗档：崖沿草皮垂帘、树冠下缘、垂藤。</summary>
        GrassDark,

        /// <summary>泥土/根须：草皮下沿的土层、树干根部、老树根。</summary>
        Dirt,

        /// <summary>石工：遗迹台基/石柱/踏步/拱梁（比天然岩层更规整、更亮、更滑）。</summary>
        Stone,

        /// <summary>木料：瞭望台/木箱/旗杆/踏板。</summary>
        Wood,

        /// <summary>水帘（半透明青蓝）：瀑布主体、水潭。</summary>
        Water,

        /// <summary>浪花/水沫（半透明白）：瀑布芯、急流、水潭边泡沫。</summary>
        Foam,

        /// <summary>云雾（半透明白）：云团、瀑布末端雾团、云裙。</summary>
        Cloud,

        /// <summary>晶体（加法发光青）：空岛魔力晶簇（遗迹核心 + 悬浮卫星晶）。</summary>
        Crystal,

        /// <summary>暖光（加法发光金）：萤火/灯芯/浮尘光点。</summary>
        Glow,

        /// <summary>旗帜（不透明红）：瞭望台桅顶海盗旗。</summary>
        Banner,
    }

    /// <summary>材质槽位对应的 shader 家族（决定 <c>Assets/Editor/FloatingIslandShowcaseMenu.cs</c> 怎么建材质）。</summary>
    public enum IslandShaderKind
    {
        /// <summary><c>PirateCrew/PirateSurface</c>：三档色阶 + 片元程序化噪声的写实不透明表面（岩/草/木/石）。</summary>
        SurfaceSolid,

        /// <summary><c>Universal Render Pipeline/Unlit</c> 不透明（旗帜）。</summary>
        UnlitOpaque,

        /// <summary><c>Universal Render Pipeline/Unlit</c> 半透明（水/沫/云）。</summary>
        UnlitTransparent,

        /// <summary><c>PirateCrew/Fx/Additive</c> 加法发光（晶体/暖光点）——在亮天空前会自然过曝成"发光"。</summary>
        FxAdditive,
    }

    /// <summary>
    /// 单个槽位的材质配方（纯 C#，无头可测）。**这里只有数据，不碰 Unity 材质对象**：
    /// 无头验证台可以断言"每个色值都解析成功、每档参数都在 shader 的值域内"，
    /// 而真正 <c>new Material(...)</c> 的部分留给 Editor 脚本。
    /// </summary>
    public sealed class IslandMaterialRecipe
    {
        /// <summary>shader 家族。</summary>
        public IslandShaderKind Kind;

        // ---- SurfaceSolid：三档色阶（A=暗 / B=中 / C=亮，对应 PirateSurface 的 _BaseColorA/B/C）----
        /// <summary>暗档 sRGB 十六进制（仅 <see cref="IslandShaderKind.SurfaceSolid"/> 用）。</summary>
        public string HexDark;
        /// <summary>中档 sRGB 十六进制。</summary>
        public string HexMid;
        /// <summary>亮档 sRGB 十六进制。</summary>
        public string HexLight;

        /// <summary>PBR 金属度（0 = 非金属；空岛全是岩/草/木/石，恒 0）。</summary>
        public float Metallic;
        /// <summary>PBR 光滑度（岩 0.18 / 石工 0.30 / 木 0.25 …）。</summary>
        public float Smoothness;
        /// <summary>片元程序化噪声的明暗强度（0 = 纯色，1 = 强斑驳）。</summary>
        public float NoiseStrength;
        /// <summary>三档色阶的对比（>1 = 分档更分明，岩层用它读"层理"）。</summary>
        public float RampContrast;
        /// <summary>噪声 XZ 拉伸：(1,1)=各向同性；(0.22,1)=沿 Z 拉长成木纹/层理。</summary>
        public Vector3 NoiseStretch;

        // ---- Unlit / Additive：单基色 + alpha / 发光强度 ----
        /// <summary>单基色 sRGB 十六进制（Unlit 与 Additive 档用）。</summary>
        public string Hex;
        /// <summary>半透明档的 alpha（0-1）。</summary>
        public float Alpha;
        /// <summary>加法发光档的强度（<c>PirateCrew/Fx/Additive</c> 的 _Intensity）。</summary>
        public float Intensity;

        /// <summary>是否投射阴影（实体档 true、半透明与发光档 false——浮空发光件不该在地上投影）。</summary>
        public bool CastShadows;

        /// <summary>
        /// 发光/加法档写入网格顶点色的颜色（白 = 不改色）。
        /// 【为什么必须显式写】<c>PirateCrew/Fx/Additive</c> 的片元是 <c>tex * _Color * IN.color</c>：
        /// 网格若没有 COLOR 通道，默认值在 D3D11 上不可依赖；写一份纯白即把这条链路钉死。
        /// </summary>
        public Color VertexTint;
    }

    /// <summary>
    /// 空岛材质表（纯 C#，无头可测）：槽位 → 配方。**几何层与 Editor 层的唯一颜色来源**。
    ///
    /// 【色值出处】
    ///   · 岩/草/木三族的**中档** = <see cref="SceneArtPalette"/> 既有值（GDD §10.4 调色板转写）；
    ///   · 其余暗/亮档、石工、水、沫、云、晶、光 = 【AI 提案】，按"同一色相 ± 明度"派生，
    ///     使一座自洽的低多边形浮空岛在无贴图前提下仍有色彩层次；
    ///   · 云/沫**禁用纯白 255**（沿用 SceneArtBuilder 的 r2 教训：硬边纯白在天空前会"烧"出一个白洞）。
    /// </summary>
    public static class IslandMaterialCatalog
    {
        /// <summary>全部槽位（供 Editor 遍历、测试全量校验）。顺序与 <see cref="IslandMaterial"/> 声明一致。</summary>
        public static readonly IslandMaterial[] All =
        {
            IslandMaterial.RockLight,
            IslandMaterial.RockMid,
            IslandMaterial.RockDark,
            IslandMaterial.GrassLight,
            IslandMaterial.GrassMid,
            IslandMaterial.GrassDark,
            IslandMaterial.Dirt,
            IslandMaterial.Stone,
            IslandMaterial.Wood,
            IslandMaterial.Water,
            IslandMaterial.Foam,
            IslandMaterial.Cloud,
            IslandMaterial.Crystal,
            IslandMaterial.Glow,
            IslandMaterial.Banner,
        };

        // ---- 岩层三档（中档 = SceneArtPalette.RockMid #8C7B6A）----
        static readonly IslandMaterialRecipe RockLightRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = "#6E6154", HexMid = SceneArtPalette.RockMid, HexLight = SceneArtPalette.RockLight,
            Metallic = 0f, Smoothness = 0.18f, NoiseStrength = 0.42f, RampContrast = 2.1f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe RockMidRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = "#4A4038", HexMid = "#6E6154", HexLight = SceneArtPalette.RockMid,
            Metallic = 0f, Smoothness = 0.16f, NoiseStrength = 0.46f, RampContrast = 2.2f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe RockDarkRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = "#2E2823", HexMid = "#403832", HexLight = SceneArtPalette.RockDark,
            Metallic = 0f, Smoothness = 0.14f, NoiseStrength = 0.40f, RampContrast = 1.9f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        // ---- 草皮三档（中档 = SceneArtPalette.GrassMid #4A8C4A）----
        static readonly IslandMaterialRecipe GrassLightRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = SceneArtPalette.GrassMid, HexMid = SceneArtPalette.GrassLight, HexLight = "#A9E39B",
            Metallic = 0f, Smoothness = 0.10f, NoiseStrength = 0.52f, RampContrast = 1.6f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe GrassMidRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = SceneArtPalette.GrassDark, HexMid = SceneArtPalette.GrassMid, HexLight = "#6FB86A",
            Metallic = 0f, Smoothness = 0.10f, NoiseStrength = 0.50f, RampContrast = 1.7f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe GrassDarkRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = "#1C3A1C", HexMid = SceneArtPalette.GrassDark, HexLight = SceneArtPalette.GrassMid,
            Metallic = 0f, Smoothness = 0.08f, NoiseStrength = 0.44f, RampContrast = 1.5f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        // ---- 泥土（中档取 WoodDark #6B4C28 的暖褐；亮档借沙地暗档）----
        static readonly IslandMaterialRecipe DirtRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = "#3D2C1D", HexMid = SceneArtPalette.WoodDark, HexLight = SceneArtPalette.SandDark,
            Metallic = 0f, Smoothness = 0.12f, NoiseStrength = 0.50f, RampContrast = 1.6f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        // ---- 石工（比天然岩层更亮更滑：遗迹的"人工感"就靠这三档与岩层区分）----
        static readonly IslandMaterialRecipe StoneRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = "#6E6759", HexMid = "#9C9282", HexLight = "#CFC7B4",
            Metallic = 0f, Smoothness = 0.30f, NoiseStrength = 0.32f, RampContrast = 1.8f,
            NoiseStretch = new Vector3(1f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        // ---- 木料（中档 = SceneArtPalette.WoodMid #A67B42；噪声沿 Z 拉长 → 木纹）----
        static readonly IslandMaterialRecipe WoodRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.SurfaceSolid,
            HexDark = SceneArtPalette.WoodDark, HexMid = SceneArtPalette.WoodMid, HexLight = SceneArtPalette.WoodLight,
            Metallic = 0f, Smoothness = 0.24f, NoiseStrength = 0.40f, RampContrast = 1.7f,
            NoiseStretch = new Vector3(0.22f, 1f, 0f), CastShadows = true, VertexTint = Color.white,
        };

        // ---- 效果类 ----
        static readonly IslandMaterialRecipe WaterRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.UnlitTransparent,
            Hex = "#63D6E8", Alpha = 0.60f, CastShadows = false, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe FoamRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.UnlitTransparent,
            // 泡沫基色取 SceneArtPalette.Foam (#F0F7FF) 再压到 #E6F1FA：禁用纯白（r2 教训）。
            Hex = "#E6F1FA", Alpha = 0.74f, CastShadows = false, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe CloudRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.UnlitTransparent,
            // 云团走"带一点天空青灰的白"：#E4EAF0（最大通道 240，与 SceneArtBuilder 的云核同口径）
            Hex = "#E4EAF0", Alpha = 0.52f, CastShadows = false, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe CrystalRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.FxAdditive,
            Hex = "#7CF0E4", Intensity = 1.9f, CastShadows = false, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe GlowRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.FxAdditive,
            Hex = "#FFCB6B", Intensity = 1.7f, CastShadows = false, VertexTint = Color.white,
        };

        static readonly IslandMaterialRecipe BannerRecipe = new IslandMaterialRecipe
        {
            Kind = IslandShaderKind.UnlitOpaque,
            Hex = SceneArtPalette.TeamRed, CastShadows = false, VertexTint = Color.white,
        };

        /// <summary>取槽位配方（未在表内的槽位回落为岩层中档，避免静默返回 null）。</summary>
        public static IslandMaterialRecipe For(IslandMaterial material)
        {
            switch (material)
            {
                case IslandMaterial.RockLight: return RockLightRecipe;
                case IslandMaterial.RockMid: return RockMidRecipe;
                case IslandMaterial.RockDark: return RockDarkRecipe;
                case IslandMaterial.GrassLight: return GrassLightRecipe;
                case IslandMaterial.GrassMid: return GrassMidRecipe;
                case IslandMaterial.GrassDark: return GrassDarkRecipe;
                case IslandMaterial.Dirt: return DirtRecipe;
                case IslandMaterial.Stone: return StoneRecipe;
                case IslandMaterial.Wood: return WoodRecipe;
                case IslandMaterial.Water: return WaterRecipe;
                case IslandMaterial.Foam: return FoamRecipe;
                case IslandMaterial.Cloud: return CloudRecipe;
                case IslandMaterial.Crystal: return CrystalRecipe;
                case IslandMaterial.Glow: return GlowRecipe;
                default: return BannerRecipe;
            }
        }

        /// <summary>槽位资源文件名（Editor 建 <c>.mat</c> 用；不含扩展名）。</summary>
        public static string AssetName(IslandMaterial material)
        {
            return "FloatingIsland_" + material;
        }
    }
}
