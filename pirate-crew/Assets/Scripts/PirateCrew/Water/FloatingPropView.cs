using PirateCrew.PirateCrew.Battle;
using UnityEngine;

namespace PirateCrew.PirateCrew.Water
{
    /// <summary>
    /// 漂浮道具的水面起伏视图（批次 F，**纯表现**）：每帧采样
    /// <see cref="WaterSurfaceSampler"/>（与 PirateOcean.shader 顶点位移同口径的水面高度），
    /// 把相对初始位置的 Y 偏移（clamp ±<see cref="MaxBobOffset"/>）与可选微倾
    /// （roll/pitch ≤ <see cref="MaxTiltDegrees"/>【提案/待定】）应用到自身 transform。
    ///
    /// 【批次契约】模拟/视觉结果**不参与任何玩法判定**：不碰刚体、碰撞体、导航与任何
    /// 玩法组件，落水/立足判定照旧用标量 <see cref="LevelGeometry.WaterSurfaceY"/>
    /// （审计报告 §二.3：玩法层与水解耦是既有工程契约，本组件只解决"chop 在船体/浮标
    /// 水线上下穿模"的表现问题）。挂载资格由保守白名单判定，见
    /// <see cref="EligibleForFloatingView"/>（装配点：WorldMapComposer）。
    ///
    /// 【旧竞技场模式】<see cref="OceanRig.Instance"/> == null（无大海域海面）→ 每帧早退、
    /// 不动 transform——道具停在放置时的静态位置，与无海面画面自洽。
    ///
    /// 【性能】每帧 0 GC：无 LINQ/闭包/字符串拼接；基线（Y、旋转）在 OnEnable 缓存一次，
    /// 采样走纯静态函数。起伏 ≤0.5u + 微倾 ≤2° 对静态合批外的普通 Renderer 零额外开销。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloatingPropView : MonoBehaviour
    {
        /// <summary>Y 起伏上限（u）。近场有效波幅 = chop 0.278 + 包络内长涌贡献，0.5 已兜住
        /// 近场全部起伏，clamp 只防极端参数下道具漂走。【提案/待定】</summary>
        public const float MaxBobOffset = 0.5f;

        /// <summary>微倾上限（度），roll/pitch 各自 clamp。小角度即可暗示"浮在浪上"，
        /// 大角度会让桅杆类道具晃眼。【提案/待定】</summary>
        public const float MaxTiltDegrees = 2f;

        /// <summary>放置 y 与静水面的最大距离（u）：超过即视为"不贴水"的道具，不挂起伏。
        /// 0.5 的口径：浮标类目录提示 y=0（距水面 0.4）仍命中；站面顶道具（y≥0.5 →
        /// 距水面 ≥0.9）全部排除——站面上起伏会"悬空"穿帮（sunken_gate 的 ShipWheelPost
        /// 实测误报即此形态）。【提案/待定】</summary>
        public const float MaxPlacementYDelta = 0.5f;

        /// <summary>坡度有限差分的采样步长（u）。太小会被网格步进噪声放大，1u 与近场格距同量
        /// 级、平滑够用。【提案/待定】</summary>
        const float SlopeSampleDistance = 1.0f;

        /// <summary>是否启用微倾（roll/pitch）。默认开；观感轮若觉多余可整体关掉。【提案/待定】</summary>
        [SerializeField] bool _tiltEnabled = true;

        Transform _tr;
        float _baseY;
        Quaternion _baseRotation;
        bool _captured;

        /// <summary>资格判定的白名单名词条目（含 Buoy；或含 Boat/Ship 且不含 Beached），
        /// 供装配层与测试共用同一份判据（保守白名单：宁缺勿滥，误挂比漏挂更显眼）。</summary>
        public static bool EligibleForFloatingView(string instanceName, float placementY)
        {
            if (string.IsNullOrEmpty(instanceName))
                return false;

            // 名字白名单（大小写敏感、序数比较：资产 id 一律 PascalCase，不需要忽略大小写）。
            // "Beached"（搁浅）是明确反例——搁浅船件钉在滩上，随波起伏反而穿帮。
            bool nameHit = instanceName.Contains("Buoy")
                || ((instanceName.Contains("Boat") || instanceName.Contains("Ship"))
                    && !instanceName.Contains("Beached"));
            if (!nameHit)
                return false;

            // 位置资格：放置 y 贴近静水面（浮标等水面件）。站面顶的道具（y ≥ 0.5）不会命中。
            float yDelta = placementY - LevelGeometry.WaterSurfaceY;
            return yDelta >= -MaxPlacementYDelta && yDelta <= MaxPlacementYDelta;
        }

        void OnEnable()
        {
            _tr = transform;
            _baseY = _tr.position.y;
            _baseRotation = _tr.rotation;
            _captured = true;
        }

        void LateUpdate()
        {
            // 旧竞技场模式（无大海域海面）：不动 transform，零采样开销。
            OceanRig rig = OceanRig.Instance;
            if (rig == null || !_captured)
                return;

            Vector3 pos = _tr.position;
            var xz = new Vector2(pos.x, pos.z);
            OceanConfig config = rig.Config;
            float bob = WaterSurfaceSampler.HeightAt(
                xz, rig.GridCenterXZ, config.ArenaCenter, config.ArenaRadius, Time.time);
            bob = Mathf.Clamp(bob, -MaxBobOffset, MaxBobOffset);

            pos.y = _baseY + bob;
            _tr.position = pos;

            if (_tiltEnabled)
                ApplyTilt(xz, rig, config);
        }

        /// <summary>
        /// 微倾（纯表现【提案/待定】）：对水面坡度做中心差分，roll 对应 ∂h/∂x、pitch 对应 ∂h/∂z，
        /// 各 clamp ±<see cref="MaxTiltDegrees"/>，叠加在初始旋转上（不改初始 yaw）。
        /// 旋转轴约定（右手系）：水面沿 +X 升 → 绕 +Z 正转让 +X 侧抬起；水面沿 +Z 升 →
        /// 绕 +X 正转让 +Z 侧下沉，故 pitch 取负号。
        /// </summary>
        void ApplyTilt(Vector2 xz, OceanRig rig, OceanConfig config)
        {
            float e = SlopeSampleDistance;
            float hx0 = SampleHeight(new Vector2(xz.x - e, xz.y), rig, config);
            float hx1 = SampleHeight(new Vector2(xz.x + e, xz.y), rig, config);
            float hz0 = SampleHeight(new Vector2(xz.x, xz.y - e), rig, config);
            float hz1 = SampleHeight(new Vector2(xz.x, xz.y + e), rig, config);

            float slopeX = (hx1 - hx0) / (2f * e);
            float slopeZ = (hz1 - hz0) / (2f * e);

            float rollDeg = Mathf.Clamp(Mathf.Atan(slopeX) * Mathf.Rad2Deg, -MaxTiltDegrees, MaxTiltDegrees);
            float pitchDeg = Mathf.Clamp(-Mathf.Atan(slopeZ) * Mathf.Rad2Deg, -MaxTiltDegrees, MaxTiltDegrees);
            _tr.rotation = _baseRotation * Quaternion.Euler(pitchDeg, 0f, rollDeg);
        }

        static float SampleHeight(Vector2 xz, OceanRig rig, OceanConfig config)
        {
            return WaterSurfaceSampler.HeightAt(
                xz, rig.GridCenterXZ, config.ArenaCenter, config.ArenaRadius, Time.time);
        }
    }
}
