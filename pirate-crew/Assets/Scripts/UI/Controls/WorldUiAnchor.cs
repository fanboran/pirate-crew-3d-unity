using UnityEngine;

namespace PirateCrew.UI.Controls
{
    /// <summary>
    /// 把一块 UI（选中头顶面板、世界标记等）钉在世界坐标上：每帧把目标点投影到屏幕，
    /// 换算成画布局部坐标后贴上去，并做整数像素对齐与画布内钳制。
    ///
    /// 【为什么不用 World Space Canvas】像素 UI 的整格对齐依赖「固定像素密度的屏幕画布」
    /// （ConstantPixelSize × Unit）。World Space Canvas 会随相机距离缩放、透视变形，
    /// 字号与 1px 边框立刻破格。这里保持 UI 在屏幕画布上，只借用世界坐标算落点。
    ///
    /// 【挂法】挂在要跟随的面板根上（RectTransform 锚点/轴心会被归整为中心锚）；
    /// 面板的显示/隐藏由业务方控制（选中时 SetActive(true)），本件只管位置与相机背后隐藏。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class WorldUiAnchor : MonoBehaviour
    {
        [Tooltip("投影用相机；留空则取 Camera.main。")]
        [SerializeField] Camera worldCamera;

        [Tooltip("跟随目标（角色根）；为空时保持当前位置。")]
        [SerializeField] Transform target;

        [Tooltip("目标点相对目标原点的世界偏移（如头顶高度）。")]
        [SerializeField] Vector3 worldOffset = Vector3.zero;

        [Tooltip("把面板整体钳在画布内（边缘留 Margin）。")]
        [SerializeField] bool clampToCanvas = true;

        [Tooltip("钳制时距画布边缘的最小留白（画布像素）。")]
        [SerializeField] Vector2 margin = new Vector2(4f, 4f);

        RectTransform _rect;
        RectTransform _canvasRect;
        Canvas _canvas;
        CanvasGroup _group;

        /// <summary>目标点（世界坐标）= 目标位置 + 偏移。</summary>
        public Vector3 WorldPoint
        {
            get { return target != null ? target.position + worldOffset : transform.position; }
        }

        /// <summary>换跟随目标（选中变化时由业务方调用；传 null 保持不动）。</summary>
        public void SetTarget(Transform value)
        {
            target = value;
        }

        /// <summary>换投影相机（相机重置/换绑时用）。</summary>
        public void SetCamera(Camera value)
        {
            worldCamera = value;
        }

        void Awake()
        {
            _rect = (RectTransform)transform;
            _rect.anchorMin = _rect.anchorMax = _rect.pivot = new Vector2(0.5f, 0.5f);

            _canvas = GetComponentInParent<Canvas>();
            _canvasRect = _canvas != null ? (RectTransform)_canvas.transform : null;
            if (_canvas != null && _canvas.rootCanvas != null)
                _canvasRect = (RectTransform)_canvas.rootCanvas.transform;

            _group = GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();
        }

        void LateUpdate()
        {
            if (target == null || _canvasRect == null)
                return;

            Camera camera = worldCamera != null ? worldCamera : Camera.main;
            if (camera == null)
                return;

            Vector3 screenPoint = camera.WorldToScreenPoint(WorldPoint);

            // 目标在相机背后：WorldToScreenPoint 的 z < 0，投出来的 x/y 是镜像值，必须藏掉。
            bool behind = screenPoint.z <= 0f;
            SetVisible(!behind);
            if (behind)
                return;

            Camera uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (_canvas.worldCamera != null ? _canvas.worldCamera : camera);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screenPoint, uiCamera, out Vector2 local))
                return;

            // 画布中心为原点（overlay 画布 pivot 0.5）→ 中心锚面板的 anchoredPosition 就是 local。
            if (clampToCanvas)
            {
                Vector2 half = _rect.rect.size * 0.5f;
                Rect canvasRect = _canvasRect.rect;
                float minX = canvasRect.xMin + margin.x + half.x;
                float maxX = canvasRect.xMax - margin.x - half.x;
                float minY = canvasRect.yMin + margin.y + half.y;
                float maxY = canvasRect.yMax - margin.y - half.y;

                if (minX <= maxX)
                    local.x = Mathf.Clamp(local.x, minX, maxX);
                if (minY <= maxY)
                    local.y = Mathf.Clamp(local.y, minY, maxY);
            }

            // 像素纪律：画布坐标取整（画布 1 单位 = Unit 屏幕像素，取整即整格落位）。
            _rect.anchoredPosition = new Vector2(Mathf.Round(local.x), Mathf.Round(local.y));
        }

        void SetVisible(bool visible)
        {
            if (_group == null)
                return;
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
            _group.interactable = visible;
        }
    }
}
