using UnityEngine;

namespace PirateCrew.Core
{
    /// <summary>
    /// 组合根：全工程唯一"创建全局服务并接线"的地方。
    ///
    /// 【架构定位】依赖分层的最底层组装根。它只做三件事：
    ///   ① 建服务宿主对象 <c>Services</c>（<c>DontDestroyOnLoad</c>，跨场景存活）；
    ///   ② 显式创建全局服务并逐个登记进 <see cref="Services"/>（谁依赖谁 = 这里的一行代码）；
    ///   ③ 让入口场景（Build Settings index 0 的 Bootstrapper 场景）在 <c>Start</c> 里进主菜单。
    ///
    /// 【两条装配路径，同一个定义】<see cref="EnsureInstalled"/> 是**幂等**的装配函数：
    ///   · 走入口场景启动：场景里的 <see cref="Bootstrapper"/> 组件在自己 <c>Awake</c> 里调用它；
    ///   · 从任意场景直接按 Play（编辑器里选中 Battle.unity 直接点播放、或工具模式启动）：
    ///     <see cref="GameEntryPoint"/> 在首个场景加载前调用它。
    ///   两条路径共用同一份装配代码，因此"直接 Play 也能跑"这条隐式价值不会因为收口而丢。
    ///
    /// 【为什么这里不装配 AudioService / FxRoot / CampaignApi】
    ///   Core 是零游戏依赖的底座程序集（不引用 Gameplay/Campaign/UI 等高层，见重构总纲 §3.1），
    ///   编译期就看不到它们。它们各自在**自己模块**里用一个
    ///   <see cref="GameBootstrapAttribute"/> 标注的方法声明接线（服务创建 / 事件订阅），
    ///   由 <see cref="GameEntryPoint"/> 在 <see cref="GameBootstrapPhase.Initialize"/> 阶段统一调用——
    ///   于是"启动期会执行哪些接线"可以一次列全（<see cref="GameBootstrap.DescribeAll"/>），
    ///   而不是散落在各自的 <c>Awake</c> 或 <c>RuntimeInitializeOnLoadMethod</c> 里。
    ///
    /// 【不做什么】不做"场景内对象"的装配（那些归场景/Prefab 与各自的控制器）；
    /// 不在 <c>Awake</c> 里做隐式接线（架构审计 P0-3 的教训：接线藏在 UI 的 Awake 里，
    /// 换一个进入路径就少一份接线，且没有任何日志能证明它跑没跑）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Bootstrapper : MonoBehaviour
    {
        /// <summary>全局访问入口（放在入口场景里的唯一实例）。</summary>
        public static Bootstrapper Instance { get; private set; }

        /// <summary>全局服务宿主对象名（便于在 Hierarchy 中辨认）。</summary>
        const string ServicesObjectName = "Services";

        /// <summary>服务宿主对象（跨场景存活）；由 <see cref="EnsureInstalled"/> 创建。</summary>
        static GameObject _servicesHost;

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

            // 服务装配走与"任意场景直接 Play"完全相同的那条路（幂等）。
            EnsureInstalled();
        }

        void Start()
        {
            // 服务已在 Awake 中就绪，进入主菜单（默认）。
            if (!Services.TryGet<SceneLoader>(out SceneLoader loader))
            {
                Debug.LogError("[Bootstrapper] SceneLoader 未创建，无法进入主菜单。");
                return;
            }

            loader.ChangeScene(StartScene());
        }

        /// <summary>
        /// 启动后进哪个场景：默认主菜单；带 <c>-bootBattle</c> 时**直接进战斗**。
        ///
        /// 【为什么要有这条】评审/试玩要的是"双击就看见目标画面"，而默认流程是
        /// Bootstrapper → 主菜单 → 选关页 → 关卡 → 出战，每验一次都得点四下。
        /// 具体关卡（关卡号或海图 id）由 <c>WorldMapRuntime</c> 读同一个开关决定——
        /// 那一层在 Gameplay 程序集，Core 不能反向引用，所以这里只负责"跳过菜单"。
        /// </summary>
        static string StartScene()
        {
            if (CommandLineOptions.Has(ToolFlags.BootBattle))
                return SceneNames.Battle;

            return SceneNames.MainMenu;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// 装配全局服务（幂等）：宿主对象 + Core 自己拥有的服务。
        /// 返回是否**本次新建**了宿主（调用方据此写日志，便于诊断"两个宿主"）。
        /// </summary>
        public static bool EnsureInstalled()
        {
            // existing != null 走 UnityEngine.Object 的重载：宿主被销毁（引用还在）时视为未装配。
            if (Services.TryGet<SceneLoader>(out SceneLoader existing) && existing != null)
            {
                EnsureHostReference(existing);
                return false;
            }

            if (_servicesHost == null)
            {
                _servicesHost = new GameObject(ServicesObjectName);
                Object.DontDestroyOnLoad(_servicesHost);
            }

            // SceneLoader.Awake 会订阅 EventBus 并建过渡遮罩 Canvas（遮罩成为宿主的子物体，一并跨场景）。
            var sceneLoader = _servicesHost.AddComponent<SceneLoader>();
            Services.Register(sceneLoader);

            // SaveManager 由 Core 模块提供；此处只创建与登记，不调用其方法。
            var saveManager = _servicesHost.AddComponent<SaveManager>();
            Services.Register(saveManager);

            Log.Info("[Bootstrapper] 服务宿主已建立：SceneLoader / SaveManager 已登记进 Services。");
            return true;
        }

        /// <summary>宿主对象被外部销毁（场景重载等）后重新取回引用；取不到则下次装配重建。</summary>
        static void EnsureHostReference(SceneLoader loader)
        {
            if (_servicesHost == null && loader != null)
                _servicesHost = loader.gameObject;
        }
    }
}
