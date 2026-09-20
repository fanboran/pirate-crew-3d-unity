using UnityEngine;

namespace PirateCrew.UI.Stick
{
    /// <summary>
    /// UI 布局原语 —— 落实「场景是布局唯一真相源」的代码侧强制。
    /// 移植自 stick-world <c>core/ui_framework/ui_kit.gd</c>（L0 布局原语）。
    ///
    /// 原则：禁止拿裸 <c>new GameObject</c> 当 UI 根——会丢 anchor 布局（默认
    /// anchor(0,0)/size 0，锚定子控件会定位到原点，静默不可见）。代码创建的全屏
    /// UI 根必须走 <see cref="FullRect"/>；角落 HUD 部件走 <see cref="Widget"/>，
    /// 不自设锚点（定位归 zone 装配层，部件只声明体量）。
    /// </summary>
    public static class StickUIKit
    {
        /// <summary>四向拉伸零偏移（gd set_anchors_preset(PRESET_FULL_RECT) + 双向 grow）。</summary>
        public static void FullRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>创建全屏 UI 根节点（gd UIKit.full_rect(script, name) 的等价出口：
        /// 脚本/组件由调用方随后 AddComponent，锚定此处强制）。</summary>
        public static RectTransform FullRect(string nodeName, Transform parent)
        {
            RectTransform rect = UiKit.CreateRect(nodeName, parent);
            FullRect(rect);
            return rect;
        }

        /// <summary>
        /// 创建角落 HUD 部件节点（gd UIKit.widget(script, name) 等价）：不自设锚点——
        /// 定位归 zone 装配层（P1 未移植，宿主按装配表落位），部件只声明
        /// <c>StickLayoutElement.MinSize</c>（= gd custom_minimum_size）体量。
        /// T 为部件组件（MonoBehaviour 子类），挂在新节点上并返回。
        /// </summary>
        public static T Widget<T>(string nodeName, Transform parent) where T : Component
        {
            GameObject go = new GameObject(nodeName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }
    }
}
