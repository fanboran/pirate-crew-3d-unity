using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using PirateCrew.Battle;
using PirateCrew.Battle.Levels;
using PirateCrew.Core;
using PirateCrew.Rendering.Pixelart;
using PirateCrew.SceneArt;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateCrew.ArtReview
{
    /// <summary>
    /// 战斗小地图**顶视底图**的播放器出图：命令行传 <c>-minimapOut &lt;绝对目录&gt;</c>（配
    /// <c>-minimapLevel &lt;关卡号&gt;</c>）启动播放器，本组件进 Battle 场景、等本关静态陈设
    /// 就绪后把场景从正上方渲成一帧平色缩略图（66×36 纹素，= 海图窗体内容区的艺术像素数），
    /// 存 PNG 后自动退出。逐关跑一遍即得全套底图（协调者用 bash 循环 1..4）。
    ///
    /// 【为什么不实时渲染】海图窗体的定位是"顶视图缩略"——底图是静态的（场景陈设只在装配期
    /// 变化），动态的只有单位位置，单位由既有点位层表达（红/蓝点 + 死亡淡出）。运行时
    /// 贴 PNG + 算点位，零渲染成本。
    ///
    /// 【为什么是平色直渲】场景物体的 <c>PixelartOpaque</c> 材质只写像素化管线的
    /// 7 张 G-buffer，没有可被普通相机取用的颜色 pass；顶视相机走普通 URP 前向渲染器，
    /// 采图前把 renderer 临时换成 <see cref="TopFlatShaderName"/> 平色材质
    /// （取色单源 <see cref="PixelartMaterialFactory.TryGetAlbedo"/>）。
    /// 取不到 albedo 的 renderer（如海面替身 Water_Ocean）直接跳过——它在本路径下不可见，
    /// 露出的正是相机背景海色，与主画面的海一致。
    ///
    /// 【零侵入】不带 <c>-minimapOut</c> 启动时本组件不安装，无任何开销。
    /// 按名查找与临时换材质仅本出图组件豁免（同 <see cref="PlayerArtCapture"/> 的口径），
    /// 运行时代码仍禁 GameObject.Find；换的是 renderer 的材质**引用**，不写资产文件，进程即弃。
    ///
    /// 【怎么跑】构建播放器后：
    ///   PirateCrew3D.exe -minimapOut &lt;工程&gt;/Assets/Resources/Minimaps -minimapLevel 1
    /// 目录直接指进 Assets/Resources/Minimaps/（运行时 Resources.Load 按此读），
    /// 之后再跑一次接线批次（HudMinimapSceneSetup.WireMinimap）设置纹理导入参数
    /// （点采样/无 mipmap/不压缩——像素图过压缩会糊）。
    /// </summary>
    public sealed class MinimapTopViewCapture : MonoBehaviour
    {
        /// <summary>顶视平色 shader 内部名（shader 文件 Assets/Art/Shaders/PirateMinimapTopFlat.shader；
        /// 由 ArtGate 登记 Always Included 保障播放器里按名可取）。</summary>
        public const string TopFlatShaderName = "PirateCrew/Minimap/TopFlat";

        /// <summary>单位根节点名（与 Editor 侧 HudMinimapSceneSetup 的常量同值互指；
        /// 出图时隐藏单位——小地图里角色由点位层表达，不进底图）。</summary>
        const string Team0RootName = "Team0_Red";
        const string Team1RootName = "Team1_Blue";

        const float ReadyTimeoutSeconds = 60f;

        string _outDir;
        int _level;

        /// <summary>
        /// 组合根接线入口（不带 <c>-minimapOut</c> 时零开销）。命令行由
        /// <see cref="CommandLineOptions"/> 统一解析——本类不自扫 argv。
        /// </summary>
        [GameBootstrap(GameBootstrapPhase.Initialize, order: 101)]
        internal static void Install()
        {
            string outDir = CommandLineOptions.GetValue(ToolFlags.MinimapOut);
            if (string.IsNullOrEmpty(outDir))
                return; // 正常启动：零开销，什么都不装。

            // 与评审出图互斥：两者的关卡覆盖共用 ArtReviewCaptureOverride，混用会互相劫持。
            if (!string.IsNullOrEmpty(CommandLineOptions.GetValue(ToolFlags.ArtReviewOut))
                || !string.IsNullOrEmpty(CommandLineOptions.GetValue(ToolFlags.PixelartOut)))
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] -minimapOut 不能与 -artReviewOut/-pixelartOut 同用"
                    + "（共用选关覆盖口）——本次不采集。");
                return;
            }

            // 【相对路径双基准陷阱】Directory.CreateDirectory 按进程 CWD 解析相对路径——
            // 入口处统一转绝对（同 PlayerArtCapture，2026-09-28 实测教训）。
            outDir = Path.GetFullPath(outDir);

            if (!CommandLineOptions.TryGetInt(ToolFlags.MinimapLevel, out int level)
                || level < ShowcaseLevels.FirstLevel || level > ShowcaseLevels.LastLevel)
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] 需要 -minimapLevel <"
                    + ShowcaseLevels.FirstLevel + ".." + ShowcaseLevels.LastLevel + ">（样板关号）。");
                Application.Quit(1);
                return;
            }

            // 借美术评审的选关覆盖口让本局解析到目标关（LevelSourceResolver ① 级）。
            ArtReviewCaptureOverride.LevelNumber = level;

            var go = new GameObject("[MinimapTopViewCapture]");
            var capture = go.AddComponent<MinimapTopViewCapture>();
            capture._outDir = outDir;
            capture._level = level;
            DontDestroyOnLoad(go);
        }

        IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] 出图会话启动：关卡 " + _level
                + " → " + _outDir);

            // 等 Bootstrapper 的引导流程走完再切战斗场景（同 PlayerArtCapture：
            // 否则 LoadScene(Battle) 会被随后的 MainMenu 加载顶掉）。
            float bootDeadline = Time.unscaledTime + 15f;
            while (SceneManager.GetActiveScene().name != SceneNames.MainMenu
                && Time.unscaledTime < bootDeadline)
                yield return null;
            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] 引导等待结束，当前场景："
                + SceneManager.GetActiveScene().name);
            yield return new WaitForSecondsRealtime(0.5f);

            SceneManager.LoadScene(SceneNames.Battle, LoadSceneMode.Single);
            yield return null;
            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] Battle 加载返回，当前场景："
                + SceneManager.GetActiveScene().name);
            if (SceneManager.GetActiveScene().name != SceneNames.Battle)
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] Battle 场景加载失败，当前场景："
                    + SceneManager.GetActiveScene().name + "——中止采集");
                Application.Quit(1);
                yield break;
            }

            // 等本关陈设就绪：RuntimeSceneArt.LastLevelNumber 由 BattleController.Awake 的
            // RebuildSceneArt 写入（烘焙件实例化完成），等于"场景本身已在场"。
            float deadline = Time.unscaledTime + ReadyTimeoutSeconds;
            RuntimeSceneArt sceneArt = FindInRoots<RuntimeSceneArt>();
            while (Time.unscaledTime < deadline)
            {
                if (sceneArt != null && sceneArt.LastLevelNumber == _level)
                    break;
                yield return null;
                if (sceneArt == null)
                    sceneArt = FindInRoots<RuntimeSceneArt>();
            }

            if (sceneArt == null || sceneArt.LastLevelNumber != _level)
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] 等 " + _level
                    + " 关陈设就绪超时（RuntimeSceneArt 未接线或烘焙件缺失）——中止采集");
                Application.Quit(1);
                yield break;
            }

            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] 陈设就绪（关卡 "
                + sceneArt.LastLevelNumber + "），等战斗环境落定");
            // 再等 Start 波次的战斗环境（海面圆盘等）落定，避免拍到半装配的场。
            yield return new WaitForSecondsRealtime(0.8f);

            // 跨度/关卡号与游戏内完全同源：走 LevelSourceResolver（ArtReviewCaptureOverride
            // 已在 Install 写入，命中 ① 级出图覆盖），不直接读资产字段（含 LevelGeometry 换算）。
            LevelSource source = LevelSourceResolver.Resolve();
            if (source == null || source.LevelNumber != _level)
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] 关卡解析不符：期望 "
                    + _level + "，实得 " + (source != null ? source.LevelNumber.ToString() : "null") + "——中止采集");
                Application.Quit(1);
                yield break;
            }

            string pngPath = Path.Combine(_outDir, "level-" + _level + ".png");
            _captureDone = false;
            yield return CaptureTopView(sceneArt, source, pngPath);
            if (!_captureDone || !File.Exists(pngPath))
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] 顶视图未落盘——中止");
                Application.Quit(1);
                yield break;
            }

            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] 关卡 " + _level + " 顶视图已落盘：" + pngPath);
            Application.Quit(0);
        }

        /// <summary>采集协程的完成标志（协程不能 return 值，用字段带回）。</summary>
        bool _captureDone;

        // ------------------------------------------------------------------
        // 采集（协程：相机渲满两帧后才读回）
        // ------------------------------------------------------------------

        /// <summary>隐藏单位 → 平色替换 → 顶视渲一帧 → 读回存 PNG。完成置 <see cref="_captureDone"/>。</summary>
        IEnumerator CaptureTopView(RuntimeSceneArt sceneArt, LevelSource levelSource, string pngPath)
        {
            HideUnitRoots();

            Shader flatShader = Shader.Find(TopFlatShaderName);
            if (flatShader == null)
            {
                global::PirateCrew.Core.Log.Error("[MinimapTopViewCapture] 找不到 shader " + TopFlatShaderName
                    + "（未登记 Always Included？见 ArtGate.RuntimeFindShaderNames）——中止采集");
                yield break;
            }

            // 平色替换：每个"原材质"换出一份平色材质（按实例缓存，同材质多 renderer 共用）。
            // 取不到 albedo 的 renderer 跳过——它在普通相机下本就不可见，露背景海色。
            var flatBySource = new Dictionary<Material, Material>();
            var untintable = new HashSet<string>();
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>(includeInactive: false);
            var swappedRenderers = new List<Renderer>(renderers.Length);
            foreach (Renderer renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer)
                    continue;

                Material[] shared = renderer.sharedMaterials;
                bool replaced = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    Material source = shared[i];
                    if (source == null || !PixelartMaterialFactory.TryGetAlbedo(source, out Color albedo))
                    {
                        if (source != null && untintable.Add(source.name))
                            global::PirateCrew.Core.Log.Warn("[MinimapTopViewCapture] 取不到 albedo，跳过材质 "
                                + source.name + "（顶视图里该物读背景海色）");
                        continue;
                    }

                    if (!flatBySource.TryGetValue(source, out Material flat))
                    {
                        flat = new Material(flatShader) { name = "MinimapFlat_" + source.name };
                        flat.SetColor("_BaseColor", albedo);
                        flatBySource.Add(source, flat);
                    }

                    shared[i] = flat;
                    replaced = true;
                }

                if (replaced)
                {
                    renderer.sharedMaterials = shared;
                    swappedRenderers.Add(renderer);
                }
            }

            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] 平色替换：renderer 共 "
                + renderers.Length + "，换材质 " + swappedRenderers.Count + "；独立材质 "
                + flatBySource.Count + "，取色失败 " + untintable.Count
                + (untintable.Count > 0 ? "（" + string.Join("、", untintable.ToArray()) + "）" : ""));

            // 内容包围盒取景：摆位点（关卡数据的 x/z）是模型**原点**，不等于几何中心
            // （化工厂整件的几何明显偏在原点一侧）——按名义场地中心取景会把核心结构
            // 挤到图的一侧。这里以「本次实际换到平色材质的 renderer」的世界包围盒
            // （顶视 = XZ 投影）为内容实测：相机对准包围盒中心、覆盖包围盒（信箱适配）。
            // 内容盒只收**陈设件**（本次实例化的烘焙件 + 空岛静态件）∩ 换材质成功者。
            // 全场景/art 根粒度不可用：水面圆盘（bounds 615m）、SceneArt 根下的运行期水部件
            //（中心在场地外 z≈-16）会把联合包围盒撑爆、中心拉偏（实测 span 9998m / centerZ=-16）。
            var pieceRenderers = new HashSet<Renderer>(sceneArt.EnumeratePieceRenderers());

            var content = new Bounds(Vector3.zero, Vector3.zero);
            bool contentStarted = false;
            foreach (Renderer r in swappedRenderers)
            {
                if (!pieceRenderers.Contains(r))
                    continue;
                if (!contentStarted)
                {
                    content = new Bounds(r.bounds.center, Vector3.zero);
                    contentStarted = true;
                }
                else
                {
                    content.Encapsulate(r.bounds);
                }
            }

            // 取景 = 内容盒 ∪ 场地矩形（单位在场内任意处，点位必须落图内；陈设核心全入图）。
            content.Encapsulate(new Vector3(0f, 0f, 0f));
            content.Encapsulate(new Vector3(levelSource.SpanX, 0f, levelSource.SpanZ));
            if (!contentStarted)
            {
                content = new Bounds(new Vector3(levelSource.SpanX * 0.5f, 0f, levelSource.SpanZ * 0.5f),
                    new Vector3(levelSource.SpanX, 0f, levelSource.SpanZ));
            }

            // 边距：长边 6% + 1 m——压边距的碎屑（危险虚线端头等）不至于顶到图缘。
            float pad = Mathf.Max(content.size.x, content.size.z) * 0.06f + 1f;
            var frame = new TopViewFrame
            {
                centerX = content.center.x,
                centerZ = content.center.z,
                spanX = content.size.x + pad * 2f,
                spanZ = content.size.z + pad * 2f,
            };
            var mapping = TopViewMapping.ForContent(
                frame.centerX, frame.centerZ, frame.spanX, frame.spanZ, TopViewFraming.TopViewTexAspect);

            string framePath = Path.Combine(_outDir, "level-" + _level + "-frame.json");
            File.WriteAllText(framePath, JsonUtility.ToJson(frame));
            global::PirateCrew.Core.Log.Info("[MinimapTopViewCapture] 取景中心 ("
                + frame.centerX.ToString("F1") + ", " + frame.centerZ.ToString("F1")
                + ") 跨度 (" + frame.spanX.ToString("F1") + " × " + frame.spanZ.ToString("F1")
                + ")，参数随图落盘：" + framePath);

            var cameraGo = new GameObject("MinimapTopViewCamera");
            Camera camera = cameraGo.AddComponent<Camera>();
            // 顶视：从正上方朝下看，屏幕上方 = 远处（-Z）——与 MinimapRules 的图方向口径一致。
            camera.transform.SetPositionAndRotation(
                new Vector3(frame.centerX, 200f, frame.centerZ),
                Quaternion.LookRotation(Vector3.down, Vector3.back));
            camera.orthographic = true;
            camera.orthographicSize = mapping.OrthoSize;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // 与主相机背景同源（BattleSceneSetup.CreateCamera；.linear 预补偿防 sRGB 提亮）。
            camera.backgroundColor = new Color(0x35 / 255f, 0x6A / 255f, 0x95 / 255f, 1f).linear;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            camera.depth = 10f;   // 场景里最晚渲，别跟主相机抢序

            int texWidth = TopViewFraming.TopViewTexWidth;
            int texHeight = TopViewFraming.TopViewTexHeight;
            var rt = new RenderTexture(texWidth, texHeight, 24,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = rt;

            // 等相机真渲上两帧（URP 每帧自动渲所有启用相机到 targetTexture）。
            yield return null;
            yield return null;

            var readback = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
            // 读回校验：整图单色 = 顶视相机还没渲出内容（release 首帧 shader 编译竞态，
            // 实测偶发拍到纯清屏色）——再等两帧重读，最多 5 次。
            for (int attempt = 0; ; attempt++)
            {
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, texWidth, texHeight), 0, 0);
                readback.Apply();
                RenderTexture.active = null;

                if (attempt >= 4 || !IsSingleColor(readback))
                    break;
                global::PirateCrew.Core.Log.Warn("[MinimapTopViewCapture] 第 " + (attempt + 1)
                    + " 次读回为纯色（相机未渲完），重等两帧重读");
                yield return null;
                yield return null;
            }

            byte[] png = readback.EncodeToPNG();
            Directory.CreateDirectory(_outDir);
            File.WriteAllBytes(pngPath, png);

            camera.targetTexture = null;
            Object.Destroy(cameraGo);
            Object.Destroy(readback);
            rt.Release();
            _captureDone = true;
        }

        /// <summary>整图是否单一颜色（相机未渲出内容的竞态特征）。</summary>
        static bool IsSingleColor(Texture2D tex)
        {
            Color32 first = tex.GetPixel(0, 0);
            Color32[] all = tex.GetPixels32();
            for (int i = 1; i < all.Length; i++)
            {
                if (all[i].r != first.r || all[i].g != first.g || all[i].b != first.b)
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 场景查找（仅出图组件豁免的按名/类型查找）
        // ------------------------------------------------------------------

        static T FindInRoots<T>() where T : Component
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }

        static void HideUnitRoots()
        {
            HideRootByName(Team0RootName);
            HideRootByName(Team1RootName);
        }

        static void HideRootByName(string rootName)
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                {
                    roots[i].SetActive(false);
                    return;
                }
            }
            global::PirateCrew.Core.Log.Warn("[MinimapTopViewCapture] 找不到单位根 " + rootName
                + "（单位可能未被生成）——继续采集，若画面里出现小人属预期外");
        }
    }
}
