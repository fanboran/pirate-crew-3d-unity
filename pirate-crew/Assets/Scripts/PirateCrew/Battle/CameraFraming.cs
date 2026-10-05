using UnityEngine;
// 类型别名：俯角常量与像素化出图口径同源（见 BasePitchDegrees 的注释），
// 全限定写太长、直接 using 整个命名空间又怕与既有类型重名，故用同类别名。
using PixelartPilotScene = PirateCrew.Rendering.Pixelart.PixelartPilotScene;

namespace PirateCrew.Battle
{
    /// <summary>
    /// 一帧取景意图（纯数据）。各表现系统（震屏/落水下压/Scope/推近/旁观）把各自的量写进
    /// <see cref="BattleCameraDriver"/> 的状态，由 Driver 每帧组装成 <see cref="CameraFrame"/>
    /// 并**唯一**地落到主相机上——「除 Driver 外无人写主相机」这条守卫的数据载体。
    /// </summary>
    public struct CameraFrame
    {
        /// <summary>最终相机位置（焦点 + 机位偏移 + 震屏/下压）。</summary>
        public Vector3 Position;

        /// <summary>最终朝向（看向焦点 + 震屏滚转——环绕重瞄裁决后朝向随焦点逐帧重算，
        /// 见 <see cref="CameraFraming.ComputeRotationLooking"/>；「恒烘焙机位」是已推翻的旧口径）。</summary>
        public Quaternion Rotation;

        /// <summary>最终正交半高（手动档 × 特效当量比率）。</summary>
        public float OrthoSize;
    }

    /// <summary>
    /// 战斗相机取景数学的纯 C# 汇总层：基准机位常量 + 机位偏移/朝向/OrthoSize 合成的纯函数。
    /// 不引用 MonoBehaviour、不实例化 GameObject，可在无头验证台（external/harness-*）直接断言；
    /// 胶水层是 <see cref="BattleCameraDriver"/>（唯一写入者）与 <see cref="BattleInputReader"/>（唯一输入采样，经交互控制器转手）。
    ///
    /// 【机位口径 — 等距像素卡通（创始人裁决 2026-09-22/23）】
    ///   正交投影、**俯角锁 30°**（与出图口径 <see cref="PixelartPilotScene.PitchDegrees"/> 同一常量，
    ///   "宣传图里的观感"才等于"玩的时候的观感"）、**方位可自由旋转**（右键拖拽只改偏移的方位角）、
    ///   **取景恒为基准档 <see cref="CloseUpOrthoSize"/>（可见 13.7 m）——没有滚轮缩放**；
    ///   以后说"缩放"只指像素比例（PixelScale 3:1 → 4:1/5:1/2:1，画面长相恒定）。
    ///
    /// 【环绕重瞄（2026-09-23 定案，推翻"不重瞄"旧口径）】
    ///   Cinemachine 时代的实机行为是：Transposer 只写位置、Aim 档为空 ⇒ 朝向恒烘焙机位，
    ///   右键环绕在视觉上是**平移拖拽**（实机探针验证过"逐位等价"——但等价的是错的行为）。
    ///   创始人连驳多次（"还是拖动""我的旋转呢"）后定案：**右键环绕 = 画面绕焦点转动**，
    ///   即 <see cref="ComputeRotationLooking"/>（LookRotation(focus − position)，本类头预告过的既定改法）。
    ///   已知取舍沿用 r12 裁决：只有方位 45° 及其对称位给出对称菱形，别的方位地面阶梯长短不一，不做 snap。
    /// </summary>
    public static class CameraFraming
    {
        // ------------------------------------------------------------------
        // 基准机位常量（档位一律走常量不走 SerializeField：场景由装配脚本烘焙，
        // 序列化字段的旧值会盖掉新档位口径——×2 扫荡期的老教训）。
        // ------------------------------------------------------------------

