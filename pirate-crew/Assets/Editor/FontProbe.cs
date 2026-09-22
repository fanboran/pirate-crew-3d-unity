using System;
using System.IO;
using UnityEngine.TextCore;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TextCore.LowLevel;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// **字体排障探针**（临时工具）：同一串字用 4 种字体资产配置各渲染一行，
    /// 一张截图里横向对比，找出"字形重影"到底出在哪个配置上。
    /// 行 1 = 烘好的 Resources/FusionPixel12-px（RASTER_HINTED + 12 采样）；
    /// 行 2 = 现场建 RASTER + 12；行 3 = 现场建 RASTER + 24（2 倍采样，显示 72）；
    /// 行 4 = 现场建 SDFAA + 64（参照：SDF 路径是否正常）。
    /// 命令行：-executeMethod ...FontProbe.CaptureFromCommandLine -uiShowcaseOut &lt;目录&gt;
    /// </summary>
    public static class FontProbe
    {
        internal const string Sample = "面板 Beveled 12px = 3× 屏幕像素";

        public static void CaptureFromCommandLine()
        {
            string dir = "export/ui-pixel-4a";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-uiShowcaseOut")
                    dir = args[i + 1];
            }
            string path = Path.Combine(dir, "font-probe.png");

            EditorApplicationIsReady.Go(path);
        }

        /// <summary>
        /// 编辑器内**度量转储**（命令桥用）：TextCore 原生调用在编辑器进程里可用，
        /// 不进 Play、不截图，把各配置下 TextCore 给的字形度量直接写回结果文件——
        /// 乱码（相邻字形互相叠）到底出在哪一档配置，看数字就知道。
        /// </summary>
        public static void DumpMetrics()
        {
            var lines = new System.Collections.Generic.List<string>();
            const string Ttf = "Assets/Art/Fonts/FusionPixel12-zh_hans.ttf";
            FontEngine.InitializeFontEngine();
            foreach (var (size, hinted) in new[] { (12, false), (12, true), (24, false), (64, false) })
            {
                var err = FontEngine.LoadFontFace(Ttf, size);
                FaceInfo face = FontEngine.GetFaceInfo();
                lines.Add($"size={size} hinted={hinted} err={err} pointSize={face.pointSize} lineHeight={face.lineHeight:F1}");
                foreach (char c in new[] { '面', '板', 'B' })
                {
                    foreach (GlyphLoadFlags flag in Enum.GetValues(typeof(GlyphLoadFlags)))
                    {
                        if (!flag.ToString().Contains("RENDER"))
                            continue;
                        Glyph g;
                        bool ok = FontEngine.TryGetGlyphWithUnicodeValue(c, flag, out g);
                        lines.Add($"  [{flag}] '{c}' ok={ok} w={g.metrics.width:F1} h={g.metrics.height:F1} adv={g.metrics.horizontalAdvance:F1} bearX={g.metrics.horizontalBearingX:F1} rect=({g.glyphRect.x},{g.glyphRect.y},{g.glyphRect.width},{g.glyphRect.height})");
                    }
                }
            }
            File.AppendAllLines(Path.GetFullPath("../export/unity-command-result.txt"), lines);
        }
    }

    /// <summary>探针的播放期状态机（与 UiShowcaseSceneSetup 同套路：进 Play → 等帧 → 截图 → 退）。</summary>
    static class EditorApplicationIsReady
    {
        static string _path;
        static int _state;
        static int _wait;

        public static void Go(string path)
        {
            _path = path;
            EditorApplication.update += Step;
        }

        static void Step()
        {
            switch (_state)
            {
                case 0:
                    EditorApplication.isPlaying = true;
                    _state = 1;
                    break;
                case 1:
                    if (EditorApplication.isPlaying && Time.frameCount == 5)
                        Build();
                    if (EditorApplication.isPlaying && Time.frameCount > 120)
                    {
                        ScreenCapture.CaptureScreenshot(_path);
                        _state = 2;
                        _wait = 0;
                    }
                    break;
                case 2:
                    if (++_wait > 20)
                    {
                        EditorApplication.isPlaying = false;
                        _state = 3;
                        _wait = 0;
                    }
                    break;
                case 3:
                    if (!EditorApplication.isPlaying && ++_wait > 5)
                    {
                        EditorApplication.update = null;
                        EditorApplication.Exit(File.Exists(_path) ? 0 : 1);
                    }
                    break;
            }
        }

        static void Build()
        {
            var go = new GameObject("ProbeCanvas", typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            Font source = Resources.Load<Font>("Fonts/FusionPixel12-zh_hans Source");
            if (source == null)
            {
                // Resources 里没有源 ttf（只在 Art/Fonts），直接从 Art 路径加载（编辑器内可用）。
                source = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(
                    "Assets/Art/Fonts/FusionPixel12-zh_hans.ttf");
            }

            string[] names = { "RASTER+12", "RASTER_HINTED+12", "RASTER+24", "SDFAA+64" };
            TMP_FontAsset[] assets =
            {
                Make(source, 12, GlyphRenderMode.RASTER),
                Make(source, 12, GlyphRenderMode.RASTER_HINTED),
                Make(source, 24, GlyphRenderMode.RASTER),
                Make(source, 64, GlyphRenderMode.SDFAA),
            };

            for (int i = 0; i < assets.Length; i++)
            {
                TextMeshProUGUI label = NewText("Row" + i, go.transform, names[i] + "  " + FontProbe.Sample,
                    assets[i], i % 2 == 0 ? 36 : 72);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 1f);
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.anchoredPosition = new Vector2(40f, -60f - i * 140f);
                label.rectTransform.sizeDelta = new Vector2(1800f, 120f);
            }

            // 行 0 处再放烘好的 Resources 资产（RASTER_HINTED+12 的入库版）
            TMP_FontAsset baked = Resources.Load<TMP_FontAsset>("Fonts/FusionPixel12-px");
            if (baked != null)
            {
                TextMeshProUGUI label = NewText("Baked", go.transform, "BAKED(HINTED12)  " + FontProbe.Sample, baked, 36);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 1f);
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.anchoredPosition = new Vector2(40f, -620f);
                label.rectTransform.sizeDelta = new Vector2(1800f, 120f);
            }
        }

        static TMP_FontAsset Make(Font source, int size, GlyphRenderMode mode)
        {
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(source, size, 0, mode, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (asset != null)
                asset.name = "Probe_" + mode + "+" + size;
            return asset;
        }

        static TextMeshProUGUI NewText(string name, Transform parent, string content,
            TMP_FontAsset font, int fontSize)
        {
            var rect = new GameObject(name, typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var text = rect.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.enableWordWrapping = false;
            return text;
        }

        /// <summary>
        /// **实机场景文本对象倒排**（命令桥用）：把活动场景里每个 TMP 文本的全路径 / 字体 /
        /// 字号 / 位置 / 尺寸写回结果文件——排查"同一行字出现两份"（乱码的真身若是
        /// 两个文本对象叠在一起，这里一眼可见；顺带印出字体图集的 filterMode 与已入库字形数）。
        /// </summary>
        public static void DumpSceneTexts() // v2 实机文本倒排
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
                        glyphCount = t.font.glyphTable.Count;
                    }
                    lines.Add($"  [{path}] font={t.font?.name} size={t.fontSize:F0} pos=({r.anchoredPosition.x:F0},{r.anchoredPosition.y:F0}) size=({r.sizeDelta.x:F0},{r.sizeDelta.y:F0}) text=\"{t.text.Substring(0, System.Math.Min(24, t.text.Length))}\" atlasFilter={filter} glyphs={glyphCount}");
                }
            }
            File.AppendAllLines(Path.GetFullPath("../export/unity-command-result.txt"), lines);
        }
    }
}