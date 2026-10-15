using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PirateCrew.WaterLab;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// WaterLab 诊断探针（无头）：进 Play 等 30 帧，读回模拟状态——
    /// 高度纹理采样值、水面 renderer（bounds/材质/着色器）、模拟组件状态。
    /// 用法：-executeMethod PirateCrew.EditorTools.WaterLabProbe.Run（须带图形）。
    /// </summary>
    public static class WaterLabProbe
    {
        static int _frames;
        const int WaitFrames = 30;
        const string KeyArmed = "WaterLabProbe.Armed";

        // 进 Play 的域重载会洗掉 update 订阅——照 BattleHudScreenshot 的 SessionState 重挂模式：
        // 静态构造在重载后的新域里发现 Armed 位，重新挂 Tick。
        static WaterLabProbe()
        {
            if (SessionState.GetBool(KeyArmed, false))
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            if (SessionState.GetBool(KeyArmed, false))
                return; // 域重载后 executeMethod 不再是入口，静态构造已接管
            SessionState.SetBool(KeyArmed, true);
            Screen.SetResolution(960, 540, FullScreenMode.Windowed);
            EditorSceneManager.OpenScene("Assets/Scenes/Lab/WaterLab.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            _frames++;
            if (_frames < WaitFrames)
                return;
            EditorApplication.update -= Tick;

            try
            {
                var sim = Object.FindFirstObjectByType<WaterLabSimulation>();
                Debug.Log($"[Probe] sim={(sim != null)} enabled={(sim != null && sim.enabled)} time={(sim != null ? sim.SimTime : -1f)}");

                var renderer = GameObject.Find("WaterSurface")?.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    var b = renderer.bounds;
                    Debug.Log($"[Probe] water renderer: enabled={renderer.enabled} shader={renderer.sharedMaterial?.shader?.name} "
                        + $"bounds=({b.center.x:F1},{b.center.y:F1},{b.center.z:F1}) size=({b.size.x:F1},{b.size.y:F1},{b.size.z:F1})");
                }
                else
                    Debug.Log("[Probe] water renderer NOT FOUND");

                // 读回高度纹理 4 个角 + 中心（经临时 RT blit 到 Texture2D）
                var texField = typeof(WaterLabSimulation).GetField("_heightTex", BindingFlags.NonPublic | BindingFlags.Instance);
                var rt = texField?.GetValue(sim) as RenderTexture;
                if (rt != null)
                {
                    Debug.Log($"[Probe] heightTex {rt.width}x{rt.height} fmt={rt.format} isCreated={rt.IsCreated()}");
                    var prev = RenderTexture.active;
                    var tmp = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat);
                    Graphics.Blit(rt, tmp);
                    RenderTexture.active = tmp;
                    var reader = new Texture2D(8, 8, TextureFormat.RGBAFloat, false);
                    reader.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                    reader.Apply();
                    var px = reader.GetPixel(1, 1);
                    var pc = reader.GetPixel(6, 6);
                    Debug.Log($"[Probe] heightTex 中心(1,1)=({px.r:F3},{px.g:F3},{px.b:F3},{px.a:F3}) (6,6)=({pc.r:F3},{pc.g:F3},{pc.b:F3},{pc.a:F3})");
                    Object.DestroyImmediate(reader);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(tmp);
                }
                else
                    Debug.Log("[Probe] heightTex 字段取不到");

                var wetField = typeof(WaterLabSimulation).GetField("_wetTex", BindingFlags.NonPublic | BindingFlags.Instance);
                var wt = wetField?.GetValue(sim) as RenderTexture;
                if (wt != null)
                {
                    var prev = RenderTexture.active;
                    var tmp = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGBFloat);
                    Graphics.Blit(wt, tmp);
                    RenderTexture.active = tmp;
                    var reader = new Texture2D(8, 8, TextureFormat.RGBAFloat, false);
                    reader.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                    reader.Apply();
                    var px = reader.GetPixel(4, 4);
                    Debug.Log($"[Probe] wetTex (4,4)=({px.r:F3})");
                    Object.DestroyImmediate(reader);
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(tmp);
                }
            }
            finally
            {
                SessionState.SetBool(KeyArmed, false);
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            }
        }
    }
}
