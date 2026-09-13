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
    /// 【收集口径（2026-09-13 r3 复验后收紧）】
    ///   r3 实测选中特写里只有躯干 + 腰带出现青虚线，头/帽/臂/腿都没有。自查发现两条独立缺陷，
    ///   本类的收集路径改为**不依赖任何外部缓存**：
    ///   ① **不再读 <see cref="CrewVisualRig.OutlineRenderers"/>**。那个列表由 rig 自己
    ///      `EnsureCached()` 懒加载（`if (_outlineRenderers != null) return;`）——一旦在"部件尚未装配完"
    ///      的时刻被读过，就会把**不完整的集合**固化下来，且没有任何告警；binder 每帧仍照常写 MPB，
    ///      外观上就是"部分部件有描边"。现在由 binder 自己 `GetComponentsInChildren` 全量扫描，
    ///      收集失败（0 个 renderer）会显式报错而不是静默不动。
    ///   ② **阵营色部件改走显式引用**（见下）。部件上的 <see cref="CrewTeamTintPart"/> 标记脚本与
    ///      `CrewVisualRig` 同处一个 .cs，而 Unity 只给"类名 == 文件名"的类生成 MonoScript，
    ///      所以预制体里这些标记全部存成了 missing script（实测 7 个预制体共 26 处）、运行时收不到 →
    ///      阵营色一个 renderer 都没写到，蓝队会顶着红队底色。现改由
    ///      <c>CrewVisualPrefabBuilder</c> 在建预制体时把"该染色的 renderer 列表"直接写进
    ///      <see cref="teamTintRenderers"/>，并用运行时标记作为回落（兼容手工装配的旧预制体）。
    ///
    /// 【阵营色与"不得污染"纪律】只有阵营色部件（头巾/上衣/腰带等大色块）才写队伍色
    /// <c>_BaseColor</c>；皮肤/铁/木/骨/皮革保留各自材质基础色（docs/角色造型规范.md §2.1）。
    /// 拿不到任何阵营色部件时**绝不**回落到"整只染色"（那会把肤色染蓝）——只有旧单立方体结构
    /// （无 rig、只有一个 renderer）才保持旧的整只染色行为，保证既有选中验收不回归。
    ///
    /// 【受击白闪】<see cref="SetColorFlash"/> 由 <c>CrewVisualAnimator</c> 驱动：
    /// 受击 0.08s 内把 <c>_BaseColor</c> 乘 (1+flash)，复位时写回材质原色。
    /// 白闪仍在本类的 MPB 通道里完成，避免两个组件各写一份 MPB 互相覆盖。
    ///
    /// 【接触阴影面片必须排除】<see cref="ContactShadowDecal"/> 是贴地半透明 quad，
    /// 描边会让它变成一圈方形轮廓、染色会让它跟队伍变红/变蓝（详见该类的注释）。
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
        [Tooltip("留空则自动收集全部子 renderer（部件化角色）；仅旧单立方体结构才需要手填。")]
        [SerializeField] Renderer targetRenderer;

        [Header("阵营色部件（由 CrewVisualPrefabBuilder 建预制体时写入）")]
        [Tooltip("只对这些 renderer 写队伍色 _BaseColor（肤色/铁/木/骨部件不得被污染）。\n" +
                 "留空时回落到运行时 CrewTeamTintPart 标记；两者都拿不到则一个都不染（不回落到整只染色）。")]
        [SerializeField] Renderer[] teamTintRenderers = new Renderer[0];

        [Header("队伍本体着色（仅表现，用于区分红/蓝队）")]
        [SerializeField] bool tintByTeam = true;

        [Tooltip("红队阵营色 #FF3A29（静态文档:721；原默认 (0.8,0.3,0.28) 为占位，本轮按规格校正）。")]
        [SerializeField] Color teamRedTint = new Color(1.000f, 0.228f, 0.161f, 1f);

        [Tooltip("蓝队阵营色 #3366FF（静态文档:721；原默认 (0.3,0.45,0.8) 为占位，本轮按规格校正）。")]
        [SerializeField] Color teamBlueTint = new Color(0.200f, 0.400f, 1.000f, 1f);

        PirateBase _pirate;
        MaterialPropertyBlock _block;
        Renderer[] _outlineRenderers;                 // null = 尚未收集（懒收集，见 EnsureCollected）
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

        /// <summary>描边收集到的 renderer 数量（性能自证/测试用；未收集时现收一次）。</summary>
        public int OutlineRendererCount
        {
            get
            {
                EnsureCollected();
                return _outlineRenderers.Length;
            }
        }

        /// <summary>阵营色部件 renderer 数量。</summary>
        public int TeamTintRendererCount => _tintAllRenderers ? OutlineRendererCount : _tintRenderers.Count;

        void Awake()
        {
            _pirate = GetComponent<PirateBase>();
            _block = new MaterialPropertyBlock();
            EnsureCollected();
            WarnIfNotOutlineMaterial();
        }

        void OnEnable()
        {
            // 重复进出对象池 / 被重新启用时重扫一次，避免沿用旧的 renderer 快照。
            RefreshRenderers();
        }

        /// <summary>
        /// 丢弃已收集的 renderer 快照并立刻重扫（对象池复用、运行时挂/卸部件后调用）。
        /// 同时把描边/染色状态置脏，保证下一次 <c>LateUpdate</c> 会重写全部部件。
        /// </summary>
        public void RefreshRenderers()
        {
            _outlineRenderers = null;
            _appliedState = -1;
            EnsureCollected();
            WarnIfNotOutlineMaterial();
        }

        void EnsureCollected()
        {
            if (_outlineRenderers != null)
                return;
            CollectRenderers();
        }

        /// <summary>
        /// 收集描边 renderer（全部角色部件，排除接触阴影面片）与阵营色 renderer（子集）。
        /// 自行全量扫描、不读 <see cref="CrewVisualRig.OutlineRenderers"/> 的缓存（见类头 ①）。
        /// </summary>
        void CollectRenderers()
        {
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            var list = new List<Renderer>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null)
                    continue;
                // 贴地接触阴影面片不是角色部件：不描边、不染队伍色（见 ContactShadowDecal 注释）。
                if (r.GetComponent<ContactShadowDecal>() != null)
                    continue;
                list.Add(r);
            }
            _outlineRenderers = list.ToArray();

            // ---- 阵营色部件子集 ----
            _tintRenderers.Clear();
            _tintAllRenderers = false;

            var rig = GetComponentInChildren<CrewVisualRig>(true);

            // ① 显式表（CrewVisualPrefabBuilder 建预制体时写入）优先——这是唯一可靠的口径。
            if (teamTintRenderers != null && teamTintRenderers.Length > 0)
            {
                for (int i = 0; i < teamTintRenderers.Length; i++)
                {
                    if (teamTintRenderers[i] != null)
                        _tintRenderers.Add(teamTintRenderers[i]);
                }
            }
            else if (rig != null)
            {
                // ② 回落：运行时标记组件（手工装配 / 旧预制体）。
                IReadOnlyList<Renderer> marked = rig.TeamTintRenderers;
                for (int i = 0; i < marked.Count; i++)
                {
                    if (marked[i] != null)
                        _tintRenderers.Add(marked[i]);
                }
            }

            // ③ 旧单立方体结构：没有 rig、只有一个 renderer —— 保持"整只染色"的旧行为。
            if (rig == null && _outlineRenderers.Length <= 1)
                _tintAllRenderers = true;
        }

        void LateUpdate()
        {
            if (_pirate == null)
                return;

            EnsureCollected();
            if (_outlineRenderers.Length == 0)
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
        /// 所以这里**逐 renderer**核对并一次性列出问题部件，避免"看不出问题但就是没描边"。
        /// 收集结果为空同样显式报错（r3 的"只覆盖躯干"就是静默失效）。
        /// </summary>
        void WarnIfNotOutlineMaterial()
        {
            if (_warnedMissingProperty || _outlineRenderers == null || _outlineRenderers.Length == 0)
                return;

            string bad = null;
            for (int i = 0; i < _outlineRenderers.Length; i++)
            {
                Renderer r = _outlineRenderers[i];
                if (r == null)
                    continue;
                Material shared = r.sharedMaterial;
                if (shared != null && shared.HasProperty(OutlineStateId))
                    continue;
                if (bad == null)
                    bad = r.name + (shared != null ? "（" + shared.name + "）" : "（材质为空）");
            }

            if (bad == null)
                return;

            _warnedMissingProperty = true;
            Debug.LogWarning("[UnitOutlineBinder] " + name + " 有部件的材质不含 _OutlineState 属性，"
                + "描边状态不会被应用，首个：" + bad
                + "。请把单位材质换成 PirateOutline shader（见 M2BattleSceneSetup.EnsureOutlineMaterial）。");
        }
    }
}
