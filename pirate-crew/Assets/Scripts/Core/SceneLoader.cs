using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.Core
{
    /// <summary>
    /// 场景加载与过渡管理器（翻译自 Godot <c>core/autoload/scene_manager.gd</c>）。
    ///
    /// 【架构定位】
    ///   统一管理场景切换：异步加载 + 黑屏淡入淡出过渡 + 场景栈（前进/后退导航）。
    ///   所有场景切换请求都通过本管理器进行，禁止业务代码直接调用 SceneManager.LoadScene。
    ///
    /// 【核心职责】
    ///   1. 异步场景加载（LoadSceneAsync，避免卡顿）
    ///   2. 过渡动画（代码建全屏黑幕 Canvas，不依赖场景摆放）
    ///   3. 场景栈管理（GoBack 返回上一场景）
    ///   4. 加载阶段通知（本地 event + 转发到 EventBus）
    ///
    /// 【使用方式】
    ///   (a) 直接调用: SceneLoader.Instance.ChangeScene("GameScene")
    ///   (b) 通过 EventBus 解耦:
    ///       EventBus.Publish("change_scene", "GameScene")
    ///       EventBus.Publish("change_scene", new Dictionary&lt;string, object&gt; {
    ///           { "path", "GameScene" }, { "transition", true } })
    ///       EventBus.Publish("go_back")
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneLoader : MonoBehaviour
    {
        /// <summary>过渡遮罩的最小步进（秒）：单帧最长只推进 1/30 秒，防止一次卡顿吃完整段淡入淡出。</summary>
        const float MaxFrameStep = 1f / 30f;

        /// <summary>过渡遮罩 Canvas 的 sortingOrder（对应 Godot CanvasLayer.layer = 128）。</summary>
        const int OverlaySortingOrder = 128;

        [SerializeField] float transitionDuration = 0.4f;

        readonly Stack<string> _sceneStack = new Stack<string>();
        bool _isLoading;
        Canvas _overlayCanvas;
        Image _overlayImage;

        /// <summary>全局访问入口。由 Bootstrapper 创建本组件后可用。</summary>
        public static SceneLoader Instance { get; private set; }

        /// <summary>开始加载某场景时触发（本地事件，等价 Godot signal scene_load_started）。</summary>
        public event Action<string> SceneLoadStarted;

        /// <summary>场景加载完成（已切换）时触发（等价 Godot signal scene_load_completed）。</summary>
        public event Action<string> SceneLoadCompleted;

        /// <summary>整段过渡（含淡入）结束时触发（等价 Godot signal scene_transition_finished）。</summary>
        public event Action<string> SceneTransitionFinished;

        /// <summary>场景栈深度（对应 Godot get_stack_depth）。</summary>
        public int StackDepth => _sceneStack.Count;

        /// <summary>当前过渡时长（秒）。</summary>
        public float TransitionDuration => transitionDuration;

        /// <summary>是否正在加载中。</summary>
        public bool IsLoading => _isLoading;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                global::PirateCrew.Core.Log.Warn("[SceneLoader] 已存在实例，销毁重复对象: " + name);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            CreateOverlay();

            // 对应 Godot _ready 里订阅 EventBus 的 "change_scene" / "go_back"
            EventBus.Subscribe(SceneEvents.ChangeScene, OnChangeSceneRequest);
            EventBus.Subscribe(SceneEvents.GoBack, OnGoBackRequest);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe(SceneEvents.ChangeScene, OnChangeSceneRequest);
            EventBus.Unsubscribe(SceneEvents.GoBack, OnGoBackRequest);

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 切换到指定场景（异步加载 + 可选过渡动画）——**前进导航**：把当前场景压入返回栈。
        /// 目标与当前场景同名时视为「原地重载」，不压栈（「再来一局」不堆历史）。
        /// </summary>
        /// <param name="sceneName">目标场景名或路径（必须在 Build Settings 中）。</param>
        /// <param name="transition">是否使用过渡动画，默认 true。</param>
        public void ChangeScene(string sceneName, bool transition = true)
        {
            RequestSceneChange(sceneName, transition, pushCurrent: true);
        }

        /// <summary>
        /// 返回上一个场景（对应 Godot go_back）：弹栈加载，**不**把当前场景压回栈——
        /// 否则每次「进二级页 → 返回」都会把已离开的场景残留在栈里，栈永不收敛
        /// （审计 代码审计报告 §一.1）。栈空时仅告警。
        /// </summary>
        public void GoBack()
        {
            if (_sceneStack.Count == 0)
            {
                global::PirateCrew.Core.Log.Warn("[SceneLoader] 场景栈为空，无法返回");
                return;
            }

            string previousScene = _sceneStack.Pop();
            RequestSceneChange(previousScene, true, pushCurrent: false);
        }

        /// <summary>
        /// 前进导航是否把当前场景记为返回点（纯函数，供无头测试钉口径）：
        /// 当前/目标场景名为空（如编辑器未命名测试场景）无从记录，不压；
        /// 目标与当前同名 = 原地重载（「再来一局」），不压——原 <c>replaceTop</c> 补丁由此退役，
        /// 它「先弹再压」在首次重开时会误吃栈顶下方的正确历史。
        /// 栈不变式：栈里只存「要返回去的场景」，当前场景不在栈上。
        /// </summary>
        public static bool ShouldRecordReturnPoint(string currentSceneName, string targetSceneName)
        {
            if (string.IsNullOrEmpty(currentSceneName) || string.IsNullOrEmpty(targetSceneName))
                return false;
            return currentSceneName != targetSceneName;
        }

        void RequestSceneChange(string sceneName, bool transition, bool pushCurrent)
        {
            if (_isLoading)
            {
                global::PirateCrew.Core.Log.Warn("[SceneLoader] 正在加载中，忽略请求: " + sceneName);
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[SceneLoader] 场景名为空");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("[SceneLoader] 场景不存在或未加入 Build Settings: " + sceneName);
                return;
            }

            _isLoading = true;

            var current = SceneManager.GetActiveScene();
            if (pushCurrent && ShouldRecordReturnPoint(current.name, sceneName))
                _sceneStack.Push(current.name);

            RaiseSceneLoadStarted(sceneName);
            StartCoroutine(LoadRoutine(sceneName, transition));
        }

        /// <summary>清空场景栈（对应 Godot clear_stack）。</summary>
        public void ClearStack()
        {
            _sceneStack.Clear();
        }

        /// <summary>设置过渡动画时长（秒），负值夹到 0（对应 Godot set_transition_duration）。</summary>
        public void SetTransitionDuration(float duration)
        {
            transitionDuration = Mathf.Max(duration, 0f);
        }

        // ------------------------------------------------------------------
        // 内部实现
        // ------------------------------------------------------------------

        /// <summary>代码创建全屏遮罩 Canvas（参照 LightGive TransitionManager 的 CreateCanvas/CreateImage 写法）。</summary>
        void CreateOverlay()
        {
            var canvasGo = new GameObject("SceneLoaderOverlay", typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _overlayCanvas = canvasGo.GetComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = OverlaySortingOrder;
            _overlayCanvas.pixelPerfect = false;

            var imageGo = new GameObject("FadeImage", typeof(Image));
            imageGo.transform.SetParent(canvasGo.transform, false);

            _overlayImage = imageGo.GetComponent<Image>();
            _overlayImage.color = new Color(0f, 0f, 0f, 0f); // 初始透明
            _overlayImage.raycastTarget = false;

            var rect = _overlayImage.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localPosition = Vector3.zero;
            rect.localScale = Vector3.one;
        }

        IEnumerator LoadRoutine(string sceneName, bool transition)
        {
            // 1. 淡出至全黑
            if (transition)
                yield return FadeRoutine(0f, 1f);

            // 2. 异步加载（allowSceneActivation 默认 true，不需要门控）
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError("[SceneLoader] 场景加载失败: " + sceneName);
                if (transition)
                    yield return FadeRoutine(1f, 0f);
                _isLoading = false;
                yield break;
            }

            while (!operation.isDone)
                yield return null;

            RaiseSceneLoadCompleted(sceneName);

            // 3. 淡入并收尾
            if (transition)
                yield return FadeRoutine(1f, 0f);

            _isLoading = false;
            RaiseSceneTransitionFinished(sceneName);
        }

        /// <summary>
        /// 淡入淡出。两个要点（参照 mygamedevtools LoadingFader）：
        /// 用 unscaledDeltaTime（暂停时也能推进）、每帧步进夹到 MaxFrameStep（防长帧吃完整段），
        /// 收尾显式置终值而非依赖最后一次插值。
        /// </summary>
        IEnumerator FadeRoutine(float fromAlpha, float toAlpha)
        {
            SetOverlayAlpha(fromAlpha);
            _overlayImage.raycastTarget = true; // 过渡期间挡住输入

            float duration = Mathf.Max(transitionDuration, 0f);
            if (duration > 0f)
            {
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameStep);
                    float t = Mathf.Clamp01(elapsed / duration);
                    SetOverlayAlpha(Mathf.Lerp(fromAlpha, toAlpha, t));
                    yield return null;
                }
            }

            SetOverlayAlpha(toAlpha);
            _overlayImage.raycastTarget = toAlpha > 0f;
        }

        void SetOverlayAlpha(float alpha)
        {
            if (_overlayImage == null)
                return;

            var color = _overlayImage.color;
            color.r = 0f;
            color.g = 0f;
            color.b = 0f;
            color.a = alpha;
            _overlayImage.color = color;
        }

        void RaiseSceneLoadStarted(string sceneName)
        {
            SceneLoadStarted?.Invoke(sceneName);
            EventBus.Publish(SceneEvents.SceneLoadStarted, sceneName);
        }

        void RaiseSceneLoadCompleted(string sceneName)
        {
            SceneLoadCompleted?.Invoke(sceneName);
            EventBus.Publish(SceneEvents.SceneLoadCompleted, sceneName);
        }

        void RaiseSceneTransitionFinished(string sceneName)
        {
            SceneTransitionFinished?.Invoke(sceneName);
            EventBus.Publish(SceneEvents.SceneTransitionFinished, sceneName);
        }

        // ------------------------------------------------------------------
        // EventBus 请求处理（对应 Godot _on_change_scene_request / _on_go_back_request）
        // ------------------------------------------------------------------

        void OnChangeSceneRequest(object payload)
        {
            switch (payload)
            {
                case null:
                    return;
                case string sceneName:
                    ChangeScene(sceneName, true);
                    break;
                case IDictionary<string, object> dict:
                {
                    if (!dict.TryGetValue("path", out var path) || !(path is string target) || string.IsNullOrEmpty(target))
                        return;

                    bool transition = true;
                    if (dict.TryGetValue("transition", out var transitionValue) && transitionValue is bool flag)
                        transition = flag;

                    ChangeScene(target, transition);
                    break;
                }
            }
        }

        void OnGoBackRequest(object payload)
        {
            GoBack();
        }
    }
}
