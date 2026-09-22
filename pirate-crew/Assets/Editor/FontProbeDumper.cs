using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **实机文本倒排**（命令桥用，独立成文件）：把活动场景里每个 TMP 文本的全路径 / 字体 /
    /// 字号 / 位置 / 尺寸 / 文字内容写回结果文件——排查乱码（相邻字形互相叠）时
    /// "是否两个文本对象叠在一起、图集过滤模式、字形入库数"在这里一眼可见。
    /// （独立文件的原因：Bee 编译缓存把 FontProbe.cs 的产物卡在旧版，新文件=新输入键=必然重编。）
    /// </summary>
    public static class FontProbeDumper
    {
        public static void DumpSceneTexts()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                lines.Add($"Canvas {canvas.name} order={canvas.sortingOrder} scale={canvas.scaleFactor:F3}");
                foreach (var t in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    string path = t.name;
                    var p = t.transform.parent;
                    while (p != null && p != canvas.transform) { path = p.name + "/" + path; p = p.parent; }
                    var r = t.rectTransform;
                    int glyphCount = 0;
                    string filter = "n/a";
                    if (t.font != null && t.font.atlasTextures != null && t.font.atlasTextures.Length > 0 && t.font.atlasTextures[0] != null)
                    {
                        filter = t.font.atlasTextures[0].filterMode.ToString();
                        glyphCount = t.font.glyphTable?.Count ?? 0;
                    }
                    lines.Add($"  [{path}] font={t.font?.name} size={t.fontSize:F0} pos=({r.anchoredPosition.x:F0},{r.anchoredPosition.y:F0}) size=({r.sizeDelta.x:F0},{r.sizeDelta.y:F0}) text=\"{t.text.Substring(0, System.Math.Min(24, t.text.Length))}\" atlasFilter={filter} glyphs={glyphCount}");
                }
            }
            File.AppendAllLines(Path.GetFullPath("../export/unity-command-result.txt"), lines);
        }
    }
}

namespace PirateCrew.EditorTools
{
    /// <summary>把像素字体资产当前的**运行时图集**导成 PNG（乱码诊断：图集里的字形本身
    /// 若已互相叠/损坏 → 是 TextCore 运行时栅格化的问题；图集干净 → 问题在 UV/材质侧）。</summary>
    public static class FontAtlasDumper
    {
        public static void Dump()
        {
            var font = Resources.Load<TMP_FontAsset>("Fonts/FusionPixel12-px");
            if (font == null || font.atlasTextures == null || font.atlasTextures.Length == 0)
            {
                File.AppendAllText(Path.GetFullPath("../export/unity-command-result.txt"),
                    "atlas dump: font/atlas missing\n");
                return;
            }
            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                Texture2D atlas = font.atlasTextures[i];
                // 图集可能不可读（运行时纹理）——经 RenderTexture 中转 ReadPixels 拿回 CPU 侧像素
                var rt = new RenderTexture(atlas.width, atlas.height, 0);
                Graphics.Blit(atlas, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
                readable.Apply();
                RenderTexture.active = null;
                // 图集是 Alpha8：字形覆盖度在 alpha 通道——把 alpha 抄进 RGB 才看得见
                var px = readable.GetPixels32();
                for (int k = 0; k < px.Length; k++)
                    px[k] = new Color32(px[k].a, px[k].a, px[k].a, 255);
                readable.SetPixels32(px);
                readable.Apply();
                var png = readable.EncodeToPNG();
                string path = Path.GetFullPath("../export/ui-pixel-4a/font-atlas-" + i + ".png");
                File.WriteAllBytes(path, png);
                File.AppendAllText(Path.GetFullPath("../export/unity-command-result.txt"),
                    $"atlas[{i}] {atlas.width}x{atlas.height} filter={atlas.filterMode} -> {path}\n");
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }
    }
}

namespace PirateCrew.EditorTools
{
    /// <summary>**编辑模式渲染探针**：不进 Play、不抢编辑器焦点——在编辑模式建一个
    /// ScreenSpaceCamera 画布 + 像素字体 TMP 行，用相机渲到 RenderTexture 落 PNG。
    /// 用于验证"图集 padding=4 重烘后，TMP 渲染是否还有乱码"（并行会话占用 Play 时的替代验证路）。</summary>
    public static class FontRenderProbe
    {
        public static void Run()
        {
            var lines = new System.Collections.Generic.List<string>();
            var font = Resources.Load<TMP_FontAsset>("Fonts/FusionPixel12-px");
            lines.Add("font=" + (font != null ? font.name : "MISSING"));
            if (font != null && font.atlasTextures != null && font.atlasTextures[0] != null)
                lines.Add($"atlas filter={font.atlasTextures[0].filterMode} size={font.atlasTextures[0].width}x{font.atlasTextures[0].height}");

            var go = new GameObject("FontRenderProbe", typeof(Camera), typeof(Canvas));
            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(20, 30, 40, 255);
            camera.cullingMask = 1 << 23;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            go.layer = 23;

            var textGo = new GameObject("ProbeText");
            textGo.layer = 23;
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 72;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.text = "Beveled Pixel 组件展示 12px = 3× 屏幕像素，确定 / 菜单 / 退出";
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40, -40);
            rt.sizeDelta = new Vector2(2400, 120);

            const int W = 1920, H = 300;
            var render = new RenderTexture(W, H, 0);
            camera.targetTexture = render;
            camera.aspect = (float)W / H;
            camera.Render();
            RenderTexture.active = render;
            var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;
            string path = Path.GetFullPath("../export/ui-pixel-4a/font-render-probe.png");
            File.WriteAllBytes(path, read.EncodeToPNG());
            lines.Add("render -> " + path);

            UnityEngine.Object.DestroyImmediate(textGo);
            UnityEngine.Object.DestroyImmediate(go);
            File.AppendAllLines(Path.GetFullPath("../export/unity-command-result.txt"), lines);
        }
    }
}