        /// <summary>
        /// 游戏内俯角（度）：**固定 30°**，方位角不进本常量（方位由 yaw 承担、可自由旋转）。
        /// 取值直接引用像素化出图口径 <see cref="PixelartPilotScene.PitchDegrees"/>，**不是各写一份的镜像**。
        /// sin 30° = 0.5 ⇒ 地面轴在屏幕上是横移 2 像素 / 下降 1 像素的规则像素阶梯；
        /// 真等距 35.264° 的 sin = 0.5773 与像素网格无整数比，阶梯长短不一，故已废。
        /// </summary>
        public const float BasePitchDegrees = PixelartPilotScene.PitchDegrees;

        /// <summary>
        /// 机位距离（世界单位）：正交下**不表达视野**（视野由 OrthoSize 决定），只决定机位高度与
        /// 裁剪范围。沿用 3D 空间契约的距离 30（格 1→2 单位后 ×2）。
        /// </summary>
        public const float BaseDistance = 30f;

        /// <summary>
        /// **近景档 OrthoSize（正交半高，两档取景的近档）= 可见 13.7 m**——中机位口径（r12 取景表）。
        /// 2026-09-30 定值 6.85（创始人定值；原 7 / 14 m）。**要改近景基准只动这一个数。**
        /// </summary>
        public const float CloseUpOrthoSize = 6.85f;

        /// <summary>全场档 OrthoSize：纵向 2×17=34u 覆盖样板关 30u 全场。
        /// 两档取景制（2026-10-05 走查反馈）下是远景档的下限（<see cref="PanoramaOrthoSizeForSpan"/>
        /// 的 clamp 下界）。</summary>
        public const int FullFieldOrthoSize = 17;

        /// <summary>全景档 OrthoSize 的上限（世界图大跨度时封顶；原退役常量 MaxOrthoSize 的唯一存活职责）。</summary>
        public const int PanoramaMaxOrthoSize = 60;

        /// <summary>默认地图可玩跨度（世界单位）= 现行竞技场 100u；未调 <see cref="PanoramaOrthoSizeForSpan"/> 对应档时的缺省。</summary>
        public const float DefaultWorldSpan = 100f;

        /// <summary>
        /// 单位视觉总高（世界单位）= 1.85：<c>CrewVisualPrefabBuilder.TargetUnitHeight</c> 的常量镜像
        /// （运行时不引用 Editor 程序集）。
        /// <b>【待标定】</b>：该镜像值尚未随现役造型总高（≈2.0321，见
        /// <c>docs/设计/美术/角色造型.md</c>）同步，取值待重新标定。
        /// </summary>
        public const float UnitVisualHeight = 1.85f;

        /// <summary>lookAt 抬高比例（用户裁决区间 0.6–0.7 取中值 0.65）：镜头看向单位胸/头部而非脚底。</summary>
        public const float LookAtHeightRatio = 0.65f;

        /// <summary>lookAt 抬高（世界单位）= UnitVisualHeight × LookAtHeightRatio ≈ 1.2025，作用在相机焦点上
        ///（随 UnitVisualHeight 待重新标定）。</summary>
        public static float LookAtHeight => UnitVisualHeight * LookAtHeightRatio;

        /// <summary>
        /// 烘焙 FOV 当量分母（正交下不参与投影）：推近等"FOV 特效"按
        /// fov/baseFov 的**当量比率**映射到 OrthoSize（手感量级连续）。与原虚机 Lens 的 60 同源。
        /// </summary>
        public const float BaseFov = 60f;

        /// <summary>正交近裁剪（原虚机 Lens 的实机值——CinemachineBrain 每帧把它推给主相机，此处取同一值）。</summary>
        public const float OrthoNearClip = 0.1f;

        /// <summary>正交远裁剪（原虚机 Lens 的实机值 200；主相机烘焙的 400 运行期一直被 Lens 覆盖）。</summary>
        public const float OrthoFarClip = 200f;

