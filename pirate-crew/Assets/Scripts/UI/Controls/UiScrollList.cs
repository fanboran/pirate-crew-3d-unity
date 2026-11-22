using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI.Controls
{
    /// <summary>
    /// 生产界面用的可滚动列表容器：统一走 <see cref="AseView"/>（Aseprite View 语义：
    /// 内容超高时滚动条**按需**出现在框内右缘）与控件库的行产线
    /// （<see cref="RuntimeUiBuilder.CreateRow"/> 的行直接挂进 <see cref="Content"/>）。
    ///
    /// 【为什么需要】菜单/管理屏的列表原先是一个固定高的 VerticalLayoutGroup，行一多就
    /// 溢出窗体（选关窗只够 10 行）。这里把「窗体内容区」换成 view 皮 + 滚动内核，
    /// 行数变化由本件在 LateUpdate 里量高后刷新，调用点不用关心滚动条。
    ///
    /// 【量高口径】行高取行上的 <see cref="LayoutElement"/>.preferredHeight（
    /// <see cref="RuntimeUiBuilder.CreateRow"/> 已声明）；没有则取行实际高。
    /// 只统计 active 的行——重建列表时旧行延迟销毁（SetActive(false)），不计入。
    /// </summary>
    public sealed class UiScrollList : MonoBehaviour
    {
        RectTransform _host;          // view 皮所在矩形（AseView 的 bounds）
        AseView _view;
        RectTransform _content;       // 行容器（挂进 view.Viewport）
        VerticalLayoutGroup _group;

        int _lastActiveCount = -1;
        Vector2 _lastSize;

        /// <summary>行容器：调用方把行建在这里（<see cref="RuntimeUiBuilder.CreateRow"/>）。</summary>
        public RectTransform Content { get { return _content; } }

        /// <summary>
        /// 反序列化自恢复：本件与 AseView 一起烘进 Prefab 后，私有引用全为空——
        /// 按「同物体 AseView → 它的视口 → 视口第一个子件（Content）」重新找回，
        /// 然后按当前行数量一遍高。运行时 <see cref="Attach"/> 新建时字段随后会被覆盖，
        /// 且此处找不到视口会直接返回，无副作用。
        /// </summary>
        void Awake()
        {
            if (_host == null)
                _host = (RectTransform)transform;
            if (_view == null)
                _view = GetComponent<AseView>();
            if (_view != null)
                _view.EnsureHydrated();

            if (_content == null && _view != null && _view.Viewport != null && _view.Viewport.childCount > 0)
                _content = _view.Viewport.GetChild(0) as RectTransform;
            if (_group == null && _content != null)
                _group = _content.GetComponent<VerticalLayoutGroup>();

            if (_content != null)
                Refresh();
        }

        /// <summary>给已定位好尺寸的矩形装滚动列表（<paramref name="host"/> 即 view 皮的矩形范围）。</summary>
        public static UiScrollList Attach(RectTransform host)
        {
            AseWidgetKit.PaintViewSkin(host);
            AseView view = AseView.Attach(host,
                (int)AseWidgetKit.ViewBorderLeft, (int)AseWidgetKit.ViewBorderTop,
                (int)AseWidgetKit.ViewBorderRight, (int)AseWidgetKit.ViewBorderBottom);

            RectTransform content = UiKit.CreateRect("Content", view.Viewport);
            UiKit.SetAnchored(content, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);

            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.childAlignment = TextAnchor.UpperCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.spacing = AseLayout.Px(1);
            group.padding = new RectOffset(0, 0, 0, 0);

            var list = host.gameObject.AddComponent<UiScrollList>();
            list._host = host;
            list._view = view;
            list._content = content;
            list._group = group;

            view.AttachToView(content);
            list.Refresh();
            return list;
        }

        /// <summary>量行高并刷新滚动区（行增删/换行/尺寸变化后调用；LateUpdate 也会自动兜底）。</summary>
        public void Refresh()
        {
            if (_content == null || _view == null)
                return;

            int active = 0;
            float total = _group.padding.top + _group.padding.bottom;
            for (int i = 0; i < _content.childCount; i++)
            {
                Transform child = _content.GetChild(i);
                if (!child.gameObject.activeSelf)
                    continue;

                float h = 0f;
                var element = child.GetComponent<LayoutElement>();
                if (element != null && element.preferredHeight > 0f)
                    h = element.preferredHeight;
                else if (child is RectTransform rect)
                    h = rect.rect.height;

                if (active > 0)
                    total += _group.spacing;
                total += h;
                active++;
            }

            float innerW = Mathf.Round(_host.rect.width)
                - (AseWidgetKit.ViewBorderLeft + AseWidgetKit.ViewBorderRight);
            float innerH = Mathf.Round(_host.rect.height)
                - (AseWidgetKit.ViewBorderTop + AseWidgetKit.ViewBorderBottom);

            bool needVerticalBar = total > innerH;
            int contentW = Mathf.Max(1, Mathf.RoundToInt(
                innerW - (needVerticalBar ? AseLayout.ScrollbarSize : 0f)));
            int contentH = Mathf.Max(1, Mathf.RoundToInt(total));

            _view.SetContentHint(contentW, contentH);
            _view.UpdateView();

            _lastActiveCount = active;
            _lastSize = _host.rect.size;
        }

        void LateUpdate()
        {
            if (_content == null)
                return;

            int active = 0;
            for (int i = 0; i < _content.childCount; i++)
            {
                if (_content.GetChild(i).gameObject.activeSelf)
                    active++;
            }

            if (active != _lastActiveCount || _host.rect.size != _lastSize)
                Refresh();
        }
    }
}
