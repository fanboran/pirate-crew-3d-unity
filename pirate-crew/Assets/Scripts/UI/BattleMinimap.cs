using System.Collections.Generic;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Battle.WorldMaps;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗小地图（UGUI）：**顶视图缩略**——关卡顶视平色底图（编辑器出图的静态 PNG，
    /// 见 <see cref="TopViewFraming.TopViewResourcePath"/>）+ 双方单位动态点位。
    ///
    /// 【口径】海图窗 = 顶视底图（<c>TopViewMapping</c> 信箱适配，
    /// 与出图取景同一份数学，点位与底图像素级对齐）+ 单位点（红/蓝队色、死亡淡出）。
    /// 常量与映射规则见 <see cref="MinimapRules"/>（本工程设计值，<b>【提案/待定】</b>）。
    ///
    /// 【架构约定】
    ///   · 数据来源全部是同场景 <c>[SerializeField]</c> 引用（单位根节点 + 底图 + 点位层），
    ///     **不**做 <c>GameObject.Find</c>、**不**依赖 <see cref="BattleController"/> 内部、
    ///     **不**新增 EventBus 事件——位置是每帧直接读取，正是「每帧高频数据不走 EventBus」的推荐做法
    ///     （见 docs/技术/架构/EventBus事件契约.md §3）。
    ///   · 本局关卡跨度/关卡号走 <see cref="LevelSourceResolver.Resolve"/>（与
    ///     BattleController.Awake 同一解析口、同值；Start 时查询安全，待战状态不被提前消费）。
    ///   · 底图缺失（未跑出图）时响亮告警一次、退化为纯点位层，不阻塞战斗。
    ///   · 点位用锚点定位（<c>anchorMin = anchorMax = 归一化坐标</c>），面板缩放时自动跟随。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleMinimap : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // 序列化引用（同场景直连，由 HudMinimapSceneSetup 程序化接线）
        // ------------------------------------------------------------------

        [Header("场景引用")]
        [Tooltip("单位根节点（Team0_Red / Team1_Blue 或它们的共同父级）；运行时扫描其下的 PirateBase。")]
        [SerializeField] Transform[] unitRoots = new Transform[0];
        [Tooltip("顶视底图（ugui RawImage）；运行时按关卡号贴 Resources/Minimaps/level-<关卡号>。")]
        [SerializeField] RawImage topViewImage;
        [Tooltip("点阵层（ugui RectTransform）；单位点按顶视映射的归一化锚点落位。")]
        [SerializeField] RectTransform dotLayer;

        [Header("竞技场范围（米；Start 按本局关卡数据覆盖）")]
        [Tooltip("竞技场横向 X 尺寸（米）。默认值仅布局未生效时的回落口径。")]
        [SerializeField] float arenaWidth = 50f;
        [Tooltip("竞技场纵深 Z 尺寸（米）。")]
        [SerializeField] float arenaDepth = 17f;

        [Header("表现参数（提案/待定）")]
        [Tooltip("布局未生效（rect=0）时的每米像素回落值，决定点位直径。")]
        [SerializeField] float pixelsPerMeter = 2.5f;
        [Tooltip("单位点位直径（px）；<=0 时按 MinimapRules.DotSizePixels(pixelsPerMeter) 推导。")]
        [SerializeField] float dotSizePixels = 0f;

        // ------------------------------------------------------------------
        // 运行时状态
        // ------------------------------------------------------------------

        PirateBase[] _units = new PirateBase[0];
        Image[] _dots = new Image[0];
        float[] _visibility = new float[0];
        bool _built;
        /// <summary>顶视取景/点位映射（跨度 × 图幅长宽比，Start 与海图模式切换时重建）。</summary>
        TopViewMapping _topView;
        /// <summary>本局关卡号（底图资源名用；取不到时回落 1）。</summary>
        int _levelNumber = 1;
        /// <summary>世界地图海图模式：按实际图跨度映射、改用站面 box 岛层（海图重做基建，现役零海图不可达）。</summary>
        bool _worldChartMode;
        RectTransform _worldChartLayer;
        /// <summary>Refresh 上次校准点径时的点层像素宽（漂移 >0.5px 时重设全部点径）。</summary>
        float _dotLayerWidthForSize = -1f;
        /// <summary>TryBuild 未收敛时的重试簿记：隔 <see cref="BuildRetryIntervalFrames"/> 帧试一次，
        /// 连续 <see cref="BuildRetryMaxAttempts"/> 次仍建不出（如单位根没接线）就放弃 + 警告一次，
        /// 不再每帧全树扫描。</summary>
        int _buildRetryFrameCounter;
        int _buildRetryAttempts;
        bool _buildAbandoned;

        /// <summary>建点失败的重试间隔（帧）。</summary>
        const int BuildRetryIntervalFrames = 60;
        /// <summary>建点失败的最大重试次数（1 次初始 + 若干次重试后放弃）。</summary>
        const int BuildRetryMaxAttempts = 10;

        /// <summary>
        /// 有效每米像素 = 点层实际像素宽 / 顶视覆盖跨度——点径随面板尺寸自动缩放
        /// （布局未生效 rect=0 时回落序列化的 <see cref="pixelsPerMeter"/>）。
        /// </summary>
        float EffectivePixelsPerMeter()
        {
            if (dotLayer != null && dotLayer.rect.width > 1f)
                return dotLayer.rect.width / _topView.CoverX;
            return pixelsPerMeter;
        }

        /// <summary>接线自检（供 PlayMode 装配测试断言）。</summary>
        public bool HasMinimapWiring =>
            dotLayer != null
            && topViewImage != null
            && unitRoots != null && unitRoots.Length > 0
            && HasAnyRoot()
            && arenaWidth > 0f && arenaDepth > 0f;

        /// <summary>已建点位数（调试/测试用）。</summary>
        public int DotCount => _dots.Length;

        /// <summary>已缓存单位数（调试/测试用）。</summary>
        public int UnitCount => _units.Length;

        /// <summary>点阵层引用（测试用）。</summary>
        public RectTransform DotLayer => dotLayer;

        bool HasAnyRoot()
        {
            for (int i = 0; i < unitRoots.Length; i++)
            {
                if (unitRoots[i] != null)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        // Awake 不做扫描：BattleController 也在 Awake 里 Instantiate 单位，两个 Awake 次序未定义。
        // Start 在所有 Awake 之后执行，此时单位已生成（见 BattleController.Awake → SpawnTeams）。
        void Start()
        {
            ApplyLevelContext();
            TryBuild();
        }

        /// <summary>
        /// 按本局关卡数据定跨度/关卡号，重建顶视映射并贴底图。
        /// <see cref="LevelSourceResolver.Resolve"/> 与 BattleController.Awake 是同一解析口
        /// （命令行覆盖 > 待战内容 > 兜底关），Start 时调用与 Awake 同值。
        /// 取景参数优先读出图随 PNG 落盘的 <see cref="TopViewFrame"/>（内容包围盒实测，
        /// 点位与底图严格同源）；缺失（旧图/未跑出图）时回落「场地矩形」口径。
        /// </summary>
        void ApplyLevelContext()
        {
            LevelSource source = LevelSourceResolver.Resolve();
            if (source != null)
            {
                arenaWidth = Mathf.Max(1f, source.SpanX);
                arenaDepth = Mathf.Max(1f, source.SpanZ);
                if (source.LevelNumber > 0)
                    _levelNumber = source.LevelNumber;
            }

            var frameAsset = Resources.Load<TextAsset>(TopViewFraming.TopViewFrameResourcePath(_levelNumber));
            if (frameAsset != null)
            {
                TopViewFrame frame = JsonUtility.FromJson<TopViewFrame>(frameAsset.text);
                _topView = TopViewMapping.ForContent(
                    frame.centerX, frame.centerZ, frame.spanX, frame.spanZ,
                    TopViewFraming.TopViewTexAspect);
            }
            else
            {
                _topView = TopViewMapping.ForSpan(
                    arenaWidth, arenaDepth, TopViewFraming.TopViewTexAspect);
            }

            if (topViewImage != null)
            {
                Texture2D topView = Resources.Load<Texture2D>(TopViewFraming.TopViewResourcePath(_levelNumber));
                if (topView != null)
                {
                    topViewImage.texture = topView;
                }
                else
                {
                    global::PirateCrew.Core.Log.Warn("[BattleMinimap] 顶视底图缺失：Resources/"
                        + TopViewFraming.TopViewResourcePath(_levelNumber)
                        + "（先跑播放器出图 -minimapOut，再跑接线批次设置导入参数）——海图窗退化为纯点位层。");
                }
            }
        }

        /// <summary>
        /// 世界地图海图模式自探测（海图重做基建；现役零海图，待战入口不可达）：
        /// 本局是世界地图时，把换算范围改为实际图跨度、改从站面 box 生成海图岛层；
        /// 顶视底图无海图资源，置空退化为纯点位 + 岛层示意。
        /// <see cref="WorldMapRuntime.TryGetPending"/> 不清除待战状态，Start 时查询安全。
        /// public 供测试与选关 UI 显式调用；
        /// **已进入海图模式后是幂等守卫直接返回**——重复调用会再叠一层岛层。
        /// </summary>
        public void ConfigureWorldChartFromRuntime()
        {
            if (_worldChartMode)
                return;

            if (!WorldMapRuntime.TryGetPending(out WorldMapDefinition map) || map == null)
                return;

            _worldChartMode = true;
            arenaWidth = Mathf.Max(1f, map.SpanX);
            arenaDepth = Mathf.Max(1f, map.SpanZ);
            _topView = TopViewMapping.ForSpan(
                arenaWidth, arenaDepth, TopViewFraming.TopViewTexAspect);

            if (topViewImage != null)
                topViewImage.texture = null;

            BuildWorldChartIslands(WorldMapRules.AllStandBoxes(map));
        }

        /// <summary>按站面 box 铺海图岛层（每 box 一块，旋转取 AABB；口径见 MinimapRules.WorldBoxToChartRect）。</summary>
        void BuildWorldChartIslands(List<WorldMapRules.WorldBox> boxes)
        {
            if (dotLayer == null || boxes == null || boxes.Count == 0)
                return;

            RectTransform parent = dotLayer;
            var chartLayerGo = new GameObject("WorldChartLayer", typeof(RectTransform));
            var chartRect = chartLayerGo.GetComponent<RectTransform>();
            chartRect.SetParent(parent, false);
            chartRect.anchorMin = Vector2.zero;
            chartRect.anchorMax = Vector2.one;
            chartRect.offsetMin = Vector2.zero;
            chartRect.offsetMax = Vector2.zero;
            _worldChartLayer = chartRect;

            Color island = MinimapRules.WorldChartIslandColor;
            for (int i = 0; i < boxes.Count; i++)
            {
                Rect r = MinimapRules.WorldBoxToChartRect(
                    boxes[i].Center, boxes[i].Size, boxes[i].YawDeg, arenaWidth, arenaDepth);

                var go = new GameObject("ChartIsland_" + i, typeof(RectTransform), typeof(Image));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(chartRect, false);
                rect.anchorMin = new Vector2(Mathf.Clamp01(r.xMin), Mathf.Clamp01(r.yMin));
                rect.anchorMax = new Vector2(Mathf.Clamp01(r.xMax), Mathf.Clamp01(r.yMax));
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                image.color = island;
            }
        }

        /// <summary>海图岛层引用（测试用；非世界图为 null）。</summary>
        public RectTransform WorldChartLayer => _worldChartLayer;

        /// <summary>是否处于世界地图海图模式（测试用）。</summary>
        public bool IsWorldChartMode => _worldChartMode;

        /// <summary>当前换算范围 X（测试/调试用；世界图模式下 = 图跨度）。</summary>
        public float ArenaWidth => arenaWidth;

        /// <summary>当前换算范围 Z（测试/调试用）。</summary>
        public float ArenaDepth => arenaDepth;

        void Update()
        {
            if (!_built)
            {
                // 万一时序变化（例如单位在延迟一帧后生成），限频重试：60 帧一次，
                // 连续多次仍建不出（多为单位根没接线）就放弃 + 警告一次，不再每帧全树扫描。
                if (_buildAbandoned)
                    return;
                _buildRetryFrameCounter++;
                if (_buildRetryFrameCounter < BuildRetryIntervalFrames)
                    return;
                _buildRetryFrameCounter = 0;
                _buildRetryAttempts++;
                if (!TryBuild())
                {
                    if (_buildRetryAttempts >= BuildRetryMaxAttempts)
                    {
                        _buildAbandoned = true;
                        global::PirateCrew.Core.Log.Error("[BattleMinimap] 连续 " + BuildRetryMaxAttempts
                                         + " 次建点失败（找不到 PirateBase）：unitRoots 接线缺失或单位未生成，"
                                         + "小地图停建。修复：HudMinimapSceneSetup 接线 / 检查单位生成时序。");
                    }
                    return;
                }
            }

            Refresh();
        }

        bool TryBuild()
        {
            if (dotLayer == null)
                return false;

            var found = new List<PirateBase>();
            for (int i = 0; i < unitRoots.Length; i++)
            {
                Transform root = unitRoots[i];
                if (root == null)
                    continue;

                PirateBase[] children = root.GetComponentsInChildren<PirateBase>(includeInactive: true);
                for (int c = 0; c < children.Length; c++)
                {
                    if (children[c] != null && !found.Contains(children[c]))
                        found.Add(children[c]);
                }
            }

            if (found.Count == 0)
                return false;

            float size = dotSizePixels > 0f ? dotSizePixels : MinimapRules.DotSizePixels(EffectivePixelsPerMeter());
            _units = found.ToArray();
            _dots = new Image[_units.Length];
            _visibility = new float[_units.Length];

            for (int i = 0; i < _units.Length; i++)
            {
                _visibility[i] = 1f;

                var go = new GameObject("MinimapDot_" + _units[i].PirateId, typeof(RectTransform), typeof(Image));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(dotLayer, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size, size);
                rect.anchoredPosition = Vector2.zero;

                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                image.color = MinimapRules.DotColor(_units[i].TeamIndex, 1f);
                _dots[i] = image;
            }

            _built = true;
            Refresh();
            return true;
        }

        /// <summary>把每个单位的当前 XZ 位置映射到小地图锚点，并按存活状态推进淡出。</summary>
        void Refresh()
        {
            if (dotLayer == null || _dots.Length == 0)
                return;

            // 点径随面板校准：布局首帧 rect.width 才生效、以及运行时面板尺寸变化时，
            // 把全部点位直径重设一遍（数量 = 单位数，代价可忽略）。
            float layerWidth = dotLayer.rect.width;
            if (layerWidth > 1f && Mathf.Abs(layerWidth - _dotLayerWidthForSize) > 0.5f)
            {
                _dotLayerWidthForSize = layerWidth;
                float size = dotSizePixels > 0f ? dotSizePixels : MinimapRules.DotSizePixels(EffectivePixelsPerMeter());
                for (int i = 0; i < _dots.Length; i++)
                {
                    if (_dots[i] != null)
                        _dots[i].rectTransform.sizeDelta = new Vector2(size, size);
                }
            }

            for (int i = 0; i < _units.Length; i++)
            {
                Image dot = _dots[i];
                PirateBase pirate = _units[i];
                if (dot == null)
                    continue;

                if (pirate == null)
                {
                    dot.enabled = false;
                    continue;
                }

                float vis = pirate.Alive
                    ? 1f
                    : MinimapRules.AdvanceVisibility(_visibility[i], Time.deltaTime);
                _visibility[i] = vis;

                Vector3 world = pirate.transform.position;
                Vector2 normalized = MinimapRules.ClampNormalized(
                    _topView.WorldToNormalized(world.x, world.z));

                RectTransform rect = dot.rectTransform;
                rect.anchorMin = normalized;
                rect.anchorMax = normalized;
                rect.anchoredPosition = Vector2.zero;

                dot.color = MinimapRules.DotColor(pirate.TeamIndex, vis);
                dot.enabled = vis > 0.001f;
            }
        }
    }
}
