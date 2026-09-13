using System.IO;
using PirateCrew.Core;
using PirateCrew.UI;
using TMPro;
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
    /// 【本波次改造（中文化 + 海盗风重设计）】
    ///   · 主菜单按 docs/UI-UX与中文本地化规范.md §3.2 线框图重排：羊皮纸卷轴标题底板 +
    ///     木板背景 + 黄铜/木板/羊皮纸/危险红四类按钮 + 「设置」「退出游戏」两个新按钮（§4.2）；
    ///   · 文本统一 <see cref="TextMeshProUGUI"/> + 中文字体（<see cref="MenuUiBuilder"/>）；
    ///   · Battle 占位场景文案改中文「战斗场景（占位）」（§4.10 第 16 条）。
    /// </summary>
    public static class SceneSetup
    {
        const string ScenesFolder = "Assets/Scenes";
        const string MaterialsFolder = "Assets/Art/Materials";
        const string GroundMaterialPath = MaterialsFolder + "/BattleGround.mat";

        static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);

        /// <summary>无头 -executeMethod 入口。</summary>
        [MenuItem("PirateCrew/Scenes/批量重建 M1 场景")]
        public static void BuildAll()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            BuildBootstrapperScene();
            BuildMainMenuScene();
            // Battle.unity 自 M2 起归 M2BattleSceneSetup 全量重建（完整战斗场景），
            // 这里**不再生成 M1 占位场景**——否则会覆盖 M2 的产物（ArtGate 第⑦步的输出
            // 被第⑨步覆盖的事故由此而来）。占位场景仅存在于 M1 时代。
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

            // 深棕暖色清屏色（木板背景之下的兜底色）。
            CreateCamera(new Color(0.14f, 0.10f, 0.07f, 1f));

            Canvas canvas = CreateCanvas("MainMenuCanvas");
            CreateEventSystem();

            TMP_FontAsset titleFont = MenuUiBuilder.TitleFont;
            TMP_FontAsset bodyFont = MenuUiBuilder.BodyFont;
            TMP_FontAsset secondaryFont = MenuUiBuilder.SecondaryFont;

            // z=0 背景：全屏木板 + 四周压暗，形成「船舱木墙」底。
            MenuUiBuilder.CreateWoodBackdrop("WoodBackdrop", canvas.transform, Color.white);
            MenuUiBuilder.CreateWoodBackdrop("Vignette", canvas.transform, new Color(0f, 0f, 0f, 0.35f));

            // 标题底板：羊皮纸卷轴 + 黄铜描边（§3.2）。
            RectTransform titleBoard = MenuUiBuilder.CreatePanel("TitleBoard", canvas.transform,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f),
                new Vector2(760f, 120f), UiSprites.Kind.PanelParchment);
            TextMeshProUGUI title = MenuUiBuilder.CreateText("Title", titleBoard,
                UiStrings.MainTitle, UiTheme.FontDisplay, TextAlignmentOptions.Center, UiTheme.Ink, titleFont);
            MenuUiBuilder.Stretch(title.rectTransform, 8f);
            MenuUiBuilder.ApplyTitleOutline(title);

            // 5 个按钮竖排居中（§3.2：主按钮 480×64；次按钮 480×52）。
            Button battleButton = MenuUiBuilder.CreateButton("BattleButton", canvas.transform,
                UiStrings.MainBattle, CenterAnchor, new Vector2(0f, 110f), new Vector2(480f, 64f),
                bodyFont, UiSprites.Kind.ButtonBrass, UiTheme.Ink, UiTheme.FontHud);

            Button campaignButton = MenuUiBuilder.CreateButton("CampaignButton", canvas.transform,
                UiStrings.MainCampaign, CenterAnchor, new Vector2(0f, 34f), new Vector2(480f, 64f),
                bodyFont, UiSprites.Kind.ButtonWood);

            Button crewButton = MenuUiBuilder.CreateButton("CrewButton", canvas.transform,
                UiStrings.MainCrew, CenterAnchor, new Vector2(0f, -42f), new Vector2(480f, 64f),
                bodyFont, UiSprites.Kind.ButtonWood);

            Button settingsButton = MenuUiBuilder.CreateButton("SettingsButton", canvas.transform,
                UiStrings.MainSettings, CenterAnchor, new Vector2(0f, -112f), new Vector2(480f, 52f),
                bodyFont, UiSprites.Kind.ButtonParchment);

            Button quitButton = MenuUiBuilder.CreateButton("QuitButton", canvas.transform,
                UiStrings.MainQuit, CenterAnchor, new Vector2(0f, -178f), new Vector2(480f, 52f),
                bodyFont, UiSprites.Kind.ButtonDanger);

            // 左下：版本号 + 存档状态（§3.2 左下 24,24 FONT_HINT）。
            TextMeshProUGUI versionText = MenuUiBuilder.CreateText("VersionText", canvas.transform,
                UiStrings.MainVersion, UiTheme.FontHint, TextAlignmentOptions.BottomLeft,
                UiTheme.BrassLight, secondaryFont);
            MenuUiBuilder.SetAnchored(versionText.rectTransform, new Vector2(0f, 0f), new Vector2(400f, 26f),
                new Vector2(UiTheme.Safe, UiTheme.Safe));

            TextMeshProUGUI statusText = MenuUiBuilder.CreateText("StatusText", canvas.transform,
                string.Empty, UiTheme.FontHint, TextAlignmentOptions.BottomLeft, UiTheme.TextLight, secondaryFont);
            MenuUiBuilder.SetAnchored(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(500f, 26f),
                new Vector2(UiTheme.Safe, UiTheme.Safe + 28f));

            // 设置界面（占位，默认隐藏）。
            MenuUiBuilder.SettingsPanelResult settings = MenuUiBuilder.BuildSettingsPanel(canvas.transform);

            // 控制器对象 + 序列化引用绑定。
            var controllerGo = new GameObject("MainMenuController", typeof(RectTransform));
            controllerGo.transform.SetParent(canvas.transform, false);
            var controller = controllerGo.AddComponent<MainMenuController>();

            var so = new SerializedObject(controller);
            so.FindProperty("battleButton").objectReferenceValue = battleButton;
            so.FindProperty("campaignButton").objectReferenceValue = campaignButton;
            so.FindProperty("crewButton").objectReferenceValue = crewButton;
            so.FindProperty("settingsButton").objectReferenceValue = settingsButton;
            so.FindProperty("quitButton").objectReferenceValue = quitButton;
            so.FindProperty("statusText").objectReferenceValue = statusText;
            so.FindProperty("versionText").objectReferenceValue = versionText;
            so.FindProperty("settingsPanel").objectReferenceValue = settings.Root;
            so.FindProperty("settingsBackButton").objectReferenceValue = settings.BackButton;
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

            // 占位说明改中文（规范 §4.10 第 16 条）。
            TextMeshProUGUI title = MenuUiBuilder.CreateText("PlaceholderText", canvas.transform,
                UiStrings.BattlePlaceholderNote, UiTheme.FontTitle, TextAlignmentOptions.Center,
                UiTheme.TextLight, MenuUiBuilder.TitleFont);
            MenuUiBuilder.SetAnchored(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(900f, 70f),
                new Vector2(0f, -60f));

            Button backButton = MenuUiBuilder.CreateButton("BackButton", canvas.transform,
                UiStrings.BackToMainMenu, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                new Vector2(240f, 48f), MenuUiBuilder.BodyFont, UiSprites.Kind.ButtonWood);

            var placeholder = backButton.gameObject.AddComponent<BattlePlaceholder>();
            var so = new SerializedObject(placeholder);
            so.FindProperty("backButton").objectReferenceValue = backButton;
            so.ApplyModifiedPropertiesWithoutUndo();

            SaveScene(scene, SceneNames.Battle);
        }

        // ------------------------------------------------------------------
        // 场景内容辅助
        // ------------------------------------------------------------------

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
