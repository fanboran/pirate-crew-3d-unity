using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PirateCrew.Rendering.Pixelart
{
    /// <summary>
    /// **把游戏本体里"还不在本路径上"的可见内容，就地换成像素化材质**（接入游戏本体的那一层）。
    ///
    /// 【为什么需要它，而不是去改六个 Builder】游戏内容来源有三类：预制体（角色/陈设/世界 kit）、
    /// 编辑器烘焙的材质资产（云件/岛件）、**运行期新建的材质**（海图站面、海面、弹体兜底）。
    /// 前三类各自去改生成器就是"同一件事写在六个地方"；而内容到底有哪些、什么时候生成，
    /// 只有跑起来才知道（海图开局合成、活物可能更晚）。所以在**内容成型之后扫一遍**：
    /// 按源材质取色派生出本路径材质、就地替换 —— 与试点装配器 `SwapMaterials` 同一个口径，
    /// 只是执行时机搬到了运行期；而且**不猜颜色**：源材质没有取色属性就报错列出，绝不拿白色顶上。
    ///
    /// 【分类规则：世界内容 vs 效果件（这是本类最容易搞错的一处）】
    /// 本路径的几何只有"不透明数据 + 全屏着色"，**放不下混合**；半透明内容只能由 rig 的
    /// **透明件叠加相机**用各自的旧材质画在成图之上（全分辨率、不参与像素化）。
    /// 所以关键不是"它是不是半透明"，而是"**它是世界的一部分，还是打在画面上的效果**"：
    /// <list type="bullet">
    ///   <item>**世界内容**（云、栈桥、浮冰、泡沫、危险虚线、海面…）即使材质是 alpha 混合，
    ///         也**必须转成不透明像素化材质**。否则它就是"旧材质 + 全分辨率 + 没有墨线色带"
    ///         地糊在成图之上——实测症状就是创始人 2026-09-22 报的"完全不是"（满屏平滑内容）。
    ///         试点场景当初也是这么处理的：危险虚线转不透明红、接触阴影直接删掉。</item>
    ///   <item>**效果件**（粒子 FX、接触阴影、弹道预览、空岛发光件）留给叠加档
    ///         （判据见 <see cref="IsEffectMaterial"/>）——这些是"贴在画面上的效果"，
    ///         转成不透明会把它们变成实体几何（爆炸变成一坨带描边的块）。</item>
    /// </list>
    ///
    /// 【为什么要重复扫】内容不是一次到齐的（海图开局合成、FX/道具更晚），所以开局扫一次 +
    /// 之后低频补扫（<see cref="rescanIntervalSeconds"/>）。只在**有新变化**时打日志，不刷屏。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelartContentConverter : MonoBehaviour
    {
        [Tooltip("补扫间隔（秒）。0 = 只扫一次（内容一次到齐的场景用）。")]
        [Min(0f)] public float rescanIntervalSeconds = 1f;

        [Tooltip("补扫总时长（秒）。之后停止补扫（跑完还在生成的内容说明它有别的接线问题）。")]
        [Min(0f)] public float rescanDurationSeconds = 30f;

        [Tooltip("日志里最多列几个材质名（免得刷屏）。")]
        [Min(1)] public int maxNamesInLog = 12;

        /// <summary>已派生过的材质（源材质实例 ID → 派生材质）。同一个源材质只派生一次。</summary>
        readonly Dictionary<int, Material> _derived = new Dictionary<int, Material>();

        /// <summary>取不到色的源材质（只报一次，避免每次补扫重复刷屏）。</summary>
        readonly HashSet<int> _reportedNoColor = new HashSet<int>();

        float _nextScanTime;
        float _stopScanTime;
        bool _summaryLogged;

        /// <summary>累计换掉的 Renderer 数。</summary>
        public int ConvertedRendererCount { get; private set; }

        /// <summary>最近一次扫描时"已在本路径上"的 Renderer 数。</summary>
        public int AlreadyPixelartCount { get; private set; }

        /// <summary>最近一次扫描时"世界内容（原本半透明）转成不透明像素化"的数量。</summary>
        public int ConvertedFromTransparentCount { get; private set; }

        /// <summary>最近一次扫描时"留给叠加档的效果件"数量。</summary>
        public int OverlayKeptCount { get; private set; }

        /// <summary>取不到色、因此没被转换的材质名（这些物体在新管线下**不画**）。</summary>
        public readonly List<string> NoColorMaterialNames = new List<string>();

        /// <summary>留给叠加档的效果件材质名。</summary>
        public readonly List<string> OverlayMaterialNames = new List<string>();

        /// <summary>由半透明世界内容转成不透明像素化的材质名。</summary>
        public readonly List<string> ConvertedFromTransparentNames = new List<string>();

        void OnEnable()
        {
            _stopScanTime = Time.unscaledTime + Mathf.Max(0f, rescanDurationSeconds);
            Scan("首次");
        }

        void Update()
        {
            if (rescanIntervalSeconds <= 0f)
                return;
            if (Time.unscaledTime < _nextScanTime || Time.unscaledTime > _stopScanTime)
                return;

            Scan(null);
        }

        /// <summary>
        /// 扫一遍场景里的 <see cref="MeshRenderer"/>：世界内容换成本路径材质，效果件记账留给叠加档。
        /// 返回本次**新**换掉的 Renderer 数。
        /// </summary>
        public int Scan(string note)
        {
            _nextScanTime = Time.unscaledTime + Mathf.Max(0.05f, rescanIntervalSeconds);

            int convertedThisScan = 0;
            int already = 0;
            int fromTransparent = 0;
            int overlayKept = 0;

            NoColorMaterialNames.Clear();
            OverlayMaterialNames.Clear();
            ConvertedFromTransparentNames.Clear();

            MeshRenderer[] renderers = FindObjectsByType<MeshRenderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer renderer = renderers[i];
                Material source = renderer.sharedMaterial;
                if (source == null || source.shader == null)
                    continue;

                if (source.shader.name == PixelartPath.ObjectShaderName)
                {
                    already++;
                    continue;
                }

                // 效果件：留给叠加档（它用旧材质在成图之上画，全分辨率、不参与像素化——已知取舍）。
                bool isTransparent = source.renderQueue >= (int)RenderQueue.Transparent;
                if (isTransparent && IsEffectMaterial(source))
                {
                    overlayKept++;
                    AddUnique(OverlayMaterialNames, source.name);
                    continue;
                }

                // 其余一律算世界内容（含"alpha 混合的世界内容"）：转成不透明像素化材质。
                Material derived = DeriveOrGet(source);
                if (derived == null)
                {
                    AddUnique(NoColorMaterialNames, source.name);
                    continue;
                }

                if (!ReferenceEquals(renderer.sharedMaterial, derived))
                {
                    renderer.sharedMaterial = derived;
                    convertedThisScan++;
                    ConvertedRendererCount++;
                    if (isTransparent)
                    {
                        fromTransparent++;
                        AddUnique(ConvertedFromTransparentNames, source.name);
                    }
                }
            }

            AlreadyPixelartCount = already;
            ConvertedFromTransparentCount = fromTransparent;
            OverlayKeptCount = overlayKept;

            // 有变化、或第一次扫，才打日志（补扫每 1s 一次，刷屏会盖掉真正重要的东西）。
            if (convertedThisScan > 0 || !_summaryLogged)
            {
                _summaryLogged = true;
                global::PirateCrew.Core.Log.Info("[PixelartContentConverter]"
                    + (string.IsNullOrEmpty(note) ? " 补扫" : " " + note)
                    + "：本次换掉 " + convertedThisScan + " 个 renderer（累计 " + ConvertedRendererCount
                    + "）、已在本路径 " + already + " 个、**世界内容转不透明** " + fromTransparent
                    + " 个、留给叠加档的效果件 " + overlayKept + " 个、派生材质池 " + _derived.Count + " 个。");

                if (ConvertedFromTransparentNames.Count > 0)
                {
                    global::PirateCrew.Core.Log.Info("[PixelartContentConverter] 由半透明世界内容转成不透明像素化："
                        + JoinNames(ConvertedFromTransparentNames));
                }

                if (OverlayMaterialNames.Count > 0)
                {
                    global::PirateCrew.Core.Log.Info("[PixelartContentConverter] 留给叠加档的效果件（全分辨率、"
                        + "不参与像素化）：" + JoinNames(OverlayMaterialNames));
                }
            }

            if (NoColorMaterialNames.Count > 0)
            {
                // 【Warn 而非 Error】Water_Ocean 这类"按设计不画"的已知缺色材质会常驻触发这条
                // （交接文档 §六 遗留项）；Error 级会被 Unity Test Framework 当未预期日志
                // 把所有加载 Battle 的 PlayMode 测试记成失败（2026-09-23 实测）。是"待补色"，不是故障。
                global::PirateCrew.Core.Log.Warn("[PixelartContentConverter] 有材质既没有 _BaseColor / _Color "
                    + "也没有 _BaseColorA，无法派生（这些物体在新管线下**不会出现在画面里**，不是黑、是不画）："
                    + JoinNames(NoColorMaterialNames)
                    + " 处置：给它们在本路径的材质表里手工指定一条颜色（口径见 PixelartMaterialFactory）。");
            }

            return convertedThisScan;
        }

        string JoinNames(List<string> names)
        {
            int take = Mathf.Min(maxNamesInLog, names.Count);
            string text = string.Join("、", names.GetRange(0, take).ToArray());
            return names.Count > take ? text + " 等 " + names.Count + " 个" : text;
        }

        static void AddUnique(List<string> list, string name)
        {
            if (!string.IsNullOrEmpty(name) && !list.Contains(name))
                list.Add(name);
        }

        /// <summary>按源材质派生（同源材质只派生一次）；取不到色返回 null 并记账。</summary>
        Material DeriveOrGet(Material source)
        {
            int key = source.GetInstanceID();
            if (_derived.TryGetValue(key, out Material cached))
                return cached;

            if (!PixelartMaterialFactory.TryGetAlbedo(source, out Color albedo))
            {
                _reportedNoColor.Add(key);   // 清单在 Scan 末尾统一列
                return null;
            }

            Material derived = PixelartMaterialFactory.Create(
                PixelartMaterialFactory.DerivedName(source.name), albedo);
            _derived[key] = derived;
            return derived;
        }

        /// <summary>
        /// 这个材质是不是**效果件**（该留给叠加档，而不是转成不透明像素化材质）。
        ///
        /// 【为什么按着色器/材质名判，而不是按渲染队列】队列只说明"要混合"，不说明对象是
        /// "世界的一部分"还是"打在画面上的效果"。云、栈桥、泡沫、危险虚线这些**世界内容**
        /// 也常常是 alpha 混合的，把它们交给叠加档就等于"绕开像素化、用旧材质全分辨率画在成图上"
        /// ——实测症状：满屏平滑内容、没有墨线没有色带（创始人 2026-09-22 报的"完全不是"）。
        ///
        /// 【名单怎么来的】对着试点场景的处置反推：粒子 FX（`PirateCrew/Fx/*`）、
        /// 船员/弹体接触阴影、弹道预览、空岛发光件——这几类是"贴在画面上的效果"，
        /// 转成不透明会变成实体几何（爆炸变一坨带描边的块）；其余一律算世界内容。
        /// </summary>
        static bool IsEffectMaterial(Material source)
        {
            if (source == null)
                return true;

            string shaderName = source.shader != null ? source.shader.name : string.Empty;
            if (shaderName.StartsWith("PirateCrew/Fx/"))
                return true;

            string name = source.name ?? string.Empty;
            return name.StartsWith("Fx_")
                || name.IndexOf("ContactShadow", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("Trajectory", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("Glow", System.StringComparison.Ordinal) >= 0
                || name.IndexOf("SelectionRing", System.StringComparison.Ordinal) >= 0;
        }
    }
}
