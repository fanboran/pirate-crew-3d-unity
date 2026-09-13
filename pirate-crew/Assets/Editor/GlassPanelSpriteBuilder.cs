using System.IO;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 半透明亚克力（"液态玻璃"）九宫格 Sprite 的程序化生成器。
    ///
    /// 【为什么自产而不抄参照物】风格参照隔壁 game-2（stick-world）的
    /// <c>assets/ui/sketch/panel_*.png</c> + <c>modules/ui_global/scripts/theme/{sketch_style,stick_tokens}.gd</c>：
    /// 黑玻璃底（WINDOW_BG = 黑 88%、BORDER_PANEL = 白 30% 1px 描边、RADIUS_PANEL = 6）+
    /// 每像素 2~3% 纸感噪点。本工程**不复制它的 PNG**，只按同一套参数语言自产：
    /// 逐像素程序化绘制（圆角矩形 + 双色描边 + 垂直渐变 + 噪点 + 顶部高光带），
    /// 落成持久 PNG 资产后按九宫格导入（编辑器装配的场景只能引用持久资产）。
    ///
    /// 【UGUI 没有真模糊 —— 折衷说明（重要）】
    /// 移动端/桌面端的 UI 后处理模糊需要 GrabPass / RenderTexture 抓屏，UGUI 内置管线没有；
    /// 强行做要额外的相机 + Blit + 遮罩，代价远超本波次收益，且半透明面板叠 3D 场景时
    /// 模糊边缘会与场景锯齿打架。因此本波次用**玻璃拟态（glassmorphism）近似**：
    ///   ① 半透明底（透出场景 = 玻璃的核心读感）；
    ///   ② 顶部 10% 高光带（模拟光源在玻璃上缘的反射）；
    ///   ③ 外侧 1px 亮描边 + 内侧 1px 暗描边（模拟玻璃厚度/倒角：上亮下暗）；
    ///   ④ 描边内侧 3px 更透的"厚度带"（scene 从这里透过来，强化"这是块玻璃"）；
    ///   ⑤ ±2% 细噪点（消除大色块的"塑料感"，game-2 的纸感来源）。
    /// 这五条合起来在静态截图里已足够读作"玻璃"，且零额外渲染开销。
    ///
    /// 【层级结构（从外到内，单位 px）】
    /// <code>
    ///   dIn = 距圆角矩形外缘的向内距离
    ///   dIn ∈ [0,1)   亮描边（上 34% / 下 18% 白，双色 = 玻璃厚度受光）
    ///   dIn ∈ [1,2)   暗描边（#05070A 42%，压住亮边内侧，厚度感来源）
    ///   dIn ∈ [2,5)   厚度带（更低 alpha —— 场景从这里透出来）
    ///   dIn ≥ 5       内容区背衬（较实，承载文字；见下"对比度"）
    ///
    /// 【垂直渐变幅度】亮端（顶）: 暗端（底）≈ 1.5~1.9×（如框架 #1B2230 → #101620）。
    /// 工单写"上 8% 亮"，实做更明显——纯 8% 在近黑底上肉眼不可见，等于没渐变（用户抱怨的
    /// "纯色面板"正是这个观感）。放大到 1.5× 后在静态截图里能读作"受光面 + 背光面"，
    /// 且**对比度一律按亮端（顶）反推**，放大渐变不会让任何文字组合跌破 4.5:1。
    /// </code>
    ///
    /// 【对比度纪律（硬约束，最坏叠加底 = 纯白场景）】
    /// 半透明底上的文字，其可读性取决于"面板色叠在场景色上"的**合成色**。判据一律按
    /// 最坏情况（背景 = 纯白 255,255,255）算，公式 = WCAG 2.1 相对亮度比（sRGB 分段线性化）。
    /// 由此反推出各 tone 的内容区 alpha 下限（详见 <see cref="Frame"/> 等各 tone 的注释）：
    /// · 金标题 #F2D06B 需合成色 ≤ 90/255 → alpha ≥ 0.727（框架 0.74 刚好过 4.52:1，内容片 0.88 有 7.76:1）
    /// · 浅字 #F5E8C8 需 ≤ 104/255 → alpha ≥ 0.665（框架 0.74 → 5.56:1）
    /// · 队伍色名 #FF8A7A 需 ≤ 64/255 → alpha ≥ 0.841（只有 0.88 的内容片够）
    /// 于是本类刻意分两族：**框架（较透，0.70）**只承载浅字；
    /// **内容片（较实，0.88）**承载金标题/队伍色名 —— 这也正好对应 game-2
    /// "窗框（window_panel）+ 列表行（menu_hover）"的两级结构。
    ///
    /// 【本地 UGUI 陷阱（沿用 MenuUiBuilder 的教训）】Image.color 是乘色：
    /// 烘焙基准色 × Image.color。故所有 tone 的**烘焙 RGB 即最终色**，调用方一律
    /// <c>image.color = Color.white</c>，不要再用 color 去调色（会把 alpha 也乘坏）。
    /// </summary>
    public static class GlassPanelSpriteBuilder
    {
        // ------------------------------------------------------------------
        // 枚举与规格
        // ------------------------------------------------------------------

        /// <summary>玻璃色调（对应"这块玻璃是什么用途"）。</summary>
        public enum Tone
        {
            /// <summary>深色亚克力框架（大面板底，较透，只承载浅字）。</summary>
            Frame,
            /// <summary>深色内容片（列表行/标题条/文字槽，较实，可承载金标题与队伍色名）。</summary>
            Dense,
            /// <summary>浅色亚克力（暖白；主菜单标题板 / 次级按钮）。</summary>
            Light,
            /// <summary>海图玻璃（深蓝绿；小地图面板）。</summary>
            Sea,
            /// <summary>全屏底（主菜单背景；近黑蓝，顶部微光）。</summary>
            Backdrop,
            /// <summary>深色按钮（normal 态基准色）。</summary>
            Button,
            /// <summary>浅色按钮（暖白亚克力）。</summary>
            ButtonLight,
            /// <summary>主行动点（金，深字）。</summary>
            Primary,
            /// <summary>危险动作（深酒红，浅字）。</summary>
            Danger,
        }

        /// <summary>九宫格几何档（决定贴图尺寸 / 圆角 / 切片边框）。</summary>
        public enum Geo
        {
            /// <summary>面板档：64×64，圆角 14，切片边框 16（适用高度 ≥32px 的容器）。</summary>
            Panel,
            /// <summary>小件档：44×44，圆角 12，切片边框 13（按钮/列表行，适用高度 ≥26px）。</summary>
            Chip,
        }

        /// <summary>单个 tone 的绘制规格。</summary>
        struct Spec
        {
            /// <summary>内容区上端色（= 垂直渐变的亮端，即"上亮下暗"）。</summary>
            public Color top;
            /// <summary>内容区下端色。</summary>
            public Color bottom;
            /// <summary>内容区 alpha（承载文字的那一层，对比度按此值反推）。</summary>
            public float fillAlpha;
            /// <summary>厚度带 alpha（比内容区更透 —— 场景从这里透出来）。</summary>
            public float edgeAlpha;
            /// <summary>亮描边顶部 alpha / 底部 alpha（双色 = 玻璃厚度受光）。</summary>
            public float rimTopAlpha, rimBotAlpha;
            /// <summary>暗描边 alpha（亮边内侧的墨线）。</summary>
            public float rimDarkAlpha;
            /// <summary>顶部高光带 alpha（占高度 10%，向下渐隐）。</summary>
            public float highlightAlpha;
        }

        /// <summary>几何档常量。</summary>
        struct GeoSpec
        {
            public int size;
            public int radius;
            public int border;
        }

        static GeoSpec GeoOf(Geo geo)
        {
            return geo == Geo.Panel
                ? new GeoSpec { size = 64, radius = 14, border = 16 }
                : new GeoSpec { size = 44, radius = 12, border = 13 };
        }

        // ------------------------------------------------------------------
        // 色板（每像素烘焙，Image.color 必须为 white）
        // ------------------------------------------------------------------

        /// <summary>
        /// 深色亚克力"框架"：近黑蓝 #1B2230→#101620（@alpha 0.74）。
        /// 叠纯白最坏底：浅字 #F5E8C8 = <b>5.56:1</b>、金 #F2D06B = <b>4.52:1</b> —— 两档都过 4.5，
        /// 所以框架层"能放浅字、极限也能放金标题"（金仍建议放内容片：7.76 比 4.52 舒服得多）。
        /// 【alpha 为什么是 0.74 而不是 0.70】0.70 时金标题只有 3.95:1，会把"金标题不许上框架"
        /// 变成一条**容易被后续改动踩碎**的隐性约定（域外构建器 SettlementCard/CrewList 就在
        /// 框架上直接放金标题）。0.74 只多 4% 不透明度，却让框架层自身达标 —— 防御性设计，
        /// 也让"透度"（26% 场景可见）与 game-2 的 WINDOW_BG(0.88)/WINDOW_BG_LIGHT(0.72) 同量级。
        /// </summary>
        static Spec Frame => new Spec
        {
            top = Rgb(0x1B, 0x22, 0x30), bottom = Rgb(0x10, 0x16, 0x20),
            fillAlpha = 0.74f, edgeAlpha = 0.52f,
            rimTopAlpha = 0.34f, rimBotAlpha = 0.16f, rimDarkAlpha = 0.44f, highlightAlpha = 0.055f,
        };

        /// <summary>深色亚克力"内容片"：比框架更实（@alpha 0.88），金标题 7.76:1、
        /// 队伍色名 #FF8A7A 5.08:1、浅字 9.56:1（全部按纯白最坏底算）。</summary>
        static Spec Dense => new Spec
        {
            top = Rgb(0x17, 0x1E, 0x29), bottom = Rgb(0x0D, 0x12, 0x1A),
            fillAlpha = 0.88f, edgeAlpha = 0.62f,
            rimTopAlpha = 0.30f, rimBotAlpha = 0.14f, rimDarkAlpha = 0.40f, highlightAlpha = 0.05f,
        };

        /// <summary>浅色亚克力（暖白）：#F4EEE2→#DCD4C4（@alpha 0.82）。
        /// 深墨字 #2A2A2A 叠最坏背景（近黑）后 8.56:1。</summary>
        static Spec Light => new Spec
        {
            top = Rgb(0xF4, 0xEE, 0xE2), bottom = Rgb(0xE2, 0xDA, 0xCB),
            fillAlpha = 0.82f, edgeAlpha = 0.60f,
            rimTopAlpha = 0.55f, rimBotAlpha = 0.30f, rimDarkAlpha = 0.28f, highlightAlpha = 0.06f,
        };

        /// <summary>海图玻璃：深蓝绿 #123240→#071A22（@alpha 0.84）；金标题 5.47:1、浅字 7.04:1。
        /// 比框架更实，因为小地图的岛格（沙 #F0D48A / 草 #6FA86F）需要稳定底衬才读得出轮廓。</summary>
        static Spec Sea => new Spec
        {
            top = Rgb(0x12, 0x32, 0x40), bottom = Rgb(0x0B, 0x24, 0x30),
            fillAlpha = 0.84f, edgeAlpha = 0.58f,
            rimTopAlpha = 0.34f, rimBotAlpha = 0.16f, rimDarkAlpha = 0.42f, highlightAlpha = 0.05f,
        };

        /// <summary>主菜单全屏底：近黑蓝 #0D1520→#04070C（@alpha 0.94）。
        /// 全屏底不是"面板"，它要压住天空/场景给菜单文字一个稳定底 —— 半透明留给面板层。</summary>
        static Spec Backdrop => new Spec
        {
            top = Rgb(0x0D, 0x15, 0x20), bottom = Rgb(0x07, 0x0C, 0x14),
            fillAlpha = 0.94f, edgeAlpha = 0.90f,
            rimTopAlpha = 0.10f, rimBotAlpha = 0.06f, rimDarkAlpha = 0.20f, highlightAlpha = 0.04f,
        };

        /// <summary>深色按钮：比面板实（@alpha 0.80，工单要求"按钮更实"）。
        /// hover/pressed 由 Button 的 ColorBlock 乘色实现（亮度 + alpha 双变），不另烘焙贴图。</summary>
        static Spec Button => new Spec
        {
            top = Rgb(0x39, 0x42, 0x4F), bottom = Rgb(0x31, 0x3A, 0x46),
            fillAlpha = 0.80f, edgeAlpha = 0.58f,
            rimTopAlpha = 0.34f, rimBotAlpha = 0.16f, rimDarkAlpha = 0.38f, highlightAlpha = 0.06f,
        };

        /// <summary>浅色按钮（暖白亚克力，深墨字）。</summary>
        static Spec ButtonLight => new Spec
        {
            top = Rgb(0xEF, 0xE8, 0xDB), bottom = Rgb(0xDF, 0xD7, 0xC7),
            fillAlpha = 0.82f, edgeAlpha = 0.60f,
            rimTopAlpha = 0.55f, rimBotAlpha = 0.28f, rimDarkAlpha = 0.30f, highlightAlpha = 0.06f,
        };

        /// <summary>主行动点（金）：#DAB43C→#AE8920（@alpha 0.88）；深墨字 #2A2A2A 最坏底 5.79:1。
        /// 强调色"只上底不上字"纪律沿用：字是深墨，靠金底区分层级。</summary>
        static Spec Primary => new Spec
        {
            top = Rgb(0xDA, 0xB4, 0x3C), bottom = Rgb(0xBD, 0x96, 0x29),
            fillAlpha = 0.88f, edgeAlpha = 0.62f,
            rimTopAlpha = 0.52f, rimBotAlpha = 0.26f, rimDarkAlpha = 0.30f, highlightAlpha = 0.07f,
        };

        /// <summary>危险动作（深酒红）：#541413→#380C0B（@alpha 0.94）。
        /// 【为什么最实】危险动作必须一眼可辨且文字绝对可读：浅字叠纯白最坏底 9.97:1；
        /// 若沿用 0.80 的半透明红，浅字只剩 3.7:1（红通道被白底抬起来），不合格 —— 安全优先于透。</summary>
        static Spec Danger => new Spec
        {
            top = Rgb(0x54, 0x14, 0x13), bottom = Rgb(0x44, 0x0F, 0x0E),
            fillAlpha = 0.94f, edgeAlpha = 0.72f,
            rimTopAlpha = 0.40f, rimBotAlpha = 0.20f, rimDarkAlpha = 0.46f, highlightAlpha = 0.05f,
        };

        static Spec SpecOf(Tone tone)
        {
            switch (tone)
            {
                case Tone.Dense: return Dense;
                case Tone.Light: return Light;
                case Tone.Sea: return Sea;
                case Tone.Backdrop: return Backdrop;
                case Tone.Button: return Button;
                case Tone.ButtonLight: return ButtonLight;
                case Tone.Primary: return Primary;
                case Tone.Danger: return Danger;
                default: return Frame;
            }
        }

        // ------------------------------------------------------------------
        // 资产路径与缓存
        // ------------------------------------------------------------------

        /// <summary>生成产物的落盘目录（与 MenuUiBuilder 的旧 Skin 同目录）。</summary>
        const string SpriteFolder = "Assets/Art/Sprites/UI";

        static readonly System.Collections.Generic.Dictionary<string, Sprite> Cache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        /// <summary>资产名（<c>Glass_&lt;tone&gt;_&lt;geo&gt;.png</c>）。</summary>
        public static string AssetNameOf(Tone tone, Geo geo)
        {
            return "Glass_" + tone + "_" + geo;
        }

        /// <summary>取（必要时生成）指定 tone × 几何档的九宫格 Sprite。</summary>
        public static Sprite Get(Tone tone, Geo geo)
        {
            string key = AssetNameOf(tone, geo);
            if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
                return cached;

            string assetPath = SpriteFolder + "/" + key + ".png";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
                sprite = Generate(assetPath, tone, geo);

            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>清缓存并强制重烘焙全部 tone（改参数后跑菜单 <c>PirateCrew/UI/重烘焙亚克力玻璃 Sprite</c>）。</summary>
        [MenuItem("PirateCrew/UI/重烘焙亚克力玻璃 Sprite")]
        public static void RebuildAll()
        {
            Cache.Clear();
            int count = 0;
            foreach (Tone tone in System.Enum.GetValues(typeof(Tone)))
            {
                if (tone == Tone.Backdrop || tone == Tone.Sea)   // 全屏底/海图只做面板档
                {
                    Generate(SpriteFolder + "/" + AssetNameOf(tone, Geo.Panel) + ".png", tone, Geo.Panel);
                    count++;
                    continue;
                }

                if (tone == Tone.Button || tone == Tone.Primary || tone == Tone.Danger)  // 按钮族只做小件档
                {
                    Generate(SpriteFolder + "/" + AssetNameOf(tone, Geo.Chip) + ".png", tone, Geo.Chip);
                    count++;
                    continue;
                }

                foreach (Geo geo in System.Enum.GetValues(typeof(Geo)))
                {
                    Generate(SpriteFolder + "/" + AssetNameOf(tone, geo) + ".png", tone, geo);
                    count++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GlassPanelSpriteBuilder] 亚克力玻璃九宫格重烘焙完成，共 " + count + " 张：" + SpriteFolder);
        }

        // ------------------------------------------------------------------
        // 像素生成
        // ------------------------------------------------------------------

        /// <summary>
        /// 画一张九宫格贴图（圆角 + 双色描边 + 厚度带 + 垂直渐变 + 噪点 + 顶部高光带）。
        /// <paramref name="border"/> 通过 <see cref="TextureImporter.spriteBorder"/> 写成切片边框，
        /// 于是四个角在拉伸时不变形、只有中心 1×1 像素被拉伸。
        /// </summary>
        public static Texture2D CreateTexture(Tone tone, Geo geo, out Vector4 border)
        {
            GeoSpec g = GeoOf(geo);
            Spec s = SpecOf(tone);

            int n = g.size;
            var pixels = new Color[n * n];

            float half = n * 0.5f;
            float b = half - 0.5f;           // 半边长（以像素中心为基准）
            float r = g.radius;

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // 像素中心到画布中心的向量（y 翻转：贴图 y=0 在底部，而我们要"上亮下暗"）。
                    float px = x + 0.5f - half;
                    float py = (n - 1 - y) + 0.5f - half;

                    float sd = SdRoundBox(px, py, b, r);
                    float dIn = -sd;          // >0 = 形状内，数值 = 向内距离

                    // 垂直渐变：t=1 顶端、t=0 底端（top 是亮端）。
                    float t = 1f - (y + 0.5f) / n;
                    Color c = Color.Lerp(s.bottom, s.top, t);

                    // 顶部 10% 高光带（向下线性渐隐）：模拟玻璃上缘的环境反射。
                    float hlBand = n * 0.10f;
                    if (y < hlBand)
                    {
                        float hl = s.highlightAlpha * (1f - y / hlBand);
                        c = Color.Lerp(c, Color.white, hl);
                    }

                    // ±2% 确定性噪点（消除大色块的塑料感；不用 Random 保证可复现）。
                    float noise = (Hash01(x, y) * 2f - 1f) * 0.02f;
                    c = new Color(
                        Mathf.Clamp01(c.r * (1f + noise)),
                        Mathf.Clamp01(c.g * (1f + noise)),
                        Mathf.Clamp01(c.b * (1f + noise)), 1f);

                    // 分层 alpha：亮描边 → 暗描边 → 厚度带 → 内容区（厚度带→内容区 1px 羽化）。
                    float alpha;
                    if (dIn < 1f)
                    {
                        float rim = Mathf.Lerp(s.rimBotAlpha, s.rimTopAlpha, t);
                        c = Color.Lerp(c, Color.white, 0.72f);          // 亮描边是白系
                        alpha = rim;
                    }
                    else if (dIn < 2f)
                    {
                        c = Color.Lerp(c, Rgb(0x05, 0x07, 0x0A), 0.85f); // 暗描边是墨系
                        alpha = s.rimDarkAlpha;
                    }
                    else if (dIn < 4.5f)
                    {
                        alpha = s.edgeAlpha;
                    }
                    else
                    {
                        alpha = Mathf.Lerp(s.edgeAlpha, s.fillAlpha, Mathf.Clamp01((dIn - 4.5f) / 1.5f));
                    }

                    // 抗锯齿：形状外缘 1px 用覆盖度收 alpha。
                    float coverage = Mathf.Clamp01(0.5f - sd);
                    pixels[y * n + x] = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha) * coverage);
                }
            }

            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();

            border = new Vector4(g.border, g.border, g.border, g.border);
            return texture;
        }

        /// <summary>圆角矩形有符号距离场（负值 = 形状内）。</summary>
        static float SdRoundBox(float px, float py, float halfExtent, float radius)
        {
            float qx = Mathf.Abs(px) - (halfExtent - radius);
            float qy = Mathf.Abs(py) - (halfExtent - radius);
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f)
                + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>确定性 2D 哈希 → [0,1)（噪点用；避免 Random 让每台机器出不同贴图）。</summary>
        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static Color Rgb(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        // ------------------------------------------------------------------
        // 落盘 + 九宫格导入
        // ------------------------------------------------------------------

        /// <summary>
        /// 生成 → 写 PNG → 按九宫格导入。失败时退回内存 Sprite（外观一致但不持久化），
        /// 绝不静默返回 null（否则调用方会拿到白块，且"看着有 UI、其实没材质"）。
        /// </summary>
        static Sprite Generate(string assetPath, Tone tone, Geo geo)
        {
            GeoSpec g = GeoOf(geo);
            Texture2D texture = null;
            try
            {
                EnsureFolder("Assets/Art");
                EnsureFolder("Assets/Art/Sprites");
                EnsureFolder(SpriteFolder);

                texture = CreateTexture(tone, geo, out Vector4 border);
                byte[] png = texture.EncodeToPNG();

                string absolutePath = Path.Combine(Application.dataPath,
                    assetPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(absolutePath, png);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 100f;         // 与旧 Skin 同口径：1px = 1 UI px
                    importer.spriteBorder = border;              // 九宫格切片（圆角不变形）
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;  // 渐变+噪点禁不起压缩
                    importer.SaveAndReimport();
                }
                else
                {
                    Debug.LogWarning("[GlassPanelSpriteBuilder] 读不到 TextureImporter：" + assetPath
                        + "，九宫格边框可能未生效。");
                }

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                {
                    Object.DestroyImmediate(texture);
                    return sprite;
                }

                Debug.LogWarning("[GlassPanelSpriteBuilder] 生成失败：" + assetPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GlassPanelSpriteBuilder] 生成异常（" + assetPath + "）：" + e.Message
                    + "\n  退回内存 Sprite（外观一致，但不随场景持久化）。");
            }

            // 兜底：内存 Sprite（Sprite.Create 带 border 重载，切片语义照样成立）。
            if (texture == null)
                texture = CreateTexture(tone, geo, out _);
            var fallback = Sprite.Create(
                texture,
                new Rect(0f, 0f, g.size, g.size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(g.border, g.border, g.border, g.border));
            fallback.name = AssetNameOf(tone, geo);
            return fallback;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
