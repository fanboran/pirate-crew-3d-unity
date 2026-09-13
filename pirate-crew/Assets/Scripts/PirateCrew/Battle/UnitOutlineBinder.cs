using System.Collections.Generic;
using PirateCrew.PirateCrew.Visual;
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
    /// 【多渲染器（角色部件化改造）】单位不再是单立方体，而是"头/躯干/四肢/配件/手持物"
    /// 多个子 renderer（见 <see cref="CrewVisualRig"/>）。描边必须**逐 renderer**写
    /// <c>_OutlineState</c>，否则会出现"躯干变青、帽子不变青"的破绽
    /// （docs/角色造型规范.md §5 接线要求 1 / R-5）。
    ///
    /// 【阵营色与"不得污染"纪律】只有带 <see cref="CrewTeamTintPart"/> 标记的部件
    /// （头巾/上衣/腰带等阵营色大色块）才写队伍色 <c>_BaseColor</c>；皮肤/铁/木/骨/皮革
    /// 保留各自材质基础色（docs/角色造型规范.md §2.1）。旧的单立方体 prefab 没有标记，
    /// 则回落到"整个 targetRenderer 都染色"的旧行为，保证既有选中验收不回归。
    ///
    /// 【受击白闪】<see cref="SetColorFlash"/> 由 <c>CrewVisualAnimator</c> 驱动：
    /// 受击 0.08s 内把 <c>_BaseColor</c> 乘 (1+flash)，复位时写回材质原色。
    /// 白闪仍在本类的 MPB 通道里完成，避免两个组件各写一份 MPB 互相覆盖。
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
        [Tooltip("留空则自动收集全部子 renderer（部件化角色）；无子 renderer 时取同物体 renderer。")]
        [SerializeField] Renderer targetRenderer;

        [Header("队伍本体着色（仅表现，用于区分红/蓝队）")]
        [SerializeField] bool tintByTeam = true;

        [Tooltip("红队阵营色 #FF3A29（静态文档:721；原默认 (0.8,0.3,0.28) 为占位，本轮按规格校正）。")]
        [SerializeField] Color teamRedTint = new Color(1.000f, 0.228f, 0.161f, 1f);

        [Tooltip("蓝队阵营色 #3366FF（静态文档:721；原默认 (0.3,0.45,0.8) 为占位，本轮按规格校正）。")]
        [SerializeField] Color teamBlueTint = new Color(0.200f, 0.400f, 1.000f, 1f);

        PirateBase _pirate;
        MaterialPropertyBlock _block;
        Renderer[] _outlineRenderers = new Renderer[0];
        readonly HashSet<Renderer> _tintRenderers = new HashSet<Renderer>();
        bool _tintAllRenderers;
        int _appliedState = -1;
        bool _appliedTint;
        float _appliedFlash = -1f;
        bool _appliedFlashActive;
        bool _warnedMissingProperty;

        /// <summary>
        /// 调试采集用：&gt;= 0 时强制本单位使用该描边档，忽略状态位；-1 = 交回正常逻辑。
        /// 由 <c>OutlineDebugCapture</c> 在逐档采集期间统一置 2（选中态），保证每档都能看到描边；
        /// 采集结束复位为 -1。正常运行路径不写这个字段。
        /// </summary>
        public int DebugForcedState { get; set; } = -1;

        /// <summary>描边收集到的 renderer 数量（性能自证/测试用）。</summary>
        public int OutlineRendererCount => _outlineRenderers.Length;

        /// <summary>阵营色部件 renderer 数量。</summary>
        public int TeamTintRendererCount => _tintAllRenderers ? _outlineRenderers.Length : _tintRenderers.Count;

        void Awake()
        {
            _pirate = GetComponent<PirateBase>();
            _block = new MaterialPropertyBlock();
            CollectRenderers();
            WarnIfNotOutlineMaterial();
        }

        /// <summary>收集描边 renderer（全部部件）与阵营色 renderer（子集）。</summary>
        void CollectRenderers()
        {
            var rig = GetComponentInChildren<CrewVisualRig>(true);
            if (rig != null && rig.OutlineRenderers.Count > 0)
            {
                var list = new List<Renderer>(rig.OutlineRenderers.Count);
                for (int i = 0; i < rig.OutlineRenderers.Count; i++)
                {
                    if (rig.OutlineRenderers[i] != null)
                        list.Add(rig.OutlineRenderers[i]);
                }
                _outlineRenderers = list.ToArray();

                IReadOnlyList<Renderer> tint = rig.TeamTintRenderers;
                for (int i = 0; i < tint.Count; i++)
                {
                    if (tint[i] != null)
                        _tintRenderers.Add(tint[i]);
                }
                _tintAllRenderers = false;
                return;
            }

            // 回落到旧结构：同物体单 renderer，整只染色（保持既有选中验收不回归）。
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<Renderer>(true);

            _outlineRenderers = targetRenderer != null ? new[] { targetRenderer } : new Renderer[0];
            _tintAllRenderers = true;
        }

        void LateUpdate()
        {
            if (_pirate == null || _outlineRenderers.Length == 0)
                return;

            int state = DebugForcedState >= 0
                ? DebugForcedState
                : OutlineStateRules.Resolve(_pirate.Alive, _pirate.Selected, _pirate.Hovered);

            if (state == _appliedState && _appliedTint == tintByTeam)
                return;

            Apply(state);
        }

        /// <summary>
        /// 设置受击白闪强度（0-1）。值未变化时不重复写 MPB。
        /// 由 <c>CrewVisualAnimator</c> 在受击 0.08s 内逐帧调用，结束后传 0 复位。
        /// </summary>
        public void SetColorFlash(float strength)
        {
            float clamped = Mathf.Clamp01(strength);
            if (Mathf.Approximately(_appliedFlash, clamped))
                return;

            _appliedFlash = clamped;
            _appliedState = -1;   // 置脏，强制下一次 LateUpdate 重写
        }

        void Apply(int state)
        {
            Color tint = _pirate.TeamIndex == 0 ? teamRedTint : teamBlueTint;
            bool flashActive = _appliedFlash > 0.001f;

            for (int i = 0; i < _outlineRenderers.Length; i++)
            {
                Renderer r = _outlineRenderers[i];
                if (r == null)
                    continue;

                r.GetPropertyBlock(_block);
                _block.SetFloat(OutlineStateId, state);

                if (tintByTeam && (_tintAllRenderers || _tintRenderers.Contains(r)))
                {
                    _block.SetColor(BaseColorId, flashActive ? tint * (1f + _appliedFlash) : tint);
                }
                else if (flashActive || _appliedFlashActive)
                {
                    // 非阵营色部件：白闪期间乘基础色，复位时写回材质原色（等效清除覆盖）。
                    Color baseColor = r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId)
                        ? r.sharedMaterial.GetColor(BaseColorId)
                        : Color.white;
                    _block.SetColor(BaseColorId, flashActive ? baseColor * (1f + _appliedFlash) : baseColor);
                }

                r.SetPropertyBlock(_block);
            }

            _appliedState = state;
            _appliedTint = tintByTeam;
            _appliedFlashActive = flashActive;
        }

        /// <summary>
        /// 材质不是 PirateOutline 时描边属性会被静默忽略（MPB 对不存在的属性不报错），
        /// 所以这里显式告警一次，避免"看不出问题但就是没描边"。
        /// </summary>
        void WarnIfNotOutlineMaterial()
        {
            if (_outlineRenderers.Length == 0 || _warnedMissingProperty)
                return;

            Material shared = _outlineRenderers[0].sharedMaterial;
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
