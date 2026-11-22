using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 菜单场景的键盘焦点：进屏给一个初始焦点（方向键/回车立即可用），Tab / Shift+Tab
    /// 按视觉顺序循环。焦点态由控件自身绘制（<c>SketchButton</c> 的 button_focused 件）。
    ///
    /// 【范围】只挂在菜单画布（主菜单/选关/船员管理）。战斗内回车是玩法确认键，
    /// 焦点导航会与它抢输入，暂不启用（战斗 Esc 走 InteractionRules 矩阵）。
    ///
    /// 【模态优先级】模态打开时 Tab 只在栈顶模态内循环；模态关闭后由
    /// <see cref="UiModalStack"/> 恢复原触发按钮的焦点。下层按钮既不收指针（遮罩挡）
    /// 也不进 Tab 序列。
    /// </summary>
    public sealed class UiKeyboardFocus : MonoBehaviour
    {
        UiModalStack _modalStack;
        bool _initialFocusDone;
        readonly List<Selectable> _candidates = new List<Selectable>(32);

        /// <summary>挂到画布上（重复调用安全）。<paramref name="modalStack"/> 可为空。</summary>
        public static UiKeyboardFocus Ensure(Transform canvas, UiModalStack modalStack)
        {
            if (canvas == null)
                return null;

            UiKeyboardFocus focus = canvas.GetComponent<UiKeyboardFocus>();
            if (focus == null)
                focus = canvas.gameObject.AddComponent<UiKeyboardFocus>();
            focus._modalStack = modalStack;
            return focus;
        }

        void Update()
        {
            if (EventSystem.current == null)
                return;

            // 初始焦点：等控制器把运行时行建完（前两帧不算），模态没开才做。
            if (!_initialFocusDone && Time.frameCount > 2
                && EventSystem.current.currentSelectedGameObject == null
                && (_modalStack == null || !_modalStack.IsOpen))
            {
                _initialFocusDone = true;
                Collect(CurrentScope(), _candidates);
                if (_candidates.Count > 0)
                    EventSystem.current.SetSelectedGameObject(_candidates[0].gameObject);
                _candidates.Clear();
            }

            if (!Input.GetKeyDown(KeyCode.Tab))
                return;

            Collect(CurrentScope(), _candidates);
            if (_candidates.Count == 0)
                return;

            bool reverse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            GameObject current = EventSystem.current.currentSelectedGameObject;
            int index = -1;
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (_candidates[i].gameObject == current)
                {
                    index = i;
                    break;
                }
            }

            int next = index < 0
                ? (reverse ? _candidates.Count - 1 : 0)
                : (index + (reverse ? -1 : 1) + _candidates.Count) % _candidates.Count;

            EventSystem.current.SetSelectedGameObject(_candidates[next].gameObject);
            _candidates.Clear();
        }

        /// <summary>焦点范围：模态开 = 栈顶模态内；否则整个画布。</summary>
        Transform CurrentScope()
        {
            if (_modalStack != null && _modalStack.IsOpen && _modalStack.TopRoot != null)
                return _modalStack.TopRoot.transform;
            return transform;
        }

        /// <summary>按视觉顺序（层级兄弟序深度优先）收集可用焦点件。</summary>
        static void Collect(Transform root, List<Selectable> into)
        {
            into.Clear();
            if (root == null)
                return;
            CollectRecursive(root, into);
        }

        static void CollectRecursive(Transform parent, List<Selectable> into)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (!child.gameObject.activeSelf)
                    continue;

                Selectable selectable = child.GetComponent<Selectable>();
                if (selectable != null && selectable.IsInteractable()
                    && selectable.navigation.mode != Navigation.Mode.None)
                {
                    into.Add(selectable);
                }

                CollectRecursive(child, into);
            }
        }
    }
}
