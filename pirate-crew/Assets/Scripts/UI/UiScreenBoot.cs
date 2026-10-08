using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PirateCrew.UI
{
    /// <summary>
    /// 菜单/管理屏的**运行时自建引导件**（场景里只有 相机 + EventSystem + 本件，
    /// UI 树在 Awake 全量构建——先例是 UIShowcase 的 UiShowcaseBoot 模式）。
    ///
    /// 【顺序】构建 UI → 建控制器对象（**先置非激活**，AddComponent 后注入 refs，
    /// 再激活——这样控制器 Awake 首跑时字段已齐，等价于原装配器按字段名回写的时序）。
    /// </summary>
    public sealed class UiScreenBoot : MonoBehaviour
    {
        /// <summary>本场景要自建哪张屏。</summary>
        public enum ScreenKind
        {
            MainMenu = 0,
            CrewManagement = 1,
            LevelSelect = 2,
        }

        /// <summary>屏类型（装配器生成最小场景时写入并序列化）。</summary>
        [SerializeField] public ScreenKind Kind = ScreenKind.MainMenu;

        void Awake()
        {
            EnsureEventSystem();
            Canvas canvas = CreateCanvas(Kind + "Canvas");

            switch (Kind)
            {
                case ScreenKind.CrewManagement:
                {
                    UiScreenBuilder.CrewRefs refs = UiScreenBuilder.BuildCrewManagement(canvas.transform);
                    AttachController<CrewManagementController>(canvas.transform, "CrewManagementController",
                        c => c.Bind(refs));
                    break;
                }
                case ScreenKind.LevelSelect:
                {
                    UiScreenBuilder.LevelRefs refs = UiScreenBuilder.BuildLevelSelect(canvas.transform);
                    AttachController<LevelSelectController>(canvas.transform, "LevelSelectController",
                        c => c.Bind(refs));
                    break;
                }
                default:
                {
                    UiScreenBuilder.MainMenuRefs refs = UiScreenBuilder.BuildMainMenu(canvas.transform);
                    AttachController<MainMenuController>(canvas.transform, "MainMenuController",
                        c => c.Bind(refs));
                    break;
                }
            }

            // 场景烘焙的画布带着出厂档（PixelSkin.Unit）进来，按运行期真值重写一遍。
            PixelScaleService.ApplyAll();
        }

        /// <summary>
        /// 控制器挂画布下：**非激活期注入 refs，激活才跑 Awake**（字段已齐）——
        /// 这是控制器 Bind 契约（"必须在对象激活前调用"）的唯一正确时序。
        /// 【2026-10-08 修】原实现先 SetActive(true) 再在外层 Bind：激活那刻字段全空，
        /// Awake 里所有 <c>if (x != null)</c> 挂监听全部跳过——主菜单五钮/设置滑条/选关/编成
        /// 全量失灵（创始人实拍"调试场景点了没反应、按压反馈却有"；探针实测 onClick 监听数=0）。
        /// 注入挪进激活前，与文档注释的口径一致。
        /// </summary>
        static T AttachController<T>(Transform canvas, string name, System.Action<T> bind) where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            go.SetActive(false);
            T controller = go.AddComponent<T>();
            bind?.Invoke(controller);
            go.SetActive(true);
            return controller;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length > 0)
                return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            // 【恒定像素密度（红警2 式）】出厂档 = PixelSkin.Unit；运行期真值由
            // PixelScaleService.ApplyAll 统一重写（本 Awake 尾部就会跑一次）。
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = PixelSkin.Unit;

            return canvas;
        }
    }
}