        /// <summary>
        /// **整张海面圆盘入画**所需的远裁剪（世界单位）：只适用于把 OceanRig 圆盘整盘框进画面的机位——
        /// 现行唯一消费方是出图路径 <c>ArtReview.PlayerArtCapture</c>（4200u 远场裙边与远景装饰要入画）。
        /// 圆盘半径 = <c>OceanGridRules.HorizonRadius</c>（4200），按 30° 俯角折算视线深度 ≈ 0.866×4200，本值留余量。
        ///
        /// 【不用于战斗相机】战斗侧海面跟随相机，其**可见部分**恒落在可见地面范围内（基准档 ≈42、
        /// 全景上限 ≈134），需求量由 <see cref="MinFarClipForOrthoSize"/> 推导；按整盘口径抬高会把
        /// 远裁剪放大 20 余倍（2026-09-30 前 <c>BattleController</c> 即如此，为已删除的世界海图引入）。
        /// 【3D 侧口径，无逆向出处】原版 Flash 无远裁剪概念。
        /// </summary>
        public const float OceanFarClipMin = 4500f;

        /// <summary>
        /// 战斗相机远裁剪面的**下界**：覆盖正交相机在给定取景档下**可见的地面范围**。
        ///
        /// 【推导】正交相机每个像素共享同一视线方向（俯角 <see cref="BasePitchDegrees"/>），
        /// 故屏幕上任意一点对应的地面片元，其视线深度只随**屏幕纵向偏移** b 变化：
        /// <c>depth(b) = BaseDistance + b·cot(俯角)</c>
        /// （相机在焦点斜上方、焦点地面深度 = BaseDistance，沿视线轴的投影关系即此式）。
        /// 取屏顶 b = orthoSize（OrthoSize 即正交半高）即得本函数。
        /// θ = 30° ⇒ cot θ = √3：基准档（6.85，2026-09-30 创始人定值）≈ 42、全景上限（60）≈ 134。
        ///
        /// 【为什么需要它】远裁剪该多大是 orthoSize 的函数，不是可以随手写死的大数；
        /// 战斗侧海面是"跟随相机、半径 4200"的圆盘，其可见部分恒落在本范围内，
        /// 无需按整盘口径（<see cref="OceanFarClipMin"/>）抬高。
        /// </summary>
        public static float MinFarClipForOrthoSize(float orthoSize)
        {
            float tanPitch = Mathf.Tan(BasePitchDegrees * Mathf.Deg2Rad);
            return BaseDistance + orthoSize / tanPitch;
        }

        // ------------------------------------------------------------------
        // 档位 / 特效当量纯函数
        // ------------------------------------------------------------------

        /// <summary>全景档 OrthoSize = clamp(round(span × 0.3), 全场档, 60)：span 100u → 30、160u → 48。</summary>
        public static int PanoramaOrthoSizeForSpan(float spanUnits)
        {
            return Mathf.Clamp(Mathf.RoundToInt(spanUnits * 0.3f), FullFieldOrthoSize, PanoramaMaxOrthoSize);
        }

        /// <summary>
        /// 两档取景切换的纯函数（相机行为契约 #9，交互操作契约 §B17）：当前档在两档中点以上
        /// 视为远景 → 切回近景基准档；否则 → 切到远景档。Tab 键的无状态翻转。
        /// </summary>
        public static float ToggleFramingTier(float currentOrthoSize, float panoramaOrthoSize)
        {
            return currentOrthoSize > (CloseUpOrthoSize + panoramaOrthoSize) * 0.5f
                ? CloseUpOrthoSize
                : panoramaOrthoSize;
        }

        /// <summary>
        /// 俯角（度）→ **相机相对焦点的单位方向**：水平分量在 +X/+Z 上等分
        /// （即方位 45°——只有它给出对称菱形构图）、竖直分量 sinθ。θ=30° 时 = (0.6124, 0.5, 0.6124)，
        /// 与出图口径 <see cref="PixelartPilotScene.CameraDirection"/> 逐分量相同
        /// （装配器/出图脚本/运行期三方同一份公式）。
        /// yaw 是相对该基准方位的偏移（右键环绕），0 方位 = 烘焙机位朝向。
        /// </summary>
        public static Vector3 OffsetDirectionForPitch(float pitchDegrees)
        {
            float p = pitchDegrees * Mathf.Deg2Rad;
            float horiz = Mathf.Cos(p) * 0.70710678f; // 水平分量在 X/Z 等分（方位 45°）
            return new Vector3(horiz, Mathf.Sin(p), horiz);
        }

