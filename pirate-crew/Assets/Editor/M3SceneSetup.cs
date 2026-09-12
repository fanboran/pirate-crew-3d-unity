using System.IO;
using PirateCrew.Core;
using PirateCrew.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 批量搭建 M3 两个场景（船员管理 / 关卡选择）并把 5 个场景写进 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/重建 M3 管理场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.M3SceneSetup.BuildAll
    ///
    /// 【产物】
    ///   Assets/Scenes/CrewManagement.unity、Assets/Scenes/LevelSelect.unity；
    ///   Build Settings = Bootstrapper(0) / MainMenu(1) / Battle(2) / CrewManagement(3) / LevelSelect(4)。
    ///
    /// 【为什么不动 Battle 场景】Battle 由 <see cref="M2BattleSceneSetup"/> 负责装配（含战斗引用接线），
    ///   本脚本只在 **不重建** 它的前提下把它保留在 Build Settings 里
    ///   （注意 <c>SceneSetup.BuildAll</c> / <c>M2BattleSceneSetup.BuildAll</c> 会把列表覆盖成 3 个场景，
    ///   重建过它们之后需要再跑一次本脚本恢复 5 个场景）。
    ///
    /// 【与 M1/M2 装配脚本的关系】本文件是自包含的：UI 构建辅助照抄 <c>SceneSetup</c> 的写法，
    ///   避免改动既有 Editor 脚本（M3 派单限定 <c>Assets/Editor/</c> 只新增本文件）。
    ///
    /// 【约定】本工程未装 TMP，UI 一律 legacy <c>UnityEngine.UI</c> + 内置 LegacyRuntime.ttf
    ///   （见 <c>SceneSetup</c> 类头）。
    /// </summary>
    public static class M3SceneSetup
    {
        const string ScenesFolder = "Assets/Scenes";

        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCenterAnchor = new Vector2(0.5f, 1f);
        static readonly Vector2 BottomCenterAnchor = new Vector2(0.5f, 0f);

        static readonly Color BackgroundColor = new Color(0.08f, 0.12f, 0.2f, 1f);
        static readonly Color TitleColor = new Color(1f, 0.85f, 0.3f, 1f);
        static readonly Color HintColor = new Color(0.85f, 0.9f, 1f, 1f);

        static Font _uiFont;

        /// <summary>无头 -executeMethod 入口；也可从菜单调用。</summary>
        [MenuItem("PirateCrew/Scenes/重建 M3 管理场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);

            BuildCrewManagementScene();
            BuildLevelSelectScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[M3SceneSetup] M3 管理场景重建完成。\n"
                + "  场景: " + ScenesFolder + "/CrewManagement.unity、"
                + ScenesFolder + "/LevelSelect.unity\n"
                + "  Build Settings: Bootstrapper(0) / MainMenu(1) / Battle(2, 未重建) / "
                + "CrewManagement(3) / LevelSelect(4)");
        }

        // ------------------------------------------------------------------
        // 船员管理场景
        // ------------------------------------------------------------------

        static void BuildCrewManagementScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(BackgroundColor);
            Canvas canvas = CreateCanvas("CrewManagementCanvas");
            CreateEventSystem();

            CreateTitle(canvas.transform, "船员管理", 44);

            Text summary = CreateText("SummaryText", canvas.transform, string.Empty, 22,
                TextAnchor.MiddleCenter, HintColor);
            SetAnchored(summary.rectTransform, TopCenterAnchor, new Vector2(1600f, 40f), new Vector2(0f, 96f));

            // 名册列表容器（行由 CrewManagementController 运行时生成）。
            RectTransform list = CreatePanel("CrewList", canvas.transform, 160f, 160f, 150f, 170f);

            Text status = CreateText("StatusText", canvas.transform, string.Empty, 22,
                TextAnchor.MiddleCenter, HintColor);
            SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(1600f, 40f), new Vector2(0f, 128f));

            Button levelSelectButton = CreateButton("LevelSelectButton", canvas.transform, "选择关卡", BottomCenterAnchor, new Vector2(-260f, 60f));
            Button saveButton = CreateButton("SaveButton", canvas.transform, "保存进度", BottomCenterAnchor, new Vector2(0f, 60f));
            Button backButton = CreateButton("BackButton", canvas.transform, "返回主菜单", BottomCenterAnchor, new Vector2(260f, 60f));

            var controllerGo = new GameObject("CrewManagementController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<CrewManagementController>();

            var so = new SerializedObject(controller);
            so.FindProperty("summaryText").objectReferenceValue = summary;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("crewListContainer").objectReferenceValue = list;
            so.FindProperty("levelSelectButton").objectReferenceValue = levelSelectButton;
            so.FindProperty("saveButton").objectReferenceValue = saveButton;
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, M3Scenes.CrewManagement);
        }

        // ------------------------------------------------------------------
        // 关卡选择场景
        // ------------------------------------------------------------------

        static void BuildLevelSelectScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera(BackgroundColor);
            Canvas canvas = CreateCanvas("LevelSelectCanvas");
            CreateEventSystem();

            CreateTitle(canvas.transform, "单人战役", 44);

            Text header = CreateText("HeaderText", canvas.transform, string.Empty, 24,
                TextAnchor.MiddleCenter, TitleColor);
            SetAnchored(header.rectTransform, TopCenterAnchor, new Vector2(1600f, 40f), new Vector2(0f, 96f));

            // 结算横幅：打完一局回到本场景时由控制器写入内容。
            Text result = CreateText("ResultText", canvas.transform, string.Empty, 22,
                TextAnchor.MiddleCenter, HintColor);
            SetAnchored(result.rectTransform, TopCenterAnchor, new Vector2(1700f, 40f), new Vector2(0f, 140f));

            // 章节按钮行（按钮由控制器运行时生成）。
            RectTransform chapters = CreateRect("ChapterContainer", canvas.transform);
            chapters.anchorMin = new Vector2(0.5f, 1f);
            chapters.anchorMax = new Vector2(0.5f, 1f);
            chapters.pivot = new Vector2(0.5f, 1f);
            chapters.sizeDelta = new Vector2(520f, 44f);
            chapters.anchoredPosition = new Vector2(0f, -190f);

            // 关卡列表容器。
            RectTransform list = CreatePanel("LevelList", canvas.transform, 260f, 260f, 260f, 170f);

            Text status = CreateText("StatusText", canvas.transform, string.Empty, 22,
                TextAnchor.MiddleCenter, HintColor);
            SetAnchored(status.rectTransform, BottomCenterAnchor, new Vector2(1700f, 40f), new Vector2(0f, 128f));

            Button crewButton = CreateButton("CrewButton", canvas.transform, "船员管理", BottomCenterAnchor, new Vector2(-180f, 60f));
            Button backButton = CreateButton("BackButton", canvas.transform, "返回", BottomCenterAnchor, new Vector2(180f, 60f));

            var controllerGo = new GameObject("LevelSelectController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<LevelSelectController>();

            var so = new SerializedObject(controller);
            so.FindProperty("headerText").objectReferenceValue = header;
            so.FindProperty("resultText").objectReferenceValue = result;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("chapterContainer").objectReferenceValue = chapters;
            so.FindProperty("levelListContainer").objectReferenceValue = list;
            so.FindProperty("crewButton").objectReferenceValue = crewButton;
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, M3Scenes.LevelSelect);
        }

        // ------------------------------------------------------------------
        // UI 构建辅助（照抄 SceneSetup 的写法，见类头说明）
        // ------------------------------------------------------------------

        static Font UiFont
        {
            get
            {
                if (_uiFont == null)
                    _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                return _uiFont;
            }
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        static void CreateEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static Camera CreateCamera(Color clearColor)
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clearColor;
            return camera;
        }

        static Text CreateTitle(Transform parent, string content, int fontSize)
        {
            Text title = CreateText("Title", parent, content, fontSize, TextAnchor.MiddleCenter, TitleColor);
            SetAnchored(title.rectTransform, TopCenterAnchor, new Vector2(900f, 60f), new Vector2(0f, 30f));
            return title;
        }

        static Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = UiFont;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static Button CreateButton(string name, Transform parent, string label, Vector2 anchor, Vector2 anchoredPosition)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = CenterAnchor;
            rect.sizeDelta = new Vector2(200f, 48f);
            rect.anchoredPosition = anchoredPosition;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText("Text", rect, label, 24, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);

            return button;
        }

        /// <summary>建一个四边内缩的容器（列表/面板用）。</summary>
        static RectTransform CreatePanel(string name, Transform parent, float left, float right, float top, float bottom)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void SetAnchored(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        // ------------------------------------------------------------------
        // 保存与 Build Settings
        // ------------------------------------------------------------------

        static void SaveScene(Scene scene, string sceneName)
        {
            string path = ScenesFolder + "/" + sceneName + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("[M3SceneSetup] 保存场景失败: " + path);
        }

        /// <summary>
        /// 写 5 个场景。Battle 保持 index 2（<c>SceneLoader</c> 与既有测试都按名字加载，顺序不影响，
        /// 但保持与 M1/M2 一致便于人工核对）。
        /// </summary>
        static void RegisterBuildSettings()
        {
            string[] names =
            {
                SceneNames.Bootstrapper,
                SceneNames.MainMenu,
                SceneNames.Battle,
                M3Scenes.CrewManagement,
                M3Scenes.LevelSelect,
            };

            var scenes = new EditorBuildSettingsScene[names.Length];
            for (int i = 0; i < names.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(ScenesFolder + "/" + names[i] + ".unity", true);

            EditorBuildSettings.scenes = scenes;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
