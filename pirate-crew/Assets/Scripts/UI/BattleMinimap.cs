using System.Collections.Generic;
using PirateCrew.PirateCrew.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗小地图（UGUI）：竞技场 XZ **俯视示意** + 双方单位点位。
    ///
    /// 【原版依据】<c>Map.as</c>（§2.4/§8.1）：<c>dotSize=3</c> 点阵，红 <c>0xFF3A29</c>、
    /// 蓝 <c>0x3366FF</c>，alpha = <c>mapVisibility*100</c>，死亡角色 <c>mapVisibility -= 0.1/帧</c> 淡出；
    /// 容器 <c>mapHolder</c> 在 (20,20)（§2.3，屏幕左上角）；每帧 <c>map.drawActive()</c>（§3.1）。
    /// 具体换算/映射见 <see cref="MinimapRules"/>（原版未给出的部分已标**提案/待定**）。
    ///
    /// 【架构约定】
    ///   · 数据来源全部是同场景 <c>[SerializeField]</c> 引用（单位根节点 + 点阵层），
    ///     **不**做 <c>GameObject.Find</c>、**不**依赖 <see cref="BattleController"/> 内部、
    ///     **不**新增 EventBus 事件——位置是每帧直接读取，正是「每帧高频数据不走 EventBus」的推荐做法
    ///     （见 docs/EventBus事件契约.md §3）。
    ///   · 点位用锚点定位（<c>anchorMin = anchorMax = 归一化坐标</c>），面板缩放时自动跟随，不依赖 rect 尺寸。
    ///   · 美术资源：纯色方块占位（无外部图片）。
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
        [Tooltip("点阵层（ugui RectTransform）；单位点按其归一化锚点落位。")]
        [SerializeField] RectTransform dotLayer;

        [Header("竞技场范围（瓦片数；1 瓦片 = 1 世界单位）")]
        [Tooltip("竞技场横向 X 尺寸（关卡 widthTiles，1 瓦片 = 1 世界单位）。")]
        [SerializeField] float arenaWidth = 50f;
        [Tooltip("竞技场纵深 Z 尺寸（关卡 heightTiles）。")]
        [SerializeField] float arenaDepth = 17f;

        [Header("表现参数（提案/待定）")]
        [Tooltip("一颗瓦片在小地图上画多少像素（决定面板与点位大小）。")]
        [SerializeField] float pixelsPerTile = 5f;
        [Tooltip("单位点位直径（px）；<=0 时按 MinimapRules.DotSizePixels(pixelsPerTile) 推导。")]
        [SerializeField] float dotSizePixels = 0f;

        // ------------------------------------------------------------------
        // 运行时状态
        // ------------------------------------------------------------------

        PirateBase[] _units = new PirateBase[0];
        Image[] _dots = new Image[0];
        float[] _visibility = new float[0];
        bool _built;

        /// <summary>接线自检（供 PlayMode 装配测试断言）。</summary>
        public bool HasMinimapWiring =>
            dotLayer != null
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
            TryBuild();
        }

        void Update()
        {
            if (!_built)
            {
                // 万一时序变化（例如单位在延迟一帧后生成），下一帧继续尝试。
                if (!TryBuild())
                    return;
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

            float size = dotSizePixels > 0f ? dotSizePixels : MinimapRules.DotSizePixels(pixelsPerTile);
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
                    MinimapRules.ArenaToNormalized(world.x, world.z, arenaWidth, arenaDepth));

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
