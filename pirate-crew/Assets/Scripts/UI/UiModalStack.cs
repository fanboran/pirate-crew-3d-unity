using System.Collections.Generic;
using UnityEngine;

namespace PirateCrew.UI
{
    /// <summary>
    /// 菜单场景的模态浮层栈：压栈即显示（遮罩本身在各模态根里），出栈即关；
    /// **Esc 只在这里解析一次**——栈顶模态优先，Esc 默认出栈，也可由压栈方接管
    /// （如选关结算的 Esc 走 CloseSettlement）。
    ///
    /// 【边界】只服务菜单场景（主菜单/选关/船员管理）。战斗内 Esc 由
    /// <c>InteractionRules.ResolveEsc</c> 的交互矩阵裁决（暂停/取消操作/取消选中/
    /// 关对话框），那是战斗状态机的一部分，不并入本栈，避免两处消费同一次按键。
    ///
    /// 【生命周期】运行时由控制器 Awake 调 <see cref="Ensure"/> 挂到画布上；
    /// 不入 Prefab、不序列化，烘焙产物零改动。
    /// </summary>
    public sealed class UiModalStack : MonoBehaviour
    {
        sealed class Entry
        {
            public GameObject Root;
            public RectTransform Card;
            public System.Action OnEsc;
            public System.Action OnClosed;
        }

        readonly List<Entry> _stack = new List<Entry>();
        UiMotion _motion;

        /// <summary>当前是否有模态打开。</summary>
        public bool IsOpen { get { return _stack.Count > 0; } }

        /// <summary>栈顶模态根（无则 null）。</summary>
        public GameObject TopRoot
        {
            get { return _stack.Count > 0 ? _stack[_stack.Count - 1].Root : null; }
        }

        /// <summary>取画布上的栈（没有就挂一个）；重复调用安全。</summary>
        public static UiModalStack Ensure(Transform canvas, UiMotion motion)
        {
            if (canvas == null)
                return null;

            UiModalStack stack = canvas.GetComponent<UiModalStack>();
            if (stack == null)
                stack = canvas.gameObject.AddComponent<UiModalStack>();
            stack._motion = motion;
            return stack;
        }

        /// <summary>
        /// 压入模态并显示。<paramref name="onEsc"/> 为空时 Esc = 出栈；
        /// <paramref name="onClosed"/> 在真正出栈时回调（落盘、状态提示等）。
        /// </summary>
        public void Push(GameObject root, RectTransform card = null,
            System.Action onEsc = null, System.Action onClosed = null)
        {
            if (root == null)
                return;

            for (int i = 0; i < _stack.Count; i++)
            {
                if (_stack[i].Root == root)
                    return;   // 已在栈中，不重复压
            }

            _stack.Add(new Entry { Root = root, Card = card, OnEsc = onEsc, OnClosed = onClosed });
            root.transform.SetAsLastSibling();
            RuntimeUiBuilder.OpenPanel(root, _motion);
        }

        /// <summary>关闭栈顶模态。</summary>
        public void Pop()
        {
            if (_stack.Count == 0)
                return;

            int last = _stack.Count - 1;
            Entry entry = _stack[last];
            _stack.RemoveAt(last);
            Close(entry);
        }

        /// <summary>关闭指定模态（不在栈中则忽略）。</summary>
        public void Pop(GameObject root)
        {
            if (root == null)
                return;

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (_stack[i].Root != root)
                    continue;

                Entry entry = _stack[i];
                _stack.RemoveAt(i);
                Close(entry);
                return;
            }
        }

        /// <summary>清空（切场景/返回前用；不触发 Esc 语义）。</summary>
        public void CloseAll()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                Entry entry = _stack[i];
                _stack.RemoveAt(i);
                Close(entry);
            }
        }

        void Close(Entry entry)
        {
            if (entry.Root != null)
                RuntimeUiBuilder.ClosePanel(entry.Root, _motion);
            entry.OnClosed?.Invoke();
        }

        void Update()
        {
            if (_stack.Count == 0)
                return;
            if (!Input.GetKeyDown(KeyCode.Escape))
                return;

            Entry top = _stack[_stack.Count - 1];
            if (top.OnEsc != null)
                top.OnEsc();
            else
                Pop();
        }
    }
}
