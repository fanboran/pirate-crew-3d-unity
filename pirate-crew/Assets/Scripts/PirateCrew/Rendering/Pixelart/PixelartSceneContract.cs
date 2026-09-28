using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// 像素化场景的**装配契约常量**（运行时侧单一真源）。
    ///
    /// 【为什么要有这一层】像素场景（试点 + 样板关 + 海图）装配时会把像素档位、环境光、主光
    /// **序列化进场景文件**；编辑器侧的装配器（<c>PixelartStageKit</c> 等）从这里取值写入，
    /// 契约测试（<c>PixelartSceneContractTests</c>）读场景 YAML 时也跟这里比对。
    /// 教训：PixelScale 常量 3→2（创始人 2026-09-24 裁决）后，场景里的序列化副本没人重跑装配器
    /// 就静默停在 3——常量单源 ≠ 场景单源，序列化副本必须有人钉住，而钉它的测试在编辑器程序集外，
    /// 所以这组常量必须住在**运行时**程序集。
    /// </summary>
    public static class PixelartSceneContract
    {
        /// <summary>像素档位契约值 = 出图口径常量（场景序列化的 rig.pixelScale 必须等于它）。</summary>
        public static int PixelScale { get { return PixelartPilotScene.PixelScale; } }

        /// <summary>环境光（暗部色）sRGB hex。暗面 = albedo × 本色，明显暗于亮面、明显亮于墨线。
        ///
        /// 【2026-09-29 调值】原 #37486B（同明度蓝移）在 r14 实机图上把暗面推成"海军蓝"、且加法项
        /// 抬亮过大 ⇒ 改为同色相但更暗一点的 #313D57：暗面回到"暗蓝灰"，与亮面/墨线的分离度更大。
        /// 这是美术旋钮（不是机制）——要回退只改这一个 hex + 重烘像素场景。</summary>
        public const string AmbientHex = "333C4A";

        /// <summary>主光色 sRGB hex。
        ///
        /// 【2026-09-29 调值】原 #FFF5E0 的暖白在 r14 实机图上把受光面推到米黄（实测：砼 albedo
        /// #9E988A 的受光面成 #B0A58F，乘子偏暖即来自主光色）⇒ 改为近中性的 #FFF9EE：保留一丝暖意、
        /// 不再整体泛黄（2026-09-29 第二轮：进一步改纯中性 #FFFFFF）。同为美术旋钮，回退只改这一个 hex + 重烘像素场景。</summary>
        public const string SunColorHex = "FFFFFF";

        /// <summary>主光欧拉角（度）：仰角 58° 是"三档色带分离"的推导值，方位 140° 是左上光惯例。</summary>
        public static readonly Vector3 SunEulerDegrees = new Vector3(58f, 140f, 0f);

        /// <summary>环境光 Color（sRGB 语义；解析口径与 <see cref="PixelartMaterialFactory.Hex"/> 同一）。</summary>
        public static Color AmbientColor { get { return PixelartMaterialFactory.Hex(AmbientHex); } }

        /// <summary>主光 Color（sRGB 语义）。</summary>
        public static Color SunColor { get { return PixelartMaterialFactory.Hex(SunColorHex); } }
    }
}
