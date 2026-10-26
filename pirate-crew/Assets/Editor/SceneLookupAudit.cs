using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PirateCrew.EditorTools
{
    /// <summary>
    /// 运行期查找防回归闸门：扫描 <c>Assets/Scripts/**</c>，把
    /// <c>GameObject.Find</c> / <c>FindObjectOfType</c> / <c>FindObjectsOfType</c> / <c>FindWithTag</c>
    /// （含新 API 逃逸写法 <c>FindAnyObjectByType</c> / <c>FindObjectsByType</c>）以及同属查找语义的
    /// <c>Camera.main</c>（属性访问形态）与 <c>transform.Find</c>（层级内按名查找）逐条分类，写入
    /// <c>docs/项目/审计/运行期查找清退报告.md</c>，**未登记白名单的命中一律以退出码 1 失败**。
    ///
    /// 【入口】
    ///   菜单: PirateCrew/审计/运行期查找清退报告
    ///   无头（CI / 主控）:
    ///     -batchmode -nographics -quit -projectPath &lt;P&gt; \
    ///       -executeMethod PirateCrew.EditorTools.SceneLookupAudit.AuditFromCommandLine -logFile -
    ///   可选参数 <c>-lookupAuditOut &lt;绝对路径&gt;</c> 覆盖报告输出位置。
    ///
    /// 【为什么是白名单而不是"归零"】存量里确实有三类命中不该被清退程序消灭：
    ///   ① 命令行工具 / 出图路径（需要在**任意手工场景**里工作，显式引用会让工具模式复杂化）；
    ///   ② 能力探测（"场景里有没有 AudioListener"这类**没有持有者**的探测，不是依赖注入）；
    ///   ③ 越过事件契约才能消除的进程级服务反查（加载荷字段属破坏性契约变更，不属本轨道）。
    ///   每一条都必须在 <see cref="Whitelist"/> 里带理由登记，否则闸门失败——这才是"存量可见、新增禁止"。
    ///
    /// 【白名单的键是 file + token，不是行号】行号会随无关改动漂移，用它做键会让闸门变成「每次
    /// 改别处都要顺手改白名单」的噪音源。故：键 = 相对路径 + 调用表达式片段（唯一、稳定），
    /// 注册时记录的行号只作**文档**用途（报告里同时打印扫描时的实时行号）。
    ///
    /// 【本脚本自己不在扫描范围内】扫描域固定 <c>Assets/Scripts/</c>（本文件在 <c>Assets/Editor/</c>）——
    /// 白名单表里必然出现这些模式字符串，扫自己会自噬。
    /// </summary>
    public static class SceneLookupAudit
    {
        const string ScriptsRoot = "Assets/Scripts";

        /// <summary>报告默认落在仓库根的入库审计目录（与入库报告同路径，重跑即刷新权威报告）。</summary>
        const string ReportPathFallback = "docs/项目/审计/运行期查找清退报告.md";

        /// <summary>报告输出覆盖参数（无头调用时可选）。</summary>
        const string OutputArg = "-lookupAuditOut";

        /// <summary>
        /// 查找形态正则。除六类方法调用外，两类"长得不像方法调用"的查找也在网内：
        /// - <c>Camera.main</c>：属性访问（不是方法调用），按 Tag 全场扫主相机，成本与 FindObjectOfType 同级。
        ///   左侧负向断言排除 <c>MyCamera.main</c> 这类自定义成员名，<c>UnityEngine.Camera.main</c>
        ///   全限定形态（前字符是 '.'）照常命中；
        /// - <c>transform.Find</c> / <c>Transform.Find</c>：层级内按名查找，只查直接子节点。
        ///   同样带左侧断言（排除 <c>myTransform.Find</c> 这类以同名结尾的局部变量），且与
        ///   <c>GameObject.Find</c> 分支互不吞并（前缀不同）。
        /// </summary>
        static readonly Regex LookupPattern = new Regex(
            @"(GameObject\s*\.\s*Find|FindObjectOfType|FindObjectsOfType|FindAnyObjectByType|FindObjectsByType|FindWithTag|FindGameObjectWithTag"
            + @"|(?<![A-Za-z0-9_])Camera\s*\.\s*main\b"
            + @"|(?<![A-Za-z0-9_])[Tt]ransform\s*\.\s*Find\b)",
            RegexOptions.Compiled);

        /// <summary>命中分类。</summary>
        enum Kind
        {
            /// <summary>运行时路径：业务代码里的场景查找（默认应清退或白名单）。</summary>
            Runtime,

            /// <summary>工具/演示路径：出图、命令行采集、编辑器辅助（允许保留，需理由）。</summary>
            Tool,
        }

        /// <summary>一条白名单登记（键 = 相对路径 + 调用片段）。</summary>
        struct WhitelistEntry
        {
            public string Path;
            public string Token;
            /// <summary>登记当时的行号（仅文档用途；匹配不看它）。</summary>
            public int LineAtRegistration;
            public Kind Kind;
            public string Reason;

            public override string ToString()
            {
                return Path + " · " + Token;
            }
        }

        /// <summary>清退前基线（2026-09-21 实测，用于报告里的前后对比表）。</summary>
        static readonly string[] Baseline =
        {
            "PirateCrew/ArtReview/PlayerArtCapture.cs:303 · FindObjectsByType",
            "PirateCrew/ArtReview/PlayerArtCapture.cs:574 · FindObjectsOfType",
            "PirateCrew/Audio/AudioService.cs:1023 · FindObjectOfType",
            "PirateCrew/Battle/BattleCameraDriver.cs:404 · FindObjectOfType",
            "PirateCrew/Battle/BattleCameraDriver.cs:419 · FindObjectOfType",
            "PirateCrew/Battle/BattleCameraDriver.cs:1117 · FindObjectOfType",
            "PirateCrew/Battle/BattleController.cs:360 · FindObjectOfType",
            "PirateCrew/Water/WaterSimulationDriver.cs:457 · FindObjectsOfType",
            "UI/BattleHud.cs:403 · FindObjectOfType",
        };

        /// <summary>白名单：**唯一**允许保留的命中。键 = Scripts 下的相对路径 + 调用片段。</summary>
        static readonly WhitelistEntry[] Whitelist =
        {
            // ---------------- 运行时路径 ----------------

            new WhitelistEntry
            {
                Path = "PirateCrew/Audio/AudioService.cs",
                Token = "FindObjectOfType<AudioListener>",
                LineAtRegistration = 1023,
                Kind = Kind.Runtime,
                Reason = "能力探测而非依赖注入：问的是\"本场景有没有监听器\"，没有持有者可以注入。"
                         + "已限频（ListenerProbeRetrySeconds=1s）+ 负缓存，命中一次后永久短路；"
                         + "换成登记制需要 5 个场景（含手摆的菜单场景）都加登记组件，属装配轨道范围（审计 F-5 亦建议保留）。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Audio/AudioService.cs",
                Token = "Camera.main",
                LineAtRegistration = 819,
                Kind = Kind.Runtime,
                Reason = "**节流点解析**（Update 内按 AmbientUpdateIntervalSeconds 节流重算，非逐帧）："
                         + "按镜头水平距离重算环境底床衰减，镜头距离是慢变量；拿不到主相机（无渲染路径的测试场景）按满增益处理。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Battle/BattleCameraDriver.cs",
                Token = "FindObjectOfType<BattleInteractionController>",
                LineAtRegistration = 386,
                Kind = Kind.Runtime,
                Reason = "**一次性装配兜底**（Awake 里跑一次，HotPath 的 Update/LateUpdate 轮询已彻底删除）："
                         + "场景是装配脚本的产物，未跑 BattleLookupWiring.Wire 的旧场景该字段为空。"
                         + "兜底把\"静默失效\"降级成\"可用但吵闹\"，而真正的装配缺陷由 "
                         + "InteractionWiredByAssembly（PlayMode 测试断言）失败暴露——注入是正路，兜底只是防静默。",
            },
            new WhitelistEntry
            {
                Path = "UI/BattleHud.cs",
                Token = "FindObjectOfType<BattleInteractionController>",
                LineAtRegistration = 202,
                Kind = Kind.Runtime,
                Reason = "**一次性装配兜底**（Awake 里跑一次）：场景是装配脚本的产物，未跑 "
                         + "BattleLookupWiring.Wire 的旧场景该字段为空；兜底把\"静默失效\"降级成\"可用但吵闹\"，"
                         + "装配完整性由 InteractionWiredByAssembly（PlayMode 测试断言）钉住。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Battle/BattleController.cs",
                Token = "Camera.main",
                LineAtRegistration = 329,
                Kind = Kind.Runtime,
                Reason = "**一次性装配兜底**（世界装配期跑一次）：远裁剪面的唯一写入者契约走 BattleCameraDriver "
                         + "的例外口抬高，无 Driver 的兜底场景才直取主相机抬一次 farClipPlane。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Battle/BattleInteractionController.cs",
                Token = "Camera.main",
                LineAtRegistration = 88,
                Kind = Kind.Runtime,
                Reason = "**一次性装配兜底**（Awake 里跑一次）：battleCamera 注入优先，未接线的场景回落主相机，"
                         + "解析一次即缓存进字段。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Battle/SelectionCube.cs",
                Token = "Camera.main",
                LineAtRegistration = 96,
                Kind = Kind.Runtime,
                Reason = "**缓存解析**（Awake 一次；Update 里仅缓存被销毁时重取，同 Token 两处由本登记放行）："
                         + "全队每帧都进 Update，帧帧打 Camera.main 不值当——已按一次性解析 + 失效重取收敛。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Battle/TrajectoryPreview.cs",
                Token = "Camera.main",
                LineAtRegistration = 184,
                Kind = Kind.Runtime,
                Reason = "**缓存解析**（Awake 一次；ResolveCamera 仅缓存失效时重取，同 Token 两处由本登记放行）："
                         + "Show/LateUpdate 在瞄准期逐帧走，已按一次性解析 + 失效重取收敛；主相机被销毁重建由空判自然重取。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Ambient/AmbientDirector.cs",
                Token = "Camera.main",
                LineAtRegistration = 219,
                Kind = Kind.Runtime,
                Reason = "**一次性装配回落**（Build 里 _built 守卫只跑一次）：targetCamera 注入优先，"
                         + "没接线才回落主相机并缓存进字段。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Water/OceanRig.cs",
                Token = "Camera.main",
                LineAtRegistration = 341,
                Kind = Kind.Runtime,
                Reason = "**缓存解析**（无 _followTarget 接线时 LateUpdate 的兜底路）：解析成功即缓存复用，"
                         + "相机销毁/换场景由 fake-null 判空自动重解析；解析失败（编辑器装配期）保持每帧重试语义。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/SceneArt/NearClipHider.cs",
                Token = "Camera.main",
                LineAtRegistration = 72,
                Kind = Kind.Runtime,
                Reason = "**待缓存优化（审计 A1）**：LateUpdate 逐帧裸打 Camera.main（本类逐帧要判近裁剪遮挡），"
                         + "应照 SelectionCube/TrajectoryPreview 的缓存解析形态改（Awake 一次 + 失效重取）；"
                         + "先登记保闸门可用，清账归手感/渲染轨道。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/WaterLab/WaterLabSimulation.cs",
                Token = "FindObjectsByType<Camera>",
                LineAtRegistration = 223,
                Kind = Kind.Runtime,
                Reason = "**诊断转储路径**（PC3D_WATERLAB_CAM_DUMP=1 环境变量门控，两个检查点各一次）："
                         + "点名场景全部相机供无头出图链判\"拍的是哪台\"，被点名对象没有持有者可以注入。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/WaterLab/WaterLabSimulation.cs",
                Token = "FindObjectsByType<Renderer>",
                LineAtRegistration = 234,
                Kind = Kind.Runtime,
                Reason = "**诊断转储路径**（同上环境变量门控）：读全场景 WaterLab 材质的运行期真值"
                         + "（shader 名/吸收/水色/折射/顶点数）验材质属性是否真进了 shader。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/WaterLab/WaterLabSimulation.cs",
                Token = "Camera.main",
                LineAtRegistration = 219,
                Kind = Kind.Runtime,
                Reason = "**诊断转储与短路回落**（同 Token 两处由本登记放行）：转储点（环境变量门控）打印 "
                         + "Camera.main 指向哪台；白水视锥剔除的相机取值是 renderCamera（装配器接线）优先的 "
                         + "?: 短路回落，接线正常时 Camera.main 分支不求值。",
            },
            new WhitelistEntry
            {
                Path = "UI/PixelScaleService.cs",
                Token = "FindObjectsByType<CanvasScaler>",
                LineAtRegistration = 101,
                Kind = Kind.Runtime,
                Reason = "**冷路径**（像素档切换与 sceneLoaded 重放时各跑一次，非逐帧）：本服务是静态全局，"
                         + "管的是场景里全部 CanvasScaler 的档位统一，画布数量与归属不固定，没有持有者可以逐个注入。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/SceneArtBake/Lowpoly/LowpolyStageBuilder.cs",
                Token = "transform.Find",
                LineAtRegistration = 111,
                Kind = Kind.Runtime,
                Reason = "**构建期一次性**（BuildCloudField 里 FreshChild 自建结构内找 Colliders 槽位，"
                         + "幂等重入时复用已有节点）：找的是本方法刚构建的子树，层级名是构建器自己的契约。",
            },
            new WhitelistEntry
            {
                Path = "UI/Skin/UiKit.cs",
                Token = "transform.Find",
                LineAtRegistration = 426,
                Kind = Kind.Runtime,
                Reason = "**装配期一次性**：窗口按钮按 Aseprite 组件树固定名（Icon）挂图标，"
                         + "按钮是本方法刚建出来的，层级名由 AseWidgets 导出结构钉死。",
            },
            new WhitelistEntry
            {
                Path = "UI/Stick/Controls/AseButtonBase.cs",
                Token = "transform.Find",
                LineAtRegistration = 44,
                Kind = Kind.Runtime,
                Reason = "**按名兜底 + 惰性缓存**（Label 属性，命中后不再找）：场景重载后序列化字段落空时按 "
                         + "Aseprite 固定名重取；不能用泛搜——影子层占兄弟序 0，泛搜会取到不可见层（类注释有实拍案例）。",
            },
            new WhitelistEntry
            {
                Path = "UI/Stick/Controls/SketchButton.cs",
                Token = "transform.Find",
                LineAtRegistration = 60,
                Kind = Kind.Runtime,
                Reason = "**按名兜底 + 惰性缓存**（ShadowLabel 属性）：同 AseButtonBase.Label——"
                         + "影子层按名重取，泛搜必错。",
            },
            new WhitelistEntry
            {
                Path = "UI/Stick/Controls/SketchCheck.cs",
                Token = "transform.Find",
                LineAtRegistration = 47,
                Kind = Kind.Runtime,
                Reason = "**按名兜底 + 惰性缓存**（Icon 属性）：同 AseButtonBase.Label——场景重载后按 "
                         + "Aseprite 固定名重取。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Rendering/Pixelart/PixelartContentConverter.cs",
                Token = "FindObjectsByType<MeshRenderer>",
                LineAtRegistration = 130,
                Kind = Kind.Runtime,
                Reason = "有界补扫（rescanIntervalSeconds=1s 起隔、rescanDurationSeconds=30s 总上限，跑完即停）："
                         + "世界内容不是一次到齐的（海图开局合成、FX/道具更晚），要转换的是场景里任意"
                         + "新出现的 MeshRenderer，没有持有者可以逐个注入。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/CharCamDebug/CharCamDebugController.cs",
                Token = "FindObjectsByType<MeshRenderer>",
                LineAtRegistration = 226,
                Kind = Kind.Runtime,
                Reason = "调试组件的一次性初始化（Start 里缓存一次场景 renderer 清单，渲染路径切换时靠它"
                         + "保存/还原各件原材质）：调试器挂在相机上、管的却是全场景材质，清单对象没有持有者可以注入。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/Rendering/Pixelart/PixelartCameraRig.cs",
                Token = "FindObjectsByType<MeshRenderer>",
                LineAtRegistration = 695,
                Kind = Kind.Runtime,
                Reason = "装配自检（LogSelfCheckOnce 守卫只跑一次）：这条路径的失效方式是静默的（shader 被剥离 /"
                         + "渲染器索引没接上 → 画面只剩背景色而不报错），首帧数一遍走本路径的 renderer 把证据写进日志。",
            },

            // ---------------- 工具/演示路径 ----------------

            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/PlayerArtCapture.cs",
                Token = "FindObjectsByType<MeshRenderer>",
                LineAtRegistration = 303,
                Kind = Kind.Tool,
                Reason = "出图工具路径（-artReviewOut 命令行模式）：按包围盒挑要出镜的网格，"
                         + "被挑对象没有持有者可以注入。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/PlayerArtCapture.cs",
                Token = "FindObjectsOfType<MeshRenderer>",
                LineAtRegistration = 574,
                Kind = Kind.Tool,
                Reason = "出图工具路径（-artReviewOut 命令行模式）：要在任意手工场景里量天际线，"
                         + "被测量对象没有持有者可以注入；改成显式引用会让命令行工具模式复杂化。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/PlayerArtCapture.cs",
                Token = "Camera.main",
                LineAtRegistration = 438,
                Kind = Kind.Tool,
                Reason = "出图工具路径（-artReviewOut 命令行模式，同 Token 两处由本登记放行）：切场景后等 rig "
                         + "就绪再取主相机做接管校验、评审相机接管时禁用原主相机。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/OrbitRunner.cs",
                Token = "FindObjectOfType<BattleCameraDriver>",
                LineAtRegistration = 35,
                Kind = Kind.Tool,
                Reason = "离线环绕出图工具路径（原 ChemPlantOrbitCapture 的嵌套载体提为顶级）：接管任意跑起来的"
                         + "战斗场景的跟随相机，拍完自动退出，接管对象没有持有者可以注入。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/OrbitRunner.cs",
                Token = "GameObject.Find",
                LineAtRegistration = 84,
                Kind = Kind.Tool,
                Reason = "离线环绕出图工具路径：按名字找关卡陈设件实例算环绕中心（候选名写死在工具里），"
                         + "陈设件由运行时装配器实例化，工具侧拿不到显式引用。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/OrbitRunner.cs",
                Token = "FindObjectOfType<BattleTerrainView>",
                LineAtRegistration = 104,
                Kind = Kind.Tool,
                Reason = "离线环绕出图工具路径：陈设件缺失时退用地形碰撞盒算环绕中心的兜底。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/OrbitRunner.cs",
                Token = "Camera.main",
                LineAtRegistration = 44,
                Kind = Kind.Tool,
                Reason = "离线环绕出图工具路径：接管场景主相机摆机位出图（拍完 Application.Quit）。运行时零调用。",
            },
            new WhitelistEntry
            {
                Path = "PirateCrew/ArtReview/MinimapTopViewCapture.cs",
                Token = "FindObjectsOfType<Renderer>",
                LineAtRegistration = 205,
                Kind = Kind.Tool,
                Reason = "俯视小地图出图工具路径：采集前把全场景 renderer 换成平色材质（进程即弃），"
                         + "被换材质的对象没有持有者可以注入。运行时零调用。",
            },
        };

        /// <summary>工具/演示路径目录前缀（这些目录里的命中按 <see cref="Kind.Tool"/> 归类）。</summary>
        static readonly string[] ToolPathPrefixes =
        {
            "PirateCrew/ArtReview/",
            "PirateCrew/SceneKitPilot/",
        };

        [MenuItem("PirateCrew/审计/运行期查找清退报告")]
        public static void Audit()
        {
            Run(writeReport: true, exitWhenDone: false);
        }

    /// <summary>无头入口（CI / 主控）：写报告 + 有违规时退出码 1。</summary>
    public static void AuditFromCommandLine()
    {
        Run(writeReport: true, exitWhenDone: Application.isBatchMode);
    }

    /// <summary>
    /// 一次扫描的机器可读结果（<see cref="AuditOnce"/> 返回）。
    /// 只装 string/int——测试程序集不引用本程序集，经反射按名字读字段，别放复杂类型。
    /// </summary>
    public sealed class AuditOutcome
    {
        /// <summary>真实调用点总数（不含注释/字符串提及）。</summary>
        public int TotalHits;

        /// <summary>其中被白名单放行的。</summary>
        public int AllowedHits;

        /// <summary>注释/字符串提及数（不是调用点）。</summary>
        public int SkippedCount;

        /// <summary>未登记命中（"路径:行 → 调用片段"），非空即闸门失败。</summary>
        public readonly List<string> Unregistered = new List<string>();

        /// <summary>白名单僵尸（"路径 · 调用片段"），非空说明登记滞后于清退。</summary>
        public readonly List<string> Zombies = new List<string>();
    }

    /// <summary>
    /// 可编程入口：跑一次扫描并返回结果，不写报告、不退进程。EditMode 闸门测试经反射调它
    /// （测试程序集不引用 Assembly-CSharp-Editor，反射惯例同 LevelAssetGateTests 调校验器）。
    /// </summary>
    public static AuditOutcome AuditOnce()
    {
        ScanAll(out List<Hit> hits, out List<Hit> skipped, out List<Hit> violations, out List<WhitelistEntry> zombies);

        var outcome = new AuditOutcome
        {
            TotalHits = hits.Count,
            AllowedHits = hits.Count - violations.Count,
            SkippedCount = skipped.Count,
        };
        for (int i = 0; i < violations.Count; i++)
            outcome.Unregistered.Add(violations[i].RepoRelativePath + ":" + violations[i].Line
                                     + " → " + violations[i].Token);
        for (int i = 0; i < zombies.Count; i++)
            outcome.Zombies.Add(zombies[i].Path + " · " + zombies[i].Token);

        return outcome;
    }

    // ------------------------------------------------------------------
    // 主流程
    // ------------------------------------------------------------------

    struct Hit
    {
        public string RepoRelativePath;
        public string ScriptsRelativePath;
        public int Line;
        public string Text;
        public string Token;
        public Kind Kind;
        public string Note;      // 分类说明 / 白名单理由
        public bool Allowed;
    }

    /// <summary>
    /// 扫描核心：<c>Assets/Scripts/**</c> 逐文件正则扫描 → 白名单比对 → 违规与僵尸清单。
    /// 源码目录缺失时返回 <c>false</c>（结果为空），调用方各自决定怎么失败。
    /// </summary>
    static bool ScanAll(out List<Hit> hits, out List<Hit> skipped, out List<Hit> violations,
        out List<WhitelistEntry> zombies)
    {
        hits = new List<Hit>();
        skipped = new List<Hit>();           // 注释/字符串里的提及（不是调用点）
        violations = new List<Hit>();
        zombies = new List<WhitelistEntry>();

        string scriptsDir = Path.Combine(UnityProjectRoot(), ScriptsRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(scriptsDir))
        {
            Debug.LogError("[SceneLookupAudit] 找不到源码目录: " + scriptsDir);
            return false;
        }

        string projectRoot = ProjectRoot();
        string[] files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);

        for (int i = 0; i < files.Length; i++)
            ScanFile(projectRoot, files[i], hits, skipped);

        for (int i = 0; i < hits.Count; i++)
        {
            if (!hits[i].Allowed)
                violations.Add(hits[i]);
        }

        // 白名单僵尸项：登记了却扫不到（清退后又没删）——提示维护，不算失败。
        for (int i = 0; i < Whitelist.Length; i++)
        {
            bool found = false;
            for (int h = 0; h < hits.Count; h++)
            {
                if (hits[h].ScriptsRelativePath == Whitelist[i].Path && hits[h].Token == Whitelist[i].Token)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                zombies.Add(Whitelist[i]);
        }

        return true;
    }

    static void Run(bool writeReport, bool exitWhenDone)
    {
        bool scanned = ScanAll(out List<Hit> hits, out List<Hit> skippedText, out List<Hit> violations,
            out List<WhitelistEntry> zombies);
        if (!scanned)
        {
            if (exitWhenDone)
                EditorApplication.Exit(1);
            return;
        }

        string projectRoot = ProjectRoot();
        string report = BuildReport(projectRoot, hits, skippedText, violations, zombies);

            if (writeReport)
            {
                string outPath = ResolveReportPath(projectRoot);
                try
                {
                    string dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(outPath, report, new UTF8Encoding(false));
                    Debug.Log("[SceneLookupAudit] 报告已写入: " + outPath);
                }
                catch (Exception e)
                {
                    Debug.LogError("[SceneLookupAudit] 写报告失败: " + e.Message);
                }
            }

            Debug.Log("[SceneLookupAudit] 调用点 " + hits.Count + " 处（白名单 "
                      + (hits.Count - violations.Count) + " / 违规 " + violations.Count + "）；"
                      + "注释与字符串提及 " + skippedText.Count + " 处。基线 " + Baseline.Length + " 处。");

            for (int i = 0; i < violations.Count; i++)
            {
                Debug.LogError("[SceneLookupAudit] 未登记的运行期查找: " + violations[i].RepoRelativePath + ":"
                               + violations[i].Line + " → " + violations[i].Text.Trim()
                               + "。请改成显式注入，或在 SceneLookupAudit.Whitelist 里带理由登记。");
            }

            for (int i = 0; i < zombies.Count; i++)
                Debug.LogWarning("[SceneLookupAudit] 白名单僵尸项（已扫不到，建议删除登记）: " + zombies[i]);

            if (exitWhenDone)
                EditorApplication.Exit(violations.Count > 0 ? 1 : 0);
        }

        static void ScanFile(string projectRoot, string fullPath, List<Hit> hits, List<Hit> skipped)
        {
            string scriptsRoot = Path.Combine(UnityProjectRoot(), ScriptsRoot);
            string repoRel = RelPath(projectRoot, fullPath);
            string pathInScripts = RelPath(scriptsRoot, fullPath);

            string[] lines;
            try
            {
                lines = File.ReadAllLines(fullPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SceneLookupAudit] 读文件失败 " + fullPath + ": " + e.Message);
                return;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int searchFrom = 0;

                while (searchFrom < line.Length)
                {
                    Match m = LookupPattern.Match(line, searchFrom);
                    if (!m.Success)
                        break;

                    searchFrom = m.Index + m.Length;

                    var hit = new Hit
                    {
                        RepoRelativePath = repoRel,
                        ScriptsRelativePath = pathInScripts,
                        Line = i + 1,
                        Text = line,
                        Token = TokenAt(line, m),
                        Kind = ClassifyPath(pathInScripts),
                    };

                    if (IsComment(line, m.Index) || IsInsideStringLiteral(line, m.Index))
                    {
                        hit.Note = IsComment(line, m.Index) ? "注释文本（不是调用点）" : "字符串/属性文本（文档提及）";
                        skipped.Add(hit);
                        continue;
                    }

                    WhitelistEntry entry = FindWhitelist(pathInScripts, hit.Token);
                    if (entry.Reason != null)
                    {
                        hit.Allowed = true;
                        hit.Kind = entry.Kind;
                        hit.Note = entry.Reason;
                    }
                    else
                    {
                        hit.Note = hit.Kind == Kind.Tool
                            ? "工具/演示路径未登记（请补白名单理由）"
                            : "运行时路径未登记（应改成显式注入）";
                    }

                    hits.Add(hit);
                }
            }
        }

        /// <summary>取命中点所在的调用片段（如 <c>FindObjectOfType&lt;AudioListener&gt;</c>），作为白名单的键。</summary>
        static string TokenAt(string line, Match m)
        {
            int start = m.Index;
            int end = m.Index + m.Length;

            // 泛型参数一起取（<...>），没有泛型括号就取到分隔符为止。
            if (end < line.Length && line[end] == '<')
            {
                int depth = 0;
                int i = end;
                for (; i < line.Length; i++)
                {
                    if (line[i] == '<')
                        depth++;
                    else if (line[i] == '>')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            i++;
                            break;
                        }
                    }
                }

                end = i;
            }

            string raw = line.Substring(start, end - start);
            return Regex.Replace(raw, @"\s+", string.Empty);
        }

        static bool IsComment(string line, int index)
        {
            int slash = line.IndexOf("//", StringComparison.Ordinal);
            if (slash >= 0 && slash < index)
                return true;

            string trimmed = line.TrimStart();
            return trimmed.StartsWith("*", StringComparison.Ordinal)
                   || trimmed.StartsWith("/*", StringComparison.Ordinal);
        }

        /// <summary>命中点是否落在字符串字面量里（引号计数为奇数 = 在串内）。</summary>
        static bool IsInsideStringLiteral(string line, int index)
        {
            bool inString = false;
            for (int i = 0; i < index && i < line.Length; i++)
            {
                if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                    inString = !inString;
            }

            return inString;
        }

        static Kind ClassifyPath(string pathInScripts)
        {
            for (int i = 0; i < ToolPathPrefixes.Length; i++)
            {
                if (pathInScripts.StartsWith(ToolPathPrefixes[i], StringComparison.Ordinal))
                    return Kind.Tool;
            }

            return Kind.Runtime;
        }

        static WhitelistEntry FindWhitelist(string pathInScripts, string token)
        {
            for (int i = 0; i < Whitelist.Length; i++)
            {
                if (Whitelist[i].Path == pathInScripts && Whitelist[i].Token == token)
                    return Whitelist[i];
            }

            return default;
        }

        // ------------------------------------------------------------------
        // 报告
        // ------------------------------------------------------------------

        static string BuildReport(string projectRoot, List<Hit> hits, List<Hit> skipped,
            List<Hit> violations, List<WhitelistEntry> zombies)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 运行期查找清退报告");
            sb.AppendLine();
            sb.AppendLine("> **这是什么**：`Assets/Scripts/**` 下 `GameObject.Find` / `FindObjectOfType` / "
                          + "`FindObjectsOfType` / `FindWithTag`（含新 API 逃逸写法 `FindAnyObjectByType` / `FindObjectsByType`）"
                          + "以及 `Camera.main` / `transform.Find`（属性访问 / 层级按名查找形态）存量的**逐条登记表**与防回归闸门说明。");
            sb.AppendLine("> 由 `Assets/Editor/SceneLookupAudit.cs` 扫描生成（唯一真源是脚本里的白名单表），"
                          + "每次改动后重跑即刷新。");
            sb.AppendLine("> 相关：场景装配债诊断见 [场景接线审计报告](场景接线审计报告.md)（F-3/F-4/F-5）。");
            sb.AppendLine();
            sb.AppendLine("## 怎么复跑");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine("U=\"F:/Unity/2022.3.62f1/Editor/Unity.exe\"");
            sb.AppendLine("P=\"F:/VSCode/pirate-crew-3d-unity/pirate-crew\"");
            sb.AppendLine();
            sb.AppendLine("# 扫描 + 写本报告；有未登记命中则退出码 1（CI 用）");
            sb.AppendLine("\"$U\" -batchmode -nographics -quit -projectPath \"$P\" \\");
            sb.AppendLine("  -executeMethod PirateCrew.EditorTools.SceneLookupAudit.AuditFromCommandLine -logFile -");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("编辑器菜单同义入口：`PirateCrew/审计/运行期查找清退报告`。");
            sb.AppendLine();

            int allowed = hits.Count - violations.Count;
            sb.AppendLine("## 一、汇总");
            sb.AppendLine();
            sb.AppendLine("| 指标 | 数值 |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine("| 清退前基线（2026-09-21 实测调用点） | " + Baseline.Length + " |");
            sb.AppendLine("| 当前调用点 | " + hits.Count + " |");
            sb.AppendLine("| 其中已登记（运行时/工具） | " + allowed + " |");
            sb.AppendLine("| **未登记（闸门失败）** | **" + violations.Count + "** |");
            sb.AppendLine("| 注释与字符串提及（不是调用点） | " + skipped.Count + " |");
            sb.AppendLine("| 白名单僵尸项 | " + zombies.Count + " |");
            sb.AppendLine();

            sb.AppendLine("## 二、前后对比（逐条）");
            sb.AppendLine();
            sb.AppendLine("| 清退前（file:line · 调用） | 处置 | 现在 |");
            sb.AppendLine("| --- | --- | --- |");
            for (int i = 0; i < Baseline.Length; i++)
            {
                sb.AppendLine("| `" + Baseline[i] + "` | " + DispositionOf(Baseline[i]) + " | " + NowOf(Baseline[i], hits) + " |");
            }

            sb.AppendLine();
            sb.AppendLine("## 三、当前调用点逐条");
            sb.AppendLine();
            if (hits.Count == 0)
            {
                sb.AppendLine("（无）");
            }
            else
            {
                sb.AppendLine("| # | 位置 | 分类 | 调用 | 允许 | 理由 / 待办 |");
                sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
                for (int i = 0; i < hits.Count; i++)
                {
                    Hit h = hits[i];
                    sb.AppendLine("| " + (i + 1) + " | `" + h.RepoRelativePath + ":" + h.Line + "` | "
                                  + KindName(h.Kind) + " | `" + h.Token + "` | "
                                  + (h.Allowed ? "是（白名单）" : "**否**") + " | " + OneLine(h.Note) + " |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## 四、白名单登记表（脚本内 `SceneLookupAudit.Whitelist`，键 = 路径 + 调用片段）");
            sb.AppendLine();
            sb.AppendLine("| 文件 | 登记时行号 | 调用 | 分类 | 理由 |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            for (int i = 0; i < Whitelist.Length; i++)
            {
                WhitelistEntry w = Whitelist[i];
                sb.AppendLine("| `" + w.Path + "` | " + w.LineAtRegistration + " | `" + w.Token + "` | "
                              + KindName(w.Kind) + " | " + OneLine(w.Reason) + " |");
            }

            if (zombies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### 僵尸项（登记了但已扫不到，建议删除登记）");
                sb.AppendLine();
                for (int i = 0; i < zombies.Count; i++)
                    sb.AppendLine("- " + zombies[i]);
            }

            sb.AppendLine();
            sb.AppendLine("## 五、注释与字符串里的提及（不是调用点，列出以正视听）");
            sb.AppendLine();
            sb.AppendLine("这些行只是**文字提到**了查找 API（多为「本类不做 GameObject.Find」这类声明式注释、"
                          + "或 Tooltip 文案）。扫描器跳过它们，计数不计入调用点——避免「文档写了禁令反而被判违规」。");
            sb.AppendLine();
            if (skipped.Count == 0)
            {
                sb.AppendLine("（无）");
            }
            else
            {
                sb.AppendLine("| # | 位置 | 文本性质 |");
                sb.AppendLine("| --- | --- | --- |");
                for (int i = 0; i < skipped.Count; i++)
                {
                    sb.AppendLine("| " + (i + 1) + " | `" + skipped[i].RepoRelativePath + ":" + skipped[i].Line
                                  + "` | " + skipped[i].Note + " |");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## 六、闸门语义");
            sb.AppendLine();
            sb.AppendLine("- **存量可见、新增禁止**：只有 `Whitelist` 里带理由的命中被允许；其余一律非 0 退出。");
            sb.AppendLine("- **键是 file + 调用片段，不是行号**：行号会随无关改动漂移，用它做键会让闸门变成噪音源；"
                          + "登记时的行号只作文档（本报告第三节打印扫描时的实时行号）。");
            sb.AppendLine("- **逃逸写法也在扫描范围**：`FindAnyObjectByType` / `FindObjectsByType`（新 API）同样命中——"
                          + "否则「把旧 API 换成新 API」就能绕过闸门。");
            sb.AppendLine("- **非方法调用的查找形态也在扫描范围**：`Camera.main` 是属性访问（按 Tag 全场扫主相机，"
                          + "成本与 FindObjectOfType 同级），`transform.Find` 是层级内按名查找——正则带左侧断言，"
                          + "不误吞 `MyCamera.main` / `myTransform.Find` 这类自定义成员名，"
                          + "`UnityEngine.Camera.main` 全限定形态照常命中。");
            sb.AppendLine("- **扫描域是 `Assets/Scripts/`**：`Assets/Editor/` 下的装配/审计脚本（含本文件白名单表）不扫，"
                          + "它们按设计要按名字/按类型在编辑器里找对象。");
            sb.AppendLine();

            return sb.ToString();
        }

        /// <summary>基线条目的处置结论（写死在脚本里：基线是一次性快照，不随扫描变化）。</summary>
        static string DispositionOf(string baseline)
        {
            if (baseline.StartsWith("PirateCrew/Battle/BattleCameraDriver.cs", StringComparison.Ordinal))
                return "清退：改为 `[SerializeField] aimThrow`（装配注入）+ Awake 一次性兜底（不在热路径）";
            if (baseline.StartsWith("UI/BattleHud.cs", StringComparison.Ordinal))
                return "清退：改为 `[SerializeField] cameraController`（装配注入）+ Awake 一次性兜底";
            if (baseline.StartsWith("PirateCrew/Water/WaterSimulationDriver.cs", StringComparison.Ordinal))
                return "清退：改为 `[SerializeField] sunLight`（装配注入，来源 AmbientDirector.sunLight）";
            if (baseline.StartsWith("PirateCrew/Audio/AudioService.cs", StringComparison.Ordinal))
                return "保留并登记：能力探测（非依赖注入），限频 + 负缓存，审计 F-5 建议保留";
            if (baseline.StartsWith("PirateCrew/ArtReview/PlayerArtCapture.cs", StringComparison.Ordinal))
                return "保留并登记：出图工具路径（命令行模式），运行时零调用";
            if (baseline.StartsWith("PirateCrew/Battle/BattleController.cs", StringComparison.Ordinal))
                return "**不属本轨道**（BattleController.cs 归数据外化轨道）：待其接线 AmbientDirector"; 
            return "（未登记处置）";
        }

        static string NowOf(string baseline, List<Hit> hits)
        {
            // 基线形如 path:line · Token；先按"同一行"比对，再退回"同文件同调用形态"。
            int dot = baseline.IndexOf(" · ", StringComparison.Ordinal);
            if (dot < 0)
                return "（无法比对）";

            string left = baseline.Substring(0, dot);
            string tokenKind = baseline.Substring(dot + 3);
            int colon = left.LastIndexOf(':');
            string path = colon > 0 ? left.Substring(0, colon) : left;
            int baselineLine = 0;
            if (colon > 0)
                int.TryParse(left.Substring(colon + 1), out baselineLine);

            int sameLine = 0;
            int sameToken = 0;
            int firstTokenLine = 0;
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].RepoRelativePath != path || !hits[i].Token.StartsWith(tokenKind, StringComparison.Ordinal))
                    continue;

                sameToken++;
                if (firstTokenLine == 0)
                    firstTokenLine = hits[i].Line;
                if (hits[i].Line == baselineLine)
                    sameLine++;
            }

            if (sameLine > 0)
                return "**仍在** `" + path + ":" + baselineLine + "`（白名单）";

            if (sameToken > 0)
                return "该处已清退；同文件另有 " + sameToken + " 处（`:"
                       + firstTokenLine + "`，白名单）";

            return "已清退（无命中）";
        }

        static string KindName(Kind kind)
        {
            return kind == Kind.Tool ? "工具路径" : "运行时路径";
        }

        static string OneLine(string text)
        {
            return text == null ? "" : Regex.Replace(text, @"\s+", " ").Trim();
        }

        // ------------------------------------------------------------------
        // 路径
        // ------------------------------------------------------------------

        /// <summary>仓库根（Unity 工程目录的上一级）；报告落在仓库根的 docs/ 下。</summary>
        static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }

        /// <summary>
        /// Unity 工程根（<c>pirate-crew/</c>）。本仓库是两层结构——仓库根放文档与 AGENTS.md，
        /// Unity 工程在 <c>pirate-crew/</c> 子目录（见 AGENTS.md「Unity 模块化架构原则」§1）。
        /// **源码要在工程根下找，报告要写到仓库根下**，两者不是同一个目录，混用会直接找不到源码。
        /// </summary>
        static string UnityProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        static string ResolveReportPath(string projectRoot)
        {
            string[] argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length - 1; i++)
            {
                if (argv[i] == OutputArg && !string.IsNullOrEmpty(argv[i + 1]))
                    return Path.GetFullPath(argv[i + 1]);
            }

            return Path.Combine(projectRoot, ReportPathFallback.Replace('/', Path.DirectorySeparatorChar));
        }

        static string RelPath(string root, string fullPath)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(fullPath);
            if (full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                full = full.Substring(normalizedRoot.Length);

            return full.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
