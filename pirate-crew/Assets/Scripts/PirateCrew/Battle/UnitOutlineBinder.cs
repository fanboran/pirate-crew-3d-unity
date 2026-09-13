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
    /// 【r4 复验"选中描边变少"的归因结论（不要再往本类的收集口径上找）】
    ///   r4 实测 unit-closeup 的选中青色像素 265px（r3 同机位 823px），但**本类的口径没有问题**：
    ///   ① 部件材质全部走 <c>PirateOutline</c>（13 个 <c>Assets/Art/Materials/Crew/Crew*.mat</c>
    ///      的 <c>m_Shader</c> 全是 outline shader，且都带 <c>_OutlineState</c>/<c>_OutlineExpandMode=0</c>）；
    ///   ② 预制体里唯一的显式数组是 <see cref="teamTintRenderers"/>（只影响阵营色，不影响描边），
    ///      描边集合是本类运行时全量收集的；
    ///   ③ r3 与 r4 的同一张图里单位像素级同形同址（红帽 bbox 都是 x896-1023 / y552-817），
    ///      即几何、机位、材质、shader 都没变。
    ///   真正的变量是**选中虚线的相位/周期**：<c>_DashFrequency=50</c> 在 1080p 下 ON≈34px / OFF≈34px
    ///   （r3 实测三个青色带各 33px 高、间距 67~69px，与该换算吻合），而 <c>phase</c> 含
    ///   <c>_Time.y × _DashSpeed</c> → **哪个部件落在 ON 带全凭截帧时刻**：r3 的三条带落在肩/腰/裙摆，
    ///   r4 只落在腰带缝 → 同一套几何给出 823 vs 265px。部件高度与 OFF 带同量级的小件（靴 ~15px、
    ///   腰带 ~16px）甚至可能整件落在 OFF 带里 → 判据里的"某部件 0 青色"。
    ///   故修法在材质侧（<c>CrewVisualPrefabBuilder.ApplyOutlineUnitMaterial</c> 提高 <c>_DashFrequency</c>，
    ///   使周期远小于最小部件），**不需要也不应该再改本类的收集逻辑**。
    ///
    /// 【本类的职责边界（r5 起）】读状态 → 写属性 → 自检：
    ///   · 收集（全量扫描、只排除接触阴影面片）；收集为空**显式报错**（不再静默）；
    ///   · 逐 renderer 写 MPB 覆盖 <c>_OutlineState</c>（+ 阵营色部件写 <c>_BaseColor</c>）；
    ///   · 周期性抽检：MPB 被外部成分（对象池复用/别的组件整块覆盖）清掉时强制重写并告警。
    ///
    /// 【r6 复验取证结论（不要再往"收集漏了 / 材质缺属性"上找）】
    ///   7 个角色预制体的**逐部件 YAML 对照**（Sailor/Captain/Bombardier/Sniper/Hook/Skeleton/Arsonist）
    ///   证明：除 <c>ContactShadow</c>（CrewContactShadow.mat，无 _OutlineState，且被本类显式排除）外，
    ///   所有部件材质都命中那 12 个带 <c>_OutlineState</c> 的 Crew 材质——Head/Nose/ArmL/ArmR 用 CrewSkin，
    ///   Bandana/BandanaKnot/Tricorn 用 CrewTeamCloth（与躯干**是同一个材质资产**，躯干有青、它没有 →
    ///   已证明不是材质问题）。也排除了"部件 inactive"：预制体里所有部件 <c>m_IsActive=1</c>、
    ///   MeshRenderer <c>m_Enabled=1</c>；且 <c>GetComponentsInChildren&lt;Renderer&gt;(true)</c> 本就含
    ///   inactive 子物体，MPB 写到 inactive renderer 同样有效（只是等它启用才画）。
    ///   故 r6 分两处收口：
    ///   ① 本类新增 <see cref="LogPartInventoryOnce"/>——首次收集后打一份"部件 → 材质 → 有无
    ///      _OutlineState"清单，让这类怀疑**一眼可判**，不必再读 YAML；
    ///   ② 真正的原因是屏幕空间法线外扩在特写距离只有 ~1px（推导见
    ///      <c>CrewVisualPrefabBuilder.OutlineWidthSelected</c>），修在材质常量侧（0.006 → 0.010）。
    ///
    /// 【分层】状态判定在纯逻辑 <see cref="OutlineStateRules"/>（可无头测）；
    ///         本类只做"读状态 → 写属性"的引擎侧薄壳。
    /// </summary>
    [RequireComponent(typeof(PirateBase))]
    public sealed class UnitOutlineBinder : MonoBehaviour
    {
        static readonly int OutlineStateId = Shader.PropertyToID("_OutlineState");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>MPB 抽检间隔（帧）。抽检只读 1 个 renderer 的 PropertyBlock，成本可忽略。</summary>
        const int VerifyIntervalFrames = 30;

        /// <summary>
        /// 已打印过部件清单的预制体名（同一类预制体只打一次，避免 12 个单位刷屏）。
        /// 静态字段在关域重载的编辑器会话里会跨 Play 保留——清单本身是静态接线，重打无新信息。
        /// </summary>
        static readonly HashSet<string> LoggedPartInventories = new HashSet<string>();

        [Header("渲染目标")]
        [Tooltip("留空则自动收集全部子 renderer（部件化角色）；仅旧单立方体结构才需要手填。")]
        [SerializeField] Renderer targetRenderer;

        [Header("阵营色部件（由 CrewVisualPrefabBuilder 建预制体时写入）")]
        [Tooltip("只对这些 renderer 写队伍色 _BaseColor（肤色/铁/木/骨部件不得被污染）。\n" +
                 "留空时回落到运行时 CrewTeamTintPart 标记；两者都拿不到则一个都不染（不回落到整只染色）。")]
        [SerializeField] Renderer[] teamTintRenderers = new Renderer[0];

        [Header("自检")]
        [Tooltip("周期性抽检每个 renderer 的 MPB 是否仍带着本类写过的 _OutlineState；\n" +
                 "被对象池复用/外部组件整块覆盖时强制重写并告警一次。")]
        [SerializeField] bool verifyAppliedState = true;

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
        bool _warnedEmptyCollection;
        bool _warnedClobbered;
        int _verifyCountdown = VerifyIntervalFrames;

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

        /// <summary>最近一次写进 MPB 的描边档（-1 = 还没写过）。调试/测试自证用。</summary>
        public int AppliedState => _appliedState;

        /// <summary>强制下一次 <c>LateUpdate</c> 重写全部部件的 MPB（自检失败、外部改动后调用）。</summary>
        public void MarkDirty()
        {
            _appliedState = -1;
        }

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
            _warnedEmptyCollection = false;
            _verifyCountdown = VerifyIntervalFrames;
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
            var list = new List<Renderer>(all.Length + 1);
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

            // 手工接线兜底：只填了 targetRenderer 的旧结构（或挂在不属于子层级的 renderer）也要收进来。
            if (targetRenderer != null && !list.Contains(targetRenderer))
                list.Add(targetRenderer);

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

            LogPartInventoryOnce();
        }

        /// <summary>
        /// 首次收集后打一份**部件清单**（渲染器 → 材质 → 是否带 <c>_OutlineState</c>；阵营色部件另标）。
        ///
        /// 【为什么在运行时打而不是只在编辑器】协调者的视觉闭环跑的是**构建出的播放器**
        /// （`PlayerArtCapture`），那里没有 Console 面板但 <c>Player.log</c> 会落盘这份日志；
        /// 编辑器会话打开本工程时同样能看到。故门槛设在"编辑器或 Development 构建"，正式包不打。
        /// 每个预制体只打一次（按 GameObject 名去重），一处缺属性的部件会被单列出来。
        /// </summary>
        void LogPartInventoryOnce()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
                return;
            if (_outlineRenderers == null || !LoggedPartInventories.Add(name))
                return;

            var sb = new System.Text.StringBuilder(512);
            sb.Append("[UnitOutlineBinder] 部件清单 ").Append(name)
              .Append("：描边 renderer ").Append(_outlineRenderers.Length)
              .Append(" 个，阵营色 renderer ").Append(TeamTintRendererCount).Append(" 个");

            int missing = 0;
            for (int i = 0; i < _outlineRenderers.Length; i++)
            {
                Renderer r = _outlineRenderers[i];
                if (r == null)
                {
                    sb.Append("\n  · <已销毁>");
                    continue;
                }

                Material shared = r.sharedMaterial;
                bool ok = shared != null && shared.HasProperty(OutlineStateId);
                if (!ok)
                    missing++;

                sb.Append("\n  · ").Append(r.name)
                  .Append(" | 材质=").Append(shared != null ? shared.name : "<空>")
                  .Append(" | _OutlineState=").Append(ok ? "有" : "缺");
                if (_tintAllRenderers || _tintRenderers.Contains(r))
                    sb.Append(" | 阵营色");
            }

            sb.Append("\n  → 缺 _OutlineState 的部件数：").Append(missing);
            if (missing > 0)
                Debug.LogWarning(sb.ToString());
            else
                Debug.Log(sb.ToString());
        }

        void LateUpdate()
        {
            if (_pirate == null)
                return;

            EnsureCollected();
            if (_outlineRenderers.Length == 0)
            {
                WarnIfNotOutlineMaterial();
                return;
            }

            int state = DebugForcedState >= 0
                ? DebugForcedState
                : OutlineStateRules.Resolve(_pirate.Alive, _pirate.Selected, _pirate.Hovered);

            if (state == _appliedState && _appliedTint == tintByTeam)
            {
                VerifyAppliedState(state);
                return;
            }

            Apply(state);
        }

        /// <summary>
        /// 抽检：本类写完 MPB 后，若别的成分（对象池复用、外部组件整块 <c>SetPropertyBlock</c>）
        /// 把这个 renderer 的块覆盖掉，描边会**静默消失**（材质属性还在，只是没人写了）。
        /// 每 <see cref="VerifyIntervalFrames"/> 帧只读一个 renderer 的块做抽查，命中即整只重写并告警一次。
        /// </summary>
        void VerifyAppliedState(int state)
        {
            if (!verifyAppliedState)
                return;

            if (--_verifyCountdown > 0)
                return;
            _verifyCountdown = VerifyIntervalFrames;

            Renderer probe = null;
            for (int i = 0; i < _outlineRenderers.Length; i++)
            {
                if (_outlineRenderers[i] != null)
                {
                    probe = _outlineRenderers[i];
                    break;
                }
            }
            if (probe == null)
                return;

            probe.GetPropertyBlock(_block);
            if (Mathf.Approximately(_block.GetFloat(OutlineStateId), state))
                return;

            MarkDirty();
            Apply(state);

            if (_warnedClobbered)
                return;
            _warnedClobbered = true;
            Debug.LogWarning("[UnitOutlineBinder] " + name + " 的 MPB 在外部被改写过（" + probe.name
                + " 上的 _OutlineState 与本类期望的 " + state + " 不一致），已强制重写。"
                + "若是对象池复用/外来组件整块 SetPropertyBlock，请在那之后调用 RefreshRenderers()。");
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
        /// 收集结果为空同样是**错误级**（r3 的"只覆盖躯干"就是静默失效，r4 的类注释却写了会报错
        /// 而实际提前 return —— 这里补齐），否则"一个 renderer 都没收到"会被当成正常空转。
        /// </summary>
        void WarnIfNotOutlineMaterial()
        {
            if (_outlineRenderers == null)
                return;

            if (_outlineRenderers.Length == 0)
            {
                if (_warnedEmptyCollection)
                    return;
                _warnedEmptyCollection = true;
                Debug.LogError("[UnitOutlineBinder] " + name + " 一个描边 renderer 都没收集到"
                    + "（子层级里没有 Renderer，或全被排除了）。选中/悬停不会画出任何描边——"
                    + "请检查预制体的 Visual 层级与 ContactShadowDecal 挂点。");
                return;
            }

            if (_warnedMissingProperty)
                return;

            string bad = null;
            int badCount = 0;
            for (int i = 0; i < _outlineRenderers.Length; i++)
            {
                Renderer r = _outlineRenderers[i];
                if (r == null)
                    continue;
                Material shared = r.sharedMaterial;
                if (shared != null && shared.HasProperty(OutlineStateId))
                    continue;
                badCount++;
                if (bad == null)
                    bad = r.name + (shared != null ? "（" + shared.name + "）" : "（材质为空）");
            }

            if (bad == null)
                return;

            _warnedMissingProperty = true;
            Debug.LogWarning("[UnitOutlineBinder] " + name + " 有 " + badCount + "/"
                + _outlineRenderers.Length + " 个部件的材质不含 _OutlineState 属性，"
                + "描边状态不会被应用，首个：" + bad
                + "。请把单位材质换成 PirateOutline shader（见 M2BattleSceneSetup.EnsureOutlineMaterial）。");
        }
    }
}
