using UnityEngine;

namespace PirateCrew.PirateCrew.SceneArt
{
    /// <summary>
    /// 天空三档预设（纯 C#，无头可测）。
    ///
    /// 【出处与标注】三档的**档位分配**是【依据】——原版水面/天空按 <c>skyColour=1/2/3</c> 切贴图，
    /// 1 = 关卡 1-5 / 16-21、2 = 6-10 / 22-27、3 = 11-15 / 28-33
    /// （`docs/参考游戏逆向-海盗军团抢宝藏-静态.md:676`、`Controller.as:311`）。
    /// **具体 RGB 全部是【AI 提案】**：原版只存了索引、未导出色值
    /// （`docs/场景设计-战斗竞技场.md` §6.1 明写「具体 RGB 全部是【AI 提案】」，
    /// 其 O5 列为待定：需原版截图取色后回填）。
    ///
    /// 【本类做什么/不做什么】只提供**数据**与关卡→档位的映射，不写 <c>RenderSettings</c>。
    /// 场景的天空盒 / 环境光 / 雾由渲染波次（`Assets/Editor/BattleSceneLighting.cs`）负责，
    /// 本工程不允许两处写同一字段（见报告「字段冲突」章节）。本类的用途：
    ///   ① 远景剪影 / 云 / 海床等**本波次自有几何**取该档的色值；
    ///   ② 给协调者/渲染波次一个「三档完整参数表」以便切换关卡时同步天空+雾+光照（美术风格指南 §4.6）。
    /// </summary>
    public static class SkyTierCatalog
    {
        /// <summary>一档天空的完整观感参数（含雾色，供渲染波次联动使用）。</summary>
        public readonly struct SkyTier
        {
            /// <summary>档号（对应原版 <c>skyColour</c> 1/2/3）。</summary>
            public readonly int Index;

            /// <summary>天顶色。</summary>
            public readonly string ZenithHex;

            /// <summary>地平线色（= 该档的雾色基准）。</summary>
            public readonly string HorizonHex;

            /// <summary>太阳盘色。</summary>
            public readonly string SunHex;

            /// <summary>地面回照色（环境光 Ground Color）。</summary>
            public readonly string GroundBounceHex;

            /// <summary>适用关卡（原版分配，见类头出处）。</summary>
            public readonly string LevelGroups;

            public SkyTier(int index, string zenith, string horizon, string sun, string groundBounce, string levelGroups)
            {
                Index = index;
                ZenithHex = zenith;
                HorizonHex = horizon;
                SunHex = sun;
                GroundBounceHex = groundBounce;
                LevelGroups = levelGroups;
            }
        }

        /// <summary>档 1「正午碧海」：关卡 1-5 / 16-21（level_1 用此档）。</summary>
        public static readonly SkyTier Noon = new SkyTier(
            1, "#4DA6D9", "#C8DDF0", "#FFF4E0", "#E8D5A3", "1-5 / 16-21");

        /// <summary>档 2「黄昏暖调」：关卡 6-10 / 22-27。</summary>
        public static readonly SkyTier Dusk = new SkyTier(
            2, "#3E7FB5", "#F2D9A8", "#FFF0C8", "#C4A76A", "6-10 / 22-27");

        /// <summary>档 3「风暴阴郁」：关卡 11-15 / 28-33。</summary>
        public static readonly SkyTier Storm = new SkyTier(
            3, "#244A72", "#A8B8C8", "#FFE8D0", "#6E7A82", "11-15 / 28-33");

        /// <summary>全部三档（索引 = 档号 - 1）。</summary>
        public static readonly SkyTier[] All = { Noon, Dusk, Storm };

        /// <summary>
        /// 按原版关卡分组规则取档号（1/2/3）。超出 1-33 或未覆盖的关卡回落到档 1。
        /// 规则出处：`参考游戏逆向…静态.md:676`。
        /// </summary>
        public static int TierIndexForLevel(int levelNumber)
        {
            if (levelNumber >= 1 && levelNumber <= 5) return 1;
            if (levelNumber >= 6 && levelNumber <= 10) return 2;
            if (levelNumber >= 11 && levelNumber <= 15) return 3;
            if (levelNumber >= 16 && levelNumber <= 21) return 1;
            if (levelNumber >= 22 && levelNumber <= 27) return 2;
            if (levelNumber >= 28 && levelNumber <= 33) return 3;
            return 1;
        }

        /// <summary>取某关的天空档（level_1 → 档 1）。</summary>
        public static SkyTier ForLevel(int levelNumber)
        {
            return All[TierIndexForLevel(levelNumber) - 1];
        }

        // ------------------------------------------------------------------
        // 远景剪影分层色（【AI 提案】：满足"逐层提亮 + 雾衰减"的验收）
        // ------------------------------------------------------------------

        /// <summary>远景剪影层数（近 / 中 / 远）。</summary>
        public const int FarSilhouetteLayers = 3;

        /// <summary>
        /// 远景剪影"逐层提亮 + 雾衰减"的基色（【AI 提案】）。
        ///
        /// 【依据与目标】场景文档 §6.3 要求远岛按距离变浅；品控 B-2/B-3 要求
        /// 远岛至少 3 层、逐层提亮，远景岛与背景 ΔL* ≥ 25、地平线带 L* ≥ 78。
        /// 做法：先按层号在 <c>FarSilhouetteNear #7E93A8</c>（L*≈59）与
        /// <c>FarSilhouetteFar #AFC2D4</c>（L*≈78）之间线性插值，再把最远层向该档
        /// <see cref="SkyTier.HorizonHex"/>（雾色）混 25% —— 即"越远越亮、越蓝灰"。
        /// 近层保持近档原色，从而与雾化后的天空仍有足够 ΔL*（对比不糊）。
        /// </summary>
        /// <param name="layerIndex">层号：0=近 / 1=中 / 2=远。</param>
        /// <param name="levelNumber">关卡号（决定该档雾色）。</param>
        public static string FarSilhouetteHex(int layerIndex, int levelNumber = 1)
        {
            int last = FarSilhouetteLayers - 1;
            float t = last <= 0 ? 0f : Mathf.Clamp01(layerIndex / (float)last);

            Color near = SceneArtPalette.Hex(SceneArtPalette.FarSilhouetteNear);
            Color far = SceneArtPalette.Hex(SceneArtPalette.FarSilhouetteFar);
            Color baseColor = Blend(near, far, t);

            // 雾衰减：最远层再向地平线雾色靠 25%。
            Color fog = SceneArtPalette.Hex(ForLevel(levelNumber).HorizonHex);
            Color blended = Blend(baseColor, fog, 0.25f * t);
            return ToHex(blended);
        }

        /// <summary>两个颜色按 <paramref name="t"/> 线性插值（纯 C#，不用 <c>Color.Lerp</c> 也避免依赖）。</summary>
        static Color Blend(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(
                a.r + (b.r - a.r) * t,
                a.g + (b.g - a.g) * t,
                a.b + (b.b - a.b) * t,
                a.a + (b.a - a.a) * t);
        }

        /// <summary>颜色转 <c>#RRGGBB</c>（纯 C#，不用 <c>ColorUtility</c> 的 ECall）。</summary>
        static string ToHex(Color color)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }
    }
}
