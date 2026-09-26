using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.DebugUi
{
    /// <summary>
    /// aseprite 语义的**单点工具**：坐标换算 / 状态映射 / 件挂皮 / 视口裁剪 / 弹层宿主。
    ///
    /// 【为什么存在】参考库的四条架构不变量（单一绝对坐标系 widget.cpp:1223-1226；
    /// 裁剪在绘制管线 widget.cpp:926-932、944-952；弹层归 Manager 托管 manager.cpp:1394-1422；
    /// 状态解析单点 theme.cpp:47-78）在 UGUI 侧没有对应物，必须写胶水——胶水散装在
    /// 各调用点时，每一处都是独立故障点（双重锚点/锚漏算/裁剪缺挂/三处同错四案皆源于此）。
    /// 本类把这四类胶水收敛为唯一实现：**调用点只许调，不许再手写锚数学/状态位/件表**。
    /// </summary>
    public static class AseUi
    {
        // ------------------------------------------------------------------
        // 弹层宿主（源：popup 是显示器/Manager 上的独立顶层窗，不是触发控件的子件）
        // ------------------------------------------------------------------

        /// <summary>件所在画布根 = UGUI 侧的「Manager 层」。弹层/气泡一律挂这里
        /// （combobox.cpp:615+652 openWindow、tooltips.cpp:135-150、manager.cpp:1394-1422）。</summary>
        public static RectTransform OverlayOf(Transform t)
        {
            if (t == null)
                return null;
            Canvas canvas = t.GetComponentInParent<Canvas>();
            if (canvas != null)
                return (RectTransform)canvas.transform;
            RectTransform root = t as RectTransform;
            while (root != null && root.parent is RectTransform parent)
                root = parent;
            return root;
        }

        // ------------------------------------------------------------------
        // 坐标单点（治双重锚点/锚漏算）
        // ------------------------------------------------------------------

        /// <summary>世界矩形的左上角在宿主里的**边距**：(left, top) = 距宿主左缘/上缘
        /// （top 向下为正）。这是「世界 → 宿主」的唯一换算出口——_inverseTransform 得到的
        /// 是画布 pivot 局部坐标，直接塞 anchoredPosition 会把锚参考点算错（半画布偏移案）。</summary>
        public static Vector2 EdgesOf(RectTransform target, RectTransform host)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);                  // 0 左下 / 1 左上 / 2 右上 / 3 右下
            Vector2 local = host.InverseTransformPoint(corners[1]);
            Rect hr = host.rect;
            return new Vector2(local.x - hr.xMin, hr.yMax - local.y);
        }

        /// <summary>按边距落位一个 (0,1) 点锚件（anchorMin=anchorMax=pivot=左上）：
        /// anchoredPosition 本身就是「距宿主左缘 / 距顶缘」——**不得再叠加锚参考点**，
        /// 否则锚点算两遍，整体平移 2×锚点出画布（组合框弹层案）。</summary>
        public static void PlaceByEdges(RectTransform rect, float left, float top)
        {
            rect.anchoredPosition = new Vector2(left, -top);
        }

        /// <summary>建一个 (0,1) 点锚件并按边距落位（CreateRect + 单点摆位的组合出口）。</summary>
        public static RectTransform CreateOverlayRect(string name, RectTransform host, float left, float top,
            Vector2 size)
        {
            RectTransform rect = UiKit.CreateRect(name, host);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            PlaceByEdges(rect, left, top);
            return rect;
        }

        // ------------------------------------------------------------------
        // 状态映射（治三处 FlagsFor 同错）
        // ------------------------------------------------------------------
        // 【归属说明】UGUI SelectionState / currentSelectionState 在 Unity 2022 是
        // Selectable 的 protected 成员，外部静态类摸不到——「SelectionState+业务位 →
        // AseStates」的映射必须住在 Selectable 子类里（AseButtonBase，W4 落地）：
        //   Disabled → Disabled；Pressed → Selected|Capture（button.cpp:168-175）；
        //   Selected → Focus |(active?Selected)；Highlighted → Mouse |(active?Selected)；
        //   Normal → active?Selected:None（theme.cpp:122-129 + 层引擎 :47-78）。

        // ------------------------------------------------------------------
        // 件挂皮单点（像素纪律：Sliced + ppum×1 + 白色禁乘色）
        // ------------------------------------------------------------------

        /// <summary>按样式与状态解析 background-border 件并挂皮；解析不到返回 false（件不动）。</summary>
        public static bool SetPart(Image image, string styleId, AseStates states)
        {
            if (image == null)
                return false;
            string part = AseThemeLayers.ResolveBackgroundPart(styleId, states);
            if (part == null)
                return false;
            SetRawPart(image, part);
            return true;
        }

        /// <summary>直切件挂皮（theme parts 表件 id 直取，sheet 原色）。
        /// 点状蚀刻线等 3px 周期件必须传 <paramref name="type"/>=Tiled（Sliced 拉伸会变实线）。</summary>
        public static void SetRawPart(Image image, string partId, Image.Type type = Image.Type.Sliced)
        {
            image.sprite = PixelSkin.Ase(partId);
            image.type = type;
            image.pixelsPerUnitMultiplier = 1f;
            image.color = Color.white;
        }

        // ------------------------------------------------------------------
        // 视口裁剪单点（治裁剪缺挂——源里是管线强制，UGUI 侧必须 opt-in，故收敛成必经此门）
        // ------------------------------------------------------------------

        /// <summary>给视口挂裁剪 + 透明接光面（源等价物：drawable region 与 Viewport
        /// childrenBounds 求交，widget.cpp:926-932 + view.cpp:197-200 viewportBounds）。
        /// 任何滚动容器/弹层视口建好后必须过这一门。</summary>
        public static RectMask2D ClipViewport(RectTransform viewport)
        {
            RectMask2D mask = viewport.gameObject.GetComponent<RectMask2D>();
            if (mask == null)
                mask = viewport.gameObject.AddComponent<RectMask2D>();
            if (viewport.GetComponent<Image>() == null)
            {
                var hit = viewport.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;   // 空白区也接事件（源里 Viewport 是 View 的收件面）
            }
            return mask;
        }
    }
}
