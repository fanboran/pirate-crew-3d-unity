using System.Collections;
using System.IO;
using PirateCrew.Battle;
using PirateCrew.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.ArtReview
{
    /// <summary>
    /// 【临时诊断工具】绕场环绕采集：进战斗后禁用跟随相机，绕场地中心 360°×3 层高度拍照，
    /// 用于复现创始人报告的「地面垂直」——正常出图通道的固定机位看不到的东西，环绕一圈就能看到。
    /// 用完即删，不入库口径。
    ///
    /// 用法：播放器 -orbitOut &lt;绝对目录&gt; -orbitLevel 4|5
    /// </summary>
    public static class ChemPlantOrbitCapture
    {
        [GameBootstrap(GameBootstrapPhase.Initialize, order: 150)]
        internal static void Install()
        {
            string outDir = GetArg("-orbitOut");
            int level = GetIntArg("-orbitLevel");
            if (string.IsNullOrEmpty(outDir) || level <= 0)
                return;

            outDir = Path.GetFullPath(outDir);
            ArtReview.ArtReviewCaptureOverride.LevelNumber = level;   // 复用官方关卡覆盖通道
            var go = new GameObject("[ChemPlantOrbitCapture]");
            go.AddComponent<OrbitRunner>().Setup(outDir, level);
            Object.DontDestroyOnLoad(go);
        }

        static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }
            return null;
        }

        static int GetIntArg(string name)
        {
            string v = GetArg(name);
            return v != null && int.TryParse(v, out int n) ? n : 0;
        }

        sealed class OrbitRunner : MonoBehaviour
        {
            string _outDir;
            int _level;

            public void Setup(string outDir, int level)
            {
                _outDir = outDir;
                _level = level;
            }

            IEnumerator Start()
            {
                // 等 Bootstrapper 引导完成（它会异步加载主菜单并顶掉抢先加载的场景），再切 Battle。
                float bootDeadline = Time.unscaledTime + 15f;
                while (SceneManager.GetActiveScene().name != "MainMenu" && Time.unscaledTime < bootDeadline)
                    yield return null;
                yield return new WaitForSeconds(0.5f);
                SceneManager.LoadScene("Battle", LoadSceneMode.Single);
                yield return new WaitForSeconds(3f);   // 等战斗装配 + 单位生成

                var driver = Object.FindObjectOfType<BattleCameraDriver>();
                if (driver != null)
                    driver.enabled = false;            // 停掉跟随，相机归我

                yield return null;

                Vector3 center = ResolveCenter();
                Debug.Log("[Orbit] level=" + _level + " center=" + center.ToString("0.0"));

                cam = Camera.main;
                if (cam == null)
                {
                    Debug.LogError("[Orbit] 找不到主相机");
                    yield break;
                }

                Directory.CreateDirectory(_outDir);
                float[] heights = { 14f, 34f, 70f };
                float radius = 42f;
                int shot = 0;
                foreach (float h in heights)
                {
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i * 30f * Mathf.Deg2Rad;
                        Vector3 pos = center + new Vector3(Mathf.Cos(a) * radius, h, Mathf.Sin(a) * radius);
                        cam.transform.position = pos;
                        cam.transform.LookAt(center + Vector3.up * 4f);
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(Path.Combine(_outDir,
                            "L" + _level + "-h" + (int)h + "-a" + (i * 30) + ".png"));
                        shot++;
                        yield return new WaitForSeconds(0.15f);
                    }
                }

                Debug.Log("[Orbit] 完成 " + shot + " 张");
                yield return new WaitForSeconds(1f);
                Application.Quit();
            }

            Camera cam;

            Vector3 ResolveCenter()
            {
                // 优先：陈设件实例（化工厂整场件）的包围盒中心
                foreach (var name in new[] { "ChemPlantTeamYard", "ChemPlantYard" })
                {
                    var go = GameObject.Find(name);
                    if (go == null) continue;
                    var renderers = go.GetComponentsInChildren<MeshRenderer>();
                    if (renderers.Length == 0) continue;
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                    Debug.Log("[Orbit] 陈设件 " + name + " bounds=" + b.size.ToString("0.0")
                        + " center=" + b.center.ToString("0.0"));
                    Transform t = go.transform;
                    while (t != null)
                    {
                        Debug.Log("[Orbit]   链 " + t.name + " localRot=" + t.localRotation.eulerAngles.ToString("0.0")
                            + " worldRot=" + t.rotation.eulerAngles.ToString("0.0")
                            + " localPos=" + t.localPosition.ToString("0.0")
                            + " lossyScale=" + t.lossyScale.ToString("0.00"));
                        t = t.parent;
                    }
                    return new Vector3(b.center.x, 0f, b.center.z);
                }
                // 兜底：地形碰撞盒
                var terrain = Object.FindObjectOfType<BattleTerrainView>();
                if (terrain != null)
                {
                    var col = terrain.GetComponentInChildren<Collider>();
                    if (col != null)
                        return new Vector3(col.bounds.center.x, 0f, col.bounds.center.z);
                }
                return new Vector3(25f, 0f, 8.5f);
            }
        }
    }
}
