using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.ArtPipeline.Tests
{
    /// <summary>
    /// Beveled Pixel UI 九宫格的 EditMode 门禁用例。
    ///
    /// 【这组用例判什么】三条，全部是**不看"像不像"的数字判据**：
    ///   ① 契约：<see cref="BuilderTypeName"/> 的目标表列出的每一张资产都在盘上，
    ///      尺寸 / 九宫格切片边框 / Sprite 导入设置（含像素那五项）全部合规；
    ///   ② 几何：**直接读盘上 PNG**（不经生成器），中轴色带边界必须落在基本单位 2px 的整数倍上、
    ///      透明像素只允许出现在四个切角、"光永远来自左上"必须量得出来（按压态反向）；
    ///   ③ 自洽：生成器自己的判据（<c>Verify()</c>）必须空——它与 ①② 是**两套独立实现**，
    ///      两边都过才算数（一边过另一边不过，说明真实缺陷在"两边判据的差集"里）。
    ///
    /// 【为什么这个文件在 Assets/Art/Tests/ 而不是 Assets/Tests/】同
    /// <see cref="PixelArtTextureImportTests"/> 的理由：无头验证台的 All 域会编译
    /// <c>Assets/Tests/**</c> 且不引用 UnityEditor，本用例必须用 <c>AssetDatabase</c> /
    /// <c>TextureImporter</c> 才谈得上"断言落盘资产"，放进 Assets/Tests/ 会把主控的
    /// 1130 条门禁打成编译错误。
    ///
    /// 【为什么要反射】生成器在 Assets/Editor（预定义程序集 Assembly-CSharp-Editor），
    /// asmdef 汇编不能引用预定义程序集。用装配件限定名取类型，既保住"目标表只有一份"，
    /// 又让"生成器被删/改名/挪走"变成一条明确的红灯而不是静默跳过。
    ///
    /// 【跑法】
    ///   Unity: Test Runner → EditMode → PirateCrew.ArtPipelineTests
    ///   无头: "&lt;Unity&gt;" -batchmode -nographics -projectPath &lt;工程&gt; -runTests
    ///         -testPlatform EditMode -testFilter PirateCrew.ArtPipeline.Tests -logFile -
    /// </summary>
    [TestFixture]
    public class BeveledPixelSkinTests
    {
        const string BuilderTypeName =
            "PirateCrew.EditorTools.BeveledPixelSpriteBuilder, Assembly-CSharp-Editor";

        /// <summary>基本单位（px）。判据"色带边界必须落在偶数行"里的那个 2。</summary>
        const int Unit = 2;

        static Type BuilderType()
        {
            Type type = Type.GetType(BuilderTypeName);
            Assert.That(type, Is.Not.Null,
                "找不到 BeveledPixelSpriteBuilder（" + BuilderTypeName + "）。生成器被删/改名/挪出 "
                + "Assets/Editor/BeveledPixelSpriteBuilder.cs 了？九宫格目标表是唯一的契约来源。");
            return type;
        }

        static object InvokeStatic(string method, params object[] args)
        {
            MethodInfo info = BuilderType().GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.That(info, Is.Not.Null, "BeveledPixelSpriteBuilder." + method + " 不见了");
            return info.Invoke(null, args);
        }

        /// <summary>一条烘焙目标的可反射快照（生成器里的 BakeTarget 是普通类 + public 字段）。</summary>
        struct Target
        {
            public string name, assetPath;
            public int width, height;
            public Vector4 border;
            public bool isFill;
        }

        static List<Target> ReadTargets()
        {
            Array raw = (Array)InvokeStatic("Targets");
            Assert.That(raw, Is.Not.Null.And.Not.Empty, "生成器的 Targets() 是空的——一张贴图都没打算烘？");

            var list = new List<Target>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                object item = raw.GetValue(i);
                Type t = item.GetType();
                list.Add(new Target
                {
                    name = (string)t.GetField("name").GetValue(item),
                    assetPath = (string)t.GetField("assetPath").GetValue(item),
                    width = (int)t.GetField("width").GetValue(item),
                    height = (int)t.GetField("height").GetValue(item),
                    border = (Vector4)t.GetField("border").GetValue(item),
                    isFill = (bool)t.GetField("isFill").GetValue(item),
                });
            }
            return list;
        }

        // ------------------------------------------------------------------
        // ① 契约：资产在、尺寸对、切片对、导入设置对
        // ------------------------------------------------------------------

        [Test]
        public void Targets_AllBaked_WithExpectedSizeAndSliceBorder()
        {
            List<Target> targets = ReadTargets();
            var problems = new List<string>();

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(t.assetPath);
                if (sprite == null)
                {
                    problems.Add(t.name + "：盘上读不到 Sprite（" + t.assetPath + "）——没烘焙，"
                        + "或者被导入成 Default 贴图（Default 没有 sprite/border，九宫格会静默失效）。");
                    continue;
                }

                if (sprite.rect.width != t.width || sprite.rect.height != t.height)
                {
                    problems.Add(t.name + "：像素尺寸 " + sprite.rect.width + "×" + sprite.rect.height
                        + "，目标表要求 " + t.width + "×" + t.height + "。");
                }
                if (sprite.border != t.border)
                {
                    problems.Add(t.name + "：切片边框 " + sprite.border + "，目标表要求 " + t.border
                        + "——边框错 = 贴图画对了但拉伸时会切进内容。");
                }
                if (!Mathf.Approximately(sprite.pixelsPerUnit, 100f))
                    problems.Add(t.name + "：pixelsPerUnit=" + sprite.pixelsPerUnit + "（应为 100）。");
                if (sprite.texture.filterMode != FilterMode.Point)
                {
                    problems.Add(t.name + "：filterMode=" + sprite.texture.filterMode
                        + "（应为 Point —— 双线性会把板内色插成板外色）。");
                }
                if (t.border.x + t.border.z >= t.width || t.border.y + t.border.w >= t.height)
                {
                    problems.Add(t.name + "：切片边框在某一轴上占满整张贴图（中心区为空，九宫格失效）。");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        [Test]
        public void Targets_ImportSettings_MatchPixelSpriteRules()
        {
            // 规矩的真源是 PixelArtTextureRules.ApplySprite/CheckSprite（在 Assets/Editor，
            // 故反射取）。同一条判据也被生成器与校验器用——三处一份。
            Type rules = Type.GetType("PirateCrew.EditorTools.Art.PixelArtTextureRules, Assembly-CSharp-Editor");
            Assert.That(rules, Is.Not.Null, "找不到 PixelArtTextureRules —— 像素导入规范的唯一真源不见了");
            MethodInfo check = rules.GetMethod("CheckSprite", BindingFlags.Public | BindingFlags.Static);
            Assert.That(check, Is.Not.Null, "PixelArtTextureRules.CheckSprite 不见了");

            var problems = new List<string>();
            List<Target> targets = ReadTargets();
            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                var importer = AssetImporter.GetAtPath(t.assetPath) as TextureImporter;
                if (importer == null)
                {
                    problems.Add(t.name + "：读不到 TextureImporter（资产不存在？）");
                    continue;
                }
                var result = (List<string>)check.Invoke(null, new object[] { importer, t.border });
                for (int k = 0; k < result.Count; k++)
                    problems.Add(t.name + "：" + result[k]);
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        // ------------------------------------------------------------------
        // ② 几何：直接读盘上 PNG 量（不经生成器）
        // ------------------------------------------------------------------

        [Test]
        public void BakedPng_BandsLandOnUnitMultiples_AndOnlyChamfersAreTransparent()
        {
            var problems = new List<string>();
            List<Target> targets = ReadTargets();

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                Color32[] px;
                int w, h;
                if (!TryLoadDisk(t, out px, out w, out h))
                {
                    problems.Add(t.name + "：盘上 PNG 读不出来或不存在。");
                    continue;
                }

                // 竖切（中列）/ 横切（中行）：每段的带宽必须是 Unit 的整数倍
                var column = new Color32[h];
                for (int y = 0; y < h; y++)
                    column[y] = px[y * w + w / 2];
                var row = new Color32[w];
                for (int x = 0; x < w; x++)
                    row[x] = px[(h / 2) * w + x];

                problems.AddRange(BandProblems(t.name + " 竖切", column));
                problems.AddRange(BandProblems(t.name + " 横切", row));

                problems.AddRange(CornerProblems(t.name, px, w, h, t.isFill));
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        /// <summary>
        /// 切角判据：**独立复算**一遍切角形状（不调生成器的判定，只借它的深度常量），
        /// 逐像素比对实际 alpha 通道。
        ///
        /// 判据 = <c>floor(dx/u) + floor(dy/u) &lt; 深度</c>——到两边界的距离**量化到基本单位**再相加，
        /// 切掉的永远是整格。这条判据是走查改出来的：第一版按像素判（<c>dx+dy&lt;2</c>），
        /// 得到 2px→1px→0px 的**离格锯齿**，在 2px 体系里既不平也不斜，创始人一眼看出"角没处理好"。
        /// 参照的角按格切（实测阶梯 4px→2px→0），故判据必须落在格上。
        /// </summary>
        static List<string> CornerProblems(string label, Color32[] px, int w, int h, bool isFill)
        {
            var problems = new List<string>();
            FieldInfo depthField = BuilderType().GetField("ChamferDepthUnits",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(depthField, Is.Not.Null, "ChamferDepthUnits 常量不见了（切角深度必须由生成器定义）");
            int depth = (int)depthField.GetRawConstantValue();
            if (isFill)
                depth = 0;                       // 填充件不切角（尾端要硬边）

            var expected = new bool[px.Length];
            for (int y = 0; y < h; y++)
            {
                int dy = Mathf.Min(y, h - 1 - y) / Unit;
                for (int x = 0; x < w; x++)
                {
                    int dx = Mathf.Min(x, w - 1 - x) / Unit;
                    expected[y * w + x] = depth > 0 && dx + dy < depth;
                }
            }

            int mismatches = 0;
            int firstBad = -1;
            for (int i = 0; i < px.Length; i++)
            {
                bool transparent = px[i].a == 0;
                if (transparent != expected[i])
                {
                    mismatches++;
                    if (firstBad < 0)
                        firstBad = i;
                }
            }
            if (mismatches > 0)
            {
                problems.Add(label + "：切角形状与判据不符，" + mismatches + " 个像素不对"
                    + "（首个 #" + firstBad + " 左上 (" + (firstBad % w) + "," + (firstBad / w)
                    + ") 实际 alpha=" + px[firstBad].a + "，判据要求"
                    + (expected[firstBad] ? "透明" : "不透明")
                    + "）——切角必须按 " + Unit + "px 整格切（深度 " + depth + " 格），"
                    + "按像素切会出离格锯齿。");
            }
            return problems;
        }

        [Test]
        public void BakedPng_OuterRingIsOneClosedLoop()
        {
            // 三层带是**同心环**，所以最外那一圈必须是一条闭合圈：8 连通 1 个分量、没有端点。
            // 这条是创始人走查（"为什么 4 角还是不是连着的"）的机器化——第一版把三层带画成
            // "上下横贯 + 左右补边"，水平带的斜面带顶到左右外缘，把两侧外环截断，四角就不闭合。
            var problems = new List<string>();
            List<Target> targets = ReadTargets();

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                Color32[] px;
                int w, h;
                if (!TryLoadDisk(t, out px, out w, out h))
                    continue;

                int count = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a == 0)
                            continue;
                        // 到四条边界的距离取较小者，量化到 Unit：0 = 最外那一圈
                        int dx = Mathf.Min(x, w - 1 - x) / Unit;
                        int dy = Mathf.Min(y, h - 1 - y) / Unit;
                        if (Mathf.Min(dx, dy) != 0)
                            continue;
                        count++;
                        RingSeen[y * w + x] = true;
                    }
                }

                int components = 0;
                for (int k = 0; k < px.Length; k++)
                {
                    if (!RingSeen[k] || RingVisited[k])
                        continue;
                    components++;
                    FloodRing(k, w, h);
                }

                int ends = 0;
                for (int k = 0; k < px.Length; k++)
                {
                    if (!RingSeen[k])
                        continue;
                    int cx = k % w, cy = k / w, neighbours = 0;
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        for (int oy = -1; oy <= 1; oy++)
                        {
                            if (ox == 0 && oy == 0)
                                continue;
                            int nx = cx + ox, ny = cy + oy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                continue;
                            if (RingSeen[ny * w + nx])
                                neighbours++;
                        }
                    }
                    if (neighbours < 2)
                        ends++;
                }

                if (components != 1 || ends > 0)
                {
                    problems.Add(t.name + "：外环 " + count + " 像素 / " + components
                        + " 段 / " + ends + " 个端点（应为 1 段闭合圈、0 端点）——四角又断开了。");
                }
                Array.Clear(RingSeen, 0, RingSeen.Length);
                Array.Clear(RingVisited, 0, RingVisited.Length);
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        // 复用的大缓冲（贴图最大 32×32；每条用例内先清空）
        static readonly bool[] RingSeen = new bool[64 * 64];
        static readonly bool[] RingVisited = new bool[64 * 64];

        static void FloodRing(int start, int w, int h)
        {
            var stack = new Stack<int>();
            stack.Push(start);
            RingVisited[start] = true;
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                int cx = cur % w, cy = cur / w;
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        if (ox == 0 && oy == 0)
                            continue;
                        int nx = cx + ox, ny = cy + oy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int ni = ny * w + nx;
                        if (RingSeen[ni] && !RingVisited[ni])
                        {
                            RingVisited[ni] = true;
                            stack.Push(ni);
                        }
                    }
                }
            }
        }

        [Test]
        public void BakedPng_LightComesFromTopLeft_AndPressedInvertsIt()
        {
            var problems = new List<string>();
            List<Target> targets = ReadTargets();

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                Color32[] px;
                int w, h;
                if (!TryLoadDisk(t, out px, out w, out h))
                    continue;

                // 贴图 y=0 在下：上带取 y=h-1，下带取 y=0；左 x=0，右 x=w-1。
                float topRing = Lum(px[(h - 1) * w + w / 2]);
                float bottomRing = Lum(px[0 * w + w / 2]);
                float leftRing = Lum(px[(h / 2) * w + 0]);
                float rightRing = Lum(px[(h / 2) * w + w - 1]);

                if (t.isFill)
                {
                    // 填充条：上亮沿 > 主体 > 下暗沿（量中列，避开左右切边）
                    float body = Lum(px[(h / 2) * w + w / 2]);
                    float topBand = Lum(px[(h - 1) * w + w / 2]);
                    float bottomBand = Lum(px[0 * w + w / 2]);
                    if (!(bottomBand < body && body < topBand))
                    {
                        problems.Add(t.name + "：填充三档明度不是 上亮 > 主体 > 下暗（实测 "
                            + topBand.ToString("F1") + " / " + body.ToString("F1") + " / "
                            + bottomBand.ToString("F1") + "）。");
                    }
                    continue;
                }

                bool pressed = t.name.EndsWith("_Pressed", StringComparison.Ordinal);
                bool verticalOk = pressed ? topRing < bottomRing : topRing > bottomRing;
                bool horizontalOk = pressed ? leftRing < rightRing : leftRing > rightRing;
                if (!verticalOk)
                {
                    problems.Add(t.name + "：竖直方向明暗" + (pressed ? "没反向" : "不是上亮下暗")
                        + "（上 " + topRing.ToString("F1") + " / 下 " + bottomRing.ToString("F1")
                        + "）——" + (pressed ? "按压态应当高光下沉" : "光必须来自上方"));
                }
                if (!horizontalOk)
                {
                    problems.Add(t.name + "：水平方向明暗" + (pressed ? "没反向" : "不是左亮右暗")
                        + "（左 " + leftRing.ToString("F1") + " / 右 " + rightRing.ToString("F1")
                        + "）——" + (pressed ? "按压态应当高光右移" : "光必须来自左方"));
                }

                // 内暗线 = 同侧外环（参照表读出的那条恒等式：凹感的来源）。
                // 索引推导：贴图 y=0 在下，从顶往下逐层是 外环 d=0,1 → 斜面 d=2,3 → 内暗线 d=4,5，
                // 所以"上内暗线"取 y = h-1-(2×Unit)（**不是 h-3**，那一层是斜面——
                // 本用例第一版就栽在这个差一层上，red 过一轮）。
                int topRingIndex = (h - 1) * w + w / 2;
                int topLineIndex = (h - 1 - 2 * Unit) * w + w / 2;
                if (!Same(px[topLineIndex], px[topRingIndex]))
                {
                    problems.Add(t.name + "：上内暗线 " + Hex(px[topLineIndex])
                        + " 与上外环 " + Hex(px[topRingIndex])
                        + " 不同色——参照表里这两道是同一个色，改散了凹感就没了。");
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        // ------------------------------------------------------------------
        // ③ 生成器自己的判据（另一套实现）必须也空
        // ------------------------------------------------------------------

        [Test]
        public void GeneratorSelfCheck_ReportsNoProblems()
        {
            var problems = (List<string>)InvokeStatic("Verify");
            Assert.That(problems, Is.Empty,
                "生成器判据报了 " + problems.Count + " 条：\n" + string.Join("\n", problems.ToArray()));
        }

        [Test]
        public void PressOffset_IsOnePixelDownRight()
        {
            FieldInfo field = BuilderType().GetField("PressOffset", BindingFlags.Public | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "PressOffset 不见了（按压位移必须由生成器统一定义，别各处各写）");
            var offset = (Vector2)field.GetValue(null);
            Assert.That(offset, Is.EqualTo(new Vector2(1f, -1f)),
                "按压位移应当是右下 1px（UI 坐标 +x 右 / -y 下）。");
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------

        static bool TryLoadDisk(Target t, out Color32[] px, out int w, out int h)
        {
            px = null;
            w = h = 0;
            string abs = Path.Combine(Path.GetDirectoryName(Application.dataPath), t.assetPath);
            if (!File.Exists(abs))
                return false;
            var tex = new Texture2D(2, 2);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(abs)))
                    return false;
                px = tex.GetPixels32();
                w = tex.width;
                h = tex.height;
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        static List<string> BandProblems(string label, Color32[] axis)
        {
            var problems = new List<string>();
            int start = 0;
            for (int i = 1; i <= axis.Length; i++)
            {
                if (i < axis.Length && Same(axis[i], axis[start]))
                    continue;
                int band = i - start;
                if (band % Unit != 0)
                {
                    problems.Add(label + "：" + start + ".." + (i - 1) + " 带宽 " + band
                        + "px 不是 " + Unit + " 的整数倍（该带色 " + Hex(axis[start])
                        + "）——色带边界必须落在偶数行。");
                }
                start = i;
            }
            return problems;
        }

        static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static float Lum(Color32 c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        static string Hex(Color32 c)
        {
            return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
        }
    }
}