        /// <summary>由机位偏移方向反推俯角（度）：<c>offset = (0, d·sinP, d·cosP)</c>。</summary>
        public static float PitchOf(Vector3 offsetDirection)
        {
            return Mathf.Atan2(offsetDirection.y,
                new Vector2(offsetDirection.x, offsetDirection.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>聚焦点 = 单位位置 + lookAt 抬高（镜头看向胸/头，而非脚底）。</summary>
        public static Vector3 FocusTargetPoint(Vector3 unitPosition)
        {
            return unitPosition + Vector3.up * LookAtHeight;
        }

        // ------------------------------------------------------------------
        // 机位合成（Driver 每帧调用的三件：偏移、位置、朝向）
        // ------------------------------------------------------------------

        /// <summary>
        /// 方位 yaw（度）+ 俯角 → 机位偏移向量（相对焦点）。
        /// 与原 Transposer 写法逐位等价：<c>AngleAxis(yaw, up) * (dir * distance)</c>。
        /// 【纯托管】绕 Y 的旋转用显式公式写（与 Unity <c>Quaternion.AngleAxis(yaw, up)</c> 同一约定，
        /// 数值经实机探针校验）——<c>Quaternion.AngleAxis</c> 是原生 icall，无头验证台跑不了。
        /// </summary>
        public static Vector3 ComputeFocusOffset(float yawDegrees, float pitchDegrees, float distance)
        {
            Vector3 dir = OffsetDirectionForPitch(pitchDegrees) * distance;
            float rad = yawDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            return new Vector3(dir.x * c + dir.z * s, dir.y, -dir.x * s + dir.z * c);
        }

        /// <summary>焦点 + 机位偏移 = 相机位置（震屏/下压由调用方按 <see cref="ShakeToWorld"/> 叠加）。</summary>
        public static Vector3 ComputePosition(
            Vector3 focusPoint, float yawDegrees, float pitchDegrees, float distance)
        {
            return focusPoint + ComputeFocusOffset(yawDegrees, pitchDegrees, distance);
        }

        /// <summary>
        /// 相机朝向 = **看向焦点**（<c>LookRotation(focus − position)</c> 的纯托管等价）+ 绕视线轴的震屏滚转。
        /// 【为什么改】创始人裁决的"右键旋转"要求环绕时**画面绕焦点转动**；旧实现（<see cref="ComputeRotation"/>，
        /// 朝向恒烘焙机位）把环绕做成了平移拖拽，2026-09-23 定案推翻。
        /// 已知取舍（r12 裁决沿用）：只有方位 45° 及其对称位给出对称菱形，别的方位下地面阶梯长短不一，不做 snap。
        /// 【纯托管】同 <see cref="ComputeRotation"/>；forward 须与世界 up 不平行（30° 俯角恒满足）。
        /// </summary>
        public static Quaternion ComputeRotationLooking(Vector3 forwardToFocus, float rollDegrees)
        {
            Quaternion look = QuaternionFromForwardUp(forwardToFocus, Vector3.up);
            if (Mathf.Approximately(rollDegrees, 0f))
                return look;
            return Multiply(look, FromAxisAngleZ(rollDegrees));
        }

        /// <summary>【已退役 2026-09-23（环绕重瞄裁决）】朝向恒烘焙机位的旧语义，仅历史测试引用；主链改用 <see cref="ComputeRotationLooking"/>。</summary>
        public static Quaternion ComputeRotation(float rollDegrees)
        {
            Vector3 forward = -OffsetDirectionForPitch(BasePitchDegrees);
            Quaternion look = QuaternionFromForwardUp(forward, Vector3.up);
            if (Mathf.Approximately(rollDegrees, 0f))
                return look;
            // 原链路语义：look * AngleAxis(roll, Vector3.forward) —— 绕相机**本地** Z（视线轴）滚转。
            return Multiply(look, FromAxisAngleZ(rollDegrees));
        }

        /// <summary>用四元数旋转向量（纯托管；<c>q * v</c> 的等价式，供无头测试断言朝向语义）。</summary>
        public static Vector3 Rotate(Quaternion q, Vector3 v)
        {
            Vector3 u = new Vector3(q.x, q.y, q.z);
            return 2f * Vector3.Dot(u, v) * u
                + (q.w * q.w - Vector3.Dot(u, u)) * v
                + 2f * q.w * Vector3.Cross(u, v);
        }

        /// <summary>
        /// 由"单位前向 + 期望世界 up"构造旋转（Unity <c>LookRotation</c> 的纯托管等价：
        /// right = normalize(cross(up, forward))、up' = cross(forward, right)，再从正交基取四元数）。
        /// </summary>
        public static Quaternion QuaternionFromForwardUp(Vector3 forward, Vector3 upward)
        {
            Vector3 f = forward.normalized;
            Vector3 r = Vector3.Cross(upward, f).normalized;
            Vector3 u = Vector3.Cross(f, r);
            return QuaternionFromBasis(r, u, f);
        }

        /// <summary>正交基（三个列向量）→ 四元数，Shepperd 分支法（数值稳定）；纯托管。</summary>
        static Quaternion QuaternionFromBasis(Vector3 columnX, Vector3 columnY, Vector3 columnZ)
        {
            float m00 = columnX.x, m01 = columnY.x, m02 = columnZ.x;
            float m10 = columnX.y, m11 = columnY.y, m12 = columnZ.y;
            float m20 = columnX.z, m21 = columnY.z, m22 = columnZ.z;

            float x, y, z, w;
            float trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s;
                x = 0.25f * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25f * s;
                z = (m12 + m21) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25f * s;
            }

            // 手工归一化（不赌 Quaternion.normalized 属性在无头环境的实现路径）。
            float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            return new Quaternion(x / length, y / length, z / length, w / length);
        }

        /// <summary>四元数 Hamilton 积（Unity <c>operator *</c> 的纯托管等价）。</summary>
        static Quaternion Multiply(Quaternion a, Quaternion b)
        {
            return new Quaternion(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        /// <summary>绕 Z 轴的四元数（滚转专用；轴为单位轴，纯托管）。</summary>
        static Quaternion FromAxisAngleZ(float angleDegrees)
        {
            float half = angleDegrees * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, 0f, Mathf.Sin(half), Mathf.Cos(half));
        }

        /// <summary>
        /// OrthoSize 合成：手动档 × FOV 当量比率（选中推近的当量映射）。
        /// Scope（瞄准镜收敛）与旁观外扩已随两态重构退役（旧链路根除，相机行为契约 §重构纪律 2）；
        /// 最后 <c>max(1, manual × fov/60)</c> 兜底。
        /// </summary>
        public static float ComposeOrthoSize(
            float manualOrthoSize,
            bool pushInActive, float pushInElapsed, float pushInDuration, float pushInDegrees)
        {
            float fov = BaseFov;
            if (pushInActive)
                fov = CameraFeelRules.PushInFov(
                    fov, pushInDegrees, pushInElapsed, pushInDuration);

            float ratio = fov / Mathf.Max(1e-3f, BaseFov);
            return Mathf.Max(1f, manualOrthoSize * ratio);
        }

        /// <summary>
        /// 相机局部平面位移（x=右, y=上）映射到世界。与原 ShakeOffsetToWorld 等价，
        /// 基向量取自当前相机朝向（烘焙机位恒定 ⇒ 基向量稳定）。
        /// </summary>
        public static Vector3 PlaneOffsetToWorld(Vector2 offset2D, Quaternion rotation)
        {
            if (offset2D == Vector2.zero)
                return Vector3.zero;
            return (rotation * Vector3.right) * offset2D.x + (rotation * Vector3.up) * offset2D.y;
        }
    }
}
