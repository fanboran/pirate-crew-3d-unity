using UnityEngine;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **像素化试点场景**的场景名与取景口径（唯一来源）。
    ///
    /// 【为什么单独一个类】这些量同时被三方读：编辑器侧的装配器（`PixelartPilotSetup` 造场景）、
    /// 运行时的出图脚本（`PlayerArtCapture` 摆机位）与判据脚本（`judge_pixelart_pilot.py` 算期望值）。
    /// 各写一份的后果实测过——装配器写 35.264°、出图脚本写 30°，于是"30° 那张对照图"根本不是
    /// 同一个场景的机位，比对结论全是错的。**凡是跨"编辑器装配 / 运行时采集 / 判据"的常量都放这里。**
    ///
    /// 【像素口径：锁"放大倍数"，不锁"画布高度"】见 <see cref="PixelScale"/> 的注释——
    /// 锁画布高度只在屏幕高是它的整数倍时成立，非整数放大就是"全屏像素不齐、放大看却正常"。
    ///
    /// 【俯角 30° 是裁决结果】地面轴的屏幕斜率 = sinθ：30° 恰为 0.5 = 横移 2 像素 / 下降 1 像素，
    /// 栅格化出的像素阶梯才是手绘像素等距那种**规则网格**；真等距 35.264°（= 等分 (1,1,1)/√3）
    /// 的斜率是 0.5773，与像素网格无整数比 ⇒ 阶梯长度逐段变化。
    /// </summary>
    public static class PixelartPilotScene
    {
        /// <summary>场景名（Build Settings 里的名字，也是装配器保存的资产名）。</summary>
        public const string SceneName = "PixelartPilot";

        /// <summary>
        /// **像素化档位 = 一个艺术像素占几个屏幕像素（锁死的整数放大倍数）**。
        ///
        /// 常见分辨率下都是严格整数倍：1920×1080 → 640×360、2560×1440 → 853×480（非整数）、
        /// 3840×2160 → 1280×720。**非整数放大是"全屏看着像素不齐"的头号原因**
        /// （块边长在 6/7 之间混排），所以这个数一旦定下就不要按分辨率去改它。
        ///
        /// 【为什么是 3 而不是 5】创始人 2026-09-22 裁决：**全局像素比例锁定 3×**——
        /// UI 的基本单位（`PixelSkin.Unit` = 3 屏幕像素）就是按这个口径设计的，
        /// 3D 与 UI 必须同一个艺术像素网格。`BeveledPixelSpriteBuilder` 的 u 对齐判据会断言本值。
        /// </summary>
        public const int PixelScale = 3;

        /// <summary>
        /// 参考画布高（1080p ÷ 3 = 360 艺术像素）。**只用于把"看得见多少米"换算成
        /// "每艺术像素多少米"**——真实画布尺寸由屏幕和 <see cref="PixelScale"/> 反推。
        /// </summary>
        public const int ReferenceRenderHeight = 360;

        /// <summary>俯角（度）。30° = 规则像素阶梯（2 像素横移 / 1 像素下降）。</summary>
        public const float PitchDegrees = 30f;

        /// <summary>方位角（度）：固定 45°，只有它给出对称菱形。</summary>
        public const float AzimuthDegrees = 45f;

        /// <summary>机位到构图中心的距离（正交相机下只影响裁剪，不影响观感）。</summary>
        public const float CameraDistance = 60f;

        /// <summary>构图中心：台阶腰高附近，不是地面（对着地面会把画面压到下半屏）。</summary>
        public static readonly Vector3 Target = new Vector3(0f, 1.2f, 0f);

        // ---------------- 机位梯子（按"可见多少米高"给，与分辨率解耦）----------------
        // 一个艺术像素的世界尺寸 = 可见高度 ÷ 参考画布高（1080p 下 360）。
        // **可见米数是美术锚、不随 PixelScale 变**：5×→3× 时取景不动、只是颗粒变细；
        // 而屏幕分辨率越高、艺术像素越多 ⇒ 可见范围越大（1080p 看 28m、1440p 看 37m）。

        /// <summary>宽机位：可见 28m 高（1080p 下正交 size 14；场景装配时相机就是这一档）。</summary>
        public const float WideVisibleMeters = 28f;

        /// <summary>中机位：可见 18m 高（正交 size 9）。</summary>
        public const float MidVisibleMeters = 18f;

        /// <summary>近机位：可见 8m 高（正交 size 4），用来看角色与描边的细节。</summary>
        public const float CloseVisibleMeters = 8f;

        /// <summary>把"可见多少米高"换算成一个艺术像素的世界尺寸（米）。</summary>
        public static float WorldPerPixel(float visibleMeters)
        {
            return visibleMeters / ReferenceRenderHeight;
        }

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
