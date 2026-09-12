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
    /// 批量搭建 M1 三个场景并写入 Build Settings。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/Scenes/批量重建 M1 场景
    ///   无头: -batchmode -quit -executeMethod PirateCrew.EditorTools.SceneSetup.BuildAll
    ///
    /// 【产物】Assets/Scenes/{Bootstrapper,MainMenu,Battle}.unity，Build Settings 顺序 0/1/2。
    ///
    /// 【说明】
    ///   本工程未安装 TextMeshPro 包（com.unity.textmeshpro 不在 manifest），
    ///   故 UI 文本统一用 UnityEngine.UI.Text + 内置 LegacyRuntime.ttf。
    ///   TODO: 安装 TMP 后把 Text 换成 TextMeshProUGUI 并在 Editor 脚本里改用 TMP_FontAsset.CreateFontAsset。
    /// </summary>
    public static class SceneSetup
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Art/Materials";
        const string GroundMaterialPath = MaterialsFolder + "/BattleGround.mat";

        static readonly Vector2 ButtonSize = new Vector2(240f, 48f);
        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);

        static Font _uiFont;

        /// <summary>无头 -executeMethod 入口。</summary>
        [MenuItem("PirateCrew/Scenes/批量重建 M1 场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            BuildBootstrapperScene();
            BuildMainMenuScene();
            BuildBattleScene();
            RegisterBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SceneSetup] M1 场景重建完成：Assets/Scenes/{Bootstrapper,MainMenu,Battle}.unity，"
                + "Build Settings 顺序 0/1/2。");
        }

        // ------------------------------------------------------------------
        // 各场景搭建
        // ------------------------------------------------------------------

        static void BuildBootstrapperScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Bootstrapper");
            go.AddComponent<Bootstrapper>();

            SaveScene(scene, SceneNames.Bootstrapper);
        }

        static void BuildMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 清屏色对应 Godot main_menu.tscn 的 ColorRect(0.08,0.12,0.2)。
            CreateCamera(new Color(0.08f, 0.12f, 0.2f, 1f));

            Canvas canvas = CreateCanvas("MainMenuCanvas");
            CreateEventSystem();

            // 标题（对应 Godot 的 LabelSettings：48 号、金色）。
            Text title = CreateText("Title", canvas.transform, "Pirate Crew 3D", 48,
                TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f, 1f));
            SetCenteredRect(title.rectTransform, new Vector2(700f, 80f), new Vector2(0f, 120f));

            // 三个按钮竖排居中（对应 Godot VBoxContainer 的 Battle/Campaign/Crew）。
            Button battleButton = CreateButton("BattleButton", canvas.transform, "进入战斗", CenterAnchor, new Vector2(0f, -40f));
            Button campaignButton = CreateButton("CampaignButton", canvas.transform, "单人战役", CenterAnchor, new Vector2(0f, -110f));
            Button crewButton = CreateButton("CrewButton", canvas.transform, "船员管理", CenterAnchor, new Vector2(0f, -180f));

            // 底部状态栏（战役/船员占位提示）。
            Text statusText = CreateText("StatusText", canvas.transform, string.Empty, 24,
                TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f, 1f));
            SetBottomRect(statusText.rectTransform, new Vector2(900f, 60f), 60f);

            // 控制器对象 + 序列化引用绑定。
            var controllerGo = new GameObject("MainMenuController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<MainMenuController>();

            var so = new SerializedObject(controller);
            so.FindProperty("battleButton").objectReferenceValue = battleButton;
            so.FindProperty("campaignButton").objectReferenceValue = campaignButton;
            so.FindProperty("crewButton").objectReferenceValue = crewButton;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.MainMenu);
        }

        static void BuildBattleScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 深蓝清屏色；相机置于 -Z 侧、identity 旋转即看向 +Z 方向（场景原点）。
            Camera camera = CreateCamera(new Color(0.02f, 0.05f, 0.12f, 1f));
            camera.transform.position = new Vector3(0f, 2f, -8f);
            camera.transform.rotation = Quaternion.identity;

            // URP/Lit 无光照会全黑，补一盏平行光。
            CreateDirectionalLight();
            CreateGround();

            Canvas canvas = CreateCanvas("BattleCanvas");
            CreateEventSystem();

            Text title = CreateText("PlaceholderText", canvas.transform, "Battle 场景占位（M2 实现）", 36,
                TextAnchor.MiddleCenter, Color.white);
            SetTopRect(title.rectTransform, new Vector2(900f, 70f), 60f);

            Button backButton = CreateButton("BackButton", canvas.transform, "返回主菜单",
                new Vector2(0.5f, 0f), new Vector2(0f, 60f));

            var placeholder = backButton.gameObject.AddComponent<BattlePlaceholder>();
            var so = new SerializedObject(placeholder);
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.Battle);
        }

        // ------------------------------------------------------------------
        // UI 构建辅助
        // ------------------------------------------------------------------

        static Font UiFont
        {
            get
            {
                // Unity 2022.2+ 内置字体资源名为 LegacyRuntime.ttf（旧版 Arial.ttf 已弃用）。
                if (_uiFont == null)
                    _uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _uiFont;
            }
        }

        static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
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

        static Text CreateText(string name, Transform parent, string content, int fontSize,
            TextAnchor alignment, Color color)
        {
            var go = CreateUiObject(name, parent);
            var text = go.AddComponent<Text>();
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
            var go = CreateUiObject(name, parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ButtonSize;
            rect.anchoredPosition = anchoredPosition;

            var image = go.AddComponent<Image>();
            // UGUI 默认按钮九宫格贴图（内置资源）与 DefaultControls 创建菜单一致。
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            Text text = CreateText("Text", go.transform, label, 24, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);

            return button;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 anchoredPosition)
        {
            rect.anchorMin = CenterAnchor;
            rect.anchorMax = CenterAnchor;
            rect.pivot = CenterAnchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
        }

        static void SetTopRect(RectTransform rect, Vector2 size, float offsetY)
        {
            var anchor = new Vector2(0.5f, 1f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, -offsetY);
        }

        static void SetBottomRect(RectTransform rect, Vector2 size, float offsetY)
        {
            var anchor = new Vector2(0.5f, 0f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, offsetY);
        }

        // ------------------------------------------------------------------
        // 场景内容辅助
        // ------------------------------------------------------------------

        static void CreateDirectionalLight()
        {
            var go = new GameObject("Directional Light", typeof(Light));
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = EnsureGroundMaterial();
        }

        static Material EnsureGroundMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[SceneSetup] 未找到 URP/Lit shader，退回内置 Standard。");
                shader = Shader.Find("Standard");
            }

            material = new Material(shader) { name = "BattleGround" };
            var gray = new Color(0.35f, 0.35f, 0.35f, 1f);
            material.color = gray;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", gray);

            AssetDatabase.CreateAsset(material, GroundMaterialPath);
            return material;
        }

        // ------------------------------------------------------------------
        // 保存与 Build Settings
        // ------------------------------------------------------------------

        static void SaveScene(Scene scene, string sceneName)
        {
            string path = ScenesFolder + "/" + sceneName + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("[SceneSetup] 保存场景失败: " + path);
        }

        static void RegisterBuildSettings()
        {
            string[] names = { SceneNames.Bootstrapper, SceneNames.MainMenu, SceneNames.Battle };
            var scenes = new EditorBuildSettingsScene[names.Length];
            for (int i = 0; i < names.Length; i++)
                scenes[i] = new EditorBuildSettingsScene(ScenesFolder + "/" + names[i] + ".unity", true);

            // 顺序即 index：Bootstrapper=0（入口）、MainMenu=1、Battle=2。
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
