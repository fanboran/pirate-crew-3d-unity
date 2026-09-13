using System.Collections;
using System.IO;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.PirateCrew.ArtReview
{
    /// <summary>
    /// 独立播放器的自动评审出图：协调者在命令行传 <c>-artReviewOut &lt;绝对目录&gt;</c> 启动播放器，
    /// 本组件直接加载 Battle 场景、等战斗就绪后按机位预设逐张截图落盘，完毕自动退出。
    ///
    /// 【为什么要有它】美术调参必须"看着画面调"。编辑器出图依赖人工点菜单，
    /// 而协调者可以无头构建播放器并自己启动它——由此获得不依赖任何人的视觉迭代闭环：
    /// 改参数 → 重建 → 跑播放器出图 → 读图评审 → 再改。
    ///
    /// 【零侵入】不带参数启动（正常游玩/测试）时本组件在 Awake 即自毁，无任何开销。
    /// 查找场景节点用的是根对象逐层按名匹配（仅本调试组件允许，运行时代码仍禁 GameObject.Find）。
    /// 机位语义镜像编辑器侧 ArtReviewShots（那边是 Editor 程序集，运行时引用不到）。
    /// </summary>
    public sealed class PlayerArtCapture : MonoBehaviour
    {
        struct Shot
        {
            public string Name;
            public bool HudVisible;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Fov;
        }

        const int Width = 1920;
        const int Height = 1080;
        const float ReadyTimeoutSeconds = 60f;

        string _outDir;
        Camera _camera;
        Canvas _hud;
        Transform _focusUnit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            string outDir = null;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-artReviewOut")
                {
                    outDir = args[i + 1];
                    break;
                }
            }
            if (string.IsNullOrEmpty(outDir))
                return; // 正常启动：零开销，什么都不装。

            var go = new GameObject("[PlayerArtCapture]");
            go.AddComponent<PlayerArtCapture>()._outDir = outDir;
            DontDestroyOnLoad(go);
        }

        IEnumerator Start()
        {
            Screen.SetResolution(Width, Height, false);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            // 等 Bootstrapper 的引导流程走完（它会异步加载主菜单并覆盖任何抢先加载的场景），
            // 再切战斗场景——否则 LoadScene(Battle) 会被随后的 MainMenu 加载顶掉，拍到的全是菜单。
            float bootDeadline = Time.unscaledTime + 15f;
            while (SceneManager.GetActiveScene().name != SceneNames.MainMenu
                && Time.unscaledTime < bootDeadline)
                yield return null;
            yield return new WaitForSeconds(0.5f);

            SceneManager.LoadScene(SceneNames.Battle, LoadSceneMode.Single);
            yield return null;
            if (SceneManager.GetActiveScene().name != SceneNames.Battle)
            {
                Debug.LogError("[PlayerArtCapture] Battle 场景加载失败，当前场景："
                    + SceneManager.GetActiveScene().name + "——中止采集");
                Application.Quit(1);
                yield break;
            }

            // 等战斗就绪：单位已生成。超时也要出图（至少能看场景本身）。
            float deadline = Time.unscaledTime + ReadyTimeoutSeconds;
            var controller = FindInRoots<Battle.BattleController>();
            while (Time.unscaledTime < deadline)
            {
                if (controller != null && controller.AllPirates != null && controller.AllPirates.Count > 0)
                    break;
                yield return null;
                if (controller == null)
                    controller = FindInRoots<Battle.BattleController>();
            }

            // 等水体/活物/后处理热身几秒（水模拟需要若干步才有涟漪层次）。
            yield return new WaitForSeconds(3f);

            CollectSceneRefs();
            Directory.CreateDirectory(_outDir);

            foreach (Shot shot in BuildShots())
            {
                SetupCamera(shot);
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();

                string path = Path.Combine(_outDir, shot.Name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                // CaptureScreenshot 异步落盘：等文件出现再切下一机位。
                float waitDeadline = Time.unscaledTime + 10f;
                while (!File.Exists(path) && Time.unscaledTime < waitDeadline)
                    yield return null;
                yield return new WaitForSeconds(0.2f);
            }

            Debug.Log("[PlayerArtCapture] 采集完成，退出。目录：" + _outDir);
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        void CollectSceneRefs()
        {
            _hud = FindInRoots<Canvas>();
            Transform teamRoot = FindRootByName("Team0_Red");
            if (teamRoot != null && teamRoot.childCount > 0)
                _focusUnit = teamRoot.GetChild(0);
        }

        Shot[] BuildShots()
        {
            // 以场景 Ground 的包围盒推焦点与构图（level_1 ≈ 50×17 单位，中心约 (25,0,8.5)）。
            var ground = FindRootByName("Ground");
            Bounds bounds = ground != null && ground.GetComponent<Renderer>() != null
                ? ground.GetComponent<Renderer>().bounds
                : new Bounds(new Vector3(25f, 0f, 8.5f), new Vector3(50f, 1f, 17f));
            Vector3 c = bounds.center;

            var shots = new System.Collections.Generic.List<Shot>
            {
                NewShot("battle-45", true, c + new Vector3(0f, 12.73f, 12.73f), 60f, c),
                NewShot("arena-overview", false, c + new Vector3(0f, 34f, 22f), 55f, c),
                NewShot("side-profile", false, c + new Vector3(-30f, 8f, 10f), 55f, c),
                NewShot("sea-shore", false, new Vector3(6f, 2.2f, 20f), 60f, new Vector3(2f, -0.2f, 8f)),
                NewShot("terrain-high", false, c + new Vector3(-14f, 10f, 16f), 55f, new Vector3(30f, 1.5f, 4f)),
                NewShot("hud-fullscreen", true, c + new Vector3(0f, 12.73f, 12.73f), 60f, c),
            };

            if (_focusUnit != null)
            {
                Vector3 u = _focusUnit.position;
                shots.Add(NewShot("unit-closeup", false,
                    u + new Vector3(1.1f, 0.9f, 1.4f), 45f, u + new Vector3(0f, 0.25f, 0f)));
            }
            return shots.ToArray();
        }

        static Shot NewShot(string name, bool hud, Vector3 pos, float fov, Vector3 lookTarget)
        {
            return new Shot
            {
                Name = name,
                HudVisible = hud,
                Position = pos,
                Rotation = Quaternion.LookRotation(lookTarget - pos, Vector3.up),
                Fov = fov,
            };
        }

        void SetupCamera(Shot shot)
        {
            if (_camera == null)
            {
                var go = new GameObject("[ArtReviewCamera]");
                go.AddComponent<AudioListener>(); // 独立场景里可能没有监听器；多余监听器仅影响音频，不影响画面
                _camera = go.AddComponent<Camera>();
            }
            // 让评审相机接管渲染：禁用主相机（含 Cinemachine Brain）。
            Camera main = Camera.main;
            if (main != null && main != _camera)
                main.enabled = false;
            var brain = FindInRoots<Cinemachine.CinemachineBrain>();
            if (brain != null)
                brain.enabled = false;

            _camera.transform.position = shot.Position;
            _camera.transform.rotation = shot.Rotation;
            _camera.fieldOfView = shot.Fov;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 300f;
            _camera.enabled = true;

            if (_hud != null)
                _hud.enabled = shot.HudVisible;
        }

        static T FindInRoots<T>() where T : Component
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }
            return null;
        }

        static Transform FindRootByName(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name)
                    return root.transform;
                foreach (Transform child in root.transform)
                {
                    if (child.name == name)
                        return child;
                }
            }
            return null;
        }
    }
}
