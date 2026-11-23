using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.Rendering.PostLine
{
    /// <summary>
    /// 渲染风格热切换的**材质换装层**：PostLine 链是单相机全分辨率渲染，物体要用
    /// 全分辨率着色 shader（<see cref="PostLinePath.ToonLitShaderName"/>）；CellShade 链的
    /// 物体材质走 G-buffer shader（<see cref="Pixelart.PixelartPath.ObjectShaderName"/>）。
    ///
    /// 【为什么是换装而不是"同材质双 pass"】两条链的物体 shader 数据流不同（一条写 7 张
    /// MRT、一条直接着色），合进一个 shader 会把两份 CBUFFER/两套光照代码塞在一起；
    /// 换装在切换时遍历一次场景、切回时还原，成本只在切换瞬间，且不改任何场景序列化。
    ///
    /// 【派生规则】新材质从源材质取"这个物体该长什么样"的公开参数（_BaseColor / 色带档数 /
    /// 抖动强度），其余用 PostLineToonLit 默认值；同一源材质只建一份，跨切换复用。
    /// </summary>
    public static class PostLineMaterialSwapper
    {
        /// <summary>源材质 → PostLine 材质（同一源只建一份；域重载清空）。</summary>
        static readonly Dictionary<Material, Material> s_Converted = new Dictionary<Material, Material>();

        /// <summary>已换装渲染器及其原始材质数组（切回时原样还原）。</summary>
        static readonly List<(Renderer renderer, Material[] originals)> s_Applied
            = new List<(Renderer, Material[])>();

        /// <summary>已处理过的渲染器 InstanceID（增量刷新用，避免重复换装）。</summary>
        static readonly HashSet<int> s_HandledIds = new HashSet<int>();

        /// <summary>当前是否处于 PostLine 换装态。</summary>
        public static bool Active { get; private set; }

        /// <summary>
        /// 切到 PostLine：遍历场景换装。**可重复调用**——战斗单位/地形/道具由
        /// BattleController 在 rig.OnEnable 之后运行时生成，首次 Enter 赶不上；
        /// rig 在 PostLine 模式下会定期调用 <see cref="Refresh"/> 补换装。
        /// </summary>
        public static void Enter()
        {
            Active = true;
            Refresh();
        }

        /// <summary>增量刷新：把新出现（运行时生成）的 PixelartObject 渲染器补换装。</summary>
        public static void Refresh()
        {
            if (!Active)
                return;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int convertedNow = 0;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || s_HandledIds.Contains(renderer.GetInstanceID()))
                    continue;

                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.shader == null
                        || source.shader.name != Pixelart.PixelartPath.ObjectShaderName)
                        continue;

                    materials[i] = GetOrCreate(source);
                    changed = true;
                }

                s_HandledIds.Add(renderer.GetInstanceID());
                if (!changed)
                    continue;

                convertedNow++;
                s_Applied.Add((renderer, renderer.sharedMaterials));
                renderer.sharedMaterials = materials;
            }

            if (convertedNow > 0)
            {
                global::PirateCrew.Core.Log.Info("[PostLineMaterialSwapper] Refresh：新换装 "
                    + convertedNow + " 个，累计处理 " + s_HandledIds.Count + " 个 renderer。");
            }
        }

        /// <summary>切回 CellShade：还原全部渲染器的原始材质。</summary>
        public static void Exit()
        {
            if (!Active)
                return;

            for (int i = s_Applied.Count - 1; i >= 0; i--)
            {
                (Renderer renderer, Material[] originals) = s_Applied[i];
                if (renderer != null)
                    renderer.sharedMaterials = originals;
            }
            s_Applied.Clear();
            s_HandledIds.Clear();
            Active = false;
        }

        static Material GetOrCreate(Material source)
        {
            if (s_Converted.TryGetValue(source, out Material converted) && converted != null)
                return converted;

            // 走 Resources 持有口（铁律：未引用 shader 在播放器里会被剥离；Shader.Find 在
            // 编辑器 Play 下也可能取不到非 Resources 的未引用 shader——本仓已有取用口不用绕）。
            Shader shader = Pixelart.PixelartShaders.Load(PostLinePath.ToonLitShaderName);
            if (shader == null)
            {
                global::PirateCrew.Core.Log.Warn(
                    "[PostLineMaterialSwapper] 找不到 shader " + PostLinePath.ToonLitShaderName + "，保持原材质。");
                return source;
            }

            converted = new Material(shader) { name = source.name + " (PostLine)" };

            // 从源材质派生可见参数：底色、色带档数、抖动强度。源材质字段名见
            // PixelartMaterialFactory（_BaseColor / _MainLightLevel / _DitherStrength）。
            if (source.HasProperty("_BaseColor"))
                converted.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            if (source.HasProperty("_MainLightLevel"))
                converted.SetFloat("_Cuts", Mathf.Clamp(Mathf.RoundToInt(source.GetFloat("_MainLightLevel")), 1, 8));
            if (source.HasProperty("_DitherStrength"))
            {
                float dither = source.GetFloat("_DitherStrength");
                converted.SetFloat("_DitherStrength", dither);
                converted.SetFloat("_UseDither", dither > 0f ? 1f : 0f);
            }

            // 【白云的影子色】参照库 Demo 的地形用 Patch + Palette 两道系统把"白/近白"读成
            // 有冷暖和层次的地形；本关云台是纯白 albedo，若照默认走"光色直乘"，4 档量化后
            // 暗面只剩灰黑。这里对云材质开 Palette：暗面 = albedo × 冷蓝灰、亮面 = 暖白——
            // 云仍是白的，但背光面读作天空反射的蓝，这是白云在 ToonLit 下的正确解。
            // 判定用材质名：只对**云体**开 Palette/Patch。不能用 "Cloud" 前缀粗判——
            // 云端漫步场景的海水/海草/鱼/羽毛/木/金属也叫 Cloud_*，套上云的冷蓝影调会把
            // 水下世界与点缀全部染成云色（2026-11-11 烘焙器审计）。
            bool isCloud = global::PirateCrew.Rendering.Pixelart.CloudSeaPath
                .IsCloudBodyMaterial(source.name);
            // 云体之外的海底自然物也开 Palette：海草/岩石/海床的 albedo 本来就深，走非 Palette
            // 分支时环境光/主光一低就整片压成黑块（2026-11-18 实拍：PostLine 水下只剩黑剪影）。
            // Palette 是 albedo 的乘色——阴影取 0.55–0.74 的中性冷灰，暗面仍有明确固有色，
            // 亮面回到原色，不改材质 hue；不能套云的 0.40–0.68 冷蓝影调，否则水下全染成云色。
            bool isNatural = !isCloud && source.name.StartsWith("Cloud_");
            if (isCloud || isNatural)
            {
                converted.SetFloat("_UsePalette", 1f);

                if (isCloud)
                {
                    // 阴影色压到中浅蓝灰：白 albedo × 0.58–0.80 ⇒ 背光面是明确的冷灰蓝，
                    // 但不再压成深蓝黑碗（实拍云腹过暗，创始人指云彩不好看）。
                    converted.SetColor("_ShadowColor", new Color(0.58f, 0.68f, 0.80f, 1f));
                    converted.SetColor("_HighlightColor", new Color(1f, 0.98f, 0.94f, 1f));

                    // 云台是大面积白面，源材质的 0.21 抖动在近景会读成棋盘格脏点；
                    // 参照 Demo 的强度默认 0.1，云台收到 0.08（只负责打散色带边界）。
                    converted.SetFloat("_DitherStrength", 0.08f);
                    converted.SetFloat("_UseDither", 1f);

                    // 大尺度斑块（参照 Demo 的地形 patch）：两层噪声只做极轻的冷白偏色，
                    // 让大片云顶不是一整块死白。噪声图缺失时 "black" 默认图 = 无斑块，不报错。
                    Texture2D noise2 = LoadNoise("Albedo2Noise");
                    Texture2D noise3 = LoadNoise("Albedo3Noise");
                    if (noise2 != null)
                    {
                        converted.SetTexture("_Noise2", noise2);
                        converted.SetColor("_Color2", new Color(0.955f, 0.975f, 1f, 1f));
                        converted.SetFloat("_Noise2Scale", 0.02f);
                        converted.SetFloat("_Noise2Threshold", 0.55f);
                    }
                    if (noise3 != null)
                    {
                        converted.SetTexture("_Noise3", noise3);
                        converted.SetColor("_Color3", new Color(0.90f, 0.94f, 0.985f, 1f));
                        converted.SetFloat("_Noise3Scale", 0.006f);
                        converted.SetFloat("_Noise3Threshold", 0.62f);
                    }
                }
                else
                {
                    converted.SetColor("_ShadowColor", new Color(0.55f, 0.63f, 0.74f, 1f));
                    converted.SetColor("_HighlightColor", new Color(1.02f, 1f, 0.96f, 1f));
                    converted.SetFloat("_UseDither", 1f);
                }
            }

            s_Converted[source] = converted;
            return converted;
        }

        /// <summary>斑块噪声按名从 Resources/PixelartNoise 取（缺图返回 null，材质走 "black" 默认）。</summary>
        static Texture2D LoadNoise(string name)
        {
            return Resources.Load<Texture2D>("PixelartNoise/" + name);
        }
    }
}
