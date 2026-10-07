using NUnit.Framework;
using UnityEngine;

namespace PirateCrew.Battle.Tests
{
    /// <summary>
    /// <see cref="CameraFraming"/> 纯数学测试（无头验证台可跑，不实例化 MonoBehaviour）。
    ///
    /// 【等价性基准】机位断言值来自 2026-09-23 的实机探针：旧 Cinemachine 链
    /// （Transposer 阻尼 0 + Aim 档为空）的 FollowOffset 与本项目纯函数逐位一致——
    ///   · yaw 0 偏移 = (18.3712, 15, 18.3712)（= 实机 transposer.m_FollowOffset）；
    ///   · yaw 60° 偏移 = (25.096, 15, -6.724)（= 实机探针 posB − target）。
    /// 【朝向规格已改（2026-09-23 裁决）】旧实机行为「朝向恒烘焙机位（环绕不重瞄）」被创始人
    /// 裁决推翻——环绕必须重瞄（画面绕焦点转动），朝向断言按新规格写（见 Rotation_LooksAtFocus）。
    /// 教训留档：等价性锚点只能锚"已裁决为正确"的行为；本文件曾把错误行为断言成测试。
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
        // 朝向（行为契约：环绕重瞄——右键环绕 = 画面绕焦点转动）
        // ------------------------------------------------------------------

        [Test]
        public void Rotation_LooksAtFocus_AndFollowsYaw()
        {
            // 【行为契约 docs/技术/相机行为契约.md】右键环绕 = 画面绕焦点转动：
            // 无论 yaw 多少，视线方向必须 = 从机位指向焦点的方向（重瞄）。
            // 旧断言"朝向恒烘焙视线（不重瞄）"是错误规格的可执行形式——把平移拖拽固化成了测试，
            // 连"防顺手修正"的反向断言都有；裁决后整段改写（教训见交接档 §七）。
            for (int yaw = 0; yaw <= 60; yaw += 30)
            {
                Vector3 forwardToFocus = (-CameraFraming.ComputeFocusOffset(
                    yaw, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance)).normalized;
                Quaternion rotation = CameraFraming.ComputeRotationLooking(forwardToFocus, 0f);
                Vector3 viewDir = CameraFraming.Rotate(rotation, Vector3.forward);
                Assert.AreEqual(0f, Vector3.Angle(viewDir, forwardToFocus), 1e-3f,
                    "yaw " + yaw + "°：视线必须正对焦点（环绕重瞄）");
            }

            // 防"退回旧语义"：yaw 0 → 60° 的视线必须大幅转动（旧实现的视线恒定不变）。
            Vector3 viewAt0 = CameraFraming.Rotate(CameraFraming.ComputeRotationLooking(
                (-CameraFraming.ComputeFocusOffset(0f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance)).normalized, 0f),
                Vector3.forward);
            Vector3 viewAt60 = CameraFraming.Rotate(CameraFraming.ComputeRotationLooking(
                (-CameraFraming.ComputeFocusOffset(60f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance)).normalized, 0f),
                Vector3.forward);
            Assert.Greater(Vector3.Angle(viewAt0, viewAt60), 30f,
                "yaw 0 → 60° 视线应大幅转动；夹角过小说明退回了「环绕不重瞄」旧语义");
        }

