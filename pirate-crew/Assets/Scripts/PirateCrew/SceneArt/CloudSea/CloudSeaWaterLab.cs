using PirateCrew.WaterLab;
using UnityEngine;

namespace PirateCrew.SceneArt.CloudSea
{
    /// <summary>
    /// 云端漫步海床 → WaterLab 复刻水的**地形源与域配置**（挂 CloudWalk.prefab 根，
    /// 由 CloudSeaAssetBaker 与 <see cref="WaterLabSimulation"/> 一起接线）。
    ///
    /// 【做什么】实现 <see cref="IWaterLabTerrain"/>：把 Blender 出库的海床高度场
    /// （mod_sea.py 的 <c>bed_z</c> 公式族，含岩簇丘包与边缘下沉）移植成纯 C# 采样，
    /// 供模拟核的初始水柱 / BlurFloor / 潮位目标读取。公式逐行对照 mod_sea.py
    /// （2026-11-20 翻号后的 Blender 本地系 = Unity 世界系，bakeAxisConversion：
    /// Blender (x, y, z) → Unity (x, z, y)，本文档内统一写 Unity 系）。
    ///
    /// 【坐标约定】模拟核内部以原点为中心（compute/格距口径不变），世界偏移全在
    /// <see cref="WaterLabSimulation.domainCenter"/>：本组件收到的 (x, z) 是**域局部
    /// 坐标**，换算世界 = (x + centerX, z + centerZ)。返回值是**模拟局部 Y**：
    /// groundSim = bedWorldY − meshWorldY，其中 meshWorldY = 水面静息世界高
    /// (<see cref="SurfaceWorldY"/>) − <see cref="WaterLabRules.SeaLevel"/>（ocean 网格
    /// 就摆在那，VS 按 η 绝对高程位移）。等价说法：水面 = SeaLevel、海床按"离水面的
    /// 深度负值"进模拟（例：海床 −2.5、水面 −0.4 → groundSim = −1.1，水柱 2.1 m）。
    ///
    /// 【参数纪律】g / 格距 / 子步 / 波幅等模拟参数全在 WaterLabRules，一个不动；
    /// 本组件只供**地形数据源**（AGENTS.md 复刻参数纪律）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CloudSeaWaterLab : MonoBehaviour, IWaterLabTerrain
    {
        [Header("域中心（世界 XZ，米）——同步写进 WaterLabSimulation.domainCenter")]
        [SerializeField] Vector2 domainCenter = new Vector2(0f, 0f);

        [Header("水面静息世界高（米）= 现役死亡线；η ±0.2 m 潮位在其上起伏")]
        [SerializeField] float surfaceWorldY = -0.4f;

        /// <summary>ocean 网格的世界 Y（VS 以 η 绝对高程位移顶点，见类头坐标约定）。
        /// 静息 η = SeaLevel − DryDepth（前 5 cm 结合水不上屏，WaterLabRules.DryDepth），
        /// 网格基准按它对齐——否则静息水面比死亡线低 5 cm。</summary>
        public float OceanMeshWorldY =>
            surfaceWorldY - (WaterLabRules.SeaLevel - WaterLabRules.DryDepth);

        /// <summary>域中心（baker 读去同步 WaterLabSimulation.domainCenter）。</summary>
        public Vector2 DomainCenter => domainCenter;

        // ---- 海床高度场（mod_sea.py 逐行移植；常量与 Blender 侧同源）----

        /// <summary>细节网格半宽（mod_sea X0/X1/Y0/Y1）：四边 1.2 m 内平滑收到深海底板。</summary>
        const float DetailHalfX = 19.0f;
        const float DetailHalfY = 14.0f;

        /// <summary>外围圈层半宽（±80 细节场 / ±240 外围）：场外恒为深海底板高。</summary>
        const float FieldHalfX = 80.0f;
        const float FieldHalfY = 80.0f;

        /// <summary>水底底板高（mod_sea BOT = cloudsea_common.SEA_BOTTOM_Z）。</summary>
        const float SeaBottomZ = -3.2f;

        /// <summary>岩簇锚点（mod_sea.MOD_ROCK_CLUSTERS：(x, z, s) = cloudsea_common 前五
        /// + 前场三座），丘包叠加源。</summary>
        static readonly Vector3[] RockClusters =
        {
            new Vector3(-3.0f, -2.0f, 1.4f),
            new Vector3(8.0f, 4.0f, 1.8f),
            new Vector3(-11.0f, 3.0f, 1.2f),
            new Vector3(14.0f, -4.0f, 1.6f),
            new Vector3(2.0f, 8.0f, 1.1f),
            new Vector3(13.2f, 11.4f, 1.3f),
            new Vector3(16.4f, 3.0f, 1.1f),
            new Vector3(17.3f, -9.0f, 1.2f),
        };

        /// <summary>珊瑚丘锚点（mod_sea.CORAL_SPOTS 的 22 处 x/z；丘包 +0.16 m、半径 0.85 m）。
        /// 与 Blender 侧同源，改表必须两边一起改。</summary>
        static readonly Vector2[] CoralMounds =
        {
            new Vector2(0.0f, 6.8f), new Vector2(-6.0f, 5.5f),
            new Vector2(7.5f, 4.0f), new Vector2(2.0f, 13.0f),
            new Vector2(12.0f, 12.0f), new Vector2(18.5f, 5.0f),
            new Vector2(-1.6f, 7.3f), new Vector2(-3.0f, 4.5f),
            new Vector2(-7.5f, 7.0f), new Vector2(-1.5f, 4.8f),
            new Vector2(5.5f, 3.0f), new Vector2(9.0f, 5.5f),
            new Vector2(4.0f, 7.0f), new Vector2(2.0f, 14.0f),
            new Vector2(14.0f, 12.5f), new Vector2(18.0f, 9.0f),
            new Vector2(-9.0f, -4.0f), new Vector2(4.0f, -8.0f),
            new Vector2(10.0f, 8.0f), new Vector2(-10.0f, -6.5f),
            new Vector2(8.0f, -6.5f), new Vector2(-2.0f, -6.0f),
        };

        /// <summary>沙纹振幅（mod_sea.SAND_RIPPLE_AMP：3-4 m 波长的规则起伏）。</summary>
        const float SandRippleAmp = 0.025f;

        /// <summary>
        /// 海床世界 Y。细节场外（|x|>FieldHalfX 等）恒 <see cref="SeaBottomZ"/>
        /// （外围深海底板），细节场内 = 沙丘场 + 沙纹 + 岩簇/珊瑚丘包，边缘 1.2 m 平滑下沉。
        /// </summary>
        public float GroundWorldY(float worldX, float worldZ)
        {
            float h = RidgeValley(worldX, worldZ);
            // 沙纹（mod_sea.bed_z 同式）：3-4 m 波长的细碎起伏，透水读成沙面纹路。
            h += SandRippleAmp
                * Mathf.Sin(worldX * 1.65f + 0.9f * Mathf.Sin(worldZ * 0.35f))
                * Mathf.Cos(worldZ * 1.35f + 0.4f);
            for (int i = 0; i < RockClusters.Length; i++)
            {
                float dx = worldX - RockClusters[i].x;
                float dz = worldZ - RockClusters[i].y;
                float s = RockClusters[i].z;
                float d2 = (dx * dx + dz * dz) / Mathf.Max(0.5f, s * s * 1.6f);
                h += 0.18f * Mathf.Exp(-d2);
            }
            for (int i = 0; i < CoralMounds.Length; i++)
            {
                float dx = worldX - CoralMounds[i].x;
                float dz = worldZ - CoralMounds[i].y;
                float d2 = (dx * dx + dz * dz) / (0.85f * 0.85f);
                h += 0.16f * Mathf.Exp(-d2);
            }
            float rim = RimFactor(worldX, worldZ, 1.2f);
            return SeaBottomZ + (h - SeaBottomZ) * rim;
        }

        /// <summary><see cref="IWaterLabTerrain"/>：域局部 (x,z) → 模拟局部 Y（见类头）。</summary>
        public float GroundHeight(float x, float z)
        {
            float bedWorldY = GroundWorldY(x + domainCenter.x, z + domainCenter.y);
            return bedWorldY - OceanMeshWorldY;
        }

        /// <summary>
        /// 边缘下沉因子（mod_sea._rim_f）：距细节场外缘 margin 内 smoothstep 收到 0；
        /// 场外返回 0（= 深海底板高）。
        /// </summary>
        static float RimFactor(float x, float z, float margin)
        {
            float ex = Mathf.Min((FieldHalfX - x) / margin, (x + FieldHalfX) / margin);
            float ey = Mathf.Min((FieldHalfY - z) / margin, (z + FieldHalfY) / margin);
            float f = Mathf.Clamp01(Mathf.Min(ex, ey));
            return f * f * (3f - 2f * f);
        }

        /// <summary>
        /// 沙丘形态场（mod_sea._ridge_valley）：h(t) 软夹三档——脊顶平台化、洼底持平、
        /// 坡面陡。值域 [−1.15, −0.75]（2026-11-22 浅海化：水深 0.35–0.75 m）。
        /// </summary>
        static float RidgeValley(float x, float z)
        {
            float t = 0.5f
                + 0.26f * Mathf.Sin(x * 0.17f + 0.4f) * Mathf.Cos(z * 0.23f - 0.8f)
                + 0.16f * Mathf.Sin(x * 0.37f - z * 0.31f + 2.0f)
                + 0.10f * Mathf.Sin(x * 0.74f + z * 0.58f + 0.9f)
                + 0.07f * Mathf.Sin(x * 1.15f - z * 0.9f + 1.6f)
                + 0.05f * Mathf.Sin(x * 2.30f + z * 1.90f + 0.3f);
            t = Mathf.Clamp01(t);
            // 2026-11-22 浅海化（与 mod_sea._ridge_valley 同步）：水面 −0.4，
            // 海床 −1.52..−0.94 → 水深 0.54–1.12 m（原作 σ 的浅滩光学段）。
            if (t < 0.36f)
            {
                float u = t / 0.36f;
                return -1.15f + 0.08f * (u * u * (3f - 2f * u));
            }
            if (t < 0.62f)
            {
                float u = (t - 0.36f) / 0.26f;
                return -1.07f + 0.26f * (u * u * (3f - 2f * u));
            }
            float v = Mathf.Min(1.0f, (t - 0.62f) / 0.38f);
            return -0.81f + 0.06f * Mathf.Sin(v * Mathf.PI * 0.5f);
        }
    }
}
