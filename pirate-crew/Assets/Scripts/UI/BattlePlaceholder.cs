using PirateCrew.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 战斗场景占位（M1 过渡用，M2 由真实战斗模块替换）。
    ///
    /// 唯一职责：返回按钮点击时发布 EventBus "go_back"，
    /// 由 SceneLoader 从场景栈弹出并返回上一场景（主菜单）。
    /// 同样遵循"UI 不直接引用 SceneLoader"的事件驱动约定。
    /// </summary>
    public sealed class BattlePlaceholder : MonoBehaviour
    {
        /// <summary>EventBus 返回事件名（与 SceneLoader 约定一致）。</summary>
        const string GoBackEvent = "go_back";

        [SerializeField] Button backButton;

        void Awake()
        {
            // 允许把本组件直接挂在按钮对象上而不必在 Inspector 里拖引用。
            if (backButton == null)
                backButton = GetComponent<Button>();

            if (backButton != null)
                backButton.onClick.AddListener(OnBackClicked);
        }

        void OnDestroy()
        {
            if (backButton != null)
                backButton.onClick.RemoveListener(OnBackClicked);
        }

        void OnBackClicked()
        {
            EventBus.Publish(GoBackEvent);
        }
    }
}