        [Test]
        public void Rotation_RollRotatesAroundCameraForward()
        {
            // 滚转 = 绕相机本地 forward（与原 LensSettings.Dutch 的应用方式一致）。
            // 断言用 Rotate + 向量夹角（纯托管）：滚转 10° 后，相机 up 绕视线轴恰好转 10°。
            Vector3 forwardToFocus = (-CameraFraming.ComputeFocusOffset(
                0f, CameraFraming.BasePitchDegrees, CameraFraming.BaseDistance)).normalized;
            Quaternion noRoll = CameraFraming.ComputeRotationLooking(forwardToFocus, 0f);
            Quaternion rolled = CameraFraming.ComputeRotationLooking(forwardToFocus, 10f);

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
        // OrthoSize 合成（推近当量；Scope/旁观已随两态重构退役）
        // ------------------------------------------------------------------

        [Test]
        public void ComposeOrthoSize_NoEffects_IsManualTierExactly()
        {
            Assert.AreEqual(7f, CameraFraming.ComposeOrthoSize(7f, false, 0f, 0.3f, 1.5f), 1e-4f);
        }

        [Test]
        public void ComposeOrthoSize_PushInPeak_ScalesByFovRatio()
        {
            // 【距离恒定收口（创始人 2026-10-07 裁决禁多镜头距离）】推近当量退役——任何参数下
            // 都返回 manualOrthoSize 本身，不再按 FOV 比率缩放。
            float pushInPeak = CameraFraming.ComposeOrthoSize(7f, true, 0.15f, 0.3f, 1.5f);
            Assert.AreEqual(7f, pushInPeak, 1e-4f, "推近不再改变距离");
        }

        [Test]
        public void ComposeOrthoSize_NeverBelowOne()
        {
            // 小手动档 + 推近峰值也不许穿透 1（原 ApplyFov 的 max(1, …) 兜底）。
            Assert.GreaterOrEqual(CameraFraming.ComposeOrthoSize(1f, true, 0.15f, 0.3f, 30f), 1f);
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
            // 近裁剪保持小正数：平面之后的物体**整件**由近裁剪剔除；"跨平面被切一半"由
            // PirateCrew.SceneArt.NearClipHider（逐物体包围球判据）兜底成"整件消失"，
            // 不走"把近平面推远"那条路（那会让相机平面之后的物体也渲染出来）。
            Assert.AreEqual(0.1f, CameraFraming.OrthoNearClip, 1e-4f,
                "近裁剪 = 原虚机 Lens 实机值（切一半由 NearClipHider 兜底，不靠推远近平面）");
            Assert.AreEqual(200f, CameraFraming.OrthoFarClip, 1e-4f, "远裁剪 = 原虚机 Lens 实机值（非主相机烘焙的 400）");
            Assert.AreEqual(2.0321f, CameraFraming.UnitVisualHeight, 1e-4f, "单位视觉总高与 CrewVisualPrefabBuilder 两件式常量同源（2.0321）");
        }

        // ------------------------------------------------------------------
        // 远裁剪需求（契约：由 orthoSize 推导，不是写死的大数）
        // ------------------------------------------------------------------

        [Test]
        public void MinFarClipForOrthoSize_CoversVisibleGroundRange()
        {
            // θ=30°、BaseDistance=30 ⇒ 屏顶地面片元的视线深度 = 30 + orthoSize·cot30° = 30 + orthoSize·√3。
            Assert.AreEqual(30f + 7f * Mathf.Sqrt(3f), CameraFraming.MinFarClipForOrthoSize(7f), 1e-3f,
                "基准档（orthoSize 7）≈ 42");
            Assert.AreEqual(30f + 60f * Mathf.Sqrt(3f), CameraFraming.MinFarClipForOrthoSize(60f), 1e-3f,
                "全景上限（orthoSize 60）≈ 134");
        }

        [Test]
        public void MinFarClipForOrthoSize_WithinOrthoBaseline_AndMonotonic()
        {
            // 正交基线 200 覆盖现役最松档位（60）——即战斗侧无需按整盘口径（OceanFarClipMin）抬高远裁剪。
            // 若本条不再成立，说明取景档被放宽了，须重评远裁剪策略（而不是顺手把断言改成新数）。
            Assert.Less(CameraFraming.MinFarClipForOrthoSize(CameraFraming.PanoramaMaxOrthoSize),
                CameraFraming.OrthoFarClip,
                "最松档位的需求量必须落在正交基线内");

            // 单调不减：防把本函数退化成与 orthoSize 无关的常数。
            Assert.Greater(CameraFraming.MinFarClipForOrthoSize(60f),
                CameraFraming.MinFarClipForOrthoSize(7f), "档位越松，需求量越大");
        }
    }
}
