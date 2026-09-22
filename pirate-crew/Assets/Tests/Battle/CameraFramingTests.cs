using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="CameraFraming"/> 纯数学测试（无头验证台可跑，不实例化 MonoBehaviour）。
    ///
    /// 【等价性基准】断言值来自 2026-09-23 的实机探针：旧 Cinemachine 链
    /// （Transposer 阻尼 0 + Aim 档为空）在偏移旋转 60° 前后，
    /// 相机位置/朝向的实测值与本项目纯函数逐位一致——这是"去 Cinemachine 化不改变实机行为"的数学锚点：
    ///   · yaw 0 偏移 = (18.3712, 15, 18.3712)（= 实机 transposer.m_FollowOffset）；
    ///   · 朝向 euler = (30, 225, 0)（= 实机主相机 rotation，且**不随偏移旋转变化**——环绕不重瞄）；
    ///   · yaw 60° 偏移 = (25.096, 15, -6.724)（= 实机探针 posB − target）。
    /// </summary>
    [TestFixture]
    public class CameraFramingTests
    {
        // ------------------------------------------------------------------
        // 机位偏移（与实机 Transposer 的 FollowOffset 逐位等价）
        // ------------------------------------------------------------------

        [Test]
        public void FocusOffset_YawZero_MatchesLiveTransposerOffset()
        {
            // θ=30°：水平分量 = cos30°·0.7071·30 = 18.37117，竖直 = sin30°·30 = 15（实机实测值）。
            Vector3 offset = CameraFraming.ComputeFocusOffset(0f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance);
            Assert.AreEqual(18.37117f, offset.x, 1e-4f, "水平 X = cosθ·0.7071·d");
            Assert.AreEqual(15f, offset.y, 1e-4f, "竖直 Y = sinθ·d");
            Assert.AreEqual(offset.x, offset.z, 1e-5f, "水平分量在 X/Z 等分（方位 45°——对称菱形构图）");
        }

        [Test]
        public void FocusOffset_YawRotatesAroundWorldUp()
        {
            // 右键环绕 = 偏移绕世界 Y 轴旋转，模长不变（距离恒 30，缩放不改写距离）。
            Vector3 zero = CameraFraming.ComputeFocusOffset(0f, 30f, 30f);
            Vector3 yawed = CameraFraming.ComputeFocusOffset(60f, 30f, 30f);
            Assert.AreEqual(25.096f, yawed.x, 1e-2f, "yaw 60° 的 X = 实机探针 posB.x");
            Assert.AreEqual(15f, yawed.y, 1e-4f, "旋转绕 Y 轴，高度分量不变");
            Assert.AreEqual(-6.724f, yawed.z, 1e-2f, "yaw 60° 的 Z = 实机探针 posB.z");
            Assert.AreEqual(zero.magnitude, yawed.magnitude, 1e-3f, "环绕不改写机位距离");
        }

        [Test]
        public void ComputePosition_FocusPlusOffset()
        {
            Vector3 focus = new Vector3(10f, 1.2f, 20f);
            Vector3 position = CameraFraming.ComputePosition(focus, 0f, 30f, 30f);
            Assert.AreEqual(focus + CameraFraming.ComputeFocusOffset(0f, 30f, 30f), position);
        }

        // ------------------------------------------------------------------
        // 朝向（实机等价关键：环绕不重瞄）
        // ------------------------------------------------------------------

        [Test]
        public void Rotation_IsBakedView_AndIgnoresYaw()
        {
            // 【环绕不重瞄】实机探针：偏移旋转 60° 后朝向逐位不变（euler 30/225/0）。
            // 断言走 Rotate（纯托管；eulerAngles/Quaternion.Equals 是 icall，无头环境跑不了）：
            // 视线方向必须是"从机位指向焦点的方向"= -OffsetDirectionForPitch(30°)。
            Quaternion noRoll = CameraFraming.ComputeRotation(0f);
            Vector3 viewDir = CameraFraming.Rotate(noRoll, Vector3.forward);
            Vector3 expectedView = -CameraFraming.OffsetDirectionForPitch(CameraFraming.BasePitchDegrees);
            Assert.AreEqual(expectedView.x, viewDir.x, 1e-4f, "视线 X（俯角 30° + 方位 225°，实机实测值）");
            Assert.AreEqual(expectedView.y, viewDir.y, 1e-4f, "视线 Y（俯角 30°，实机实测值）");
            Assert.AreEqual(expectedView.z, viewDir.z, 1e-4f, "视线 Z（方位 45° 基准，实机实测值）");
            Assert.AreEqual(1f, viewDir.magnitude, 1e-4f, "视线方向应为单位向量");

            // 若有人"顺手修正"成重瞄（朝向跟随偏移方向），会与实机行为分叉——这条守住等价性：
            // yaw 60° 的"重瞄视线"与烘焙视线相差约 60°，而本函数的视线恒定不变。
            Vector3 yawedOffset = CameraFraming.ComputeFocusOffset(60f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance);
            Vector3 reAimedView = -yawedOffset.normalized;
            Assert.Greater(Vector3.Angle(viewDir, reAimedView), 30f,
                "重瞄视线与烘焙视线应相差巨大（本函数保持不重瞄，实机等价）");
            Assert.AreEqual(0f, Vector3.Angle(viewDir, expectedView), 1e-3f, "本函数视线 = 烘焙视线（不随 yaw 变化）");
        }

        [Test]
        public void Rotation_RollRotatesAroundCameraForward()
        {
            // 滚转 = 绕相机本地 forward（与原 LensSettings.Dutch 的应用方式一致）。
            // 断言用 Rotate + 向量夹角（纯托管）：滚转 10° 后，相机 up 绕视线轴恰好转 10°。
            Quaternion noRoll = CameraFraming.ComputeRotation(0f);
            Quaternion rolled = CameraFraming.ComputeRotation(10f);

            Vector3 viewAxis = CameraFraming.Rotate(noRoll, Vector3.forward);
            Vector3 upBefore = CameraFraming.Rotate(noRoll, Vector3.up);
            Vector3 upAfter = CameraFraming.Rotate(rolled, Vector3.up);
            Assert.AreEqual(10f, Vector3.Angle(upBefore, upAfter), 1e-2f, "滚转 10° 应恰好偏转相机 up 10°");
            Assert.AreEqual(0f, Vector3.Angle(viewAxis, CameraFraming.Rotate(rolled, Vector3.forward)), 1e-3f,
                "滚转不改视线方向（只绕视线轴）");

            // 转轴必须是相机本地 forward：upBefore/upAfter 差向量应垂直于视线轴。
            Assert.AreEqual(0f, Vector3.Dot((upAfter - upBefore).normalized, viewAxis), 1e-3f,
                "滚转的转轴应垂直于视线（即绕视线轴滚动）");
        }

        // ------------------------------------------------------------------
        // OrthoSize 合成（与原 ApplyFov 逐式等价）
        // ------------------------------------------------------------------

        [Test]
        public void ComposeOrthoSize_NoEffects_IsManualTierExactly()
        {
            Assert.AreEqual(7f, CameraFraming.ComposeOrthoSize(7f, 0f, true, false, false, 0f, 0.3f, 1.5f), 1e-4f);
        }

        [Test]
        public void ComposeOrthoSize_ScopeFull_ScalesByFovRatio()
        {
            // Scope 完全收敛：fov 60 → 28 ⇒ size × 28/60（画面放大 2.14 倍）。
            Assert.AreEqual(7f * 28f / 60f,
                CameraFraming.ComposeOrthoSize(7f, 1f, true, false, false, 0f, 0.3f, 1.5f), 1e-4f);
        }

        [Test]
        public void ComposeOrthoSize_SpectatorWiden_AndPushInPeak()
        {
            // 旁观：+1.5° 外扩；推近中点：−1.5° 收窄（sin(π/2)=1 的峰值）。
            float spectator = CameraFraming.ComposeOrthoSize(7f, 0f, true, true, false, 0f, 0.3f, 1.5f);
            Assert.AreEqual(7f * 61.5f / 60f, spectator, 1e-4f, "旁观外扩按当量比率作用");

            float pushInPeak = CameraFraming.ComposeOrthoSize(7f, 0f, true, false, true, 0.15f, 0.3f, 1.5f);
            Assert.AreEqual(7f * 58.5f / 60f, pushInPeak, 1e-4f, "推近峰值 = −1.5° 当量");

            // aiSpectatorEnabled=false 时旁观不生效。
            float spectatorOff = CameraFraming.ComposeOrthoSize(7f, 0f, false, true, false, 0f, 0.3f, 1.5f);
            Assert.AreEqual(7f, spectatorOff, 1e-4f, "旁观开关关闭时不外扩");
        }

        [Test]
        public void ComposeOrthoSize_NeverBelowOne()
        {
            // 极端 Scope + 小手动档也不许穿透 1（原 ApplyFov 的 max(1, …) 兜底）。
            Assert.GreaterOrEqual(CameraFraming.ComposeOrthoSize(1f, 1f, true, false, false, 0f, 0.3f, 1.5f), 1f);
        }

        // ------------------------------------------------------------------
        // 焦点 / 平面位移
        // ------------------------------------------------------------------

        [Test]
        public void FocusTargetPoint_RaisesByLookAtHeight()
        {
            Vector3 focus = CameraFraming.FocusTargetPoint(new Vector3(1f, 0.25f, 2f));
            Assert.AreEqual(0.25f + CameraFraming.UnitVisualHeight * CameraFraming.LookAtHeightRatio, focus.y, 1e-4f,
                "焦点抬高 = 单位视觉高 × 0.65（看向胸/头）");
            Assert.AreEqual(1f, focus.x, 1e-4f);
            Assert.AreEqual(2f, focus.z, 1e-4f);
        }

        [Test]
        public void PlaneOffsetToWorld_UsesCameraBasis()
        {
            Quaternion rotation = CameraFraming.ComputeRotation(0f);
            Vector3 world = CameraFraming.PlaneOffsetToWorld(new Vector2(1f, 2f), rotation);
            Vector3 expected = CameraFraming.Rotate(rotation, Vector3.right) * 1f
                + CameraFraming.Rotate(rotation, Vector3.up) * 2f;
            // 按分量带容差断言（裸 NUnit 的 Vector3 相等是逐位比较，浮点累加会差在最后一位）。
            Assert.AreEqual(expected.x, world.x, 1e-4f, "x 沿相机右");
            Assert.AreEqual(expected.y, world.y, 1e-4f, "y 沿相机上");
            Assert.AreEqual(expected.z, world.z, 1e-4f, "z 分量（x 沿右 y 沿上 ⇒ z 只有舍入残差）");

            Assert.AreEqual(Vector3.zero, CameraFraming.PlaneOffsetToWorld(Vector2.zero, rotation),
                "零位移直接返回零向量");
        }

        // ------------------------------------------------------------------
        // 常量口径（防止"改一处漏一处"）
        // ------------------------------------------------------------------

        [Test]
        public void FramingConstants_MatchRulingValues()
        {
            Assert.AreEqual(30f, CameraFraming.BasePitchDegrees, 1e-4f, "俯角锁 30°（与出图口径同一常量）");
            Assert.AreEqual(30f, CameraFraming.BaseDistance, 1e-4f, "机位距离 30（正交下只定机位）");
            Assert.AreEqual(60f, CameraFraming.BaseFov, 1e-4f, "FOV 当量分母 60");
            Assert.AreEqual(0.1f, CameraFraming.OrthoNearClip, 1e-4f, "近裁剪 = 原虚机 Lens 实机值");
            Assert.AreEqual(200f, CameraFraming.OrthoFarClip, 1e-4f, "远裁剪 = 原虚机 Lens 实机值（非主相机烘焙的 400）");
            Assert.AreEqual(1.85f, CameraFraming.UnitVisualHeight, 1e-4f, "单位视觉总高与 CrewVisualPrefabBuilder 同源");
        }
    }
}
