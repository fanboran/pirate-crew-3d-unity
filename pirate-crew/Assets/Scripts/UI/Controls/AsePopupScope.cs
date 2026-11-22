using System;
using System.Collections.Generic;

namespace PirateCrew.UI.Controls
{
    /// <summary>
    /// 可被全局收起的浮层（菜单弹层、组合框列表…）。源的 Manager 只持有一个 popup 窗，
    /// 开新必关旧；UGUI 侧没有这个容器，靠本接口收口。
    /// </summary>
    public interface IAsePopup
    {
        /// <summary>收起本浮层；重复调用必须安全。</summary>
        void ClosePopup();
    }

    /// <summary>
    /// 同屏只留一层浮层的登记处（对应源 Manager 的单一 popup 窗语义）。
    /// 弹层打开时 <see cref="Register"/>，销毁/收起时 <see cref="Unregister"/>；
    /// 新弹层打开前调 <see cref="CloseAll"/>。
    /// </summary>
    public static class AsePopupScope
    {
        static readonly List<IAsePopup> s_open = new List<IAsePopup>();

        public static void Register(IAsePopup popup)
        {
            if (popup != null && !s_open.Contains(popup))
                s_open.Add(popup);
        }

        public static void Unregister(IAsePopup popup)
        {
            if (popup != null)
                s_open.Remove(popup);
        }

        /// <summary>收起当前所有浮层（先快照再清表，关闭回调里反注册安全）。</summary>
        public static void CloseAll()
        {
            if (s_open.Count == 0)
                return;

            IAsePopup[] snapshot = s_open.ToArray();
            s_open.Clear();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i].ClosePopup();
        }
    }
}
