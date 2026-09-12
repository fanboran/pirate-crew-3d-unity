using UnityEngine;

namespace PirateCrew.PirateCrew.Battle
{
    /// <summary>
    /// 描边表现绑定器：把 <see cref="PirateBase"/> 的 Selected / Hovered / Alive 状态位每帧写进
    /// 渲染器的 <see cref="MaterialPropertyBlock"/>（<c>_OutlineState</c>），
    /// 使 <c>PirateOutline.shader</c> 能在**同一份共享材质**上逐个单位切换描边。
    ///
    /// 【这是 §4.5 characterOverlay 的 Unity 载体】<see cref="PirateBase"/> 只维护状态位
    /// （`SetSelected` / `SetHover` 的注释写明"描边表现由外部渲染层负责"），本类就是那个渲染层。
    ///
    /// 【为什么用 MaterialPropertyBlock 而不是 renderer.material】
    ///   <c>renderer.material</c> 会为每个单位克隆一份材质实例（12 个单位 = 12 份克隆，
    ///   退出 play mode 时还会留下泄露的实例）；MPB 是逐渲染器的属性覆盖，
    ///   共享材质资产本身不被改脏。代价是该渲染器会退出 SRP Batcher 批次，
    ///   对全场十余个单位量级没有实际影响。
    ///
    /// 【分层】状态判定在纯逻辑 <see cref="OutlineStateRules"/>（可无头测）；
    ///         本类只做"读状态 → 写属性"的引擎侧薄壳。
    /// </summary>
    [RequireComponent(typeof(PirateBase))]
    public sealed class UnitOutlineBinder : MonoBehaviour
    {
        static readonly int OutlineStateId = Shader.PropertyToID("_OutlineState");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("渲染目标")]
        [Tooltip("留空则在 Awake 时取同物体的第一个 Renderer。")]
        [SerializeField] Renderer targetRenderer;

        [Header("队伍本体着色（仅表现，用于区分红/蓝队；与原版美术无关）")]
        [SerializeField] bool tintByTeam = true;
        [SerializeField] Color teamRedTint = new Color(0.80f, 0.30f, 0.28f, 1f);
        [SerializeField] Color teamBlueTint = new Color(0.30f, 0.45f, 0.80f, 1f);

        PirateBase _pirate;
        MaterialPropertyBlock _block;
        int _appliedState = -1;
        bool _appliedTint;
        bool _warnedMissingProperty;

        /// <summary>
        /// 调试采集用：&gt;= 0 时强制本单位使用该描边档，忽略状态位；-1 = 交回正常逻辑。
        /// 由 <c>OutlineDebugCapture</c> 在逐档采集期间统一置 2（选中态），保证每档都能看到描边；
        /// 采集结束复位为 -1。正常运行路径不写这个字段。
        /// </summary>
        public int DebugForcedState { get; set; } = -1;

        void Awake()
        {
            _pirate = GetComponent<PirateBase>();
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>();
            _block = new MaterialPropertyBlock();

            WarnIfNotOutlineMaterial();
        }

        void LateUpdate()
        {
            if (_pirate == null || targetRenderer == null)
                return;

            int state = DebugForcedState >= 0
                ? DebugForcedState
                : OutlineStateRules.Resolve(_pirate.Alive, _pirate.Selected, _pirate.Hovered);

            if (state == _appliedState && _appliedTint == tintByTeam)
                return;

            Apply(state);
        }

        void Apply(int state)
        {
            targetRenderer.GetPropertyBlock(_block);
            _block.SetFloat(OutlineStateId, state);

            if (tintByTeam)
                _block.SetColor(BaseColorId, _pirate.TeamIndex == 0 ? teamRedTint : teamBlueTint);

            targetRenderer.SetPropertyBlock(_block);
            _appliedState = state;
            _appliedTint = tintByTeam;
        }

        /// <summary>
        /// 材质不是 PirateOutline 时描边属性会被静默忽略（MPB 对不存在的属性不报错），
        /// 所以这里显式告警一次，避免"看不出问题但就是没描边"。
        /// </summary>
        void WarnIfNotOutlineMaterial()
        {
            if (targetRenderer == null || _warnedMissingProperty)
                return;

            Material shared = targetRenderer.sharedMaterial;
            if (shared != null && shared.HasProperty(OutlineStateId))
                return;

            _warnedMissingProperty = true;
            Debug.LogWarning("[UnitOutlineBinder] " + name + " 的材质"
                + (shared != null ? "（" + shared.name + "）" : "为空")
                + " 不含 _OutlineState 属性，描边状态不会被应用。"
                + "请把单位材质换成 PirateOutline shader（见 M2BattleSceneSetup.EnsureOutlineMaterial）。");
        }
    }
}
