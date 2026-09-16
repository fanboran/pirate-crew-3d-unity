using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 游戏引导器（对应 Godot 的 autoload 机制在 Unity 里的载体）。
    ///
    /// 【架构定位】
    ///   依赖分层的最底层组装根：入口场景 Bootstrapper（Build Settings index 0）里唯一手动摆放的对象。
    ///   它负责创建所有全局服务（SceneLoader / SaveManager），并统一 DontDestroyOnLoad，
    ///   使这些服务跨场景存活；业务场景不得再手工摆放它们的实例。
    ///
    /// 【启动流程】
    ///   Awake：去重（防重复实例）→ 自身 DontDestroyOnLoad → 建 "Services" 挂全局服务。
    ///   Start：跳转主菜单（此时服务已就绪，且必须在全部 Awake 之后）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Bootstrapper : MonoBehaviour
    {
        /// <summary>全局访问入口（放在入口场景里的唯一实例）。</summary>
        public static Bootstrapper Instance { get; private set; }

        /// <summary>全局服务宿主对象名（便于在 Hierarchy 中辨认）。</summary>
        const string ServicesObjectName = "Services";

        void Awake()
        {
            // 重复实例保护：从别的场景误入 Bootstrapper 时，只保留首个实例。
            if (Instance != null && Instance != this)
            {
                global::PirateCrew.Core.Log.Warn("[Bootstrapper] 已存在实例，销毁重复对象: " + name);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            CreateServices();
        }

        void Start()
        {
            // 服务已在 Awake 中就绪，进入主菜单。
            if (SceneLoader.Instance == null)
            {
                Debug.LogError("[Bootstrapper] SceneLoader 未创建，无法进入主菜单。");
                return;
            }

            SceneLoader.Instance.ChangeScene(SceneNames.MainMenu);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 创建全局服务宿主并挂载各 Manager（对应 Godot 的多个 autoload 单例）。
        /// 全部 DontDestroyOnLoad，跨场景存活。
        /// </summary>
        void CreateServices()
        {
            var services = new GameObject(ServicesObjectName);
            DontDestroyOnLoad(services);

            // SceneLoader.Awake 会订阅 EventBus 并建过渡遮罩 Canvas（遮罩成为 services 的子物体，一并跨场景）。
            services.AddComponent<SceneLoader>();

            // SaveManager 由 Core 模块提供；此处只挂载，不调用其方法。
            services.AddComponent<SaveManager>();
        }
    }
}
