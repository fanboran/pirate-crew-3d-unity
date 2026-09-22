using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PirateCrew.UI;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.ArtPipeline.Tests
{
    /// <summary>
    /// Beveled Pixel UI 九宫格的 EditMode 门禁用例。
    ///
    /// 【这组用例判什么】四条，全部是**不看"像不像"的数字判据**：
    ///   ① 契约：<see cref="BuilderTypeName"/> 的目标表列出的每一张资产都在盘上，
    ///      尺寸 / 九宫格切片边框 / Sprite 导入设置（含像素那五项）全部合规；
    ///   ② 几何：**直接读盘上 PNG**（不经生成器），中轴色带边界必须落在基本单位的整数倍上、
    ///      透明像素只允许出现在该画法族的已知形状里、"光永远来自左上"必须量得出来（按压态反向）；
    ///   ③ 自洽：生成器自己的判据（<c>Verify()</c>）必须空——它与 ①② 是**两套独立实现**，
    ///      两边都过才算数（一边过另一边不过，说明真实缺陷在"两边判据的差集"里）；
    ///   ④ 皮肤契约：运行时 <see cref="PixelSkin"/>/PixelSkinAsset 图集满格、取用不空
    ///      （装配侧唯一的取图口，空一格 = 某块 UI 白块）。
    ///
    /// 【为什么这个文件在 Assets/Art/Tests/ 而不是 Assets/Tests/】同
    /// <see cref="PixelArtTextureImportTests"/> 的理由：无头验证台的 All 域会编译
    /// <c>Assets/Tests/**</c> 且不引用 UnityEditor，本用例必须用 <c>AssetDatabase</c> /
    /// <c>TextureImporter</c> 才谈得上"断言落盘资产"，放进 Assets/Tests/ 会把主控的
    /// 门禁打成编译错误。
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

        /// <summary>
        /// 基本单位（px），真源 = 运行时 <see cref="PixelSkin.Unit"/>（u=3 与 3D 像素块 1:1 对齐）。
        /// 测试与生成器同用这一个值，"色带边界落在 u 的整数倍上"才有共同语言。
        /// </summary>
        static readonly int Unit = PixelSkin.Unit;

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
            public string name, assetPath, kind;
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
                    kind = (string)t.GetField("kind").GetValue(item),
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
                // "中心区为空"检查只对可双向拉伸的件成立：分隔线横竖各只有一条拉伸轴，
                // 交叉轴整条都是边（border 占满 = 有意为之），位点干脆不切片。
                if (t.kind != "sep" && t.kind != "pip"
                    && (t.border.x + t.border.z >= t.width || t.border.y + t.border.w >= t.height))
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
        public void BakedPng_BandsLandOnUnitMultiples_AndAlphaMatchesKindShape()
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

                problems.AddRange(AlphaShapeProblems(t, px, w, h));
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        /// <summary>
        /// 透明形状判据：**独立复算**每个画法族的已知形状（不调生成器的画法，只借深度常量），
        /// 逐像素比对 alpha。谁该透明谁不该，一张 mask 说清：
        ///   plate/shadow = 四角对称切角；tab = 只切上两角；ring = 1u 厚方环；
        ///   pip = u 格菱形；fill/sep = 全不透明。
        /// 切角判据 <c>floor(dx/u)+floor(dy/u) &lt; 深度</c>：距离量化到基本单位再相加，
        /// 切掉的永远是整格——按像素切会出离格锯齿（本类第一版踩过、创始人一眼看穿的坑）。
        /// </summary>
        static List<string> AlphaShapeProblems(Target t, Color32[] px, int w, int h)
        {
            var problems = new List<string>();
            FieldInfo depthField = BuilderType().GetField("ChamferDepthUnits",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(depthField, Is.Not.Null, "ChamferDepthUnits 常量不见了（切角深度必须由生成器定义）");
            int depth = (int)depthField.GetRawConstantValue();

            var expected = new bool[px.Length];        // true = 应不透明
            switch (t.kind)
            {
                case "fill":
                case "sep":
                    for (int i = 0; i < expected.Length; i++)
                        expected[i] = true;
                    break;
                case "tab":
                    for (int y = 0; y < h; y++)
                    {
                        int dyTop = (h - 1 - y) / Unit;
                        for (int x = 0; x < w; x++)
                        {
                            int dx = Mathf.Min(x, w - 1 - x) / Unit;
                            expected[y * w + x] = !(depth > 0 && dx + dyTop < depth);
                        }
                    }
                    break;
                case "ring":
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int dx = Mathf.Min(x, w - 1 - x);
                            int dy = Mathf.Min(y, h - 1 - y);
                            bool chamfer = depth > 0
                                && (dx / Unit) + (dy / Unit) < depth;
                            expected[y * w + x] = Mathf.Min(dx, dy) < 2 * Unit && !chamfer;   // 环厚 2u
                        }
                    }
                    break;
                case "pip":
                {
                    int cells = w / Unit;
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int cx = x / Unit, cy = y / Unit;
                            int a = Mathf.Abs(2 * cx - (cells - 1)) + Mathf.Abs(2 * cy - (cells - 1));
                            expected[y * w + x] = a <= cells - 2;
                        }
                    }
                    break;
                }
                default:   // plate / shadow：四角对称切角
                    for (int y = 0; y < h; y++)
                    {
                        int dy = Mathf.Min(y, h - 1 - y) / Unit;
                        for (int x = 0; x < w; x++)
                        {
                            int dx = Mathf.Min(x, w - 1 - x) / Unit;
                            expected[y * w + x] = !(depth > 0 && dx + dy < depth);
                        }
                    }
                    break;
            }

            int mismatches = 0;
            int firstBad = -1;
            for (int i = 0; i < px.Length; i++)
            {
                bool opaque = px[i].a == 255;
                if (opaque != expected[i])
                {
                    mismatches++;
                    if (firstBad < 0)
                        firstBad = i;
                }
            }
            if (mismatches > 0)
            {
                problems.Add(t.name + "：透明形状与该画法族（" + t.kind + "）的判据不符，" + mismatches
                    + " 个像素不对（首个 #" + firstBad + " 左上 (" + (firstBad % w) + "," + (firstBad / w)
                    + ") 实际 alpha=" + px[firstBad].a + "，判据要求"
                    + (expected[firstBad] ? "不透明" : "透明") + "）。");
            }
            return problems;
        }

        [Test]
        public void BakedPng_OuterRingIsOneClosedLoop()
        {
            // 三层带是**同心环**，所以最外那一圈必须是一条闭合圈：8 连通 1 个分量、没有端点。
            // 这条是创始人走查（"为什么 4 角还是不是连着的"）的机器化——第一版把三层带画成
            // "上下横贯 + 左右补边"，水平带的斜面带顶到左右外缘，把两侧外环截断，四角就不闭合。
            // 页签（tab）底边无带：判"环像素"时到下边界的距离不参与，环是一条 U（1 段、0 端点）；
            // fill/pip/sep 没有环结构，不查。
            var problems = new List<string>();
            List<Target> targets = ReadTargets();

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                if (t.kind != "plate" && t.kind != "tab" && t.kind != "ring" && t.kind != "shadow")
                    continue;
                Color32[] px;
                int w, h;
                if (!TryLoadDisk(t, out px, out w, out h))
                    continue;
                bool bottomOpen = t.kind == "tab";

                int count = 0;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a == 0)
                            continue;
                        // 到各边界的距离取较小者，量化到 Unit：0 = 最外那一圈
                        int dx = Mathf.Min(x, w - 1 - x) / Unit;
                        int dy = bottomOpen ? (h - 1 - y) / Unit : Mathf.Min(y, h - 1 - y) / Unit;
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

        // 复用的大缓冲（贴图最大 36×36；每条用例内先清空）
        static readonly bool[] RingSeen = new bool[96 * 96];
        static readonly bool[] RingVisited = new bool[96 * 96];

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

                // 填充条：上亮沿 > 主体 > 下暗沿（量中列，避开左右切边）
                if (t.isFill)
                {
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

                // 明暗方向判据只对"三层带浮雕"族成立；页签底边无带（跳过竖直比较），
                // 单语义件（环/位点/分隔线/投影）没有受光面结构，不查。
                if (t.kind != "plate" && t.kind != "tab")
                    continue;

                // 贴图 y=0 在下：上带取 y=h-1，下带取 y=0；左 x=0，右 x=w-1。
                float topRing = Lum(px[(h - 1) * w + w / 2]);
                float bottomRing = Lum(px[0 * w + w / 2]);
                float leftRing = Lum(px[(h / 2) * w + 0]);
                float rightRing = Lum(px[(h / 2) * w + w - 1]);

                bool pressed = t.name.EndsWith("_Pressed", StringComparison.Ordinal);
                bool horizontalOk = pressed ? leftRing < rightRing : leftRing > rightRing;
                if (!horizontalOk)
                {
                    problems.Add(t.name + "：水平方向明暗" + (pressed ? "没反向" : "不是左亮右暗")
                        + "（左 " + leftRing.ToString("F1") + " / 右 " + rightRing.ToString("F1")
                        + "）——" + (pressed ? "按压态应当高光右移" : "光必须来自左方"));
                }
                if (t.kind != "tab")
                {
                    bool verticalOk = pressed ? topRing < bottomRing : topRing > bottomRing;
                    if (!verticalOk)
                    {
                        problems.Add(t.name + "：竖直方向明暗" + (pressed ? "没反向" : "不是上亮下暗")
                            + "（上 " + topRing.ToString("F1") + " / 下 " + bottomRing.ToString("F1")
                            + "）——" + (pressed ? "按压态应当高光下沉" : "光必须来自上方"));
                    }
                }

                // 内暗线 = 同侧外环（参照表读出的那条恒等式：凹感的来源）。
                // 索引推导：贴图 y=0 在下，从顶往下逐层是 外环 d∈[0,u) → 斜面 d∈[u,2u) → 内暗线 d∈[2u,3u)，
                // 所以"上内暗线"取 y = h-1-(2×Unit)（**不是 h-1-u**，那一层是斜面——
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

        [Test]
        public void Tab_BottomEdgeIsFlat_NoBands()
        {
            // 页签的定义性特征：底边是平底（与宿主面板贴合）。独立判据 = 中列最下 3u 行同色。
            var problems = new List<string>();
            List<Target> targets = ReadTargets();

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                if (t.kind != "tab")
                    continue;
                Color32[] px;
                int w, h;
                if (!TryLoadDisk(t, out px, out w, out h))
                {
                    problems.Add(t.name + "：盘上 PNG 读不出来。");
                    continue;
                }
                Color32 bottom = px[0 * w + w / 2];
                for (int y = 1; y < 3 * Unit; y++)
                {
                    if (!Same(px[y * w + w / 2], bottom))
                    {
                        problems.Add(t.name + "：底边中列前 " + 3 * Unit + " 行不是同一色（"
                            + Hex(bottom) + " vs 第 " + y + " 行 " + Hex(px[y * w + w / 2])
                            + "）——页签底边必须无带，带了就是普通 Plate。");
                        break;
                    }
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems.ToArray()));
        }

        [Test]
        public void Singles_ShadowIsSingleColor_SepHasTwoColors_PipDiamond()
        {
            var problems = new List<string>();
            List<Target> targets = ReadTargets();
            Color32[] pipOff = null, pipOn = null;
            int pipW = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                Target t = targets[i];
                Color32[] px;
                int w, h;
                if (!TryLoadDisk(t, out px, out w, out h))
                    continue;

                if (t.kind == "shadow")
                {
                    // 投影 = 纯 INK 剪影：所有不透明像素同色（基线取首个**不透明**像素——
                    // 第 0 号像素是切角透明 (0,0,0,0)，拿它当基线会把整个剪影误判成杂色）。
                    int baseIdx = -1;
                    for (int k = 0; k < px.Length; k++)
                    {
                        if (px[k].a == 255) { baseIdx = k; break; }
                    }
                    Color32 c = px[Mathf.Max(baseIdx, 0)];
                    for (int k = 0; k < px.Length; k++)
                    {
                        if (px[k].a == 255 && !Same(px[k], c))
                        {
                            problems.Add(t.name + "：投影出现了第二种颜色（" + Hex(c) + " vs "
                                + Hex(px[k]) + "）——投影必须是单色剪影。");
                            break;
                        }
                    }
                }
                else if (t.kind == "sep")
                {
                    // 分隔线 = 恰好两色（暗 u 压亮 u）
                    var seen = new List<Color32>();
                    for (int k = 0; k < px.Length; k++)
                    {
                        if (px[k].a != 255 || seen.Exists(c => Same(c, px[k])))
                            continue;
                        seen.Add(px[k]);
                        if (seen.Count > 2)
                            break;
                    }
                    if (seen.Count != 2)
                        problems.Add(t.name + "：分隔线应恰好 2 色，实测 " + seen.Count + " 色。");
                }
                else if (t.kind == "pip")
                {
                    if (t.name == "Pixel_Pip_Off")
                    {
                        pipOff = px;
                        pipW = w;
                        // off 态没有高光像素：必须中心对称（180° 旋转自等）
                        for (int k = 0; k < px.Length; k++)
                        {
                            if (!Same(px[k], px[px.Length - 1 - k]))
                            {
                                problems.Add(t.name + "：暗位点不是中心对称的（#" + k + " "
                                    + Hex(px[k]) + " vs " + Hex(px[px.Length - 1 - k]) + "）。");
                                break;
                            }
                        }
                    }
                    else
                    {
                        pipOn = px;
                    }
                }
            }

            if (pipOff != null && pipOn != null)
            {
                int diff = 0;
                for (int k = 0; k < pipOff.Length; k++)
                {
                    if (!Same(pipOff[k], pipOn[k]))
                        diff++;
                }
                if (diff == 0)
                    problems.Add("Pixel_Pip_On 与 Pixel_Pip_Off 完全同图——亮/暗位点必须可区分。");
            }
            else
            {
                problems.Add("位点资产缺失（on=" + (pipOn != null) + " off=" + (pipOff != null) + "）。");
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
            Assert.That(PixelSkin.PressOffset, Is.EqualTo(new Vector2(1f, -1f)),
                "按压位移应当是右下 1px（UI 坐标 +x 右 / -y 下）。");
        }

        // ------------------------------------------------------------------
        // ④ 皮肤契约：图集满格、运行时取用不空（装配侧唯一的取图口）
        // ------------------------------------------------------------------

        [Test]
        public void PixelSkin_AtlasIsFullGrid_AndAccessorsNeverNull()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PixelSkinAsset>("Assets/Resources/UI/PixelSkin.asset");
            Assert.That(asset, Is.Not.Null,
                "图集资产缺失：Assets/Resources/UI/PixelSkin.asset（烘焙入口会生成它）。");

            Assert.That(asset.plates, Is.Not.Null.And.Length.EqualTo(7 * 3), "plates 应为 7 tone×3 state");
            Assert.That(asset.tracks, Is.Not.Null.And.Length.EqualTo(7), "tracks 应为 7");
            Assert.That(asset.tabs, Is.Not.Null.And.Length.EqualTo(7), "tabs 应为 7");
            Assert.That(asset.fills, Is.Not.Null.And.Length.EqualTo(5), "fills 应为 5");
            Assert.That(asset.toneColors, Is.Not.Null.And.Length.EqualTo(7 * 3), "toneColors 应为 21");
            Assert.That(asset.ring && asset.focus && asset.pipOn && asset.pipOff
                && asset.separatorH && asset.separatorV && asset.shadow,
                Is.True, "语义件（ring/focus/pip×2/sep×2/shadow）有空槽。");
            for (int i = 0; i < asset.plates.Length; i++)
                Assert.That(asset.plates[i], Is.Not.Null, "plates[" + i + "] 空槽。");

            // 取用层全量走一遍：任何一个入口返回 null，装配侧就是白块
            foreach (PixelTone tone in Enum.GetValues(typeof(PixelTone)))
            {
                Assert.That(PixelSkin.Plate(tone, PixelState.Normal), Is.Not.Null, "Plate " + tone);
                Assert.That(PixelSkin.Plate(tone, PixelState.Hovered), Is.Not.Null, "Plate 悬停 " + tone);
                Assert.That(PixelSkin.Plate(tone, PixelState.Pressed), Is.Not.Null, "Plate 按压 " + tone);
                Assert.That(PixelSkin.Track(tone), Is.Not.Null, "Track " + tone);
                Assert.That(PixelSkin.Tab(tone), Is.Not.Null, "Tab " + tone);
                Assert.That(PixelSkin.TextColorOn(tone).a, Is.EqualTo((byte)255), "TextColorOn " + tone);
            }
            foreach (PixelFillKind kind in Enum.GetValues(typeof(PixelFillKind)))
                Assert.That(PixelSkin.Fill(kind), Is.Not.Null, "Fill " + kind);
            Assert.That(PixelSkin.Ring, Is.Not.Null);
            Assert.That(PixelSkin.Focus, Is.Not.Null);
            Assert.That(PixelSkin.Pip(true), Is.Not.Null);
            Assert.That(PixelSkin.Pip(false), Is.Not.Null);
            Assert.That(PixelSkin.Separator(true), Is.Not.Null);
            Assert.That(PixelSkin.Separator(false), Is.Not.Null);
            Assert.That(PixelSkin.ShadowSprite, Is.Not.Null);
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
                        + "）——色带边界必须落在 u 网格上。");
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
