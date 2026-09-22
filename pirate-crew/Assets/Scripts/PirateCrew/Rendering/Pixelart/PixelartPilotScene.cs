using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化试点场景**的场景名与取景口径（唯一来源）。
    ///
    /// 【为什么单独一个类】这些量同时被两边读：编辑器侧的装配器（`PixelartPilotSetup` 造场景）
    /// 和运行时的出图脚本（`PlayerArtCapture` 摆机位）。各写一份的后果实测过——
    /// 装配器写 35.264°、出图脚本写 30°，于是"30° 那张对照图"根本不是同一个场景的机位，
    /// 比对结论全是错的。**凡是跨"编辑器装配 / 运行时采集"的常量都放这里。**
    ///
    /// 【俯角 30° 是裁决结果】地面轴的屏幕斜率 = sinθ：30° 恰为 0.5 = 横移 2 像素 / 下降 1 像素，
    /// 栅格化出的像素阶梯才是手绘像素等距那种**规则网格**；真等距 35.264°（= 等分 (1,1,1)/√3）
    /// 的斜率是 0.5773，与像素网格无整数比 ⇒ 阶梯长度逐段变化（创始人判"这斜线怎么是这样的"）。
    /// 本档取 30°。
    /// </summary>
    public static class PixelartPilotScene
    {
        /// <summary>场景名（Build Settings 里的名字，也是装配器保存的资产名）。</summary>
        public const string SceneName = "PixelartPilot";

        /// <summary>
        /// 低分辨率 RT 高。**216**（16:9 即 384×216），在 1920 宽屏上 1 像素 = **5 屏幕像素**。
        ///
        /// 【为什么是 216】1920 宽屏要"一像素恰好整数屏幕像素"，RT 宽必须整除 1920：
        /// 320 → 块 6、**384 → 块 5（本档）**、480 → 块 4、640 → 块 3。
        /// 档位台账见 docs/技术/渲染/像素化着色路径.md §5。
        /// </summary>
        public const int RenderHeight = 216;

        /// <summary>俯角（度）。30° = 规则像素阶梯（2 像素横移 / 1 像素下降）。</summary>
        public const float PitchDegrees = 30f;

        /// <summary>方位角（度）：固定 45°，只有它给出对称菱形。</summary>
        public const float AzimuthDegrees = 45f;

        /// <summary>机位到构图中心的距离（正交相机下只影响裁剪，不影响观感）。</summary>
        public const float CameraDistance = 60f;

        /// <summary>正交 size = 画面可见高度的一半（14 ⇒ 可见 28m 高）。</summary>
        public const float OrthoSize = 14f;

        /// <summary>构图中心：台阶腰高附近，不是地面（对着地面会把画面压到下半屏）。</summary>
        public static readonly Vector3 Target = new Vector3(0f, 1.2f, 0f);

        /// <summary>
        /// 相机相对构图中心的方向（单位向量）：俯角 θ、方位 φ 时 =
        /// <c>(cosθ·sinφ, sinθ, cosθ·cosφ)</c>（θ=30°、φ=45° ⇒ (0.6124, 0.5, 0.6124)）。
        /// 装配器与出图脚本共用它，保证"俯角"两边是同一个定义。
        /// </summary>
        public static Vector3 CameraDirection(float pitchDegrees, float azimuthDegrees)
        {
            float pitch = pitchDegrees * Mathf.Deg2Rad;
            float azim = azimuthDegrees * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Cos(pitch) * Mathf.Sin(azim),
                Mathf.Sin(pitch),
                Mathf.Cos(pitch) * Mathf.Cos(azim));
        }
    }
}
